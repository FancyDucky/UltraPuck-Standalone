using System.IO;
using NUnit.Framework;
using UltraPuck.Core;
using UnityEngine;

namespace UltraPuck.Tests
{
    /// <summary>The cards that bring their own rules: one test (or two) each.</summary>
    public class CardMechanicsTests
    {
        private static MapDef Rink()
        {
            return MapDef.FromJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Maps", "test-rink.json")));
        }

        private static CardLibrary Library()
        {
            var lib = new CardLibrary();
            var files = Directory.GetFiles(Path.Combine(Application.streamingAssetsPath, "Cards"), "*.json");
            System.Array.Sort(files, System.StringComparer.Ordinal);
            foreach (var f in files) lib.AddJson(File.ReadAllText(f), Path.GetFileName(f));
            return lib;
        }

        private CardLibrary _lib;
        private Simulation _sim;

        [SetUp]
        public void Make() { _lib = Library(); _sim = new Simulation(Rink(), null, _lib); }

        private GameState Game(params int[] teams) { return _sim.NewGame(teams, 1u); }

        /// <summary>Give player <paramref name="i"/> these cards and work out everyone's stats again.</summary>
        private void Give(GameState s, int i, params string[] ids)
        {
            var c = new int[ids.Length];
            for (var k = 0; k < ids.Length; k++) c[k] = _lib.ById(ids[k]).Index;
            s.Players[i].Cards = c;
            _lib.Recompute(s);
        }

        /// <summary>Team Vote: one offer per team, and everyone on the team gets the card most of them voted for.</summary>
        [Test]
        public void TeamVoteGivesTheWholeTeamTheWinningCard()
        {
            var sim = new Simulation(Rink(), new Tuning { TeamVote = true, CardsOffered = 3 }, _lib);
            var s = sim.NewGame(new[] { 0, 0, 0, 1 }, 7u);
            sim.StartPicking(s, new[] { 0, 1, 2 });
            CollectionAssert.AreEqual(s.Players[0].Offer, s.Players[1].Offer, "the same offer for the whole team");
            CollectionAssert.AreEqual(s.Players[0].Offer, s.Players[2].Offer);
            // two vote for the second card, one for the first
            sim.Step(s, new[] { new PlayerInput { Pick = 2 }, new PlayerInput { Pick = 2 }, new PlayerInput { Pick = 1 }, default(PlayerInput) });
            var won = s.Players[0].Cards[s.Players[0].Cards.Length - 1];
            for (var i = 0; i < 3; i++) Assert.AreEqual(won, s.Players[i].Cards[s.Players[i].Cards.Length - 1], "player " + i + " got the team's card");
            Assert.AreEqual(0, s.Players[3].Cards.Length, "the other team didn't pick");
        }

        /// <summary>Team Vote: bots don't swing the vote — only humans count, and bots copy them.</summary>
        [Test]
        public void TeamVoteBotsFollowHumanMajority()
        {
            var sim = new Simulation(Rink(), new Tuning { TeamVote = true, CardsOffered = 3 }, _lib);
            var s = sim.NewGame(new[] { 0, 0, 0, 1 }, 11u);
            s.Players[1].Bot = true;
            s.Players[2].Bot = true;
            sim.StartPicking(s, new[] { 0, 1, 2 });
            var humanPick = s.Players[0].Offer[0];
            // human picks the first card; bots would prefer a higher tier but must follow the human
            var botAlone = Bot.Think(sim, s, 1);
            Assert.AreEqual(0, botAlone.Pick, "bots wait until a human looks");
            s.Players[0].Hover = 1;
            Assert.AreEqual(1, Bot.Think(sim, s, 1).Pick, "bots follow the human's hover");
            sim.Step(s, new[] {
                new PlayerInput { Pick = 1 },
                Bot.Think(sim, s, 1),
                Bot.Think(sim, s, 2),
                default(PlayerInput)
            });
            Assert.AreEqual(humanPick, s.Players[0].Cards[s.Players[0].Cards.Length - 1]);
            Assert.AreEqual(humanPick, s.Players[1].Cards[s.Players[1].Cards.Length - 1], "bot got the human's card");
            Assert.AreEqual(humanPick, s.Players[2].Cards[s.Players[2].Cards.Length - 1], "other bot too");
        }

        /// <summary>Pylon goalies can be pushed around (they're heavier than a player), and go back to their spot at a face-off.</summary>
        [Test]
        public void PylonGoaliesCanBePushed()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.pylongoalie");
            _sim.PlacePylons(s);
            Play(s);
            Assert.AreEqual(1, s.Pylons.Length);
            var home = s.Pylons[0].Pos;
            // the other player skates straight through it
            s.Players[1].Pos = home + new FixVec2(Fix.FromInt(3), Fix.Zero);
            for (var t = 0; t < 120; t++) _sim.Step(s, new[] { default(PlayerInput), new PlayerInput { MoveX = -127 } });
            Assert.Greater((s.Pylons[0].Pos - home).Length.ToDouble(), 0.3, "pushed off its spot");
            _sim.FaceOff(s);
            Assert.AreEqual(home, s.Pylons[0].Pos, "back on its spot for the face-off");
        }

        /// <summary>
        /// On every rink, whichever way its nets face, the pylon goalies stand on the ice in a row just in front of
        /// their team's net: between the net and a shot coming straight at its mouth.
        /// </summary>
        [TestCase("test-rink.json")]
        [TestCase("hockey-rink.json")]
        [TestCase("s-rink.json")]
        [TestCase("u-rink.json")]
        public void PylonGoaliesStandInFrontOfTheirNetOnEveryRink(string file)
        {
            var map = MapDef.FromJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Maps", file)));
            var sim = new Simulation(map, null, _lib);
            var s = sim.NewGame(new[] { 0, 1 }, 1u);
            var pg = _lib.ById("ultrapuck.pylongoalie").Index;
            s.Players[0].Cards = new[] { pg, pg, pg };
            s.Players[1].Cards = new[] { pg, pg, pg };
            _lib.Recompute(s);
            sim.PlacePylons(s);
            Assert.AreEqual(6, s.Pylons.Length);
            foreach (var py in s.Pylons)
            {
                var g = map.Goals.Find(x => x.Team == py.Team);
                Assert.IsTrue(map.Inside(py.Pos), "on the ice: " + py.Pos);
                Assert.IsFalse(g.Touches(py.Pos, sim.T.PylonRadius), "not in the net itself");
                // which way the net opens: it says, or (a straight rink) towards the middle
                var mid = new FixVec2((g.Min.X + g.Max.X) / 2, (g.Min.Y + g.Max.Y) / 2);
                var front = g.HasFacing ? g.Facing : new FixVec2(map.PuckSpot.X > mid.X ? Fix.One : -Fix.One, Fix.Zero);
                var off = py.Pos - mid;
                var ahead = FixVec2.Dot(off, front).ToDouble();
                var aside = System.Math.Abs(FixVec2.Dot(off, new FixVec2(-front.Y, front.X)).ToDouble());
                Assert.AreEqual(sim.T.PylonOut.ToDouble(), ahead, 1e-3, "out in front of the mouth");
                Assert.LessOrEqual(aside, sim.T.PylonSpread.ToDouble() * 1.5 + 1e-3, "and across it, not off to one side");
                // nothing between it and the net's middle but the open mouth
                Assert.IsFalse(map.Blocked(py.Pos, mid), "no wall between the pylon and the inside of its net");
            }
        }

        /// <summary>Switched-off cards are never offered.</summary>
        [Test]
        public void SwitchedOffCardsAreNeverOffered()
        {
            var off = new System.Collections.Generic.List<int>();
            foreach (var c in _lib.Cards) if (c.Index % 2 == 0) off.Add(c.Index);
            var sim = new Simulation(Rink(), new Tuning { CardsOff = off.ToArray() }, _lib);
            for (uint seed = 1; seed < 40; seed++)
            {
                var s = sim.NewGame(new[] { 0, 1 }, seed);
                sim.StartPicking(s, new[] { 0 });
                foreach (var c in s.Players[0].Offer) Assert.IsFalse(off.Contains(c), "card " + c + " is switched off");
            }
        }

        private static void Play(GameState s) { s.Phase = Phase.Play; s.PhaseTime = Fix.Zero; }
        private static FixVec2 At(double x, double y) { return new FixVec2(Fix.FromDecimal((decimal)x), Fix.FromDecimal((decimal)y)); }
        private static PlayerInput Press(byte b) { return new PlayerInput { Buttons = b }; }
        private void Run(GameState s, int ticks, params PlayerInput[] inputs) { for (var t = 0; t < ticks; t++) _sim.Step(s, inputs); }

        [Test]
        public void HeadStartGivesTheTeamThePuck()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.headstart");
            _sim.FaceOff(s);
            Assert.AreEqual(0, s.Carrier, "orange starts with it");
            Give(s, 1, "ultrapuck.headstart");
            _sim.FaceOff(s);
            Assert.AreEqual(-1, s.Carrier, "a tie: it starts in the middle");
        }

        [Test]
        public void DoubleAgentStartsOnTheOtherSide()
        {
            var s = Game(0, 1);
            Assert.Less(s.Players[0].Pos.X.ToDouble(), 0.0);
            Give(s, 0, "ultrapuck.doubleagent");
            _sim.FaceOff(s);
            Assert.Greater(s.Players[0].Pos.X.ToDouble(), 0.0, "on blue's side");
            Assert.Greater(s.Players[0].Facing.X.ToDouble(), 0.0, "still facing the goal they attack");
            Assert.Greater(s.Players[1].Pos.X.ToDouble(), 0.0, "blue is still at home");
            Assert.AreNotEqual(s.Players[0].Pos, s.Players[1].Pos);
        }

        [Test]
        public void PylonGoaliesBlockShots()
        {
            var s = Game(0, 1);
            Give(s, 1, "ultrapuck.pylongoalie");
            _sim.FaceOff(s);
            Assert.AreEqual(1, s.Pylons.Length);
            Assert.AreEqual(1, s.Pylons[0].Team);
            Assert.Greater(s.Pylons[0].Pos.X.ToDouble(), 20.0, "in front of blue's net");
            Play(s);
            s.Players[0].Pos = At(-10, 8); s.Players[1].Pos = At(-10, -8);
            s.PuckPos = At(14, 0);
            s.PuckVel = At(25, 0);
            Run(s, 240);
            Assert.AreEqual(0, s.Score[0], "the shot was blocked");
            Give(s, 1, "ultrapuck.pylongoalie", "ultrapuck.pylongoalie");
            _sim.FaceOff(s);
            Assert.AreEqual(2, s.Pylons.Length, "one per copy");
        }

        [Test]
        public void PuckGunStunsAndKnocksThePuckLoose()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.puckgun");
            Play(s);
            s.Players[0].Pos = At(-4, 0); s.Players[0].Facing = FixVec2.Right;
            s.Players[1].Pos = At(3, 0); s.Players[1].Facing = new FixVec2(Fix.Zero, Fix.One);
            s.Carrier = 1;
            _sim.Step(s, new[] { Press(PlayerInput.ChargeBrake), default(PlayerInput) });
            Assert.AreEqual(1, s.Shots.Length, "fired");
            Run(s, 60, default(PlayerInput), default(PlayerInput));
            Assert.Greater(s.Players[1].Stun.ToDouble(), 0.0, "stunned");
            Assert.AreNotEqual(1, s.Carrier, "dropped the puck");
            Assert.AreEqual(0, s.Shots.Length);
            // a second shot has to wait for the cooldown
            _sim.Step(s, new[] { default(PlayerInput), default(PlayerInput) });
            _sim.Step(s, new[] { Press(PlayerInput.ChargeBrake), default(PlayerInput) });
            Assert.AreEqual(0, s.Shots.Length, "still cooling down");
            Run(s, 2 * _sim.T.TicksPerSecond, default(PlayerInput), default(PlayerInput));
            _sim.Step(s, new[] { Press(PlayerInput.ChargeBrake), default(PlayerInput) });
            Assert.AreEqual(1, s.Shots.Length, "ready again");
        }

        [Test]
        public void PuckGunKnocksTheLoosePuck()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.puckgun");
            Play(s);
            s.Players[0].Pos = At(-4, -6); s.Players[0].Facing = FixVec2.Right;
            s.Players[1].Pos = At(10, 8);
            s.PuckPos = At(0, -6);
            _sim.Step(s, new[] { Press(PlayerInput.ChargeBrake), default(PlayerInput) });
            for (var t = 0; t < 60 && s.Shots.Length > 0; t++) _sim.Step(s, new[] { default(PlayerInput), default(PlayerInput) });
            Assert.Greater(s.PuckVel.X.ToDouble(), 8.0, "the shot knocked the puck along");
        }

        [Test]
        public void WithoutTheCardUtilityDoesNothing()
        {
            var s = Game(0, 1);
            Play(s);
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            Assert.AreEqual(0, s.Shots.Length);
            Assert.AreEqual(Fix.Zero, s.Players[0].InvisibleTime);
        }

        [Test]
        public void InvisibilityLastsFiveSecondsThenCoolsDown()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.invisibility");
            Play(s);
            _sim.Step(s, new[] { Press(PlayerInput.Utility) });
            Assert.Greater(s.Players[0].InvisibleTime.ToDouble(), 4.9);
            Run(s, (int)(5.2 * _sim.T.TicksPerSecond), default(PlayerInput));
            Assert.AreEqual(Fix.Zero, s.Players[0].InvisibleTime, "visible again");
            _sim.Step(s, new[] { Press(PlayerInput.Utility) });
            Assert.AreEqual(Fix.Zero, s.Players[0].InvisibleTime, "still cooling down");
        }

        /// <summary>Player 0 checks player 1 (both on <paramref name="sameTeam"/>'s terms).</summary>
        private GameState Check(bool sameTeam, string card, int target = 1)
        {
            var s = Game(0, sameTeam ? 0 : 1);
            if (card != null) Give(s, target, card);
            Play(s);
            s.Players[0].Pos = At(-1, -6); s.Players[0].Facing = FixVec2.Right;
            s.Players[1].Pos = At(1.5, -6);
            _sim.Step(s, new[] { Press(PlayerInput.ShootCheck), default(PlayerInput) });
            for (var t = 0; t < 40 && s.Players[1].Hits == 0; t++) _sim.Step(s, new[] { new PlayerInput { MoveX = 127 }, default(PlayerInput) });
            return s;
        }

        [Test]
        public void FriendlyFireBoostsInsteadOfDazing()
        {
            var s = Check(true, "ultrapuck.friendlyfire");
            Assert.Greater(s.Players[1].BoostTime.ToDouble(), 1.5, "boosted");
            Assert.AreEqual(Fix.Zero, s.Players[1].Stun, "not dazed");
            var plain = Check(true, null);
            Assert.AreEqual(Fix.Zero, plain.Players[1].BoostTime, "without the card, no boost");
        }

        [Test]
        public void ShockwavePushesEveryoneNearby()
        {
            var s = Game(0, 1, 1);
            Give(s, 0, "ultrapuck.shockwave");
            Play(s);
            s.Players[0].Pos = At(0, -6);
            s.Players[1].Pos = At(1.0, -6);   // foe
            s.Players[2].Pos = At(-1.0, -6);  // teammate
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput), default(PlayerInput) });
            Assert.AreEqual(1, s.Players[0].Shocks, "a shockwave went out");
            Assert.Greater(s.Players[1].Vel.X.ToDouble(), 0.5, "foe shoved away");
            Assert.Less(s.Players[2].Vel.X.ToDouble(), -0.5, "teammate shoved away too");
            Assert.AreEqual(Fix.Zero, s.Players[1].SlowTime, "no slow — push only");
            Assert.Greater(s.Players[0].ShockCooldown.ToDouble(), 5.5, "cooling down");
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput), default(PlayerInput) });
            Assert.AreEqual(1, s.Players[0].Shocks, "still cooling down: no second wave");
        }

        [Test]
        public void TakingASecondUtilityReplacesTheFirst()
        {
            var s = Game(0, 1);
            _sim.GiveCard(s, s.Players[0], _lib.ById("ultrapuck.invisibility").Index);
            _lib.Recompute(s);
            Assert.AreEqual(1, s.Players[0].Stats.Invisibility);
            _sim.GiveCard(s, s.Players[0], _lib.ById("ultrapuck.shockwave").Index);
            _lib.Recompute(s);
            Assert.AreEqual(0, s.Players[0].Stats.Invisibility, "invisibility was replaced");
            Assert.AreEqual(1, s.Players[0].Stats.Shockwave, "shockwave is the utility now");
            Assert.IsTrue(_lib.WouldReplaceUtility(s.Players[0], _lib.ById("ultrapuck.swap").Index));
        }

        [Test]
        public void MultiUtilityKeepsBoth()
        {
            var sim = new Simulation(Rink(), new Tuning { MultiUtility = true }, _lib);
            var s = sim.NewGame(new[] { 0, 1 }, 1u);
            sim.GiveCard(s, s.Players[0], _lib.ById("ultrapuck.invisibility").Index);
            sim.GiveCard(s, s.Players[0], _lib.ById("ultrapuck.shockwave").Index);
            _lib.Recompute(s);
            Assert.AreEqual(1, s.Players[0].Stats.Invisibility);
            Assert.AreEqual(1, s.Players[0].Stats.Shockwave);
        }

        [Test]
        public void SwapThrowsARockThenSwapsPlaces()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.swap");
            Play(s);
            s.Players[0].Pos = At(0, -6);
            s.Players[0].Facing = FixVec2.Right;
            s.Players[0].Vel = FixVec2.Zero;
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            Assert.AreEqual(1, s.Rocks.Length, "a rock was thrown");
            Assert.AreEqual(0, s.Players[0].Swaps, "throw is silent — no clap yet");
            Assert.AreEqual(0, s.Rocks[0].Owner);
            Assert.Greater(s.Rocks[0].Pos.X.ToDouble(), s.Players[0].Pos.X.ToDouble(), "rock ahead of the player");
            // park the rock somewhere known with momentum, then swap (let go of Utility first — it's a press edge)
            s.Rocks[0] = new Rock { Pos = At(4, -6), Vel = FixVec2.Zero, Owner = 0, Carrier = -1 };
            s.Players[0].Pos = At(0, -6);
            _sim.Step(s, new PlayerInput[2]);
            var before = s.Players[0].Pos;
            var playerVel = new FixVec2(Fix.FromInt(3), Fix.Zero);
            var rockVel = new FixVec2(Fix.FromInt(-5), Fix.Zero);
            s.Players[0].Vel = playerVel;
            s.Rocks[0] = new Rock { Pos = At(4, -6), Vel = rockVel, Owner = 0, Carrier = -1 };
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            Assert.AreEqual(1, s.Players[0].Swaps, "teleport claps");
            Assert.AreEqual(At(4, -6), s.Players[0].SwapTo, "swapped to the rock's spot");
            Assert.AreEqual(before, s.Players[0].SwapFrom, "left from where they stood");
            // same Step still moves everyone after the swap, so check the directions stuck
            Assert.Less(s.Players[0].Vel.X.ToDouble(), -3.0, "player kept the rock's leftward momentum");
            Assert.Greater(s.Rocks[0].Vel.X.ToDouble(), 1.5, "rock kept the player's rightward momentum");
        }

        [Test]
        public void SwapRockCanBePickedUpAndShot()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.swap");
            Play(s);
            s.Carrier = -1;
            s.PuckPos = At(14, 0); // out of the way
            s.Players[0].Pos = At(0, -6);
            s.Players[0].Facing = FixVec2.Right;
            s.Players[0].Vel = FixVec2.Zero;
            s.Players[0].NoPickup = Fix.Zero;
            s.Rocks = new[] { new Rock { Pos = At(0.9, -6), Vel = FixVec2.Zero, Owner = 0, Carrier = -1 } };
            _sim.Step(s, new PlayerInput[2]);
            Assert.AreEqual(0, s.Rocks[0].Carrier, "picked up the rock");
            _sim.Step(s, new[] { Press(PlayerInput.ShootCheck), default(PlayerInput) });
            Assert.AreEqual(-1, s.Rocks[0].Carrier, "shot the rock");
            Assert.Greater(s.Rocks[0].Vel.X.ToDouble(), 5.0, "rock flew forward");
            Assert.AreEqual(1, s.Players[0].ShotsFired);
        }

        [Test]
        public void RockAndPuckBounceOffEachOther()
        {
            var s = Game(0, 1);
            Play(s);
            s.Carrier = -1;
            s.PuckPos = At(0, -6);
            s.PuckVel = new FixVec2(Fix.FromInt(12), Fix.Zero);
            s.Rocks = new[] { new Rock { Pos = At(1.2, -6), Vel = FixVec2.Zero, Owner = 0, Carrier = -1 } };
            for (var t = 0; t < 20; t++) _sim.Step(s, new PlayerInput[2]);
            Assert.Less(s.PuckVel.X.ToDouble(), 12.0, "puck lost speed into the rock");
            Assert.Greater(s.Rocks[0].Vel.X.ToDouble(), 1.0, "rock was shoved");
        }

        [Test]
        public void RockKnocksCarriedPuckLooseLikePuckGun()
        {
            var s = Game(0, 1);
            Play(s);
            s.Players[0].Pos = At(0, -6);
            s.Players[0].Facing = FixVec2.Right;
            s.Players[0].Vel = FixVec2.Zero;
            s.Carrier = 0;
            s.PuckPos = At(0.8, -6);
            s.PuckVel = FixVec2.Zero;
            // already overlapping the carried puck, flying left (like a Puck Gun bolt)
            s.Rocks = new[] { new Rock { Pos = At(1.0, -6), Vel = new FixVec2(Fix.FromInt(-14), Fix.Zero), Owner = 1, Carrier = -1 } };
            _sim.Step(s, new PlayerInput[2]);
            Assert.AreEqual(-1, s.Carrier, "puck was knocked loose");
            Assert.Greater(s.Players[0].NoPickup.ToDouble(), 0.0, "carrier can't scoop it straight back");
            Assert.Greater(s.Rocks[0].Vel.X.ToDouble(), -14.0, "rock rebounded off the hit");
        }

        [Test]
        public void BotSwapHasAThreeSecondCooldown()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.swap");
            Play(s);
            s.Players[0].Bot = true;
            s.Players[0].Pos = At(0, -6);
            s.Players[0].Facing = FixVec2.Right;
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            Assert.AreEqual(1, s.Rocks.Length);
            Assert.Greater(s.Players[0].SwapCooldown.ToDouble(), 2.9, "bot cooling down after throw");
            s.Rocks[0] = new Rock { Pos = At(8, -6), Vel = FixVec2.Zero, Owner = 0, Carrier = -1 };
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            Assert.AreEqual(0, s.Players[0].Swaps, "still cooling: no teleport");
            s.Players[0].SwapCooldown = Fix.Zero;
            _sim.Step(s, new PlayerInput[2]);
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            Assert.AreEqual(1, s.Players[0].Swaps, "ready again: teleported");
        }

        [Test]
        public void SwapRockGlidesAndIsEasyToKnock()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.swap");
            Play(s);
            s.Players[0].Pos = At(0, -6);
            s.Players[0].Facing = FixVec2.Right;
            s.Players[0].Vel = FixVec2.Zero;
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            Assert.AreEqual(1, s.Rocks.Length);
            var thrown = s.Rocks[0].Vel.Length.ToDouble();
            Assert.Greater(thrown, 10.0, "thrown hard");
            // coast with nobody touching it: still moving after a good while (low drag)
            for (var t = 0; t < 120; t++) _sim.Step(s, new PlayerInput[2]);
            Assert.Greater(s.Rocks[0].Vel.Length.ToDouble(), thrown * 0.35, "still gliding");
            // a light bump from the side should shove it a lot (low weight)
            s.Rocks[0] = new Rock { Pos = At(2, -6), Vel = FixVec2.Zero, Owner = 0, Carrier = -1 };
            s.Players[1].Pos = At(1.2, -6);
            s.Players[1].Vel = new FixVec2(Fix.FromInt(8), Fix.Zero);
            s.Players[1].NoPickup = Fix.FromInt(1); // bump it, don't pick it up
            _sim.Step(s, new[] { default(PlayerInput), default(PlayerInput) });
            Assert.Greater(s.Rocks[0].Vel.X.ToDouble(), 3.0, "easy to knock");
        }

        /// <summary>Ice Trail: a fully charged shot lays ice behind the puck that slows opponents (not teammates), then melts.</summary>
        [Test]
        public void IceTrailSlowsOpponentsAndMelts()
        {
            var s = Game(0, 1, 0);
            Give(s, 0, "ultrapuck.icetrail");
            Play(s);
            s.Players[0].Pos = At(-8, -6); s.Players[0].Facing = FixVec2.Right;
            s.Players[1].Pos = At(8, 7);
            s.Players[2].Pos = At(-8, 7);
            s.Carrier = 0; s.PuckPos = At(-7.15, -6);
            for (var t = 0; t < 300; t++) _sim.Step(s, new[] { Press(PlayerInput.ChargeBrake), default(PlayerInput), default(PlayerInput) });
            Assert.GreaterOrEqual(s.Players[0].Charge.ToDouble(), 1.0, "fully charged");
            _sim.Step(s, new PlayerInput[3]); // let go: the shot
            Assert.AreEqual(-1, s.Carrier);
            for (var t = 0; t < 30; t++) _sim.Step(s, new PlayerInput[3]);
            Assert.Greater(s.Ice.Length, 3, "ice behind the puck");
            Assert.Greater(s.IceLaying.ToDouble(), 0.0, "still laying it");

            s.Players[1].Pos = s.Ice[1].Pos; s.Players[1].Vel = FixVec2.Zero;
            s.Players[2].Pos = s.Ice[1].Pos + At(0, 0.1); s.Players[2].Vel = FixVec2.Zero;
            _sim.Step(s, new PlayerInput[3]);
            Assert.AreEqual(0.1, s.Players[1].Slow.ToDouble(), 1e-3, "an opponent on the ice is 10% slower");
            Assert.AreEqual(Fix.Zero, s.Players[2].SlowTime, "a teammate isn't");

            for (var t = 0; t < 120 * 12; t++) _sim.Step(s, new PlayerInput[3]);
            Assert.AreEqual(0, s.Ice.Length, "all melted");
        }

        /// <summary>Picking the puck up ends the trail there, and it starts melting straight away.</summary>
        [Test]
        public void PickingUpThePuckEndsTheIceTrail()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.icetrail");
            Play(s);
            s.Players[0].Pos = At(-8, -6); s.Players[0].Facing = FixVec2.Right;
            s.Players[1].Pos = At(-2, -6); // right in the way
            s.Carrier = 0; s.PuckPos = At(-7.15, -6);
            for (var t = 0; t < 300; t++) _sim.Step(s, new[] { Press(PlayerInput.ChargeBrake), default(PlayerInput) });
            _sim.Step(s, new PlayerInput[2]);
            for (var t = 0; t < 60 && s.Carrier != 1; t++) _sim.Step(s, new PlayerInput[2]);
            Assert.AreEqual(1, s.Carrier, "the opponent caught it");
            _sim.Step(s, new PlayerInput[2]);
            Assert.AreEqual(Fix.Zero, s.IceLaying, "the trail ended at the pickup");
            var n = s.Ice.Length;
            Assert.Greater(n, 0);
            for (var t = 0; t < 120 * 2; t++) _sim.Step(s, new PlayerInput[2]);
            Assert.Greater(s.Ice.Length, 0, "still there after 2 s: it lasts 3");
            for (var t = 0; t < 120 * 3; t++) _sim.Step(s, new PlayerInput[2]);
            Assert.AreEqual(0, s.Ice.Length, "melted soon after: the trail was short, so the slow melt didn't take long");
        }

        /// <summary>Player 0 passes along y = -5 to teammate 1; an opponent (2) waits somewhere.</summary>
        private GameState Pass(string card, FixVec2 opponent)
        {
            var s = Game(0, 0, 1);
            Give(s, 0, card);
            Play(s);
            s.Players[0].Pos = At(-6, -5); s.Players[0].Facing = FixVec2.Right;
            s.Players[1].Pos = At(0, -5); s.Players[1].Facing = new FixVec2(-Fix.One, Fix.Zero);
            s.Players[2].Pos = opponent;
            s.Carrier = 0;
            s.Players[0].DashCharge = Fix.Zero;
            s.Players[1].DashCharge = Fix.Zero;
            _sim.Step(s, new[] { Press(PlayerInput.ShootCheck), default(PlayerInput), default(PlayerInput) });
            for (var t = 0; t < 120 && s.Carrier != 1; t++) _sim.Step(s, new PlayerInput[3]);
            Assert.AreEqual(1, s.Carrier, "the pass arrived");
            return s;
        }

        [Test]
        public void TeamPlayRestoresCooldownsAfterACleanPass()
        {
            var s = Pass("ultrapuck.teamplay", At(10, 8));
            Assert.AreEqual(1, s.Players[0].Refreshes);
            Assert.GreaterOrEqual(s.Players[0].DashCharge.ToDouble(), 1.0, "the passer's check is ready again");
            Assert.Less(s.Players[1].DashCharge.ToDouble(), 1.0, "Team Play is only the passer's");
        }

        [Test]
        public void TeamPlayDoesntCountIfAnOpponentTouchesIt()
        {
            // close enough to the puck's path to touch it, too far to pick it up
            var s = Pass("ultrapuck.teamplay", At(-3, -4.05));
            Assert.AreEqual(0, s.Players[0].Refreshes);
        }

        [Test]
        public void TeamPlayTwoRestoresTheWholeTeam()
        {
            var s = Pass("ultrapuck.teamplay2", At(10, 8));
            Assert.GreaterOrEqual(s.Players[1].DashCharge.ToDouble(), 1.0, "the teammate's check too");
            Assert.AreEqual(1, s.Players[1].Refreshes);
            Assert.AreEqual(0, s.Players[2].Refreshes, "not the other team");
        }

        /// <summary>
        /// Every card in play at once (random hands, bots using their utility cards): two separate runs and a
        /// rewind-and-replay must give exactly the same game, or online play would drift apart.
        /// </summary>
        [Test]
        public void AllCardsStayDeterministic()
        {
            System.Func<GameState> make = () =>
            {
                var s = _sim.NewGame(new[] { 0, 1, 0, 1 }, 2024u);
                var r = new System.Random(5);
                foreach (var p in s.Players)
                {
                    var hand = new int[5];
                    for (var k = 0; k < hand.Length; k++) hand[k] = r.Next(_lib.Cards.Count);
                    p.Cards = hand;
                }
                _lib.Recompute(s);
                _sim.FaceOff(s);
                return s;
            };
            System.Action<GameState, int> run = (s, ticks) =>
            {
                var inputs = new PlayerInput[4];
                for (var t = 0; t < ticks; t++)
                {
                    for (var i = 0; i < 4; i++) inputs[i] = Bot.Think(_sim, s, i);
                    _sim.Step(s, inputs);
                }
            };
            var a = make();
            var b = make();
            run(a, 2400);
            var saved = a.Clone();
            run(a, 2400);
            run(b, 4800);
            Assert.AreEqual(a.Checksum(), b.Checksum(), "two runs agree");
            run(saved, 2400);
            Assert.AreEqual(a.Checksum(), saved.Checksum(), "rewind and replay agrees");
            var used = 0;
            foreach (var p in a.Players) used += p.Shocks + p.Refreshes;
            TestContext.WriteLine("shocks + refreshes " + used + ", score " + a.Score[0] + "-" + a.Score[1] + ", pylons " + a.Pylons.Length);
        }

        [Test]
        public void TeamCardsNeedATeamAndTeamPlayTwoNeedsTeamPlay()
        {
            var solo = Game(0, 1, 1);
            var tp = _lib.ById("ultrapuck.teamplay").Index;
            var tp2 = _lib.ById("ultrapuck.teamplay2").Index;
            var ff = _lib.ById("ultrapuck.friendlyfire").Index;
            for (var k = 0; k < 300; k++)
            {
                var offer = _lib.DrawOffer(solo, 0, 3);
                CollectionAssert.DoesNotContain(offer, tp, "orange is on their own");
                CollectionAssert.DoesNotContain(offer, ff);
                CollectionAssert.DoesNotContain(offer, tp2, "blue has no Team Play yet");
                CollectionAssert.DoesNotContain(_lib.DrawOffer(solo, 1, 3), tp2);
            }
            solo.Players[2].Cards = new[] { tp };
            var seen = false;
            for (var k = 0; k < 2000 && !seen; k++) seen = System.Array.IndexOf(_lib.DrawOffer(solo, 1, 3), tp2) >= 0;
            Assert.IsTrue(seen, "once a teammate has Team Play, Team Play II can turn up");
        }
    }
}
