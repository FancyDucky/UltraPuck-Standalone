using System;
using System.Collections.Generic;
using System.Text;

namespace UltraPuck.Core.Net
{
    /// <summary>Message types. Every UDP packet is one message; its first byte says which.</summary>
    public enum Msg : byte
    {
        Hello = 1,      // client to host: please let me in (version, build fingerprint)
        Welcome = 2,    // host to client: you're in, here's your peer number
        Refuse = 3,     // host to client: you can't join (and why)
        Players = 4,    // client to host: the players on my PC (names, teams)
        Lobby = 5,      // host to everyone: who's in the game
        Start = 6,      // host to everyone: the match begins (seed, seats)
        Inputs = 7,     // controls for a run of ticks, plus acks
        Checksum = 8,   // a fingerprint of the game at a tick, to catch two PCs drifting apart
        Ping = 9,
        Pong = 10,
        Leave = 11,     // I'm going
        Away = 12,      // host to everyone: who's away (the bot plays them) from which tick until which
        Rejoin = 13,    // host to someone coming back mid-match: the game as it is, and the seats
        Recolour = 14,  // client to host: please change my side's colour (from, to)
        CardStats = 15, // in the lobby, both ways: the card pick counts this PC knows of (the host passes them on)
        Styles = 16,    // client to host: the looks (Profile: dash, banner, goal explosion) of the players on my PC
        StyleTable = 17,// host to everyone: every PC's players' looks
    }

    /// <summary>Writes a message into bytes (little-endian, compact).</summary>
    public sealed class Writer
    {
        private readonly List<byte> _b = new List<byte>(256);

        public Writer(Msg type) { _b.Add((byte)type); }

        public Writer U8(int v) { _b.Add((byte)v); return this; }
        public Writer S8(sbyte v) { _b.Add((byte)v); return this; }
        public Writer I32(int v) { for (var k = 0; k < 4; k++) _b.Add((byte)(v >> (8 * k))); return this; }
        public Writer U32(uint v) { return I32((int)v); }
        public Writer U64(ulong v) { for (var k = 0; k < 8; k++) _b.Add((byte)(v >> (8 * k))); return this; }
        public Writer F32(float v) { return I32(BitConverter.ToInt32(BitConverter.GetBytes(v), 0)); }

        public Writer Str(string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s ?? "");
            var n = Math.Min(bytes.Length, 255);
            _b.Add((byte)n);
            for (var k = 0; k < n; k++) _b.Add(bytes[k]);
            return this;
        }

        /// <summary>Any number of raw bytes (a whole game state), with a 4-byte length first.</summary>
        public Writer BigBytes(byte[] bytes)
        {
            var n = bytes == null ? 0 : bytes.Length;
            I32(n);
            for (var k = 0; k < n; k++) _b.Add(bytes[k]);
            return this;
        }

        /// <summary>Up to 255 raw bytes, with their length first.</summary>
        public Writer Bytes(byte[] bytes)
        {
            var n = bytes == null ? 0 : Math.Min(bytes.Length, 255);
            _b.Add((byte)n);
            for (var k = 0; k < n; k++) _b.Add(bytes[k]);
            return this;
        }

        public Writer Input(PlayerInput i)
        {
            _b.Add((byte)i.MoveX); _b.Add((byte)i.MoveY); _b.Add((byte)i.AimX); _b.Add((byte)i.AimY);
            _b.Add(i.Buttons); _b.Add(i.Pick); _b.Add(i.Hover);
            return this;
        }

        public int Length { get { return _b.Count; } }
        public byte[] ToArray() { return _b.ToArray(); }
    }

    /// <summary>Reads a message written by <see cref="Writer"/>. Reading past the end throws, so a damaged packet is simply dropped.</summary>
    public sealed class Reader
    {
        private readonly byte[] _b;
        private int _i;

        public Reader(byte[] data) { _b = data; _i = 1; Type = data.Length > 0 ? (Msg)data[0] : 0; }

        public readonly Msg Type;
        public bool More { get { return _i < _b.Length; } }

        private void Need(int n) { if (_i + n > _b.Length) throw new FormatException("short packet"); }

        public int U8() { Need(1); return _b[_i++]; }
        public sbyte S8() { Need(1); return (sbyte)_b[_i++]; }
        public byte[] BigBytes() { var n = I32(); if (n < 0) throw new FormatException("bad length"); Need(n); var b = new byte[n]; Array.Copy(_b, _i, b, 0, n); _i += n; return b; }
        public byte[] Bytes() { var n = U8(); Need(n); var b = new byte[n]; Array.Copy(_b, _i, b, 0, n); _i += n; return b; }
        public int I32() { Need(4); var v = _b[_i] | (_b[_i + 1] << 8) | (_b[_i + 2] << 16) | (_b[_i + 3] << 24); _i += 4; return v; }
        public uint U32() { return (uint)I32(); }
        public ulong U64() { Need(8); ulong v = 0; for (var k = 0; k < 8; k++) v |= (ulong)_b[_i + k] << (8 * k); _i += 8; return v; }
        public float F32() { return BitConverter.ToSingle(BitConverter.GetBytes(I32()), 0); }

        public string Str()
        {
            var n = U8();
            Need(n);
            var s = Encoding.UTF8.GetString(_b, _i, n);
            _i += n;
            return s;
        }

        public PlayerInput Input()
        {
            Need(7);
            var i = new PlayerInput
            {
                MoveX = (sbyte)_b[_i], MoveY = (sbyte)_b[_i + 1], AimX = (sbyte)_b[_i + 2], AimY = (sbyte)_b[_i + 3],
                Buttons = _b[_i + 4], Pick = _b[_i + 5], Hover = _b[_i + 6],
            };
            _i += 7;
            return i;
        }
    }
}
