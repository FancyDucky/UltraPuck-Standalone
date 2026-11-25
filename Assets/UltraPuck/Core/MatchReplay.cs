using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace UltraPuck.Core
{
    /// <summary>
    /// A whole match, kept to watch again (Profile > Replays): the game as it was every couple of ticks, plus who was
    /// who, the rink and the rules, and markers for the goals, saves and card picks. States are stored in chunks of
    /// <see cref="ChunkFrames"/>: the first whole, each next one as its difference from the one before (mostly zeros),
    /// and each chunk squeezed with Deflate, so a ten-minute match is a few megabytes. Looks only: not part of the rules.
    /// </summary>
    public sealed class MatchReplay
    {
        public const int FileVersion = 3;
        public const int ChunkFrames = 120;
        public const string Extension = ".upreplay";

        public enum MarkerKind : byte { Goal = 0, Save = 1, Cards = 2 }

        /// <summary>From frame <see cref="Frame"/>: song <see cref="Song"/> ("" none) was <see cref="At"/> seconds in.</summary>
        public struct MusicMark { public int Frame; public string Song; public float At; }

        public readonly List<MusicMark> Music = new List<MusicMark>();

        /// <summary>The song at <paramref name="tick"/> and how far into it (seconds), or "" for none.</summary>
        public string SongAt(float tick, out float at)
        {
            at = 0f;
            if (Music.Count == 0 || Frames == 0) return "";
            var f = FrameAt(tick);
            var k = -1;
            for (var i = 0; i < Music.Count; i++) if (Music[i].Frame <= f) k = i;
            if (k < 0) return "";
            var m = Music[k];
            at = m.At + (tick - Ticks[Math.Max(0, Math.Min(Frames - 1, m.Frame))]) / TicksPerSecond;
            return m.Song ?? "";
        }

        public struct Marker
        {
            public int Frame;
            public MarkerKind Kind;
            public int Team, Player;
        }

        /// <summary>Who each player was (by index) from frame <see cref="Frame"/> on: names, symbols and Profile looks.</summary>
        public sealed class Cast
        {
            public int Frame;
            public string[] Names = new string[0];
            public int[] Symbols = new int[0];
            public int[] Styles = new int[0];
            /// <summary>Each player's team (0, 1).</summary>
            public int[] Teams = new int[0];

            public bool Same(Cast o)
            {
                if (o == null || o.Names.Length != Names.Length || o.Teams.Length != Teams.Length) return false;
                for (var k = 0; k < Names.Length; k++)
                    if (o.Names[k] != Names[k] || o.Symbols[k] != Symbols[k] || o.Styles[k] != Styles[k]) return false;
                for (var k = 0; k < Teams.Length; k++) if (o.Teams[k] != Teams[k]) return false;
                return true;
            }
        }

        // ------------------------------------------------------------------ what it is

        public string GameVersion = "";
        public int NetVersion;
        public long WhenUtcTicks;
        public string MapFile = "", MapName = "";
        public byte[] Rules = new byte[0];
        public int Colour0, Colour1 = 1;
        public int TicksPerSecond = 120;
        public int Score0, Score1;
        public readonly List<Cast> Casts = new List<Cast>();
        public readonly List<Marker> Markers = new List<Marker>();

        /// <summary>Every frame's tick and phase (all of them are in memory; the states are in the chunks).</summary>
        public readonly List<int> Ticks = new List<int>();
        public readonly List<byte> Phases = new List<byte>();

        private readonly List<byte[]> _chunks = new List<byte[]>();
        private ReplayCodec _codec;

        public int Frames { get { return Ticks.Count; } }
        public float Seconds { get { return Frames >= 2 ? (Ticks[Frames - 1] - Ticks[0]) / (float)TicksPerSecond : (_lastTick - _firstTick) / (float)TicksPerSecond; } }
        // (from the description, for a replay whose frames haven't been read)
        private int _firstTick, _lastTick;
        public DateTime When { get { return new DateTime(WhenUtcTicks, DateTimeKind.Utc).ToLocalTime(); } }

        public Cast CastAt(int frame)
        {
            Cast c = Casts.Count > 0 ? Casts[0] : null;
            foreach (var x in Casts) if (x.Frame <= frame) c = x;
            return c;
        }

        /// <summary>The frame shown at <paramref name="tick"/> (the last one at or before it).</summary>
        public int FrameAt(float tick)
        {
            int lo = 0, hi = Frames - 1;
            if (hi < 0) return 0;
            if (tick <= Ticks[0]) return 0;
            if (tick >= Ticks[hi]) return hi;
            while (lo < hi)
            {
                var mid = (lo + hi + 1) / 2;
                if (Ticks[mid] <= tick) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        // ------------------------------------------------------------------ recording

        /// <summary>Writes a match down as it's played. Encoding and squeezing happen on a thread of their own.</summary>
        public sealed class Recorder
        {
            public readonly MatchReplay Replay;
            private readonly BlockingCollection<GameState> _queue;
            private readonly Thread _thread;
            private readonly bool _threaded;
            private MemoryStream _chunk;
            private BinaryWriter _chunkW;
            private byte[] _prev;
            private int _inChunk;
            private readonly object _lock = new object();
            private Exception _failed;

            private int _lastTick = int.MinValue, _lastGoals = -1;
            private int[] _saves = new int[0];
            private byte _lastPhase = 255;

            /// <param name="threaded">Encode on a background thread (false: right away, for tests).</param>
            public Recorder(MatchReplay meta, bool threaded = true)
            {
                Replay = meta;
                Replay._codec = ReplayCodec.ForWriting();
                _threaded = threaded;
                if (threaded)
                {
                    _queue = new BlockingCollection<GameState>(new ConcurrentQueue<GameState>(), 2000);
                    _thread = new Thread(Run) { IsBackground = true, Name = "Ultra Puck replay" };
                    _thread.Start();
                }
            }

            /// <summary>How far apart frames are kept, in ticks (60 a second at 120 ticks a second).</summary>
            public int Spacing = 2;

            /// <summary>
            /// The game now (<paramref name="s"/> is copied if it's kept). <paramref name="cast"/>: who's who now (null:
            /// as before). Returns whether a frame was kept.
            /// </summary>
            public bool Add(GameState s, Cast cast) { return Add(s, cast, "", 0f); }

            private string _song = "";

            /// <summary><see cref="Add(GameState, Cast)"/>, and the song playing (<paramref name="song"/>, <paramref name="songAt"/> seconds in).</summary>
            public bool Add(GameState s, Cast cast, string song, float songAt)
            {
                if (s != null && s.Tick >= _lastTick + Spacing && _failed == null)
                {
                    song = song ?? "";
                    var frame = Replay.Ticks.Count;
                    // now and then (it drifts if the music pauses), and whenever the song changes
                    if (song != _song || frame % 60 == 0) Replay.Music.Add(new MusicMark { Frame = frame, Song = song, At = songAt });
                    _song = song;
                }
                return AddFrame(s, cast);
            }

            private bool AddFrame(GameState s, Cast cast)
            {
                if (s == null || s.Tick < _lastTick + Spacing) return false;
                if (_failed != null) return false;
                _lastTick = s.Tick;
                var frame = Replay.Ticks.Count;
                Replay.Ticks.Add(s.Tick);
                Replay.Phases.Add((byte)s.Phase);
                if (cast != null && (Replay.Casts.Count == 0 || !cast.Same(Replay.Casts[Replay.Casts.Count - 1]))) { cast.Frame = frame; Replay.Casts.Add(cast); }
                // markers: goals, saves, and card picks
                var goals = s.Score[0] + s.Score[1];
                if (_lastGoals >= 0 && goals > _lastGoals) Replay.Markers.Add(new Marker { Frame = frame, Kind = MarkerKind.Goal, Team = s.LastScorer, Player = s.LastGoalBy });
                _lastGoals = goals;
                if (_saves.Length != s.Players.Length) { var was = _saves; _saves = new int[s.Players.Length]; for (var i = 0; i < _saves.Length; i++) _saves[i] = i < was.Length ? was[i] : s.Players[i].Saves; }
                for (var i = 0; i < s.Players.Length; i++)
                {
                    if (s.Players[i].Saves > _saves[i]) Replay.Markers.Add(new Marker { Frame = frame, Kind = MarkerKind.Save, Team = s.Players[i].Team, Player = i });
                    _saves[i] = s.Players[i].Saves;
                }
                if (s.Phase == Phase.Picking && _lastPhase != (byte)Phase.Picking) Replay.Markers.Add(new Marker { Frame = frame, Kind = MarkerKind.Cards, Team = -1, Player = -1 });
                _lastPhase = (byte)s.Phase;
                Replay.Score0 = s.Score[0]; Replay.Score1 = s.Score[1];

                var copy = s.Clone();
                if (_threaded) { if (!_queue.TryAdd(copy)) _failed = new Exception("replay: too far behind"); }
                else Encode(copy);
                return true;
            }

            private void Run()
            {
                try { foreach (var s in _queue.GetConsumingEnumerable()) Encode(s); }
                catch (Exception e) { _failed = e; }
            }

            private void Encode(GameState s)
            {
                var bytes = Replay._codec.Write(s);
                lock (_lock)
                {
                    if (_chunk == null) { _chunk = new MemoryStream(); _chunkW = new BinaryWriter(_chunk); _prev = null; _inChunk = 0; }
                    if (_prev != null && _prev.Length == bytes.Length)
                    {
                        var diff = new byte[bytes.Length];
                        for (var k = 0; k < bytes.Length; k++) diff[k] = (byte)(bytes[k] ^ _prev[k]);
                        _chunkW.Write((byte)1); _chunkW.Write(diff.Length); _chunkW.Write(diff);
                    }
                    else { _chunkW.Write((byte)0); _chunkW.Write(bytes.Length); _chunkW.Write(bytes); }
                    _prev = bytes;
                    _inChunk++;
                    if (_inChunk >= ChunkFrames) CloseChunk();
                }
            }

            private void CloseChunk()
            {
                if (_chunk == null) return;
                _chunkW.Flush();
                Replay._chunks.Add(Squeeze(_chunk.ToArray()));
                _chunk = null; _chunkW = null; _prev = null; _inChunk = 0;
            }

            /// <summary>Stop without keeping anything (the match was left part-way).</summary>
            public void Abandon()
            {
                if (_threaded && !_queue.IsAddingCompleted) _queue.CompleteAdding();
            }

            /// <summary>Everything written so far, ready to save or watch (recording can't go on after this).</summary>
            public MatchReplay Finish()
            {
                if (_threaded)
                {
                    _queue.CompleteAdding();
                    _thread.Join(10000);
                }
                lock (_lock) CloseChunk();
                if (_failed != null) throw new IOException("The replay couldn't be recorded: " + _failed.Message, _failed);
                // (frames the encoder didn't get to: none, unless it gave up)
                var kept = Replay._chunks.Count == 0 ? 0 : (Replay._chunks.Count - 1) * ChunkFrames + Replay.CountInLast();
                while (Replay.Ticks.Count > kept) { Replay.Ticks.RemoveAt(Replay.Ticks.Count - 1); Replay.Phases.RemoveAt(Replay.Phases.Count - 1); }
                return Replay;
            }
        }

        private int CountInLast()
        {
            if (_chunks.Count == 0) return 0;
            return DecodeChunk(_chunks.Count - 1).Length;
        }

        // ------------------------------------------------------------------ playing back

        private readonly Dictionary<int, GameState[]> _cache = new Dictionary<int, GameState[]>();
        private readonly List<int> _cacheOrder = new List<int>();
        private readonly HashSet<int> _fetching = new HashSet<int>();
        private readonly object _cacheLock = new object();

        /// <summary>The game at frame <paramref name="frame"/> (decoded a chunk at a time and kept for a while).</summary>
        public GameState FrameState(int frame)
        {
            if (Frames == 0) return null;
            frame = Math.Max(0, Math.Min(Frames - 1, frame));
            var chunk = frame / ChunkFrames;
            var states = ChunkStates(chunk);
            // the next chunk is decoded in the background before it's needed
            if (frame % ChunkFrames > ChunkFrames / 2) Prefetch(chunk + 1);
            var k = frame - chunk * ChunkFrames;
            return k < states.Length ? states[k] : states[states.Length - 1];
        }

        // ------------------------------------------------------------------ scrubbing

        // unpacked chunks (bytes only: cheap to keep many) and the last few single frames read from them
        private readonly Dictionary<int, byte[]> _raw = new Dictionary<int, byte[]>();
        private readonly List<int> _rawOrder = new List<int>();
        private readonly Dictionary<int, GameState> _single = new Dictionary<int, GameState>();
        private readonly List<int> _singleOrder = new List<int>();
        private const int RawKept = 48, SinglesKept = 8;

        /// <summary>
        /// <see cref="FrameState"/>, but when its chunk isn't decoded yet only this one frame is (for dragging along the
        /// time bar, where every move can land in another chunk). The whole chunk is decoded in the background after.
        /// </summary>
        public GameState FrameStateQuick(int frame)
        {
            if (Frames == 0) return null;
            frame = Math.Max(0, Math.Min(Frames - 1, frame));
            var chunk = frame / ChunkFrames;
            // (playing on: the next chunk is decoded in the background before it's needed)
            if (frame % ChunkFrames > ChunkFrames / 2) Prefetch(chunk + 1);
            lock (_cacheLock)
            {
                GameState[] got;
                if (_cache.TryGetValue(chunk, out got)) { var j = frame - chunk * ChunkFrames; return j < got.Length ? got[j] : got[got.Length - 1]; }
                GameState one;
                if (_single.TryGetValue(frame, out one)) return one;
            }
            var s = DecodeOne(chunk, frame - chunk * ChunkFrames);
            lock (_cacheLock)
            {
                _single[frame] = s;
                _singleOrder.Add(frame);
                while (_singleOrder.Count > SinglesKept) { _single.Remove(_singleOrder[0]); _singleOrder.RemoveAt(0); }
            }
            Prefetch(chunk);
            return s;
        }

        private byte[] RawChunk(int chunk)
        {
            lock (_cacheLock)
            {
                byte[] got;
                if (_raw.TryGetValue(chunk, out got)) { _rawOrder.Remove(chunk); _rawOrder.Add(chunk); return got; }
            }
            var raw = Unsqueeze(_chunks[chunk]);
            lock (_cacheLock)
            {
                _raw[chunk] = raw;
                _rawOrder.Remove(chunk);
                _rawOrder.Add(chunk);
                while (_rawOrder.Count > RawKept) { _raw.Remove(_rawOrder[0]); _rawOrder.RemoveAt(0); }
            }
            return raw;
        }

        /// <summary>Frame <paramref name="k"/> of a chunk: the differences are undone up to it, and only it is read.</summary>
        private GameState DecodeOne(int chunk, int k)
        {
            var raw = RawChunk(chunk);
            byte[] cur = null;
            var pos = 0;
            for (var i = 0; pos < raw.Length; i++)
            {
                var kind = raw[pos];
                var n = BitConverter.ToInt32(raw, pos + 1);
                pos += 5;
                if (n < 0 || pos + n > raw.Length) throw new InvalidDataException("replay: bad frame");
                if (kind == 1)
                {
                    if (cur == null || cur.Length != n) throw new InvalidDataException("replay: a difference with nothing before it");
                    for (var b = 0; b < n; b++) cur[b] ^= raw[pos + b];
                }
                else { cur = new byte[n]; Buffer.BlockCopy(raw, pos, cur, 0, n); }
                pos += n;
                if (i == k) break;
            }
            return _codec.Read(cur);
        }

        private GameState[] ChunkStates(int chunk)
        {
            lock (_cacheLock)
            {
                GameState[] got;
                if (_cache.TryGetValue(chunk, out got)) { _cacheOrder.Remove(chunk); _cacheOrder.Add(chunk); return got; }
            }
            var states = DecodeChunk(chunk);
            Keep(chunk, states);
            return states;
        }

        private void Keep(int chunk, GameState[] states)
        {
            lock (_cacheLock)
            {
                _cache[chunk] = states;
                _cacheOrder.Remove(chunk);
                _cacheOrder.Add(chunk);
                while (_cacheOrder.Count > 6) { _cache.Remove(_cacheOrder[0]); _cacheOrder.RemoveAt(0); }
            }
        }

        private void Prefetch(int chunk)
        {
            if (chunk < 0 || chunk >= _chunks.Count) return;
            lock (_cacheLock) { if (_cache.ContainsKey(chunk) || !_fetching.Add(chunk)) return; }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { Keep(chunk, DecodeChunk(chunk)); }
                catch { /* it'll be decoded when it's needed */ }
                finally { lock (_cacheLock) _fetching.Remove(chunk); }
            });
        }

        private GameState[] DecodeChunk(int chunk)
        {
            var raw = RawChunk(chunk);
            var list = new List<GameState>(ChunkFrames);
            using (var r = new BinaryReader(new MemoryStream(raw)))
            {
                byte[] prev = null;
                while (r.BaseStream.Position < r.BaseStream.Length)
                {
                    var kind = r.ReadByte();
                    var n = r.ReadInt32();
                    if (n < 0 || n > 64 * 1024 * 1024) throw new InvalidDataException("replay: bad frame");
                    var bytes = r.ReadBytes(n);
                    if (kind == 1)
                    {
                        if (prev == null || prev.Length != n) throw new InvalidDataException("replay: a difference with nothing before it");
                        for (var k = 0; k < n; k++) bytes[k] ^= prev[k];
                    }
                    prev = bytes;
                    list.Add(_codec.Read(bytes));
                }
            }
            return list.ToArray();
        }

        private static byte[] Squeeze(byte[] raw)
        {
            using (var ms = new MemoryStream())
            {
                using (var d = new DeflateStream(ms, CompressionLevel.Fastest, true)) d.Write(raw, 0, raw.Length);
                return ms.ToArray();
            }
        }

        private static byte[] Unsqueeze(byte[] squeezed)
        {
            using (var d = new DeflateStream(new MemoryStream(squeezed), CompressionMode.Decompress))
            using (var ms = new MemoryStream())
            {
                d.CopyTo(ms);
                return ms.ToArray();
            }
        }

        // ------------------------------------------------------------------ files

        private static readonly byte[] Magic = { (byte)'U', (byte)'P', (byte)'R', (byte)'P' };

        public void Save(string path)
        {
            var tmp = path + ".tmp";
            using (var w = new BinaryWriter(File.Create(tmp)))
            {
                w.Write(Magic);
                w.Write(FileVersion);
                WriteHeader(w);
                _codec.WriteSchema(w);
                w.Write(Ticks.Count);
                for (var k = 0; k < Ticks.Count; k++) { w.Write(Ticks[k]); w.Write(Phases[k]); }
                w.Write(_chunks.Count);
                foreach (var c in _chunks) { w.Write(c.Length); w.Write(c); }
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        private void WriteHeader(BinaryWriter w)
        {
            w.Write(GameVersion ?? ""); w.Write(NetVersion); w.Write(WhenUtcTicks);
            w.Write(MapFile ?? ""); w.Write(MapName ?? "");
            w.Write(Rules.Length); w.Write(Rules);
            w.Write(Colour0); w.Write(Colour1); w.Write(TicksPerSecond); w.Write(Score0); w.Write(Score1);
            w.Write(Frames > 0 ? Ticks[0] : 0); w.Write(Frames > 0 ? Ticks[Frames - 1] : 0);
            w.Write(Casts.Count);
            foreach (var c in Casts)
            {
                w.Write(c.Frame); w.Write(c.Names.Length);
                for (var k = 0; k < c.Names.Length; k++) { w.Write(c.Names[k] ?? ""); w.Write(c.Symbols[k]); w.Write(c.Styles[k]); w.Write(k < c.Teams.Length ? c.Teams[k] : 0); }
            }
            w.Write(Markers.Count);
            foreach (var m in Markers) { w.Write(m.Frame); w.Write((byte)m.Kind); w.Write(m.Team); w.Write(m.Player); }
            w.Write(Music.Count);
            foreach (var m in Music) { w.Write(m.Frame); w.Write(m.Song ?? ""); w.Write(m.At); }
        }

        private void ReadHeader(BinaryReader r, int version)
        {
            GameVersion = r.ReadString(); NetVersion = r.ReadInt32(); WhenUtcTicks = r.ReadInt64();
            MapFile = r.ReadString(); MapName = r.ReadString();
            Rules = r.ReadBytes(Count(r, 4096));
            Colour0 = r.ReadInt32(); Colour1 = r.ReadInt32(); TicksPerSecond = Math.Max(1, r.ReadInt32()); Score0 = r.ReadInt32(); Score1 = r.ReadInt32();
            if (version >= 2) { _firstTick = r.ReadInt32(); _lastTick = r.ReadInt32(); }
            for (int k = 0, n = Count(r, 100000); k < n; k++)
            {
                var c = new Cast { Frame = r.ReadInt32() };
                var m = Count(r, 64);
                c.Names = new string[m]; c.Symbols = new int[m]; c.Styles = new int[m]; c.Teams = new int[m];
                for (var i = 0; i < m; i++) { c.Names[i] = r.ReadString(); c.Symbols[i] = r.ReadInt32(); c.Styles[i] = r.ReadInt32(); if (version >= 2) c.Teams[i] = r.ReadInt32(); }
                Casts.Add(c);
            }
            for (int k = 0, n = Count(r, 1000000); k < n; k++)
                Markers.Add(new Marker { Frame = r.ReadInt32(), Kind = (MarkerKind)r.ReadByte(), Team = r.ReadInt32(), Player = r.ReadInt32() });
            if (version >= 3)
                for (int k = 0, n = Count(r, 1000000); k < n; k++)
                    Music.Add(new MusicMark { Frame = r.ReadInt32(), Song = r.ReadString(), At = r.ReadSingle() });
        }

        private static int Count(BinaryReader r, int max)
        {
            var n = r.ReadInt32();
            if (n < 0 || n > max) throw new InvalidDataException("replay: a list is the wrong size");
            return n;
        }

        private static int CheckMagic(BinaryReader r)
        {
            var m = r.ReadBytes(4);
            if (m.Length != 4 || m[0] != Magic[0] || m[1] != Magic[1] || m[2] != Magic[2] || m[3] != Magic[3]) throw new InvalidDataException("Not an Ultra Puck replay.");
            var v = r.ReadInt32();
            if (v > FileVersion) throw new InvalidDataException("This replay is from a newer version of Ultra Puck.");
            return v;
        }

        /// <summary>Just the description (who, where, when, the score, the markers): quick, for the list of replays.</summary>
        public static MatchReplay LoadInfo(string path)
        {
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                var v = CheckMagic(r);
                var rep = new MatchReplay();
                rep.ReadHeader(r, v);
                return rep;
            }
        }

        /// <summary>The whole replay, to watch.</summary>
        public static MatchReplay Load(string path)
        {
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                var v = CheckMagic(r);
                var rep = new MatchReplay();
                rep.ReadHeader(r, v);
                rep._codec = ReplayCodec.ReadSchema(r);
                for (int k = 0, n = Count(r, 10000000); k < n; k++) { rep.Ticks.Add(r.ReadInt32()); rep.Phases.Add(r.ReadByte()); }
                for (int k = 0, n = Count(r, 1000000); k < n; k++) rep._chunks.Add(r.ReadBytes(Count(r, 256 * 1024 * 1024)));
                if (rep.Frames == 0 || rep._chunks.Count == 0) throw new InvalidDataException("This replay is empty.");
                return rep;
            }
        }
    }
}
