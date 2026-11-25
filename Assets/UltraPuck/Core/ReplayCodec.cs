using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace UltraPuck.Core
{
    /// <summary>
    /// Game states as bytes for saved match replays. Unlike the network's <see cref="Net.StateCodec"/> (both PCs run
    /// the same build), a replay is read back by later versions of the game, so the bytes come with a description of
    /// every type in them (field names and types). Reading matches fields up by name: a field added since is left at its
    /// starting value, one that's gone is read and dropped. Not used by the rules, so it may use reflection freely.
    /// </summary>
    public sealed class ReplayCodec
    {
        private enum Kind : byte { Int32, UInt32, Int64, UInt64, Bool, Byte, SByte, Int16, String, Enum, Array, Object }

        private sealed class SavedType
        {
            public string Name;
            public Kind Kind;
            public int Element;           // arrays: the element's type id
            public bool IsClass;          // objects: can be null
            public string[] FieldNames;   // objects
            public int[] FieldTypes;
            // reading: the type in this build it matches (null: nothing does) and the fields to fill
            public Type Now;
            public FieldInfo[] NowFields;
        }

        private readonly List<SavedType> _types = new List<SavedType>();
        private readonly Dictionary<Type, int> _ids = new Dictionary<Type, int>();

        /// <summary>A codec for writing states of this build (describes <see cref="GameState"/> and everything in it).</summary>
        public static ReplayCodec ForWriting()
        {
            var c = new ReplayCodec();
            c.IdOf(typeof(GameState));
            return c;
        }

        private ReplayCodec() { }

        // ------------------------------------------------------------------ describing types

        private static FieldInfo[] FieldsOf(Type t)
        {
            var list = new List<FieldInfo>();
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (!f.IsLiteral && !f.IsNotSerialized) list.Add(f);
            list.Sort((a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
            return list.ToArray();
        }

        private static Kind KindOf(Type t)
        {
            if (t == typeof(int)) return Kind.Int32;
            if (t == typeof(uint)) return Kind.UInt32;
            if (t == typeof(long)) return Kind.Int64;
            if (t == typeof(ulong)) return Kind.UInt64;
            if (t == typeof(bool)) return Kind.Bool;
            if (t == typeof(byte)) return Kind.Byte;
            if (t == typeof(sbyte)) return Kind.SByte;
            if (t == typeof(short)) return Kind.Int16;
            if (t == typeof(string)) return Kind.String;
            if (t.IsEnum) return Kind.Enum;
            if (t.IsArray) return Kind.Array;
            return Kind.Object;
        }

        private int IdOf(Type t)
        {
            int id;
            if (_ids.TryGetValue(t, out id)) return id;
            var s = new SavedType { Name = t.FullName, Kind = KindOf(t), Now = t };
            id = _types.Count;
            _types.Add(s);
            _ids[t] = id;
            if (s.Kind == Kind.Array) s.Element = IdOf(t.GetElementType());
            else if (s.Kind == Kind.Object)
            {
                s.IsClass = !t.IsValueType;
                var fs = FieldsOf(t);
                s.NowFields = fs;
                s.FieldNames = new string[fs.Length];
                s.FieldTypes = new int[fs.Length];
                for (var k = 0; k < fs.Length; k++) { s.FieldNames[k] = fs[k].Name; s.FieldTypes[k] = IdOf(fs[k].FieldType); }
            }
            return id;
        }

        /// <summary>The description of the types, to store once at the top of a replay.</summary>
        public void WriteSchema(BinaryWriter w)
        {
            w.Write(_types.Count);
            foreach (var s in _types)
            {
                w.Write(s.Name ?? "");
                w.Write((byte)s.Kind);
                if (s.Kind == Kind.Array) w.Write(s.Element);
                else if (s.Kind == Kind.Object)
                {
                    w.Write(s.IsClass);
                    w.Write(s.FieldNames.Length);
                    for (var k = 0; k < s.FieldNames.Length; k++) { w.Write(s.FieldNames[k]); w.Write(s.FieldTypes[k]); }
                }
            }
        }

        /// <summary>A codec for reading states written with the description in <paramref name="r"/> (by any version).</summary>
        public static ReplayCodec ReadSchema(BinaryReader r)
        {
            var c = new ReplayCodec();
            var n = r.ReadInt32();
            if (n < 1 || n > 10000) throw new InvalidDataException("replay: bad type list");
            for (var k = 0; k < n; k++)
            {
                var s = new SavedType { Name = r.ReadString(), Kind = (Kind)r.ReadByte() };
                if (s.Kind == Kind.Array) s.Element = r.ReadInt32();
                else if (s.Kind == Kind.Object)
                {
                    s.IsClass = r.ReadBoolean();
                    var m = r.ReadInt32();
                    if (m < 0 || m > 10000) throw new InvalidDataException("replay: bad field list");
                    s.FieldNames = new string[m];
                    s.FieldTypes = new int[m];
                    for (var f = 0; f < m; f++) { s.FieldNames[f] = r.ReadString(); s.FieldTypes[f] = r.ReadInt32(); }
                }
                c._types.Add(s);
            }
            foreach (var s in c._types)
            {
                if (s.Kind == Kind.Array && (s.Element < 0 || s.Element >= n)) throw new InvalidDataException("replay: bad array type");
                if (s.FieldTypes != null) foreach (var f in s.FieldTypes) if (f < 0 || f >= n) throw new InvalidDataException("replay: bad field type");
            }
            // match the saved types to this build's, starting from the state itself (type 0)
            c.Match(0, typeof(GameState));
            return c;
        }

        /// <summary>Saved type <paramref name="id"/> is read into <paramref name="t"/> (null: read and dropped).</summary>
        private void Match(int id, Type t)
        {
            var s = _types[id];
            if (s.Now != null || t == null) return;
            if (s.Kind == Kind.Object)
            {
                if (KindOf(t) != Kind.Object || (t.IsValueType == s.IsClass)) return;
                s.Now = t;
                var now = FieldsOf(t);
                s.NowFields = new FieldInfo[s.FieldNames.Length];
                for (var k = 0; k < s.FieldNames.Length; k++)
                {
                    foreach (var f in now)
                        if (f.Name == s.FieldNames[k] && Fits(s.FieldTypes[k], f.FieldType)) { s.NowFields[k] = f; break; }
                    if (s.NowFields[k] != null) Match(s.FieldTypes[k], s.NowFields[k].FieldType);
                }
            }
            else if (s.Kind == Kind.Array)
            {
                if (!t.IsArray || !Fits(s.Element, t.GetElementType())) return;
                s.Now = t;
                Match(s.Element, t.GetElementType());
            }
            else s.Now = t;
        }

        /// <summary>Can a value of saved type <paramref name="id"/> go into a field of type <paramref name="t"/>?</summary>
        private bool Fits(int id, Type t)
        {
            var s = _types[id];
            var k = KindOf(t);
            switch (s.Kind)
            {
                case Kind.Object: return k == Kind.Object && t.IsValueType != s.IsClass && (t.FullName == s.Name || s.Now == t);
                case Kind.Array: return k == Kind.Array && Fits(s.Element, t.GetElementType());
                case Kind.String: return k == Kind.String;
                default: return k != Kind.Object && k != Kind.Array && k != Kind.String; // numbers, bools and enums turn into each other
            }
        }

        // ------------------------------------------------------------------ values

        public byte[] Write(GameState s)
        {
            using (var ms = new MemoryStream(4096))
            using (var w = new BinaryWriter(ms))
            {
                WriteValue(w, 0, s);
                w.Flush();
                return ms.ToArray();
            }
        }

        private void WriteValue(BinaryWriter w, int id, object v)
        {
            var s = _types[id];
            switch (s.Kind)
            {
                case Kind.Int32: w.Write((int)v); return;
                case Kind.UInt32: w.Write((uint)v); return;
                case Kind.Int64: w.Write((long)v); return;
                case Kind.UInt64: w.Write((ulong)v); return;
                case Kind.Bool: w.Write((bool)v); return;
                case Kind.Byte: w.Write((byte)v); return;
                case Kind.SByte: w.Write((sbyte)v); return;
                case Kind.Int16: w.Write((short)v); return;
                case Kind.Enum: w.Write(Convert.ToInt64(v)); return;
                case Kind.String: w.Write(v != null); if (v != null) w.Write((string)v); return;
                case Kind.Array:
                    var a = (Array)v;
                    if (a == null) { w.Write(-1); return; }
                    w.Write(a.Length);
                    for (var k = 0; k < a.Length; k++) WriteValue(w, s.Element, a.GetValue(k));
                    return;
                default:
                    if (s.IsClass) { w.Write(v != null); if (v == null) return; }
                    for (var k = 0; k < s.NowFields.Length; k++) WriteValue(w, s.FieldTypes[k], s.NowFields[k].GetValue(v));
                    return;
            }
        }

        public GameState Read(byte[] bytes)
        {
            using (var r = new BinaryReader(new MemoryStream(bytes)))
                return ReadValue(r, 0, typeof(GameState)) as GameState;
        }

        /// <summary>A value of saved type <paramref name="id"/>, as <paramref name="t"/> (null: read past it).</summary>
        private object ReadValue(BinaryReader r, int id, Type t)
        {
            var s = _types[id];
            switch (s.Kind)
            {
                case Kind.Int32: return Number(r.ReadInt32(), t);
                case Kind.UInt32: return Number(r.ReadUInt32(), t);
                case Kind.Int64: return Number(r.ReadInt64(), t);
                case Kind.UInt64: return Number((long)r.ReadUInt64(), t);
                case Kind.Bool: return Number(r.ReadBoolean() ? 1 : 0, t);
                case Kind.Byte: return Number(r.ReadByte(), t);
                case Kind.SByte: return Number(r.ReadSByte(), t);
                case Kind.Int16: return Number(r.ReadInt16(), t);
                case Kind.Enum: return Number(r.ReadInt64(), t);
                case Kind.String: return r.ReadBoolean() ? r.ReadString() : null;
                case Kind.Array:
                {
                    var n = r.ReadInt32();
                    if (n < 0) return null;
                    if (n > 100000) throw new InvalidDataException("replay: array too long");
                    var e = t != null && t.IsArray ? t.GetElementType() : null;
                    var a = e != null ? Array.CreateInstance(e, n) : null;
                    for (var k = 0; k < n; k++)
                    {
                        var v = ReadValue(r, s.Element, e);
                        if (a != null && v != null) a.SetValue(v, k);
                    }
                    return a;
                }
                default:
                {
                    if (s.IsClass && !r.ReadBoolean()) return null;
                    var into = t != null && s.Now == t ? Activator.CreateInstance(t, true) : null;
                    for (var k = 0; k < s.FieldNames.Length; k++)
                    {
                        var f = into != null ? s.NowFields[k] : null;
                        var v = ReadValue(r, s.FieldTypes[k], f != null ? f.FieldType : null);
                        if (f != null && v != null) f.SetValue(into, v);
                    }
                    return into;
                }
            }
        }

        private static object Number(long v, Type t)
        {
            if (t == null) return null;
            if (t.IsEnum) return Enum.ToObject(t, v);
            if (t == typeof(int)) return (int)v;
            if (t == typeof(uint)) return (uint)v;
            if (t == typeof(long)) return v;
            if (t == typeof(ulong)) return (ulong)v;
            if (t == typeof(bool)) return v != 0;
            if (t == typeof(byte)) return (byte)v;
            if (t == typeof(sbyte)) return (sbyte)v;
            if (t == typeof(short)) return (short)v;
            return null;
        }
    }
}
