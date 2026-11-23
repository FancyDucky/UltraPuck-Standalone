using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// How often each card has been picked in online matches. Everyone keeps their own picks (one record per match, so
    /// a match is never counted twice), and a table of everyone's they've heard of. Each PC's table goes round the online
    /// lobby when people meet, so "global" grows into everyone's picks put together. Counts only ever go up, so two
    /// tables merge by taking the bigger count for each person and card; a person's own picks come only from them.
    /// Kept in <c>card-stats.dat</c> in the save folder (from 0.1.49 on).
    /// </summary>
    public sealed class CardStatsTable
    {
        public sealed class Person
        {
            public string Name = "";
            public readonly Dictionary<string, int> Picks = new Dictionary<string, int>();
        }

        /// <summary>Everyone heard of (this PC's people too), by their id (a Steam id, or this PC's own random id).</summary>
        public readonly Dictionary<string, Person> People = new Dictionary<string, Person>();

        private const int MaxPeople = 512, MaxCards = 512, MaxText = 96, MaxCount = 1000000;

        /// <summary>Take in another table: for each person and card, the bigger count (never the people in <paramref name="mine"/>: only this PC knows those).</summary>
        public bool Merge(CardStatsTable other, ICollection<string> mine)
        {
            var changed = false;
            foreach (var kv in other.People)
            {
                if (mine != null && mine.Contains(kv.Key)) continue;
                Person p;
                if (!People.TryGetValue(kv.Key, out p))
                {
                    if (People.Count >= MaxPeople) continue;
                    p = new Person();
                    People[kv.Key] = p;
                    changed = true;
                }
                if (!string.IsNullOrEmpty(kv.Value.Name) && p.Name != kv.Value.Name) { p.Name = kv.Value.Name; changed = true; }
                foreach (var c in kv.Value.Picks)
                {
                    int have;
                    p.Picks.TryGetValue(c.Key, out have);
                    if (c.Value > have) { p.Picks[c.Key] = c.Value; changed = true; }
                }
            }
            return changed;
        }

        /// <summary>Every person's picks of <paramref name="card"/> added up.</summary>
        public int Total(string card)
        {
            var n = 0;
            foreach (var p in People.Values) { int c; if (p.Picks.TryGetValue(card, out c)) n += c; }
            return n;
        }

        public void Write(BinaryWriter w)
        {
            w.Write(People.Count);
            foreach (var kv in People)
            {
                w.Write(kv.Key);
                w.Write(kv.Value.Name ?? "");
                w.Write(kv.Value.Picks.Count);
                foreach (var c in kv.Value.Picks) { w.Write(c.Key); w.Write(c.Value); }
            }
        }

        /// <summary>Read a table (from the network or the file). Anything odd or oversized throws or is left out.</summary>
        public static CardStatsTable Read(BinaryReader r)
        {
            var t = new CardStatsTable();
            var n = r.ReadInt32();
            if (n < 0 || n > MaxPeople) throw new FormatException("card stats: too many people");
            for (var k = 0; k < n; k++)
            {
                var id = Text(r.ReadString());
                var p = new Person { Name = Text(r.ReadString()) };
                var cards = r.ReadInt32();
                if (cards < 0 || cards > MaxCards) throw new FormatException("card stats: too many cards");
                for (var c = 0; c < cards; c++)
                {
                    var card = Text(r.ReadString());
                    var count = r.ReadInt32();
                    if (card.Length > 0 && count > 0) p.Picks[card] = Math.Min(count, MaxCount);
                }
                if (id.Length > 0) t.People[id] = p;
            }
            return t;
        }

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                Write(w);
                w.Flush();
                return ms.ToArray();
            }
        }

        public static CardStatsTable FromBytes(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms, Encoding.UTF8))
                return Read(r);
        }

        private static string Text(string s) { return s == null ? "" : s.Length > MaxText ? s.Substring(0, MaxText) : s; }
    }

    /// <summary>This PC's card stats: its own picks match by match, and the shared table (see <see cref="CardStatsTable"/>).</summary>
    internal static class CardStats
    {
        private const string FileName = "card-stats.dat";
        private const int FileVersion = 1;

        private static bool _loaded;
        /// <summary>This PC's own ids (a Steam id, and a random one for when Steam isn't running).</summary>
        private static readonly List<string> _mine = new List<string>();
        private static string _localId = "";
        /// <summary>This PC's people's picks, per match ("match id:seed"), most of each card seen in that match.</summary>
        private static readonly Dictionary<string, Dictionary<string, int>> _matches = new Dictionary<string, Dictionary<string, int>>();
        private static readonly CardStatsTable _table = new CardStatsTable();
        private static float _saveAt = -1f;

        /// <summary>Screenshot runs and the like: nothing is saved.</summary>
        public static bool NoSave;

        private static string MyId
        {
            get
            {
                if (SteamNet.Ready)
                {
                    try { return "steam:" + Steamworks.SteamUser.GetSteamID().m_SteamID; } catch (Exception) { }
                }
                return _localId;
            }
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                var path = Path.Combine(Application.persistentDataPath, FileName);
                if (File.Exists(path))
                    using (var r = new BinaryReader(File.OpenRead(path), Encoding.UTF8))
                    {
                        if (r.ReadInt32() != FileVersion) throw new FormatException("card stats: unknown version");
                        _localId = r.ReadString();
                        var ids = r.ReadInt32();
                        for (var k = 0; k < ids && k < 16; k++) { var id = r.ReadString(); if (!_mine.Contains(id)) _mine.Add(id); }
                        var matches = r.ReadInt32();
                        for (var k = 0; k < matches; k++)
                        {
                            var key = r.ReadString();
                            var picks = new Dictionary<string, int>();
                            var n = r.ReadInt32();
                            for (var c = 0; c < n; c++) { var card = r.ReadString(); picks[card] = r.ReadInt32(); }
                            _matches[key] = picks;
                        }
                        _table.Merge(CardStatsTable.Read(r), null);
                    }
            }
            catch (Exception e) { Debug.LogWarning("Ultra Puck: card stats not read: " + e.Message); }
            if (string.IsNullOrEmpty(_localId)) _localId = "pc:" + Guid.NewGuid().ToString("N");
            if (!_mine.Contains(_localId)) _mine.Add(_localId);
            Mine();
        }

        /// <summary>Make sure this PC's ids are known (Steam may have started since) and its own row is up to date.</summary>
        private static void Mine()
        {
            var id = MyId;
            if (!_mine.Contains(id)) _mine.Add(id);
            // all of this PC's picks go under its current id; its other ids' rows are emptied (so nothing counts twice)
            var me = new CardStatsTable.Person { Name = SteamNet.Ready ? SteamNet.Name : "This PC" };
            foreach (var m in _matches.Values)
                foreach (var c in m) { int n; me.Picks.TryGetValue(c.Key, out n); me.Picks[c.Key] = n + c.Value; }
            foreach (var other in _mine) if (other != id) _table.People.Remove(other);
            _table.People[id] = me;
        }

        /// <summary>
        /// In an online match: the cards this PC's people hold right now (as counts by card id). Each match keeps the
        /// most of each card seen, so a utility that was replaced still counts as picked.
        /// </summary>
        public static void SeeMatch(string match, Dictionary<string, int> held)
        {
            Load();
            Dictionary<string, int> m;
            if (!_matches.TryGetValue(match, out m)) { m = new Dictionary<string, int>(); _matches[match] = m; }
            var changed = false;
            foreach (var c in held)
            {
                int had;
                m.TryGetValue(c.Key, out had);
                if (c.Value > had) { m[c.Key] = c.Value; changed = true; }
            }
            if (!changed) return;
            Mine();
            SaveSoon();
        }

        /// <summary>This PC's table, to send round the lobby.</summary>
        public static byte[] Out()
        {
            Load();
            Mine();
            return _table.ToBytes();
        }

        /// <summary>Someone else's table, from the lobby.</summary>
        public static void In(byte[] data)
        {
            Load();
            try
            {
                if (_table.Merge(CardStatsTable.FromBytes(data), _mine)) SaveSoon();
            }
            catch (Exception e) { Debug.LogWarning("Ultra Puck: card stats from the lobby not read: " + e.Message); }
        }

        /// <summary>How many times this PC's people have picked <paramref name="card"/>, and everyone heard of.</summary>
        public static int Personal(string card)
        {
            Load();
            var n = 0;
            foreach (var m in _matches.Values) { int c; if (m.TryGetValue(card, out c)) n += c; }
            return n;
        }

        public static int Global(string card) { Load(); return _table.Total(card); }

        /// <summary>How many people the global numbers come from.</summary>
        public static int PeopleCount { get { Load(); var n = 0; foreach (var p in _table.People.Values) if (p.Picks.Count > 0) n++; return n; } }

        private static void SaveSoon() { _saveAt = Time.unscaledTime + 2f; }

        /// <summary>Every frame: writes the file a moment after a change.</summary>
        public static void Tick()
        {
            if (_saveAt < 0f || Time.unscaledTime < _saveAt) return;
            _saveAt = -1f;
            Save();
        }

        public static void Save()
        {
            if (!_loaded || NoSave) return;
            try
            {
                var path = Path.Combine(Application.persistentDataPath, FileName);
                var tmp = path + ".tmp";
                using (var w = new BinaryWriter(File.Create(tmp), Encoding.UTF8))
                {
                    w.Write(FileVersion);
                    w.Write(_localId);
                    w.Write(_mine.Count);
                    foreach (var id in _mine) w.Write(id);
                    w.Write(_matches.Count);
                    foreach (var kv in _matches)
                    {
                        w.Write(kv.Key);
                        w.Write(kv.Value.Count);
                        foreach (var c in kv.Value) { w.Write(c.Key); w.Write(c.Value); }
                    }
                    _table.Write(w);
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e) { Debug.LogWarning("Ultra Puck: card stats not saved: " + e.Message); }
        }
    }
}
