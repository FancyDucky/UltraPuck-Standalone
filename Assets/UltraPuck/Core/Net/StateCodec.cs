using System;
using System.Collections.Generic;
using System.Reflection;

namespace UltraPuck.Core.Net
{
    /// <summary>
    /// A whole <see cref="GameState"/> as bytes and back, so a player who rejoins a match can be handed the game as
    /// it is. It walks every field of the state (players, their stats and cards, shots, pylons, rocks, ice…) in the
    /// order they're declared, so a new field is included without anything to update here. Both PCs run the same
    /// build (the version check makes sure), so the field order is the same on both.
    /// </summary>
    public static class StateCodec
    {
        public static byte[] Write(GameState s)
        {
            var w = new Writer(Msg.Hello); // (the type byte is skipped when reading)
            WriteValue(w, typeof(GameState), s);
            return w.ToArray();
        }

        public static GameState Read(byte[] bytes)
        {
            var r = new Reader(bytes);
            return (GameState)ReadValue(r, typeof(GameState));
        }

        private static readonly Dictionary<Type, FieldInfo[]> _fields = new Dictionary<Type, FieldInfo[]>();

        private static FieldInfo[] FieldsOf(Type t)
        {
            FieldInfo[] f;
            if (_fields.TryGetValue(t, out f)) return f;
            var list = new List<FieldInfo>();
            foreach (var x in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (!x.IsLiteral && !x.IsNotSerialized) list.Add(x);
            list.Sort((a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
            f = list.ToArray();
            _fields[t] = f;
            return f;
        }

        private static void WriteValue(Writer w, Type t, object v)
        {
            if (t == typeof(int)) { w.I32((int)v); return; }
            if (t == typeof(uint)) { w.U32((uint)v); return; }
            if (t == typeof(long)) { w.U64((ulong)(long)v); return; }
            if (t == typeof(ulong)) { w.U64((ulong)v); return; }
            if (t == typeof(bool)) { w.U8((bool)v ? 1 : 0); return; }
            if (t == typeof(byte)) { w.U8((byte)v); return; }
            if (t == typeof(sbyte)) { w.S8((sbyte)v); return; }
            if (t == typeof(short)) { w.I32((short)v); return; }
            if (t == typeof(string)) { w.U8(v == null ? 0 : 1); if (v != null) w.Str((string)v); return; }
            if (t.IsEnum) { w.I32(Convert.ToInt32(v)); return; }
            if (t.IsArray)
            {
                var a = (Array)v;
                if (a == null) { w.I32(-1); return; }
                w.I32(a.Length);
                var e = t.GetElementType();
                for (var k = 0; k < a.Length; k++) WriteValue(w, e, a.GetValue(k));
                return;
            }
            if (!t.IsValueType) { w.U8(v == null ? 0 : 1); if (v == null) return; }
            foreach (var f in FieldsOf(t)) WriteValue(w, f.FieldType, f.GetValue(v));
        }

        private static object ReadValue(Reader r, Type t)
        {
            if (t == typeof(int)) return r.I32();
            if (t == typeof(uint)) return r.U32();
            if (t == typeof(long)) return (long)r.U64();
            if (t == typeof(ulong)) return r.U64();
            if (t == typeof(bool)) return r.U8() != 0;
            if (t == typeof(byte)) return (byte)r.U8();
            if (t == typeof(sbyte)) return r.S8();
            if (t == typeof(short)) return (short)r.I32();
            if (t == typeof(string)) return r.U8() == 0 ? null : r.Str();
            if (t.IsEnum) return Enum.ToObject(t, r.I32());
            if (t.IsArray)
            {
                var n = r.I32();
                if (n < 0) return null;
                if (n > 100000) throw new FormatException("state: array too long");
                var e = t.GetElementType();
                var a = Array.CreateInstance(e, n);
                for (var k = 0; k < n; k++) a.SetValue(ReadValue(r, e), k);
                return a;
            }
            if (!t.IsValueType && r.U8() == 0) return null;
            var o = Activator.CreateInstance(t, true);
            foreach (var f in FieldsOf(t)) f.SetValue(o, ReadValue(r, f.FieldType));
            return o;
        }
    }
}
