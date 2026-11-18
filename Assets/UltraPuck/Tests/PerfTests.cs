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
