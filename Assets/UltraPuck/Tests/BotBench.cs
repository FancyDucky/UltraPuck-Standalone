using System.IO;
using NUnit.Framework;
using UltraPuck.Core;
using UnityEngine;

namespace UltraPuck.Tests
{
    /// <summary>
    /// The bot-vs-bot scoreboard: all-bot matches with the newer bot brain on one team and the one before on the other
    /// (<see cref="Simulation.BotBrain"/>), both ways round, on a few rinks and team sizes, no cards. Prints wins and
    /// goals, so a change to the bots can be measured instead of judged by feel. Run on purpose (it takes a minute or
    /// two): -testFilter UltraPuck.Tests.BotBench. Set ULTRAPUCK_BENCH_SEEDS for more matches each (default 4).
    /// </summary>
    [TestFixture, Explicit("the bot-vs-bot scoreboard: run on purpose")]
    public class BotBench
    {
        private static MapDef Map(string file)
        {
            return MapDef.FromJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Maps", file)));
        }

        /// <summary>One match to 5 goals (8 minutes at most): the new brain's goals, the old brain's goals.</summary>
        private static int[] Play(MapDef map, int perTeam, uint seed, int newTeam)
        {
            var sim = new Simulation(map, new Tuning { GoalsToWin = 5, TimeLimit = Fix.Zero, CardsAfterGoals = false }, null);
            sim.BotBrain[newTeam] = 1;
            sim.BotBrain[1 - newTeam] = 0;
            var teams = new int[perTeam * 2];
            var bots = new bool[perTeam * 2];
            for (var k = 0; k < teams.Length; k++) { teams[k] = k % 2; bots[k] = true; }
            var s = sim.NewGame(teams, seed, bots);
            var none = new PlayerInput[teams.Length];
            for (var t = 0; t < sim.T.TicksPerSecond * 60 * 8 && s.Phase != Phase.GameOver; t++) sim.Step(s, sim.Route(s, none));
            return new[] { s.Score[newTeam], s.Score[1 - newTeam] };
        }

        [Test]
        public void NewBotsAgainstOld()
        {
            int seeds;
            if (!int.TryParse(System.Environment.GetEnvironmentVariable("ULTRAPUCK_BENCH_SEEDS"), out seeds) || seeds < 1) seeds = 4;
            int wins = 0, losses = 0, draws = 0, gf = 0, ga = 0;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (var file in new[] { "test-rink.json", "hockey-rink.json" })
            {
                var map = Map(file);
                for (var size = 1; size <= 3; size++)
                {
                    int w = 0, l = 0, d = 0, f = 0, a = 0;
                    for (var seed = 1; seed <= seeds; seed++)
                        for (var side = 0; side < 2; side++)
                        {
                            var r = Play(map, size, (uint)(seed * 7919 + size * 31), side);
                            f += r[0]; a += r[1];
                            if (r[0] > r[1]) w++; else if (r[0] < r[1]) l++; else d++;
                        }
                    TestContext.WriteLine(file + " " + size + "v" + size + ": new won " + w + ", lost " + l + ", drew " + d + "; goals " + f + " - " + a);
                    wins += w; losses += l; draws += d; gf += f; ga += a;
                }
            }
            var line = "BOTBENCH new brain vs old: won " + wins + ", lost " + losses + ", drew " + draws + "; goals " + gf + " - " + ga
                + " (" + clock.Elapsed.TotalSeconds.ToString("0") + " s)";
            TestContext.WriteLine(line);
            Debug.Log(line);
            Assert.Pass(line);
        }
    }
}
