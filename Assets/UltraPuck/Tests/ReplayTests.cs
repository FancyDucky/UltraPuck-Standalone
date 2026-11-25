using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UltraPuck.Core;
using UltraPuck.Game;
using UnityEngine;

namespace UltraPuck.Tests
{
    /// <summary>Saved match replays: what's written comes back the same, and replays from other versions still read.</summary>
    public class ReplayTests
    {
        private static MapDef LoadRink()
        {
            return MapDef.FromJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Maps", "test-rink.json")));
        }

        private static PlayerInput[] Inputs(int tick, int players)
        {
            var r = new System.Random(7 * 100003 + tick / 6);
            var a = new PlayerInput[players];
            for (var i = 0; i < players; i++)
                a[i] = new PlayerInput { MoveX = (sbyte)r.Next(-127, 128), MoveY = (sbyte)r.Next(-127, 128), AimY = (sbyte)r.Next(-127, 128), Buttons = (byte)r.Next(0, 8) };
            return a;
        }

        /// <summary>A few seconds of a 4-player game, recorded; and the states it went through, by tick.</summary>
        private static MatchReplay Record(bool threaded, Dictionary<int, ulong> sums, int ticks = 900)
        {
            var sim = new Simulation(LoadRink());
            var s = sim.NewGame(new[] { 0, 1, 0, 1 });
            s.Phase = Phase.Play; s.PhaseTime = Fix.Zero;
            var rec = new MatchReplay.Recorder(new MatchReplay { MapFile = "test-rink.json", MapName = "Test Rink", Rules = new byte[] { 1, 2, 3 } }, threaded);
            for (var t = 0; t < ticks; t++)
            {
                sim.Step(s, Inputs(t, 4));
                var cast = new MatchReplay.Cast { Names = new[] { "Ann", "Bot", "Cy", "Bot" }, Symbols = new[] { 0, 1, 2, 3 }, Styles = new[] { Cosmetics.Pack(2, 3, 4), 0, 0, 0 } };
                if (rec.Add(s, cast)) sums[s.Tick] = s.Checksum();
            }
            return rec.Finish();
        }

        [Test]
        public void ARecordedMatchPlaysBackExactly([Values(false, true)] bool threaded)
        {
            var sums = new Dictionary<int, ulong>();
            var rep = Record(threaded, sums);
            Assert.AreEqual(sums.Count, rep.Frames);
            for (var f = 0; f < rep.Frames; f++)
                Assert.AreEqual(sums[rep.Ticks[f]], rep.FrameState(f).Checksum(), "frame " + f);
        }

        [Test]
        public void ScrubbingReadsTheSameFramesAsPlaying()
        {
            var sums = new Dictionary<int, ulong>();
            var rep = Record(false, sums);
            // jumping about, as dragging along the bar does (each one usually in a chunk not decoded yet)
            var rng = new System.Random(3);
            for (var n = 0; n < 200; n++)
            {
                var f = rng.Next(rep.Frames);
                Assert.AreEqual(sums[rep.Ticks[f]], rep.FrameStateQuick(f).Checksum(), "frame " + f);
            }
            Assert.AreEqual(sums[rep.Ticks[rep.Frames - 1]], rep.FrameStateQuick(rep.Frames - 1).Checksum(), "the last frame");
        }

        [Test]
        public void ASavedReplayLoadsTheSame()
        {
            var sums = new Dictionary<int, ulong>();
            var rep = Record(true, sums);
            var path = Path.Combine(Path.GetTempPath(), "ultrapuck-test" + MatchReplay.Extension);
            try
            {
                rep.Save(path);
                var info = MatchReplay.LoadInfo(path);
                Assert.AreEqual("Test Rink", info.MapName);
                Assert.AreEqual(1, info.Casts.Count, "the cast didn't change, so it's stored once");
                Assert.AreEqual(Cosmetics.Pack(2, 3, 4), info.Casts[0].Styles[0]);
                Assert.AreEqual(2, Cosmetics.Dash(info.Casts[0].Styles[0]));
                Assert.AreEqual(3, Cosmetics.Banner(info.Casts[0].Styles[0]));
                Assert.AreEqual(4, Cosmetics.Boom(info.Casts[0].Styles[0]));
                var back = MatchReplay.Load(path);
                Assert.AreEqual(rep.Frames, back.Frames);
                CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, back.Rules);
                for (var f = 0; f < back.Frames; f += 7) Assert.AreEqual(sums[back.Ticks[f]], back.FrameState(f).Checksum(), "frame " + f);
                // a ten-minute match should be a few megabytes at most
                var perMinute = new FileInfo(path).Length / (back.Seconds / 60f);
                Assert.Less(perMinute, 1.5f * 1024 * 1024, "bytes a minute");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Test]
        public void GoalsAndSavesAreMarked()
        {
            var sim = new Simulation(LoadRink());
            var s = sim.NewGame(new[] { 0, 1 });
            var rec = new MatchReplay.Recorder(new MatchReplay(), false);
            rec.Add(s, null);
            s.Tick += 2; s.Score[1]++; s.LastScorer = 1; s.LastGoalBy = 1;
            rec.Add(s, null);
            s.Tick += 2; s.Players[0].Saves++;
            rec.Add(s, null);
            var rep = rec.Finish();
            Assert.AreEqual(2, rep.Markers.Count);
            Assert.AreEqual(MatchReplay.MarkerKind.Goal, rep.Markers[0].Kind);
            Assert.AreEqual(1, rep.Markers[0].Team);
            Assert.AreEqual(1, rep.Markers[0].Frame);
            Assert.AreEqual(MatchReplay.MarkerKind.Save, rep.Markers[1].Kind);
            Assert.AreEqual(0, rep.Markers[1].Player);
        }

        /// <summary>
        /// A replay from a version with a field this one doesn't have (here: Touch1 under another name) still reads:
        /// that field is dropped, this version's field keeps its starting value, and everything else is right.
        /// </summary>
        [Test]
        public void AReplayFromAnotherVersionStillReads()
        {
            var sums = new Dictionary<int, ulong>();
            var rep = Record(false, sums, 240);
            var path = Path.Combine(Path.GetTempPath(), "ultrapuck-test-old" + MatchReplay.Extension);
            try
            {
                rep.Save(path);
                var bytes = File.ReadAllBytes(path);
                var from = Encoding.UTF8.GetBytes("Touch1");
                var to = Encoding.UTF8.GetBytes("Touch9");
                var found = 0;
                for (var k = 0; k + from.Length <= bytes.Length; k++)
                {
                    var same = bytes[k - 1 < 0 ? 0 : k - 1] == from.Length; // a whole name (its length byte before it)
                    for (var j = 0; same && j < from.Length; j++) same = bytes[k + j] == from[j];
                    if (!same) continue;
                    for (var j = 0; j < to.Length; j++) bytes[k + j] = to[j];
                    found++;
                }
                Assert.AreEqual(1, found, "the field's name is in the description once");
                File.WriteAllBytes(path, bytes);
                var back = MatchReplay.Load(path);
                var f = back.Frames - 1;
                var got = back.FrameState(f);
                var want = rep.FrameState(f);
                Assert.AreEqual(-1, got.Touch1, "a field the replay doesn't have keeps its starting value");
                got.Touch1 = want.Touch1;
                Assert.AreEqual(want.Checksum(), got.Checksum(), "everything else");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        [Test]
        public void LooksPackAndUnpack()
        {
            var v = Cosmetics.Pack(Cosmetics.DashNames.Length - 1, Cosmetics.BannerNames.Length - 1, Cosmetics.BoomNames.Length - 1);
            Assert.AreEqual(Cosmetics.DashNames.Length - 1, Cosmetics.Dash(v));
            Assert.AreEqual(Cosmetics.BannerNames.Length - 1, Cosmetics.Banner(v));
            Assert.AreEqual(Cosmetics.BoomNames.Length - 1, Cosmetics.Boom(v));
            Assert.AreEqual(0, Cosmetics.Dash(Cosmetics.Pack(200, 0, 0)), "a look this version doesn't know is the plain one");
        }
    }
}
