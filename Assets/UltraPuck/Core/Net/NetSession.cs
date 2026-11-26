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
        public const int Version = 90;// 2: knock-loose checks, bigger puck, Puck Gun on brake. 3: carried puck physics, card hover. 4: thick walls, faster shots. 5: bots go round nets. 6: hit lag, timed matches, team colours and symbols. 7: no daze on checks, pushable pylons, smaller nets. 8: 8 players, goals on touch, charge from the pickup, hit strength by weight. 9: Shockwave slows 45%. 10: Ice Trail. 11: ice melts from 1 s, slower. 12: ice melts slower still. 13: replay skip votes, ready up, bots with their own team and symbol. 14: S and U rinks, bots follow routes, smaller Test Rink nets. 15: faster wall checks, rewinds gathered, 0.4 s prediction. 16: ice lasts 3 s and melts at half pace, pass boost, wedge pickups. 17: 3 s countdown, deeper nets, carrying into your own net. 18: hold to keep dashing, wider S and U rinks. 19: quicker acceleration. 20: the host pauses everyone. 21: shots counted (for sounds). 22: every card as likely as any other, nets 30% bigger, S Rink nets on the ice, upright U Rink, pylons by the way a net faces, bots pass. 23: no check after a quick shot until Shoot is let go, carried puck may sit in the carrier at a wall. 24: Over Charge shot speed scales past a full charge (1.5×/2×/…); puck speed cap raised so it can. 25: Shockwave is a utility (Triangle / Y), not on check. 26: Shockwave 3 s / range 15 radii / 6 s cooldown. 27: Shockwave 50% / 2 s / range 5 / 6 s cooldown. 28: Shockwave pushes everyone (no slow), thin expanding ring. 29: Shockwave push 2×. 30: one utility at a time (replace), MultiUtility setting, Swap rock card. 31: Swap clap (teleport). 32: clap only on teleport (not throw); rock lighter and glidey. 33: bot Swap CD 3 s, swap keeps momentum, rocks pickable/shootable. 34: rock↔puck collide (gun-style knock when carried). 35: team vote counts humans only (bots follow). 36: bot-only team still picks by tier. 37: goal blast shove + LastGoalPlayer; Shockwave shoves puck and rocks; replay after 3 s. 38: no Swap utility while holding puck; dash CD +10% + longer coast. 39: goal box front at the net opening. 40: team-vote bots hover-follow without locking until a human locks. 41: Friendly Fire picks up and throws teammates, Shockwave knocks the puck loose, one Pylon Goalie that grows a size a card and stays out of the nets; teammates picked up on the wedge; pre-charge without the puck; dash where you move (input bit 32); Team Play refreshes the team, Team Play II gone. 42: Pylon Goalie grows 55% a card. 43: swapping onto bots is part of the game (input bits 64/128, Pilot), mid-match bots (Pick 201/202), match stats, Puck Gun knockback (no stun, 1 s), 30% slower while charging, puck drag 7/11, bot pass/positioning, Team Vote people draw first, lobby/start messages ordered. 44: countdown waits 1.5 s after picks, leave/rejoin mid-match (Away / Rejoin messages, state hand-over), host kick-off and score commands (Pick 203-207). 45: bot-run players wait between Swap uses, bots throw a carried teammate at the puck. 46: Power Shot = full charge quick shot (QuickShotFull), Invisible Quick Shot (PuckHidden), Cannon Ball (CannonTeam). 47: Cannon Ball shoves the catcher along the shot (Shoved), no hit lag. 48: Swap rock gone once stopped / no swap to a held rock, Ice Trail on every shot (teammates +, 3x longer), Team Play = pass speed boost (Fast), Cannon Ball shove into your own net scores (ShovedBy), Stone Wall (Stones). 49: Same Draw picking (rules bit 16), Stone Wall up to 30 long / 3 s. 50: Swap rock glides 35% less (RockDrag 0.277), Ice Trail 15 s, teammates +60% (IceBoost), RockDrag 0.326, swapping gives you the rock's momentum and the rock keeps its own. 51: Friendly Fire grabs only while holding charge/brake, throws like a shot (MateTossDrag, TossEndSpeed), +15% speed 4 s after. 52: Invisible Shot 0.5 s. 53: host takes a bot out mid-match (Pick 208 + player). 54: Stone Wall stands 6 s. 55: no charging slow-down while carrying a teammate. 56: Shockwave held to grow (ShockHold, 1.5-6 units over 6 s, no cooldown), Stone Wall goes on after bounces and stands 5.5 s, Heavyweight size x1.16. 57: Shockwave grows from 0. 58: Swap rock lasts 10 s (Rock.Life), passes only end when an opponent takes the puck. 59: side colours in the lobby message, Recolour message. 60: bots pass more / quick shots / go round nets; Hockey and Big Rink nets say which way they open. 61: cards go with the person on a swap. 62: Puck Gun on the utility button, 0.85 s cooldown, +3 s for bots. 63: kick-off spot rotates, tight face-off groups in front of the nets, Portal Gun, Team Play +50%. 64: portals only on the back of nets, one Portal Gun a team (maxTeam), going through a portal a bit at a time (PortalVia), Invisible Shot gone. 65: one-a-team cards to one picker in shared hands (Swap too), portals replace the other team's, free play puck drills (Pick 220-223), Slot Machine. 66: Shockwave charges faster (3.6 s), pushes up to 3.5x harder at a full charge. 67: Swap trades momentum again (you take the rock's, it takes yours). 68: own goal only if nobody on the scoring team touched it (TeamTouch0/1), at most two portals a team, slot reels only prize symbols. 69: Choose (pick from the deck), New Hand (re-roll a pick), Double Card (two random cards). 70: a dash counts as a check for 0.4 s (DashHitTime, was 0.25). 71: the Portal Gun keeps its colour until a portal sticks. 72: Double Card shows its two cards before the countdown (Gifts, GiftRevealTime). 73: bots look over their cards for 1.8 s before taking one (BotPickDelay). 74: after new cards (Choose deck, re-roll) a pick waits until the old one is let go (PickHeld). 75: Carry-through (CarryOn / CarryPercent, Pick 224-246, rules byte after the card list), the host's rules in the lobby message, joining a match in progress, changing team and watching mid-match (the match is re-seated: Layout in Inputs / Away / Checksum, inputs in the hand-over, look bit 128 = watching). 76: Knock-away (KnockPercent, Pick 100-120, rules byte after Carry-through). 77: catching up on cards after joining part-way (PlayerState.CatchingUp / PickRounds, GameState.TeamRounds). 78: Double Agents take turns round the other side's face-off spots (kick-off too), face the puck, and start disguised (PlayerState.Disguised / DisguiseAs / DisguiseTime); Head Start skips agents; Backwards Rink (net "faceoff" side); a carried puck shoved dead in line goes straight out (no random side). 79: Stone Wall stands 11 s, up to 45 long / 4.5 s of laying. 80: RAGE (Stats.Rage, PlayerState.RageMeter / RageTime / RageCooldown). 81: raging: 3x dash, no taking the puck, smashes Stone Walls. 82: knock-outs (a raging check near the boards, or leaving the rink) and respawns (PlayerState.OutTime / OutAt / OutDir / RespawnTime / Launches / Respawns). 83: knock-outs need a hard raging hit and a 1-in-5 roll, from anywhere (OutFrom). 84: only Featherweights get launched, 5 s respawn. 85: no 1-in-5 roll for a launch. 86: Profile looks (Styles / StyleTable messages). 87: Steam accounts with the looks (profile pictures on banners). 88: PlayerInput.Look (the Black Hole goal explosion pulls everyone in: PlayerState.GoalPull); stealing with the whole wedge as drawn (was a point at the body's front); no cap on how fast a check sends someone (the tick is cut into more steps instead). 89: RAGE fills by the checker's weight over yours, to a random 3-5 (PlayerState.RageFill / RageNeed, was 7 checks). 90: Momentum and Loyal cards.
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
        public static int Look(int colour, int symbol, bool ready = false, bool watching = false) { return (colour & 7) | ((symbol & 7) << 3) | (ready ? 64 : 0) | (watching ? 128 : 0); }

        /// <summary>A player's look says they only want to watch the match that's on (they keep no seat in it).</summary>
        public static bool Watching(int look) { return (look & 128) != 0; }

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
            /// <summary>Host: the newest seating of this match this PC has said it plays (-1: none yet).</summary>
            public int LayoutAcked = -1;
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
            if (IsHost && Current == Stage.Playing) _seatsDirty = true; // the seats are the match's: re-seat it if need be
            else if (IsHost) RebuildSeats();
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

        /// <summary>
        /// The host's match settings as they are now (the host's game sets them; everyone else gets them in the lobby
        /// message), so every player can see them. Not the rules of the match being played (that's <see cref="Rules"/>).
        /// </summary>
        public byte[] LobbyRules = new byte[0];

        /// <summary>Client: the host has a match on (from the lobby message; ready up in the lobby to join it).</summary>
        public bool HostPlaying { get; private set; }

        /// <summary>
        /// How many times the match being played has been re-seated (someone joined, changed team or went to watch).
        /// Controls, checks and away lists say which seating they're for, so none from an older one is used by mistake.
        /// </summary>
        public int Layout { get { return _layout; } }
        private int _layout;
        /// <summary>Host: the seats need comparing with what everyone's players want (a team, or to watch).</summary>
        private bool _seatsDirty;

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
            // the lobby's bots are in the match's order: count the bots before them (players past the seats are bots
            // added during the match)
            var b = 0;
            for (var i = 0; i < player; i++) if (i >= Seats.Count || Seats[i].Bot) b++;
            if (b >= 0 && b < _bots.Count) _bots.RemoveAt(b);
            if (player >= 0 && player < Seats.Count && Seats[player].Bot) Seats.RemoveAt(player);
            if (Match != null) _botLeftTick = Match.Tick + InputDelay; // (no re-seating until it's gone on that tick)
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
            foreach (var l in _links.Values) { l.StartAcked = false; l.LayoutAcked = -1; for (var k = 0; k < l.Acked.Length; k++) l.Acked[k] = -1; }
            _seatsDirty = false;
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
            _layout = 0;
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
                if (Current == Stage.Playing && Match != null) Reseat();
                if (Current == Stage.Playing && _now - _lastHello >= 0.25)
                {
                    _lastHello = _now;
                    if (_layout == 0) SendStartToAll(_now - _lastStartToAll >= 1.0); // (once re-seated, the hand-over does it)
                    if (AnyAway() || _anyBack) SendAway();
                    foreach (var l in _links.Values) if (NeedsHandOver(l) && _now - l.RejoinSent >= 0.5) SendRejoin(l);
                }
            }
            // (mid-match too: someone changing team, or going to watch, keeps asking until the host has it)
            if (!IsHost && (Current == Stage.Lobby || Current == Stage.Playing) && _now - _lastHello >= 1.0) { _lastHello = _now; SendPlayers(); }
            if (Current == Stage.Lobby && CardStatsOut != null && _now - _lastStats >= StatsEvery) { _lastStats = _now; SendCardStats(); }

            if (Current == Stage.Playing && Match != null)
            {
                // a fixed rate, not every frame (catching up after a slow frame, but never more than one at once)
                if (_now >= _nextSend - 0.25 / SendRate)
                {
                    _nextSend = Math.Max(_nextSend + 1.0 / SendRate, _now - 1.0 / SendRate);
                    foreach (var l in _links.Values) if (l.Peer >= 0 && !(IsHost && NeedsHandOver(l))) SendInputs(l);
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
                    else if (Seats.Count >= MaxPlayers && !AnyAway()) why = "That game is full.";
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
                    if (MyPeer < 0) { MyPeer = r.U8(); Current = Stage.Lobby; SendPlayers(); SendStyles(); }
                    break;
                case Msg.Refuse:
                    if (!IsHost) Close(r.Str());
                    break;
                case Msg.Players:
                    if (!IsHost) return;
                    var n = r.U8();
                    l.Players.Clear();
                    for (var k = 0; k < n && k < MaxPlayers; k++) { var name = r.Str(); l.Players.Add(new KeyValuePair<string, int>(name, r.U8())); }
                    // sent before it heard of the latest recolours: put its players right
                    for (var v = Math.Max(0, r.I32()); v < _remaps.Count; v++)
                        for (var k = 0; k < l.Players.Count; k++)
                            if ((l.Players[k].Value & 7) == _remaps[v].Key) l.Players[k] = new KeyValuePair<string, int>(l.Players[k].Key, (l.Players[k].Value & ~7) | _remaps[v].Value);
                    SendStyleTable(); // (someone new has everyone's looks straight away)
                    if (Current == Stage.Playing && l.MidMatch) TakeBackSeats(l);
                    if (Current == Stage.Playing) _seatsDirty = true;
                    else RebuildSeats();
                    break;
                case Msg.Lobby:
                    if (IsHost) return;
                    ReadLobby(r);
                    break;
                case Msg.Styles:
                    if (!IsHost || l == null || l.Peer <= 0) return;
                    _styles[l.Peer] = ReadStyles(r);
                    _accounts[l.Peer] = ReadAccounts(r);
                    SendStyleTable();
                    break;
                case Msg.StyleTable:
                    if (IsHost) return;
                    _styles.Clear();
                    _accounts.Clear();
                    for (int k = 0, n2 = r.U8(); k < n2; k++) { var peer = r.U8(); _styles[peer] = ReadStyles(r); _accounts[peer] = ReadAccounts(r); }
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
                    if (r.I32() != MatchId || r.I32() != _layout) return;
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

        // ------------------------------------------------------------------ looks (Profile)

        // the looks of the players on this PC, and (from the host) every other PC's, by peer: looks only, never the game
        private int[] _myStyles = new int[0];
        private ulong[] _myAccounts = new ulong[0];
        private readonly Dictionary<int, int[]> _styles = new Dictionary<int, int[]>();
        // each player's Steam account (0: none), for their profile picture
        private readonly Dictionary<int, ulong[]> _accounts = new Dictionary<int, ulong[]>();

        /// <summary>The looks (see the game's Cosmetics) of the people on this PC, in the order of <see cref="SetLocalPlayers"/>.</summary>
        public void SetLocalStyles(IList<int> styles) { SetLocalStyles(styles, new ulong[styles.Count]); }

        /// <summary>The looks, and each person's Steam account (0: none), of the people on this PC.</summary>
        public void SetLocalStyles(IList<int> styles, IList<ulong> accounts)
        {
            var same = styles.Count == _myStyles.Length && accounts.Count == _myAccounts.Length;
            for (var k = 0; same && k < styles.Count; k++) same = styles[k] == _myStyles[k];
            for (var k = 0; same && k < accounts.Count; k++) same = accounts[k] == _myAccounts[k];
            if (same) return;
            _myStyles = new int[styles.Count];
            styles.CopyTo(_myStyles, 0);
            _myAccounts = new ulong[accounts.Count];
            accounts.CopyTo(_myAccounts, 0);
            if (IsHost) { _styles[0] = _myStyles; _accounts[0] = _myAccounts; SendStyleTable(); }
            else SendStyles();
        }

        /// <summary>The Steam account of the player in seat <paramref name="seat"/> (0: none, or a bot).</summary>
        public ulong AccountOf(int seat)
        {
            if (seat < 0 || seat >= Seats.Count) return 0;
            var s = Seats[seat];
            if (s.Bot || s.Local < 0) return 0;
            ulong[] a;
            if (s.Peer == MyPeer) a = _myAccounts;
            else if (!_accounts.TryGetValue(s.Peer, out a)) return 0;
            return s.Local < a.Length ? a[s.Local] : 0;
        }

        /// <summary>The look of the player in seat <paramref name="seat"/> (0, the plain look, for bots and anyone unknown).</summary>
        public int StyleOf(int seat)
        {
            if (seat < 0 || seat >= Seats.Count) return 0;
            var s = Seats[seat];
            if (s.Bot || s.Local < 0) return 0;
            int[] a;
            if (s.Peer == MyPeer) a = _myStyles;
            else if (!_styles.TryGetValue(s.Peer, out a)) return 0;
            return s.Local < a.Length ? a[s.Local] : 0;
        }

        private static int[] ReadStyles(Reader r)
        {
            var n = Math.Min(r.U8(), MaxPlayers);
            var a = new int[n];
            for (var k = 0; k < n; k++) a[k] = r.I32();
            return a;
        }

        private static ulong[] ReadAccounts(Reader r)
        {
            var n = Math.Min(r.U8(), MaxPlayers);
            var a = new ulong[n];
            for (var k = 0; k < n; k++) a[k] = r.U64();
            return a;
        }

        private static void WriteAccounts(Writer w, ulong[] a)
        {
            w.U8(a.Length);
            foreach (var v in a) w.U64(v);
        }

        private void SendStyles()
        {
            if (IsHost || MyPeer < 0) return;
            var w = new Writer(Msg.Styles).U8(_myStyles.Length);
            foreach (var v in _myStyles) w.I32(v);
            WriteAccounts(w, _myAccounts);
            Send(0, w.ToArray());
        }

        private void SendStyleTable()
        {
            if (!IsHost) return;
            _styles[0] = _myStyles;
            _accounts[0] = _myAccounts;
            var w = new Writer(Msg.StyleTable).U8(_styles.Count);
            foreach (var kv in _styles)
            {
                w.U8(kv.Key).U8(kv.Value.Length);
                foreach (var v in kv.Value) w.I32(v);
                ulong[] acc;
                WriteAccounts(w, _accounts.TryGetValue(kv.Key, out acc) ? acc : new ulong[0]);
            }
            var bytes = w.ToArray();
            foreach (var l in _links.Values) if (l.Peer > 0) Send(l.Conn, bytes);
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
            w.Bytes(LobbyRules);
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
            var rules = r.Bytes();
            // messages can arrive late and out of order: an old one mustn't undo a newer one (an old "in the lobby"
            // landing after the start used to drop this PC out of the match while everyone else waited for it)
            if (version < _theirLobby) return;
            _theirLobby = version;
            LobbyRules = rules;
            HostPlaying = playing;
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
            var w = new Writer(Msg.Inputs).I32(MatchId).I32(_layout).I32(Match.Tick);
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
            if (r.I32() != _layout) return; // for another seating of the match (player numbers may differ)
            if (IsHost) l.LayoutAcked = _layout;
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
            if (Match == null || l.Rejoined.Count > 0) return;
            // back on the tick a moment from now, so their controls have time to reach everyone
            var back = Match.Tick + 60;
            for (var k = 0; k < l.Players.Count; k++)
            {
                if (Watching(l.Players[k].Value) || SeatOf(Seats, l.Peer, k) >= 0) continue;
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
            // nobody's seat waiting: they're given a new one once they ready up (see Reseat)
            if (l.Rejoined.Count == 0) return;
            Notice = Seats[l.Rejoined[0]].Name + " is back.";
            _anyBack = true;
            SendAway();
            SendRejoin(l);
        }

        /// <summary>The seat of <paramref name="peer"/>'s <paramref name="local"/>-th person (not away), or -1.</summary>
        private static int SeatOf(List<Seat> seats, int peer, int local)
        {
            for (var p = 0; p < seats.Count; p++) if (!seats[p].Bot && !seats[p].Away && seats[p].Peer == peer && seats[p].Local == local) return p;
            return -1;
        }

        /// <summary>Host: this PC should be handed the match as it is (it's coming in part-way, or the match was re-seated).</summary>
        private bool NeedsHandOver(Link l)
        {
            if (!IsHost || l.Peer <= 0 || Current != Stage.Playing || Match == null || l.LayoutAcked == _layout) return false;
            if (!l.MidMatch) return _layout > 0;
            // joined part-way: once they have a seat (or have been watching)
            if (l.LayoutAcked >= 0) return true;
            foreach (var s in Seats) if (!s.Bot && !s.Away && s.Peer == l.Peer) return true;
            return false;
        }

        private List<KeyValuePair<string, int>> PlayersOf(int peer)
        {
            if (peer == 0) return _localPlayers;
            foreach (var l in _links.Values) if (l.Peer == peer) return l.Players;
            return null;
        }

        private Link LinkOf(int peer)
        {
            foreach (var l in _links.Values) if (l.Peer == peer) return l;
            return null;
        }

        /// <summary>The team a colour is (-1: neither side's).</summary>
        private int TeamOfColour(int colour)
        {
            colour &= 7;
            return TeamColours[0] == colour ? 0 : TeamColours[1] == colour ? 1 : -1;
        }

        /// <summary>Host: a bot was taken out on this tick or later; seats can't be moved about until then.</summary>
        private int _botLeftTick = -1;

        /// <summary>
        /// Host, mid-match: give the match the seats everyone's players want now. Someone in the lobby who has readied up
        /// gets a new seat (joining the match part-way); someone whose colour is now the other side's changes team;
        /// someone who chose to watch gives up their seat. It's done on a settled tick: the game there is changed, every
        /// PC is handed it (with everyone's controls from then on), and plays on from it to where it was.
        /// </summary>
        private void Reseat()
        {
            if (!_seatsDirty || !IsHost || Match == null) return;
            var at = Math.Min(Match.Confirmed + 1, Match.Tick - 1);
            if (at <= _botLeftTick) return;
            var saved = Match.SavedAt(at);
            if (saved == null) return; // not settled yet: next frame
            if (saved.Phase == Phase.Picking || Match.State.Phase == Phase.Picking) return; // after the card pick
            var st = saved.Clone();
            var sim = Match.Sim;
            var seats = new List<Seat>();
            foreach (var s0 in Seats)
                seats.Add(new Seat { Peer = s0.Peer, Local = s0.Local, Team = s0.Team, Colour = s0.Colour, Symbol = s0.Symbol, Bot = s0.Bot, Away = s0.Away, Ready = s0.Ready, Name = s0.Name });
            // bots added during the match have no seats yet
            var bot = 0;
            foreach (var s0 in seats) if (s0.Bot) bot++;
            for (var i = seats.Count; i < st.Players.Length; i++)
            {
                var t = st.Players[i].Team & 1;
                seats.Add(new Seat { Peer = 0, Local = -1, Bot = true, Ready = true, Team = t, Colour = TeamColours[t], Symbol = (BotLook(bot++) >> 3) & 7, Name = "Bot" });
            }
            if (seats.Count != st.Players.Length) return; // (shouldn't happen: a bot's seat taken away before its tick)
            var map = new List<int>();
            for (var i = 0; i < seats.Count; i++) map.Add(i);
            var news = new List<string>();

            // changing team, or going to watch
            for (var i = seats.Count - 1; i >= 0; i--)
            {
                var seat = seats[i];
                if (seat.Bot || seat.Away) continue;
                var list = PlayersOf(seat.Peer);
                if (list == null) continue; // (their PC is gone: Lost marks them away)
                if (seat.Local < 0 || seat.Local >= list.Count || Watching(list[seat.Local].Value))
                {
                    news.Add(seat.Name + " is watching.");
                    sim.RemovePlayer(st, i);
                    seats.RemoveAt(i);
                    map.RemoveAt(i);
                    continue;
                }
                var team = TeamOfColour(list[seat.Local].Value);
                if (team >= 0 && team != seat.Team)
                {
                    sim.ChangeTeam(st, i, team);
                    seat.Team = team;
                    seat.Colour = TeamColours[team];
                    news.Add(seat.Name + " changed team.");
                }
            }

            // joining: people with no seat who want one
            var peers = new List<int> { 0 };
            foreach (var l in _links.Values) if (l.Peer > 0) peers.Add(l.Peer);
            peers.Sort();
            foreach (var peer in peers)
            {
                var list = PlayersOf(peer);
                var link = LinkOf(peer);
                var inMatch = peer == 0 || (link != null && link.LayoutAcked >= 0);
                for (var k = 0; list != null && k < list.Count; k++)
                {
                    var look = list[k].Value;
                    if (Watching(look) || SeatOf(seats, peer, k) >= 0) continue;
                    if (!inMatch && (look & 64) == 0) continue; // still in the lobby: in once they're ready
                    if (seats.Count >= MaxPlayers) break;
                    var team = TeamOfColour(look);
                    if (team < 0)
                    {
                        var red = 0;
                        foreach (var s0 in seats) if (s0.Team == 0) red++;
                        team = red * 2 <= seats.Count ? 0 : 1;
                    }
                    sim.AddPlayer(st, team, false);
                    seats.Add(new Seat { Peer = peer, Local = k, Team = team, Colour = TeamColours[team], Symbol = (look >> 3) & 7, Name = list[k].Key });
                    map.Add(-1);
                    news.Add(list[k].Key + (inMatch ? " is playing." : " joined the match."));
                }
            }

            if (news.Count == 0) { _seatsDirty = false; return; }
            if (sim.Cards != null) sim.Cards.Recompute(st);

            var old = Match;
            var oldTick = old.Tick;
            Seats.Clear();
            Seats.AddRange(seats);
            _layout++;
            var local = new bool[st.Players.Length];
            var bots = new bool[st.Players.Length];
            for (var p = 0; p < st.Players.Length; p++) { bots[p] = Seats[p].Bot; local[p] = !Seats[p].Bot && Seats[p].Peer == MyPeer; }
            Match = new Rollback(sim, st, local, bots, InputDelay, at);
            for (var p = 0; p < Match.Players; p++)
            {
                var o = map[p];
                if (bots[p]) continue;
                if (o >= 0)
                {
                    PlayerInput i;
                    for (var t = at; t < at + Rollback.Ring - 2; t++) if (old.TryGetInput(o, t, out i)) Match.Load(p, t, i);
                    Match.SetAway(p, old.AwayFrom(o), old.AwayUntil(o));
                }
                else Match.SetAway(p, at, oldTick + 60); // new: the bot plays them until their controls have had time to arrive
            }
            while (Match.Tick < oldTick) Match.Advance();
            foreach (var l in _links.Values) for (var k = 0; k < l.Acked.Length; k++) l.Acked[k] = at - 1;
            _myChecks.Clear(); _theirChecks.Clear(); _lastChecked = at - at % ChecksumEvery;
            Notice = string.Join(" ", news.ToArray());
            _lobbyVersion++;
            foreach (var l in _links.Values) if (NeedsHandOver(l)) SendRejoin(l);
            // (and again next frame, in case someone else wants a change too)
        }

        /// <summary>
        /// Host: the game as it is now (a settled tick) and everyone's controls from then on, to a PC coming in part-way
        /// or after the match was re-seated. Sent until their controls for this seating arrive.
        /// </summary>
        private void SendRejoin(Link l)
        {
            if (Match == null) return;
            var at = Math.Min(Match.Confirmed + 1, Match.Tick - 1);
            var state = Match.SavedAt(at);
            if (state == null) return; // not settled yet: next time
            l.RejoinSent = _now;
            var w = new Writer(Msg.Rejoin).I32(MatchId).U32(Seed).I32(_layout);
            WriteSeats(w);
            w.U8(_map);
            w.Bytes(Rules);
            w.I32(at);
            WriteAway(w);
            w.BigBytes(StateCodec.Write(state));
            w.U8(Match.Players);
            for (var p = 0; p < Match.Players; p++)
            {
                var to = Match.IsBot(p) ? at - 1 : Math.Min(Match.KnownUpTo(p), at + Rollback.Ring - 3);
                w.I32(Math.Max(0, to - at + 1));
                for (var t = at; t <= to; t++) { PlayerInput i; Match.TryGetInput(p, t, out i); w.Input(i); }
            }
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
            var w = new Writer(Msg.Away).I32(MatchId).I32(_layout);
            WriteAway(w);
            var bytes = w.ToArray();
            foreach (var l in _links.Values) if (l.Peer > 0 && !NeedsHandOver(l)) Send(l.Conn, bytes);
        }

        private void ReadAway(Reader r)
        {
            if (r.I32() != MatchId || Match == null || r.I32() != _layout) return;
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

        /// <summary>
        /// Client: handed the match as it is: coming back or joining part-way, or the host re-seated it (someone joined,
        /// changed team or went to watch). Start from that game with everyone's controls from then on (and, if this PC
        /// was already playing, its own controls it had made since), then catch up to where it was.
        /// </summary>
        private void ReadRejoin(Reader r)
        {
            var id = r.I32();
            var seed = r.U32();
            var layout = r.I32();
            if (id == MatchId && Match != null && layout <= _layout) return; // already have this one
            var oldSeats = new List<Seat>(Seats);
            ReadSeats(r);
            _map = r.U8();
            var rules = r.Bytes();
            var at = r.I32();
            // the away list needs the match first: read it into a buffer, then the state
            var n = r.U8();
            var spans = new int[n, 3];
            for (var p = 0; p < n; p++) { spans[p, 0] = r.I32(); spans[p, 1] = r.I32(); spans[p, 2] = r.U8(); }
            var state = StateCodec.Read(r.BigBytes());
            var np = r.U8();
            var inputs = new List<PlayerInput[]>();
            for (var p = 0; p < np; p++)
            {
                var count = r.I32();
                if (count < 0 || count > Rollback.Ring) throw new FormatException("bad input count");
                var arr = new PlayerInput[count];
                for (var k = 0; k < count; k++) arr[k] = r.Input();
                inputs.Add(arr);
            }
            var old = id == MatchId ? Match : null;
            MatchId = id; Seed = seed;
            _layout = layout;
            Rules = rules;
            var sim = old != null ? old.Sim : _makeSim();
            var players = state.Players.Length;
            var local = new bool[players];
            var bots = new bool[players];
            for (var k = 0; k < players; k++) { bots[k] = k >= Seats.Count || Seats[k].Bot; local[k] = !bots[k] && Seats[k].Peer == MyPeer; }
            Match = new Rollback(sim, state, local, bots, InputDelay, at);
            for (var p = 0; p < inputs.Count && p < players; p++)
                for (var k = 0; k < inputs[p].Length; k++) Match.Load(p, at + k, inputs[p][k]);
            // this PC's own controls the host hasn't had yet (it was already playing): kept
            if (old != null)
                for (var p = 0; p < players; p++)
                {
                    if (!local[p]) continue;
                    var o = SeatOf(oldSeats, MyPeer, Seats[p].Local);
                    if (o < 0 || o >= old.Players) continue;
                    var from = at + (p < inputs.Count ? inputs[p].Length : 0);
                    PlayerInput i;
                    for (var t = from; t < at + Rollback.Ring - 2; t++) if (old.TryGetInput(o, t, out i)) Match.Load(p, t, i);
                }
            for (var p = 0; p < n; p++) { Match.SetAway(p, spans[p, 0], spans[p, 1]); if (p < Seats.Count) Seats[p].Away = spans[p, 2] != 0; }
            foreach (var l in _links.Values) for (var k = 0; k < l.Acked.Length; k++) l.Acked[k] = -1;
            _myChecks.Clear(); _theirChecks.Clear(); _lastChecked = at - at % ChecksumEvery;
            DesyncTick = -1;
            Current = Stage.Playing;
            _started = _now;
            if (old != null) while (Match.Tick < old.Tick) Match.Advance();
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
                var bytes = new Writer(Msg.Checksum).I32(MatchId).I32(_layout).I32(t).U64(hash).ToArray();
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
