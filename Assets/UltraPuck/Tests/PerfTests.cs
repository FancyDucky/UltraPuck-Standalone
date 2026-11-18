using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using UltraPuck.Core;
using UltraPuck.Core.Net;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UltraPuck.Tests
{
    /// <summary>
    /// How much a rollback game costs: the ticks a second a PC can simulate (with the state saved every tick, as
    /// rollback does). Online, every late input re-runs the game from that tick, so a 6-player game can need many
    /// times 120 ticks a second. Run on purpose: -testFilter UltraPuck.Tests.PerfTests
    /// </summary>
    [Explicit]
    public class PerfTests
    {
        private static MapDef Map(string file)
        {
            return MapDef.FromJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Maps", file)));
        }

        /// <summary>
        /// 1-on-1 bot matches, both ways round, 60 each: the goals should split evenly by side and by player slot. (A
        /// rule that settles things in player order, rather than fairly, shows up here as one slot winning far more.)
        /// </summary>
        [TestCase("test-rink.json")]
        [TestCase("hockey-rink.json")]
        public void OneOnOneSlotsAreFair(string file)
        {
            var sim = new Simulation(Map(file));
            var slot = new int[2];
            foreach (var lineup in new[] { new[] { 0, 1 }, new[] { 1, 0 } })
            {
                for (var k = 0; k < 60; k++)
                {
                    var s = sim.NewGame(lineup);
                    s.Rng = 0x9E3779B9u + (uint)k * 104729u;
                    var inputs = new PlayerInput[2];
                    for (var t = 0; t < 150 * 120 && s.Phase != Phase.GameOver; t++)
                    {
                        for (var i = 0; i < 2; i++) inputs[i] = Bot.Think(sim, s, i);
                        sim.Step(s, inputs);
                    }
                    slot[0] += s.Score[lineup[0]]; slot[1] += s.Score[lineup[1]];
                }
            }
            Debug.Log("PERF 1v1 " + file + ": slot 0 scored " + slot[0] + ", slot 1 " + slot[1]);
            var share = slot[0] / (double)(slot[0] + slot[1]);
            Assert.That(share, Is.InRange(0.42, 0.58), "slot 0's share of the goals");
        }

        [TestCase("hockey-rink.json", 6)]
        [TestCase("hockey-rink.json", 8)]
        [TestCase("s-rink.json", 6)]
        public void TicksPerSecond(string file, int players)
        {
            var sim = new Simulation(Map(file));
            var teams = new int[players];
            for (var k = 0; k < players; k++) teams[k] = k & 1;
            var s = sim.NewGame(teams, 12345u);
            var inputs = new PlayerInput[players];
            // warm up, then time ticks the way rollback runs them: save a copy, think for bots, step
            for (var t = 0; t < 600; t++) { for (var i = 0; i < players; i++) inputs[i] = Bot.Think(sim, s, i); sim.Step(s, inputs); }
            const int n = 6000;
            var clock = Stopwatch.StartNew();
            for (var t = 0; t < n; t++)
            {
                var saved = s.Clone();
                for (var i = 0; i < players; i++) inputs[i] = Bot.Think(sim, s, i);
                sim.Step(s, inputs);
            }
            var secs = clock.Elapsed.TotalSeconds;
            Debug.Log("PERF " + file + " " + players + " players: " + Mathf.RoundToInt((float)(n / secs)) + " ticks/s (" + (secs * 1e6 / n).ToString("0.0") + " us a tick)");
            clock = Stopwatch.StartNew();
            for (var t = 0; t < n; t++) for (var i = 0; i < players; i++) inputs[i] = Bot.Think(sim, s, i);
            Debug.Log("PERF   bots thinking alone: " + (clock.Elapsed.TotalSeconds * 1e6 / n).ToString("0.0") + " us");
            clock = Stopwatch.StartNew();
            for (var t = 0; t < n; t++) sim.Step(s, inputs);
            Debug.Log("PERF   step alone: " + (clock.Elapsed.TotalSeconds * 1e6 / n).ToString("0.0") + " us");
            clock = Stopwatch.StartNew();
            for (var t = 0; t < n; t++) s.Clone();
            Debug.Log("PERF   clone alone: " + (clock.Elapsed.TotalSeconds * 1e6 / n).ToString("0.0") + " us");
            clock = Stopwatch.StartNew();
            for (var t = 0; t < n; t++) s.Checksum();
            Debug.Log("PERF   checksum: " + (clock.Elapsed.TotalSeconds * 1e6 / n).ToString("0.0") + " us");
        }
    }
}
