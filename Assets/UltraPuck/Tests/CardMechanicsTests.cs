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

        /// <summary>The one-a-team cards (Portal Gun, Swap), left out where a test needs every hand on a team the same.</summary>
        private int[] OneATeamOff()
        {
            var off = new System.Collections.Generic.List<int>();
            foreach (var c in _lib.Cards) if (c.MaxPerTeam > 0) off.Add(c.Index);
            return off.ToArray();
        }

        /// <summary>Give player <paramref name="i"/> these cards and work out everyone's stats again.</summary>
        private void Give(GameState s, int i, params string[] ids)
        {
            var c = new int[ids.Length];
            for (var k = 0; k < ids.Length; k++) c[k] = _lib.ById(ids[k]).Index;
            s.Players[i].Cards = c;
            _lib.Recompute(s);
        }

        /// <summary>No one-a-team cards and no card givers (Choose, Double Card), so every pick is one plain card.</summary>
        private int[] PlainCardsOnly()
        {
            var off = new System.Collections.Generic.List<int>(OneATeamOff());
            off.AddRange(_lib.CardGivers());
            foreach (var c in _lib.Cards) if (_lib.IsNewHand(c.Index)) off.Add(c.Index); // (it deals a fresh hand straight away)
            return off.ToArray();
        }

        /// <summary>
        /// Joining part-way (individual picking): one pick for each card round their team has had, on their own card
        /// screen while the match goes on; meanwhile they stand still and can't take the puck.
        /// </summary>
        [Test]
        public void JoiningPartWayCatchesUpOnCards()
        {
            var sim = new Simulation(Rink(), new Tuning { CardsOffered = 3, CardsOff = PlainCardsOnly(), MultiUtility = true }, _lib);
            var s = sim.NewGame(new[] { 0, 1 }, 5u);
            for (var round = 0; round < 3; round++)
            {
                sim.StartPicking(s, new[] { 0, 1 });
                sim.Step(s, new[] { new PlayerInput { Pick = 1 }, new PlayerInput { Pick = 1 } });
                Assert.AreNotEqual(Phase.Picking, s.Phase);
            }
            sim.StartPicking(s, new[] { 1 }); // blue picks once more (red scored)
            sim.Step(s, new[] { default(PlayerInput), new PlayerInput { Pick = 1 } });
            Assert.AreEqual(3, s.TeamRounds[0]);
            Assert.AreEqual(4, s.TeamRounds[1]);
            Play(s);

            var j = sim.AddPlayer(s, 1, false);
            var none = new PlayerInput[3];
            sim.Step(s, none);
            Assert.AreEqual(Phase.Play, s.Phase, "the match goes on");
            Assert.IsTrue(Simulation.CatchingUp(s, j));
            Assert.AreEqual(3, s.Players[j].Offer.Length, "their first catch-up hand");
            // (a person away for a moment, played by the bot meanwhile: the bot leaves their cards to them)
            s.Players[j].PickHeld = false;
            s.Players[j].Bot = true; // (how the rollback marks someone away)
            Assert.AreEqual(0, Bot.Think(sim, s, j).Pick, "the bot doesn't pick a person's catch-up cards");
            s.Players[j].Bot = false;
            s.Players[j].PickHeld = true;
            // they push the stick and the puck comes right at them: they stay put and can't take it
            var at = s.Players[j].Pos;
            s.Carrier = -1;
            s.PuckPos = at + new FixVec2(Fix.FromInt(2), Fix.Zero);
            s.PuckVel = new FixVec2(Fix.FromInt(-4), Fix.Zero);
            for (var t = 0; t < 60; t++) sim.Step(s, new[] { default(PlayerInput), default(PlayerInput), new PlayerInput { MoveX = 127 } });
            Assert.Less((s.Players[j].Pos - at).Length.ToDouble(), 0.05, "they wait where they are");
            Assert.AreNotEqual(j, s.Carrier, "and can't take the puck");
            Assert.AreEqual(0, s.Players[j].Cards.Length);
            var mid = s.Clone();
            Assert.AreEqual(s.Checksum(), mid.Clone().Checksum());

            // four picks: each sent until the next hand comes, then let go
            for (var k = 0; k < 4; k++)
            {
                Assert.AreEqual(3, s.Players[j].Offer.Length, "hand " + (k + 1));
                sim.Step(s, new[] { default(PlayerInput), default(PlayerInput), new PlayerInput { Pick = 2 } });
                sim.Step(s, none);
            }
            Assert.IsFalse(Simulation.CatchingUp(s, j), "caught up");
            Assert.AreEqual(4, s.Players[j].Cards.Length, "as many cards as their team's card rounds");
            Assert.AreEqual(4, s.Players[j].PickRounds);
            Assert.AreEqual(0, s.Players[j].Offer.Length);
            for (var t = 0; t < 20; t++) sim.Step(s, new[] { default(PlayerInput), default(PlayerInput), new PlayerInput { MoveX = 127 } });
            Assert.Greater(s.Players[j].Vel.X.ToDouble(), 0.5, "and they play on");
            Assert.AreNotEqual(mid.Checksum(), s.Checksum(), "the checksum sees it");
        }

        /// <summary>Joining part-way with a shared hand (Team Vote / Same Draw): the team's cards straight away, no card screen.</summary>
        [Test]
        public void JoiningPartWayWithTeamVoteGetsTheTeamsCards()
        {
            foreach (var sameDraw in new[] { false, true })
            {
                var sim = new Simulation(Rink(), new Tuning { TeamVote = !sameDraw, SameDraw = sameDraw, CardsOffered = 3, CardsOff = PlainCardsOnly(), MultiUtility = true }, _lib);
                var s = sim.NewGame(new[] { 0, 0, 1 }, 9u);
                for (var round = 0; round < 2; round++)
                {
                    sim.StartPicking(s, new[] { 0, 1, 2 });
                    sim.Step(s, new[] { new PlayerInput { Pick = 1 }, new PlayerInput { Pick = 1 }, new PlayerInput { Pick = 2 } });
                }
                Play(s);
                var j = sim.AddPlayer(s, 0, false);
                sim.Step(s, new PlayerInput[4]);
                Assert.IsFalse(Simulation.CatchingUp(s, j), "no card screen");
                CollectionAssert.AreEqual(s.Players[0].Cards, s.Players[j].Cards, "the team's cards");
                Assert.AreEqual(2, s.Players[j].Cards.Length);
            }
        }

        /// <summary>A bot added part-way catches up on its own.</summary>
        [Test]
        public void ABotAddedPartWayPicksItsCatchUpCards()
        {
            var sim = new Simulation(Rink(), new Tuning { CardsOffered = 3, CardsOff = PlainCardsOnly(), MultiUtility = true }, _lib);
            var s = sim.NewGame(new[] { 0, 1 }, 3u);
            for (var round = 0; round < 2; round++)
            {
                sim.StartPicking(s, new[] { 0, 1 });
                sim.Step(s, new[] { new PlayerInput { Pick = 1 }, new PlayerInput { Pick = 1 } });
            }
            Play(s);
            sim.HostCommand(s, PlayerInput.AddBotRed);
            var j = s.Players.Length - 1;
            for (var t = 0; t < 120 && Simulation.CatchingUp(s, j); t++) sim.Step(s, sim.Route(s, new PlayerInput[s.Players.Length]));
            Assert.IsFalse(Simulation.CatchingUp(s, j));
            Assert.AreEqual(2, s.Players[j].Cards.Length);
        }

        /// <summary>Team Vote: one offer per team, and everyone on the team gets the card most of them voted for.</summary>
        [Test]
        public void TeamVoteGivesTheWholeTeamTheWinningCard()
        {
            var sim = new Simulation(Rink(), new Tuning { TeamVote = true, CardsOffered = 3, CardsOff = OneATeamOff() }, _lib);
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
            var sim = new Simulation(Rink(), new Tuning { SameDraw = true, CardsOffered = 3, CardsOff = OneATeamOff() }, _lib);
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
            // (and none of the cards that hand out more cards: the last card held has to be the one picked)
            var off = new System.Collections.Generic.List<int>(OneATeamOff());
            foreach (var id in new[] { "ultrapuck.doublecard", "ultrapuck.choose", "ultrapuck.newhand" }) off.Add(_lib.ById(id).Index);
            var sim = new Simulation(Rink(), new Tuning { TeamVote = true, CardsOffered = 3, CardsOff = off.ToArray() }, _lib);
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
            var sim = new Simulation(Rink(), new Tuning { TeamVote = true, CardsOffered = 3, CardsOff = OneATeamOff() }, _lib);
            var s = sim.NewGame(new[] { 0, 1 }, 3u);
            s.Players[1].Bot = true;
            sim.StartPicking(s, new[] { 1 });
            s.PhaseTime -= sim.T.BotPickDelay; // once it has looked the cards over
            var alone = Bot.Think(sim, s, 1);
            Assert.Greater(alone.Pick, 0, "bot alone on the team picks (no human to wait for)");
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
            Assert.AreEqual(s.Players[1].Facing, s.Players[0].Facing, "facing the puck, like the players they stand with");
            Assert.Less(s.Players[0].Facing.X.ToDouble(), 0.0);
            Assert.Greater(s.Players[1].Pos.X.ToDouble(), 0.0, "blue is still at home");
            Assert.AreNotEqual(s.Players[0].Pos, s.Players[1].Pos);
        }

        /// <summary>
        /// A Double Agent moves one spot round the other side's face-off group each face-off, the kick-off spot included,
        /// never on top of anyone; the home players share the rest.
        /// </summary>
        [Test]
        public void DoubleAgentTakesTurnsRoundTheOtherSidesSpots()
        {
            var s = Game(0, 1, 1, 1);
            Give(s, 0, "ultrapuck.doubleagent");
            var seen = new System.Collections.Generic.HashSet<FixVec2>();
            var kickOffs = 0;
            for (var k = 0; k < 8; k++)
            {
                _sim.FaceOff(s);
                var agent = s.Players[0].Pos;
                Assert.Greater(agent.X.ToDouble(), 0.0, "on blue's side");
                for (var i = 1; i < 4; i++) Assert.AreNotEqual(agent, s.Players[i].Pos, "not on top of a blue player");
                var group = new System.Collections.Generic.HashSet<FixVec2>();
                for (var slot = 0; slot < 4; slot++) group.Add(_sim.FaceOffSpot(1, slot).Pos);
                Assert.IsTrue(group.Contains(agent), "in blue's group");
                if (agent == _sim.FaceOffSpot(1, 0).Pos) kickOffs++;
                seen.Add(agent);
            }
            Assert.AreEqual(4, seen.Count, "every one of the four spots in turn");
            Assert.AreEqual(2, kickOffs, "the kick-off spot every fourth face-off");
        }

        /// <summary>
        /// Double Agent's disguise: from the face-off the agent goes under an opponent's name (looks only), through the
        /// countdown and the first second of play, then fades back over 1.5 s and is gone.
        /// </summary>
        [Test]
        public void DoubleAgentStartsInDisguise()
        {
            var s = Game(0, 1, 1);
            Give(s, 0, "ultrapuck.doubleagent");
            _sim.FaceOff(s);
            var a = s.Players[0];
            Assert.IsTrue(a.Disguised);
            Assert.That(a.DisguiseAs, Is.EqualTo(1).Or.EqualTo(2), "an opponent's name");
            Assert.IsFalse(s.Players[1].Disguised, "the others aren't");
            for (var t = 0; t < 1200 && s.Phase == Phase.Countdown; t++) _sim.Step(s, null);
            Assert.AreEqual(Phase.Play, s.Phase);
            Assert.AreEqual(0.0, a.DisguiseTime.ToDouble(), 0.02, "the clock starts when the puck drops");
            var hold = _sim.T.DisguiseHold + _sim.T.DisguiseFade;
            var ticks = (hold * Fix.FromInt(_sim.T.TicksPerSecond)).FloorToInt();
            for (var t = 0; t < ticks - 2; t++) _sim.Step(s, null);
            Assert.IsTrue(a.Disguised, "still fading back just before 2.5 s");
            for (var t = 0; t < 4; t++) _sim.Step(s, null);
            Assert.IsFalse(a.Disguised, "the disguise has worn off");
            Assert.AreEqual(-1, a.DisguiseAs, "their own name again");
            // the same face-off on another PC borrows the same name
            var s2 = Game(0, 1, 1);
            Give(s2, 0, "ultrapuck.doubleagent");
            var s3 = Game(0, 1, 1);
            Give(s3, 0, "ultrapuck.doubleagent");
            _sim.FaceOff(s2); _sim.FaceOff(s3);
            Assert.AreEqual(s2.Players[0].DisguiseAs, s3.Players[0].DisguiseAs);
            Assert.AreEqual(s2.Checksum(), s3.Checksum());
        }

        /// <summary>
        /// A whole team of Double Agents (as Team Vote gives it): each takes its own spot on the other side, they borrow
        /// different names while there are enough to go round, and Head Start's puck goes to a player on their own side
        /// when there is one.
        /// </summary>
        [Test]
        public void SeveralDoubleAgentsShareTheOtherSide()
        {
            var s = Game(0, 0, 1, 1);
            Give(s, 0, "ultrapuck.doubleagent");
            Give(s, 1, "ultrapuck.doubleagent");
            for (var k = 0; k < 4; k++)
            {
                _sim.FaceOff(s);
                var spots = new System.Collections.Generic.HashSet<FixVec2>();
                foreach (var p in s.Players) spots.Add(p.Pos);
                Assert.AreEqual(4, spots.Count, "nobody on top of anyone");
                Assert.Greater(s.Players[0].Pos.X.ToDouble(), 0.0);
                Assert.Greater(s.Players[1].Pos.X.ToDouble(), 0.0);
                Assert.AreNotEqual(s.Players[0].DisguiseAs, s.Players[1].DisguiseAs, "two different names");
                Assert.GreaterOrEqual(s.Players[0].DisguiseAs, 2);
                Assert.GreaterOrEqual(s.Players[1].DisguiseAs, 2);
            }
            // one agent and a teammate at home, both with Head Start: the one at home starts with it
            var h = Game(0, 0, 1, 1);
            Give(h, 0, "ultrapuck.doubleagent", "ultrapuck.headstart");
            Give(h, 1, "ultrapuck.headstart");
            for (var k = 0; k < 4; k++)
            {
                _sim.FaceOff(h);
                Assert.AreEqual(1, h.Carrier, "the teammate at home has the puck");
            }
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

        /// <summary>One check from player 1 into player 0 (both put in place first).</summary>
        private void CheckInto(GameState s)
        {
            s.Players[0].Pos = At(0, 0); s.Players[0].Vel = FixVec2.Zero;
            s.Players[1].Pos = At(-2.2, 0); s.Players[1].Vel = FixVec2.Zero; s.Players[1].Facing = FixVec2.Right;
            s.Players[1].DashCharge = Fix.One; s.Players[1].DashTime = Fix.Zero; s.Players[1].HitLag = Fix.Zero; s.Players[0].HitLag = Fix.Zero;
            s.Carrier = -1; s.PuckPos = At(0, 9); s.PuckVel = FixVec2.Zero;
            _sim.Step(s, new[] { default(PlayerInput), Press(PlayerInput.ShootCheck) });
            Run(s, 50, default(PlayerInput), default(PlayerInput));
        }

        /// <summary>
        /// RAGE: each even check taken fills the meter by one; at a random 3-5 you rage for 7 s (2.5x size and weight,
        /// +25% speed, 10x knockback given), then 20 s of cooldown when checks don't count.
        /// </summary>
        [Test]
        public void RageBuildsUpFromChecksAndWearsOff()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.rage");
            Play(s);
            var size = s.Players[0].Stats.Size;
            CheckInto(s);
            var need = s.Players[0].RageNeed;
            Assert.That(need, Is.InRange(3, 5), "needs 3-5 checks");
            for (var k = 1; k < need; k++)
            {
                if (k > 1) CheckInto(s);
                Assert.AreEqual(k, s.Players[0].RageFill.ToDouble(), 1e-6, "check " + k + " counted as one (even weights)");
                Assert.AreEqual(Fix.Zero, s.Players[0].RageTime);
            }
            CheckInto(s);
            Assert.Greater(s.Players[0].RageTime.ToDouble(), 6.0, "raging after check " + need);
            Assert.AreEqual(Fix.Zero, s.Players[0].RageFill);
            Assert.AreEqual(0, s.Players[0].RageNeed, "a new need is rolled next time");
            var st = s.Players[0].Stats;
            Assert.AreEqual(size.ToDouble() * 2.5, st.Size.ToDouble(), 1e-3, "2.5x bigger");
            Assert.AreEqual(2.5, st.Weight.ToDouble(), 1e-3, "2.5x heavier");
            Assert.AreEqual(1.25, st.MoveSpeed.ToDouble(), 1e-3, "25% faster");
            Assert.AreEqual(1.0, st.ShotPower.ToDouble(), 1e-3, "no extra shot power (no puck while raging)");
            Assert.AreEqual(10.0, st.KnockbackGiven.ToDouble(), 1e-3, "10x knockback");
            Run(s, 120 * 7, default(PlayerInput), default(PlayerInput));
            Assert.AreEqual(Fix.Zero, s.Players[0].RageTime, "over after 7 s");
            Assert.AreEqual(size, s.Players[0].Stats.Size, "back to size");
            Assert.Greater(s.Players[0].RageCooldown.ToDouble(), 18.0, "cooling down");
            CheckInto(s);
            Assert.AreEqual(Fix.Zero, s.Players[0].RageFill, "no rage builds while cooling down");
            Run(s, 120 * 20, default(PlayerInput), default(PlayerInput));
            Assert.AreEqual(Fix.Zero, s.Players[0].RageCooldown);
            CheckInto(s);
            Assert.AreEqual(1.0, s.Players[0].RageFill.ToDouble(), 1e-6, "counting again");
            // without the card nothing builds up
            CheckInto(s);
            var plain = Game(0, 1);
            Play(plain);
            CheckInto(plain);
            Assert.AreEqual(Fix.Zero, plain.Players[0].RageFill);
        }

        /// <summary>Player 0's shot sent straight up from the middle at 40 u/s (players out of the way): its speed 5 s later.</summary>
        private double SpeedLater(int cards)
        {
            var s = Game(0, 1);
            var hand = new string[cards];
            for (var k = 0; k < cards; k++) hand[k] = "ultrapuck.momentum";
            if (cards > 0) Give(s, 0, hand);
            Play(s);
            s.Players[0].Pos = At(-10, 0); s.Players[1].Pos = At(10, 0);
            s.Carrier = -1; s.PassFrom = 0;
            s.PuckPos = At(0, 0); s.PuckVel = new FixVec2(Fix.Zero, Fix.FromInt(40)); s.ShotSpeed = Fix.FromInt(40);
            for (var t = 0; t < 600; t++) _sim.Step(s, new[] { default(PlayerInput), default(PlayerInput) });
            return s.PuckVel.Length.ToDouble();
        }

        /// <summary>Once it's in, the puck is gone: it stops where it went in, no more bounces (sounds) and no more ice being laid.</summary>
        [Test]
        public void ThePuckIsGoneOnceItsAGoal()
        {
            var s = Game(0, 1);
            Play(s);
            var g = _sim.Map.Goals.Find(x => x.Team == 1);
            var mid = new FixVec2((g.Min.X + g.Max.X) * Fix.Half, (g.Min.Y + g.Max.Y) * Fix.Half);
            var toward = mid.X < Fix.Zero ? -Fix.One : Fix.One;
            s.Players[0].Pos = At(0, 7); s.Players[1].Pos = At(0, -7);
            s.Carrier = -1; s.PassFrom = 0; s.PuckTeam = 0;
            s.PuckPos = new FixVec2(mid.X - toward * Fix.FromInt(5), mid.Y);
            s.PuckVel = new FixVec2(toward * Fix.FromInt(40), Fix.Zero);
            s.IceLaying = Fix.FromInt(3);
            for (var t = 0; t < 120 && s.Phase == Phase.Play; t++) _sim.Step(s, new[] { default(PlayerInput), default(PlayerInput) });
            Assert.AreEqual(Phase.Scored, s.Phase, "scored");
            var at = s.PuckPos; var knocks = s.PuckKnocks;
            for (var t = 0; t < 60; t++) _sim.Step(s, new[] { default(PlayerInput), default(PlayerInput) });
            Assert.AreEqual(at, s.PuckPos, "stopped where it went in");
            Assert.AreEqual(knocks, s.PuckKnocks, "no more bounces");
            Assert.AreEqual(0.0, s.IceLaying.ToDouble(), "no more ice laid");
        }

        /// <summary>A Black Hole goal swallows the players it pulls in (gone, not flown out of the rink); a plain goal doesn't.</summary>
        [Test]
        public void ABlackHoleSwallowsPlayersWhoReachIt()
        {
            foreach (var hole in new[] { true, false })
            {
                var s = Game(0, 1);
                Play(s);
                var g = _sim.Map.Goals.Find(x => x.Team == 1);
                var mid = new FixVec2((g.Min.X + g.Max.X) * Fix.Half, (g.Min.Y + g.Max.Y) * Fix.Half);
                var toward = mid.X < Fix.Zero ? -Fix.One : Fix.One;
                s.Players[0].Pos = At(0, 7);
                s.Players[1].Pos = new FixVec2(mid.X - toward * Fix.FromInt(4), mid.Y + Fix.FromInt(2));
                s.Carrier = -1; s.PassFrom = 0; s.PuckTeam = 0;
                s.PuckPos = new FixVec2(mid.X - toward * Fix.FromInt(5), mid.Y);
                s.PuckVel = new FixVec2(toward * Fix.FromInt(40), Fix.Zero);
                var look = new PlayerInput { Look = hole ? PlayerInput.LookPull : (byte)0 };
                for (var t = 0; t < 120 && s.Phase == Phase.Play; t++) _sim.Step(s, new[] { look, default(PlayerInput) });
                Assert.AreEqual(Phase.Scored, s.Phase, "scored");
                var launches = s.Players[1].Launches;
                for (var t = 0; t < 90; t++) _sim.Step(s, new[] { look, default(PlayerInput) });
                var p = s.Players[1];
                if (hole)
                {
                    Assert.IsTrue(p.Swallowed && p.OutTime > Fix.Zero, "swallowed");
                    Assert.AreEqual(launches, p.Launches, "not a launch out of the rink");
                }
                else Assert.IsFalse(p.Swallowed || p.OutTime > Fix.Zero, "a plain goal leaves them be");
                for (var t = 0; t < 600 && s.Phase != Phase.Play; t++) _sim.Step(s, new[] { look, default(PlayerInput) });
                Assert.IsFalse(s.Players[1].Swallowed || s.Players[1].OutTime > Fix.Zero, "back for the face-off");
            }
        }

        /// <summary>The puck's wall bounces are counted for their sound, with the speed going in.</summary>
        [Test]
        public void PuckBouncesAreCountedForTheirSound()
        {
            var s = Game(0, 1);
            Play(s);
            s.Players[0].Pos = At(-10, 0); s.Players[1].Pos = At(10, 0);
            s.Carrier = -1; s.PassFrom = -1;
            s.PuckPos = At(0, 0); s.PuckVel = new FixVec2(Fix.Zero, Fix.FromInt(40));
            var knocks = s.PuckKnocks;
            for (var t = 0; t < 240 && s.PuckKnocks == knocks; t++) _sim.Step(s, new[] { default(PlayerInput), default(PlayerInput) });
            Assert.AreEqual(knocks + 1, s.PuckKnocks, "one bounce");
            Assert.Greater(s.PuckKnockSpeed.ToDouble(), 20.0, "going in fast");
        }

        /// <summary>MOMENTUM: the shot never slows below 33% of its speed per card (bar the moment of a wall bounce).</summary>
        [Test]
        public void MomentumKeepsAShareOfTheShotsSpeed()
        {
            var plain = SpeedLater(0);
            var one = SpeedLater(1);
            var three = SpeedLater(3);
            Assert.Less(plain, 40 * 0.33 * 0.8, "slows right down without the card");
            Assert.GreaterOrEqual(one, 40 * 0.33 * 0.8, "one card: about a third");
            Assert.LessOrEqual(one, 40 * 0.33 * 1.05, "one card: but no more");
            Assert.GreaterOrEqual(three, 40 * 0.99 * 0.8, "three cards: nearly all of it");
        }

        /// <summary>LOYAL: your own shot goes into your own net without counting (without the card it's a goal for the other team).</summary>
        [Test]
        public void LoyalShotsDontGoInYourOwnNet()
        {
            foreach (var loyal in new[] { true, false })
            {
                var s = Game(0, 1);
                if (loyal) Give(s, 0, "ultrapuck.loyal");
                Play(s);
                var g = _sim.Map.Goals.Find(x => x.Team == 0);
                var mid = new FixVec2((g.Min.X + g.Max.X) * Fix.Half, (g.Min.Y + g.Max.Y) * Fix.Half);
                var toward = mid.X < Fix.Zero ? -Fix.One : Fix.One; // the way to that net from the middle
                s.Players[0].Pos = At(0, 7); s.Players[1].Pos = At(3, 7);
                s.Carrier = -1; s.PassFrom = 0;
                s.PuckPos = new FixVec2(mid.X - toward * Fix.FromInt(4), mid.Y);
                s.PuckVel = new FixVec2(toward * Fix.FromInt(40), Fix.Zero);
                var wentIn = false;
                for (var t = 0; t < 120 && s.Phase == Phase.Play; t++)
                {
                    _sim.Step(s, new[] { default(PlayerInput), default(PlayerInput) });
                    if (g.Touches(s.PuckPos, Fix.Zero)) wentIn = true;
                }
                if (loyal)
                {
                    Assert.IsTrue(wentIn, "the puck goes into the net");
                    Assert.AreEqual(0, s.Score[0] + s.Score[1], "no own goal");
                    Assert.AreEqual(Phase.Play, s.Phase);
                }
                else Assert.AreEqual(1, s.Score[1], "an own goal without the card");
            }
        }

        /// <summary>LOYAL doesn't stop a Cannon Ball: shoved into your own net by the other team's full charge is still their goal.</summary>
        [Test]
        public void LoyalStillLetsACannonBallShoveYouIn()
        {
            var s = Game(0, 1);
            Give(s, 1, "ultrapuck.loyal");
            Play(s);
            var g = _sim.Map.Goals.Find(x => x.Team == 1);
            var mid = new FixVec2((g.Min.X + g.Max.X) * Fix.Half, (g.Min.Y + g.Max.Y) * Fix.Half);
            var toward = mid.X < Fix.Zero ? -Fix.One : Fix.One;
            s.Players[0].Pos = At(0, 7);
            var c = s.Players[1];
            c.Pos = new FixVec2(mid.X - toward * Fix.FromInt(5), mid.Y);
            c.Vel = new FixVec2(toward * Fix.FromInt(30), Fix.Zero);
            c.Shoved = _sim.T.CannonTime; c.ShovedBy = 0;
            s.Carrier = 1; s.PassFrom = -1;
            for (var t = 0; t < 120 && s.Phase == Phase.Play; t++) { c.Shoved = _sim.T.CannonTime; _sim.Step(s, new[] { default(PlayerInput), default(PlayerInput) }); }
            Assert.AreEqual(1, s.Score[0], "the Cannon Ball's goal");
        }

        /// <summary>RAGE: a heavier checker fills the meter faster (by weight over yours), a lighter one slower.</summary>
        [Test]
        public void RageFillsByTheWeightDifference()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.rage");
            Give(s, 1, "ultrapuck.heavyweight");
            Play(s);
            CheckInto(s);
            var heavy = s.Players[0].RageFill.ToDouble();
            var ratio = (Simulation.Heft(s.Players[1]) / Simulation.Heft(s.Players[0])).ToDouble();
            Assert.Greater(ratio, 1.05, "the checker is heavier");
            Assert.AreEqual(System.Math.Min(ratio, 3.0), heavy, 1e-3, "filled by the weight difference");

            var t = Game(0, 1);
            Give(t, 0, "ultrapuck.rage", "ultrapuck.heavyweight");
            Play(t);
            CheckInto(t);
            Assert.Less(t.Players[0].RageFill.ToDouble(), 1.0, "a lighter checker fills it less");
            Assert.Greater(t.Players[0].RageFill.ToDouble(), 0.0);
        }

        /// <summary>How far player 0 goes on one dash from standing still (nobody else near).</summary>
        private double DashDistance(GameState s)
        {
            s.Players[0].Pos = At(-20, 0); s.Players[0].Vel = FixVec2.Zero; s.Players[0].Facing = FixVec2.Right;
            s.Players[0].DashCharge = Fix.One; s.Players[0].DashTime = Fix.Zero; s.Players[0].DashCoast = Fix.Zero;
            s.Players[1].Pos = At(10, 10);
            s.Carrier = -1; s.PuckPos = At(0, -10); s.PuckVel = FixVec2.Zero;
            var from = s.Players[0].Pos.X.ToDouble();
            _sim.Step(s, new[] { Press(PlayerInput.ShootCheck), default(PlayerInput) });
            Run(s, 360, default(PlayerInput), default(PlayerInput));
            return s.Players[0].Pos.X.ToDouble() - from;
        }

        /// <summary>RAGE: a raging dash goes about 3 times as far; no picking up the puck; straight through Stone Walls.</summary>
        [Test]
        public void RagingDashesFarSmashesWallsAndCantTakeThePuck()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.rage");
            Play(s);
            var plain = DashDistance(s);
            s.Players[0].RageTime = Fix.FromInt(7);
            _lib.Recompute(s);
            s.Players[0].RageTime = Fix.FromInt(30); // (long enough for the measuring)
            var raging = DashDistance(s);
            TestContext.WriteLine("dash: " + plain + " plain, " + raging + " raging");
            Assert.AreEqual(3.0, raging / plain, 0.35, "about 3x as far");
            // the puck right in front: not taken
            s.Players[0].Pos = At(0, 0); s.Players[0].Vel = FixVec2.Zero;
            s.PuckPos = At(0.5, 0); s.PuckVel = FixVec2.Zero; s.Carrier = -1;
            Run(s, 30, default(PlayerInput), default(PlayerInput));
            Assert.AreNotEqual(0, s.Carrier, "no hands for the puck");
            // a wall across the way: straight through, and it's gone where they went
            s.Stones = new[] { new Stone { A = At(5, -3), B = At(5, 3), Life = Fix.FromInt(10), Wall = 1 } };
            s.Players[0].Pos = At(0, 0); s.Players[0].Vel = FixVec2.Zero;
            Run(s, 180, new PlayerInput { MoveX = 127 }, default(PlayerInput));
            Assert.Greater(s.Players[0].Pos.X.ToDouble(), 7.0, "through the wall");
            Assert.AreEqual(0, s.Stones.Length, "smashed");
        }

        /// <summary>
        /// A hard raging check launches a Featherweight out of the rink 1 time in 5 (here forced: always / never); they're
        /// back at their spawn 5 s later. Anyone else just bounces round the rink. A heavy player isn't knocked hard enough to go; a plain check never does; and a check
        /// that doesn't launch doesn't put anyone through the boards either.
        /// </summary>
        [Test]
        public void RagingChecksCanLaunchPeopleOutAndTheyRespawn()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.rage");
            Play(s);
            s.Players[0].RageTime = Fix.FromInt(7);
            _lib.Recompute(s);
            s.Players[0].RageTime = Fix.FromInt(60);
            var chance = _sim.T.KnockOutChance;
            System.Action<double> check = bx =>
            {
                var r0 = _sim.RadiusOf(s.Players[0]).ToDouble();
                s.Players[1].Pos = At(bx, 0); s.Players[1].Vel = FixVec2.Zero;
                s.Players[0].Pos = At(bx - r0 - 1.2, 0); s.Players[0].Vel = FixVec2.Zero; s.Players[0].Facing = FixVec2.Right;
                s.Players[0].DashCharge = Fix.One; s.Players[0].DashTime = Fix.Zero; s.Players[0].HitLag = Fix.Zero; s.Players[1].HitLag = Fix.Zero;
                s.Carrier = -1; s.PuckPos = At(0, 9);
                _sim.Step(s, new[] { Press(PlayerInput.ShootCheck), default(PlayerInput) });
                Run(s, 40, default(PlayerInput), default(PlayerInput));
            };
            try
            {
                // never: knocked back hard, but stays in the rink, even right by the boards
                _sim.T.KnockOutChance = 1000000;
                check(0);
                check(_sim.Map.BoundsMax.X.ToDouble() - 3);
                Assert.AreEqual(0, s.Players[1].Launches, "no knock-out, and not through the boards");
                Assert.IsTrue(_sim.Map.Inside(s.Players[1].Pos));
                // always, but not someone without a Featherweight card: they just bounce round the rink
                _sim.T.KnockOutChance = 1;
                check(0);
                Assert.AreEqual(0, s.Players[1].Launches, "only Featherweights fly out");
                Assert.IsTrue(_sim.Map.Inside(s.Players[1].Pos));
                // a Featherweight: launched from the middle of the ice, out through the boards
                Give(s, 1, "ultrapuck.featherweight");
                s.Players[0].RageTime = Fix.FromInt(7);
                _lib.Recompute(s);
                s.Players[0].RageTime = Fix.FromInt(60);
                check(0);
                Assert.AreEqual(1, s.Players[1].Launches, "launched out of the rink");
                Assert.IsFalse(_sim.Map.Inside(s.Players[1].Pos), "off the ice");
                Assert.Greater(s.Players[1].OutDir.X.ToDouble(), 0.5, "the way they were hit");
                Assert.IsFalse(_sim.Map.Inside(s.Players[1].OutAt), "leaving the rink at the boards");
                Run(s, 120 * 4, default(PlayerInput), default(PlayerInput));
                Assert.Greater(s.Players[1].OutTime.ToDouble(), 0.0, "still out after 4 s");
                Run(s, 120 * 1 + 10, default(PlayerInput), default(PlayerInput));
                Assert.AreEqual(Fix.Zero, s.Players[1].OutTime, "back after 5 s");
                Assert.AreEqual(1, s.Players[1].Respawns);
                Assert.AreEqual(_sim.FaceOffSpot(1, 0).Pos.X.ToDouble(), s.Players[1].Pos.X.ToDouble(), 1.5, "at their spawn");
                // a heavy one holds on (a Featherweight with Heavyweights on top: too heavy to launch)
                Give(s, 1, "ultrapuck.featherweight", "ultrapuck.heavyweight", "ultrapuck.heavyweight");
                s.Players[0].RageTime = Fix.FromInt(7);
                _lib.Recompute(s);
                s.Players[0].RageTime = Fix.FromInt(60);
                check(0);
                Assert.AreEqual(1, s.Players[1].Launches, "too heavy to launch");
                // a plain check: never
                Give(s, 1);
                s.Players[0].RageTime = Fix.Zero; s.Players[0].RageCooldown = Fix.FromInt(60);
                _lib.Recompute(s);
                check(0);
                Assert.AreEqual(1, s.Players[1].Launches, "no knock-out without the rage");
            }
            finally { _sim.T.KnockOutChance = chance; }
            // anyone ending up outside the rink is out as well
            s.Players[1].Pos = At(_sim.Map.BoundsMax.X.ToDouble() + 5, 0);
            Run(s, 2, default(PlayerInput), default(PlayerInput));
            Assert.AreEqual(2, s.Players[1].Launches);
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
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            Assert.AreEqual(1, s.Shots.Length, "fired");
            Run(s, 60, default(PlayerInput), default(PlayerInput));
            Assert.AreEqual(0.0, s.Players[1].Stun.ToDouble(), "no stun: a knockback, like a check");
            Assert.AreNotEqual(1, s.Carrier, "dropped the puck");
            Assert.AreEqual(1, s.Players[0].Bumps, "counts as a check landed");
            Assert.AreEqual(0, s.Shots.Length);
            // a second shot has to wait for the cooldown
            _sim.Step(s, new[] { default(PlayerInput), default(PlayerInput) });
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            Assert.AreEqual(0, s.Shots.Length, "still cooling down");
            Run(s, _sim.T.TicksPerSecond, default(PlayerInput), default(PlayerInput));
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            Assert.AreEqual(1, s.Shots.Length, "ready again after a second");
            Assert.AreEqual(0.85, s.Players[0].GunCooldown.ToDouble(), 0.01, "0.85 s for a person");
        }

        [Test]
        public void BotsWaitLongerBetweenPuckGunShots()
        {
            var s = _sim.NewGame(new[] { 0, 1 }, 1u, new[] { true, false });
            Give(s, 0, "ultrapuck.puckgun");
            Play(s);
            s.Players[0].Pos = At(-4, 0); s.Players[0].Facing = FixVec2.Right;
            s.Players[1].Pos = At(3, 0);
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            Assert.AreEqual(1, s.Shots.Length, "fired");
            Assert.AreEqual(3.85, s.Players[0].GunCooldown.ToDouble(), 0.01, "3 s longer for a bot");
        }

        [Test]
        public void ChargeNoLongerFiresThePuckGun()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.puckgun");
            Play(s);
            s.Players[0].Pos = At(-4, 0); s.Players[0].Facing = FixVec2.Right;
            s.Players[1].Pos = At(3, 0);
            _sim.Step(s, new[] { Press(PlayerInput.ChargeBrake), default(PlayerInput) });
            Assert.AreEqual(0, s.Shots.Length, "charge / brake just brakes now");
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
                _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
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

        /// <summary>Swapping onto a bot while raging: the rage stays with your body (the bot runs it), not with you.</summary>
        [Test]
        public void SwappingWhileRagingLeavesTheRageOnYourBody()
        {
            var s = _sim.NewGame(new[] { 0, 0, 1 }, 1u, new[] { false, true, true });
            Give(s, 0, "ultrapuck.rage");
            Play(s);
            s.Players[0].RageTime = _sim.T.RageTime;
            _lib.Recompute(s);
            var raging = s.Players[0].Stats.Size.ToDouble();
            _sim.Step(s, _sim.Route(s, new[] { new PlayerInput { Buttons = PlayerInput.Swap }, default(PlayerInput), default(PlayerInput) }));
            Assert.AreEqual(1, Simulation.Steers(s, 0), "on the bot teammate");
            Assert.Greater(s.Players[0].RageTime.ToDouble(), 0.0, "their own body still rages, run by the bot");
            Assert.AreEqual(raging, s.Players[0].Stats.Size.ToDouble(), 0.001, "still raging-size");
            Assert.AreEqual(0.0, s.Players[1].RageTime.ToDouble(), "the body they took over doesn't rage");
            Assert.Less(s.Players[1].Stats.Size.ToDouble(), raging, "nor grow");
        }

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

        /// <summary>
        /// Carry-through (match setting): any pass or shot that reaches another player takes them along the puck's way,
        /// a teammate or an opponent alike, and the host can switch it and its strength mid-match.
        /// </summary>
        [Test]
        public void CarryThroughTakesWhoeverGetsThePuckAlong()
        {
            // player 0 quick-shoots along +x at player `to` standing still: how fast is `to` going along +x once they have it?
            System.Func<bool, int, int, double> caught = (on, pct, to) =>
            {
                Make();
                var s = Game(0, 0, 1);
                Play(s);
                if (on) _sim.HostCommand(s, PlayerInput.CarryOn);
                _sim.HostCommand(s, (byte)(PlayerInput.CarryStrength + pct / 5));
                Assert.AreEqual(on, s.CarryOn);
                Assert.AreEqual(pct, s.CarryPercent);
                s.Players[0].Pos = At(-8, 0); s.Players[0].Facing = FixVec2.Right;
                s.Players[1].Pos = At(-8, 8); s.Players[2].Pos = At(-8, -8);
                s.Players[to].Pos = At(4, 0); s.Players[to].Facing = new FixVec2(-Fix.One, Fix.Zero);
                s.Carrier = 0;
                var inputs = new PlayerInput[3];
                inputs[0] = Press(PlayerInput.ShootCheck);
                _sim.Step(s, inputs);
                inputs[0] = default(PlayerInput);
                for (var t = 0; t < 120 && s.Carrier != to; t++) { s.Players[to].Pos = At(4, 0); s.Players[to].Vel = FixVec2.Zero; _sim.Step(s, inputs); }
                Assert.AreEqual(to, s.Carrier, "caught it");
                return s.Players[to].Vel.X.ToDouble();
            };
            foreach (var to in new[] { 1, 2 })
            {
                var who = to == 1 ? "a teammate" : "an opponent";
                var off = caught(false, 50, to);
                var half = caught(true, 50, to);
                var full = caught(true, 100, to);
                Assert.Greater(half, off + 3.0, who + " is carried along with it");
                Assert.Greater(full, half + 3.0, who + ": a stronger setting carries them further");
            }
            // the rules start it (and the checksum sees it)
            var sim = new Simulation(Rink(), new Tuning { CarryThrough = true, CarryPercent = 35 }, _lib);
            var g = sim.NewGame(new[] { 0, 1 }, 1u);
            Assert.IsTrue(g.CarryOn);
            Assert.AreEqual(35, g.CarryPercent);
            var before = g.Checksum();
            sim.HostCommand(g, PlayerInput.CarryOff);
            Assert.IsFalse(g.CarryOn);
            Assert.AreNotEqual(before, g.Checksum());
            Assert.IsFalse(g.Clone().CarryOn);
        }

        /// <summary>Changing team mid-match: the player goes to the other side, lets go of the puck, and keeps their cards.</summary>
        [Test]
        public void ChangingTeamMidMatch()
        {
            var s = Game(0, 0, 1);
            Give(s, 0, "ultrapuck.cannonball");
            Play(s);
            s.Carrier = 0;
            _sim.ChangeTeam(s, 0, 1);
            Assert.AreEqual(1, s.Players[0].Team);
            Assert.AreEqual(-1, s.Carrier, "the puck is let go");
            Assert.AreEqual(1, s.Players[0].Cards.Length, "cards stay with them");
            Run(s, 30, new PlayerInput[3]);
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
            s.Players[0].Pos = At(-10, 8); s.Players[2].Pos = At(10, -8); s.Players[3].Pos = At(-4, -5); s.Players[1].Pos = At(-3, -5);
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

        /// <summary>Stealing goes by the wedge as drawn: its tip on the carried puck takes it, out of reach of the body.</summary>
        [Test]
        public void TheWedgeTipStealsThePuck()
        {
            var s = _sim.NewGame(new[] { 0, 1 }, 1u, new[] { false, false });
            Play(s);
            var c = s.Players[0]; var q = s.Players[1];
            c.Pos = At(0, 0); c.Facing = new FixVec2(Fix.One, Fix.Zero);
            s.Carrier = 0;
            _sim.Step(s, new PlayerInput[2]);
            Assert.AreEqual(0, s.Carrier);
            // the thief faces the puck from beyond it, the tip of their wedge just on it
            var R = _sim.RadiusOf(q);
            var puck = s.PuckPos;
            q.Facing = new FixVec2(-Fix.One, Fix.Zero);
            q.Pos = puck + new FixVec2(R * _sim.T.WedgeTip + _sim.T.PuckRadius - Fix.Ratio(1, 20), Fix.Zero);
            q.Vel = FixVec2.Zero;
            _sim.Step(s, new PlayerInput[2]);
            Assert.AreEqual(1, s.Carrier, "the wedge's tip took it");
        }

        /// <summary>Knockback has no speed cap: someone sent far faster than a step can carry them still bounces off the boards.</summary>
        [Test]
        public void VeryFastPlayersStillBounceOffTheBoards()
        {
            foreach (var dir in new[] { new FixVec2(Fix.One, Fix.Zero), new FixVec2(Fix.Zero, -Fix.One), new FixVec2(-Fix.One, Fix.One).Normalized })
            {
                var s = _sim.NewGame(new[] { 0, 1 }, 1u, new[] { false, false });
                Play(s);
                var p = s.Players[0];
                p.Pos = At(0, 0);
                p.Vel = dir * Fix.FromInt(400);
                for (var t = 0; t < 60; t++)
                {
                    _sim.Step(s, new PlayerInput[2]);
                    Assert.IsTrue(p.OutTime <= Fix.Zero, "not knocked out through the boards");
                    Assert.IsTrue(_sim.Map.Inside(p.Pos), "still in the rink");
                }
            }
        }

        /// <summary>A scorer whose goal explosion is the Black Hole (their controls say so) pulls everyone in instead of blasting them away.</summary>
        [Test]
        public void TheBlackHolePullsEveryoneIn()
        {
            foreach (var pull in new[] { false, true })
            {
                var s = _sim.NewGame(new[] { 0, 1 }, 1u, new[] { false, false });
                Play(s);
                var g = _sim.Map.Goals[0];
                var mouth = (g.Min + g.Max) / Fix.FromInt(2);
                var shooter = s.Players[1 - g.Team];
                var other = s.Players[g.Team];
                other.Pos = mouth + new FixVec2(Fix.FromInt(g.Team == 0 ? 5 : -5), Fix.FromInt(2));
                other.Vel = FixVec2.Zero;
                shooter.Pos = mouth + new FixVec2(Fix.FromInt(g.Team == 0 ? 8 : -8), -Fix.FromInt(3));
                s.PuckPos = mouth; s.PuckVel = FixVec2.Zero; s.Carrier = -1;
                s.PassFrom = 1 - g.Team; s.Touch1 = 1 - g.Team;
                var inputs = new PlayerInput[2];
                inputs[1 - g.Team].Look = pull ? PlayerInput.LookPull : (byte)0;
                var before = (other.Pos - mouth).Length;
                for (var t = 0; t < 6 && s.Phase == Phase.Play; t++) _sim.Step(s, inputs);
                Assert.AreEqual(Phase.Scored, s.Phase, "a goal");
                for (var t = 0; t < 20; t++) _sim.Step(s, inputs);
                var after = (other.Pos - mouth).Length;
                if (pull) Assert.Less(after.ToFloat(), before.ToFloat(), "pulled in"); else Assert.Greater(after.ToFloat(), before.ToFloat(), "blasted away");
            }
        }

        /// <summary>On rinks with nets out on the ice, bots don't get stuck on them: hardly any seconds spent pinned by one.</summary>
        [Test, Timeout(240000)]
        public void BotsDontGetStuckOnNets()
        {
            foreach (var file in new[] { "hockey-rink.json", "s-rink.json", "big-rink.json" })
            {
                var map = MapDef.FromJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Maps", file)));
                var sim = new Simulation(map, null, _lib);
                int stuck = 0, total = 0;
                foreach (var seed in new[] { 5u, 9u })
                {
                    var s = sim.NewGame(new[] { 0, 1, 0, 1 }, seed, new[] { true, true, true, true });
                    Play(s);
                    var was = new FixVec2[4];
                    for (var t = 0; t < 120 * 90; t++)
                    {
                        sim.Step(s, sim.Route(s, new PlayerInput[4]));
                        if (s.Phase == Phase.Countdown) { s.Phase = Phase.Play; s.PhaseTime = Fix.Zero; }
                        if (t % 120 != 0) continue;
                        // once a second: who hardly moved, next to a net?
                        for (var i = 0; i < 4; i++)
                        {
                            var p = s.Players[i];
                            if (s.Phase == Phase.Play && t > 0)
                            {
                                total++;
                                var near = false;
                                foreach (var g in map.Goals) if (g.Touches(p.Pos, Fix.FromInt(3))) near = true;
                                if (near && (p.Pos - was[i]).SqrLength < Fix.FromInt(1) && s.Carrier != i) stuck++;
                            }
                            was[i] = p.Pos;
                        }
                    }
                }
                TestContext.WriteLine(file + ": bots stuck by a net " + (100.0 * stuck / total).ToString("0.0") + "% of seconds");
            }
        }

        /// <summary>The puck stuck behind a net: bots go round to it, none of them stays wedged in or behind the net.</summary>
        [Test, Timeout(120000)]
        public void BotsGoRoundANetToThePuckBehindIt()
        {
            foreach (var file in new[] { "hockey-rink.json", "s-rink.json" })
            {
                var map = MapDef.FromJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Maps", file)));
                var sim = new Simulation(map, null, _lib);
                int stuck = 0, total = 0, reached = 0, tries = 0;
                foreach (var g in map.Goals)
                {
                    if (!g.HasFacing) continue;
                    var centre = new FixVec2((g.Min.X + g.Max.X) / 2, (g.Min.Y + g.Max.Y) / 2);
                    var behind = centre - g.Facing * Fix.FromInt(3); // just behind the net
                    for (var seed = 1u; seed <= 3u; seed++)
                    {
                        var s = sim.NewGame(new[] { 0, 1, 0, 1 }, seed, new[] { true, true, true, true });
                        Play(s);
                        var was = new FixVec2[4];
                        var got = false;
                        tries++;
                        for (var t = 0; t < 120 * 8; t++)
                        {
                            if (s.Carrier < 0) { s.PuckPos = behind; s.PuckVel = FixVec2.Zero; }
                            else got = true;
                            sim.Step(s, sim.Route(s, new PlayerInput[4]));
                            if (s.Phase != Phase.Play) break;
                            if (t % 60 != 0) continue;
                            for (var i = 0; i < 4; i++)
                            {
                                var p = s.Players[i];
                                if (t >= 240)
                                {
                                    total++;
                                    var near = g.Touches(p.Pos, Fix.FromInt(3));
                                    if (near && (p.Pos - was[i]).SqrLength < Fix.Ratio(1, 4) && s.Carrier != i) stuck++;
                                }
                                was[i] = p.Pos;
                            }
                        }
                        if (got) reached++;
                    }
                }
                TestContext.WriteLine(file + ": puck behind a net reached " + reached + "/" + tries + ", bots stuck by the net " + (100.0 * stuck / System.Math.Max(1, total)).ToString("0") + "% of half-seconds");
            }
        }

        /// <summary>A bot going after a loose puck a long way off dashes its way there: much quicker than skating.</summary>
        [Test]
        public void BotsDashToAFarLoosePuck()
        {
            var s = _sim.NewGame(new[] { 0, 1 }, 4u, new[] { true, true });
            Play(s);
            s.Players[0].Pos = At(-18, -4); s.Players[0].Vel = FixVec2.Zero;
            s.Players[1].Pos = At(18, 9);
            var t = 0;
            var dashes = 0;
            for (; t < 120 * 8 && s.Carrier != 0; t++)
            {
                s.PuckPos = At(12, -4); s.PuckVel = FixVec2.Zero; s.Players[1].Pos = At(18, 9);
                var before = s.Players[0].DashCharge;
                _sim.Step(s, _sim.Route(s, new PlayerInput[2]));
                if (s.Players[0].DashCharge < before - Fix.Half) dashes++;
            }
            TestContext.WriteLine("30 units to a loose puck: " + (t / 120.0).ToString("0.00") + " s, " + dashes + " dashes");
            Assert.AreEqual(0, s.Carrier, "got there");
            Assert.Greater(dashes, 1, "dashed along the way");
            Assert.Less(t, 120 * 3, "in under 3 s (skating alone takes about 4)");
        }

        /// <summary>Swapping onto a bot teammate: your cards (size and all) go with you; your own body gets the bot's.</summary>
        [Test]
        public void YourCardsGoWithYouWhenYouSwap()
        {
            var s = _sim.NewGame(new[] { 0, 0, 1 }, 2u, new[] { false, true, true });
            Give(s, 0, "ultrapuck.heavyweight", "ultrapuck.shockwave");
            Give(s, 1, "ultrapuck.speedup");
            Play(s);
            var big = s.Players[0].Stats.Size;
            Assert.Greater(big.ToDouble(), 1.1, "you're big");
            var swap = new PlayerInput { Buttons = PlayerInput.Swap };
            _sim.Step(s, _sim.Route(s, new[] { swap, default(PlayerInput), default(PlayerInput) }));
            Assert.AreEqual(1, Simulation.Steers(s, 0), "steering the bot now");
            Assert.AreEqual(big, s.Players[1].Stats.Size, "the body you steer is big now");
            Assert.AreEqual(1, s.Players[1].Stats.Shockwave, "and has your Shockwave");
            Assert.AreEqual(1.0, s.Players[0].Stats.Size.ToDouble(), 1e-6, "your own body is back to normal size");
            Assert.AreEqual(0, s.Players[0].Stats.Shockwave, "without your Shockwave");
            Assert.Greater(s.Players[0].Stats.MoveSpeed.ToDouble(), 1.1, "with the bot's Speed Up instead");
            Assert.AreEqual(2, s.Players[0].Cards.Length, "the cards are still yours");
            // and back
            _sim.Step(s, _sim.Route(s, new[] { default(PlayerInput), default(PlayerInput), default(PlayerInput) }));
            _sim.Step(s, _sim.Route(s, new[] { swap, default(PlayerInput), default(PlayerInput) }));
            Assert.AreEqual(0, Simulation.Steers(s, 0), "back on your own player");
            Assert.AreEqual(big, s.Players[0].Stats.Size, "big again");
            Assert.AreEqual(1.0, s.Players[1].Stats.Size.ToDouble(), 1e-6, "the bot's body normal again");
        }

        /// <summary>Bots play as a team: over a few minutes of 2 v 2 they pass to each other and take quick shots, and still score.</summary>
        [Test, Timeout(120000)]
        public void BotsPassAndTakeQuickShots()
        {
            int passes = 0, quick = 0, charged = 0, goals = 0, attempts = 0;
            foreach (var seed in new[] { 3u, 11u })
            {
                var s = _sim.NewGame(new[] { 0, 1, 0, 1 }, seed, new[] { true, true, true, true });
                Play(s);
                int last = -1, scored = 0;
                var shots = new int[4];
                var charge = new double[4];
                for (var t = 0; t < 120 * 90; t++)
                {
                    _sim.Step(s, _sim.Route(s, new PlayerInput[4]));
                    if (s.Phase == Phase.Countdown) { s.Phase = Phase.Play; s.PhaseTime = Fix.Zero; }
                    if (s.Carrier >= 0)
                    {
                        if (last >= 0 && last != s.Carrier && s.Players[last].Team == s.Players[s.Carrier].Team) passes++;
                        last = s.Carrier;
                    }
                    if (s.Phase == Phase.Scored) last = -1;
                    for (var i = 0; i < 4; i++)
                    {
                        if (s.Players[i].ShotsFired > shots[i])
                        {
                            if (charge[i] <= 0.0) quick++; else charged++;
                            shots[i] = s.Players[i].ShotsFired;
                            // aimed at a teammate: a pass attempt
                            var mate = s.Players[i ^ 2];
                            var to = (mate.Pos - s.Players[i].Pos).Normalized;
                            if (FixVec2.Dot(to, s.PuckVel.Normalized) > Fix.Ratio(9, 10)) attempts++;
                        }
                        charge[i] = s.Players[i].Charge.ToDouble();
                    }
                }
                scored = s.Score[0] + s.Score[1];
                goals += scored;
            }
 TestContext.WriteLine("3 minutes of 2v2 bots: " + attempts + " pass attempts, " + passes + " passes, " + quick + " quick shots, " + charged + " charged, " + goals + " goals");
            Assert.Greater(passes, 20, "they pass to each other");
            Assert.Greater(quick, 20, "plenty of quick shots");
                        Assert.Greater(goals, 0, "and still score");
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
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
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

        /// <summary>Thrown by a teammate (Friendly Fire): a boost they can steer, not a flight they ride out.</summary>
        [Test]
        public void AThrownTeammateCanSteer()
        {
            var s = Grab();
            for (var t = 0; t < 30; t++) _sim.Step(s, new[] { new PlayerInput { AimY = 127, Buttons = PlayerInput.ChargeBrake }, default(PlayerInput), default(PlayerInput) });
            _sim.Step(s, new[] { new PlayerInput { AimY = 127, Buttons = PlayerInput.ShootCheck | PlayerInput.ChargeBrake }, default(PlayerInput), default(PlayerInput) });
            Assert.AreEqual(-1, s.Players[1].HeldBy, "thrown");
            var up = s.Players[1].Vel.Y.ToDouble();
            for (var t = 0; t < 30; t++) _sim.Step(s, new[] { default(PlayerInput), new PlayerInput { MoveX = 127 }, default(PlayerInput) });
            Assert.Greater(s.Players[1].Vel.X.ToDouble(), 3.0, "steered sideways");
            Assert.Greater(s.Players[1].Vel.Y.ToDouble(), up * 0.4, "still carried along by the throw");
            Assert.Greater(s.Players[1].TossTime.ToDouble(), 0.0, "still boosted");
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
            for (var t = 0; t < 240; t++) { s.Players[1].Pos = At(1.5, -6); _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) }); }
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
            for (var t = 0; t < 120; t++) { s.Players[1].Pos = At(1.0, -6); s.Players[2].Pos = At(-1.0, -6); s.Players[1].Vel = s.Players[2].Vel = FixVec2.Zero; _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput), default(PlayerInput) }); }
            _sim.Step(s, new PlayerInput[3]); // let go after a second: 1.68 units
            Assert.AreEqual(1, s.Players[0].Shocks, "a shockwave went out");
            Assert.Greater(s.Players[1].Vel.X.ToDouble(), 0.5, "foe shoved away");
            Assert.Less(s.Players[2].Vel.X.ToDouble(), -0.5, "teammate shoved away too");
            Assert.AreEqual(Fix.Zero, s.Players[1].SlowTime, "no slow — push only");
            Assert.AreEqual(1.68, s.Players[0].ShockRadius.ToDouble(), 0.02, "held a second: 1.68 units (it grows from nothing, full in 3.6 s)");
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput), default(PlayerInput) });
            _sim.Step(s, new PlayerInput[3]);
            Assert.Less(s.Players[0].ShockRadius.ToDouble(), 0.05, "a tap: next to nothing");
            Assert.AreEqual(2, s.Players[0].Shocks, "no cooldown: another straight away");
            for (var t = 0; t < 120 * 7; t++) _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput), default(PlayerInput) });
            _sim.Step(s, new PlayerInput[3]);
            Assert.AreEqual(6.0, s.Players[0].ShockRadius.ToDouble(), 0.01, "held 3.6 s or more: 6 units, no bigger");
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
            s.Rocks = new[] { new Rock { Pos = At(-1.2, -6), Vel = new FixVec2(Fix.Zero, -Fix.One), Owner = 0, Carrier = -1, Life = Fix.FromInt(10) } };
            for (var t = 0; t < 180; t++) _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            s.PuckPos = At(0, -4.8); s.PuckVel = FixVec2.Zero;
            s.Rocks[0].Pos = At(-1.2, -6); s.Rocks[0].Vel = new FixVec2(Fix.Zero, -Fix.One);
            _sim.Step(s, new PlayerInput[2]); // let go: 1.5 units
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
            s.Rocks = new[] { new Rock { Pos = At(4, -6), Vel = FixVec2.Zero, Owner = 0, Carrier = -1, Life = Fix.FromInt(10) } };
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
            s.Rocks[0] = new Rock { Pos = At(4, -6), Vel = new FixVec2(Fix.FromInt(2), Fix.Zero), Owner = 0, Carrier = -1, Life = Fix.FromInt(10) };
            s.Players[0].Pos = At(0, -6);
            _sim.Step(s, new PlayerInput[2]);
            var before = s.Players[0].Pos;
            var playerVel = new FixVec2(Fix.FromInt(3), Fix.Zero);
            var rockVel = new FixVec2(Fix.FromInt(-5), Fix.Zero);
            s.Players[0].Vel = playerVel;
            s.Rocks[0] = new Rock { Pos = At(4, -6), Vel = rockVel, Owner = 0, Carrier = -1, Life = Fix.FromInt(10) };
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            Assert.AreEqual(1, s.Players[0].Swaps, "teleport claps");
            Assert.AreEqual(At(4, -6), s.Players[0].SwapTo, "swapped to the rock's spot");
            Assert.AreEqual(before, s.Players[0].SwapFrom, "left from where they stood");
            // same Step still moves everyone after the swap, so check the directions stuck
            Assert.Less(s.Players[0].Vel.X.ToDouble(), -3.0, "player kept the rock's leftward momentum");
            Assert.Greater(s.Rocks[0].Vel.X.ToDouble(), 2.0, "rock took the player's rightward momentum (they trade)");
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
            s.Rocks = new[] { new Rock { Pos = At(0.9, -6), Vel = FixVec2.Zero, Owner = 0, Carrier = -1, Life = Fix.FromInt(10) } };
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
            s.Rocks = new[] { new Rock { Pos = At(1.2, -6), Vel = new FixVec2(Fix.Zero, Fix.One), Owner = 0, Carrier = -1, Life = Fix.FromInt(10) } }; // (a still one would be gone)
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
            s.Rocks = new[] { new Rock { Pos = At(1.0, -6), Vel = new FixVec2(Fix.FromInt(-14), Fix.Zero), Owner = 1, Carrier = -1, Life = Fix.FromInt(10) } };
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
            s.Rocks[0] = new Rock { Pos = At(8, -6), Vel = new FixVec2(Fix.Zero, Fix.One), Owner = 0, Carrier = -1, Life = Fix.FromInt(10) }; // (a still one would be gone)
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
            s.Rocks[0] = new Rock { Pos = At(2, -6), Vel = FixVec2.Zero, Owner = 0, Carrier = -1, Life = Fix.FromInt(10) };
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
            Assert.AreEqual(0.5, s.Players[0].Fast.ToDouble(), 1e-3, "the passer is 50% faster");
            Assert.AreEqual(0.5, s.Players[1].Fast.ToDouble(), 1e-3, "and the teammate");
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
        public void ASwapRockIsGoneTenSecondsAfterItsThrown()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.swap");
            Play(s);
            s.PuckPos = At(14, 0);
            s.Players[0].Pos = At(-6, -6); s.Players[0].Facing = FixVec2.Right; s.Players[1].Pos = At(10, 8);
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            Assert.AreEqual(1, s.Rocks.Length, "thrown");
            for (var t = 0; t < 120 * 9; t++) _sim.Step(s, new PlayerInput[2]);
            Assert.AreEqual(1, s.Rocks.Length, "still there after 9 s, even once it's stopped");
            Assert.Less(s.Rocks[0].Life.ToDouble(), 1.1);
            for (var t = 0; t < 130; t++) _sim.Step(s, new PlayerInput[2]);
            Assert.AreEqual(0, s.Rocks.Length, "gone after 10 s");
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
            s.Rocks = new[] { new Rock { Pos = At(4.9, -6), Vel = FixVec2.Zero, Owner = 0, Carrier = 1, Life = Fix.FromInt(10) } };
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

        [TestCase(false)]
        [TestCase(true)]
        public void AnOwnGoalOnlyWhenNobodyOnTheScoringTeamTouchedIt(bool touched)
        {
            var s = Game(0, 1);
            Play(s);
            var net = _sim.Map.Goals.Find(g => g.Team == 1);
            var side = net.Min.X > Fix.Zero ? 1 : -1;
            var mouth = (side > 0 ? net.Min.X : net.Max.X).ToDouble();
            var midY = (net.Min.Y + net.Max.Y).ToDouble() / 2;
            s.Players[0].Pos = At(-side * 10, midY + 6); s.Players[0].Vel = FixVec2.Zero;
            s.Players[1].Pos = At(mouth - side * 8, midY - 6); s.Players[1].Vel = FixVec2.Zero;
            s.Carrier = -1;
            if (touched)
            {
                // the loose puck brushes red's player (who can't pick it up just now)
                s.Players[0].NoPickup = Fix.FromInt(1);
                s.PuckPos = s.Players[0].Pos + new FixVec2(_sim.T.PlayerRadius + _sim.T.PuckRadius, Fix.Zero);
                s.PuckVel = FixVec2.Zero;
                _sim.Step(s, new PlayerInput[2]);
            }
            // then blue knocks it into blue's own net
            s.PassFrom = 1; s.PuckTeam = 1;
            s.PuckPos = At(mouth - side * 1.5, midY);
            s.PuckVel = new FixVec2(Fix.FromInt(side * 10), Fix.Zero);
            for (var t = 0; t < 60 && s.Phase == Phase.Play; t++) _sim.Step(s, new PlayerInput[2]);
            Assert.AreEqual(Phase.Scored, s.Phase);
            Assert.AreEqual(0, s.LastScorer, "red's goal");
            Assert.AreEqual(touched ? 0 : -1, s.LastGoalBy, touched ? "red touched it: red's player scored" : "nobody on red touched it: an own goal");
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
            Assert.LessOrEqual(len, 45.01, "no longer than 45");
            // someone skating across it is stopped
            var mid = s.Stones[s.Stones.Length / 2];
            var x = ((mid.A.X + mid.B.X) / 2).ToDouble();
            s.Players[1].Pos = At(x, -2); s.Players[1].Vel = FixVec2.Zero;
            for (var t = 0; t < 90; t++) _sim.Step(s, new[] { default(PlayerInput), new PlayerInput { MoveY = -127 } });
            Assert.Greater(s.Players[1].Pos.Y.ToDouble(), -5.0, "can't get through");
            var first = s.Stones[0];
            for (var t = 0; t < 120 * 9; t++) _sim.Step(s, new PlayerInput[2]);
            Assert.AreEqual(first.A, s.Stones[0].A, "the first piece still standing after 9 s");
            for (var t = 0; t < 120 * 6; t++) _sim.Step(s, new PlayerInput[2]);
            Assert.AreEqual(0, s.Stones.Length, "all crumbled (each piece lasts 11 s)");
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
        public void TeamPlayStillCountsIfThePassBrushesPastAnOpponent()
        {
            // close enough to the puck's path to touch it, too far to pick it up: still a pass
            var s = Pass("ultrapuck.teamplay", At(-3, -4.05));
            Assert.AreEqual(0.5, s.Players[1].Fast.ToDouble(), 1e-3, "the teammate got the boost");
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
    

        // ------------------------------------------------------------------ face-offs

        /// <summary>The kick-off spot (the front of the group, nearest the puck) goes round the team, a face-off each.</summary>
        [Test]
        public void TheKickOffSpotRotatesRoundTheTeam()
        {
            var s = Game(0, 0, 1, 1);
            int Front() { return s.Players[0].Pos.X > s.Players[1].Pos.X ? 0 : 1; }
            var first = Front();
            _sim.FaceOff(s);
            Assert.AreNotEqual(first, Front(), "the other one takes the next kick-off");
            _sim.FaceOff(s);
            Assert.AreEqual(first, Front(), "and back again");
            // tight together, just in front of their own net
            Assert.Less((s.Players[0].Pos - s.Players[1].Pos).Length.ToDouble(), 1.4, "close together");
            var net = Bot.GoalCentre(_sim.Map, 0);
            foreach (var i in new[] { 0, 1 })
                Assert.Less((s.Players[i].Pos - net).Length.ToDouble(), 9.0, "in front of their net");
            // blue rotates too
            int BlueFront() { return s.Players[2].Pos.X < s.Players[3].Pos.X ? 2 : 3; }
            var b = BlueFront();
            _sim.FaceOff(s);
            Assert.AreNotEqual(b, BlueFront());
        }

        // ------------------------------------------------------------------ Portal Gun

        private GameState PortalGame()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.portalgun");
            Play(s);
            s.PuckPos = At(-12, -4);
            s.Players[0].Pos = At(0, 0); s.Players[0].Vel = FixVec2.Zero;
            s.Players[1].Pos = At(-16, 5);
            return s;
        }

        /// <summary>Shoot with the utility button: the portal goes up where the shot meets the wall.</summary>
        private void ShootPortal(GameState s, FixVec2 from, FixVec2 facing)
        {
            s.Players[0].Pos = from; s.Players[0].Vel = FixVec2.Zero; s.Players[0].Facing = facing.Normalized;
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            for (var t = 0; t < 70; t++)
            {
                s.Players[0].Pos = from; s.Players[0].Vel = FixVec2.Zero;
                _sim.Step(s, new PlayerInput[2]);
            }
        }

        private static Portal PortalOf(GameState s, int colour)
        {
            foreach (var po in s.Portals) if (po.Colour == colour) return po;
            Assert.Fail("no " + (colour == 0 ? "blue" : "orange") + " portal");
            return default(Portal);
        }

        [Test]
        public void PortalGunShootsBlueThenOrangeThenReplacesBlue()
        {
            var s = PortalGame();
            var up = new FixVec2(Fix.Zero, Fix.One);
            ShootPortal(s, At(0, 0), up);
            Assert.AreEqual(1, s.Portals.Length, "blue is up");
            var blue = PortalOf(s, 0);
            Assert.Greater(blue.Pos.Y.ToDouble(), 5.0, "on the top wall");
            Assert.Less(blue.Normal.Y.ToDouble(), -0.9, "facing down, into the rink");
            ShootPortal(s, At(0, 0), -up);
            Assert.AreEqual(2, s.Portals.Length, "then orange");
            Assert.Less(PortalOf(s, 1).Pos.Y.ToDouble(), -5.0, "on the bottom wall");
            ShootPortal(s, At(8, 0), up);
            Assert.AreEqual(2, s.Portals.Length, "a third shot replaces blue");
            Assert.AreEqual(8.0, PortalOf(s, 0).Pos.X.ToDouble(), 0.3, "blue moved");
            Assert.Less(PortalOf(s, 1).Pos.Y.ToDouble(), -5.0, "orange stayed");
        }

        [Test]
        public void APortalShotIntoANetFizzles()
        {
            var s = PortalGame();
            ShootPortal(s, At(18, 0), FixVec2.Right);
            Assert.AreEqual(0, s.Portals.Length, "no portal inside a net");
            Assert.AreEqual(0, s.Players[0].PortalNext, "it didn't stick: still blue next");
            ShootPortal(s, At(0, 0), new FixVec2(Fix.Zero, Fix.One));
            Assert.AreEqual(0, PortalOf(s, 0).Colour, "the next shot is blue");
            Assert.AreEqual(1, s.Players[0].PortalNext, "blue stuck: orange next");
        }

        [Test]
        public void APortalGoesOnACurvedCorner()
        {
            var map = MapDef.FromJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Maps", "hockey-rink.json")));
            _sim = new Simulation(map, null, _lib);
            var s = PortalGame();
            ShootPortal(s, At(-20, 2), new FixVec2(-Fix.One, Fix.One));
            Assert.AreEqual(1, s.Portals.Length, "up on the corner");
            var po = s.Portals[0];
            Assert.Greater(po.Normal.X.ToDouble(), 0.2, "the corner's slant (not straight along a side)");
            Assert.Less(po.Normal.Y.ToDouble(), -0.2);
            Assert.IsTrue(map.Inside(po.Pos + po.Normal), "facing the ice");
        }

        [Test]
        public void ThePuckGoesThroughAPortalAndComesOutTheOther()
        {
            var s = PortalGame();
            ShootPortal(s, At(0, 0), new FixVec2(Fix.Zero, Fix.One));
            ShootPortal(s, At(0, 0), new FixVec2(Fix.Zero, -Fix.One));
            var blue = PortalOf(s, 0); var orange = PortalOf(s, 1);
            s.Players[0].Pos = At(-12, 3);
            s.Carrier = -1;
            s.PuckPos = blue.Pos + blue.Normal * Fix.FromInt(3);
            s.PuckVel = -blue.Normal * Fix.FromInt(12);
            var speedIn = s.PuckVel.Length.ToDouble();
            var through = false;
            var halfway = false;
            var face = blue.Pos + blue.Normal * (_sim.T.WallThickness * Fix.Half);
            for (var t = 0; t < 60 && !through; t++)
            {
                _sim.Step(s, new PlayerInput[2]);
                var depth = FixVec2.Dot(s.PuckPos - face, blue.Normal).ToDouble();
                if (s.PuckPortal == blue.Id && depth >= 0.0 && depth < _sim.T.PuckRadius.ToDouble() * 0.9) halfway = true;
                if ((s.PuckPos - orange.Pos).Length < Fix.Two) through = true;
            }
            Assert.IsTrue(halfway, "part way in for a moment, not straight across (it's drawn half at each portal)");
            Assert.IsTrue(through, "came out of orange");
            Assert.Greater(FixVec2.Dot(s.PuckVel, orange.Normal).ToDouble(), 0.0, "going away from the wall");
            Assert.AreEqual(speedIn, s.PuckVel.Length.ToDouble(), 2.0, "about as fast as it went in");
            Assert.Greater(PortalOf(s, 0).Uses, 0, "counted (for the sparks)");
        }

        [Test]
        public void PlayersGoThroughPortalsToo()
        {
            var s = PortalGame();
            ShootPortal(s, At(0, 0), new FixVec2(Fix.Zero, Fix.One));
            ShootPortal(s, At(0, 0), new FixVec2(Fix.Zero, -Fix.One));
            var blue = PortalOf(s, 0); var orange = PortalOf(s, 1);
            var p = s.Players[1];
            p.Pos = orange.Pos + orange.Normal * Fix.FromInt(2);
            p.Vel = -orange.Normal * Fix.FromInt(12);
            var through = false;
            for (var t = 0; t < 60 && !through; t++)
            {
                _sim.Step(s, new PlayerInput[2]);
                if ((p.Pos - blue.Pos).Length < Fix.Two) through = true;
            }
            Assert.IsTrue(through, "came out of blue");
        }

        [Test]
        public void AFaceOffClearsThePortals()
        {
            var s = PortalGame();
            ShootPortal(s, At(0, 0), new FixVec2(Fix.Zero, Fix.One));
            Assert.AreEqual(1, s.Portals.Length);
            _sim.FaceOff(s);
            Assert.AreEqual(0, s.Portals.Length);
            Assert.AreEqual(0, s.Players[0].PortalNext, "blue first again");
        }
    

        [Test]
        public void PortalsGoOnlyOnTheBackOfANet()
        {
            var map = MapDef.FromJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Maps", "hockey-rink.json")));
            _sim = new Simulation(map, null, _lib);
            var s = PortalGame();
            // the outside of a side of blue's net, and its post from the front: no portal
            ShootPortal(s, At(23.8, 6), new FixVec2(Fix.Zero, -Fix.One));
            Assert.AreEqual(0, s.Portals.Length, "not on the side of a net");
            ShootPortal(s, At(17, 1.8), FixVec2.Right);
            Assert.AreEqual(0, s.Portals.Length, "not on a post (it used to end up inside the net)");
            // the outside of its back: yes, all of it on the back wall, facing away from the net
            ShootPortal(s, At(29, 0.5), -FixVec2.Right);
            Assert.AreEqual(1, s.Portals.Length, "on the back of the net");
            Assert.Greater(s.Portals[0].Normal.X.ToDouble(), 0.95);
            Assert.AreEqual(0.0, s.Portals[0].Pos.Y.ToDouble(), 0.4, "all of it on the back wall");
        }

        [Test]
        public void OnlyOnePlayerATeamCanHaveThePortalGun()
        {
            var s = Game(0, 0, 1);
            var pg = _lib.ById("ultrapuck.portalgun").Index;
            var sp = _lib.ById("ultrapuck.speedup").Index;
            Give(s, 0, "ultrapuck.portalgun");
            CollectionAssert.DoesNotContain(_lib.DrawOffer(s, 1, 40), pg, "not offered to a teammate");
            CollectionAssert.Contains(_lib.DrawOffer(s, 2, 40), pg, "the other team can still get one");
            // two teammates take it on the same tick: one gets it (at random, neither slot favoured), and the other's hand
            // has a fresh card in its place, to choose again from
            s.Players[0].Cards = new int[0];
            _lib.Recompute(s);
            s.Players[0].Offer = new[] { pg, sp }; s.Players[1].Offer = new[] { pg, sp };
            s.Phase = Phase.Picking; s.PhaseTime = Fix.FromInt(10);
            _sim.Step(s, new[] { new PlayerInput { Pick = 1 }, new PlayerInput { Pick = 1 }, default(PlayerInput) });
            var got = System.Array.IndexOf(s.Players[0].Cards, pg) >= 0 ? 0 : 1;
            var other = s.Players[1 - got];
            CollectionAssert.Contains(s.Players[got].Cards, pg);
            Assert.AreEqual(0, other.Cards.Length, "the other chooses again (not handed a card)");
            CollectionAssert.DoesNotContain(other.Offer, pg, "the Portal Gun is gone from their hand");
            Assert.AreEqual(2, other.Offer.Length, "a fresh card in its place");
            CollectionAssert.Contains(other.Offer, sp);
            _sim.Step(s, new PlayerInput[3]); // (they let go)
            var mine = new PlayerInput[3];
            mine[1 - got].Pick = (byte)(System.Array.IndexOf(other.Offer, sp) + 1);
            _sim.Step(s, mine);
            CollectionAssert.Contains(other.Cards, sp);
        }
    

        /// <summary>Skating into a portal with the puck: you and the puck come out of the other one (not stopped like at a wall).</summary>
        [Test]
        public void CarryingThePuckThroughAPortal()
        {
            var s = PortalGame();
            ShootPortal(s, At(0, 0), new FixVec2(Fix.Zero, Fix.One));
            ShootPortal(s, At(0, 0), new FixVec2(Fix.Zero, -Fix.One));
            var blue = PortalOf(s, 0); var orange = PortalOf(s, 1);
            var p = s.Players[0];
            p.Pos = blue.Pos + blue.Normal * Fix.FromInt(3);
            p.Vel = FixVec2.Zero;
            p.Facing = -blue.Normal;
            s.Carrier = 0;
            s.PuckPos = p.Pos - blue.Normal;
            var push = new PlayerInput { MoveX = PlayerInput.Axis(-blue.Normal.X.ToFloat()), MoveY = PlayerInput.Axis(-blue.Normal.Y.ToFloat()) };
            var through = false;
            var closest = 99.0;
            var puckIn = false;
            for (var t = 0; t < 240 && !through; t++)
            {
                _sim.Step(s, new[] { push, default(PlayerInput) });
                if (s.PuckPortal == blue.Id && p.PortalVia == 0) puckIn = true; // the puck went in ahead of the carrier
                closest = System.Math.Min(closest, FixVec2.Dot(p.Pos - blue.Pos, blue.Normal).ToDouble());
                if ((p.Pos - orange.Pos).Length < Fix.FromInt(2)) through = true;
            }
            TestContext.WriteLine("closest to blue's wall line: " + closest + ", carrier " + s.Carrier + ", via " + p.PortalVia);
            Assert.IsTrue(through, "the carrier came out of orange");
            Assert.IsTrue(puckIn, "the puck slid in ahead (not pulled back as at a wall)");
            Assert.AreEqual(0, s.Carrier, "still carrying the puck");
            Assert.Greater(FixVec2.Dot(p.Facing, orange.Normal).ToDouble(), 0.9, "facing out of orange (turned with the portals)");
            Assert.AreEqual(0, s.Score[0] + s.Score[1], "no goal from it");
        }
    

        /// <summary>A portal on a curved corner lets you in near its ends too (not only at its middle).</summary>
        [Test]
        public void TheEndsOfACurvedPortalLetYouIn()
        {
            var map = MapDef.FromJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Maps", "hockey-rink.json")));
            _sim = new Simulation(map, null, _lib);
            var s = PortalGame();
            ShootPortal(s, At(-20, 2), new FixVec2(-Fix.One, Fix.One));
            ShootPortal(s, At(0, 0), new FixVec2(Fix.Zero, -Fix.One));
            var blue = PortalOf(s, 0);
            var tangent = new FixVec2(-blue.Normal.Y, blue.Normal.X);
            var q = s.Players[1];
            // aimed at a spot near one end, along the wall's own normal there
            FixVec2 dir;
            var end = map.PointAlong(blue.Wall, blue.At + _sim.T.PortalLength * Fix.Ratio(4, 10), out dir);
            var n = new FixVec2(-dir.Y, dir.X);
            if (FixVec2.Dot(n, blue.Normal) < Fix.Zero) n = -n;
            q.Pos = end + n * Fix.Two;
            q.Vel = -n * Fix.FromInt(8);
            var went = false;
            for (var t = 0; t < 90 && !went; t++)
            {
                q.Vel = -n * Fix.FromInt(8);
                _sim.Step(s, new PlayerInput[2]);
                if (q.PortalVia != 0) went = true;
            }
            Assert.IsTrue(went, "started going in near the end of the portal");
        }
    

        /// <summary>Skating along the boards past a portal (pressed against them) never takes you off the ice.</summary>
        [TestCase("hockey-rink.json")]
        [TestCase("test-rink.json")]
        public void HuggingTheWallPastAPortalStaysOnTheIce(string file)
        {
            var map = MapDef.FromJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Maps", file)));
            _sim = new Simulation(map, null, _lib);
            foreach (var corner in new[] { true, false })
            {
                var s = PortalGame();
                if (corner) ShootPortal(s, At(-12, 2), new FixVec2(-Fix.One, Fix.One));
                else ShootPortal(s, At(-6, 0), new FixVec2(Fix.Zero, Fix.One));
                ShootPortal(s, At(4, 0), new FixVec2(Fix.Zero, -Fix.One));
                var blue = PortalOf(s, 0);
                var q = s.Players[1];
                foreach (var way in new[] { 1, -1 })
                {
                    // start along the wall a bit past one end of blue, then skate along it past the whole portal,
                    // pushing into the boards the whole time
                    FixVec2 dir;
                    var start = map.PointAlong(blue.Wall, blue.At - _sim.T.PortalLength * way, out dir);
                    var inward = new FixVec2(-dir.Y, dir.X);
                    if (FixVec2.Dot(inward, blue.Normal) < Fix.Zero) inward = -inward;
                    q.Pos = start + inward * Fix.One; q.PortalVia = 0;
                    for (var t = 0; t < 120; t++)
                    {
                        FixVec2 d2;
                        map.PointAlong(blue.Wall, blue.At, out d2);
                        q.Vel = d2 * Fix.FromInt(9 * way) - blue.Normal * Fix.FromInt(3);
                        _sim.Step(s, new PlayerInput[2]);
                        Assert.IsTrue(map.Inside(q.Pos), file + (corner ? " corner" : " side") + ": on the ice at tick " + t + " (" + q.Pos + ")");
                        foreach (var g in map.Goals) Assert.IsFalse(g.Contains(q.Pos), "not in a net");
                    }
                }
            }
        }
    

        /// <summary>
        /// Going into a straight portal right by its edge and out of one on a curved corner: you come out on the ice,
        /// along the curve (it used to measure along a straight line, which past the curve's end is behind the boards).
        /// </summary>
        [Test]
        public void OutOfACurvedPortalByItsEdgeStaysOnTheIce()
        {
            var map = MapDef.FromJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Maps", "hockey-rink.json")));
            _sim = new Simulation(map, null, _lib);
            foreach (var side in new[] { 1, -1 })
            {
                var s = PortalGame();
                ShootPortal(s, At(-20, 2), new FixVec2(-Fix.One, Fix.One));   // blue: the curved corner
                ShootPortal(s, At(0, 0), new FixVec2(Fix.Zero, -Fix.One));    // orange: the flat bottom
                var blue = PortalOf(s, 0); var orange = PortalOf(s, 1);
                var tO = new FixVec2(-orange.Normal.Y, orange.Normal.X);
                var q = s.Players[1];
                // right by orange's edge, moving into it
                q.Pos = orange.Pos + orange.Normal * (_sim.T.WallThickness * Fix.Half + Fix.Ratio(3, 10)) + tO * (_sim.T.PortalLength * Fix.Ratio(48, 100) * side);
                q.PortalVia = 0;
                var through = false;
                for (var t = 0; t < 90; t++)
                {
                    q.Vel = -orange.Normal * Fix.FromInt(6);
                    if (through) q.Vel = blue.Normal * Fix.FromInt(6);
                    _sim.Step(s, new PlayerInput[2]);
                    if (q.PortalVia == blue.Id) through = true;
                    Assert.IsTrue(map.Inside(q.Pos), "side " + side + ": on the ice at tick " + t + " (" + q.Pos + ")");
                }
                Assert.IsTrue(through, "side " + side + ": came out of blue");
            }
        }
    

        /// <summary>
        /// Hundreds of runs at the portals (a curved corner and the back of a net) from all angles, speeds and places,
        /// sliding along the boards too: nobody ever ends up off the ice or inside a net.
        /// </summary>
        [Test]
        public void NoWayOffTheIceThroughPortals()
        {
            var map = MapDef.FromJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Maps", "hockey-rink.json")));
            _sim = new Simulation(map, null, _lib);
            var s = PortalGame();
            ShootPortal(s, At(-20, 2), new FixVec2(-Fix.One, Fix.One));   // blue: the top left corner
            ShootPortal(s, At(29, 0.5), -FixVec2.Right);                  // orange: the back of blue's net
            Assert.AreEqual(2, s.Portals.Length);
            var rng = new System.Random(1234);
            var q = s.Players[1];
            s.Players[0].Pos = At(0, -8);
            var throughs = 0;
            for (var run = 0; run < 400; run++)
            {
                var po = s.Portals[run % 2];
                var t0 = new FixVec2(-po.Normal.Y, po.Normal.X);
                var lateral = (rng.NextDouble() * 2 - 1) * 3.0;   // up to well past the ends
                var outward = 0.6 + rng.NextDouble() * 2.0;
                q.Pos = po.Pos + po.Normal * Fix.FromDecimal((decimal)outward) + t0 * Fix.FromDecimal((decimal)lateral);
                if (!map.Inside(q.Pos)) continue;
                q.PortalVia = 0; q.Vel = FixVec2.Zero;
                var ang = (rng.NextDouble() * 2 - 1) * 1.4;      // into the wall, up to almost along it
                var speed = 2 + rng.NextDouble() * 16;
                var into = -po.Normal;
                var dir = new FixVec2(Fix.FromDecimal((decimal)(System.Math.Cos(ang))), Fix.FromDecimal((decimal)(System.Math.Sin(ang))));
                var push = into * dir.X + t0 * dir.Y;
                var seen = q.PortalVia;
                for (var t = 0; t < 70; t++)
                {
                    q.Vel = push * Fix.FromDecimal((decimal)speed);
                    _sim.Step(s, new PlayerInput[2]);
                    if (q.PortalVia != 0 && q.PortalVia != seen && seen != 0) throughs++;
                    if (q.PortalVia != 0) seen = q.PortalVia;
                    Assert.IsTrue(map.Inside(q.Pos), "run " + run + " tick " + t + ": off the ice at " + q.Pos);
                    foreach (var g in map.Goals) Assert.IsFalse(g.Contains(q.Pos), "run " + run + " tick " + t + ": in a net at " + q.Pos);
                }
            }
            TestContext.WriteLine("went through " + throughs + " times");
            Assert.Greater(throughs, 20, "plenty of the runs went through");
        }
    

        /// <summary>No portal shots while you have the puck; a portal on a net's back sits clear of its posts.</summary>
        [Test]
        public void NoPortalWithThePuckAndNonePastTheNetPosts()
        {
            var s = PortalGame();
            s.Carrier = 0;
            ShootPortal(s, At(0, 0), new FixVec2(Fix.Zero, Fix.One));
            Assert.AreEqual(0, s.Portals.Length + s.PortalShots.Length, "nothing fired while carrying");

            var map = MapDef.FromJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Maps", "hockey-rink.json")));
            _sim = new Simulation(map, null, _lib);
            foreach (var y in new[] { 1.7, -1.7 })
            {
                s = PortalGame();
                ShootPortal(s, At(29, y), -FixVec2.Right);   // right by a post
                Assert.AreEqual(1, s.Portals.Length, "on the back of the net (y " + y + ")");
                var half = _sim.T.PortalLength.ToDouble() / 2;
                var post = 2.085 - _sim.T.WallThickness.ToDouble() / 2;
                Assert.LessOrEqual(System.Math.Abs(s.Portals[0].Pos.Y.ToDouble()) + half, post + 0.02, "clear of the post");
            }
        }
    

        /// <summary>Free play's d-pad puck drills: to you, at you, at their net, at yours.</summary>
        [Test]
        public void FreePlayPuckDrills()
        {
            var sim = new Simulation(Rink(), Tuning.ForFreePlay(), _lib);
            var s = sim.NewGame(new[] { 0 }, 1u);
            sim.Step(s, null);
            Assert.AreEqual(Phase.Play, s.Phase);
            var p = s.Players[0];
            p.Pos = At(-5, 3); p.Vel = FixVec2.Zero;
            System.Func<byte, PlayerInput[]> drill = d => new[] { new PlayerInput { Pick = d } };

            sim.Step(s, drill(PlayerInput.PuckToMe));
            Assert.AreEqual(0, s.Carrier, "down: the puck's yours");

            sim.Step(s, drill(PlayerInput.PuckAtTheirNet));
            Assert.AreEqual(-1, s.Carrier);
            Assert.Greater(s.PuckVel.X.ToDouble(), 20.0, "right: hard at the other team's net (to the right)");

            s.Carrier = 0;
            sim.Step(s, drill(PlayerInput.PuckAtOurNet));
            Assert.Less(s.PuckVel.X.ToDouble(), -20.0, "left: at your own net");

            sim.Step(s, drill(PlayerInput.PuckAtMe));
            var to = p.Pos - s.PuckPos;
            Assert.Greater(to.Length.ToDouble(), 6.0, "up: from a few strides away");
            Assert.Greater(FixVec2.Dot(s.PuckVel.Normalized, to.Normalized).ToDouble(), 0.95, "coming straight at you");
            var caught = false;
            for (var t = 0; t < 120 && !caught; t++) { sim.Step(s, new PlayerInput[1]); caught = s.Carrier == 0; }
            Assert.IsTrue(caught, "and it reaches you");

            // not in a real match
            var match = _sim.NewGame(new[] { 0, 1 }, 1u);
            Play(match);
            match.Carrier = -1;
            _sim.Step(match, new[] { new PlayerInput { Pick = PlayerInput.PuckToMe }, default(PlayerInput) });
            Assert.AreEqual(-1, match.Carrier, "only in free play");
        }
    

        /// <summary>
        /// Same Draw / Team Vote: the Portal Gun (one a team) is never in the shared hand; just one person on the team gets
        /// it in place of one card, and in a Team Vote they keep it while the rest get the vote's winner.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void OneATeamCardsGoToOnePickerInASharedHand(bool vote)
        {
            var pg = _lib.ById("ultrapuck.portalgun").Index;
            var keep = new[] { pg, _lib.ById("ultrapuck.speedup").Index, _lib.ById("ultrapuck.heavyweight").Index, _lib.ById("ultrapuck.slowpoke").Index };
            var off = new System.Collections.Generic.List<int>();
            foreach (var c in _lib.Cards) if (System.Array.IndexOf(keep, c.Index) < 0) off.Add(c.Index);
            var seen = 0;
            for (uint seed = 1; seed < 40; seed++)
            {
                var sim = new Simulation(Rink(), new Tuning { SameDraw = !vote, TeamVote = vote, CardsOffered = 3, CardsOff = off.ToArray() }, _lib);
                var s = sim.NewGame(new[] { 0, 0, 0, 1 }, seed);
                sim.StartPicking(s, new[] { 0, 1, 2 });
                int with = 0, special = -1;
                for (var i = 0; i < 3; i++) if (System.Array.IndexOf(s.Players[i].Offer, pg) >= 0) { with++; special = i; }
                Assert.LessOrEqual(with, 1, "seed " + seed + ": in one hand at most");
                if (with == 0) continue;
                seen++;
                // the others' hands match, and differ from the special one in just that spot
                var other = special == 0 ? 1 : 0;
                var diff = 0;
                for (var k = 0; k < 3; k++) if (s.Players[special].Offer[k] != s.Players[other].Offer[k]) diff++;
                Assert.AreEqual(1, diff, "one card swapped for it");
                // the special one takes it; the others take the first card of the shared hand
                var inputs = new PlayerInput[4];
                for (var i = 0; i < 3; i++)
                    inputs[i] = new PlayerInput { Pick = (byte)(i == special ? System.Array.IndexOf(s.Players[i].Offer, pg) + 1 : 1) };
                var firstShared = s.Players[other].Offer[0];
                sim.Step(s, inputs);
                for (var i = 0; i < 3; i++)
                {
                    if (i == special) CollectionAssert.Contains(s.Players[i].Cards, pg, "it's theirs");
                    else { CollectionAssert.DoesNotContain(s.Players[i].Cards, pg); CollectionAssert.Contains(s.Players[i].Cards, firstShared, "the rest got the shared pick"); }
                }
                // and it isn't offered on that team again
                sim.StartPicking(s, new[] { 0, 1, 2 });
                for (var i = 0; i < 3; i++) CollectionAssert.DoesNotContain(s.Players[i].Offer, pg, "not again");
            }
            Assert.Greater(seen, 3, "the Portal Gun came up in some of the draws");
        }
    

        /// <summary>A portal shot onto the other team's portal takes its place: theirs is gone, the new one is there instead.</summary>
        [Test]
        public void APortalOnTheOtherTeamsPortalReplacesIt()
        {
            var s = PortalGame();
            Give(s, 1, "ultrapuck.portalgun");
            Give(s, 0, "ultrapuck.portalgun");
            ShootPortal(s, At(0, 0), new FixVec2(Fix.Zero, Fix.One));           // red's blue portal on the top wall
            var red = PortalOf(s, 0);
            // blue's player shoots near it (not quite on it)
            var q = s.Players[1];
            q.Pos = At(0.8, 0); q.Vel = FixVec2.Zero; q.Facing = new FixVec2(Fix.Zero, Fix.One);
            _sim.Step(s, new[] { default(PlayerInput), Press(PlayerInput.Utility) });
            for (var t = 0; t < 70; t++) { q.Pos = At(0.8, 0); q.Vel = FixVec2.Zero; s.Players[0].Pos = At(-6, -6); _sim.Step(s, new PlayerInput[2]); }
            Assert.AreEqual(1, s.Portals.Length, "red's is gone");
            Assert.AreEqual(1, s.Portals[0].Owner, "blue's is up");
            Assert.AreEqual(red.Pos, s.Portals[0].Pos, "in exactly its place");
        }
    

        /// <summary>
        /// Only the Portal Gun can be drawn (every other card switched off), two players: after a goal the pick still
        /// happens and play goes on, whatever the picking mode and whether they're on one team or two (it used to hang).
        /// </summary>
        [Test, Timeout(20000)]
        public void PicksWithOnlyTheOneATeamCardLeft([Values(0, 1, 2)] int mode, [Values(true, false)] bool sameTeam)
        {
            var pg = _lib.ById("ultrapuck.portalgun").Index;
            var off = new System.Collections.Generic.List<int>();
            foreach (var c in _lib.Cards) if (c.Index != pg) off.Add(c.Index);
            var sim = new Simulation(Rink(), new Tuning { SameDraw = mode == 1, TeamVote = mode == 2, CardsOffered = 3, CardsOff = off.ToArray() }, _lib);
            var s = sim.NewGame(sameTeam ? new[] { 0, 0 } : new[] { 0, 1 }, 5u);
            for (var round = 0; round < 3; round++)
            {
                sim.StartPicking(s, new[] { 0, 1 });
                // (a hand that changes under them waits for the button to be let go, as a person does)
                for (var t = 0; t < 600 && s.Phase == Phase.Picking; t++)
                    sim.Step(s, t % 2 == 0 ? new[] { new PlayerInput { Pick = 1 }, new PlayerInput { Pick = 1 } } : new PlayerInput[2]);
                Assert.AreNotEqual(Phase.Picking, s.Phase, "the pick finished (round " + round + ")");
                foreach (var p in s.Players) Assert.AreEqual(round + 1, p.Cards.Length, "everyone got a card each round (a random one if the Portal Gun wasn't theirs to have)");
                var holders = 0;
                foreach (var p in s.Players) if (System.Array.IndexOf(p.Cards, pg) >= 0) holders++;
                Assert.LessOrEqual(holders, sameTeam ? 1 : 2);
            }
        }
    

        // ------------------------------------------------------------------ Slot Machine

        private GameState SlotGame()
        {
            var s = Game(0, 1);
            Give(s, 0, "ultrapuck.slotmachine");
            Play(s);
            s.PuckPos = At(14, 8);
            s.Players[0].Pos = At(-6, 0); s.Players[1].Pos = At(10, -8);
            return s;
        }

        /// <summary>A spin lands on what was settled when it started: here forced to <paramref name="prize"/>.</summary>
        private void Land(GameState s, int prize)
        {
            var p = s.Players[0];
            p.SlotSpin = Fix.Ratio(1, 120); p.SlotWin = prize; p.SlotSpins++;
            _sim.Step(s, new PlayerInput[2]);
        }

        [Test]
        public void SlotMachineSpinsAndLosesOrWinsAboutAsOftenAsItShould()
        {
            var s = SlotGame();
            var p = s.Players[0];
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            Assert.Greater(p.SlotSpin.ToDouble(), 1.0, "spinning");
            Assert.AreEqual(1, p.SlotSpins);
            _sim.Step(s, new PlayerInput[2]);
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            Assert.AreEqual(1, p.SlotSpins, "no second spin while it spins");
            // the odds: each prize 1 in 30, so about 4 in 30 spins win
            int wins = 0, spins = 3000;
            for (var k = 0; k < spins; k++)
            {
                p.SlotSpin = Fix.Zero; p.SlotPrizeTime = Fix.Zero; p.SlotWait = Fix.Zero; p.PrevButtons = 0;
                _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
                if (p.SlotWin > 0) wins++;
                var r = p.SlotReels;
                var same = (r & 15) == ((r >> 4) & 15) && ((r >> 4) & 15) == ((r >> 8) & 15);
                Assert.AreEqual(p.SlotWin > 0, same, "three alike exactly when it's a win");
                for (var k2 = 0; k2 < 3; k2++) Assert.That((r >> (4 * k2)) & 15, Is.InRange(1, 4), "only prize symbols on the reels");
            }
            Assert.AreEqual(4.0 / 30, (double)wins / spins, 0.025, "about 4 in 30");
        }

        [Test]
        public void SlotMachinePrizes()
        {
            // Giant: twice as big and heavy for 10 s, then back
            var s = SlotGame();
            var p = s.Players[0];
            var size = p.Stats.Size.ToDouble(); var weight = p.Stats.Weight.ToDouble();
            Land(s, CardLibrary.SlotGiant);
            Assert.AreEqual(size * 2, p.Stats.Size.ToDouble(), 1e-3, "giant");
            Assert.AreEqual(weight * 2, p.Stats.Weight.ToDouble(), 1e-3);
            // no spinning while a prize lasts
            _sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            Assert.AreEqual(Fix.Zero, p.SlotSpin, "one prize at a time");
            Run(s, _sim.T.TicksPerSecond * 9, default(PlayerInput), default(PlayerInput));
            Assert.AreEqual(size * 2, p.Stats.Size.ToDouble(), 1e-3, "still giant after 9 s");
            Run(s, _sim.T.TicksPerSecond + 2, default(PlayerInput), default(PlayerInput));
            Assert.AreEqual(size, p.Stats.Size.ToDouble(), 1e-3, "back to normal after 10 s");

            // Strong: as if holding 4 Over Charge cards, for 10 s
            Land(s, CardLibrary.SlotStrong);
            Assert.AreEqual(2.0, p.Stats.OverCharge.ToDouble(), 1e-3, "4 Over Charges (+50% each)");
            Run(s, _sim.T.TicksPerSecond * 10 + 2, default(PlayerInput), default(PlayerInput));
            Assert.AreEqual(0.0, p.Stats.OverCharge.ToDouble(), 1e-3);

            // Invisible: 12 s
            Land(s, CardLibrary.SlotInvisible);
            Assert.AreEqual(12.0, p.InvisibleTime.ToDouble(), 0.05);

            // Speed Up: 120% faster for 10 s
            var t = SlotGame();
            var q = t.Players[0];
            q.Pos = At(-21, 0);
            var right = new PlayerInput { MoveX = 127 };
            Run(t, 90, right, default(PlayerInput));
            var normal = q.Vel.X.ToDouble();
            t = SlotGame(); q = t.Players[0];
            q.Pos = At(-21, 0);
            q.SlotSpin = Fix.Ratio(1, 120); q.SlotWin = CardLibrary.SlotSpeed;
            Run(t, 90, right, default(PlayerInput));
            Assert.AreEqual(normal * 2.2, q.Vel.X.ToDouble(), normal * 0.12, "120% faster");

            // a loss: spin again straight away
            var u = SlotGame(); var w = u.Players[0];
            w.SlotSpin = Fix.Ratio(1, 120); w.SlotWin = 0;
            _sim.Step(u, new PlayerInput[2]);
            _sim.Step(u, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
            Assert.Greater(w.SlotSpin.ToDouble(), 1.0, "spinning again straight away");
        }
    

        /// <summary>A Shockwave held longer pushes harder: at a full charge, 3.5x what the same wave pushed before.</summary>
        [Test]
        public void AFullShockwavePushesHarder()
        {
            double Shove(Simulation sim, int ticks)
            {
                var s = sim.NewGame(new[] { 0, 1 }, 1u);
                var c = new int[] { _lib.ById("ultrapuck.shockwave").Index };
                s.Players[0].Cards = c;
                _lib.Recompute(s);
                Play(s);
                s.PuckPos = At(14, 8);
                s.Players[0].Pos = At(0, -6);
                for (var t = 0; t < ticks; t++)
                {
                    s.Players[1].Pos = At(1.2, -6); s.Players[1].Vel = FixVec2.Zero;
                    sim.Step(s, new[] { Press(PlayerInput.Utility), default(PlayerInput) });
                }
                s.Players[1].Pos = At(1.2, -6); s.Players[1].Vel = FixVec2.Zero;
                sim.Step(s, new PlayerInput[2]);
                return s.Players[1].Vel.X.ToDouble();
            }
            var plain = new Simulation(Rink(), new Tuning { ShockFullPush = Fix.One }, _lib);
            var before = Shove(plain, 120 * 5);
            var full = Shove(_sim, 120 * 5);
            Assert.Greater(before, 0.5);
            Assert.AreEqual(3.5, full / before, 0.1, "a full charge: 3.5x the push");
            // half a charge: half way there (2.25x)
            var half = (int)(_sim.T.ShockChargeTime.ToDouble() * 60);
            Assert.AreEqual(2.25, Shove(_sim, half) / Shove(plain, half), 0.1, "half a charge: 2.25x");
        }

        // ------------------------------------------------------------------ Choose, Double Card, New Hand

        private int Id(string id) { return _lib.ById(id).Index; }

        /// <summary>Start a pick for player 0 alone, offered just <paramref name="card"/>, and take it.</summary>
        private void TakeOnly(Simulation sim, GameState s, int card)
        {
            sim.StartPicking(s, new[] { 0 });
            s.Players[0].Offer = new[] { card };
            var inputs = new PlayerInput[s.Players.Length];
            inputs[0].Pick = 1;
            sim.Step(s, inputs);
        }

        [Test]
        public void ChooseOffersTheWholeDeckWithTheUsualLimits()
        {
            var off = new[] { Id("ultrapuck.slowpoke") };
            var sim = new Simulation(Rink(), new Tuning { CardsOffered = 3, CardsOff = off }, _lib);
            var s = sim.NewGame(new[] { 0, 0, 1 }, 3u, new[] { false, false, true });
            Give(s, 0, "ultrapuck.powershot");               // max 1: has it already
            s.Players[1].Cards = new[] { Id("ultrapuck.swap") }; // one a team: a teammate has it
            _lib.Recompute(s);
            TakeOnly(sim, s, Id("ultrapuck.choose"));
            var p = s.Players[0];
            Assert.AreEqual(Phase.Picking, s.Phase, "taking Choose goes on to the deck");
            Assert.AreEqual(1, p.PickKind);
            Assert.Contains(Id("ultrapuck.choose"), p.Cards);
            foreach (var bad in new[] { "ultrapuck.choose", "ultrapuck.doublecard", "ultrapuck.powershot", "ultrapuck.swap", "ultrapuck.slowpoke" })
                Assert.IsFalse(System.Array.IndexOf(p.Offer, Id(bad)) >= 0, bad + " isn't in the deck");
            Assert.Contains(Id("ultrapuck.speedup"), p.Offer);
            Assert.Greater(p.Offer.Length, 10, "most of the deck");
            for (var k = 1; k < p.Offer.Length; k++)
                Assert.LessOrEqual(string.CompareOrdinal(_lib.Cards[p.Offer[k - 1]].Name.ToUpperInvariant(), _lib.Cards[p.Offer[k]].Name.ToUpperInvariant()), 0, "A to Z by name");
            var want = System.Array.IndexOf(p.Offer, Id("ultrapuck.speedup"));
            var inputs = new PlayerInput[3];
            sim.Step(s, inputs); // the Choose pick let go
            inputs[0].Pick = (byte)(want + 1);
            sim.Step(s, inputs);
            Assert.AreNotEqual(Phase.Picking, s.Phase, "the deck pick ends the pick");
            Assert.AreEqual(new[] { Id("ultrapuck.powershot"), Id("ultrapuck.choose"), Id("ultrapuck.speedup") }, p.Cards);
            Assert.AreEqual(0, p.PickKind);
        }

        /// <summary>
        /// The card screen keeps sending the Choose pick for a moment (a frame runs a few ticks): that mustn't take
        /// a card from the deck for them.
        /// </summary>
        [Test]
        public void TheChoosePickStillHeldDoesntPickFromTheDeck()
        {
            var sim = new Simulation(Rink(), new Tuning { CardsOffered = 3 }, _lib);
            var s = sim.NewGame(new[] { 0, 1 }, 6u);
            sim.StartPicking(s, new[] { 0 });
            s.Players[0].Offer = new[] { Id("ultrapuck.speedup"), Id("ultrapuck.choose"), Id("ultrapuck.doubledash") };
            var held = new PlayerInput[2];
            held[0].Pick = 2;
            for (var t = 0; t < 10; t++) sim.Step(s, held);
            Assert.AreEqual(Phase.Picking, s.Phase, "still choosing from the deck");
            Assert.AreEqual(1, s.Players[0].PickKind);
            Assert.AreEqual(1, s.Players[0].Cards.Length, "only Choose so far");
            sim.Step(s, new PlayerInput[2]);
            sim.Step(s, held);
            Assert.AreNotEqual(Phase.Picking, s.Phase, "a fresh pick after letting go takes a deck card");
            Assert.AreEqual(2, s.Players[0].Cards.Length);
        }

        [Test]
        public void ChooseWithTeamVoteLetsEveryonePickTheirOwn()
        {
            var sim = new Simulation(Rink(), new Tuning { TeamVote = true, CardsOffered = 3, CardsOff = OneATeamOff() }, _lib);
            var s = sim.NewGame(new[] { 0, 0 }, 4u);
            sim.StartPicking(s, new[] { 0, 1 });
            foreach (var q in s.Players) q.Offer = new[] { Id("ultrapuck.choose"), Id("ultrapuck.speedup") };
            sim.Step(s, new[] { new PlayerInput { Pick = 1 }, new PlayerInput { Pick = 1 } });
            Assert.AreEqual(Phase.Picking, s.Phase);
            sim.Step(s, new PlayerInput[2]); // the Choose picks let go
            var a = System.Array.IndexOf(s.Players[0].Offer, Id("ultrapuck.speedup"));
            var b = System.Array.IndexOf(s.Players[1].Offer, Id("ultrapuck.doubledash"));
            sim.Step(s, new[] { new PlayerInput { Pick = (byte)(a + 1) }, new PlayerInput { Pick = (byte)(b + 1) } });
            Assert.AreNotEqual(Phase.Picking, s.Phase);
            Assert.Contains(Id("ultrapuck.speedup"), s.Players[0].Cards);
            Assert.Contains(Id("ultrapuck.doubledash"), s.Players[1].Cards, "not outvoted: each picks their own from the deck");
        }

        /// <summary>Two teammates both taking Choose in one pick: a one-a-team card (Swap, Portal Gun) is in only one of their decks.</summary>
        [Test]
        public void TeammatesBothChoosingOnlyOneCanTakeSwap([Values(1u, 2u, 3u, 4u, 5u, 6u)] uint seed, [Values("ultrapuck.swap", "ultrapuck.portalgun")] string card)
        {
            var sim = new Simulation(Rink(), new Tuning { CardsOffered = 3 }, _lib);
            var s = sim.NewGame(new[] { 0, 0, 1 }, seed, new[] { false, false, true });
            sim.StartPicking(s, new[] { 0, 1 });
            s.Players[0].Offer = new[] { Id("ultrapuck.choose") };
            s.Players[1].Offer = new[] { Id("ultrapuck.choose") };
            sim.Step(s, new[] { new PlayerInput { Pick = 1 }, new PlayerInput { Pick = 1 }, default(PlayerInput) });
            Assert.AreEqual(1, s.Players[0].PickKind);
            Assert.AreEqual(1, s.Players[1].PickKind);
            var swap = Id(card);
            var with = 0;
            foreach (var i in new[] { 0, 1 }) if (System.Array.IndexOf(s.Players[i].Offer, swap) >= 0) with++;
            Assert.AreEqual(1, with, card + " in one deck, not both");
            // and taking it from there works
            var who = System.Array.IndexOf(s.Players[0].Offer, swap) >= 0 ? 0 : 1;
            var inputs = new PlayerInput[3];
            sim.Step(s, inputs); // the Choose picks let go
            inputs[who].Pick = (byte)(System.Array.IndexOf(s.Players[who].Offer, swap) + 1);
            inputs[1 - who].Pick = 1;
            sim.Step(s, inputs);
            Assert.AreNotEqual(Phase.Picking, s.Phase);
            Assert.Contains(swap, s.Players[who].Cards);
            Assert.IsFalse(System.Array.IndexOf(s.Players[1 - who].Cards, swap) >= 0);
        }

        /// <summary>
        /// One teammate takes Choose and the other Double Card in the same pick (an online game froze around this):
        /// if Double Card hands out Swap, it's gone from the Choose deck; either way the pick finishes and the match
        /// goes on.
        /// </summary>
        [Test]
        public void ChooseAndDoubleCardTogether([Values(1u, 2u, 3u, 4u, 5u, 6u, 7u, 8u, 9u, 10u, 11u, 12u)] uint seed, [Values("ultrapuck.swap", "ultrapuck.portalgun")] string card)
        {
            var sim = new Simulation(Rink(), new Tuning { CardsOffered = 5 }, _lib);
            var s = sim.NewGame(new[] { 0, 1, 0, 1 }, seed);
            sim.StartPicking(s, new[] { 1, 3 });
            s.Players[1].Offer = new[] { Id("ultrapuck.choose"), Id("ultrapuck.speedup") };
            s.Players[3].Offer = new[] { Id("ultrapuck.speedup"), Id("ultrapuck.doublecard") };
            var inputs = new PlayerInput[4];
            inputs[1].Pick = 1; inputs[3].Pick = 2;
            sim.Step(s, inputs);
            var swap = Id(card);
            Assert.AreEqual(Phase.Picking, s.Phase, "the Choose deck is next");
            Assert.AreEqual(1, s.Players[1].PickKind);
            Assert.AreEqual(2, s.Players[3].Gifts.Length, "Double Card's two cards");
            if (System.Array.IndexOf(s.Players[3].Cards, swap) >= 0)
                Assert.IsFalse(System.Array.IndexOf(s.Players[1].Offer, swap) >= 0, "Double Card gave Swap: not in the teammate's deck");
            // the deck pick (the Choose pick let go first), then on to the face-off and play
            inputs = new PlayerInput[4];
            sim.Step(s, inputs);
            inputs[1].Pick = (byte)s.Players[1].Offer.Length;
            sim.Step(s, inputs);
            Assert.AreEqual(Phase.Countdown, s.Phase);
            Assert.AreEqual(3, s.Players[3].Cards.Length);
            Assert.AreEqual(2, s.Players[1].Cards.Length);
            var holders = 0;
            foreach (var i in new[] { 1, 3 }) if (System.Array.IndexOf(s.Players[i].Cards, swap) >= 0) holders++;
            Assert.LessOrEqual(holders, 1, "one " + card + " a team");
            var ticks = 0;
            for (; ticks < 120 * 30 && s.Phase != Phase.Play; ticks++) sim.Step(s, new PlayerInput[4]);
            Assert.AreEqual(Phase.Play, s.Phase, "the match goes on (" + ticks + " ticks, " + s.PhaseTime.ToDouble() + " s left)");
            Assert.Less(ticks, 120 * 10, "within 10 s");
        }

        [Test]
        public void DoubleCardGivesTwoOtherCards([Values(1u, 2u, 3u, 4u, 5u, 6u, 7u, 8u)] uint seed)
        {
            var sim = new Simulation(Rink(), new Tuning { CardsOffered = 3 }, _lib);
            var s = sim.NewGame(new[] { 0, 0 }, seed);
            Give(s, 0, "ultrapuck.powershot", "ultrapuck.shockwave");
            s.Players[1].Cards = new[] { Id("ultrapuck.portalgun") };
            _lib.Recompute(s);
            TakeOnly(sim, s, Id("ultrapuck.doublecard"));
            var p = s.Players[0];
            Assert.AreNotEqual(Phase.Picking, s.Phase);
            Assert.AreEqual(5, p.Cards.Length, "its own card and two more");
            var got = new[] { p.Cards[3], p.Cards[4] };
            Assert.AreNotEqual(got[0], got[1]);
            foreach (var c in got)
            {
                Assert.IsFalse(_lib.IsChoose(c) || _lib.IsDouble(c), "never Choose or Double Card");
                Assert.AreNotEqual(Id("ultrapuck.powershot"), c, "max 1");
                Assert.AreNotEqual(Id("ultrapuck.portalgun"), c, "a teammate has it");
                Assert.IsFalse(_lib.IsUtilityIndex(c), "never a second utility that would replace Shockwave");
            }
        }

        [Test]
        public void DoubleCardsCardsAreShownBeforeTheCountdown()
        {
            var sim = new Simulation(Rink(), new Tuning { CardsOffered = 3 }, _lib);
            var plain = sim.NewGame(new[] { 0, 1 }, 2u);
            TakeOnly(sim, plain, Id("ultrapuck.speedup"));
            var s = sim.NewGame(new[] { 0, 1 }, 2u);
            TakeOnly(sim, s, Id("ultrapuck.doublecard"));
            var p = s.Players[0];
            Assert.AreEqual(Phase.Countdown, s.Phase);
            Assert.AreEqual(1, p.Gifted);
            Assert.AreEqual(new[] { p.Cards[1], p.Cards[2] }, p.Gifts, "the two cards it gave, for the reveal");
            Assert.AreEqual(0, plain.Players[0].Gifts.Length);
            Assert.AreEqual((plain.PhaseTime + sim.T.GiftRevealTime).Raw, s.PhaseTime.Raw, "the countdown waits for the reveal");
            sim.StartPicking(s, new[] { 0 });
            Assert.AreEqual(0, p.Gifts.Length, "a new pick clears the last reveal");
        }

        [Test]
        public void NewHandRerollsOncePerPick()
        {
            var sim = new Simulation(Rink(), new Tuning { CardsOffered = 3, ChaosOdds = 0 }, _lib); // (the plain hand: no chaos card on top)
            var s = sim.NewGame(new[] { 0, 1 }, 9u);
            sim.StartPicking(s, new[] { 0 });
            var before = (int[])s.Players[0].Offer.Clone();
            var press = new PlayerInput[2];
            press[0].Pick = PlayerInput.Reroll;
            sim.Step(s, press);
            Assert.AreEqual(before, s.Players[0].Offer, "no New Hand: a re-roll does nothing");

            Give(s, 0, "ultrapuck.newhand");
            sim.StartPicking(s, new[] { 0 });
            before = (int[])s.Players[0].Offer.Clone();
            sim.Step(s, new PlayerInput[2]);
            sim.Step(s, press);
            var after = (int[])s.Players[0].Offer.Clone();
            Assert.AreEqual(3, after.Length);
            foreach (var c in after) Assert.IsFalse(System.Array.IndexOf(before, c) >= 0, "a fresh hand");
            Assert.AreEqual(1, s.Players[0].Rerolls);
            sim.Step(s, new PlayerInput[2]);
            sim.Step(s, press);
            Assert.AreEqual(after, s.Players[0].Offer, "only once a pick");
            var pick = new PlayerInput[2];
            pick[0].Pick = 2;
            sim.Step(s, pick);
            Assert.AreNotEqual(Phase.Picking, s.Phase);
            Assert.AreEqual(after[1], s.Players[0].Cards[s.Players[0].Cards.Length - 1], "the pick is from the new hand");

            sim.StartPicking(s, new[] { 0 });
            Assert.AreEqual(0, s.Players[0].Rerolls, "a new pick: the re-roll is back");
        }

        /// <summary>
        /// The re-roll is asked for while the utility button was already held when the pick started (Shockwave is
        /// charged by holding it): it still re-rolls (it used not to, as no fresh press was seen).
        /// </summary>
        [Test]
        public void NewHandRerollsWhateverTheUtilityButtonWasDoing()
        {
            var sim = new Simulation(Rink(), new Tuning { CardsOffered = 3 }, _lib);
            var s = sim.NewGame(new[] { 0, 1 }, 9u);
            Give(s, 0, "ultrapuck.newhand");
            s.Players[0].PrevButtons = PlayerInput.Utility;
            sim.StartPicking(s, new[] { 0 });
            var before = (int[])s.Players[0].Offer.Clone();
            sim.Step(s, new[] { new PlayerInput { Buttons = PlayerInput.Utility, Pick = PlayerInput.Reroll }, default(PlayerInput) });
            Assert.AreEqual(1, s.Players[0].Rerolls);
            Assert.AreNotEqual(before, s.Players[0].Offer);
            // the request still being sent (it takes a moment to stop) doesn't use up anything more
            for (var t = 0; t < 5; t++) sim.Step(s, new[] { new PlayerInput { Pick = PlayerInput.Reroll }, default(PlayerInput) });
            Assert.AreEqual(1, s.Players[0].Rerolls);
            Assert.AreEqual(Phase.Picking, s.Phase, "still choosing");
        }

        /// <summary>The host ends a card pick that's stuck (no time limit here): everyone still choosing gets a random card, Choose's deck too.</summary>
        [Test]
        public void TheHostCanEndTheCardPick()
        {
            var sim = new Simulation(Rink(), new Tuning { CardsOffered = 3, PickTime = Fix.Zero }, _lib);
            var s = sim.NewGame(new[] { 0, 1, 1 }, 5u);
            sim.StartPicking(s, new[] { 1, 2 });
            s.Players[1].Offer = new[] { Id("ultrapuck.choose") };
            for (var t = 0; t < 600; t++) sim.Step(s, new PlayerInput[3]);
            Assert.AreEqual(Phase.Picking, s.Phase, "nobody chooses, no time limit: waits");
            sim.Step(s, new[] { new PlayerInput { Pick = PlayerInput.EndPick }, default(PlayerInput), default(PlayerInput) });
            Assert.AreNotEqual(Phase.Picking, s.Phase, "ended");
            Assert.AreEqual(2, s.Players[1].Cards.Length, "Choose, and a random card from its deck");
            Assert.AreEqual(1, s.Players[2].Cards.Length);
            // offline: the host's command straight away
            sim.StartPicking(s, new[] { 1, 2 });
            sim.HostCommand(s, PlayerInput.EndPick);
            Assert.AreNotEqual(Phase.Picking, s.Phase);
        }

        [Test]
        public void NewHandInATeamVoteRerollsTheTeamsHand()
        {
            var sim = new Simulation(Rink(), new Tuning { TeamVote = true, CardsOffered = 3, CardsOff = OneATeamOff() }, _lib);
            var s = sim.NewGame(new[] { 0, 0 }, 11u);
            Give(s, 0, "ultrapuck.newhand");
            sim.StartPicking(s, new[] { 0, 1 });
            var before = (int[])s.Players[0].Offer.Clone();
            sim.Step(s, new[] { new PlayerInput(), new PlayerInput { Pick = 1 } }); // the teammate locks a vote
            Assert.AreEqual(before[0], s.Players[1].Picked);
            sim.Step(s, new[] { new PlayerInput { Pick = PlayerInput.Reroll }, new PlayerInput { Pick = 1 } });
            Assert.AreEqual(s.Players[0].Offer, s.Players[1].Offer, "the team still shares one hand");
            foreach (var c in s.Players[0].Offer) Assert.IsFalse(System.Array.IndexOf(before, c) >= 0, "a fresh hand");
            Assert.AreEqual(-1, s.Players[1].Picked, "everyone on the team chooses again");
        }

        [Test]
        public void NewCardPicksAreTheSameEveryRun()
        {
            ulong Run()
            {
                var sim = new Simulation(Rink(), new Tuning { SameDraw = true, CardsOffered = 3 }, _lib);
                var s = sim.NewGame(new[] { 0, 0, 1, 1 }, 21u, new[] { false, true, false, true });
                foreach (var i in new[] { 0, 1 }) s.Players[i].Cards = new[] { Id("ultrapuck.newhand") };
                _lib.Recompute(s);
                sim.StartPicking(s, new[] { 0, 1 });
                var inputs = new PlayerInput[4];
                inputs[0].Pick = PlayerInput.Reroll;
                sim.Step(s, sim.Route(s, inputs));
                s.Players[0].Offer = new[] { Id("ultrapuck.choose") };
                s.Players[1].Offer = new[] { Id("ultrapuck.doublecard") };
                inputs[0].Pick = 1;
                for (var t = 0; t < 30; t++) sim.Step(s, sim.Route(s, inputs));
                var c = s.Clone();
                Assert.AreEqual(s.Checksum(), c.Checksum());
                return s.Checksum();
            }
            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void TwoTimesGoalSizeMakesEveryNetTwiceAsWide()
        {
            var card = _lib.ById("ultrapuck.widenets");
            Assert.IsNotNull(card);
            Assert.IsTrue(card.Chaos, "Giant Nets is a chaos card");
            foreach (var file in new[] { "test-rink.json", "hockey-rink.json", "s-rink.json", "u-rink.json" })
            {
                var map = MapDef.FromJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Maps", file)));
                var sim = new Simulation(map, null, _lib);
                var s = sim.NewGame(new[] { 0, 1 }, 1u);
                sim.Step(s, new PlayerInput[2]);
                Assert.AreSame(map, sim.Map, file + ": the plain map to start with");
                sim.GiveCard(s, s.Players[1], card.Index);
                _lib.Recompute(s);
                sim.Step(s, new PlayerInput[2]);
                Assert.AreSame(sim.WideMap, sim.Map, file + ": wide nets while someone holds it");
                for (var g = 0; g < map.Goals.Count; g++)
                {
                    GoalDef a = map.Goals[g], b = sim.WideMap.Goals[g];
                    var across = a.HasFacing ? Fix.Abs(a.Facing.X) >= Fix.Abs(a.Facing.Y) : (a.Max.Y - a.Min.Y) >= (a.Max.X - a.Min.X);
                    Fix wa = across ? a.Max.Y - a.Min.Y : a.Max.X - a.Min.X, wb = across ? b.Max.Y - b.Min.Y : b.Max.X - b.Min.X;
                    Assert.LessOrEqual(System.Math.Abs((wa * 2 - wb).Raw), 1L, file + ": net " + g + " twice as wide");
                    Assert.IsTrue(a.Min + a.Max == b.Min + b.Max, file + ": net " + g + " in the same place");
                    // a shot just past the old post goes in now
                    var past = across ? new FixVec2((a.Min.X + a.Max.X) / 2, a.Max.Y + wa / 4) : new FixVec2(a.Max.X + wa / 4, (a.Min.Y + a.Max.Y) / 2);
                    Assert.IsFalse(a.Contains(past));
                    Assert.IsTrue(b.Contains(past), file + ": net " + g + " reaches past its old post");
                }
                Assert.AreEqual(map.Segments.Count, sim.WideMap.Segments.Count, file + ": the same walls, moved");
                s.Players[1].Cards = new int[0];
                _lib.Recompute(s);
                sim.Step(s, new PlayerInput[2]);
                Assert.AreSame(map, sim.Map, file + ": back to normal once nobody holds it");
            }
        }

        [Test]
        public void MorePucksPutsAnExtraPuckOnTheIceThatScores()
        {
            var card = _lib.ById("ultrapuck.morepucks");
            Assert.IsNotNull(card);
            Assert.IsTrue(card.Chaos, "More Pucks is a chaos card");
            var s = Game(0, 1);
            Assert.AreEqual(0, s.Rocks.Length, "one puck to start with");
            _sim.GiveCard(s, s.Players[1], card.Index);
            _lib.Recompute(s);
            _sim.FaceOff(s);
            Assert.AreEqual(1, s.Rocks.Length, "an extra puck at the kick-off");
            var x = s.Rocks[0];
            Assert.IsTrue(x.Puck);
            Assert.AreEqual(-1, x.Owner, "nobody's");
            Assert.IsTrue(_sim.Map.Inside(x.Pos));
            Assert.Greater((x.Pos - _sim.Map.PuckSpot).Length.ToDouble(), 1.0, "beside the puck, not on it");

            // it doesn't run out, and it goes in a net like the puck: red scores in blue's net
            s.Phase = Phase.Play;
            GoalDef blue = null;
            foreach (var g in _sim.Map.Goals) if (g.Team == 1) blue = g;
            var mouth = new FixVec2(blue.Min.X - Fix.FromInt(2), (blue.Min.Y + blue.Max.Y) / 2);
            s.Rocks[0].Pos = mouth;
            s.Rocks[0].Vel = new FixVec2(Fix.FromInt(12), Fix.Zero);
            s.Rocks[0].Touch = 0;
            for (var k = 0; k < 120 && s.Phase == Phase.Play; k++) _sim.Step(s, new PlayerInput[2]);
            Assert.AreEqual(Phase.Scored, s.Phase, "the extra puck scored");
            Assert.AreEqual(1, s.Score[0]);
            Assert.AreEqual(0, s.Score[1]);
            Assert.AreEqual(1, s.Players[0].Goals, "the goal is red's, who last had it");
            Assert.IsTrue(blue.Touches(s.PuckPos, _sim.T.PuckRadius), "the puck sits in the net it went into (they trade places)");

            // back beside the kick-off spot at the next face-off; gone once nobody holds the card
            _sim.FaceOff(s);
            Assert.AreEqual(1, s.Rocks.Length);
            Assert.AreEqual(x.Pos, s.Rocks[0].Pos);
            s.Players[1].Cards = new int[0];
            _lib.Recompute(s);
            _sim.FaceOff(s);
            Assert.AreEqual(0, s.Rocks.Length, "no extra puck without the card");
        }

        [Test]
        public void LaserSightsTraceTheShotIntoTheNetAndOffTheBoards()
        {
            var laser = _lib.ById("ultrapuck.lasersight");
            Assert.IsNotNull(laser);
            var s = Game(0, 1);
            _sim.GiveCard(s, s.Players[0], laser.Index);
            _lib.Recompute(s);
            Assert.AreEqual(1, s.Players[0].Stats.LaserSight);
            var pts = new System.Collections.Generic.List<FixVec2>();
            var p = s.Players[0];
            // straight at the other team's net from in front of it: the line ends in it
            var g = _sim.Map.Goals.Find(x => x.Team == 1);
            var mid = new FixVec2((g.Min.X + g.Max.X) * Fix.Half, (g.Min.Y + g.Max.Y) * Fix.Half);
            var way = mid.X > Fix.Zero ? Fix.One : -Fix.One;
            p.Pos = new FixVec2(mid.X - way * Fix.FromInt(8), mid.Y);
            p.Vel = FixVec2.Zero;
            p.Facing = new FixVec2(way, Fix.Zero);
            s.Carrier = 0;
            s.PuckPos = p.Pos + p.Facing * Fix.One;
            p.Charge = Fix.One;
            _sim.LaserPath(s, 0, pts);
            Assert.GreaterOrEqual(pts.Count, 2);
            Assert.IsTrue(g.Touches(pts[pts.Count - 1], _sim.T.PuckRadius), "ends in the net");
            var before = s.Checksum();
            // straight at the boards beside: it bounces (a corner in the line) and changes nothing
            p.Pos = FixVec2.Zero;
            p.Facing = new FixVec2(Fix.Zero, Fix.One);
            s.PuckPos = p.Pos + p.Facing * Fix.One;
            before = s.Checksum();
            _sim.LaserPath(s, 0, pts);
            Assert.GreaterOrEqual(pts.Count, 3, "a bounce");
            Assert.Less(pts[pts.Count - 1].Y.ToDouble(), pts[1].Y.ToDouble(), "back off the boards");
            Assert.AreEqual(before, s.Checksum(), "only drawing");
            // not carrying it: no laser
            s.Carrier = -1;
            _sim.LaserPath(s, 0, pts);
            Assert.AreEqual(0, pts.Count);
        }

        [Test]
        public void LaserSightsFollowTheRealPuckRoundACurvedCorner()
        {
            var s = Game(0, 1);
            s.Phase = Phase.Play;
            // the rink's top right: a shot from inside it, up and right into the rounded corner
            Fix maxX = Fix.FromInt(-1000), maxY = Fix.FromInt(-1000);
            foreach (var seg in _sim.Map.Segments) { maxX = Fix.Max(maxX, Fix.Max(seg.A.X, seg.B.X)); maxY = Fix.Max(maxY, Fix.Max(seg.A.Y, seg.B.Y)); }
            var p = s.Players[0];
            p.Pos = new FixVec2(maxX - Fix.FromInt(9), maxY - Fix.FromInt(6));
            p.Vel = FixVec2.Zero;
            p.Facing = new FixVec2(Fix.Ratio(8, 10), Fix.Ratio(6, 10));
            s.Players[1].Pos = new FixVec2(-maxX + Fix.FromInt(4), -maxY + Fix.FromInt(4));
            s.Players[1].Vel = FixVec2.Zero;
            s.Carrier = 0;
            s.PuckPos = p.Pos + p.Facing * Fix.One;
            p.Charge = Fix.One;
            var pts = new System.Collections.Generic.List<FixVec2>();
            _sim.LaserPath(s, 0, pts);
            Assert.GreaterOrEqual(pts.Count, 3, "it comes off the corner");
            // now the real shot, as Shoot sends it, and every tick of where the puck goes against the line
            s.Carrier = -1;
            s.PuckVel = p.Facing * (_sim.T.ChargedShotMax * p.Stats.ShotPower);
            p.Charge = Fix.Zero;
            p.NoPickup = Fix.FromInt(100);
            p.Pos = new FixVec2(-maxX + Fix.FromInt(4), maxY - Fix.FromInt(4)); // (out of its way: the laser goes through bodies)
            var none = new PlayerInput[2];
            var worst = 0.0;
            var last = pts[pts.Count - 1];
            for (var k = 0; k < _sim.T.TicksPerSecond; k++)
            {
                _sim.Step(s, none);
                if (s.Phase != Phase.Play) break;
                var at = s.PuckPos;
                var best = double.MaxValue;
                for (var j = 0; j + 1 < pts.Count; j++)
                {
                    var a = pts[j]; var b = pts[j + 1];
                    var ab = b - a;
                    var len2 = ab.SqrLength;
                    var u = len2.Raw > 0 ? Fix.Clamp01(FixVec2.Dot(at - a, ab) / len2) : Fix.Zero;
                    best = System.Math.Min(best, (at - (a + ab * u)).Length.ToDouble());
                }
                worst = System.Math.Max(worst, best);
                if ((at - last).Length < Fix.Half) break; // (got to the end of the line)
            }
            Assert.Less(worst, 0.1, "the puck stays on the laser");
        }

        [Test]
        public void MovementTechRefillsDashesOnPickingUpThePuck()
        {
            var tech = _lib.ById("ultrapuck.movementtech");
            Assert.IsNotNull(tech);
            foreach (var has in new[] { true, false })
            {
                var s = Game(0, 1);
                s.Phase = Phase.Play;
                if (has) { _sim.GiveCard(s, s.Players[0], tech.Index); _lib.Recompute(s); }
                var p = s.Players[0];
                p.DashCharge = Fix.Zero;
                p.NoPickup = Fix.Zero;
                s.Players[1].Pos = -p.Pos; // (well away)
                s.Carrier = -1;
                s.PuckVel = FixVec2.Zero;
                s.PuckPos = p.Pos + p.Facing * Fix.Ratio(7, 10);
                _sim.Step(s, new PlayerInput[2]);
                Assert.AreEqual(0, s.Carrier, "picked it up");
                if (has) Assert.AreEqual(Simulation.MaxDashes(p), p.DashCharge, "dashes back");
                else Assert.Less(p.DashCharge.ToDouble(), 0.5, "no card: still recharging");
            }
        }

        [Test]
        public void HeadStartGivesTheExtraPucksToo()
        {
            var more = _lib.ById("ultrapuck.morepucks");
            var head = _lib.ById("ultrapuck.headstart");
            Assert.IsNotNull(more); Assert.IsNotNull(head);
            // two on red with Head Start: one starts with the puck, the other with the extra puck
            var s = Game(0, 0, 1);
            _sim.GiveCard(s, s.Players[0], head.Index);
            _sim.GiveCard(s, s.Players[2], more.Index);
            _lib.Recompute(s);
            _sim.FaceOff(s);
            Assert.AreEqual(1, s.Rocks.Length);
            Assert.IsTrue(s.Carrier == 0 || s.Carrier == 1, "red has the puck");
            Assert.AreEqual(1 - s.Carrier, s.Rocks[0].Carrier, "and the other red player the extra puck");
            // alone on red: the extra puck waits right beside them
            s = Game(0, 1);
            _sim.GiveCard(s, s.Players[0], head.Index);
            _sim.GiveCard(s, s.Players[1], more.Index);
            _lib.Recompute(s);
            _sim.FaceOff(s);
            Assert.AreEqual(0, s.Carrier);
            Assert.AreEqual(-1, s.Rocks[0].Carrier);
            Assert.Less((s.Rocks[0].Pos - s.Players[0].Pos).Length.ToDouble(), 2.0, "beside red's player");
        }

        [Test]
        public void ChaosCardsComeOnTopOfAHandAboutOneTimeInSix()
        {
            var chaosOffers = 0;
            const int picks = 600;
            for (var k = 0; k < picks; k++)
            {
                var s = _sim.NewGame(new[] { 0, 1 }, (uint)(k + 1));
                _sim.StartPicking(s, new[] { 0 });
                var offer = s.Players[0].Offer;
                Assert.Greater(offer.Length, 0);
                for (var j = 0; j < offer.Length - 1; j++) Assert.IsFalse(_lib.IsChaosIndex(offer[j]), "never in the hand itself");
                if (_lib.IsChaosIndex(offer[offer.Length - 1]))
                {
                    chaosOffers++;
                    Assert.AreEqual(_sim.T.CardsOffered + 1, offer.Length, "on top of a full hand");
                }
            }
            Assert.That(chaosOffers, Is.InRange(picks / 6 - 40, picks / 6 + 40), "about one pick in six");
            // one copy a match: once anyone holds a chaos card, nobody is offered it again (only the others)
            var held = new System.Collections.Generic.List<int>();
            foreach (var c in _lib.Cards) if (c.Chaos) held.Add(c.Index);
            Assert.Greater(held.Count, 1, "needs two chaos cards");
            var always = new Simulation(_sim.Map, new Tuning { ChaosOdds = 1 }, _lib);
            for (var k = 0; k < 60; k++)
            {
                var s = always.NewGame(new[] { 0, 1 }, (uint)(k + 1));
                always.GiveCard(s, s.Players[1], held[0]); // the other team has it
                _lib.Recompute(s);
                always.StartPicking(s, new[] { 0 });
                var offer = s.Players[0].Offer;
                Assert.AreNotEqual(held[0], offer[offer.Length - 1], "not again");
                Assert.IsTrue(_lib.IsChaosIndex(offer[offer.Length - 1]), "another chaos card instead");
                foreach (var c in held) always.GiveCard(s, s.Players[1], c);
                _lib.Recompute(s);
                always.StartPicking(s, new[] { 0 });
                foreach (var c in s.Players[0].Offer) Assert.IsFalse(_lib.IsChaosIndex(c), "every chaos card is taken: none offered");
            }
            // switched off: never
            var never = new Simulation(_sim.Map, new Tuning { ChaosOdds = 0 }, _lib);
            for (var k = 0; k < 60; k++)
            {
                var s = never.NewGame(new[] { 0, 1 }, (uint)(k + 1));
                never.StartPicking(s, new[] { 0 });
                foreach (var c in s.Players[0].Offer) Assert.IsFalse(_lib.IsChaosIndex(c));
            }
        }
    }
}
