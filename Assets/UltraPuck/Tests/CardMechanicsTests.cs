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

        /// <summary>Same Draw: everyone on a team is offered the same cards, and each takes their own.</summary>
        [Test]
        public void SameDrawOffersTheTeamTheSameCardsButEachPicks()
        {
            var sim = new Simulation(Rink(), new Tuning { SameDraw = true, CardsOffered = 3 }, _lib);
            var s = sim.NewGame(new[] { 0, 0, 0, 1 }, 7u);
            sim.StartPicking(s, new[] { 0, 1, 2 });
            CollectionAssert.AreEqual(s.Players[0].Offer, s.Players[1].Offer, "the same offer for the whole team");
            CollectionAssert.AreEqual(s.Players[0].Offer, s.Players[2].Offer);
            var offer = (int[])s.Players[0].Offer.Clone();
            sim.Step(s, new[] { new PlayerInput { Pick = 1 }, new PlayerInput { Pick = 2 }, new PlayerInput { Pick = 3 }, default(PlayerInput) });
            for (var i = 0; i < 3; i++) Assert.AreEqual(offer[i], s.Players[i].Cards[s.Players[i].Cards.Length - 1], "player " + i + " got the card they took");
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
            var looking = Bot.Think(sim, s, 1);
            Assert.AreEqual(1, looking.Hover, "bots' cursor follows the human's hover");
            Assert.AreEqual(0, looking.Pick, "bots don't lock until a human locks (so the cursor can keep moving)");
            s.Players[0].Hover = 2;
            Assert.AreEqual(2, Bot.Think(sim, s, 1).Hover, "bots keep following when the human looks elsewhere");
            // human locks card 1; bots only hover this tick (they see the lock on the next)
            sim.Step(s, new[] {
                new PlayerInput { Pick = 1, Hover = 1 },
                Bot.Think(sim, s, 1),
                Bot.Think(sim, s, 2),
                default(PlayerInput)
            });
            Assert.AreEqual(humanPick, s.Players[0].Picked, "human locked");
            Assert.Less(s.Players[1].Picked, 0, "bot still open so its cursor can move");
            // bots see the lock, lock with them, vote resolves
            sim.Step(s, new[] {
                new PlayerInput { Pick = 1, Hover = 1 },
                Bot.Think(sim, s, 1),
                Bot.Think(sim, s, 2),
                default(PlayerInput)
            });
            Assert.AreEqual(humanPick, s.Players[0].Cards[s.Players[0].Cards.Length - 1]);
            Assert.AreEqual(humanPick, s.Players[1].Cards[s.Players[1].Cards.Length - 1], "bot got the human's card");
            Assert.AreEqual(humanPick, s.Players[2].Cards[s.Players[2].Cards.Length - 1], "other bot too");
        }

        [Test]
        public void TeamVoteBotAlonePicksByTier()
        {
            // 1v1: bot's team has no human — must still pick (not wait forever)
            var sim = new Simulation(Rink(), new Tuning { TeamVote = true, CardsOffered = 3 }, _lib);
            var s = sim.NewGame(new[] { 0, 1 }, 3u);
            s.Players[1].Bot = true;
            sim.StartPicking(s, new[] { 1 });
            var alone = Bot.Think(sim, s, 1);
            Assert.Greater(alone.Pick, 0, "bot alone on the team picks straight away");
            Assert.AreEqual(alone.Pick, alone.Hover, "hover matches the pick");
            Assert.LessOrEqual(alone.Pick, s.Players[1].Offer.Length);
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
            Assert.AreEqual(2, s.Pylons.Length, "one each, however many cards");
            foreach (var py in s.Pylons)
            {
                var g = map.Goals.Find(x => x.Team == py.Team);
                Assert.IsTrue(map.Inside(py.Pos), "on the ice: " + py.Pos);
                Assert.IsFalse(g.Touches(py.Pos, py.Radius), "not in the net itself");
                // which way the net opens: it says, or (a straight rink) towards the middle
                var mid = new FixVec2((g.Min.X + g.Max.X) / 2, (g.Min.Y + g.Max.Y) / 2);
                var front = g.HasFacing ? g.Facing : new FixVec2(map.PuckSpot.X > mid.X ? Fix.One : -Fix.One, Fix.Zero);
                var off = py.Pos - mid;
                var ahead = FixVec2.Dot(off, front).ToDouble();
                var aside = System.Math.Abs(FixVec2.Dot(off, new FixVec2(-front.Y, front.X)).ToDouble());
                Assert.GreaterOrEqual(ahead, sim.T.PylonOut.ToDouble() - 1e-3, "out in front of the mouth");
                Assert.AreEqual(0.0, aside, 1e-3, "in the middle of it, not off to one side");
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
            var small = s.Pylons[0].Radius;
            Give(s, 1, "ultrapuck.pylongoalie", "ultrapuck.pylongoalie");
            _sim.FaceOff(s);
            Assert.AreEqual(1, s.Pylons.Length, "still one pylon");
            Assert.AreEqual(small.ToDouble() * 1.55, s.Pylons[0].Radius.ToDouble(), 1e-3, "55% bigger for the second copy");
        }

        /// <summary>A pylon goalie can't be pushed into a net: it stops at the goal box, so nobody can plug a net with it.</summary>
        [Test]
        public void PylonGoaliesStayOutOfTheNet()
        {
            var s = Game(0, 1);
            Give(s, 1, "ultrapuck.pylongoalie", "ultrapuck.pylongoalie");
            _sim.FaceOff(s);
            Play(s);
            s.Players[0].Pos = At(-10, 8); s.Players[1].Pos = At(-10, -8);
            var py = s.Pylons[0];
            var net = _sim.Map.Goals.Find(g => g.Team == 1);
            // sent flying straight into blue's net
            py.Vel = new FixVec2(Fix.FromInt(net.Min.X > Fix.Zero ? 30 : -30), Fix.Zero);
            s.Pylons[0] = py;
            for (var t = 0; t < 240; t++)
            {
                _sim.Step(s, new PlayerInput[2]);
                Assert.IsFalse(net.Touches(s.Pylons[0].Pos, s.Pylons[0].Radius - Fix.Ratio(1, 100)), "in the goal box at tick " + t + ": " + s.Pylons[0].Pos);
            }
            // and a player shoving it in doesn't get it there either
            s.Players[1].Pos = s.Pylons[0].Pos - new FixVec2(net.Min.X > Fix.Zero ? Fix.Two : -Fix.Two, Fix.Zero);
            for (var t = 0; t < 240; t++)
            {
                _sim.Step(s, new[] { default(PlayerInput), new PlayerInput { MoveX = (sbyte)(net.Min.X > Fix.Zero ? 127 : -127) } });
                Assert.IsFalse(net.Touches(s.Pylons[0].Pos, s.Pylons[0].Radius - Fix.Ratio(1, 100)), "pushed into the goal box at tick " + t);
            }
        }

        [Test]
        public void PuckGunKnocksBackAndKnocksThePuckLoose()
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
            Assert.AreEqual(0.0, s.Players[1].Stun.ToDouble(), "no stun: a knockback, like a check");
            Assert.AreNotEqual(1, s.Carrier, "dropped the puck");
            Assert.AreEqual(1, s.Players[0].Bumps, "counts as a check landed");
            Assert.AreEqual(0, s.Shots.Length);
            // a second shot has to wait for the cooldown
            _sim.Step(s, new[] { default(PlayerInput), default(PlayerInput) });
            _sim.Step(s, new[] { Press(PlayerInput.ChargeBrake), default(PlayerInput) });
            Assert.AreEqual(0, s.Shots.Length, "still cooling down");
            Run(s, _sim.T.TicksPerSecond, default(PlayerInput), default(PlayerInput));
            _sim.Step(s, new[] { Press(PlayerInput.ChargeBrake), default(PlayerInput) });
            Assert.AreEqual(1, s.Shots.Length, "ready again after a second");
        }

        /// <summary>A Puck Gun hit shoves a light player further than a heavy one (like a check).</summary>
        [Test]
        public void PuckGunKnockbackScalesWithWeight()
        {
            System.Func<string, double> shove = card =>
            {
                Make();
                var s = Game(0, 1);
                Give(s, 0, "ultrapuck.puckgun");
                if (card != null) Give(s, 1, card);
                Play(s);
                s.Players[0].Pos = At(-4, 0); s.Players[0].Facing = FixVec2.Right;
                s.Players[1].Pos = At(1, 0);
                s.PuckPos = At(10, 8);
                _sim.Step(s, new[] { Press(PlayerInput.ChargeBrake), default(PlayerInput) });
                var best = 0.0;
                for (var t = 0; t < 30; t++) { _sim.Step(s, new PlayerInput[2]); best = System.Math.Max(best, s.Players[1].Vel.X.ToDouble()); }
                return best;
            };
            var normal = shove(null);
            var heavy = shove("ultrapuck.heavyweight"); // (Knockback Taken x0.2)
            Assert.Greater(normal, 3.0, "knocked back");
            Assert.Less(heavy, normal * 0.8, "a heavier player moves less");
        }

        [Test]
        public void BotsAreNeverOfferedThePuckGun()
        {
            var s = _sim.NewGame(new[] { 0, 1 }, 1u, new[] { false, true });
            var gun = _lib.ById("ultrapuck.puckgun").Index;
            var seen = false;
            for (var k = 0; k < 400; k++)
            {
                CollectionAssert.DoesNotContain(_lib.DrawOffer(s, 1, 3), gun, "bot");
                seen |= System.Array.IndexOf(_lib.DrawOffer(s, 0, 3), gun) >= 0;
            }
            Assert.IsTrue(seen, "people still get it");
        }

        /// <summary>Charging a shot with the puck: 30% slower.</summary>
        [Test]
        public void ChargingWithThePuckIsSlower()
        {
            System.Func<bool, double> speed = charge =>
            {
                var s = Game(0, 1);
                Play(s);
                s.Players[1].Pos = At(10, 8);
                s.Players[0].Pos = At(-8, -3);
                s.Carrier = 0;
                var input = new PlayerInput { MoveX = 127, Buttons = charge ? PlayerInput.ChargeBrake : (byte)0 };
                for (var t = 0; t < 120; t++) _sim.Step(s, new[] { input, default(PlayerInput) });
                return s.Players[0].Vel.Length.ToDouble();
            };
            var plain = speed(false);
            var charging = speed(true);
            Assert.AreEqual(plain * 0.7, charging, plain * 0.05, "30% slower while charging");
        }

        // ------------------------------------------------------------------ swapping onto bots (Simulation.Route)

        [Test]
        public void SwapTakesOverTheNextBotTeammateAndBack()
        {
            var s = _sim.NewGame(new[] { 0, 0, 0, 1 }, 1u, new[] { false, true, true, true });
            Play(s);
            var swap = new PlayerInput { Buttons = PlayerInput.Swap };
            _sim.Step(s, _sim.Route(s, new[] { swap, default(PlayerInput), default(PlayerInput), default(PlayerInput) }));
            Assert.AreEqual(1, Simulation.Steers(s, 0), "onto the first bot teammate");
            Assert.AreEqual(-1, Simulation.DriverOf(s, 0), "their own player is the bot's now");
            // held: still one swap
            _sim.Step(s, _sim.Route(s, new[] { swap, default(PlayerInput), default(PlayerInput), default(PlayerInput) }));
            Assert.AreEqual(1, Simulation.Steers(s, 0));
            _sim.Step(s, _sim.Route(s, new PlayerInput[4]));
            _sim.Step(s, _sim.Route(s, new[] { swap, default(PlayerInput), default(PlayerInput), default(PlayerInput) }));
            Assert.AreEqual(2, Simulation.Steers(s, 0), "the next one");
            _sim.Step(s, _sim.Route(s, new PlayerInput[4]));
            _sim.Step(s, _sim.Route(s, new[] { swap, default(PlayerInput), default(PlayerInput), default(PlayerInput) }));
            Assert.AreEqual(0, Simulation.Steers(s, 0), "round to their own player");
            // the controls go to whoever they steer
            _sim.Step(s, _sim.Route(s, new PlayerInput[4]));
            _sim.Step(s, _sim.Route(s, new[] { swap, default(PlayerInput), default(PlayerInput), default(PlayerInput) }));
            var routed = _sim.Route(s, new[] { new PlayerInput { MoveX = 100 }, default(PlayerInput), default(PlayerInput), default(PlayerInput) });
            Assert.AreEqual(100, routed[1].MoveX, "the bot they took over gets their stick");
        }

        [Test]
        public void ABotTeammateWithThePuckIsHandedOverUnlessTurnedOff()
        {
            foreach (var off in new[] { false, true })
            {
                var s = _sim.NewGame(new[] { 0, 0, 1 }, 1u, new[] { false, true, true });
                Play(s);
                var mine = new PlayerInput { Buttons = off ? PlayerInput.NoAutoSwap : (byte)0 };
                _sim.Step(s, _sim.Route(s, new[] { mine, default(PlayerInput), default(PlayerInput) }));
                s.Carrier = 1;
                _sim.Route(s, new[] { mine, default(PlayerInput), default(PlayerInput) });
                Assert.AreEqual(off ? 0 : 1, Simulation.Steers(s, 0), off ? "turned off: stays put" : "handed the bot with the puck");
                // the card screen: back on their own player
                s.Phase = Phase.Picking;
                _sim.Route(s, new[] { mine, default(PlayerInput), default(PlayerInput) });
                Assert.AreEqual(0, Simulation.Steers(s, 0));
            }
        }

        /// <summary>A bot carrying a teammate (Friendly Fire) fires them at the puck straight away.</summary>
        [Test]
        public void BotsThrowACarriedTeammateAtThePuck()
        {
            var s = _sim.NewGame(new[] { 0, 0, 1 }, 1u, new[] { true, true, true });
            Give(s, 0, "ultrapuck.friendlyfire");
            Play(s);
            s.Players[0].Pos = At(-4, 0); s.Players[0].Facing = FixVec2.Right;
            s.Players[1].HeldBy = 0;
            s.Players[2].Pos = At(10, 9);
            s.PuckPos = At(6, 0);
            var thrown = false;
            for (var t = 0; t < 120 && !thrown; t++)
            {
                _sim.Step(s, _sim.Route(s, new PlayerInput[3]));
                thrown = s.Players[1].HeldBy < 0;
            }
            Assert.IsTrue(thrown, "thrown within a second");
            Assert.Greater(s.Players[1].Vel.X.ToDouble(), 3.0, "towards the puck");
        }

        /// <summary>Your own player, run by the bot while you steer a bot teammate, waits between Swap uses like any bot.</summary>
        [Test]
        public void ABotRunPlayerDoesntSpamSwap()
        {
            var s = _sim.NewGame(new[] { 0, 0, 1 }, 1u, new[] { false, true, true });
            Give(s, 0, "ultrapuck.swap");
            Play(s);
            s.Players[0].Pilot = 1; // the person steers the bot teammate
            var swaps = 0;
            for (var t = 0; t < 2 * _sim.T.TicksPerSecond; t++)
            {
                var before = s.Players[0].Swaps + s.Rocks.Length;
                _sim.Step(s, _sim.Route(s, new PlayerInput[3]));
                if (s.Players[0].Swaps + s.Rocks.Length != before) swaps++;
            }
            Assert.LessOrEqual(swaps, 1, "at most one use in two seconds (bots wait three)");
        }

        /// <summary>The speed of a quick shot (shoot pressed with the puck) for a player holding these cards.</summary>
        private double QuickShot(params string[] cards)
        {
            var s = Game(0, 1);
            if (cards.Length > 0) Give(s, 0, cards);
            Play(s);
            s.Players[1].Pos = At(10, 8);
            s.Players[0].Pos = At(-6, 0); s.Players[0].Facing = FixVec2.Right; s.Players[0].Vel = FixVec2.Zero;
            s.Carrier = 0;
            _sim.Step(s, new[] { Press(PlayerInput.ShootCheck), default(PlayerInput) });
            return s.PuckVel.Length.ToDouble();
        }

        [Test]
        public void PowerShotIsAFullChargeAndOverChargeDoesntAdd()
        {
            var full = _sim.T.ChargedShotMax.ToDouble();
            Assert.AreEqual(_sim.T.ShotSpeed.ToDouble(), QuickShot(), 0.3, "a plain quick shot");
            Assert.AreEqual(full, QuickShot("ultrapuck.powershot"), 0.3, "Power Shot: a full charge's speed");
            Assert.AreEqual(full, QuickShot("ultrapuck.powershot", "ultrapuck.overcharge"), 0.3, "Over Charge doesn't add to it");
            Assert.AreEqual(1, _lib.ById("ultrapuck.powershot").MaxPerPlayer);
        }

        [Test]
        public void InvisibleQuickShotHidesThePuckForHalfASecond()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.ghostshot");
            Play(s);
            s.Players[1].Pos = At(10, 8);
            s.Players[0].Pos = At(-6, -3); s.Players[0].Facing = FixVec2.Right;
            s.Carrier = 0;
            _sim.Step(s, new[] { Press(PlayerInput.ShootCheck), default(PlayerInput) });
            Assert.Greater(s.PuckHidden.ToDouble(), 0.45);
            Assert.AreEqual(0, s.PuckHiddenTeam);
            Run(s, _sim.T.TicksPerSecond * 6 / 10, default(PlayerInput), default(PlayerInput));
            Assert.AreEqual(0.0, s.PuckHidden.ToDouble(), "seen again");
            // a charged shot isn't hidden
            s.Carrier = 0; s.PuckHidden = Fix.Zero;
            var hold = new PlayerInput { Buttons = PlayerInput.ChargeBrake };
            for (var t = 0; t < 60; t++) _sim.Step(s, new[] { hold, default(PlayerInput) });
            _sim.Step(s, new PlayerInput[2]);
            Assert.AreEqual(0.0, s.PuckHidden.ToDouble());
        }

        [Test]
        public void CannonBallKnocksBackTheOpponentWhoCatchesIt()
        {
            System.Func<bool, double> catcher = cannon =>
            {
                Make();
                var s = Game(0, 1);
                if (cannon) Give(s, 0, "ultrapuck.cannonball");
                Play(s);
                s.Players[0].Pos = At(-8, 0); s.Players[0].Facing = FixVec2.Right;
                s.Players[1].Pos = At(6, 0); s.Players[1].Facing = new FixVec2(-Fix.One, Fix.Zero);
                s.Carrier = 0;
                var hold = new PlayerInput { Buttons = PlayerInput.ChargeBrake };
                // the catcher skates at the shooter the whole time (like a bot going for the puck)
                var at = new PlayerInput { MoveX = -127 };
                for (var t = 0; t < 130; t++) { _sim.Step(s, new[] { hold, default(PlayerInput) }); s.Players[1].Pos = At(6, 0); }
                _sim.Step(s, new[] { default(PlayerInput), at }); // let go: a full charge
                var caughtAt = -1.0;
                for (var t = 0; t < 120 && caughtAt < 0; t++) { _sim.Step(s, new[] { default(PlayerInput), at }); if (s.Carrier == 1) caughtAt = s.Players[1].Pos.X.ToDouble(); }
                Assert.GreaterOrEqual(caughtAt, -100.0, "caught it");
                Assert.AreEqual(0.0, s.Players[1].HitLag.ToDouble(), "no hit pause");
                for (var t = 0; t < 60; t++) _sim.Step(s, new[] { default(PlayerInput), at });
                return s.Players[1].Pos.X.ToDouble() - caughtAt; // how far along the shot (+x) they went in half a second
            };
            var plain = catcher(false);
            var cannon = catcher(true);
            Assert.Less(plain, 0.0, "without it, they just keep skating at the shooter");
            Assert.Greater(cannon, 3.0, "carried off the way the shot was going, even skating against it");
        }

        [Test]
        public void TheHostCanAddABotMidMatch()
        {
            var s = Game(0, 1);
            Play(s);
            _sim.Step(s, new[] { new PlayerInput { Pick = PlayerInput.AddBotBlue }, default(PlayerInput) });
            Assert.AreEqual(3, s.Players.Length);
            Assert.AreEqual(1, s.Players[2].Team);
            Assert.IsTrue(s.Players[2].Bot);
            _sim.Step(s, _sim.Route(s, new PlayerInput[3]));
            Assert.AreEqual(3, s.Players.Length, "once");
        }

        [Test]
        public void TheHostCanTakeABotOutMidMatch()
        {
            var s = _sim.NewGame(new[] { 0, 1, 1, 0 }, 1u, new[] { false, false, true, true });
            Play(s);
            s.Carrier = 3;
            s.Players[1].HeldBy = 3;
            _sim.Step(s, new[] { new PlayerInput { Pick = (byte)(PlayerInput.RemoveBot + 1) }, default(PlayerInput), default(PlayerInput), default(PlayerInput) });
            Assert.AreEqual(4, s.Players.Length, "a person is never taken out");
            _sim.Step(s, _sim.Route(s, new[] { new PlayerInput { Pick = (byte)(PlayerInput.RemoveBot + 2) }, default(PlayerInput), default(PlayerInput), default(PlayerInput) }));
            Assert.AreEqual(3, s.Players.Length, "the bot is gone");
            Assert.IsTrue(s.Players[2].Bot && s.Players[2].Team == 0, "the one after it (red bot 3) moved down");
            Assert.AreEqual(2, s.Carrier, "the carrier's number moved down with them");
            Assert.AreEqual(2, s.Players[1].HeldBy, "so did who carries whom");
        }

        [Test]
        public void GoalsAndAssistsAreCounted()
        {
            var s = Game(0, 0, 1);
            Play(s);
            s.Players[2].Pos = At(-12, 8);
            var net = _sim.Map.Goals.Find(g => g.Team == 1);
            var toward = net.Min.X > Fix.Zero ? 1 : -1;
            // player 1 has it, then player 0 (the pass), then 0 shoots it in
            s.Players[0].Pos = At(toward * 3, 0); s.Players[1].Pos = At(0, 5);
            s.Carrier = 1; _sim.Step(s, new PlayerInput[3]);
            s.Carrier = 0; _sim.Step(s, new PlayerInput[3]);
            s.PassFrom = 0; s.Carrier = -1;
            var mid = new FixVec2((net.Min.X + net.Max.X) / 2, (net.Min.Y + net.Max.Y) / 2);
            s.PuckPos = mid; s.PuckVel = FixVec2.Zero;
            _sim.Step(s, new PlayerInput[3]);
            Assert.AreEqual(Phase.Scored, s.Phase);
            Assert.AreEqual(1, s.Players[0].Goals);
            Assert.AreEqual(1, s.Players[1].Assists);
            Assert.AreEqual(0, s.LastGoalBy);
            Assert.AreEqual(1, s.LastAssist);
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

        /// <summary>Friendly Fire: player 0 (with the card) checks teammate 1, who has the puck, and picks them up.</summary>
        private GameState Grab(bool card = true)
        {
            var s = Game(0, 0, 1);
            if (card) Give(s, 0, "ultrapuck.friendlyfire");
            Play(s);
            s.Players[2].Pos = At(-10, 8);
            s.Players[0].Pos = At(-1, -6); s.Players[0].Facing = FixVec2.Right;
            s.Players[1].Pos = At(1.5, -6); s.Players[1].Facing = FixVec2.Right;
            s.Carrier = 1; s.PuckTeam = 0;
            s.PuckPos = At(2.3, -6);
            // with the card, holding charge/brake (that's what picks them up); without, a plain check
            var held = card ? PlayerInput.ChargeBrake : (byte)0;
            _sim.Step(s, new[] { Press((byte)(PlayerInput.ShootCheck | held)), default(PlayerInput), default(PlayerInput) });
            for (var t = 0; t < 40 && s.Players[1].Hits == 0; t++) _sim.Step(s, new[] { new PlayerInput { MoveX = 127, Buttons = held }, default(PlayerInput), default(PlayerInput) });
            // until the hit lag is over (still holding: letting go throws them)
            for (var t = 0; t < 60 && s.Players[0].HitLag > Fix.Zero; t++) _sim.Step(s, new[] { new PlayerInput { Buttons = held }, default(PlayerInput), default(PlayerInput) });
            return s;
        }

        private static readonly PlayerInput Hold = new PlayerInput { Buttons = PlayerInput.ChargeBrake };

        [Test]
        public void FriendlyFireOnlyPicksUpWhileHoldingCharge()
        {
            var s = Game(0, 0, 1);
            Give(s, 0, "ultrapuck.friendlyfire");
            Play(s);
            s.Players[2].Pos = At(-10, 8);
            s.Players[0].Pos = At(-1, -6); s.Players[0].Facing = FixVec2.Right;
            s.Players[1].Pos = At(1.5, -6);
            for (var t = 0; t < 120; t++) _sim.Step(s, new[] { new PlayerInput { MoveX = 127 }, default(PlayerInput), default(PlayerInput) });
            Assert.AreEqual(-1, s.Players[1].HeldBy, "just skating into them: not picked up");
            for (var t = 0; t < 30 && s.Players[1].HeldBy < 0; t++) _sim.Step(s, new[] { new PlayerInput { MoveX = 127, Buttons = PlayerInput.ChargeBrake }, default(PlayerInput), default(PlayerInput) });
            Assert.AreEqual(0, s.Players[1].HeldBy, "holding charge: picked up");
        }

        [Test]
        public void CarryingATeammateIsntSlowedByTheCharge()
        {
            System.Func<bool, double> run = mate =>
            {
                Make();
                var s = Game(0, 0, 1);
                Give(s, 0, "ultrapuck.friendlyfire");
                Play(s);
                s.Players[2].Pos = At(10, 8);
                s.Players[0].Pos = At(-10, -6); s.Players[0].Facing = FixVec2.Right; s.Players[0].Vel = FixVec2.Zero;
                s.Players[1].Pos = At(-10, 6);
                if (mate) s.Players[1].HeldBy = 0;
                else { s.Carrier = 0; s.PuckPos = At(-9.2, -6); }
                for (var t = 0; t < 120; t++) _sim.Step(s, new[] { new PlayerInput { MoveX = 127, Buttons = PlayerInput.ChargeBrake }, default(PlayerInput), default(PlayerInput) });
                return s.Players[0].Pos.X.ToDouble() + 10;
            };
            Assert.Greater(run(true), run(false) * 1.2, "a held teammate doesn't slow you like charging a shot does");
        }

        [Test]
        public void FriendlyFireThrowsLikeAShotAndSpeedsThemUp()
        {
            var s = Grab();
            Assert.AreEqual(0, s.Players[1].HeldBy);
            s.Players[0].Charge = Fix.One;
            _sim.Step(s, new PlayerInput[3]); // let go: a full-charge throw
            Assert.AreEqual(-1, s.Players[1].HeldBy, "thrown");
            Assert.Greater(s.Players[1].Vel.Length.ToDouble(), _sim.T.ChargedShotMax.ToDouble() * 0.9, "as fast as a full-charge shot");
            Assert.AreEqual(0.15, s.Players[1].Fast.ToDouble(), 1e-3, "15% faster after");
            Assert.Greater(s.Players[1].FastTime.ToDouble(), 3.9, "for 4 s");
            var from = s.Players[1].Pos;
            for (var t = 0; t < 60; t++) _sim.Step(s, new PlayerInput[3]);
            Assert.Greater((s.Players[1].Pos - from).Length.ToDouble(), 15.0, "and flies a long way");
        }

        [Test]
        public void FriendlyFirePicksUpATeammateWithThePuck()
        {
            var s = Grab();
            Assert.AreEqual(0, s.Players[1].HeldBy, "picked up");
            Assert.AreEqual(1, s.Carrier, "still has the puck");
            Assert.AreEqual(Fix.Zero, s.Players[1].Stun, "not dazed");
            // carried along: they stay just in front of the carrier
            for (var t = 0; t < 30; t++) _sim.Step(s, new[] { new PlayerInput { MoveY = 127, Buttons = PlayerInput.ChargeBrake }, default(PlayerInput), default(PlayerInput) });
            Assert.AreEqual(0, s.Players[1].HeldBy);
            var gap = (s.Players[1].Pos - s.Players[0].Pos).Length.ToDouble();
            Assert.AreEqual(1.0, gap, 0.1, "right in front of the carrier");

            var plain = Grab(false);
            Assert.AreEqual(-1, plain.Players[1].HeldBy, "without the card, a check is a check");
            Assert.AreEqual(-1, plain.Carrier, "and knocks the puck loose");
        }

        /// <summary>Picking a teammate up is as easy as the puck: just skate into them, no check needed.</summary>
        [Test]
        public void FriendlyFirePicksUpATeammateJustByTouching()
        {
            var s = Game(0, 0, 1);
            Give(s, 0, "ultrapuck.friendlyfire");
            Play(s);
            s.Players[2].Pos = At(-10, 8);
            s.Players[0].Pos = At(-1, -6); s.Players[0].Facing = FixVec2.Right;
            s.Players[1].Pos = At(1.5, -6);
            for (var t = 0; t < 120 && s.Players[1].HeldBy < 0; t++) _sim.Step(s, new[] { new PlayerInput { MoveX = 127, Buttons = PlayerInput.ChargeBrake }, default(PlayerInput), default(PlayerInput) });
            Assert.AreEqual(0, s.Players[1].HeldBy, "picked up by skating into them (holding charge)");
            Assert.AreEqual(0, s.Players[1].Hits, "no check happened");
            // a teammate bumping into your back or side isn't picked up: only on the wedge
            var side = Game(0, 0, 1);
            Give(side, 0, "ultrapuck.friendlyfire");
            Play(side);
            side.Players[2].Pos = At(-10, 8);
            side.Players[0].Pos = At(0, -6); side.Players[0].Facing = FixVec2.Right;
            side.Players[1].Pos = At(-2.5, -6); // behind, skating into their back
            for (var t = 0; t < 120; t++) _sim.Step(side, new[] { Hold, new PlayerInput { MoveX = 127 }, default(PlayerInput) });
            Assert.AreEqual(-1, side.Players[1].HeldBy, "touching the back doesn't pick them up");
            // thrown: not straight back into the thrower's arms
            _sim.Step(s, new[] { Press(PlayerInput.ShootCheck), default(PlayerInput), default(PlayerInput) });
            Assert.AreEqual(-1, s.Players[1].HeldBy, "thrown");
            Run(s, 20, default(PlayerInput), default(PlayerInput), default(PlayerInput));
            Assert.AreEqual(-1, s.Players[1].HeldBy, "still flying, not grabbed again");
        }

        [Test]
        public void FriendlyFireThrowsTheTeammateAndTheyKeepThePuck()
        {
            var s = Grab();
            Assert.AreEqual(0, s.Players[1].HeldBy);
            // face up the rink and throw
            for (var t = 0; t < 30; t++) _sim.Step(s, new[] { new PlayerInput { AimY = 127, Buttons = PlayerInput.ChargeBrake }, default(PlayerInput), default(PlayerInput) });
            Assert.AreEqual(1, s.Carrier, "kept the puck while carried round");
            Assert.Greater(s.Players[1].Pos.Y.ToDouble(), s.Players[0].Pos.Y.ToDouble() + 0.8, "carried round to the front");
            _sim.Step(s, new[] { new PlayerInput { AimY = 127, Buttons = PlayerInput.ShootCheck | PlayerInput.ChargeBrake }, default(PlayerInput), default(PlayerInput) });
            Assert.AreEqual(-1, s.Players[1].HeldBy, "thrown");
            Assert.Greater(s.Players[1].TossTime.ToDouble(), 0.5, "flying");
            Assert.Greater(s.Players[1].Vel.Y.ToDouble(), 5.0, "thrown the way the carrier faces");
            for (var t = 0; t < 20; t++) _sim.Step(s, new PlayerInput[3]);
            Assert.AreEqual(1, s.Carrier, "they kept the puck while flying");
        }

        [Test]
        public void FriendlyFireThrowsAnywayAfterAWhile()
        {
            var s = Grab();
            Assert.AreEqual(0, s.Players[1].HeldBy);
            Run(s, (int)(_sim.T.MateHoldMax.ToDouble() * _sim.T.TicksPerSecond) + 2, Hold, default(PlayerInput), default(PlayerInput));
            Assert.AreEqual(-1, s.Players[1].HeldBy, "not carried forever");
        }

        [Test]
        public void ShockwaveKnocksThePuckOffItsCarrier()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.shockwave");
            Play(s);
            s.Players[0].Pos = At(0, -6);
            s.Players[1].Pos = At(1.5, -6); s.Players[1].Facing = FixVec2.Right;
            s.Carrier = 1; s.PuckTeam = 1;
            s.PuckPos = At(2.3, -6);
            for (var t = 0; t < 120; t++) { s.Players[1].Pos = At(1.5, -6); _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) }); }
            Assert.AreEqual(0, s.Players[0].Shocks, "held: not yet");
            _sim.Step(s, new PlayerInput[2]); // let go
            Assert.AreEqual(1, s.Players[0].Shocks);
            Assert.AreEqual(-1, s.Carrier, "the carrier lost the puck");
            Assert.Greater(s.PuckVel.X.ToDouble(), 0.5, "it pops away from the shockwave");
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
            _sim.Step(s, new PlayerInput[3]); // a tap: let go straight away
            Assert.AreEqual(1, s.Players[0].Shocks, "a shockwave went out");
            Assert.Greater(s.Players[1].Vel.X.ToDouble(), 0.5, "foe shoved away");
            Assert.Less(s.Players[2].Vel.X.ToDouble(), -0.5, "teammate shoved away too");
            Assert.AreEqual(Fix.Zero, s.Players[1].SlowTime, "no slow — push only");
            Assert.AreEqual(1.5, s.Players[0].ShockRadius.ToDouble(), 0.05, "a tap: the smallest one");
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput), default(PlayerInput) });
            _sim.Step(s, new PlayerInput[3]);
            Assert.AreEqual(2, s.Players[0].Shocks, "no cooldown: another straight away");
            for (var t = 0; t < 120 * 7; t++) _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput), default(PlayerInput) });
            _sim.Step(s, new PlayerInput[3]);
            Assert.AreEqual(6.0, s.Players[0].ShockRadius.ToDouble(), 0.01, "held 6 s or more: 6 units, no bigger");
        }

        [Test]
        public void ShockwaveAlsoPushesThePuckAndRocks()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.shockwave");
            Play(s);
            s.Players[0].Pos = At(0, -6);
            s.Players[1].Pos = At(8, 0);
            s.Carrier = -1;
            s.Players[0].Facing = FixVec2.Right;
            s.PuckPos = At(0, -4.8); // beside them, off the wedge (they'd scoop it up)
            s.PuckVel = FixVec2.Zero;
            s.Rocks = new[] { new Rock { Pos = At(-1.2, -6), Vel = new FixVec2(Fix.Zero, -Fix.One), Owner = 0, Carrier = -1 } };
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            _sim.Step(s, new PlayerInput[2]);
            Assert.AreEqual(1, s.Players[0].Shocks);
            Assert.Greater(s.PuckVel.Y.ToDouble(), 0.5, "puck shoved away");
            Assert.Less(s.Rocks[0].Vel.X.ToDouble(), -0.5, "rock shoved away");
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
        public void SwapDoesNothingWhileHoldingThePuck()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.swap");
            Play(s);
            s.Players[0].Pos = At(0, -6);
            s.Players[0].Facing = FixVec2.Right;
            s.Carrier = 0;
            s.PuckPos = At(0.5, -6);
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            Assert.AreEqual(0, s.Rocks.Length, "no rock while carrying the puck");
            s.Rocks = new[] { new Rock { Pos = At(4, -6), Vel = FixVec2.Zero, Owner = 0, Carrier = -1 } };
            var at = s.Players[0].Pos;
            _sim.Step(s, new PlayerInput[2]);
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            Assert.AreEqual(at, s.Players[0].Pos, "no teleport while carrying the puck");
            Assert.AreEqual(0, s.Players[0].Swaps);
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
            s.Rocks[0] = new Rock { Pos = At(4, -6), Vel = new FixVec2(Fix.FromInt(2), Fix.Zero), Owner = 0, Carrier = -1 };
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
            Assert.Less(s.Rocks[0].Vel.X.ToDouble(), -3.0, "rock kept its own leftward momentum, not the player's");
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
            s.Rocks = new[] { new Rock { Pos = At(1.2, -6), Vel = new FixVec2(Fix.Zero, Fix.One), Owner = 0, Carrier = -1 } }; // (a still one would be gone)
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
            s.Rocks[0] = new Rock { Pos = At(8, -6), Vel = new FixVec2(Fix.Zero, Fix.One), Owner = 0, Carrier = -1 }; // (a still one would be gone)
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

        /// <summary>Ice Trail: a shot lays ice behind the puck that slows opponents and speeds teammates up, then melts.</summary>
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
            Assert.AreEqual(0.6, s.Players[2].Fast.ToDouble(), 1e-3, "a teammate is 60% faster");
            Assert.Greater(s.Players[2].FastTime.ToDouble(), 0.0);
            Assert.AreEqual(Fix.Zero, s.Players[1].FastTime, "an opponent isn't");

            for (var t = 0; t < 120 * 20; t++) _sim.Step(s, new PlayerInput[3]);
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
            for (var t = 0; t < 120 * 12; t++) _sim.Step(s, new PlayerInput[2]);
            Assert.Greater(s.Ice.Length, 0, "still there after 14 s: it lasts 15");
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
        public void TeamPlaySpeedsUpBothAfterACleanPass()
        {
            var s = Pass("ultrapuck.teamplay", At(10, 8));
            Assert.AreEqual(0.8, s.Players[0].Fast.ToDouble(), 1e-3, "the passer is 80% faster");
            Assert.AreEqual(0.8, s.Players[1].Fast.ToDouble(), 1e-3, "and the teammate");
            Assert.Greater(s.Players[0].FastTime.ToDouble(), 2.9, "for 3 s");
            Assert.AreEqual(Fix.Zero, s.Players[2].FastTime, "not the other team");
            for (var t = 0; t < 120 * 3 + 2; t++) _sim.Step(s, new PlayerInput[3]);
            Assert.AreEqual(Fix.Zero, s.Players[1].FastTime, "worn off");
        }

        [Test]
        public void TeamPlayMakesYouFaster()
        {
            System.Func<bool, double> run = fast =>
            {
                Make();
                var s = Game(0, 1);
                Play(s);
                s.Players[0].Pos = At(-10, -6); s.Players[0].Vel = FixVec2.Zero;
                s.Players[1].Pos = At(10, 8);
                if (fast) { s.Players[0].Fast = Fix.Ratio(8, 10); s.Players[0].FastTime = Fix.FromInt(3); }
                for (var t = 0; t < 120; t++) _sim.Step(s, new[] { new PlayerInput { MoveX = 127 }, default(PlayerInput) });
                return s.Players[0].Pos.X.ToDouble() + 10;
            };
            Assert.Greater(run(true), run(false) * 1.5, "skates much further in a second");
        }

        [Test]
        public void IceTrailComesOffQuickShotsToo()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.icetrail");
            Play(s);
            s.Players[0].Pos = At(-8, -6); s.Players[0].Facing = FixVec2.Right;
            s.Players[1].Pos = At(8, 7);
            s.Carrier = 0; s.PuckPos = At(-7.15, -6);
            _sim.Step(s, new[] { Press(PlayerInput.ShootCheck), default(PlayerInput) });
            Assert.AreEqual(-1, s.Carrier, "a quick shot");
            for (var t = 0; t < 30; t++) _sim.Step(s, new PlayerInput[2]);
            Assert.Greater(s.Ice.Length, 3, "ice behind it");
        }

        [Test]
        public void ASwapRockIsGoneOnceItStops()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.swap");
            Play(s);
            s.PuckPos = At(14, 0);
            s.Players[0].Pos = At(-6, -6); s.Players[1].Pos = At(10, 8);
            s.Rocks = new[] { new Rock { Pos = At(0, -6), Vel = new FixVec2(Fix.FromInt(4), Fix.Zero), Owner = 0, Carrier = -1 } };
            _sim.Step(s, new PlayerInput[2]);
            Assert.AreEqual(1, s.Rocks.Length, "still going");
            s.Rocks[0].Vel = new FixVec2(Fix.Ratio(1, 5), Fix.Zero);
            _sim.Step(s, new PlayerInput[2]);
            Assert.AreEqual(0, s.Rocks.Length, "stopped: gone");
            // a held rock stays, even standing still
            s.Players[0].NoPickup = Fix.Zero; s.Players[0].Facing = FixVec2.Right; s.Players[0].Vel = FixVec2.Zero;
            s.Rocks = new[] { new Rock { Pos = At(-5.1, -6), Vel = FixVec2.Zero, Owner = 0, Carrier = 0 } };
            for (var t = 0; t < 120; t++) _sim.Step(s, new PlayerInput[2]);
            Assert.AreEqual(1, s.Rocks.Length, "held: stays");
        }

        [Test]
        public void NoSwappingToARockSomeoneHolds()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.swap");
            Play(s);
            s.PuckPos = At(14, 0);
            s.Players[0].Pos = At(-6, -6);
            s.Players[1].Pos = At(4, -6); s.Players[1].Facing = FixVec2.Right;
            s.Rocks = new[] { new Rock { Pos = At(4.9, -6), Vel = FixVec2.Zero, Owner = 0, Carrier = 1 } };
            var at = s.Players[0].Pos;
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            Assert.AreEqual(0, s.Players[0].Swaps, "no swap");
            Assert.AreEqual(at.X.ToDouble(), s.Players[0].Pos.X.ToDouble(), 0.05, "still where they were");
            Assert.AreEqual(1, s.Rocks[0].Carrier, "the opponent still has it");
        }

        [Test]
        public void ACannonBallShovingTheCatcherIntoTheirNetScores()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.cannonball");
            Play(s);
            var net = _sim.Map.Goals.Find(g => g.Team == 1);
            var side = net.Min.X > Fix.Zero ? 1 : -1;
            var mouth = (side > 0 ? net.Min.X : net.Max.X).ToDouble();
            // the catcher stands just in front of their own net, the shooter fires a full charge into them
            s.Players[1].Pos = At(mouth - side * 2.2, (net.Min.Y + net.Max.Y).ToDouble() / 2); s.Players[1].Facing = new FixVec2(Fix.FromInt(-side), Fix.Zero);
            s.Players[0].Pos = At(mouth - side * 12, s.Players[1].Pos.Y.ToDouble()); s.Players[0].Facing = new FixVec2(Fix.FromInt(side), Fix.Zero);
            s.Carrier = 0;
            var hold = new PlayerInput { Buttons = PlayerInput.ChargeBrake };
            var at = s.Players[1].Pos;
            for (var t = 0; t < 130; t++) { _sim.Step(s, new[] { hold, default(PlayerInput) }); s.Players[1].Pos = at; s.Players[1].Vel = FixVec2.Zero; }
            _sim.Step(s, new PlayerInput[2]);
            for (var t = 0; t < 120 && s.Phase == Phase.Play; t++) _sim.Step(s, new PlayerInput[2]);
            Assert.AreEqual(Phase.Scored, s.Phase, "shoved in with the puck: a goal");
            Assert.AreEqual(0, s.LastScorer, "for the shooter's team");
            Assert.AreEqual(0, s.LastGoalBy, "the shooter's goal");
        }

        [Test]
        public void CarryingThePuckIntoYourOwnNetIsStillSafeWithoutACannonBall()
        {
            var s = Game(0, 1);
            Play(s);
            var net = _sim.Map.Goals.Find(g => g.Team == 1);
            var side = net.Min.X > Fix.Zero ? 1 : -1;
            var mouth = (side > 0 ? net.Min.X : net.Max.X).ToDouble();
            s.Players[1].Pos = At(mouth - side * 2, (net.Min.Y + net.Max.Y).ToDouble() / 2); s.Players[1].Facing = new FixVec2(Fix.FromInt(side), Fix.Zero);
            s.Players[0].Pos = At(0, 8);
            s.Carrier = 1;
            for (var t = 0; t < 120; t++) _sim.Step(s, new[] { default(PlayerInput), new PlayerInput { MoveX = (sbyte)(side * 127) } });
            Assert.AreEqual(Phase.Play, s.Phase, "no goal");
        }

        [Test]
        public void StoneWallRisesBehindAFullChargeAndCrumbles()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.stonewall");
            Play(s);
            s.Players[0].Pos = At(-10, -5); s.Players[0].Facing = FixVec2.Right;
            s.Players[1].Pos = At(10, 8);
            s.Carrier = 0;
            var hold = new PlayerInput { Buttons = PlayerInput.ChargeBrake };
            for (var t = 0; t < 130; t++) _sim.Step(s, new[] { hold, default(PlayerInput) });
            _sim.Step(s, new PlayerInput[2]);
            for (var t = 0; t < 70; t++) _sim.Step(s, new PlayerInput[2]);
            Assert.Greater(s.Stones.Length, 3, "a wall behind the puck");
            var len = 0.0;
            foreach (var st in s.Stones) len += (st.B - st.A).Length.ToDouble();
            Assert.AreEqual(-5.0, s.Stones[0].A.Y.ToDouble(), 0.2, "along the shot");
            Assert.Greater(s.Stones[0].A.X.ToDouble(), -9.5, "starting past the shooter");
            Assert.Greater(len, 12.0, "a long way behind the puck");
            Assert.LessOrEqual(len, 30.01, "no longer than 30");
            // someone skating across it is stopped
            var mid = s.Stones[s.Stones.Length / 2];
            var x = ((mid.A.X + mid.B.X) / 2).ToDouble();
            s.Players[1].Pos = At(x, -2); s.Players[1].Vel = FixVec2.Zero;
            for (var t = 0; t < 90; t++) _sim.Step(s, new[] { default(PlayerInput), new PlayerInput { MoveY = -127 } });
            Assert.Greater(s.Players[1].Pos.Y.ToDouble(), -5.0, "can't get through");
            var first = s.Stones[0];
            for (var t = 0; t < 120 * 4; t++) _sim.Step(s, new PlayerInput[2]);
            Assert.AreEqual(first.A, s.Stones[0].A, "the first piece still standing after 4 s");
            for (var t = 0; t < 120 * 6; t++) _sim.Step(s, new PlayerInput[2]);
            Assert.AreEqual(0, s.Stones.Length, "all crumbled (each piece lasts 5.5 s)");
        }

        [Test]
        public void StoneWallCarriesOnAfterTheBoards()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.stonewall");
            Play(s);
            // shot hard into the side boards at an angle: the wall goes on along the bounce
            s.Players[0].Pos = At(-6, 2); s.Players[0].Facing = new FixVec2(Fix.Ratio(6, 10), Fix.Ratio(8, 10));
            s.Players[1].Pos = At(10, -8);
            s.Carrier = 0;
            var hold = new PlayerInput { Buttons = PlayerInput.ChargeBrake };
            for (var t = 0; t < 130; t++) _sim.Step(s, new[] { hold, default(PlayerInput) });
            _sim.Step(s, new PlayerInput[2]);
            var bounced = false;
            for (var t = 0; t < 120 && !bounced; t++) { _sim.Step(s, new PlayerInput[2]); bounced = s.PuckVel.Y < Fix.Zero; }
            Assert.IsTrue(bounced, "off the boards");
            var before = s.Stones.Length;
            for (var t = 0; t < 30; t++) _sim.Step(s, new PlayerInput[2]);
            Assert.Greater(s.Stones.Length, before, "more wall after the bounce");
            Assert.Less(s.Stones[s.Stones.Length - 1].B.Y.ToDouble(), s.Stones[before > 0 ? before - 1 : 0].B.Y.ToDouble(), "heading back down the rink");
        }

        [Test]
        public void NoStoneWallFromAQuickShot()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.stonewall");
            Play(s);
            s.Players[0].Pos = At(-10, -5); s.Players[0].Facing = FixVec2.Right;
            s.Carrier = 0;
            _sim.Step(s, new[] { Press(PlayerInput.ShootCheck), default(PlayerInput) });
            for (var t = 0; t < 40; t++) _sim.Step(s, new PlayerInput[2]);
            Assert.AreEqual(0, s.Stones.Length);
        }

        /// <summary>Holding charge/brake without the puck charges a shot; a pass arriving can be fired straight off (a one-timer).</summary>
        [Test]
        public void ChargeWithoutThePuckForAOneTimer()
        {
            var s = Game(0, 0, 1);
            Play(s);
            s.Players[2].Pos = At(10, 8);
            s.Players[0].Pos = At(-6, -5); s.Players[0].Facing = FixVec2.Right;
            s.Players[1].Pos = At(0, -5); s.Players[1].Facing = new FixVec2(-Fix.One, Fix.Zero);
            s.Carrier = 0;
            var hold = new PlayerInput { Buttons = PlayerInput.ChargeBrake };
            _sim.Step(s, new[] { Press(PlayerInput.ShootCheck), hold, default(PlayerInput) });
            for (var t = 0; t < 120 && s.Carrier != 1; t++) _sim.Step(s, new[] { default(PlayerInput), hold, default(PlayerInput) });
            Assert.AreEqual(1, s.Carrier, "the pass arrived");
            Assert.Greater(s.Players[1].Charge.ToDouble(), 0.2, "charged while waiting, and kept it on the pickup");
            // let go: a charged shot straight away
            _sim.Step(s, new[] { default(PlayerInput), default(PlayerInput), default(PlayerInput) });
            Assert.AreEqual(-1, s.Carrier, "fired");
            Assert.Greater(s.PuckVel.Length.ToDouble(), 12.0, "a charged shot, as hard as that much charge makes it");
            // without the puck, letting go just drops the charge
            for (var t = 0; t < 30; t++) _sim.Step(s, new[] { hold, default(PlayerInput), default(PlayerInput) });
            Assert.Greater(s.Players[0].Charge.ToDouble(), 0.1);
            _sim.Step(s, new PlayerInput[3]);
            Assert.AreEqual(Fix.Zero, s.Players[0].Charge, "gone, nothing fired");
        }

        private FixVec2 _lastDashDir;

        /// <summary>Dash where you move: along the move stick when it's pushed, where you face when it isn't (or with the setting off).</summary>
        [Test]
        public void DashGoesWhereYouMoveWithTheSetting()
        {
            System.Func<sbyte, byte, FixVec2> dash = (moveY, extra) =>
            {
                var s = Game(0, 1);
                Play(s);
                s.Players[0].Pos = At(0, -3); s.Players[0].Facing = FixVec2.Right; s.Players[0].Vel = FixVec2.Zero;
                s.Players[1].Pos = At(10, 8);
                // aim right (so facing stays right), move up, press check
                _sim.Step(s, new[] { new PlayerInput { AimX = 127, MoveY = moveY, Buttons = (byte)(PlayerInput.ShootCheck | extra) }, default(PlayerInput) });
                _lastDashDir = s.Players[0].DashDir;
                return s.Players[0].Vel;
            };
            var along = dash(127, PlayerInput.DashMove);
            Assert.Greater(_lastDashDir.Y.ToDouble(), 0.99, "the dash lines go the way the dash did");
            Assert.Greater(along.Y.ToDouble(), 5.0, "dashed up, the way the stick pushes");
            Assert.Less(System.Math.Abs(along.X.ToDouble()), 1.0);
            var still = dash(0, PlayerInput.DashMove);
            Assert.Greater(still.X.ToDouble(), 5.0, "not moving: dashed where they look");
            var off = dash(127, 0);
            Assert.Greater(off.X.ToDouble(), 5.0, "setting off: always where they look");
        }

        [Test]
        public void TeamPlayDoesntCountIfAnOpponentTouchesIt()
        {
            // close enough to the puck's path to touch it, too far to pick it up
            var s = Pass("ultrapuck.teamplay", At(-3, -4.05));
            Assert.AreEqual(0, s.Players[0].Refreshes);
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
        public void TeamCardsNeedATeam()
        {
            var solo = Game(0, 1, 1);
            var tp = _lib.ById("ultrapuck.teamplay").Index;
            var ff = _lib.ById("ultrapuck.friendlyfire").Index;
            Assert.IsNull(_lib.ById("ultrapuck.teamplay2"), "Team Play II is gone (Team Play does what it did)");
            for (var k = 0; k < 300; k++)
            {
                var offer = _lib.DrawOffer(solo, 0, 3);
                CollectionAssert.DoesNotContain(offer, tp, "orange is on their own");
                CollectionAssert.DoesNotContain(offer, ff);
            }
            var seen = false;
            for (var k = 0; k < 2000 && !seen; k++) seen = System.Array.IndexOf(_lib.DrawOffer(solo, 1, 3), tp) >= 0;
            Assert.IsTrue(seen, "blue (two players) can get Team Play");
        }
    }
}
