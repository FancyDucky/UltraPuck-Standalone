using System;
using System.Collections.Generic;

namespace UltraPuck.Core.Net
{
    /// <summary>
    /// An online game: the lobby, starting matches, and swapping controls with the other PCs while
    /// <see cref="Rollback"/> keeps everyone's game identical.
    ///
    /// One PC hosts; the others join it, and everything goes through the host (it passes each PC's controls on to
    /// the others). Every PC can have several people on it (couch + online). Controls are re-sent until the other
    /// side confirms them, so lost packets don't matter. Every second the PCs compare a fingerprint of the game, so
    /// a desync (a bug) is caught at once instead of turning into two different matches.
    ///
    /// Plain C# with no Unity: the game drives it with <see cref="Update"/> and <see cref="Step"/>, and tests can run
    /// several of these over a pretend network.
    /// </summary>
    public sealed class NetSession : IDisposable
    {
        public const int Version = 63;// 2: knock-loose checks, bigger puck, Puck Gun on brake. 3: carried puck physics, card hover. 4: thick walls, faster shots. 5: bots go round nets. 6: hit lag, timed matches, team colours and symbols. 7: no daze on checks, pushable pylons, smaller nets. 8: 8 players, goals on touch, charge from the pickup, hit strength by weight. 9: Shockwave slows 45%. 10: Ice Trail. 11: ice melts from 1 s, slower. 12: ice melts slower still. 13: replay skip votes, ready up, bots with their own team and symbol. 14: S and U rinks, bots follow routes, smaller Test Rink nets. 15: faster wall checks, rewinds gathered, 0.4 s prediction. 16: ice lasts 3 s and melts at half pace, pass boost, wedge pickups. 17: 3 s countdown, deeper nets, carrying into your own net. 18: hold to keep dashing, wider S and U rinks. 19: quicker acceleration. 20: the host pauses everyone. 21: shots counted (for sounds). 22: every card as likely as any other, nets 30% bigger, S Rink nets on the ice, upright U Rink, pylons by the way a net faces, bots pass. 23: no check after a quick shot until Shoot is let go, carried puck may sit in the carrier at a wall. 24: Over Charge shot speed scales past a full charge (1.5×/2×/…); puck speed cap raised so it can. 25: Shockwave is a utility (Triangle / Y), not on check. 26: Shockwave 3 s / range 15 radii / 6 s cooldown. 27: Shockwave 50% / 2 s / range 5 / 6 s cooldown. 28: Shockwave pushes everyone (no slow), thin expanding ring. 29: Shockwave push 2×. 30: one utility at a time (replace), MultiUtility setting, Swap rock card. 31: Swap clap (teleport). 32: clap only on teleport (not throw); rock lighter and glidey. 33: bot Swap CD 3 s, swap keeps momentum, rocks pickable/shootable. 34: rock↔puck collide (gun-style knock when carried). 35: team vote counts humans only (bots follow). 36: bot-only team still picks by tier. 37: goal blast shove + LastGoalPlayer; Shockwave shoves puck and rocks; replay after 3 s. 38: no Swap utility while holding puck; dash CD +10% + longer coast. 39: goal box front at the net opening. 40: team-vote bots hover-follow without locking until a human locks. 41: Friendly Fire picks up and throws teammates, Shockwave knocks the puck loose, one Pylon Goalie that grows a size a card and stays out of the nets; teammates picked up on the wedge; pre-charge without the puck; dash where you move (input bit 32); Team Play refreshes the team, Team Play II gone. 42: Pylon Goalie grows 55% a card. 43: swapping onto bots is part of the game (input bits 64/128, Pilot), mid-match bots (Pick 201/202), match stats, Puck Gun knockback (no stun, 1 s), 30% slower while charging, puck drag 7/11, bot pass/positioning, Team Vote people draw first, lobby/start messages ordered. 44: countdown waits 1.5 s after picks, leave/rejoin mid-match (Away / Rejoin messages, state hand-over), host kick-off and score commands (Pick 203-207). 45: bot-run players wait between Swap uses, bots throw a carried teammate at the puck. 46: Power Shot = full charge quick shot (QuickShotFull), Invisible Quick Shot (PuckHidden), Cannon Ball (CannonTeam). 47: Cannon Ball shoves the catcher along the shot (Shoved), no hit lag. 48: Swap rock gone once stopped / no swap to a held rock, Ice Trail on every shot (teammates +, 3x longer), Team Play = pass speed boost (Fast), Cannon Ball shove into your own net scores (ShovedBy), Stone Wall (Stones). 49: Same Draw picking (rules bit 16), Stone Wall up to 30 long / 3 s. 50: Swap rock glides 35% less (RockDrag 0.277), Ice Trail 15 s, teammates +60% (IceBoost), RockDrag 0.326, swapping gives you the rock's momentum and the rock keeps its own. 51: Friendly Fire grabs only while holding charge/brake, throws like a shot (MateTossDrag, TossEndSpeed), +15% speed 4 s after. 52: Invisible Shot 0.5 s. 53: host takes a bot out mid-match (Pick 208 + player). 54: Stone Wall stands 6 s. 55: no charging slow-down while carrying a teammate. 56: Shockwave held to grow (ShockHold, 1.5-6 units over 6 s, no cooldown), Stone Wall goes on after bounces and stands 5.5 s, Heavyweight size x1.16. 57: Shockwave grows from 0. 58: Swap rock lasts 10 s (Rock.Life), passes only end when an opponent takes the puck. 59: side colours in the lobby message, Recolour message. 60: bots pass more / quick shots / go round nets; Hockey and Big Rink nets say which way they open. 61: cards go with the person on a swap. 62: Puck Gun on the utility button, 0.85 s cooldown, +3 s for bots. 63: kick-off spot rotates, tight face-off groups in front of the nets, Portal Gun, Team Play +50%
        public const int MaxPlayers = 8;
        /// <summary>Your own controls take effect this many ticks later (25 ms at 120 a second), which hides a little lag for free.</summary>
        public const int InputDelay = 3;
        private const double HelloEvery = 0.5, LobbyEvery = 0.5, PingEvery = 0.5, Timeout = 8, ConnectTimeout = 10;
        private const int ChecksumEvery = 120, MaxSendTicks = 60;
        /// <summary>
        /// Controls go out this many times a second, whatever the frame rate (they only change 120 times a second).
        /// Sending every frame swamped the connections at high frame rates with many players.
        /// </summary>
        public const int SendRate = 120;
        /// <summary>
        /// Each message carries a player's newest controls back this many ticks, in case some messages get lost.
        /// Older ones still not confirmed (a long gap) are sent again now and then (<see cref="RepairEvery"/>).
        /// </summary>
        public const int ResendTicks = 16;
        private const double RepairEvery = 0.2;

        public enum Stage { Connecting, Lobby, Playing, Closed }

        public sealed class Seat
        {
            public int Peer;     // which PC (0 = host)
            public int Local;    // which player on that PC
            public int Team;
            /// <summary>The team colour this player picked (0..7, the game's colour list) and their symbol (0..7).</summary>
            public int Colour, Symbol;
            public bool Bot;
            /// <summary>Left mid-match: the bot plays them until they come back (their seat and cards wait for them).</summary>
            public bool Away;
            /// <summary>The player has readied up in the lobby (bots always have).</summary>
            public bool Ready;
            public string Name = "";
        }

        /// <summary>
        /// A player's look, as sent around: colour in the low 3 bits, symbol in the next 3. Players who picked the same
        /// colour are a team; the first colour (in seat order) is team 0, the next team 1.
        /// </summary>
        public static int Look(int colour, int symbol, bool ready = false) { return (colour & 7) | ((symbol & 7) << 3) | (ready ? 64 : 0); }

        /// <summary>Everyone (not counting bots) has readied up.</summary>
        public bool AllReady { get { foreach (var s in Seats) if (!s.Bot && !s.Ready) return false; return Seats.Count > 0; } }

        /// <summary>More than two colours picked: there can only be two teams.</summary>
        public bool TooManyTeams { get; private set; }

        public readonly bool IsHost;
        public Stage Current { get; private set; }
        /// <summary>Why the session closed (or a warning, like a desync), for the screen.</summary>
        public string Problem { get; private set; }
        /// <summary>Short news for a pop-up ("Sam left the game."), set as it happens; the game takes it and clears it.</summary>
        public string Notice;
        public int MyPeer { get; private set; }

        /// <summary>
        /// The colours of the two sides (indexes into the game's colour list), from the seats. <see cref="Recolours"/>
        /// goes up whenever a side is given a new colour: each PC then puts its own players in their side's colour.
        /// </summary>
        public readonly int[] TeamColours = { 0, 1 };
        public int Recolours { get; private set; }
        /// <summary>Host: every recolour so far (from, to), so a PC that sends its players before it's heard of one is put right.</summary>
        private readonly List<KeyValuePair<int, int>> _remaps = new List<KeyValuePair<int, int>>();

        /// <summary>
        /// Give the side that's <paramref name="from"/> the colour <paramref name="to"/>: everyone on it, people (on any
        /// PC) and bots. Anyone may ask; the host does it and tells everyone.
        /// </summary>
        public void RequestRecolour(int from, int to)
        {
            if (Current != Stage.Lobby) return;
            if (IsHost) RecolourSide(from, to);
            else Send(0, new Writer(Msg.Recolour).U8(from).U8(to).ToArray());
        }

        private void RecolourSide(int from, int to)
        {
            from &= 7; to &= 7;
            if (from == to || (TeamColours[0] != from && TeamColours[1] != from)) return;
            var other = TeamColours[0] == from ? TeamColours[1] : TeamColours[0];
            if (to == other) return; // that's the other side's colour
            for (var k = 0; k < _localPlayers.Count; k++)
                if ((_localPlayers[k].Value & 7) == from) _localPlayers[k] = new KeyValuePair<string, int>(_localPlayers[k].Key, (_localPlayers[k].Value & ~7) | to);
            foreach (var l in _links.Values)
                for (var k = 0; k < l.Players.Count; k++)
                    if ((l.Players[k].Value & 7) == from) l.Players[k] = new KeyValuePair<string, int>(l.Players[k].Key, (l.Players[k].Value & ~7) | to);
            for (var b = 0; b < _bots.Count; b++) if ((_bots[b] & 7) == from) _bots[b] = (_bots[b] & ~7) | to;
            _remaps.Add(new KeyValuePair<int, int>(from, to));
            Recolours++;
            RebuildSeats();
        }
        /// <summary>Who's in the game, in player order. Kept by the host; clients get copies.</summary>
        public readonly List<Seat> Seats = new List<Seat>();
        public Rollback Match { get; private set; }
        public int MatchId { get; private set; }
        public uint Seed { get; private set; }
        /// <summary>The tick of the first desync found, or -1.</summary>
        public int DesyncTick { get; private set; }

        private readonly ITransport _t;
        private readonly Func<Simulation> _makeSim;
        private readonly uint _fingerprint;
        private double _now, _lastHello, _lastLobby, _started, _lastStartToAll, _nextSend;

        /// <summary>Everything this PC has sent: messages and bytes (for the network line in the log).</summary>
        public long MessagesSent { get; private set; }
        public long BytesSent { get; private set; }

        /// <summary>
        /// Card pick counts, swapped in the lobby every few seconds: the game gives this PC's table to send, and takes in
        /// what arrives. Clients send to the host; the host sends its (merged) table to everyone.
        /// </summary>
        public Func<byte[]> CardStatsOut;
        public Action<byte[]> CardStatsIn;
        private double _lastStats = -10.0;
        private const double StatsEvery = 3.0;

        private void SendCardStats()
        {
            var payload = CardStatsOut != null ? CardStatsOut() : null;
            if (payload == null || payload.Length == 0) return;
            var bytes = new Writer(Msg.CardStats).BigBytes(payload).ToArray();
            if (IsHost) { foreach (var l in _links.Values) if (l.Peer > 0) Send(l.Conn, bytes); }
            else Send(0, bytes);
        }

        private void Send(int conn, byte[] data)
        {
            MessagesSent++;
            BytesSent += data.Length;
            _t.Send(conn, data);
        }
        /// <summary>Client: the newest lobby message seen (older ones arriving late are ignored).</summary>
        private int _theirLobby = int.MinValue;
        private int _nextPeer = 1, _lobbyVersion, _seenLobbyVersion = -1;
        /// <summary>Host: someone has come back this match (keep telling everyone when they're back from).</summary>
        private bool _anyBack;
        /// <summary>Host: the bots' looks (colour and symbol, see <see cref="Look"/>), in order.</summary>
        private readonly List<int> _bots = new List<int>();

        private sealed class Link
        {
            public int Conn, Peer = -1;
            public double LastHeard, LastPing;
            public float Rtt = 0.1f;
            public int RemoteTick;
            public bool StartAcked;
            /// <summary>Host: this PC joined while a match was on (it gets the game handed over, not a Start).</summary>
            public bool MidMatch;
            /// <summary>Host: the seats this PC took back mid-match, and when the last hand-over was sent.</summary>
            public readonly List<int> Rejoined = new List<int>();
            public double RejoinSent = -10;
            public readonly int[] Acked = new int[8];
            public double LastRepair;
            public readonly List<KeyValuePair<string, int>> Players = new List<KeyValuePair<string, int>>();
            public Link() { for (var k = 0; k < Acked.Length; k++) Acked[k] = -1; }
        }

        private readonly Dictionary<int, Link> _links = new Dictionary<int, Link>();
        private readonly List<KeyValuePair<string, int>> _localPlayers = new List<KeyValuePair<string, int>>();
        private readonly Dictionary<int, ulong> _myChecks = new Dictionary<int, ulong>(), _theirChecks = new Dictionary<int, ulong>();
        private int _lastChecked;

        /// <param name="makeSim">Makes the rules for a match (map, tuning, cards). Every PC must make the same.</param>
        /// <param name="fingerprint">A hash of the map and card files: PCs with different files can't play together.</param>
        private NetSession(bool host, ITransport transport, Func<Simulation> makeSim, uint fingerprint)
        {
            IsHost = host;
            _t = transport;
            _makeSim = makeSim;
            _fingerprint = fingerprint;
            DesyncTick = -1;
            if (host) { MyPeer = 0; Current = Stage.Lobby; }
            else { MyPeer = -1; Current = Stage.Connecting; _links[0] = new Link { Conn = 0, Peer = 0 }; }
        }

        public static NetSession Host(ITransport t, Func<Simulation> makeSim, uint fingerprint) { return new NetSession(true, t, makeSim, fingerprint); }
        public static NetSession Join(ITransport t, Func<Simulation> makeSim, uint fingerprint) { return new NetSession(false, t, makeSim, fingerprint); }

        public string Describe() { return _t.Describe(); }

        /// <summary>Round-trip time to the host (clients) or the slowest client (host), in seconds.</summary>
        public float Ping
        {
            get { var r = 0f; foreach (var l in _links.Values) if (l.Peer >= 0) r = Math.Max(r, l.Rtt); return r; }
        }

        /// <summary>How many other PCs are connected.</summary>
        public int Connected { get { var n = 0; foreach (var l in _links.Values) if (l.Peer >= 0 && (IsHost || MyPeer >= 0)) n++; return n; } }

        // ------------------------------------------------------------------ lobby

        /// <summary>The people playing on this PC right now (name and team each). Call whenever it changes, or every frame.</summary>
        public void SetLocalPlayers(IList<KeyValuePair<string, int>> players)
        {
            var same = players.Count == _localPlayers.Count;
            for (var k = 0; same && k < players.Count; k++) same = players[k].Key == _localPlayers[k].Key && players[k].Value == _localPlayers[k].Value;
            if (same) return;
            _localPlayers.Clear();
            _localPlayers.AddRange(players);
            if (IsHost) RebuildSeats();
            else SendPlayers();
        }

        /// <summary>Host: how many bots to add to the game.</summary>
        /// <summary>Which rink (an index into everyone's map list, which the fingerprint check keeps the same). The host picks it.</summary>
        public int Map
        {
            get { return _map; }
            set { if (!IsHost || value == _map) return; _map = Math.Max(0, Math.Min(255, value)); _lobbyVersion++; }
        }
        private int _map;

        /// <summary>The host's match rules (rounds, picking, which cards...), packed by the game; sent with every match start.</summary>
        public byte[] Rules = new byte[0];

        public int Bots
        {
            get { return _bots.Count; }
            set
            {
                if (!IsHost) return;
                value = Math.Max(0, Math.Min(MaxPlayers, value));
                while (_bots.Count > value) _bots.RemoveAt(_bots.Count - 1);
                while (_bots.Count < value) _bots.Add(NewBotLook());
                RebuildSeats();
            }
        }

        /// <summary>Host: a bot's look (index among the bots).</summary>
        public int BotLook(int i) { return i >= 0 && i < _bots.Count ? _bots[i] : 0; }

        /// <summary>Host: change a bot's colour (so its team) and symbol.</summary>
        public void SetBot(int i, int colour, int symbol)
        {
            if (!IsHost || i < 0 || i >= _bots.Count) return;
            _bots[i] = Look(colour, symbol);
            RebuildSeats();
        }

        /// <summary>
        /// Host: a bot joined the match on <paramref name="team"/> (the game adds it from the host's controls); it
        /// stays for the next match. The seats aren't rebuilt until then (they're fixed while a match is on).
        /// </summary>
        public void BotJoinedMatch(int team)
        {
            if (!IsHost) return;
            var colour = team & 1;
            foreach (var s in Seats) if (s.Team == (team & 1)) { colour = s.Colour; break; }
            _bots.Add(Look(colour, (NewBotLook() >> 3) & 7));
        }

        /// <summary>
        /// Host: the bot at match player <paramref name="player"/> was taken out mid-match. Its lobby bot goes too (the
        /// people come first, then the bots in order), and its seat if it had one, so the seats still line up.
        /// </summary>
        public void BotLeftMatch(int player)
        {
            if (!IsHost) return;
            var people = 0;
            foreach (var s in Seats) if (!s.Bot) people++;
            var b = player - people;
            if (b >= 0 && b < _bots.Count) _bots.RemoveAt(b);
            if (player >= 0 && player < Seats.Count && Seats[player].Bot) Seats.RemoveAt(player);
        }

        /// <summary>Host: take a bot out.</summary>
        public void RemoveBot(int i)
        {
            if (!IsHost || i < 0 || i >= _bots.Count) return;
            _bots.RemoveAt(i);
            RebuildSeats();
        }

        /// <summary>A new bot goes on the smaller team, with a symbol nobody has yet.</summary>
        private int NewBotLook()
        {
            int red = 0, blue = 0;
            var used = new List<int>();
            foreach (var s in Seats) { if (s.Bot) continue; if (s.Colour == 1) blue++; else red++; used.Add(s.Symbol); }
            foreach (var b in _bots) { if ((b & 7) == 1) blue++; else red++; used.Add((b >> 3) & 7); }
            var symbol = 0;
            while (symbol < 7 && used.Contains(symbol)) symbol++;
            return Look(red <= blue ? 0 : 1, symbol);
        }

        private void RebuildSeats()
        {
            Seats.Clear();
            for (var k = 0; k < _localPlayers.Count; k++) Seats.Add(new Seat { Peer = 0, Local = k, Name = _localPlayers[k].Key, Colour = _localPlayers[k].Value & 7, Symbol = (_localPlayers[k].Value >> 3) & 7, Ready = (_localPlayers[k].Value & 64) != 0 });
            var peers = new List<Link>();
            foreach (var l in _links.Values) if (l.Peer > 0) peers.Add(l);
            peers.Sort((a, b) => a.Peer.CompareTo(b.Peer));
            foreach (var l in peers)
                for (var k = 0; k < l.Players.Count; k++) Seats.Add(new Seat { Peer = l.Peer, Local = k, Name = l.Players[k].Key, Colour = l.Players[k].Value & 7, Symbol = (l.Players[k].Value >> 3) & 7, Ready = (l.Players[k].Value & 64) != 0 });
            if (Seats.Count > MaxPlayers) Seats.RemoveRange(MaxPlayers, Seats.Count - MaxPlayers);
            // the bots, each with the colour (team) and symbol the host gave it
            for (var b = 0; b < _bots.Count && Seats.Count < MaxPlayers; b++)
                Seats.Add(new Seat { Peer = 0, Local = -1, Bot = true, Ready = true, Colour = _bots[b] & 7, Symbol = (_bots[b] >> 3) & 7, Name = "Bot" });
            // teams from colours: the first colour picked is team 0, the second team 1 (a third can't play)
            var colours = new List<int>();
            foreach (var s in Seats) if (!colours.Contains(s.Colour)) colours.Add(s.Colour);
            TooManyTeams = colours.Count > 2;
            if (colours.Count == 0) colours.Add(0);
            if (colours.Count == 1) colours.Add(colours[0] == 1 ? 0 : 1); // nobody on the other side yet: red, or blue
            foreach (var s in Seats) { var i = colours.IndexOf(s.Colour); s.Team = i < 0 ? 0 : i & 1; }
            TeamColours[0] = colours[0]; TeamColours[1] = colours[1];
            _lobbyVersion++;
            _lastLobby = -1;
        }

        /// <summary>Host: start (or restart) a match with everyone in the lobby.</summary>
        public void StartMatch(uint seed)
        {
            if (!IsHost || Seats.Count == 0) return;
            foreach (var l in _links.Values) { l.MidMatch = false; l.Rejoined.Clear(); }
            _anyBack = false;
            RebuildSeats(); // (bots added during the last match get their seats; anyone away is dropped)
            MatchId++;
            Seed = seed == 0 ? 1u : seed;
            foreach (var l in _links.Values) { l.StartAcked = false; for (var k = 0; k < l.Acked.Length; k++) l.Acked[k] = -1; }
            Begin();
            _lobbyVersion++;
            SendStartToAll();
        }

        /// <summary>Host: everyone back to the lobby.</summary>
        public void BackToLobby()
        {
            if (!IsHost) return;
            Match = null;
            Current = Stage.Lobby;
            foreach (var l in _links.Values) { l.MidMatch = false; l.Rejoined.Clear(); }
            _anyBack = false;
            RebuildSeats();
            _lobbyVersion++;
            _lastLobby = -1;
        }

        private void Begin()
        {
            var sim = _makeSim();
            var teams = new int[Seats.Count];
            var local = new bool[Seats.Count];
            var bots = new bool[Seats.Count];
            for (var k = 0; k < Seats.Count; k++) { teams[k] = Seats[k].Team; bots[k] = Seats[k].Bot; local[k] = !Seats[k].Bot && Seats[k].Peer == MyPeer; }
            Match = new Rollback(sim, sim.NewGame(teams, Seed, bots), local, bots, InputDelay);
            _myChecks.Clear(); _theirChecks.Clear(); _lastChecked = 0;
            DesyncTick = -1;
            Current = Stage.Playing;
            _started = _now;
        }

        /// <summary>The match player index of this PC's <paramref name="local"/>-th player, or -1.</summary>
        public int PlayerOf(int local)
        {
            for (var k = 0; k < Seats.Count; k++) if (!Seats[k].Bot && Seats[k].Peer == MyPeer && Seats[k].Local == local) return k;
            return -1;
        }

        // ------------------------------------------------------------------ the match

        /// <summary>
        /// Simulate one tick with this PC's controls (<paramref name="localInputs"/>[k] for its k-th player). Returns
        /// false (and does nothing) when we're too far ahead of the others' confirmed controls and must wait.
        /// </summary>
        public bool Step(IList<PlayerInput> localInputs)
        {
            if (Current != Stage.Playing || Match == null || !Match.CanAdvance) return false;
            var at = Match.LocalInputTick;
            for (var k = 0; k < Seats.Count; k++)
            {
                var s = Seats[k];
                if (s.Bot || s.Peer != MyPeer) continue;
                Match.SetInput(k, at, s.Local < localInputs.Count ? localInputs[s.Local] : default(PlayerInput));
            }
            Match.Advance();
            return true;
        }

        /// <summary>
        /// Ticks we're ahead of the PC furthest behind (allowing for the lag between us). The game slows down a
        /// touch while this is above 1, so nobody runs ahead and makes the others rewind more.
        /// </summary>
        public float Advantage
        {
            get
            {
                if (Match == null) return 0f;
                var worst = 0f;
                foreach (var l in _links.Values)
                {
                    if (l.Peer < 0) continue;
                    var theirs = l.RemoteTick + l.Rtt / 2f * Match.Sim.T.TicksPerSecond;
                    worst = Math.Max(worst, Match.Tick - theirs);
                }
                return worst;
            }
        }

        // ------------------------------------------------------------------ network

        /// <summary>Send and receive. Call every frame with the time in seconds.</summary>
        public void Update(double now)
        {
            _now = now;
            if (Current == Stage.Closed) return;
            int conn; byte[] data;
            while (_t.Poll(out conn, out data))
            {
                try { Receive(conn, data); }
                catch (FormatException) { } // a damaged or foreign packet: ignore it
                catch (IndexOutOfRangeException) { }
            }
            if (Current == Stage.Closed) return;

            if (!IsHost && Current == Stage.Connecting)
            {
                if (_now - _lastHello >= HelloEvery)
                {
                    _lastHello = _now;
                    Send(0, new Writer(Msg.Hello).I32(Version).U32(_fingerprint).ToArray());
                }
                if (_started == 0) _started = _now;
                if (_now - _started > ConnectTimeout) Close("No answer from the host. Check the address, and that the host's firewall lets the game through.");
                return;
            }

            // timeouts and pings
            var gone = new List<Link>();
            foreach (var l in _links.Values)
            {
                if (l.Peer < 0) continue;
                if (l.LastHeard == 0) l.LastHeard = _now;
                if (_now - l.LastHeard > Timeout) { gone.Add(l); continue; }
                if (_now - l.LastPing >= PingEvery) { l.LastPing = _now; Send(l.Conn, new Writer(Msg.Ping).F32((float)_now).ToArray()); }
            }
            foreach (var l in gone) Lost(l, "lost connection");

            if (IsHost && Current != Stage.Closed)
            {
                if (_now - _lastLobby >= LobbyEvery || _seenLobbyVersion != _lobbyVersion) { _lastLobby = _now; _seenLobbyVersion = _lobbyVersion; SendLobby(); }
                if (Current == Stage.Playing && _now - _lastHello >= 0.25)
                {
                    _lastHello = _now;
                    SendStartToAll(_now - _lastStartToAll >= 1.0);
                    if (AnyAway() || _anyBack) SendAway();
                    foreach (var l in _links.Values) if (l.MidMatch && l.Rejoined.Count > 0 && !l.StartAcked && _now - l.RejoinSent >= 0.5) SendRejoin(l);
                }
            }
            if (!IsHost && Current == Stage.Lobby && _now - _lastHello >= 1.0) { _lastHello = _now; SendPlayers(); }
            if (Current == Stage.Lobby && CardStatsOut != null && _now - _lastStats >= StatsEvery) { _lastStats = _now; SendCardStats(); }

            if (Current == Stage.Playing && Match != null)
            {
                // a fixed rate, not every frame (catching up after a slow frame, but never more than one at once)
                if (_now >= _nextSend - 0.25 / SendRate)
                {
                    _nextSend = Math.Max(_nextSend + 1.0 / SendRate, _now - 1.0 / SendRate);
                    foreach (var l in _links.Values) if (l.Peer >= 0) SendInputs(l);
                }
                Checksums();
            }
        }

        private void Receive(int conn, byte[] data)
        {
            var r = new Reader(data);
            Link l;
            _links.TryGetValue(conn, out l);

            if (IsHost && r.Type == Msg.Hello)
            {
                var version = r.I32();
                var fp = r.U32();
                if (l == null)
                {
                    string why = null;
                    if (version != Version) why = "The host is running a different version of the game.";
                    else if (fp != _fingerprint) why = "The host has different maps or cards. Make sure you both have the same game files and mods.";
                    else if (Current == Stage.Playing && !AnyAway()) why = "That game has already started. Ask the host to go back to the lobby.";
                    else if (Seats.Count >= MaxPlayers) why = "That game is full.";
                    if (why != null) { Send(conn, new Writer(Msg.Refuse).Str(why).ToArray()); _t.Drop(conn); return; }
                    l = new Link { Conn = conn, Peer = _nextPeer++, LastHeard = _now, MidMatch = Current == Stage.Playing };
                    _links[conn] = l;
                    if (!l.MidMatch) RebuildSeats(); // (mid-match the seats stay put: they take back an empty one)
                }
                Send(conn, new Writer(Msg.Welcome).U8(l.Peer).ToArray());
                return;
            }
            if (l == null) return;
            l.LastHeard = _now;

            switch (r.Type)
            {
                case Msg.Welcome:
                    if (IsHost) return;
                    if (MyPeer < 0) { MyPeer = r.U8(); Current = Stage.Lobby; SendPlayers(); }
                    break;
                case Msg.Refuse:
                    if (!IsHost) Close(r.Str());
                    break;
                case Msg.Players:
                    if (!IsHost) return;
                    var n = r.U8();
                    l.Players.Clear();
                    for (var k = 0; k < n && k < MaxPlayers; k++) { var name = r.Str(); l.Players.Add(new KeyValuePair<string, int>(name, r.U8() & 127)); }
                    // sent before it heard of the latest recolours: put its players right
                    for (var v = Math.Max(0, r.I32()); v < _remaps.Count; v++)
                        for (var k = 0; k < l.Players.Count; k++)
                            if ((l.Players[k].Value & 7) == _remaps[v].Key) l.Players[k] = new KeyValuePair<string, int>(l.Players[k].Key, (l.Players[k].Value & ~7) | _remaps[v].Value);
                    if (Current == Stage.Playing && l.MidMatch) TakeBackSeats(l);
                    else if (Current != Stage.Playing) RebuildSeats();
                    break;
                case Msg.Lobby:
                    if (IsHost) return;
                    ReadLobby(r);
                    break;
                case Msg.CardStats:
                    if (l.Peer < 0 && IsHost) return;
                    var stats = r.BigBytes();
                    if (CardStatsIn != null) CardStatsIn(stats);
                    break;
                case Msg.Recolour:
                    if (!IsHost || l == null || l.Peer < 0 || Current != Stage.Lobby) return;
                    var from = r.U8();
                    RecolourSide(from, r.U8());
                    break;
                case Msg.Start:
                    if (IsHost) return;
                    var id = r.I32();
                    var seed = r.U32();
                    ReadSeats(r);
                    _map = r.U8();
                    var rules = r.Bytes();
                    if (id != MatchId || Match == null) Rules = rules;
                    if (id != MatchId || Match == null) { MatchId = id; Seed = seed; for (var k = 0; k < l.Acked.Length; k++) l.Acked[k] = -1; Begin(); }
                    break;
                case Msg.Inputs:
                    ReadInputs(l, r);
                    break;
                case Msg.Away:
                    if (IsHost) return;
                    ReadAway(r);
                    break;
                case Msg.Rejoin:
                    if (IsHost) return;
                    ReadRejoin(r);
                    break;
                case Msg.Checksum:
                    if (r.I32() != MatchId) return;
                    var tick = r.I32();
                    var hash = r.U64();
                    _theirChecks[tick] = hash;
                    Compare(tick);
                    break;
                case Msg.Ping:
                    Send(conn, new Writer(Msg.Pong).F32(r.F32()).ToArray());
                    break;
                case Msg.Pong:
                    var sent = r.F32();
                    var rtt = (float)(_now - sent);
                    if (rtt >= 0f && rtt < 5f) l.Rtt = l.Rtt * 0.8f + rtt * 0.2f;
                    break;
                case Msg.Leave:
                    Lost(l, "left the game");
                    break;
            }
        }

        private void Lost(Link l, string why)
        {
            _links.Remove(l.Conn);
            _t.Drop(l.Conn);
            if (!IsHost) { Close("The host " + why + "."); return; }
            var name = "A player";
            foreach (var s in Seats) if (s.Peer == l.Peer && !s.Bot && !s.Away) { name = s.Name; break; }
            if (Current == Stage.Playing && Match != null)
            {
                // the match goes on: a bot plays them from the first tick we don't have their controls for, and their
                // seat (and cards) wait in case they come back
                var any = false;
                for (var p = 0; p < Seats.Count && p < Match.Players; p++)
                {
                    var s = Seats[p];
                    if (s.Bot || s.Away || s.Peer != l.Peer) continue;
                    s.Away = true;
                    Match.SetAway(p, Match.KnownUpTo(p) + 1, int.MaxValue);
                    any = true;
                }
                if (any) { Notice = name + " " + why + ". A bot plays for them until they're back."; SendAway(); }
            }
            else
            {
                RebuildSeats();
                Notice = name + " " + why + ".";
            }
        }

        private void Close(string why)
        {
            if (Current == Stage.Closed) return;
            Problem = why;
            Current = Stage.Closed;
            Match = null;
        }

        // ------------------------------------------------------------------ messages

        private void SendPlayers()
        {
            if (MyPeer < 0) return;
            var w = new Writer(Msg.Players).U8(_localPlayers.Count);
            foreach (var p in _localPlayers) w.Str(p.Key).U8(p.Value);
            w.I32(Recolours); // the recolours this PC has heard of
            Send(0, w.ToArray());
        }

        private void WriteSeats(Writer w)
        {
            w.U8(Seats.Count);
            foreach (var s in Seats) w.U8(s.Peer).S8((sbyte)s.Local).U8(s.Team).U8(Look(s.Colour, s.Symbol, s.Ready)).U8(s.Bot ? 1 : 0).Str(s.Name);
            w.U8(TooManyTeams ? 1 : 0);
        }

        private void ReadSeats(Reader r)
        {
            var n = r.U8();
            Seats.Clear();
            for (var k = 0; k < n && k < MaxPlayers; k++)
            {
                var seat = new Seat { Peer = r.U8(), Local = r.S8(), Team = r.U8() & 1 };
                var look = r.U8();
                seat.Colour = look & 7; seat.Symbol = (look >> 3) & 7; seat.Ready = (look & 64) != 0;
                seat.Bot = r.U8() != 0;
                seat.Name = r.Str();
                Seats.Add(seat);
            }
            TooManyTeams = r.U8() != 0;
        }

        private void SendLobby()
        {
            var w = new Writer(Msg.Lobby).I32(_lobbyVersion).U8(Current == Stage.Playing ? 1 : 0).I32(MatchId);
            WriteSeats(w);
            w.U8(_map);
            w.U8(TeamColours[0]).U8(TeamColours[1]).I32(Recolours);
            var bytes = w.ToArray();
            foreach (var l in _links.Values) if (l.Peer > 0) Send(l.Conn, bytes);
        }

        private void ReadLobby(Reader r)
        {
            var version = r.I32();
            var playing = r.U8() != 0;
            var match = r.I32();
            // messages can arrive late and out of order: an old one mustn't undo a newer one (an old "in the lobby"
            // landing after the start used to drop this PC out of the match while everyone else waited for it)
            if (version < _theirLobby) return;
            _theirLobby = version;
            if (Current == Stage.Playing && !playing && match >= MatchId) { Match = null; Current = Stage.Lobby; } // the host went back to the lobby
            if (Current == Stage.Playing) return; // the seats are fixed for the match
            ReadSeats(r);
            _map = r.U8();
            TeamColours[0] = r.U8() & 7; TeamColours[1] = r.U8() & 7;
            var recolours = r.I32();
            if (recolours > Recolours)
            {
                // a side got a new colour: our players go in their side's colour (and tell the host)
                Recolours = recolours;
                for (var k = 0; k < _localPlayers.Count; k++)
                    foreach (var seat in Seats)
                        if (!seat.Bot && seat.Peer == MyPeer && seat.Local == k)
                        {
                            _localPlayers[k] = new KeyValuePair<string, int>(_localPlayers[k].Key, (_localPlayers[k].Value & ~7) | TeamColours[seat.Team & 1]);
                            break;
                        }
                SendPlayers();
            }
        }

        /// <param name="everyone">Also to PCs that already said they started (once a second): one that missed the
        /// start, or was knocked back to the lobby by a late message, gets back in instead of everyone waiting on it.</param>
        private void SendStartToAll(bool everyone = false)
        {
            if (everyone) _lastStartToAll = _now;
            var w = new Writer(Msg.Start).I32(MatchId).U32(Seed);
            WriteSeats(w);
            w.U8(_map);
            w.Bytes(Rules);
            var bytes = w.ToArray();
            foreach (var l in _links.Values) if (l.Peer > 0 && !l.MidMatch && (everyone || !l.StartAcked)) Send(l.Conn, bytes);
        }

        /// <summary>Which players' controls <paramref name="l"/> needs from us.</summary>
        private bool SendsTo(Link l, int p)
        {
            if (p >= Seats.Count) return false;
            var s = Seats[p];
            if (s.Bot) return false;
            return IsHost ? s.Peer != l.Peer : s.Peer == MyPeer;
        }

        private void SendInputs(Link l)
        {
            var w = new Writer(Msg.Inputs).I32(MatchId).I32(Match.Tick);
            var blocks = 0;
            var players = Match.Players;
            var body = new List<int>();
            var repair = _now - l.LastRepair >= RepairEvery;
            var repaired = false;
            for (var p = 0; p < players; p++)
            {
                if (!SendsTo(l, p)) continue;
                var oldest = Math.Max(l.Acked[p] + 1, Match.Tick - Rollback.Ring / 2);
                var to = Match.KnownUpTo(p);
                if (to < oldest) continue;
                // the newest few: every message (so a lost message or two costs nothing)
                var from = Math.Max(oldest, to - ResendTicks + 1);
                body.Add(p); body.Add(from); body.Add(to);
                blocks++;
                // older ones they haven't confirmed: now and then (usually the confirmation is just still on its way)
                if (from > oldest && repair)
                {
                    body.Add(p); body.Add(oldest); body.Add(Math.Min(from - 1, oldest + MaxSendTicks - 1));
                    blocks++;
                    repaired = true;
                }
            }
            if (repaired) l.LastRepair = _now;
            w.U8(blocks);
            for (var b = 0; b < blocks; b++)
            {
                int p = body[b * 3], from = body[b * 3 + 1], to = body[b * 3 + 2];
                w.U8(p).I32(from).U8(to - from + 1);
                for (var t = from; t <= to; t++) { PlayerInput i; Match.TryGetInput(p, t, out i); w.Input(i); }
            }
            // acks: how far we have each player's controls
            w.U8(players);
            for (var p = 0; p < players; p++) w.I32(Match.KnownUpTo(p));
            Send(l.Conn, w.ToArray());
        }

        private void ReadInputs(Link l, Reader r)
        {
            if (r.I32() != MatchId || Match == null) return;
            l.RemoteTick = r.I32();
            var blocks = r.U8();
            for (var b = 0; b < blocks; b++)
            {
                var p = r.U8();
                var from = r.I32();
                var n = r.U8();
                for (var k = 0; k < n; k++)
                {
                    var input = r.Input();
                    // only accept a player's controls from the PC that owns them (or the host passing them on)
                    if (p < Match.Players && p < Seats.Count && !Seats[p].Bot && Seats[p].Peer != MyPeer && (IsHost ? Seats[p].Peer == l.Peer : true))
                        Match.SetInput(p, from + k, input);
                }
            }
            var acks = r.U8();
            var first = IsHost && l.MidMatch && !l.StartAcked;
            for (var p = 0; p < acks && p < l.Acked.Length; p++) { var a = r.I32(); l.Acked[p] = first ? a : Math.Max(l.Acked[p], a); }
            if (IsHost) l.StartAcked = true;
        }

        // ------------------------------------------------------------------ leaving and coming back mid-match

        private bool AnyAway()
        {
            foreach (var s in Seats) if (s.Away) return true;
            return false;
        }

        /// <summary>Host: someone joined mid-match: give them back seats left empty (their own name first) and hand them the game.</summary>
        private void TakeBackSeats(Link l)
        {
            if (Match == null) return;
            if (l.Rejoined.Count == 0)
            {
                // back on the tick a moment from now, so their controls have time to reach everyone
                var back = Match.Tick + 60;
                for (var k = 0; k < l.Players.Count; k++)
                {
                    var want = l.Players[k].Key;
                    var seat = -1;
                    for (var p = 0; p < Seats.Count && p < Match.Players; p++) if (Seats[p].Away && Seats[p].Name == want) { seat = p; break; }
                    if (seat < 0) for (var p = 0; p < Seats.Count && p < Match.Players; p++) if (Seats[p].Away) { seat = p; break; }
                    if (seat < 0) break;
                    var s = Seats[seat];
                    s.Away = false; s.Peer = l.Peer; s.Local = k; s.Name = want;
                    Match.SetAway(seat, Match.AwayFrom(seat), back);
                    l.Rejoined.Add(seat);
                }
                if (l.Rejoined.Count == 0)
                {
                    Send(l.Conn, new Writer(Msg.Refuse).Str("That game has already started and every seat is taken. Ask the host to go back to the lobby.").ToArray());
                    _links.Remove(l.Conn);
                    _t.Drop(l.Conn);
                    return;
                }
                Notice = Seats[l.Rejoined[0]].Name + " is back.";
                _anyBack = true;
                SendAway();
            }
            SendRejoin(l);
        }

        /// <summary>Host: the game as it is now (a settled tick), to someone coming back. Sent until their controls arrive.</summary>
        private void SendRejoin(Link l)
        {
            if (l.StartAcked || Match == null) return;
            var at = Math.Min(Match.Confirmed + 1, Match.Tick - 1);
            var state = Match.SavedAt(at);
            if (state == null) return; // not settled yet: next time
            l.RejoinSent = _now;
            var w = new Writer(Msg.Rejoin).I32(MatchId).U32(Seed);
            WriteSeats(w);
            w.U8(_map);
            w.Bytes(Rules);
            w.I32(at);
            WriteAway(w);
            w.BigBytes(StateCodec.Write(state));
            // their copy of everyone's controls starts from that tick
            for (var k = 0; k < l.Acked.Length; k++) l.Acked[k] = at - 1;
            Send(l.Conn, w.ToArray());
        }

        private void WriteAway(Writer w)
        {
            w.U8(Match.Players);
            for (var p = 0; p < Match.Players; p++) w.I32(Match.AwayFrom(p)).I32(Match.AwayUntil(p)).U8(Seats[p].Away ? 1 : 0);
        }

        private void SendAway()
        {
            if (Match == null) return;
            var w = new Writer(Msg.Away).I32(MatchId);
            WriteAway(w);
            var bytes = w.ToArray();
            foreach (var l in _links.Values) if (l.Peer > 0 && (!l.MidMatch || l.StartAcked)) Send(l.Conn, bytes);
        }

        private void ReadAway(Reader r)
        {
            if (r.I32() != MatchId || Match == null) return;
            ReadAwayList(r);
        }

        private void ReadAwayList(Reader r)
        {
            var n = r.U8();
            for (var p = 0; p < n; p++)
            {
                int from = r.I32(), until = r.I32();
                var away = r.U8() != 0;
                if (Match != null) Match.SetAway(p, from, until);
                if (p < Seats.Count) Seats[p].Away = away;
            }
        }

        /// <summary>Client: back in a match that was going on: start from the game the host handed over.</summary>
        private void ReadRejoin(Reader r)
        {
            var id = r.I32();
            var seed = r.U32();
            if (id == MatchId && Match != null) return; // already back in
            ReadSeats(r);
            _map = r.U8();
            Rules = r.Bytes();
            var at = r.I32();
            // the away list needs the match first: read it into a buffer, then the state
            var n = r.U8();
            var spans = new int[n, 3];
            for (var p = 0; p < n; p++) { spans[p, 0] = r.I32(); spans[p, 1] = r.I32(); spans[p, 2] = r.U8(); }
            var state = StateCodec.Read(r.BigBytes());
            MatchId = id; Seed = seed;
            var sim = _makeSim();
            var local = new bool[Seats.Count];
            var bots = new bool[Seats.Count];
            for (var k = 0; k < Seats.Count; k++) { bots[k] = Seats[k].Bot; local[k] = !Seats[k].Bot && Seats[k].Peer == MyPeer; }
            Match = new Rollback(sim, state, local, bots, InputDelay, at);
            for (var p = 0; p < n; p++) { Match.SetAway(p, spans[p, 0], spans[p, 1]); if (p < Seats.Count) Seats[p].Away = spans[p, 2] != 0; }
            foreach (var l in _links.Values) for (var k = 0; k < l.Acked.Length; k++) l.Acked[k] = -1;
            _myChecks.Clear(); _theirChecks.Clear(); _lastChecked = at;
            DesyncTick = -1;
            Current = Stage.Playing;
            _started = _now;
        }

        // ------------------------------------------------------------------ desync check

        private void Checksums()
        {
            var confirmed = Match.Confirmed;
            for (var t = _lastChecked + ChecksumEvery; t <= confirmed && t < Match.Tick; t += ChecksumEvery)
            {
                var s = Match.SavedAt(t);
                if (s == null && t > Match.Tick - Rollback.Ring + 8) break; // not settled yet (a replay is pending): try again next time
                if (s == null) { _lastChecked = t; continue; }             // fell out of the history: skip it
                _lastChecked = t;
                var hash = s.Checksum();
                _myChecks[t] = hash;
                var bytes = new Writer(Msg.Checksum).I32(MatchId).I32(t).U64(hash).ToArray();
                foreach (var l in _links.Values) if (l.Peer >= 0) Send(l.Conn, bytes);
                Compare(t);
            }
            // forget old ones
            if (_myChecks.Count > 64) { var old = new List<int>(); foreach (var k in _myChecks.Keys) if (k < _lastChecked - 64 * ChecksumEvery) old.Add(k); foreach (var k in old) { _myChecks.Remove(k); _theirChecks.Remove(k); } }
        }

        private void Compare(int tick)
        {
            ulong mine, theirs;
            if (!_myChecks.TryGetValue(tick, out mine) || !_theirChecks.TryGetValue(tick, out theirs)) return;
            if (mine != theirs && DesyncTick < 0)
            {
                DesyncTick = tick;
                Problem = "Desync at tick " + tick + ": the PCs' games differ. This is a bug; please report it.";
            }
        }

        public void Dispose()
        {
            foreach (var l in _links.Values) if (l.Peer >= 0) Send(l.Conn, new Writer(Msg.Leave).ToArray());
            _t.Dispose();
            Current = Stage.Closed;
        }
    }
}
