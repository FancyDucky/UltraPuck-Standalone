using System.Collections.Generic;
using UltraPuck.Core;
using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// The last few seconds of play, kept so a goal can be shown again. <see cref="Record"/> takes a copy of the game
    /// every tick (online, every frame: a few ticks may be missing, which playback smooths over). If the game rewinds
    /// (online, a late input), the copies from the rewound ticks are replaced as it plays forward again.
    /// </summary>
    internal sealed class ReplayBuffer
    {
        public const float Seconds = 10f;
        private readonly List<GameState> _states = new List<GameState>();
        private readonly int _keep;

        public ReplayBuffer(int ticksPerSecond) { _keep = Mathf.CeilToInt(Seconds * ticksPerSecond) + 30; }

        public void Clear() { _states.Clear(); }

        public void Record(GameState s)
        {
            if (s == null) return;
            while (_states.Count > 0 && _states[_states.Count - 1].Tick >= s.Tick) _states.RemoveAt(_states.Count - 1);
            _states.Add(s.Clone());
            var drop = 0;
            while (drop < _states.Count && _states[_states.Count - 1].Tick - _states[drop].Tick > _keep) drop++;
            if (drop > 0) _states.RemoveRange(0, drop);
        }

        /// <summary>The states from up to <see cref="Seconds"/> before <paramref name="tick"/> to it.</summary>
        public List<GameState> Before(int tick, int ticksPerSecond)
        {
            var from = tick - Mathf.CeilToInt(Seconds * ticksPerSecond);
            var list = new List<GameState>();
            foreach (var s in _states) if (s.Tick >= from && s.Tick <= tick) list.Add(s);
            return list;
        }
    }

    /// <summary>
    /// A goal shown again: up to ten seconds of play leading to it, in slow motion around shots, when the puck gets
    /// near a defender, and for the last moment before it goes in. Fits into the time it's given (the celebration,
    /// the same on every PC) by starting later if it has to.
    /// </summary>
    internal sealed class GoalReplay
    {
        private readonly List<GameState> _frames;
        private readonly float[] _speed;
        private float _at;   // position in ticks from the first frame
        private readonly int _first, _last;
        public bool Done { get; private set; }
        public float Slow { get; private set; }

        public const float SlowSpeed = 0.3f;

        public GoalReplay(List<GameState> frames, int scorer, Simulation sim, float budget)
        {
            _frames = frames;
            if (frames.Count < 2) { Done = true; return; }
            _first = frames[0].Tick;
            _last = frames[frames.Count - 1].Tick;
            var n = _last - _first + 1;
            var slow = new bool[n];
            var tps = sim.T.TicksPerSecond;

            // slow motion: around shots, when the puck is near a defender, and the last moment before the goal
            for (var k = 1; k < frames.Count; k++)
            {
                var a = frames[k - 1];
                var b = frames[k];
                var shot = a.Carrier >= 0 && b.Carrier < 0 && b.PassFrom >= 0 && (b.PuckVel - a.PuckVel).Length > Fix.FromInt(10);
                if (shot) Mark(slow, b.Tick - _first - tps / 4, b.Tick - _first + tps / 2);
                if (b.Carrier < 0 && b.Phase == Phase.Play)
                    foreach (var p in b.Players)
                    {
                        if (p.Team == scorer) continue;
                        var r = sim.RadiusOf(p) + sim.T.PuckRadius + Fix.FromInt(2);
                        if ((b.PuckPos - p.Pos).SqrLength < r * r) { Mark(slow, b.Tick - _first - tps / 10, b.Tick - _first + tps / 5); break; }
                    }
            }
            var goalAt = n - 1;
            for (var k = 0; k < frames.Count; k++) if (frames[k].Phase == Phase.Scored) { goalAt = frames[k].Tick - _first; break; }
            Mark(slow, goalAt - (int)(tps * 0.6f), goalAt);

            // a speed for every tick, eased in and out of slow motion
            _speed = new float[n];
            var sp = 1f;
            for (var k = 0; k < n; k++)
            {
                sp = Mathf.MoveTowards(sp, slow[k] ? SlowSpeed : 1f, 0.03f);
                _speed[k] = sp;
            }

            // too long for the time there is: start later
            var total = 0f;
            for (var k = 0; k < n; k++) total += 1f / (tps * _speed[k]);
            var start = 0;
            while (total > budget && start < n - tps)
            {
                total -= 1f / (tps * _speed[start]);
                start++;
            }
            _at = start;
        }

        private static void Mark(bool[] slow, int from, int to)
        {
            for (var k = Mathf.Max(0, from); k <= to && k < slow.Length; k++) slow[k] = true;
        }

        /// <summary>Move along by <paramref name="dt"/> seconds of real time; the two frames to draw between, and how far between them.</summary>
        public void Advance(float dt, int tps, out GameState a, out GameState b, out float t)
        {
            a = b = null; t = 0f;
            if (Done || _frames.Count == 0) { Done = true; return; }
            var k = Mathf.Clamp((int)_at, 0, _speed.Length - 1);
            Slow = _speed[k];
            _at += dt * tps * _speed[k];
            var tick = _first + _at;
            if (tick >= _last) { Done = true; a = b = _frames[_frames.Count - 1]; return; }
            // the recorded frames either side of this moment (online a few ticks may be missing)
            var hi = 1;
            while (hi < _frames.Count - 1 && _frames[hi].Tick < tick) hi++;
            a = _frames[hi - 1];
            b = _frames[hi];
            t = b.Tick == a.Tick ? 1f : Mathf.Clamp01((tick - a.Tick) / (b.Tick - a.Tick));
        }
    }
}
