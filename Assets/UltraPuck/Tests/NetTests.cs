using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UltraPuck.Core;
using UltraPuck.Core.Net;
using UnityEngine;

namespace UltraPuck.Tests
{
    public class NetTests
    {
        private static MapDef Rink()
        {
            return MapDef.FromJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Maps", "test-rink.json")));
        }

        private static CardLibrary Cards()
        {
            var lib = new CardLibrary();
            var files = Directory.GetFiles(Path.Combine(Application.streamingAssetsPath, "Cards"), "*.json");
            System.Array.Sort(files, System.StringComparer.Ordinal);
            foreach (var f in files) lib.AddJson(File.ReadAllText(f), Path.GetFileName(f));
            return lib;
        }

        /// <summary>A person's controls: random, held for a few ticks like a real thumb, sometimes picking a card.</summary>
        private static PlayerInput Person(int who, int tick)
        {
            var r = new System.Random(who * 7919 + tick / 8);
            return new PlayerInput
            {
                MoveX = (sbyte)r.Next(-127, 128), MoveY = (sbyte)r.Next(-127, 128),
                AimX = r.Next(3) == 0 ? (sbyte)r.Next(-127, 128) : (sbyte)0, AimY = (sbyte)r.Next(-127, 128),
                Buttons = (byte)r.Next(0, 4),
                Pick = (byte)(r.Next(4) == 0 ? r.Next(1, 4) : 0),
            };
        }

        /// <summary>
        /// Two PCs over a bad network (latency, jitter, 10% loss), one person on each plus two bots, cards on:
        /// after half a minute of play both PCs must have exactly the same game, with rewinds along the way.
        /// </summary>
        [Test, Timeout(300000)]
        public void TwoPcsOverABadNetworkStayIdentical()
        {
            var map = Rink();
            var cards = Cards();
            System.Func<Simulation> make = () => new Simulation(map, null, cards);
            var net = new FakeNetwork(1234) { Latency = 0.05, Jitter = 0.04, Loss = 0.1 };
            var host = NetSession.Host(net.Add(), make, 42u);
            var client = NetSession.Join(net.Add(), make, 42u);
            host.SetLocalPlayers(new[] { new KeyValuePair<string, int>("Host", 0) });
            client.SetLocalPlayers(new[] { new KeyValuePair<string, int>("Guest", 1) });
            host.Bots = 2;

            const double dt = 1.0 / 120.0;
            var now = 0.0;
            System.Action tickNet = () => { now += dt; net.Advance(dt); host.Update(now); client.Update(now); };

            for (var k = 0; k < 600 && !(client.Current == NetSession.Stage.Lobby && host.Seats.Count == 4 && client.Seats.Count == 4); k++) tickNet();
            Assert.AreEqual(NetSession.Stage.Lobby, client.Current, "the client got in");
            Assert.AreEqual(4, client.Seats.Count, "the client sees everyone");

            host.StartMatch(777u);
            for (var k = 0; k < 600 && client.Current != NetSession.Stage.Playing; k++) tickNet();
            Assert.AreEqual(NetSession.Stage.Playing, client.Current, "the client started");

            var hostPlayer = host.PlayerOf(0);
            var clientPlayer = client.PlayerOf(0);
            Assert.AreNotEqual(hostPlayer, clientPlayer);
            var stalls = 0;
            for (var k = 0; k < 30 * 120; k++)
            {
                tickNet();
                if (!host.Step(new[] { Person(1, host.Match.LocalInputTick) })) stalls++;
                if (!client.Step(new[] { Person(2, client.Match.LocalInputTick) })) stalls++;
            }
            // let everything arrive: both keep going with no input until a recent tick is confirmed everywhere
            var settle = host.Match.Tick;
            for (var k = 0; k < 2000 && (host.Match.Confirmed < settle || client.Match.Confirmed < settle); k++)
            {
                tickNet();
                host.Step(new[] { default(PlayerInput) });
                client.Step(new[] { default(PlayerInput) });
            }
            Assert.GreaterOrEqual(host.Match.Confirmed, settle);
            Assert.GreaterOrEqual(client.Match.Confirmed, settle);
            // corrections are gathered for a few ticks before the replay runs: play those so it has
            for (var k = 0; k <= Rollback.RewindEvery; k++) { tickNet(); host.Step(new[] { default(PlayerInput) }); client.Step(new[] { default(PlayerInput) }); }
            var a = host.Match.SavedAt(settle);
            var b = client.Match.SavedAt(settle);
            Assert.IsNotNull(a); Assert.IsNotNull(b);
            Assert.AreEqual(a.Checksum(), b.Checksum(), "both PCs have the same game");
            Assert.AreEqual(-1, host.DesyncTick, host.Problem);
            Assert.AreEqual(-1, client.DesyncTick, client.Problem);
            Assert.Greater(host.Match.Rewinds + client.Match.Rewinds, 50, "late controls were fixed up by rewinding");
            Assert.Greater(a.Tick, 3000);
            TestContext.WriteLine("rewinds host " + host.Match.Rewinds + " (longest " + host.Match.LongestRewind + "), client " + client.Match.Rewinds +
                " (longest " + client.Match.LongestRewind + "), stalls " + stalls + ", score " + a.Score[0] + "-" + a.Score[1] + ", cards " + a.Players[0].Cards.Length + "/" + a.Players[1].Cards.Length);
        }

        /// <summary>

        /// Six PCs drawing about 600 frames a second over a laggy network: controls still go out at the fixed rate

        /// (not once a frame), the messages stay small, nobody waits much, and everyone ends with the same game.

        /// </summary>

        [Test, Timeout(300000)]

        public void SixPcsAtAHighFrameRateSendAtTheFixedRate()

        {

            var map = Rink();

            var cards = Cards();

            System.Func<Simulation> make = () => new Simulation(map, null, cards);

            var net = new FakeNetwork(66) { Latency = 0.08, Jitter = 0.03, Loss = 0.05 };

            var pcs = new List<NetSession> { NetSession.Host(net.Add(), make, 5u) };

            for (var k = 1; k < 6; k++) pcs.Add(NetSession.Join(net.Add(), make, 5u));

            for (var k = 0; k < 6; k++) pcs[k].SetLocalPlayers(new[] { new KeyValuePair<string, int>("P" + k, k & 1) });

            var host = pcs[0];



            const int frames = 5; // updates a tick: 600 fps

            const double dt = 1.0 / 120.0 / frames;

            var now = 0.0;

            System.Action frame = () => { now += dt; net.Advance(dt); foreach (var s in pcs) s.Update(now); };

            for (var k = 0; k < 600 * frames && pcs.Exists(s => s.Current != NetSession.Stage.Lobby || s.Seats.Count != 6); k++) frame();

            Assert.IsFalse(pcs.Exists(s => s.Seats.Count != 6), "everyone got in");

            host.StartMatch(4u);

            for (var k = 0; k < 600 * frames && pcs.Exists(s => s.Current != NetSession.Stage.Playing); k++) frame();

            Assert.IsFalse(pcs.Exists(s => s.Current != NetSession.Stage.Playing), "everyone started");



            long msgs = host.MessagesSent, bytes = host.BytesSent;

            var stalls = 0;

            const int seconds = 10;

            for (var k = 0; k < seconds * 120 * frames; k++)

            {

                frame();

                if (k % frames != frames - 1) continue;

                for (var i = 0; i < pcs.Count; i++)

                    if (!pcs[i].Step(new[] { Person(20 + i, pcs[i].Match.LocalInputTick) })) stalls++;

            }

            var perLink = (host.MessagesSent - msgs) / (double)seconds / 5;

            var kbPerLink = (host.BytesSent - bytes) / (double)seconds / 5 / 1024;

            TestContext.WriteLine("host: " + perLink.ToString("0") + " messages a second per PC, " + kbPerLink.ToString("0.0") + " KB/s per PC, stalls " + stalls);

            Assert.Less(perLink, NetSession.SendRate * 1.2, "a fixed rate, not once a frame");

            Assert.Less(kbPerLink, 100, "small messages");

            Assert.Less(stalls, seconds * 120 * 6 / 10, "hardly any waiting");



            var settle = host.Match.Tick;

            for (var k = 0; k < 3000 * frames && pcs.Exists(s => s.Match.Confirmed < settle); k++)

            {

                frame();

                if (k % frames == frames - 1) foreach (var s in pcs) s.Step(new[] { default(PlayerInput) });

            }

            for (var k = 0; k <= Rollback.RewindEvery; k++) { for (var f = 0; f < frames; f++) frame(); foreach (var s in pcs) s.Step(new[] { default(PlayerInput) }); }

            var h = host.Match.SavedAt(settle).Checksum();

            foreach (var s in pcs)

            {

                Assert.AreEqual(h, s.Match.SavedAt(settle).Checksum(), "everyone has the same game");

                Assert.AreEqual(-1, s.DesyncTick, s.Problem);

            }

        }



        /// <summary>Three PCs (two through the host); someone leaves and the match goes on, then they come back to their seats.</summary>
        [Test, Timeout(300000)]
        public void ThreePcsThroughTheHost()
        {
            var map = Rink();
            System.Func<Simulation> make = () => new Simulation(map);
            var net = new FakeNetwork(99) { Latency = 0.03, Jitter = 0.03, Loss = 0.05 };
            var host = NetSession.Host(net.Add(), make, 1u);
            var c1 = NetSession.Join(net.Add(), make, 1u);
            var c2 = NetSession.Join(net.Add(), make, 1u);
            host.SetLocalPlayers(new[] { new KeyValuePair<string, int>("H", 0) });
            c1.SetLocalPlayers(new[] { new KeyValuePair<string, int>("A", 1) });
            c2.SetLocalPlayers(new[] { new KeyValuePair<string, int>("B", 0), new KeyValuePair<string, int>("C", 1) });
            const double dt = 1.0 / 120.0;
            var now = 0.0;
            var all = new[] { host, c1, c2 };
            System.Action tickNet = () => { now += dt; net.Advance(dt); foreach (var s in all) s.Update(now); };
            for (var k = 0; k < 600 && host.Seats.Count < 4; k++) tickNet();
            Assert.AreEqual(4, host.Seats.Count);
            host.StartMatch(5u);
            for (var k = 0; k < 600 && (c1.Current != NetSession.Stage.Playing || c2.Current != NetSession.Stage.Playing); k++) tickNet();
            for (var k = 0; k < 20 * 120; k++)
            {
                tickNet();
                host.Step(new[] { Person(10, host.Match.LocalInputTick) });
                c1.Step(new[] { Person(11, c1.Match.LocalInputTick) });
                c2.Step(new[] { Person(12, c2.Match.LocalInputTick), Person(13, c2.Match.LocalInputTick) });
            }
            var settle = host.Match.Tick;
            for (var k = 0; k < 2000; k++)
            {
                var done = true;
                foreach (var s in all) done &= s.Match.Confirmed >= settle;
                if (done) break;
                tickNet();
                foreach (var s in all) s.Step(new[] { default(PlayerInput), default(PlayerInput) });
            }
            // corrections are gathered for a few ticks before the replay runs: play those so it has
            for (var k = 0; k <= Rollback.RewindEvery; k++) { tickNet(); foreach (var s in all) s.Step(new[] { default(PlayerInput), default(PlayerInput) }); }
            var h = host.Match.SavedAt(settle).Checksum();
            Assert.AreEqual(h, c1.Match.SavedAt(settle).Checksum(), "client 1 matches the host");
            Assert.AreEqual(h, c2.Match.SavedAt(settle).Checksum(), "client 2 (whose controls go through the host) matches too");
            foreach (var s in all) Assert.AreEqual(-1, s.DesyncTick, s.Problem);

            // client 2 leaves: the match goes on, bots play their two seats
            c2.Dispose();
            for (var k = 0; k < 12 * 120; k++)
            {
                now += dt; net.Advance(dt); host.Update(now); c1.Update(now);
                host.Step(new[] { Person(10, host.Match.LocalInputTick) });
                c1.Step(new[] { Person(11, c1.Match.LocalInputTick) });
            }
            Assert.AreEqual(NetSession.Stage.Playing, host.Current, "still playing");
            Assert.AreEqual(NetSession.Stage.Playing, c1.Current);
            Assert.IsTrue(host.Seats[2].Away && host.Seats[3].Away, "their seats wait for them");
            Assert.IsTrue(c1.Seats[2].Away, "client 1 was told");
            Assert.Greater(host.Match.Tick, settle + 1000, "the game kept going without them");

            // and comes back: handed the game as it is, on their old seats (cards and all)
            var c3 = NetSession.Join(net.Add(), make, 1u);
            c3.SetLocalPlayers(new[] { new KeyValuePair<string, int>("B", 0), new KeyValuePair<string, int>("C", 1) });
            var three = new[] { host, c1, c3 };
            System.Action tick3 = () => { now += dt; net.Advance(dt); foreach (var s in three) s.Update(now); };
            for (var k = 0; k < 1200 && c3.Current != NetSession.Stage.Playing; k++)
            {
                tick3();
                host.Step(new[] { Person(10, host.Match.LocalInputTick) });
                c1.Step(new[] { Person(11, c1.Match.LocalInputTick) });
            }
            Assert.AreEqual(NetSession.Stage.Playing, c3.Current, "back in the match");
            Assert.AreEqual(2, c3.PlayerOf(0), "on their old seat");
            Assert.IsFalse(host.Seats[2].Away);
            for (var k = 0; k < 10 * 120; k++)
            {
                tick3();
                host.Step(new[] { Person(10, host.Match.LocalInputTick) });
                c1.Step(new[] { Person(11, c1.Match.LocalInputTick) });
                c3.Step(new[] { Person(12, c3.Match.LocalInputTick), Person(13, c3.Match.LocalInputTick) });
            }
            Assert.Greater(c3.Match.Tick, 0);
            settle = host.Match.Tick;
            for (var k = 0; k < 3000; k++)
            {
                var done = true;
                foreach (var s in three) done &= s.Match.Confirmed >= settle;
                if (done) break;
                tick3();
                foreach (var s in three) s.Step(new[] { default(PlayerInput), default(PlayerInput) });
            }
            for (var k = 0; k <= Rollback.RewindEvery; k++) { tick3(); foreach (var s in three) s.Step(new[] { default(PlayerInput), default(PlayerInput) }); }
            h = host.Match.SavedAt(settle).Checksum();
            Assert.AreEqual(h, c1.Match.SavedAt(settle).Checksum(), "client 1 still matches");
            Assert.AreEqual(h, c3.Match.SavedAt(settle).Checksum(), "the one who came back matches too");
            Assert.IsFalse(host.Match.State.Players[2].Bot, "a person again");
            foreach (var s in three) Assert.AreEqual(-1, s.DesyncTick, s.Problem);
        }

        /// <summary>The host takes a bot out mid-match: every PC drops the same one and they stay identical.</summary>
        [Test, Timeout(300000)]
        public void TheHostTakesABotOutOnline()
        {
            var map = Rink();
            var cards = Cards();
            System.Func<Simulation> make = () => new Simulation(map, null, cards);
            var net = new FakeNetwork(77) { Latency = 0.04, Jitter = 0.02, Loss = 0.05 };
            var host = NetSession.Host(net.Add(), make, 8u);
            var client = NetSession.Join(net.Add(), make, 8u);
            host.SetLocalPlayers(new[] { new KeyValuePair<string, int>("Host", 0) });
            client.SetLocalPlayers(new[] { new KeyValuePair<string, int>("Guest", 1) });
            host.Bots = 2;
            const double dt = 1.0 / 120.0;
            var now = 0.0;
            System.Action tick = () => { now += dt; net.Advance(dt); host.Update(now); client.Update(now); };
            for (var k = 0; k < 600 && !(client.Current == NetSession.Stage.Lobby && client.Seats.Count == 4); k++) tick();
            host.StartMatch(99u);
            for (var k = 0; k < 600 && client.Current != NetSession.Stage.Playing; k++) tick();
            Assert.AreEqual(NetSession.Stage.Playing, client.Current);
            for (var k = 0; k < 240; k++) { tick(); host.Step(new[] { Person(1, host.Match.LocalInputTick) }); client.Step(new[] { Person(2, client.Match.LocalInputTick) }); }
            Assert.AreEqual(4, host.Match.State.Players.Length);
            // the host's command: take out player 3 (a bot)
            var sent = false;
            for (var k = 0; k < 600 && !sent; k++)
            {
                tick();
                var mine = Person(1, host.Match.LocalInputTick);
                mine.Pick = (byte)(PlayerInput.RemoveBot + 3);
                if (host.Step(new[] { mine })) { sent = true; host.BotLeftMatch(3); }
                client.Step(new[] { Person(2, client.Match.LocalInputTick) });
            }
            Assert.IsTrue(sent);
            for (var k = 0; k < 600; k++) { tick(); host.Step(new[] { Person(1, host.Match.LocalInputTick) }); client.Step(new[] { Person(2, client.Match.LocalInputTick) }); }
            var settle = host.Match.Tick;
            for (var k = 0; k < 2000 && (host.Match.Confirmed < settle || client.Match.Confirmed < settle); k++) { tick(); host.Step(new[] { default(PlayerInput) }); client.Step(new[] { default(PlayerInput) }); }
            for (var k = 0; k <= Rollback.RewindEvery; k++) { tick(); host.Step(new[] { default(PlayerInput) }); client.Step(new[] { default(PlayerInput) }); }
            Assert.AreEqual(3, host.Match.SavedAt(settle).Players.Length, "the bot is gone on the host");
            Assert.AreEqual(3, client.Match.SavedAt(settle).Players.Length, "and on the client");
            Assert.AreEqual(host.Match.SavedAt(settle).Checksum(), client.Match.SavedAt(settle).Checksum(), "still the same game");
            Assert.AreEqual(-1, host.DesyncTick, host.Problem);
            Assert.AreEqual(-1, client.DesyncTick, client.Problem);
            Assert.AreEqual(1, host.Bots, "the lobby has one bot left");
        }

        /// <summary>
        /// Mid-match: someone new joins a match that's on (once they ready up), someone changes team, and the host goes to
        /// watch. Every PC is re-seated the same way and they all stay identical.
        /// </summary>
        [Test, Timeout(300000)]
        public void JoiningChangingTeamAndWatchingMidMatch()
        {
            var map = Rink();
            var cards = Cards();
            System.Func<Simulation> make = () => new Simulation(map, null, cards);
            var net = new FakeNetwork(31) { Latency = 0.03, Jitter = 0.02, Loss = 0.05 };
            var host = NetSession.Host(net.Add(), make, 4u);
            var a = NetSession.Join(net.Add(), make, 4u);
            host.SetLocalPlayers(new[] { new KeyValuePair<string, int>("H", NetSession.Look(0, 0)) });
            a.SetLocalPlayers(new[] { new KeyValuePair<string, int>("A", NetSession.Look(1, 1)) });
            host.Bots = 1;
            const double dt = 1.0 / 120.0;
            var now = 0.0;
            var all = new List<NetSession> { host, a };
            System.Action tick = () =>
            {
                now += dt; net.Advance(dt);
                foreach (var x in all) x.Update(now);
                for (var i = 0; i < all.Count; i++)
                    if (all[i].Current == NetSession.Stage.Playing && all[i].Match != null)
                        all[i].Step(new[] { Person(20 + i, all[i].Match.LocalInputTick) });
            };
            for (var k = 0; k < 600 && a.Seats.Count != 3; k++) tick();
            host.StartMatch(11u);
            for (var k = 0; k < 600 && a.Current != NetSession.Stage.Playing; k++) tick();
            for (var k = 0; k < 3 * 120; k++) tick();
            Assert.AreEqual(3, host.Match.State.Players.Length);

            // B comes in while it's on: waits in the lobby until they ready up, then gets a seat on blue
            var b = NetSession.Join(net.Add(), make, 4u);
            all.Add(b);
            b.SetLocalPlayers(new[] { new KeyValuePair<string, int>("B", NetSession.Look(1, 2)) });
            for (var k = 0; k < 240; k++) tick();
            Assert.AreEqual(NetSession.Stage.Lobby, b.Current, "not in until ready");
            Assert.IsTrue(b.HostPlaying, "told a match is on");
            Assert.Greater(b.LobbyRules.Length, -1);
            b.SetLocalPlayers(new[] { new KeyValuePair<string, int>("B", NetSession.Look(1, 2, true)) });
            for (var k = 0; k < 1200 && b.Current != NetSession.Stage.Playing; k++) tick();
            Assert.AreEqual(NetSession.Stage.Playing, b.Current, "joined the match part-way");
            Assert.AreEqual(4, host.Match.State.Players.Length);
            Assert.AreEqual(1, host.Seats[b.PlayerOf(0)].Team, "on the side they picked");
            for (var k = 0; k < 3 * 120; k++) tick();

            // A changes to red
            a.SetLocalPlayers(new[] { new KeyValuePair<string, int>("A", NetSession.Look(0, 1)) });
            for (var k = 0; k < 3 * 120; k++) tick();
            Assert.AreEqual(0, host.Match.State.Players[host.Seats.FindIndex(x => x.Name == "A")].Team, "A is red now");

            // the host goes to watch
            host.SetLocalPlayers(new[] { new KeyValuePair<string, int>("H", NetSession.Look(0, 0, false, true)) });
            for (var k = 0; k < 3 * 120; k++) tick();
            Assert.AreEqual(-1, host.PlayerOf(0), "no seat while watching");
            Assert.AreEqual(3, host.Match.State.Players.Length);

            // everyone settles on the same game
            var settle = host.Match.Tick;
            for (var k = 0; k < 3000; k++)
            {
                var done = true;
                foreach (var x in all) done &= x.Match.Confirmed >= settle;
                if (done) break;
                tick();
            }
            for (var k = 0; k <= Rollback.RewindEvery; k++) tick();
            foreach (var x in all) Assert.AreEqual(host.Layout, x.Layout, "the same seating everywhere");
            var h = host.Match.SavedAt(settle).Checksum();
            Assert.AreEqual(h, a.Match.SavedAt(settle).Checksum(), "A matches the host");
            Assert.AreEqual(h, b.Match.SavedAt(settle).Checksum(), "B (who joined part-way) matches too");
            foreach (var x in all) Assert.AreEqual(-1, x.DesyncTick, x.Problem);
        }

        /// <summary>A client asks for their side's colour to change: the host recolours the whole side (bots too) and tells everyone.</summary>
        [Test]
        public void AnyoneCanRecolourTheirSide()
        {
            var map = Rink();
            var cards = Cards();
            System.Func<Simulation> make = () => new Simulation(map, null, cards);
            var net = new FakeNetwork(5) { Latency = 0.03, Loss = 0 };
            var host = NetSession.Host(net.Add(), make, 3u);
            var client = NetSession.Join(net.Add(), make, 3u);
            host.SetLocalPlayers(new[] { new KeyValuePair<string, int>("Host", NetSession.Look(0, 0)) });
            client.SetLocalPlayers(new[] { new KeyValuePair<string, int>("Guest", NetSession.Look(1, 1)) });
            host.Bots = 1;
            host.SetBot(0, 1, 2); // a bot on the guest's (blue) side
            const double dt = 1.0 / 120.0;
            var now = 0.0;
            System.Action tick = () => { now += dt; net.Advance(dt); host.Update(now); client.Update(now); };
            for (var k = 0; k < 600 && client.Seats.Count != 3; k++) tick();
            Assert.AreEqual(3, client.Seats.Count);
            client.RequestRecolour(1, 3); // blue to green
            for (var k = 0; k < 240; k++) tick();
            Assert.AreEqual(3, host.TeamColours[1], "the host recoloured that side");
            Assert.AreEqual(0, host.TeamColours[0], "not the other one");
            foreach (var s in host.Seats) Assert.AreEqual(s.Team == 1 ? 3 : 0, s.Colour, s.Name + " in their side's colour (bots too)");
            Assert.AreEqual(1, client.Recolours, "the client was told");
            Assert.AreEqual(3, client.TeamColours[1]);
            client.RequestRecolour(3, 0); // not to the other side's colour
            for (var k = 0; k < 120; k++) tick();
            Assert.AreEqual(3, host.TeamColours[1], "refused");
        }

        /// <summary>The whole game state survives being turned into bytes and back (for rejoining).</summary>
        [Test]
        public void StateCodecRoundTrips()
        {
            var sim = new Simulation(Rink(), null, Cards());
            var s = sim.NewGame(new[] { 0, 1, 0 }, 9u, new[] { false, true, true });
            for (var t = 0; t < 900; t++) sim.Step(s, sim.Route(s, new[] { Person(1, t), default(PlayerInput), default(PlayerInput) }));
            s.Players[0].Cards = new[] { 1, 2, 2 };
            var copy = StateCodec.Read(StateCodec.Write(s));
            Assert.AreEqual(s.Checksum(), copy.Checksum());
            Assert.AreEqual(3, copy.Players[0].Cards.Length);
        }

        /// <summary>After a card pick, the countdown waits for the cards to leave before 3, 2, 1.</summary>
        [Test]
        public void CountdownWaitsForTheCardsToLeave()
        {
            // (no cards that hand out more cards: Double Card shows its two before the countdown)
            var lib = Cards();
            var off = new System.Collections.Generic.List<int>();
            foreach (var id in new[] { "ultrapuck.doublecard", "ultrapuck.choose", "ultrapuck.newhand" }) off.Add(lib.ById(id).Index);
            var sim = new Simulation(Rink(), new Tuning { CardsOff = off.ToArray() }, lib);
            var s = sim.NewGame(new[] { 0, 1 }, 3u);
            s.Phase = Phase.Play;
            sim.StartPicking(s, new[] { 0, 1 });
            Assert.AreEqual(Phase.Picking, s.Phase);
            sim.Step(s, new[] { new PlayerInput { Pick = 1 }, new PlayerInput { Pick = 1 } });
            Assert.AreEqual(Phase.Countdown, s.Phase);
            Assert.AreEqual((sim.T.CountdownTime + sim.T.PickOutroTime).ToDouble(), s.PhaseTime.ToDouble(), 0.02);
        }

        [Test]
        public void DifferentFilesCantJoin()
        {
            var map = Rink();
            System.Func<Simulation> make = () => new Simulation(map);
            var net = new FakeNetwork(3) { Loss = 0 };
            var host = NetSession.Host(net.Add(), make, 1u);
            var client = NetSession.Join(net.Add(), make, 2u);
            var now = 0.0;
            for (var k = 0; k < 240 && client.Current == NetSession.Stage.Connecting; k++) { now += 1.0 / 120; net.Advance(1.0 / 120); host.Update(now); client.Update(now); }
            Assert.AreEqual(NetSession.Stage.Closed, client.Current);
            StringAssert.Contains("different", client.Problem);
        }

        /// <summary>The real UDP transport carries a big packet (a hand-over of the whole game is several KB).</summary>
        [Test, Timeout(30000)]
        public void RealUdpCarriesBigPackets()
        {
            using (var host = UdpTransport.Host(47992))
            using (var client = UdpTransport.Join("127.0.0.1:47992"))
            {
                client.Send(0, new byte[] { 1 });
                int conn = -1; byte[] got = null;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (clock.Elapsed.TotalSeconds < 5 && !host.Poll(out conn, out got)) System.Threading.Thread.Sleep(2);
                Assert.IsNotNull(got, "the host heard the client");
                var big = new byte[30000];
                for (var k = 0; k < big.Length; k++) big[k] = (byte)(k * 7);
                host.Send(conn, big);
                got = null;
                int from;
                while (clock.Elapsed.TotalSeconds < 10 && !client.Poll(out from, out got)) System.Threading.Thread.Sleep(2);
                Assert.IsNotNull(got, "the big packet got through");
                CollectionAssert.AreEqual(big, got);
            }
        }

        /// <summary>The real UDP transport on this PC: a host and a client find each other and start a match.</summary>
        [Test, Timeout(60000)]
        public void RealUdpOnThisPc()
        {
            var map = Rink();
            System.Func<Simulation> make = () => new Simulation(map);
            using (var host = NetSession.Host(UdpTransport.Host(47991), make, 9u))
            using (var client = NetSession.Join(UdpTransport.Join("127.0.0.1:47991"), make, 9u))
            {
                host.SetLocalPlayers(new[] { new KeyValuePair<string, int>("H", 0) });
                client.SetLocalPlayers(new[] { new KeyValuePair<string, int>("C", 1) });
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (clock.Elapsed.TotalSeconds < 5 && host.Seats.Count < 2) { host.Update(clock.Elapsed.TotalSeconds); client.Update(clock.Elapsed.TotalSeconds); System.Threading.Thread.Sleep(5); }
                Assert.AreEqual(2, host.Seats.Count, "the client joined over UDP");
                host.StartMatch(3u);
                while (clock.Elapsed.TotalSeconds < 10 && client.Current != NetSession.Stage.Playing) { host.Update(clock.Elapsed.TotalSeconds); client.Update(clock.Elapsed.TotalSeconds); System.Threading.Thread.Sleep(5); }
                Assert.AreEqual(NetSession.Stage.Playing, client.Current);
                for (var k = 0; k < 240; k++)
                {
                    host.Update(clock.Elapsed.TotalSeconds); client.Update(clock.Elapsed.TotalSeconds);
                    host.Step(new[] { Person(1, k) }); client.Step(new[] { Person(2, k) });
                    System.Threading.Thread.Sleep(2);
                }
                Assert.Greater(client.Match.Confirmed, 50, "controls are flowing both ways");
                Assert.Greater(host.Match.Confirmed, 50);
            }
        }
    

        /// <summary>Card stats tables merge by the bigger count, survive the trip as bytes, and never overwrite this PC's own row.</summary>
        [Test]
        public void CardStatsTablesMergeByTheBiggerCount()
        {
            var a = new UltraPuck.Game.CardStatsTable();
            var me = new UltraPuck.Game.CardStatsTable.Person { Name = "Me" };
            me.Picks["ultrapuck.swap"] = 2;
            a.People["me"] = me;
            var friend = new UltraPuck.Game.CardStatsTable.Person { Name = "Friend" };
            friend.Picks["ultrapuck.swap"] = 1;
            a.People["friend"] = friend;

            var b = new UltraPuck.Game.CardStatsTable();
            var friend2 = new UltraPuck.Game.CardStatsTable.Person { Name = "Friend" };
            friend2.Picks["ultrapuck.swap"] = 3;
            friend2.Picks["ultrapuck.portalgun"] = 1;
            b.People["friend"] = friend2;
            var fakeMe = new UltraPuck.Game.CardStatsTable.Person { Name = "Me?" };
            fakeMe.Picks["ultrapuck.swap"] = 99;
            b.People["me"] = fakeMe;
            var stranger = new UltraPuck.Game.CardStatsTable.Person { Name = "New" };
            stranger.Picks["ultrapuck.portalgun"] = 4;
            b.People["new"] = stranger;

            var b2 = UltraPuck.Game.CardStatsTable.FromBytes(b.ToBytes());
            Assert.IsTrue(a.Merge(b2, new[] { "me" }));
            Assert.AreEqual(2, a.People["me"].Picks["ultrapuck.swap"], "this PC's own row isn't taken from others");
            Assert.AreEqual(3, a.People["friend"].Picks["ultrapuck.swap"], "the bigger count");
            Assert.AreEqual(1, a.People["friend"].Picks["ultrapuck.portalgun"]);
            Assert.AreEqual(2 + 3, a.Total("ultrapuck.swap"));
            Assert.AreEqual(1 + 4, a.Total("ultrapuck.portalgun"), "someone met through someone else counts too");
            Assert.IsFalse(a.Merge(b2, new[] { "me" }), "the same again changes nothing");
        }
    }
}
