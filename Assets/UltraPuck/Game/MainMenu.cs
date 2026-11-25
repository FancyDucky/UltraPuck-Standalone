using System;
using System.Collections.Generic;
using UltraPuck.Core.Net;
using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// The main menu and the online screens: a big title and a column of large buttons on the left, with bots
    /// playing a live match on the rink behind. Works with any controller, the keyboard, or the mouse.
    /// <see cref="GameRunner"/> does what the buttons ask.
    /// </summary>
    internal sealed class MainMenu : MonoBehaviour
    {
        /// <summary><see cref="Title"/> is the "press any button" screen the game starts on (see <see cref="TitleScreen"/>).</summary>
        public enum Page { None, Main, Online, JoinIp, Browse, OnlineLobby, Friends, Title }

        public Page Current { get; private set; }
        public GameRunner Runner;
        public LocalPlayers Local;
        /// <summary>The online game, while there is one (set by the runner).</summary>
        public NetSession Session;
        /// <summary>A message to show (an error, or why a game ended).</summary>
        public string Message;

        private int _focus;
        private float _focusY = -1f, _openedAt;
        private string _address;
        private bool _typing;

        private sealed class Row
        {
            public string Label, Detail;
            /// <summary>A label that changes (shown instead of <see cref="Label"/>).</summary>
            public Func<string> LabelNow;
            public Action Press;
            public Action<int> Step;
            public Func<string> Value;            // a choice's current value, shown as  ‹ value ›
            public Func<string> Shown;            // a setting to look at (no arrows: only the host changes it)
            public Func<bool> Enabled = () => true;
            public bool TextField;
        }

        private readonly List<Row> _rows = new List<Row>();

        private void Awake()
        {
            _address = PlayerPrefs.GetString("join_address", "127.0.0.1");
        }

        public void Show(Page page)
        {
            if (page != Current) { _focus = 0; _focusY = -1f; _openedAt = Time.unscaledTime; _openedFrame = Time.frameCount; _scroll = 0; _scrollFocus = -1; }
            if (page != Page.OnlineLobby && page != Page.Friends) OptionsOpen = false;
            if (page == Page.Title && Current != Page.Title) _titleAt = Time.unscaledTime;
            if (page != Page.Main) _leftTitleAt = -100f;
            Current = page;
            _typing = page == Page.JoinIp;
        }

        // the frame the page opened on: the click that opened it mustn't also press a button on it
        private int _openedFrame = -1;
        // when the title screen appeared, and when a button was pressed on it (the change to the main menu takes a moment)
        private float _titleAt, _leftTitleAt = -100f;

        /// <summary>Screenshots: buttons don't leave the title screen (someone using the PC mustn't spoil the picture).</summary>
        public bool TitleStays;

        /// <summary>A button was pressed on the title screen: on to the main menu.</summary>
        public void LeaveTitle()
        {
            if (Current != Page.Title) return;
            Sound.Select();
            Show(Page.Main);
            _leftTitleAt = Time.unscaledTime;
            _titleMouseHeld = true;
        }

        // the click (or key) that left the title screen: the menu's buttons take no mouse until it has been let go and
        // the menu has finished sliding in
        private bool _titleMouseHeld;

        /// <summary>The main menu is still arriving from the title screen (or the button that left it is still down).</summary>
        private bool MouseLocked()
        {
            if (Current != Page.Main) return false;
            var m = UnityEngine.InputSystem.Mouse.current;
            if (_titleMouseHeld && (m == null || (!m.leftButton.isPressed && !m.rightButton.isPressed))) _titleMouseHeld = false;
            return _titleMouseHeld || Time.unscaledTime - _leftTitleAt < TitleScreen.LeaveTime;
        }

        /// <summary>The online lobby's options panel is open (the host's settings, inviting, leaving).</summary>
        public bool OptionsOpen { get; private set; }
        private float _optionsAt;

        /// <summary>The lobby's own controls (joining, switching team) are on hold: a panel or page is on top.</summary>
        public bool LobbyBusy { get { return OptionsOpen || Current == Page.Friends; } }

        private void OpenOptions(bool open)
        {
            if (open == OptionsOpen) return;
            OptionsOpen = open;
            _focus = 0; _focusY = -1f; _optionsAt = Time.unscaledTime;
        }

        /// <summary>Open or close the online lobby's options (for the screenshots).</summary>
        public void ShowOptions(bool open) { OpenOptions(open); }

        private void InviteFriends()
        {
            if (SteamNet.OverlayWorks) { SteamNet.Invite(); return; }
            _friends = SteamNet.OnlineFriends();
            _friendsAt = Time.unscaledTime;
            Show(Page.Friends);
        }

        // ------------------------------------------------------------------ the rows on each page

        private void BuildRows()
        {
            _rows.Clear();
            switch (Current)
            {
                case Page.Main:
                    _rows.Add(new Row { Label = "Play", Detail = "Local match: everyone on this PC", Press = () => Runner.GoLocalLobby() });
                    _rows.Add(new Row { Label = "Online", Detail = "Steam, or by IP address", Press = () => Show(Page.Online) });
                    _rows.Add(new Row { Label = "Free play", Detail = "Practice, bots optional", Press = () => Runner.StartFreePlayFrom(_lastDevice) });
                    _rows.Add(new Row { Label = "Cards", Detail = "Every card, and which get picked most online", Press = () => Runner.OpenCards() });
                    _rows.Add(new Row { Label = "Settings", Press = () => Runner.OpenSettings() });
                    _rows.Add(new Row { Label = "Quit", Press = Application.Quit });
                    break;
                case Page.Online:
                    _rows.Add(new Row { Label = "Host on Steam", Detail = "Friends join through Steam, no setup", Press = () => Runner.HostSteam(), Enabled = () => SteamNet.Ready });
                    _rows.Add(new Row { Label = "Find games on Steam", Press = () => { Show(Page.Browse); SteamNet.Search(); }, Enabled = () => SteamNet.Ready });
                    _rows.Add(new Row { Label = "Host by IP", Detail = "Same PC, home network, or Tailscale / ZeroTier", Press = () => Runner.HostIp() });
                    _rows.Add(new Row { Label = "Join by IP", Press = () => Show(Page.JoinIp) });
                    _rows.Add(new Row { Label = "Back", Press = () => Show(Page.Main) });
                    break;
                case Page.JoinIp:
                    _rows.Add(new Row { Label = "Address", TextField = true });
                    _rows.Add(new Row { Label = "Join", Press = () => { PlayerPrefs.SetString("join_address", _address); Runner.JoinIp(_address); } });
                    _rows.Add(new Row { Label = "Join this PC (127.0.0.1)", Detail = "Two copies of the game on one PC", Press = () => Runner.JoinIp("127.0.0.1") });
                    _rows.Add(new Row { Label = "Back", Press = () => Show(Page.Online) });
                    break;
                case Page.Browse:
                    foreach (var l in SteamNet.Found)
                    {
                        var id = l.Id;
                        var listing = l;
                        _rows.Add(new Row
                        {
                            Label = l.Name,
                            Detail = l.SameVersion ? l.Players + " / " + NetSession.MaxPlayers + " players" + (l.Playing ? ", match on" : "") + (l.Friend ? " (friend)" : "") : "Different version: you both need the latest update",
                            Press = () => Runner.JoinSteam(id),
                            Enabled = () => listing.SameVersion,
                        });
                    }
                    _rows.Add(new Row { Label = SteamNet.Searching ? "Searching..." : "Refresh", Press = SteamNet.Search, Enabled = () => !SteamNet.Searching });
                    _rows.Add(new Row { Label = "Back", Press = () => Show(Page.Online) });
                    break;
                case Page.OnlineLobby:
                    var s = Session;
                    if (s == null || !OptionsOpen) break;
                    if (s.IsHost)
                    {
                        if (SteamNet.InLobby) _rows.Add(new Row { Label = "Invite Steam friends", Press = InviteFriends });
                        _rows.Add(Cycle("Match mode", new[] { "Score", "Time" }, () => Settings.MatchMode, v => Settings.MatchMode = v));
                        _rows.Add(new Row
                        {
                            LabelNow = () => Settings.MatchMode == 1 ? "Time" : "Score to win",
                            Value = () => Settings.MatchMode == 1 ? Mathf.Min(Settings.MatchMinutes, 10) + ":00" : Mathf.Min(Settings.GoalsToWin, 10).ToString(),
                            Step = StepLength,
                            Press = () => StepLength(1),
                        });
                        if (Runner.MapCount > 1) _rows.Add(new Row { Label = "Rink", Value = () => Runner.MapName(Runner.MapIndex), Step = d => Runner.StepMap(d), Press = () => Runner.StepMap(1) });
                        _rows.Add(Cycle("Rounds mode", new[] { "Off", "On" }, () => Settings.Rounds ? 1 : 0, v => Settings.Rounds = v == 1));
                        _rows.Add(Cycle("Goal replays", new[] { "Off", "On" }, () => Settings.Replays ? 1 : 0, v => Settings.Replays = v == 1));
                        var picking = Cycle("Picking", Settings.PickModes, () => Settings.PickMode, v => Settings.PickMode = v);
                        picking.Enabled = () => Settings.Rounds;
                        _rows.Add(picking);
                        var offered = new string[Settings.OfferCounts.Length];
                        for (var k = 0; k < offered.Length; k++) offered[k] = Settings.OfferCounts[k].ToString();
                        _rows.Add(Cycle("Cards offered", offered, () => Mathf.Max(0, Array.IndexOf(Settings.OfferCounts, Settings.CardsOffered)), v => Settings.CardsOffered = Settings.OfferCounts[v]));
                        var times = new string[Settings.PickTimes.Length];
                        for (var k = 0; k < times.Length; k++) times[k] = Settings.PickTimes[k] == 0 ? "No limit" : Settings.PickTimes[k] + " s";
                        _rows.Add(Cycle("Pick time", times, () => Mathf.Max(0, Array.IndexOf(Settings.PickTimes, Settings.PickTime)), v => Settings.PickTime = Settings.PickTimes[v]));
                        _rows.Add(Cycle("Multiple utilities", new[] { "Off", "On" }, () => Settings.MultiUtility ? 1 : 0, v => Settings.MultiUtility = v == 1));
                        _rows.Add(Cycle("Carry-through", new[] { "Off", "On" }, () => Settings.CarryThrough ? 1 : 0, v => Settings.CarryThrough = v == 1));
                        var carry = new Row
                        {
                            Label = "Carry-through strength", Value = () => Settings.CarryPercent + "%",
                            Step = d => Settings.CarryPercent = Mathf.Clamp(Settings.CarryPercent + d * 5, 5, 100),
                            Press = () => Settings.CarryPercent = Settings.CarryPercent >= 100 ? 5 : Settings.CarryPercent + 5,
                        };
                        carry.Enabled = () => Settings.CarryThrough;
                        _rows.Add(carry);
                        _rows.Add(new Row
                        {
                            Label = "Knock-away", Value = () => Settings.KnockAwayPercent + "%",
                            Step = d => Settings.KnockAwayPercent = Mathf.Clamp(Settings.KnockAwayPercent + d * 25, 0, 500),
                            Press = () => Settings.KnockAwayPercent = Settings.KnockAwayPercent >= 500 ? 0 : Settings.KnockAwayPercent + 25,
                        });
                        _rows.Add(new Row { Label = "Bots", Value = () => s.Bots.ToString(), Step = d => s.Bots = Mathf.Clamp(s.Bots + d, 0, NetSession.MaxPlayers), Press = () => s.Bots = s.Bots >= NetSession.MaxPlayers - 1 ? 0 : s.Bots + 1 });
                    }
                    else
                    {
                        if (SteamNet.InLobby) _rows.Add(new Row { Label = "Invite Steam friends", Press = InviteFriends });
                        // everyone sees the host's match settings (only the host changes them)
                        var r = RulesView.Read(s.LobbyRules);
                        if (r != null)
                        {
                            _rows.Add(Shown("Match", () => r.MatchText));
                            if (Runner.MapCount > 1) _rows.Add(Shown("Rink", () => Runner.MapName(s.Map)));
                            _rows.Add(Shown("Rounds mode", () => r.Rounds ? "On" : "Off"));
                            _rows.Add(Shown("Goal replays", () => r.Replays ? "On" : "Off"));
                            _rows.Add(Shown("Picking", () => Settings.PickModes[Mathf.Clamp(r.PickMode, 0, Settings.PickModes.Length - 1)]));
                            _rows.Add(Shown("Cards offered", () => r.CardsOffered.ToString()));
                            _rows.Add(Shown("Pick time", () => r.PickTime == 0 ? "No limit" : r.PickTime + " s"));
                            _rows.Add(Shown("Multiple utilities", () => r.MultiUtility ? "On" : "Off"));
                            if (r.CardsOff > 0) _rows.Add(Shown("Cards switched off", () => r.CardsOff.ToString()));
                            _rows.Add(Shown("Carry-through", () => r.Carry ? "On, " + r.CarryPercent + "%" : "Off"));
                            _rows.Add(Shown("Knock-away", () => r.KnockPercent + "%"));
                        }
                    }
                    _rows.Add(new Row { Label = "Leave the game", Press = () => Runner.LeaveOnline(null) });
                    _rows.Add(new Row { Label = "Back", Press = () => OpenOptions(false) });
                    break;
                case Page.Friends:
                    if (Time.unscaledTime - _friendsAt > 3f) { _friends = SteamNet.OnlineFriends(); _friendsAt = Time.unscaledTime; }
                    foreach (var f in _friends)
                    {
                        var friend = f;
                        var sent = _invited.Contains(friend.Id.m_SteamID);
                        _rows.Add(new Row
                        {
                            Label = friend.Name,
                            Detail = sent ? "Invited" : friend.Playing ? "In Ultra Puck" : "Online",
                            Press = () => { if (SteamNet.InviteFriend(friend.Id)) _invited.Add(friend.Id.m_SteamID); },
                        });
                    }
                    if (SteamNet.OverlayWorks) _rows.Add(new Row { Label = "Open the Steam overlay", Press = SteamNet.Invite });
                    _rows.Add(new Row { Label = "Back", Press = () => Show(Page.OnlineLobby) });
                    break;
            }
        }

        /// <summary>A setting to look at (the host's, on someone else's PC): its value, nothing to change.</summary>
        private static Row Shown(string label, Func<string> value) { return new Row { Label = label, Shown = value }; }

        /// <summary>A row that steps through a few named values (left / right, or press for the next).</summary>
        private static Row Cycle(string label, string[] names, Func<int> get, Action<int> set)
        {
            Action<int> step = d => set((get() + d + names.Length) % names.Length);
            return new Row { Label = label, Value = () => names[Mathf.Clamp(get(), 0, names.Length - 1)], Step = step, Press = () => step(1) };
        }

        /// <summary>The match's length: 1 to 10 goals, or 1:00 to 10:00 (wrapping round).</summary>
        private static void StepLength(int d)
        {
            if (Settings.MatchMode == 1) Settings.MatchMinutes = (Mathf.Clamp(Settings.MatchMinutes, 1, 10) - 1 + d + 10) % 10 + 1;
            else Settings.GoalsToWin = (Mathf.Clamp(Settings.GoalsToWin, 1, 10) - 1 + d + 10) % 10 + 1;
        }

        private UnityEngine.InputSystem.InputDevice _lastDevice;
        private List<SteamNet.Friend> _friends = new List<SteamNet.Friend>();
        private float _friendsAt;
        private int _scroll;
        // the focus the scroll last followed: the list only jumps to the focus when it moves (the wheel scrolls freely)
        private int _scrollFocus = -1;
        private float _scrollY;
        private readonly HashSet<ulong> _invited = new HashSet<ulong>();

        // ------------------------------------------------------------------ input

        public void Tick()
        {
            if (Current == Page.None) return;
            if (Current == Page.Title)
            {
                // anything at all: a key, a mouse button, any button on any controller
                var pressed = LocalPlayers.AnyButton();
                if (pressed != null && Time.unscaledTime - _titleAt > 0.5f)
                {
                    Debug.Log("Ultra Puck: title screen: a button on " + pressed.displayName + (TitleStays ? " (ignored: taking screenshots)" : ""));
                    if (TitleStays) return;
                    if (!(pressed is UnityEngine.InputSystem.Mouse)) _lastDevice = pressed; // free play starts on the device last used
                    LeaveTitle();
                }
                return;
            }
            if (Current == Page.OnlineLobby && !OptionsOpen)
            {
                // the lobby itself: joining and teams are the players' own controls; Select / Tab opens the options
                if (Local.SettingsPressed()) { Sound.Select(); OpenOptions(true); }
                return;
            }
            if (Current == Page.OnlineLobby && Local.SettingsPressed()) { Sound.Back(); OpenOptions(false); return; }
            BuildRows();
            var m = Local.Menu();
            if (m.Device != null) _lastDevice = m.Device;
            if (_typing)
            {
                // typing an address: the keyboard is for text, so only Enter, Escape, Tab and the arrows do anything
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) { _typing = false; _focus = 1; Sound.Select(); _rows[1].Press(); return; }
                if (kb != null && (kb.escapeKey.wasPressedThisFrame)) { Sound.Back(); Show(Page.Online); return; }
                if (kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)) { _typing = false; _focus = 1; Sound.Move(); }
                return;
            }
            if (m.Back)
            {
                if (Current != Page.Main) Sound.Back(); // the first page has nowhere to go back to
                if (Current == Page.Online) Show(Page.Main);
                else if (Current == Page.JoinIp || Current == Page.Browse) Show(Page.Online);
                else if (Current == Page.Friends) Show(Page.OnlineLobby);
                else if (Current == Page.OnlineLobby) OpenOptions(false);
                return;
            }
            if (_rows.Count == 0) return;
            if (m.Y != 0)
            {
                var was = _focus;
                for (var k = 0; k < _rows.Count; k++)
                {
                    _focus = (_focus - m.Y + _rows.Count) % _rows.Count;
                    if (_rows[_focus].Enabled()) break;
                }
                if (_focus != was) Sound.Move();
            }
            _focus = Mathf.Clamp(_focus, 0, _rows.Count - 1);
            var row = _rows[_focus];
            if (!row.Enabled()) return;
            if (row.TextField && m.Confirm) { Sound.Select(); _typing = true; return; }
            if (m.X != 0 && row.Step != null) { row.Step(m.X); Sound.Step(m.X); }
            else if (m.Confirm && row.Press != null) { PressSound(row); row.Press(); }
        }

        /// <summary>The sound for pressing a row: going back or leaving sounds different from choosing.</summary>
        private static void PressSound(Row row) { if (row.Label == "Back" || row.Label == "Leave the game") Sound.Back(); else Sound.Select(); }

        // ------------------------------------------------------------------ drawing

        private void OnGUI()
        {
            if (Current == Page.None) return;
            GUI.depth = 2;
            Ui.Begin();
            BuildRows();
            var t = Time.unscaledTime - _openedAt;
            if (Current == Page.Title) { TitleScreen.Draw(Time.unscaledTime - _titleAt, 0f); return; }

            // the main page shows the live match behind; the other pages a plain backdrop (it's too busy there)
            if (Current != Page.Main) Ui.Box(new Rect(0f, 0f, Ui.W, Ui.H), Palette.Background, 0f);
            if (Current == Page.OnlineLobby) { Ui.Flat = true; try { Lobby(t); } finally { Ui.Flat = false; } return; }
            // straight from the title screen: the panel slides in from the left while the name flies up to its place
            var leave = (Time.unscaledTime - _leftTitleAt) / TitleScreen.LeaveTime;
            var arriving = Current == Page.Main && leave < 1f;
            var slide = arriving ? (1f - Ui.EaseOut(leave)) * -740f : 0f;
            Ui.Box(new Rect(slide, 0f, 620f, Ui.H), new Color(0f, 0f, 0f, 0.62f), 0f);

            var x = TitleScreen.MenuX;
            if (!arriving) TitleScreen.Logo(x, TitleScreen.MenuY, 1f, 1f);
            // the shadow as tight to these small letters as the name's is to its big ones (the usual 1.5 / 2 sat apart from them)
            Ui.Label(new Rect(x + 4f, 150f, 600f, 30f), Subtitle(), Ui.Font(18), Ui.TextDim, new Vector2(0.6f, 0.8f));

            // the main page's choices sit right under the title (the other pages have a line of subtitle there)
            Rows(x, Current == Page.Main ? 160f : 220f, 460f, 64f, 10f, 26, t, Current == Page.Friends ? Ui.H - 130f : float.MaxValue);

            var status = Status();
            if (!string.IsNullOrEmpty(status)) Ui.Label(new Rect(x, Ui.H - 120f, 540f, 80f), status, WrapStyle(), new Color(1f, 1f, 1f, 0.65f));
            if (!string.IsNullOrEmpty(Message))
            {
                Ui.Box(new Rect(x - 10f, Ui.H - 200f, 540f, 70f), new Color(0.55f, 0.15f, 0.15f, 0.85f), 12f);
                Ui.Label(new Rect(x + 6f, Ui.H - 198f, 510f, 66f), Message, WrapStyle(), Color.white);
            }

            Glyphs.Hint(new Rect(x, Ui.H - 38f, 900f, 28f), Hint(), 15, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleLeft);
            Ui.Label(new Rect(Ui.W - 220f, Ui.H - 36f, 200f, 24f), "v" + Updater.Current, Ui.Font(14, false, TextAnchor.MiddleRight), new Color(1f, 1f, 1f, 0.35f));
            // the title screen on its way out, over the top
            if (arriving) TitleScreen.Draw(Time.unscaledTime - _titleAt, leave);
        }

        /// <summary>
        /// The online lobby: the same players screen as on this PC (the two teams side by side). Players join, pick a
        /// team and ready up with their own controls; readied, the host's people work on the bots. The host's settings,
        /// inviting and leaving are in an options panel (Select / Tab), so the stick only ever does one thing at a time.
        /// </summary>
        private void Lobby(float t)
        {
            float w = Ui.W, h = Ui.H;
            var s = Session;
            var appear = Ui.EaseOut(t / 0.3f);
            PlayScreens.Title(w, "PLAYERS", Subtitle(), appear);
            var info = Status();
            if (!string.IsNullOrEmpty(info)) Ui.Label(new Rect(40f, 110f, w - 80f, 24f), info, Ui.Font(15, false, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.45f * appear));

            var tiles = new List<PlayScreens.Tile>();
            if (s != null)
            {
                var bot = 0;
                foreach (var seat in s.Seats)
                {
                    var mine = !seat.Bot && seat.Peer == s.MyPeer;
                    var device = mine && seat.Local >= 0 && seat.Local < Local.Slots.Count ? Local.Slots[seat.Local].Device : null;
                    tiles.Add(new PlayScreens.Tile
                    {
                        Team = (seat.Colour & 7) == 1 ? 1 : 0, Symbol = seat.Symbol, Name = string.IsNullOrEmpty(seat.Name) ? (seat.Bot ? "Bot" : "Player") : seat.Name,
                        Tag = seat.Bot ? "Bot" : mine ? (seat.Peer == 0 ? "You  ·  host" : "You") : seat.Peer == 0 ? "Host" : "Online",
                        Mine = mine, Ready = seat.Ready, Bot = seat.Bot, Device = device, BotIndex = seat.Bot ? bot++ : -1,
                    });
                }
                if (s.IsHost) PlayScreens.MarkCursors(tiles, Local);
            }
            var canJoin = s != null && s.Current != NetSession.Stage.Connecting && Local.Slots.Count < LocalPlayers.MaxPlayers;
            PlayScreens.TeamsView(w, 142f + (1f - appear) * 30f, h - 150f, tiles, canJoin, appear);

            // what happens next: start (host), or waiting; and the options
            var fy = h - 128f;
            if (s != null && s.IsHost)
            {
                var ready = s.Seats.Count >= 2 && !s.TooManyTeams && s.AllReady;
                var text = ready ? "{start} Start match" : s.Seats.Count < 2 ? "Need 2 players to start" : !s.AllReady ? "Waiting for everyone to ready up" : "Only two teams";
                if (PlayScreens.Chip(new Rect(w / 2f - 290f, fy, 280f, 46f), text, ready, true, !OptionsOpen) && ready) { Sound.Select(); Runner.StartOnlineMatch(); }
                if (PlayScreens.Chip(new Rect(w / 2f + 10f, fy, 240f, 46f), "{settings} Options", true, false, !OptionsOpen)) { Sound.Select(); OpenOptions(true); }
            }
            else
            {
                Ui.Label(new Rect(w / 2f - 300f, fy, 290f, 46f), "Waiting for the host to start", Ui.Font(17, false, TextAnchor.MiddleRight), new Color(1f, 1f, 1f, 0.55f));
                if (PlayScreens.Chip(new Rect(w / 2f + 10f, fy, 240f, 46f), "{settings} Options", true, false, !OptionsOpen)) { Sound.Select(); OpenOptions(true); }
            }

            if (!string.IsNullOrEmpty(Message))
            {
                Ui.Box(new Rect(w / 2f - 300f, fy - 84f, 600f, 70f), new Color(0.55f, 0.15f, 0.15f, 0.92f), 12f);
                Ui.Label(new Rect(w / 2f - 286f, fy - 82f, 572f, 66f), Message, WrapStyle(), Color.white);
            }

            Hud.LeavingBar(new Rect(w / 2 - 150f, h - 100f, 300f, 22f), Local.NobodyLeaving, "Leaving the game");
            PlayScreens.HintLines(w, h, PlayScreens.LobbyHint(Local, s != null && s.IsHost));

            if (OptionsOpen) OptionsPanel(w, h);
        }

        /// <summary>The lobby's options: a panel over the players with the host's settings, inviting and leaving.</summary>
        private void OptionsPanel(float w, float h)
        {
            var a = Ui.EaseOut((Time.unscaledTime - _optionsAt) / 0.2f);
            Ui.Box(new Rect(0f, 0f, w, h), new Color(0f, 0f, 0f, 0.6f * a), 0f);
            const float rowH = 46f, gap = 6f, pw = 520f;
            var ph = Mathf.Min(96f + _rows.Count * (rowH + gap) + 40f, h - 60f);
            var p = new Rect(w / 2f - pw / 2f, h / 2f - ph / 2f + (1f - a) * 24f, pw, ph);
            Ui.Box(p, new Color(0.09f, 0.11f, 0.15f, 0.97f), 20f);
            Ui.Label(new Rect(p.x + 30f, p.y + 18f, pw - 60f, 44f), "Options", Ui.Font(28, true), Color.white);
            Rows(p.x + 20f, p.y + 76f, pw - 64f, rowH, gap, 19, Time.unscaledTime - _optionsAt, p.yMax - 44f);
            Glyphs.Hint(new Rect(p.x, p.yMax - 40f, pw, 28f), "{confirm} Choose      {side} Change      {back} Close", 14, new Color(1f, 1f, 1f, 0.55f));
            if (!p.Contains(Ui.Mouse) && Ui.TakeClick()) { Sound.Back(); OpenOptions(false); }
        }

        /// <summary>The page's buttons, in a column at (<paramref name="x"/>, <paramref name="top"/>).</summary>
        private void Rows(float x, float top, float w, float rowH, float gap, int font, float t, float bottom = float.MaxValue)
        {
            // straight from the title screen: the mouse does nothing until the menu is in place
            var mouse = MouseLocked() ? new Vector2(-10000f, -10000f) : Ui.Mouse;
            // more rows than fit (a long friends list): scroll so the highlighted one is in view
            var fit = bottom >= float.MaxValue ? _rows.Count : Mathf.Max(1, Mathf.FloorToInt((bottom - top + gap) / (rowH + gap)));
            if (_rows.Count <= fit) _scroll = 0;
            else
            {
                if (_focus != _scrollFocus)
                {
                    if (_focus < _scroll) _scroll = _focus;
                    if (_focus >= _scroll + fit) _scroll = _focus - fit + 1;
                    _scrollFocus = _focus;
                }
                // the mouse wheel scrolls the list (the focus stays where it is)
                if (Event.current.type == EventType.ScrollWheel)
                {
                    _scroll += Event.current.delta.y > 0 ? 1 : -1;
                    Event.current.Use();
                }
                _scroll = Mathf.Clamp(_scroll, 0, _rows.Count - fit);
            }
            _scrollY = Mathf.Lerp(_scrollY, _scroll, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 18f));
            if (Mathf.Abs(_scrollY - _scroll) < 0.01f) _scrollY = _scroll;
            top -= _scrollY * (rowH + gap);
            if (_rows.Count > fit)
            {
                // a scrollbar down the right
                var trackH = fit * (rowH + gap) - gap;
                var trackTop = top + _scrollY * (rowH + gap);
                Ui.Box(new Rect(x + w + 12f, trackTop, 6f, trackH), new Color(1f, 1f, 1f, 0.1f), 3f);
                var thumbH = trackH * fit / _rows.Count;
                Ui.Box(new Rect(x + w + 12f, trackTop + (trackH - thumbH) * _scrollY / (_rows.Count - fit), 6f, thumbH), new Color(1f, 1f, 1f, 0.5f), 3f);
            }
            for (var i = 0; i < _rows.Count; i++)
            {
                if (_rows.Count > fit && (i < _scroll || i >= _scroll + fit)) continue;
                var r = new Rect(x, top + i * (rowH + gap), w, rowH);
                if (r.Contains(mouse) && (Ui.MouseMoved || Ui.Clicked) && _rows[i].Enabled()) { if (_focus != i && Ui.MouseMoved) Sound.Move(); _focus = i; if (!_rows[i].TextField) _typing = false; }
            }
            _focus = Mathf.Clamp(_focus, 0, Mathf.Max(0, _rows.Count - 1));
            var target = top + _focus * (rowH + gap);
            _focusY = _focusY < 0f ? target : Mathf.Lerp(_focusY, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 22f));
            if (_rows.Count > 0)
            {
                Ui.Box(new Rect(x, _focusY, w, rowH), new Color(1f, 1f, 1f, 0.12f), 12f);
            }

            for (var i = 0; i < _rows.Count; i++)
            {
                if (_rows.Count > fit && (i < Mathf.FloorToInt(_scrollY) || i > Mathf.CeilToInt(_scrollY) + fit - 1)) continue;
                var row = _rows[i];
                // rows slide in one after another when a page opens
                var appear = Ui.EaseOut((t - i * 0.04f) / 0.3f);
                var r = new Rect(x + (1f - appear) * -40f, top + i * (rowH + gap), w, rowH);
                var on = row.Enabled();
                var col = on ? (i == _focus ? Color.white : new Color(1f, 1f, 1f, 0.82f)) : new Color(1f, 1f, 1f, 0.3f);
                col.a *= appear;
                if (row.TextField) { AddressField(r, i == _focus); continue; }
                var label = row.LabelNow != null ? row.LabelNow() : row.Label;
                if (row.Value != null) label += ":   ‹  " + row.Value() + "  ›";
                else if (row.Shown != null) { label += ":   " + row.Shown(); col.a *= 0.8f; }
                var detail = row.Detail != null && rowH >= 60f;
                var pad = rowH >= 60f ? 26f : 16f;
                Ui.Label(new Rect(r.x + pad, r.y + (detail ? 6f : 0f), r.width - pad - 10f, detail ? 36f : r.height), label, Ui.Font(font, i == _focus), col);
                if (detail) Ui.Label(new Rect(r.x + pad + 1f, r.y + 36f, r.width - 40f, 22f), row.Detail, Ui.Font(14), new Color(1f, 1f, 1f, 0.5f * appear));
                if (on && r.Contains(mouse) && Time.frameCount != _openedFrame && Ui.TakeClick())
                {
                    if (row.Step != null) { var d = mouse.x < r.center.x ? -1 : 1; row.Step(d); Sound.Step(d); }
                    else if (row.Press != null) { PressSound(row); row.Press(); }
                }
            }
        }

        private GUIStyle _wrap;
        private GUIStyle WrapStyle()
        {
            if (_wrap == null) { _wrap = new GUIStyle(Ui.Font(16)) { wordWrap = true, alignment = TextAnchor.MiddleLeft }; }
            return _wrap;
        }

        private void AddressField(Rect r, bool focused)
        {
            Ui.Box(r, new Color(1f, 1f, 1f, _typing ? 0.16f : 0.06f), 12f);
            Ui.Label(new Rect(r.x + 26f, r.y, 140f, r.height), "Address", Ui.Font(20, focused), Ui.TextDim);
            GUI.SetNextControlName("address");
            var style = new GUIStyle(Ui.Font(24)) { normal = { textColor = Color.white } };
            style.focused.textColor = Color.white;
            _address = GUI.TextField(new Rect(r.x + 150f, r.y + 14f, r.width - 170f, r.height - 28f), _address ?? "", 64, style);
            if (_typing) GUI.FocusControl("address");
            else if (GUI.GetNameOfFocusedControl() == "address") GUI.FocusControl(null);
            if (r.Contains(Ui.Mouse) && Ui.TakeClick()) _typing = true;
        }

        private string Subtitle()
        {
            switch (Current)
            {
                case Page.Online: return "Online";
                case Page.JoinIp: return "Join by IP";
                case Page.Browse: return "Games on Steam";
                case Page.Friends: return "Invite friends";
                case Page.OnlineLobby: return Session == null ? "" : Session.Current == NetSession.Stage.Connecting ? "Connecting..." : Session.IsHost ? "Your online game" : "Online game";
                default: return "";
            }
        }

        private string Status()
        {
            switch (Current)
            {
                case Page.Online:
                    return SteamNet.Ready ? "Signed in to Steam as " + SteamNet.Name + "." : (SteamNet.Problem ?? "Steam isn't available.");
                case Page.JoinIp:
                    return "The host's address, with :port if it isn't " + UdpTransport.DefaultPort + ". On the same PC use 127.0.0.1; on a home network, the host's 192.168... address; across the internet, a Tailscale or ZeroTier address (or forward UDP port " + UdpTransport.DefaultPort + ").";
                case Page.Browse:
                    return SteamNet.Found.Count == 0 && !SteamNet.Searching ? "No open games found. Friends can also invite you from Steam." : "";
                case Page.Friends:
                    return _friends.Count == 0 ? "None of your Steam friends are online." : "";
                case Page.OnlineLobby:
                    if (Session == null) return "";
                    var s = Session.Describe();
                    if (Session.IsHost && !SteamNet.InLobby)
                    {
                        var ips = UdpTransport.LocalAddresses();
                        if (ips.Count > 0) s += ". Others join " + string.Join(" or ", ips.ToArray());
                    }
                    if (Session.Connected > 0) s += ".   Ping " + Mathf.RoundToInt(Session.Ping * 1000f) + " ms";
                    if (Runner.MapCount > 1) s += ".   Rink: " + Runner.MapName(Session.Map);
                    if (Session.TooManyTeams) s += ".   Only two teams: red and blue";
                    if (Session.IsHost) s += ".   " + Settings.MatchText;
                    else
                    {
                        var rules = RulesView.Read(Session.LobbyRules);
                        if (rules != null) s += ".   " + rules.MatchText;
                        if (Session.HostPlaying) s += ".   A match is on: ready up to join it";
                    }
                    return s;
                default: return "";
            }
        }

        private string Hint()
        {
            switch (Current)
            {
                case Page.OnlineLobby: return "{join} Join      {side} Switch team      {shape} Symbol      {leave} Leave seat      {settings} Options";
                case Page.JoinIp: return _typing ? "Type the address, then Enter      Esc: back" : "{confirm} Choose      {back} Back";
                case Page.Main: return "";
                case Page.Title: return "";
                default: return "{confirm} Choose      {back} Back";
            }
        }
    }
}
