using System.IO;
using NUnit.Framework;
using UltraPuck.Core;
using UnityEngine;

namespace UltraPuck.Tests
{
    public class CoreTests
    {
        private static MapDef LoadRink()
        {
            return MapDef.FromJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Maps", "test-rink.json")));
        }

        /// <summary>Random but repeatable controls for <paramref name="players"/> players at <paramref name="tick"/>.</summary>
        private static PlayerInput[] Inputs(int seed, int tick, int players)
        {
            var r = new System.Random(seed * 100003 + tick / 6); // hold each input for a few ticks, like a person
            var a = new PlayerInput[players];
            for (var i = 0; i < players; i++)
                a[i] = new PlayerInput
                {
                    MoveX = (sbyte)r.Next(-127, 128), MoveY = (sbyte)r.Next(-127, 128),
                    AimX = r.Next(3) == 0 ? (sbyte)r.Next(-127, 128) : (sbyte)0, AimY = (sbyte)r.Next(-127, 128),
                    Buttons = (byte)r.Next(0, 8),
                };
            return a;
        }

        private static void PlayNow(GameState s) { s.Phase = Phase.Play; s.PhaseTime = Fix.Zero; }

        [Test]
        public void MapLoads()
        {
            var m = LoadRink();
            Assert.AreEqual("Test Rink", m.Name);
            Assert.Greater(m.Segments.Count, 10);
            Assert.AreEqual(2, m.Goals.Count);
        }

        [Test]
        public void FixedPointMaths()
        {
            Assert.AreEqual(Fix.FromInt(3), Fix.Sqrt(Fix.FromInt(9)));
            Assert.AreEqual(Fix.Ratio(3, 2), Fix.Parse("1.5"));
            Assert.AreEqual(Fix.FromInt(-6), Fix.FromInt(2) * Fix.FromInt(-3));
            Assert.AreEqual(Fix.Ratio(1, 4), Fix.One / Fix.FromInt(4));
            var n = new FixVec2(Fix.FromInt(3), Fix.FromInt(4)).Normalized;
            Assert.AreEqual(0.6, n.X.ToDouble(), 1e-4);
        }

        /// <summary>Two separate runs with the same inputs must agree on every tick (what online play needs).</summary>
        [Test]
        public void SameInputsSameResult()
        {
            var map = LoadRink();
            var teams = new[] { 0, 1, 0, 1 };
            Simulation a = new Simulation(map), b = new Simulation(LoadRink());
            GameState sa = a.NewGame(teams), sb = b.NewGame(teams);
            var played = 0;
            for (var t = 0; t < 6000; t++)
            {
                var input = Inputs(7, t, teams.Length);
                a.Step(sa, input);
                b.Step(sb, input);
                if (sa.Phase == Phase.Play) played++;
                Assert.AreEqual(sa.Checksum(), sb.Checksum(), "drifted apart at tick " + t);
            }
            Assert.Greater(played, 1000, "the game actually ran");
        }

        /// <summary>Save, play on, go back to the save and replay: the result must be identical (rollback).</summary>
        [Test]
        public void RollbackReplaysExactly()
        {
            var sim = new Simulation(LoadRink());
            var teams = new[] { 0, 1, 1 };
            var s = sim.NewGame(teams);
            for (var t = 0; t < 150; t++) sim.Step(s, Inputs(3, t, 3));
            var saved = s.Clone();
            for (var t = 150; t < 400; t++) sim.Step(s, Inputs(3, t, 3));
            var expected = s.Checksum();

            // a different future first, to prove the save wasn't touched
            var other = saved.Clone();
            for (var t = 150; t < 400; t++) sim.Step(other, Inputs(99, t, 3));
            Assert.AreNotEqual(expected, other.Checksum());

            var replay = saved.Clone();
            for (var t = 150; t < 400; t++) sim.Step(replay, Inputs(3, t, 3));
            Assert.AreEqual(expected, replay.Checksum());
        }

        [Test]
        public void PuckInEachGoalScoresForTheOtherTeam()
        {
            var sim = new Simulation(LoadRink());
            for (var goalTeam = 0; goalTeam < 2; goalTeam++)
            {
                var s = sim.NewGame(new[] { 0, 1 });
                PlayNow(s);
                var dir = goalTeam == 1 ? 1 : -1; // team 1 defends the right goal
                s.PuckPos = new FixVec2(Fix.FromInt(13 * dir), Fix.Zero);
                s.PuckVel = new FixVec2(Fix.FromInt(20 * dir), Fix.Zero);
                for (var t = 0; t < 240 && s.Phase == Phase.Play; t++) sim.Step(s, null);
                Assert.AreEqual(Phase.Scored, s.Phase, "goal " + goalTeam);
                Assert.AreEqual(1, s.Score[1 - goalTeam], "goal " + goalTeam);
                Assert.AreEqual(0, s.Score[goalTeam], "goal " + goalTeam);

                for (var t = 0; t < 600 && s.Phase == Phase.Scored; t++) sim.Step(s, null);
                Assert.AreEqual(Phase.Countdown, s.Phase);
                Assert.AreEqual(sim.Map.PuckSpot, s.PuckPos);
                Assert.AreEqual(-1, s.Carrier);
            }
        }

        [Test]
        public void FirstToFiveWins()
        {
            var sim = new Simulation(LoadRink());
            var s = sim.NewGame(new[] { 0, 1 });
            s.Score[0] = 4;
            PlayNow(s);
            s.PuckPos = new FixVec2(Fix.FromInt(13), Fix.Zero);
            s.PuckVel = new FixVec2(Fix.FromInt(20), Fix.Zero);
            for (var t = 0; t < 900 && s.Phase != Phase.GameOver; t++) sim.Step(s, null);
            Assert.AreEqual(Phase.GameOver, s.Phase);
            Assert.AreEqual(0, s.LastScorer);
        }

        /// <summary>Free play: a goal counts, but there's no winner and only the puck goes back to its spot.</summary>
        [Test]
        public void FreePlayGoalsNeverEndTheGame()
        {
            var sim = new Simulation(LoadRink(), Tuning.ForFreePlay());
            var s = sim.NewGame(new[] { 0 });
            sim.Step(s, null); // no countdown in free play
            Assert.AreEqual(Phase.Play, s.Phase);
            s.Score[0] = 99;
            var spot = new FixVec2(Fix.FromInt(-5), Fix.FromInt(3));
            s.Players[0].Pos = spot;
            s.PuckPos = new FixVec2(Fix.FromInt(13), Fix.Zero);
            s.PuckVel = new FixVec2(Fix.FromInt(20), Fix.Zero);
            for (var t = 0; t < 240 && s.Phase == Phase.Play; t++) sim.Step(s, null);
            Assert.AreEqual(Phase.Scored, s.Phase);
            Assert.AreEqual(100, s.Score[0]);
            for (var t = 0; t < 600 && s.Phase == Phase.Scored; t++) sim.Step(s, null);
            Assert.AreEqual(Phase.Play, s.Phase);
            Assert.AreEqual(sim.Map.PuckSpot, s.PuckPos);
            Assert.AreEqual(spot, s.Players[0].Pos, "players stay where they are");
        }

        [Test]
        public void FreePlayCallsThePuckBack()
        {
            var sim = new Simulation(LoadRink(), Tuning.ForFreePlay());
            var s = sim.NewGame(new[] { 0 });
            sim.Step(s, null);
            s.PuckPos = new FixVec2(Fix.FromInt(10), Fix.FromInt(5));
            s.PuckVel = new FixVec2(Fix.FromInt(3), Fix.Zero);
            sim.Step(s, new[] { new PlayerInput { Buttons = PlayerInput.ResetPuck } });
            Assert.AreEqual(sim.Map.PuckSpot, s.PuckPos);
            Assert.AreEqual(FixVec2.Zero, s.PuckVel);

            // the same button does nothing in a normal match
            var match = new Simulation(LoadRink());
            var m = match.NewGame(new[] { 0 });
            PlayNow(m);
            var away = new FixVec2(Fix.FromInt(10), Fix.FromInt(5));
            m.PuckPos = away;
            match.Step(m, new[] { new PlayerInput { Buttons = PlayerInput.ResetPuck } });
            Assert.AreNotEqual(match.Map.PuckSpot, m.PuckPos);
        }

        [Test]
        public void PlayersCanJoinAndLeaveMidGame()
        {
            var sim = new Simulation(LoadRink(), Tuning.ForFreePlay());
            var s = sim.NewGame(new[] { 0 });
            for (var t = 0; t < 30; t++) sim.Step(s, null);
            var i = sim.AddPlayer(s, 1);
            Assert.AreEqual(1, i);
            Assert.AreEqual(2, s.Players.Length);
            Assert.AreEqual(1, s.Players[1].Team);
            s.Carrier = 1;
            for (var t = 0; t < 30; t++) sim.Step(s, new PlayerInput[2]);
            sim.RemovePlayer(s, 0);
            Assert.AreEqual(1, s.Players.Length);
            Assert.AreEqual(0, s.Carrier, "the carrier moved down a place with everyone else");
            sim.RemovePlayer(s, 0);
            Assert.AreEqual(-1, s.Carrier);
            for (var t = 0; t < 30; t++) sim.Step(s, null); // an empty rink still runs
        }

        /// <summary>Dashing into a wall over and over, and a puck at top speed, never end up outside the rink.</summary>
        [Test]
        public void NothingGoesThroughWalls()
        {
            var sim = new Simulation(LoadRink());
            var s = sim.NewGame(new[] { 0, 1 });
            PlayNow(s);
            s.PuckPos = FixVec2.Zero;
            s.PuckVel = new FixVec2(Fix.FromInt(28), Fix.FromInt(28)); // ~40, the cap, toward a corner
            s.Players[1].Pos = new FixVec2(Fix.FromInt(-2), Fix.FromInt(-6));
            var r = sim.RadiusOf(s.Players[0]).ToDouble();
            for (var t = 0; t < 600; t++)
            {
                // player 0 holds up and dashes whenever it can
                var up = new PlayerInput { MoveY = 127, Buttons = (byte)((t % 2 == 0) ? PlayerInput.ShootCheck : 0) };
                sim.Step(s, new[] { up, default(PlayerInput) });
                Assert.LessOrEqual(s.Players[0].Pos.Y.ToDouble() + r, sim.Map.BoundsMax.Y.ToDouble() + 0.02, "player through the top wall at tick " + t);
                Assert.LessOrEqual(System.Math.Abs(s.PuckPos.Y.ToDouble()), sim.Map.BoundsMax.Y.ToDouble(), "puck out at tick " + t);
                Assert.LessOrEqual(System.Math.Abs(s.PuckPos.X.ToDouble()), sim.Map.BoundsMax.X.ToDouble(), "puck out at tick " + t);
                if (s.Phase != Phase.Play) PlayNow(s);
            }
        }

        [Test]
        public void CheckingTheCarrierKnocksThePuckLoose()
        {
            var sim = new Simulation(LoadRink());
            var s = sim.NewGame(new[] { 0, 1 });
            PlayNow(s);
            s.Players[1].Pos = new FixVec2(Fix.FromInt(2), Fix.Zero);
            s.Players[1].Facing = new FixVec2(Fix.Zero, Fix.One);
            s.Carrier = 1;
            s.Players[0].Pos = new FixVec2(Fix.FromInt(-1), Fix.Zero);
            s.Players[0].Facing = FixVec2.Right;
            sim.Step(s, new[] { new PlayerInput { Buttons = PlayerInput.ShootCheck }, default(PlayerInput) });
            for (var t = 0; t < 60 && s.Carrier == 1; t++) sim.Step(s, new[] { new PlayerInput { MoveX = 127 }, default(PlayerInput) });
            Assert.AreEqual(-1, s.Carrier, "knocked loose: nobody has it");
            Assert.Greater(s.Players[0].NoPickup.ToDouble(), 0.0, "the checker can't just scoop it up in the same instant");
            Assert.Greater(s.Players[1].NoPickup.ToDouble(), 0.0, "the old carrier can't grab it straight back");
            Assert.Greater(s.Players[1].Stun.ToDouble(), 0.0, "a check dazes");
            Assert.Greater(s.Players[1].Hits, 0, "the hit was recorded for the animation");
            Assert.Greater(s.Players[0].Hits, 0, "the checker feels it too");
            Assert.AreEqual(1.0, s.Players[1].HitPower.ToDouble(), 1e-6);
        }

        [Test]
        public void ReachingTheCarriedPuckWithYourWedgeStealsIt()
        {
            var sim = new Simulation(LoadRink());
            var s = sim.NewGame(new[] { 0, 1 });
            PlayNow(s);
            s.Players[1].Pos = new FixVec2(Fix.FromInt(2), Fix.Zero);
            s.Players[1].Facing = new FixVec2(-Fix.One, Fix.Zero); // the puck sits on the side facing the checker
            s.Carrier = 1;
            s.Players[0].Pos = new FixVec2(Fix.FromInt(-1), Fix.Zero);
            s.Players[0].Facing = FixVec2.Right;
            sim.Step(s, new[] { new PlayerInput { Buttons = PlayerInput.ShootCheck }, default(PlayerInput) });
            for (var t = 0; t < 60 && s.Carrier == 1; t++) sim.Step(s, new[] { new PlayerInput { MoveX = 127 }, default(PlayerInput) });
            Assert.AreEqual(0, s.Carrier, "the wedge got to the puck: it's stolen, no bump needed");
            Assert.Greater(s.Players[1].NoPickup.ToDouble(), 0.0, "the old carrier can't snatch it straight back");
        }

        [Test]
        public void TheCarriedPuckSlidesAlongTheBoardsInsteadOfIntoYou()
        {
            var sim = new Simulation(LoadRink());
            var s = sim.NewGame(new[] { 0 });
            PlayNow(s);
            var top = sim.Map.BoundsMax.Y;
            s.Players[0].Pos = new FixVec2(Fix.Zero, top - Fix.FromInt(3));
            s.Players[0].Facing = new FixVec2(Fix.Zero, Fix.One);
            s.Carrier = 0;
            var r = (sim.RadiusOf(s.Players[0]) + sim.T.PuckRadius).ToDouble();
            var sideways = 0.0;
            for (var t = 0; t < 360 && s.Carrier == 0; t++)
            {
                sim.Step(s, new[] { new PlayerInput { MoveY = 127 } });
                if (s.Carrier != 0) break;
                var d = s.PuckPos - s.Players[0].Pos;
                Assert.GreaterOrEqual(d.Length.ToDouble(), r - 0.03, "the puck never gets pushed into the player (tick " + t + ")");
                sideways = System.Math.Max(sideways, System.Math.Abs(d.X.ToDouble()));
            }
            Assert.AreEqual(-1, s.Carrier, "pressing into the boards, you lose it in the end");
            Assert.Greater(sideways, 0.3, "it slid off to the side first");
        }

        [Test]
        public void AnotherPlayersBodyCanPushTheCarriedPuckAway()
        {
            var sim = new Simulation(LoadRink());
            var s = sim.NewGame(new[] { 0, 1 });
            PlayNow(s);
            // the carrier faces right; an opponent backs into the puck, body first (facing away, so it isn't a steal)
            s.Players[0].Pos = new FixVec2(Fix.FromInt(-2), Fix.Ratio(1, 10));
            s.Players[0].Facing = FixVec2.Right;
            s.Carrier = 0;
            s.Players[1].Pos = new FixVec2(Fix.FromInt(1), Fix.Zero);
            s.Players[1].Facing = FixVec2.Right;
            for (var t = 0; t < 240 && s.Carrier == 0; t++)
            {
                sim.Step(s, new[] { default(PlayerInput), new PlayerInput { MoveX = -127, AimX = 127 } });
            }
            Assert.AreEqual(-1, s.Carrier, "the body pushed the puck off the wedge: loose, not handed over");
        }

        [Test]
        public void CheckingIntoALoosePuckPicksItUp()
        {
            var sim = new Simulation(LoadRink());
            var s = sim.NewGame(new[] { 0 });
            PlayNow(s);
            s.Players[0].Pos = new FixVec2(Fix.FromInt(-3), Fix.Zero);
            s.Players[0].Facing = FixVec2.Right;
            s.PuckPos = FixVec2.Zero;
            sim.Step(s, new[] { new PlayerInput { Buttons = PlayerInput.ShootCheck } });
            for (var t = 0; t < 60 && s.Carrier != 0; t++) sim.Step(s, null);
            Assert.AreEqual(0, s.Carrier);
            Assert.Less(s.PuckVel.Length.ToDouble(), 12.0, "carried along, not knocked away");
        }

        /// <summary>A carrier and nobody else, already playing, facing right.</summary>
        private static GameState Carrying(Simulation sim)
        {
            var s = sim.NewGame(new[] { 0 });
            PlayNow(s);
            s.Players[0].Pos = FixVec2.Zero;
            s.Players[0].Facing = FixVec2.Right;
            s.Carrier = 0;
            sim.Step(s, null);
            return s;
        }

        private static PlayerInput Press(byte buttons) { return new PlayerInput { Buttons = buttons }; }

        [Test]
        public void ShootCheckWithThePuckShootsInsteadOfChecking()
        {
            var sim = new Simulation(LoadRink());
            var s = Carrying(sim);
            sim.Step(s, new[] { Press(PlayerInput.ShootCheck) });
            Assert.AreEqual(-1, s.Carrier, "it shot");
            Assert.AreEqual(Fix.One, s.Players[0].DashCharge, "no check while carrying");
            Assert.AreEqual(Fix.Zero, s.Players[0].DashTime);
            Assert.Greater(s.PuckVel.X.ToDouble(), 10.0, "shot forward");
        }

        [Test]
        public void ChargeShotFiresWhenLetGoAndIsFaster()
        {
            var quick = new Simulation(LoadRink());
            var q = Carrying(quick);
            quick.Step(q, new[] { Press(PlayerInput.ShootCheck) });
            var quickSpeed = q.PuckVel.Length.ToDouble();

            var sim = new Simulation(LoadRink());
            var s = Carrying(sim);
            for (var t = 0; t < (int)(0.75 * sim.T.TicksPerSecond); t++) sim.Step(s, new[] { Press(PlayerInput.ChargeBrake) });
            Assert.AreEqual(0, s.Carrier, "still holding it while charging");
            Assert.Greater(s.Players[0].Charge.ToDouble(), 0.5);
            sim.Step(s, new[] { default(PlayerInput) });
            Assert.AreEqual(-1, s.Carrier, "let go: it fired");
            Assert.Greater(s.PuckVel.Length.ToDouble(), quickSpeed + 5.0, "a charged shot is faster");
        }

        /// <summary>Charged shot speed follows how long charge was held, in a straight line, from a tap to full.</summary>
        [Test]
        public void ChargePowerFollowsHoldTime()
        {
            foreach (var held in new[] { 0.0, 0.225, 0.45, 0.675, 0.9, 1.5 })
            {
                var sim = new Simulation(LoadRink());
                var s = Carrying(sim);
                var ticks = System.Math.Max(1, (int)System.Math.Round(held * sim.T.TicksPerSecond));
                for (var t = 0; t < ticks; t++) sim.Step(s, new[] { Press(PlayerInput.ChargeBrake) });
                sim.Step(s, new[] { default(PlayerInput) });
                Assert.AreEqual(-1, s.Carrier, "fired after " + held + " s");
                double min = sim.T.ChargedShotMin.ToDouble(), max = sim.T.ChargedShotMax.ToDouble();
                var heldFor = ticks / (double)sim.T.TicksPerSecond;
                var expected = min + (max - min) * System.Math.Min(1.0, heldFor / sim.T.ChargeTime.ToDouble());
                expected *= 1.0 - sim.T.PuckDrag.ToDouble() / sim.T.TicksPerSecond; // the puck's drag during the tick it was fired
                Assert.AreEqual(expected, s.PuckVel.Length.ToDouble(), 0.12, "held " + held + " s");
            }
        }

        [Test]
        public void HoldingChargeWhenYouPickUpThePuckDoesNotFire()
        {
            var sim = new Simulation(LoadRink());
            var s = sim.NewGame(new[] { 0 });
            PlayNow(s);
            s.Players[0].Pos = FixVec2.Zero;
            s.PuckPos = new FixVec2(Fix.Ratio(7, 10), Fix.Zero); // touching
            for (var t = 0; t < 10; t++) sim.Step(s, new[] { Press(PlayerInput.ChargeBrake) }); // braking, then picks it up
            Assert.AreEqual(0, s.Carrier);
            sim.Step(s, new[] { default(PlayerInput) }); // let go of the brake
            Assert.AreEqual(0, s.Carrier, "letting go of a brake isn't a shot");
        }

        [Test]
        public void ChargeBrakeWithoutThePuckBrakes()
        {
            var sim = new Simulation(LoadRink());
            Fix free = Fix.Zero, braked = Fix.Zero;
            for (var k = 0; k < 2; k++)
            {
                var s = sim.NewGame(new[] { 0 });
                PlayNow(s);
                s.Players[0].Pos = new FixVec2(Fix.Zero, Fix.FromInt(-5)); // away from the puck
                s.Players[0].Vel = new FixVec2(Fix.FromInt(6), Fix.Zero);
                for (var t = 0; t < 40; t++) sim.Step(s, new[] { Press(k == 1 ? PlayerInput.ChargeBrake : (byte)0) });
                if (k == 0) free = s.Players[0].Vel.Length; else braked = s.Players[0].Vel.Length;
            }
            Assert.Less(braked.ToDouble(), free.ToDouble() * 0.6);
        }

        [Test]
        public void AimStickOverridesWhereYouFace()
        {
            var sim = new Simulation(LoadRink());
            var s = sim.NewGame(new[] { 0 });
            PlayNow(s);
            s.Players[0].Pos = new FixVec2(Fix.Zero, Fix.FromInt(-5));
            for (var t = 0; t < 40; t++) sim.Step(s, new[] { new PlayerInput { MoveX = 127, AimY = 127 } });
            Assert.Greater(s.Players[0].Facing.Y.ToDouble(), 0.95, "facing the aim stick (up) while moving right");
            Assert.Greater(s.Players[0].Vel.X.ToDouble(), 1.0, "still moving right");
            for (var t = 0; t < 60; t++) sim.Step(s, new[] { new PlayerInput { MoveX = 127 } });
            Assert.Greater(s.Players[0].Facing.X.ToDouble(), 0.95, "aim stick let go: facing where you move again");
        }

        [Test]
        public void HardBumpsAreRecorded()
        {
            var sim = new Simulation(LoadRink());
            var s = sim.NewGame(new[] { 0, 1 });
            PlayNow(s);
            s.Players[0].Pos = new FixVec2(Fix.FromInt(-2), Fix.FromInt(-5));
            s.Players[1].Pos = new FixVec2(Fix.FromInt(2), Fix.FromInt(-5));
            s.Players[0].Vel = new FixVec2(Fix.FromInt(7), Fix.Zero);
            s.Players[1].Vel = new FixVec2(Fix.FromInt(-7), Fix.Zero);
            for (var t = 0; t < 60; t++) sim.Step(s, null);
            Assert.AreEqual(1, s.Players[0].Hits);
            Assert.AreEqual(1, s.Players[1].Hits);
            Assert.Less(s.Players[0].Vel.X.ToDouble(), 0.0, "they bounced apart");
        }

        // ------------------------------------------------------------------ fairness

        /// <summary>A whole bot match (or until <paramref name="maxSeconds"/>); returns the score.</summary>
        private static int[] BotMatch(Simulation sim, int[] teams, uint seed, int maxSeconds = 300)
        {
            var s = sim.NewGame(teams);
            s.Rng = seed;
            var inputs = new PlayerInput[teams.Length];
            for (var t = 0; t < maxSeconds * sim.T.TicksPerSecond && s.Phase != Phase.GameOver; t++)
            {
                for (var i = 0; i < teams.Length; i++) inputs[i] = Bot.Think(sim, s, i);
                sim.Step(s, inputs);
            }
            return s.Score;
        }

        /// <summary>
        /// A match and its mirror image (left and right swapped, teams swapped) must play out as exact mirror images,
        /// down to the last bit: the rules may not favour a side. Bots on both, so it's a real game.
        /// </summary>
        [Test]
        public void MirroredMatchesStayMirrored() { Mirrored(LoadRink()); }

        [Test]
        public void MirroredMatchesStayMirroredOnTheHockeyRink() { Mirrored(LoadMap("hockey-rink.json")); }

        private static MapDef LoadMap(string file)
        {
            return MapDef.FromJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Maps", file)));
        }

        /// <summary>The Hockey Rink: bots can score into the nets, and both teams about as often.</summary>
        [Test]
        public void BotsScoreOnTheHockeyRink()
        {
            var sim = new Simulation(LoadMap("hockey-rink.json"));
            int[] goals = new int[2];
            // the rink is 35% longer than the Test Rink, so goals come slower: longer matches
            for (var k = 0; k < 10; k++)
            {
                var score = BotMatch(sim, new[] { 0, 1, 0, 1 }, 0x51ED27u + (uint)k * 7919u, 240);
                goals[0] += score[0]; goals[1] += score[1];
            }
            Assert.Greater(goals[0] + goals[1], 10, "goals: orange " + goals[0] + " blue " + goals[1]);
            Assert.Greater(goals[0], 0); Assert.Greater(goals[1], 0);
        }

        /// <summary>The nets are solid from behind: a puck fired at the back of a net bounces off without scoring.</summary>
        [Test]
        public void TheHockeyNetsBlockShotsFromBehind()
        {
            var sim = new Simulation(LoadMap("hockey-rink.json"));
            var s = sim.NewGame(new[] { 0 });
            PlayNow(s);
            s.Players[0].Pos = new FixVec2(Fix.Zero, Fix.FromInt(10));
            // the left net (team 0's): its back wall is just behind the goal area
            var goal = sim.Map.Goals[0].Team == 0 ? sim.Map.Goals[0] : sim.Map.Goals[1];
            var back = goal.Min.X.ToDouble() - 0.34; // the back wall's middle line
            s.PuckPos = new FixVec2(goal.Min.X - Fix.FromInt(3), Fix.Zero);
            s.PuckVel = new FixVec2(Fix.FromInt(40), Fix.Zero);
            for (var t = 0; t < 60; t++) sim.Step(s, null);
            Assert.AreEqual(0, s.Score[0] + s.Score[1], "no goal from behind");
            Assert.Less(s.PuckPos.X.ToDouble(), back, "it stayed behind the net");
            // and from the front it goes in
            s.PuckPos = new FixVec2(goal.Max.X + Fix.FromInt(6), Fix.Zero);
            s.PuckVel = new FixVec2(Fix.FromInt(-30), Fix.Zero);
            for (var t = 0; t < 60 && s.Phase == Phase.Play; t++) sim.Step(s, null);
            Assert.AreEqual(1, s.Score[1], "a shot into the mouth scores for the team attacking that net");
        }

        private static void Mirrored(MapDef map)
        {
            var sim = new Simulation(map);
            var a = sim.NewGame(new[] { 0, 1, 0, 1 }, 4242u);
            var b = sim.NewGame(new[] { 1, 0, 1, 0 }, 4242u);
            // the same slots start on mirrored spawns, since each team's spawns mirror the other's
            var ia = new PlayerInput[4];
            var ib = new PlayerInput[4];
            for (var t = 0; t < 20 * sim.T.TicksPerSecond; t++)
            {
                for (var i = 0; i < 4; i++)
                {
                    ia[i] = Bot.Think(sim, a, i);
                    var m = ia[i];
                    // the mirrored player pushes the mirrored stick
                    ib[i] = new PlayerInput { MoveX = (sbyte)-m.MoveX, MoveY = m.MoveY, AimX = (sbyte)-m.AimX, AimY = m.AimY, Buttons = m.Buttons };
                }
                sim.Step(a, ia);
                sim.Step(b, ib);
                for (var i = 0; i < 4; i++)
                {
                    Assert.AreEqual(-a.Players[i].Pos.X.Raw, b.Players[i].Pos.X.Raw, "player " + i + " x at tick " + t);
                    Assert.AreEqual(a.Players[i].Pos.Y.Raw, b.Players[i].Pos.Y.Raw, "player " + i + " y at tick " + t);
                }
                Assert.AreEqual(-a.PuckPos.X.Raw, b.PuckPos.X.Raw, "puck x at tick " + t);
                Assert.AreEqual(a.Carrier, b.Carrier, "carrier at tick " + t);
                Assert.AreEqual(a.Score[0], b.Score[1], "score at tick " + t);
            }
        }

        /// <summary>Bots play the same game on every PC from the same seed, and a different game from a different seed.</summary>
        [Test]
        public void BotsFollowTheMatchSeed()
        {
            var sim = new Simulation(LoadRink());
            var teams = new[] { 0, 1, 0, 1 };
            ulong Run(uint seed)
            {
                var s = sim.NewGame(teams, seed);
                var inputs = new PlayerInput[teams.Length];
                for (var t = 0; t < 20 * sim.T.TicksPerSecond; t++)
                {
                    for (var i = 0; i < teams.Length; i++) inputs[i] = Bot.Think(sim, s, i);
                    sim.Step(s, inputs);
                }
                return s.Checksum();
            }
            Assert.AreEqual(Run(12345u), Run(12345u));
            Assert.AreNotEqual(Run(12345u), Run(777u));
        }

        /// <summary>
        /// The rink is mirrored, so over many bot matches neither side should come out ahead, whichever order the
        /// players joined in. (One match proves nothing: bots and rules are deterministic, so the same seed always
        /// plays out the same game.)
        /// </summary>
        [Test, Timeout(600000)]
        public void BotMatchesAreFair()
        {
            var sim = new Simulation(LoadRink());
            var report = new System.Text.StringBuilder();
            var lineups = new[] { new[] { 0, 1 }, new[] { 0, 1, 0, 1 }, new[] { 1, 0, 1, 0 }, new[] { 0, 0, 1, 1 } };
            int[] goals = new int[2], wins = new int[2];
            foreach (var lineup in lineups)
            {
                int[] g = new int[2], w = new int[2];
                // 160 matches gave orange 422 goals, blue 420 (2026-11-16); 12 per lineup keeps the test quick
                for (var k = 0; k < 12; k++)
                {
                    var score = BotMatch(sim, lineup, 0x9E3779B9u + (uint)k * 7919u, 150);
                    g[0] += score[0]; g[1] += score[1];
                    if (score[0] != score[1]) w[score[0] > score[1] ? 0 : 1]++;
                }
                report.AppendLine("lineup " + string.Join(",", lineup) + ": wins orange " + w[0] + " blue " + w[1] + ", goals orange " + g[0] + " blue " + g[1]);
                for (var t = 0; t < 2; t++) { goals[t] += g[t]; wins[t] += w[t]; }
            }
            report.Append("total: wins orange " + wins[0] + " blue " + wins[1] + ", goals orange " + goals[0] + " blue " + goals[1]);
            TestContext.WriteLine(report.ToString());
            Debug.Log("Ultra Puck fairness\n" + report);
            var share = goals[0] / (double)(goals[0] + goals[1]);
            Assert.Greater(goals[0] + goals[1], 80, "the bots scored enough to judge\n" + report);
            Assert.That(share, Is.InRange(0.38, 0.62), "orange's share of the goals\n" + report);
        }

        /// <summary>
        /// Pressed against every piece of wall, facing every way, quick and charged shots: the carried puck never sits
        /// in the boards and a shot never leaves the rink.
        /// </summary>
        [Test]
        public void ShotsFromTheBoardsStayInTheRink()
        {
            var sim = new Simulation(LoadRink());
            var r = sim.T.PuckRadius.ToDouble();
            var cases = 0;
            foreach (var seg in sim.Map.Segments)
            {
                var mid = new FixVec2((seg.A.X + seg.B.X) / 2, (seg.A.Y + seg.B.Y) / 2);
                var inward = seg.Side;
                if (!sim.Map.Inside(mid + inward * Fix.Ratio(55, 100))) inward = -inward;
                var start = mid + inward * Fix.Ratio(52, 100);
                if (!sim.Map.Inside(start)) continue; // a very short piece in a corner
                for (var dir = 0; dir < 16; dir++)
                    for (var charged = 0; charged < 2; charged++)
                    {
                        var s = sim.NewGame(new[] { 0 });
                        PlayNow(s);
                        var a = dir * System.Math.PI * 2 / 16;
                        s.Players[0].Pos = start;
                        s.Players[0].Facing = new FixVec2(Fix.FromDecimal((decimal)System.Math.Cos(a)), Fix.FromDecimal((decimal)System.Math.Sin(a))).Normalized;
                        s.Carrier = 0;
                        // lean into the wall while carrying
                        var lean = new PlayerInput { MoveX = (sbyte)(-inward.X.ToDouble() * 120), MoveY = (sbyte)(-inward.Y.ToDouble() * 120) };
                        var hold = lean; hold.Buttons = PlayerInput.ChargeBrake;
                        for (var t = 0; t < 30; t++)
                        {
                            sim.Step(s, new[] { charged == 1 && t > 0 ? hold : lean });
                            if (s.Carrier != 0) break;
                            foreach (var w in sim.Map.Segments)
                            {
                                var ab = w.B - w.A;
                                var tt = Fix.Clamp01(FixVec2.Dot(s.PuckPos - w.A, ab) / ab.SqrLength);
                                var d = (s.PuckPos - (w.A + ab * tt)).Length.ToDouble();
                                Assert.GreaterOrEqual(d, r - 0.01, "carried puck in the wall");
                            }
                        }
                        // fire: a quick shot, or let go of a full charge
                        var fire = charged == 1 ? lean : new PlayerInput { MoveX = lean.MoveX, MoveY = lean.MoveY, Buttons = PlayerInput.ShootCheck };
                        for (var t = 0; t < 90; t++)
                        {
                            sim.Step(s, new[] { t == 0 ? fire : lean });
                            Assert.IsTrue(sim.Map.Inside(s.PuckPos), "puck outside the rink after a shot from " + start + " facing " + dir + (charged == 1 ? " (charged)" : "") + " at tick " + t);
                            if (s.Phase != Phase.Play) break; // scored
                        }
                        cases++;
                    }
            }
            Assert.Greater(cases, 1000);
        }

        [Test]
        public void InsideTheRink()
        {
            var map = LoadRink();
            Assert.IsTrue(map.Inside(FixVec2.Zero));
            Assert.IsTrue(map.Inside(new FixVec2(Fix.FromInt(25), Fix.Zero)), "in the net");
            Assert.IsFalse(map.Inside(new FixVec2(Fix.FromInt(30), Fix.Zero)));
            Assert.IsFalse(map.Inside(new FixVec2(Fix.Zero, Fix.FromInt(14))));
            Assert.IsFalse(map.Inside(new FixVec2(Fix.FromInt(23), Fix.FromInt(13))), "past a rounded corner");
        }
    }
}
