using System.IO;
using NUnit.Framework;
using UltraPuck.Core;
using UnityEngine;

namespace UltraPuck.Tests
{
    public class CardTests
    {
        private static MapDef Rink()
        {
            return MapDef.FromJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Maps", "test-rink.json")));
        }

        /// <summary>Every card file, in name order (the same way the game loads them).</summary>
        private static CardLibrary Library()
        {
            var lib = new CardLibrary();
            var files = Directory.GetFiles(Path.Combine(Application.streamingAssetsPath, "Cards"), "*.json");
            System.Array.Sort(files, System.StringComparer.Ordinal);
            foreach (var f in files) lib.AddJson(File.ReadAllText(f), Path.GetFileName(f));
            return lib;
        }

        private static int Card(CardLibrary lib, string id) { return lib.ById(id).Index; }

        /// <summary>Cards with no picture yet (take an id out once its art is in Cards/Art).</summary>
        private static readonly string[] AwaitingArt = { "ultrapuck.choose", "ultrapuck.newhand", "ultrapuck.doublecard" };

        /// <summary>Plain one-card picks: Choose (a second pick) and Double Card (two more cards) switched off.</summary>
        private static Tuning OneCardPicks(CardLibrary lib) { return new Tuning { CardsOff = lib.CardGivers() }; }

        [Test]
        public void CardsLoadWithArt()
        {
            var lib = Library();
            Assert.GreaterOrEqual(lib.Cards.Count, 8);
            foreach (var c in lib.Cards)
            {
                var art = Path.Combine(Application.streamingAssetsPath, "Cards", "Art", c.Art);
                // (new cards waiting for their pictures show the card without one until then)
                if (System.Array.IndexOf(AwaitingArt, c.Id) < 0)
                    Assert.IsTrue(File.Exists(art + ".jpg") || File.Exists(art + ".png"), c.Id + " has art");
                Assert.IsTrue(c.Face.Count > 0 || !string.IsNullOrEmpty(c.Text), c.Id + " has face rows or text");
                Assert.IsNotEmpty(c.Flavor, c.Id + " has flavour");
            }
        }

        [Test]
        public void ATypoInACardFileIsAnError()
        {
            var lib = new CardLibrary();
            Assert.Throws<System.FormatException>(() => lib.AddJson("{\"cards\":[{\"id\":\"x\",\"stats\":{\"MoveSped\":\"x2\"}}]}", "test"));
            Assert.Throws<System.FormatException>(() => lib.AddJson("{\"cards\":[{\"id\":\"y\",\"tier\":\"Z\"}]}", "test"));
        }

        [Test]
        public void CardsCanHaveAFinish()
        {
            var lib = new CardLibrary();
            lib.AddJson("{\"cards\":[{\"id\":\"a\",\"finish\":\"Foil\"},{\"id\":\"b\",\"finish\":\"polychrome\"},{\"id\":\"c\"},{\"id\":\"e\",\"finish\":\"HOLO\"},{\"id\":\"f\",\"finish\":\"pearl\"}]}", "test");
            Assert.AreEqual("holo", lib.ById("e").Finish);
            Assert.AreEqual("pearl", lib.ById("f").Finish);
            var all = new CardLibrary();
            for (var k = 0; k < CardDef.FinishNames.Length; k++)
                all.AddJson("{\"cards\":[{\"id\":\"f" + k + "\",\"finish\":\"" + CardDef.FinishNames[k] + "\"}]}", "test");
            Assert.AreEqual(CardDef.FinishNames.Length, all.Cards.Count);
            Assert.AreEqual("foil", lib.ById("a").Finish);
            Assert.AreEqual("polychrome", lib.ById("b").Finish);
            Assert.IsNull(lib.ById("c").Finish);
            var glare = new CardLibrary();
            glare.AddJson("{\"cards\":[{\"id\":\"g\",\"glare\":false},{\"id\":\"h\",\"glare\":true},{\"id\":\"i\"}]}", "test");
            Assert.IsFalse(glare.ById("g").Glare);
            Assert.IsTrue(glare.ById("h").Glare);
            Assert.IsTrue(glare.ById("i").Glare);
            glare.AddJson("{\"cards\":[{\"id\":\"j\",\"textBoxes\":true}]}", "test");
            Assert.IsTrue(glare.ById("j").TextBoxes);
            Assert.IsFalse(glare.ById("i").TextBoxes);
            glare.AddJson("{\"cards\":[{\"id\":\"k\",\"textOutline\":true}]}", "test");
            Assert.IsTrue(glare.ById("k").TextOutline);
            Assert.IsFalse(glare.ById("j").TextOutline);
            Assert.Throws<System.FormatException>(() => lib.AddJson("{\"cards\":[{\"id\":\"d\",\"finish\":\"gold\"}]}", "test"));
        }

        [Test]
        public void CardsChangeStats()
        {
            var lib = Library();
            var sim = new Simulation(Rink(), null, lib);
            var s = sim.NewGame(new[] { 0, 1 });
            s.Players[0].Cards = new[] { Card(lib, "ultrapuck.speedup"), Card(lib, "ultrapuck.slowpoke"), Card(lib, "ultrapuck.doubledash") };
            lib.Recompute(s);
            Assert.AreEqual(1.15, s.Players[0].Stats.MoveSpeed.ToDouble(), 1e-4, "Speed Up");
            Assert.AreEqual(0.9, s.Players[1].Stats.MoveSpeed.ToDouble(), 1e-4, "Slow Poke slows the other team");
            Assert.AreEqual(1, s.Players[0].Stats.ExtraDashes, "Double Dash");
            Assert.AreEqual(0, s.Players[1].Stats.ExtraDashes);
        }

        [Test]
        public void DoubleDashChecksTwiceBeforeRecharging()
        {
            var lib = Library();
            var sim = new Simulation(Rink(), null, lib);
            var s = sim.NewGame(new[] { 0 });
            s.Players[0].Cards = new[] { Card(lib, "ultrapuck.doubledash") };
            lib.Recompute(s);
            sim.FaceOff(s);
            s.Phase = Phase.Play;
            s.Players[0].Pos = new FixVec2(Fix.Zero, Fix.FromInt(-6));
            var press = new[] { new PlayerInput { Buttons = PlayerInput.ShootCheck } };
            var none = new[] { default(PlayerInput) };
            sim.Step(s, press); sim.Step(s, none);
            Assert.Greater(s.Players[0].DashTime.ToDouble(), 0.0, "first check");
            for (var t = 0; t < 40; t++) sim.Step(s, none);
            sim.Step(s, press);
            Assert.Greater(s.Players[0].DashTime.ToDouble(), 0.0, "second check straight after");
            sim.Step(s, none);
            for (var t = 0; t < 60; t++) sim.Step(s, none); // past the second check (0.4 s), well before a recharge
            sim.Step(s, press);
            Assert.AreEqual(0.0, s.Players[0].DashTime.ToDouble(), "no third check until it recharges");
        }

        /// <summary>Score a goal against team 1 (orange scores) and run out the celebration.</summary>
        private static void OrangeScores(Simulation sim, GameState s, PlayerInput[] inputs = null)
        {
            s.Phase = Phase.Play;
            s.PuckPos = new FixVec2(Fix.FromInt(20), Fix.Zero);
            s.PuckVel = new FixVec2(Fix.FromInt(20), Fix.Zero);
            for (var t = 0; t < 1000 && s.Phase != Phase.Picking && s.Phase != Phase.Countdown; t++) sim.Step(s, inputs);
        }

        [Test]
        public void TheTeamThatConcededPicksACard()
        {
            var lib = Library();
            var sim = new Simulation(Rink(), new Tuning { ChaosOdds = 0 }, lib); // (the plain hand: no chaos card on top)
            var s = sim.NewGame(new[] { 0, 1, 0, 1 }, 99u);
            OrangeScores(sim, s);
            Assert.AreEqual(Phase.Picking, s.Phase);
            for (var i = 0; i < 4; i++)
            {
                var p = s.Players[i];
                if (p.Team == 0) { Assert.AreEqual(0, p.Offer.Length, "the scorers don't pick"); continue; }
                Assert.AreEqual(3, p.Offer.Length, "three cards offered");
                Assert.AreNotEqual(p.Offer[0], p.Offer[1]);
                Assert.AreNotEqual(p.Offer[1], p.Offer[2]);
                Assert.AreNotEqual(p.Offer[0], p.Offer[2]);
            }
            // an ordinary card from each offer (not one that hands out more cards: Choose, Double Card, New Hand)
            System.Func<int[], int> plain = offer =>
            {
                for (var k = offer.Length - 1; k >= 0; k--)
                {
                    var giver = false;
                    foreach (var m in lib.Cards[offer[k]].Mods) giver |= m.Stat == "Choose" || m.Stat == "DoubleCard" || m.Stat == "NewHand";
                    if (!giver) return k;
                }
                return -1;
            };
            var k1 = plain(s.Players[1].Offer);
            var k3 = plain(s.Players[3].Offer);
            Assert.GreaterOrEqual(k1, 0); Assert.GreaterOrEqual(k3, 0);
            var want1 = s.Players[1].Offer[k1];
            var want3 = s.Players[3].Offer[k3];
            // player 1 takes theirs; player 3 hasn't chosen yet, so play waits
            sim.Step(s, new[] { default(PlayerInput), new PlayerInput { Pick = (byte)(k1 + 1) }, default(PlayerInput), default(PlayerInput) });
            Assert.AreEqual(Phase.Picking, s.Phase);
            sim.Step(s, new[] { default(PlayerInput), default(PlayerInput), default(PlayerInput), new PlayerInput { Pick = (byte)(k3 + 1) } });
            Assert.AreEqual(Phase.Countdown, s.Phase, "everyone chose: on to the face-off");
            CollectionAssert.AreEqual(new[] { want1 }, s.Players[1].Cards);
            CollectionAssert.AreEqual(new[] { want3 }, s.Players[3].Cards);
            Assert.AreEqual(0, s.Players[0].Cards.Length);
        }

        [Test]
        public void RunningOutOfTimePicksForYou()
        {
            var lib = Library();
            var sim = new Simulation(Rink(), OneCardPicks(lib), lib);
            var s = sim.NewGame(new[] { 0, 1 }, 5u);
            OrangeScores(sim, s);
            Assert.AreEqual(Phase.Picking, s.Phase);
            var offer = s.Players[1].Offer;
            for (var t = 0; t < 31 * sim.T.TicksPerSecond && s.Phase == Phase.Picking; t++) sim.Step(s, null);
            Assert.AreEqual(Phase.Countdown, s.Phase);
            Assert.AreEqual(1, s.Players[1].Cards.Length);
            CollectionAssert.Contains(offer, s.Players[1].Cards[0]);
        }

        [Test]
        public void OffersRespectTheCopyLimit()
        {
            var lib = Library();
            var sim = new Simulation(Rink(), null, lib);
            var s = sim.NewGame(new[] { 0, 1 }, 7u);
            var rc = Card(lib, "ultrapuck.quickrecharge"); // max 3
            s.Players[1].Cards = new[] { rc, rc, rc };
            for (var k = 0; k < 200; k++)
                CollectionAssert.DoesNotContain(lib.DrawOffer(s, 1, 3), rc);
        }

        /// <summary>Every card a player could be offered turns up as often as any other, whatever its tier.</summary>
        [Test]
        public void EveryCardIsOfferedAsOftenAsAnyOther()
        {
            var lib = Library();
            var sim = new Simulation(Rink(), null, lib);
            var s = sim.NewGame(new[] { 0, 1, 0, 1 }, 21u);
            var seen = new int[lib.Cards.Count];
            const int offers = 30000;
            for (var k = 0; k < offers; k++)
                foreach (var c in lib.DrawOffer(s, 1, 3)) seen[c]++;
            int least = int.MaxValue, most = 0, kinds = 0;
            var tiers = new System.Collections.Generic.HashSet<CardTier>();
            for (var c = 0; c < seen.Length; c++)
            {
                if (seen[c] == 0) continue; // not on offer to this player (it needs another card first)
                if (lib.Cards[c].Odds != 100) continue; // (rarer on purpose: checked below)
                kinds++;
                tiers.Add(lib.Cards[c].Tier);
                least = System.Math.Min(least, seen[c]);
                most = System.Math.Max(most, seen[c]);
            }
            TestContext.WriteLine(kinds + " cards on offer, seen between " + least + " and " + most + " times each");
            Assert.GreaterOrEqual(kinds, lib.Cards.Count - 3, "nearly every card can be offered to a player with a teammate (not chaos cards, and Choose is counted apart)");
            Assert.Greater(tiers.Count, 2, "cards of several tiers");
            var each = offers * 3.0 / kinds;
            // (0.9: a hand holds one utility card at most, so the utilities come a little less often than the rest)
            Assert.Greater(least, each * 0.9, "the least seen card is close to an even share (" + each + ")");
            Assert.Less(most, each * 1.07, "and so is the most seen");
            // Choose ("odds": 70) comes about 30% less often than the rest
            var choose = lib.ById("ultrapuck.choose");
            Assert.AreEqual(70, choose.Odds);
            Assert.That(seen[choose.Index] / (double)((least + most) / 2), Is.InRange(0.6, 0.8), "Choose is about 70% as likely");
        }

        [Test]
        public void BotsPickTheirCards()
        {
            var lib = Library();
            var sim = new Simulation(Rink(), OneCardPicks(lib), lib);
            var s = sim.NewGame(new[] { 0, 1 }, 11u);
            OrangeScores(sim, s);
            var inputs = new PlayerInput[2];
            var ticks = 0;
            var hovers = new System.Collections.Generic.HashSet<byte>();
            while (s.Phase == Phase.Picking && ticks < 10 * sim.T.TicksPerSecond)
            {
                for (var i = 0; i < 2; i++) inputs[i] = Bot.Think(sim, s, i);
                hovers.Add(inputs[1].Hover);
                sim.Step(s, inputs);
                ticks++;
            }
            Assert.AreEqual(Phase.Countdown, s.Phase, "the bot chose");
            Assert.AreEqual(1, s.Players[1].Cards.Length);
            var delay = (int)(sim.T.BotPickDelay.ToDouble() * sim.T.TicksPerSecond);
            Assert.GreaterOrEqual(ticks, delay, "after looking the cards over, so people can see it choose");
            Assert.LessOrEqual(ticks, delay + 4, "but no longer than that");
            Assert.Greater(hovers.Count, 1, "its highlight moved along the cards");
        }
    }
}
