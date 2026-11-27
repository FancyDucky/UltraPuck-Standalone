using NUnit.Framework;
using UltraPuck.Core;

namespace UltraPuck.Tests
{
    /// <summary>The rink maker's rinks: they turn into maps the game plays on, open again in the maker, and stay fair.</summary>
    public class RinkPlanTests
    {
        private static Simulation Play(MapDef map, int ticks, int seed)
        {
            var sim = new Simulation(map, Tuning.ForFreePlay());
            var s = sim.NewGame(new[] { 0, 1, 0, 1 }, (uint)seed | 1u, new[] { true, true, true, true });
            for (var t = 0; t < ticks; t++)
            {
                var inputs = new PlayerInput[s.Players.Length];
                for (var i = 0; i < inputs.Length; i++) inputs[i] = Bot.Think(sim, s, i);
                sim.Step(s, inputs);
            }
            return sim;
        }

        [Test]
        public void TheStarterRinkIsAPlayableMap()
        {
            var p = RinkPlan.Starter();
            Assert.IsNull(p.Problem());
            var map = MapDef.FromJson(p.ToJson());
            Assert.AreEqual(2, map.Goals.Count);
            Assert.AreEqual(8, map.Spawns.Count);
            Assert.IsTrue(map.WallClosed[0], "the outline comes first");
            foreach (var s in map.Spawns) Assert.IsTrue(map.Inside(s.Pos), "every spawn is on the ice");
            Assert.IsTrue(map.Inside(map.PuckSpot));
            Assert.AreEqual(0, map.Route.Count, "an open rink needs no route");
            Play(map, 120 * 20, 7);
        }

        [Test]
        public void AMirroredRinkIsTheSameOnBothSides()
        {
            var map = MapDef.FromJson(RinkPlan.Starter().ToJson());
            // every outline point has its mirror image across x = 0
            var pts = map.WallLines[0];
            foreach (var a in pts)
            {
                var found = false;
                foreach (var b in pts) if (b.X.Raw == -a.X.Raw && b.Y.Raw == a.Y.Raw) { found = true; break; }
                Assert.IsTrue(found, "no mirror for " + a);
            }
            for (var k = 0; k < 4; k++)
            {
                SpawnDef red = map.SpawnFor(0, k), blue = map.SpawnFor(1, k);
                Assert.AreEqual(-red.Pos.X.Raw, blue.Pos.X.Raw);
                Assert.AreEqual(red.Pos.Y.Raw, blue.Pos.Y.Raw);
            }
        }

        [Test]
        public void ItOpensAgainInTheMaker()
        {
            var p = RinkPlan.Starter();
            p.Name = "Back \"Yard\"";
            p.SetIce(40, 20, true); // a stray square, not joined on
            p.SpotPlaced = true; p.SpotX = 0; p.SpotY = 2;
            var back = RinkPlan.FromJson(p.ToJson());
            Assert.IsNotNull(back);
            Assert.IsTrue(back.SameAs(p));
            Assert.AreEqual("Back \"Yard\"", back.Name);
        }

        [Test]
        public void AnUnfinishedRinkSavesButIsntAMap()
        {
            var p = RinkPlan.Starter();
            p.Nets[1].Placed = false;
            Assert.AreEqual("Place the blue net", p.Problem());
            Assert.IsNull(p.ToJson());
            var file = p.ToFile();
            Assert.IsTrue(RinkPlan.FromJson(file).SameAs(p));
            Assert.Throws<System.FormatException>(() => MapDef.FromJson(file));
        }

        [Test]
        public void OnlyTheBiggestPatchOfIceCounts()
        {
            var p = RinkPlan.Starter();
            for (var cy = 15; cy < 20; cy++) for (var cx = -5; cx < 5; cx++) p.SetIce(cx, cy, true); // an island above the rink
            var kept = p.Kept();
            Assert.IsTrue(RinkPlan.Stray(p, kept, 0, 17));
            var map = MapDef.FromJson(p.ToJson());
            Assert.AreEqual(1, CountClosed(map), "the island has no walls");
        }

        [Test]
        public void AHoleInTheIceIsAPillar()
        {
            var p = RinkPlan.Starter();
            for (var cy = 5; cy < 8; cy++) for (var cx = -2; cx < 2; cx++) p.SetIce(cx, cy, false);
            var map = MapDef.FromJson(p.ToJson());
            Assert.AreEqual(2, CountClosed(map));
            Assert.IsFalse(map.Inside(new FixVec2(Fix.FromInt(30), Fix.Zero)), "beyond the outline");
            Play(map, 120 * 10, 3);
        }

        [Test]
        public void NetsNeedIceAndCantBeErasedFromUnder()
        {
            var p = RinkPlan.Starter();
            var n = p.Nets[0];
            Assert.IsTrue(p.UnderNet(n.X - 1, n.Y), "the net's own squares");
            Assert.IsTrue(p.UnderNet(n.X, n.Y), "the row in front of its mouth");
            var off = new RinkPlan.Net { Placed = true, X = -45, Y = 0, Dir = 0 }; // off the ice
            Assert.IsFalse(p.NetFits(off, p.Nets[1]));
            Assert.IsTrue(p.NetFits(n, p.Nets[1]));
            var mirror = RinkPlan.Mirror(p.Nets[1]);
            Assert.AreEqual(n.X, mirror.X, "the mirror of blue's net is red's");
            Assert.AreEqual(n.Y, mirror.Y);
            Assert.AreEqual(n.Dir, mirror.Dir);
        }

        [Test]
        public void ABentRinkGetsARouteForTheBots()
        {
            // a U: two arms joined along the bottom, a net at the top of each arm facing down
            var p = new RinkPlan();
            for (var cy = -14; cy < 14; cy++)
                for (var cx = -24; cx < 24; cx++)
                {
                    var arm = (cx < -8 || cx >= 8) && cx >= -24 && cx < 24;
                    var bottom = cy < -4;
                    if (arm || bottom) p.SetIce(cx, cy, true);
                }
            p.Nets[0] = new RinkPlan.Net { Placed = true, X = -16, Y = 10, Dir = 3 };
            p.Nets[1] = new RinkPlan.Net { Placed = true, X = 16, Y = 10, Dir = 3 };
            p.SpotPlaced = true; p.SpotX = 0; p.SpotY = -9;
            Assert.IsNull(p.Problem());
            var map = MapDef.FromJson(p.ToJson());
            Assert.GreaterOrEqual(map.Route.Count, 3);
            Play(map, 120 * 15, 11);
        }

        [Test]
        public void StepsTraceAsSlopesNotStairs()
        {
            // a diagonal edge painted in single steps: the traced wall doesn't zig-zag a whole square
            var p = new RinkPlan();
            for (var cy = -10; cy < 10; cy++) for (var cx = -20; cx < 20 - (cy + 10); cx++) p.SetIce(cx, cy, true);
            var loops = RinkPlan.Outline(p.Kept());
            Assert.AreEqual(1, loops.Count);
            foreach (var q in loops[0])
            {
                // on the slope (its steps run between x + y = 10 and 11), every point lies near the middle of them
                if (q[1] > -7 && q[1] < 7 && q[0] > -14 && q[0] + q[1] > 5) Assert.Less(System.Math.Abs(q[0] + q[1] - 10.5), 0.45, "point " + q[0] + ", " + q[1]);
            }
        }

        [Test]
        public void WallLinesBecomeWallsAndOpenAgain()
        {
            var p = RinkPlan.Starter();
            // a mirrored pair of short walls across the ice, and one up the middle line
            for (var x = -12; x < -8; x++) { p.SetWall(x, 5, false, true); p.SetWall(RinkPlan.MirrorEdge(x, 5, false)[0], 5, false, true); }
            for (var y = -9; y < -6; y++) p.SetWall(0, y, true, true);
            Assert.IsNull(p.Problem());
            var map = MapDef.FromJson(p.ToJson());
            var open = 0;
            foreach (var c in map.WallClosed) if (!c) open++;
            Assert.AreEqual(2 + 3, open, "the two nets and three wall runs");
            Assert.IsTrue(RinkPlan.FromJson(p.ToJson()).SameAs(p));
            Play(map, 120 * 10, 5);
        }

        [Test]
        public void WallsMustLeaveAWayBetweenTheNets()
        {
            var p = RinkPlan.Starter();
            p.SpotPlaced = true; p.SpotX = -4; p.SpotY = 0; // (off the middle line, where the wall goes)
            for (var y = -13; y < 13; y++) p.SetWall(0, y, true, true); // right across the rink
            Assert.AreEqual("Walls shut one net off from the other", p.Problem());
            for (var y = 6; y < 10; y++) p.SetWall(0, y, true, false); // a gap, off to one side
            Assert.IsNull(p.Problem());
            var map = MapDef.FromJson(p.ToJson());
            Assert.GreaterOrEqual(map.Route.Count, 3, "the bots get a way round, through the gap");
            Play(map, 120 * 10, 9);
        }

        [Test]
        public void WallsStayOffTheNetsAndTheIce()
        {
            var p = RinkPlan.Starter();
            var n = p.Nets[0];
            Assert.IsFalse(p.WallFits(n.X - 1, n.Y, true), "not through a net");
            Assert.IsFalse(p.WallFits(-45, 0, true), "not out where there's no ice");
            Assert.IsTrue(p.WallFits(-5, 5, false));
            p.SetWall(-15, 5, false, true);
            Assert.IsTrue(p.NetFits(n, p.Nets[1]));
            Assert.IsFalse(p.NetFits(new RinkPlan.Net { Placed = true, X = -12, Y = 6, Dir = 0 }, p.Nets[1]), "a net can't go over a wall");
        }

        private static int CountClosed(MapDef map)
        {
            var n = 0;
            foreach (var c in map.WallClosed) if (c) n++;
            return n;
        }
    }
}
