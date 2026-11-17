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
        public enum Page { None, Main, Online, JoinIp, Browse, OnlineLobby, Friends }

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
            public Action Press;
            public Action<int> Step;
            public Func<string> Value;            // a choice's current value, shown as  ‹ value ›
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
            if (page != Current) { _focus = 0; _focusY = -1f; _openedAt = Time.unscaledTime; }
            Current = page;
            _typing = page == Page.JoinIp;
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
                            Detail = l.SameVersion ? l.Players + " / " + NetSession.MaxPlayers + " players" + (l.Friend ? " (friend)" : "") : "Different version: you both need the latest update",
                            Press = () => Runner.JoinSteam(id),
                            Enabled = () => listing.SameVersion,
                        });
                    }
                    _rows.Add(new Row { Label = SteamNet.Searching ? "Searching..." : "Refresh", Press = SteamNet.Search, Enabled = () => !SteamNet.Searching });
                    _rows.Add(new Row { Label = "Back", Press = () => Show(Page.Online) });
                    break;
                case Page.OnlineLobby:
                    var s = Session;
                    if (s == null) break;
                    if (s.IsHost)
                    {
                        _rows.Add(new Row { Label = "Start match", Press = () => Runner.StartOnlineMatch(), Enabled = () => Session != null && Session.Seats.Count >= 2 });
                        _rows.Add(new Row { Label = "Bots", Detail = null, Value = () => s.Bots.ToString(), Step = d => s.Bots = Mathf.Clamp(s.Bots + d, 0, NetSession.MaxPlayers), Press = () => s.Bots = s.Bots >= NetSession.MaxPlayers - 1 ? 0 : s.Bots + 1 });
                        _rows.Add(new Row { Label = "Match", Value = () => Settings.MatchText, Step = Settings.StepMatch, Press = () => Settings.StepMatch(1) });
                        if (Runner.MapCount > 1) _rows.Add(new Row { Label = "Rink", Value = () => Runner.MapName(Runner.MapIndex), Step = d => Runner.StepMap(d), Press = () => Runner.StepMap(1) });
                        if (SteamNet.InLobby) _rows.Add(new Row { Label = "Invite Steam friends", Press = () => { _friends = SteamNet.OnlineFriends(); _friendsAt = Time.unscaledTime; Show(Page.Friends); } });
                    }
                    _rows.Add(new Row { Label = "Leave", Press = () => Runner.LeaveOnline(null) });
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
                        if (_rows.Count >= 7) break; // the list fits the screen; the most likely friends come first
                    }
                    if (SteamNet.OverlayWorks) _rows.Add(new Row { Label = "Open the Steam overlay", Press = SteamNet.Invite });
                    _rows.Add(new Row { Label = "Back", Press = () => Show(Page.OnlineLobby) });
                    break;
            }
        }

        private UnityEngine.InputSystem.InputDevice _lastDevice;
        private List<SteamNet.Friend> _friends = new List<SteamNet.Friend>();
        private float _friendsAt;
        private readonly HashSet<ulong> _invited = new HashSet<ulong>();

        // ------------------------------------------------------------------ input

        public void Tick()
        {
            if (Current == Page.None) return;
            BuildRows();
            // in the online lobby only people who have joined drive the buttons (pressing A to join mustn't press one)
            var m = Current == Page.OnlineLobby || Current == Page.Friends ? Local.MenuJoined() : Local.Menu();
            if (m.Device != null) _lastDevice = m.Device;
            if (_typing)
            {
                // typing an address: the keyboard is for text, so only Enter, Escape, Tab and the arrows do anything
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) { _typing = false; _focus = 1; _rows[1].Press(); return; }
                if (kb != null && (kb.escapeKey.wasPressedThisFrame)) { Show(Page.Online); return; }
                if (kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)) { _typing = false; _focus = 1; }
                return;
            }
            if (m.Back)
            {
                if (Current == Page.Online) Show(Page.Main);
                else if (Current == Page.JoinIp || Current == Page.Browse) Show(Page.Online);
                else if (Current == Page.Friends) Show(Page.OnlineLobby);
                return;
            }
            if (_rows.Count == 0) return;
            if (m.Y != 0)
                for (var k = 0; k < _rows.Count; k++)
                {
                    _focus = (_focus - m.Y + _rows.Count) % _rows.Count;
                    if (_rows[_focus].Enabled()) break;
                }
            _focus = Mathf.Clamp(_focus, 0, _rows.Count - 1);
            var row = _rows[_focus];
            if (!row.Enabled()) return;
            if (row.TextField && m.Confirm) { _typing = true; return; }
            if (m.X != 0 && row.Step != null) row.Step(m.X);
            else if (m.Confirm && row.Press != null) row.Press();
        }

        // ------------------------------------------------------------------ drawing

        private void OnGUI()
        {
            if (Current == Page.None) return;
            GUI.depth = 2;
            Ui.Begin();
            BuildRows();
            var t = Time.unscaledTime - _openedAt;

            // the main page shows the live match behind; the other pages a plain backdrop (it's too busy there)
            if (Current != Page.Main) Ui.Box(new Rect(0f, 0f, Ui.W, Ui.H), Palette.Background, 0f);
            if (Current == Page.OnlineLobby) { Lobby(t); return; }
            Ui.Box(new Rect(0f, 0f, 620f, Ui.H), new Color(0f, 0f, 0f, 0.62f), 0f);
            Ui.Box(new Rect(620f, 0f, 120f, Ui.H), new Color(0f, 0f, 0f, 0.3f), 0f);

            var x = 80f;
            Ui.Label(new Rect(x, 70f, 600f, 90f), "ULTRA PUCK", Ui.Font(76, true), Color.white);
            if (Current == Page.Main) Ui.Label(new Rect(x + 4f, 46f, 300f, 26f), "v" + Updater.Current, Ui.Font(16, true), new Color(1f, 1f, 1f, 0.5f));
            Ui.Label(new Rect(x + 4f, 150f, 600f, 30f), Subtitle(), Ui.Font(18), Ui.TextDim);

            Rows(x, 220f, 460f, 64f, 10f, 26, t);

            var status = Status();
            if (!string.IsNullOrEmpty(status)) Ui.Label(new Rect(x, Ui.H - 120f, 540f, 80f), status, WrapStyle(), new Color(1f, 1f, 1f, 0.65f));
            if (!string.IsNullOrEmpty(Message))
            {
                Ui.Box(new Rect(x - 10f, Ui.H - 200f, 540f, 70f), new Color(0.55f, 0.15f, 0.15f, 0.85f), 12f);
                Ui.Label(new Rect(x + 6f, Ui.H - 198f, 510f, 66f), Message, WrapStyle(), Color.white);
            }

            Glyphs.Hint(new Rect(x, Ui.H - 38f, 900f, 28f), Hint(), 15, new Color(1f, 1f, 1f, 0.55f), TextAnchor.MiddleLeft);
            Ui.Label(new Rect(Ui.W - 220f, Ui.H - 36f, 200f, 24f), "v" + Updater.Current, Ui.Font(14, false, TextAnchor.MiddleRight), new Color(1f, 1f, 1f, 0.35f));
        }

        /// <summary>The online lobby: like the local join screen, a card per player, with the options down the left.</summary>
        private void Lobby(float t)
        {
            float w = Ui.W, h = Ui.H;
            var s = Session;
            PlayScreens.Title(w, "PLAYERS", Subtitle() + "   ·   red against blue", Ui.EaseOut(t / 0.3f));
            Rows(36f, 140f, 300f, 48f, 8f, 19, t);

            var status = Status();
            if (!string.IsNullOrEmpty(status)) Ui.Label(new Rect(36f, h - 230f, 300f, 140f), status, WrapStyle(), new Color(1f, 1f, 1f, 0.6f));
            if (!string.IsNullOrEmpty(Message))
            {
                Ui.Box(new Rect(30f, h - 330f, 312f, 90f), new Color(0.55f, 0.15f, 0.15f, 0.85f), 12f);
                Ui.Label(new Rect(42f, h - 326f, 290f, 82f), Message, WrapStyle(), Color.white);
            }

            var area = new Rect(360f, 128f, w - 390f, h - 128f - 96f);
            for (var k = 0; k < NetSession.MaxPlayers; k++)
            {
                var r = PlayScreens.CardRect(area, k, NetSession.MaxPlayers);
                if (s == null || k >= s.Seats.Count) { PlayScreens.EmptyCard(r, "P" + (k + 1), k, s != null && s.Current != NetSession.Stage.Connecting); continue; }
                var seat = s.Seats[k];
                var mine = !seat.Bot && seat.Peer == s.MyPeer;
                var device = mine && seat.Local >= 0 && seat.Local < Local.Slots.Count ? Local.Slots[seat.Local].Device : null;
                PlayScreens.Card(r, seat.Bot ? "CPU" : "P" + (k + 1), seat.Colour, seat.Symbol, seat.Name, mine,
                    mine ? "{profile} Name   {shape} Symbol" : seat.Bot ? "Bot" : "Online", mine, device);
            }

            Hud.LeavingBar(new Rect(w / 2 - 150f, h - 84f, 300f, 24f), Local.NobodyLeaving, "Leaving the game");
            Glyphs.Hint(new Rect(0f, h - 44f, w, 28f), Hint(), 15, new Color(1f, 1f, 1f, 0.6f));
        }

        /// <summary>The page's buttons, in a column at (<paramref name="x"/>, <paramref name="top"/>).</summary>
        private void Rows(float x, float top, float w, float rowH, float gap, int font, float t)
        {
            var mouse = Ui.Mouse;
            for (var i = 0; i < _rows.Count; i++)
            {
                var r = new Rect(x, top + i * (rowH + gap), w, rowH);
                if (r.Contains(mouse) && Ui.MouseMoved && _rows[i].Enabled()) { _focus = i; if (!_rows[i].TextField) _typing = false; }
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
                var row = _rows[i];
                // rows slide in one after another when a page opens
                var appear = Ui.EaseOut((t - i * 0.04f) / 0.3f);
                var r = new Rect(x + (1f - appear) * -40f, top + i * (rowH + gap), w, rowH);
                var on = row.Enabled();
                var col = on ? (i == _focus ? Color.white : new Color(1f, 1f, 1f, 0.82f)) : new Color(1f, 1f, 1f, 0.3f);
                col.a *= appear;
                if (row.TextField) { AddressField(r, i == _focus); continue; }
                var label = row.Label;
                if (row.Value != null) label += ":   ‹  " + row.Value() + "  ›";
                var detail = row.Detail != null && rowH >= 60f;
                var pad = rowH >= 60f ? 26f : 16f;
                Ui.Label(new Rect(r.x + pad, r.y + (detail ? 6f : 0f), r.width - pad - 10f, detail ? 36f : r.height), label, Ui.Font(font, i == _focus), col);
                if (detail) Ui.Label(new Rect(r.x + pad + 1f, r.y + 36f, r.width - 40f, 22f), row.Detail, Ui.Font(14), new Color(1f, 1f, 1f, 0.5f * appear));
                if (on && Ui.Clicked && r.Contains(mouse) && Event.current.type == EventType.Repaint)
                {
                    if (row.Step != null) row.Step(mouse.x < r.center.x ? -1 : 1);
                    else if (row.Press != null) row.Press();
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
            if (Ui.Clicked && r.Contains(Ui.Mouse) && Event.current.type == EventType.Repaint) _typing = true;
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
                    var help = _friends.Count == 0 ? "None of your Steam friends are online." : "The invite arrives in their Steam chat. If they have Ultra Puck open, accepting joins your game; they can also use Online > Find games on Steam, or Join game on you in their Steam friends list.";
                    if (!SteamNet.OverlayWorks) help += " (The Steam overlay only works when Steam starts the game: add UltraPuck.exe with Games > Add a Non-Steam Game and start it from your Steam library.)";
                    return help;
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
                    return s;
                default: return "";
            }
        }

        private string Hint()
        {
            switch (Current)
            {
                case Page.OnlineLobby: return "{join} Join      {side} Red or blue      {profile} Name      {shape} Symbol      {leave} Leave seat      Hold {leave} Leave game";
                case Page.JoinIp: return _typing ? "Type the address, then Enter      Esc: back" : "{confirm} Choose      {back} Back";
                case Page.Main: return "{confirm} Choose";
                default: return "{confirm} Choose      {back} Back";
            }
        }
    }
}
