using System.Collections.Generic;
using UltraPuck.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UltraPuck.Game
{
    /// <summary>
    /// Setting up a match on this PC, one screen after another:
    /// <list type="number">
    /// <item>Join: everyone presses A (or Space) to get a player card. Left / right picks a team colour (the same
    /// colour = the same team), Triangle / Y types a name on an on-screen keyboard, Square / X changes the player's
    /// symbol, LB / RB takes away or adds a bot. Start / Enter carries on.</item>
    /// <item>Rink: pick the map.</item>
    /// <item>Rules: first to so many goals, or a time limit.</item>
    /// </list>
    /// B goes back a screen; on the join screen, B gives up your card and holding it (with no card) leaves.
    /// </summary>
    internal sealed class PlayScreens : MonoBehaviour
    {
        public enum Page { None, Join, Map, Rules }
        public Page Current { get; private set; }

        public GameRunner Runner;
        public LocalPlayers Local;

        private float _openedAt;
        private string _problem;
        private float _problemAt;
        private int _rulesRow;

        public void Open(Page page)
        {
            if (page != Current) _openedAt = Time.unscaledTime;
            Current = page;
            _problem = null;
            CloseKeyboard(false);
        }

        // ------------------------------------------------------------------ input

        /// <summary>One frame of the screens. Returns false if they're closed.</summary>
        public bool Tick()
        {
            if (_typist != null) { KeyboardTick(); return true; }
            if (Current == Page.None) return false;
            switch (Current)
            {
                case Page.Join: JoinTick(); break;
                case Page.Map: MapTick(); break;
                case Page.Rules: RulesTick(); break;
            }
            return true;
        }

        private void JoinTick()
        {
            if (Local.SettingsPressed()) { Sound.Select(); Runner.OpenSettings(); return; }
            Local.LobbyUpdate();
            if (Local.LeaveLobbyHeld()) { Sound.Back(); Runner.GoMainMenu(); return; }
            if (Local.HumanCount > 0 && Local.StartPressed()) Continue();
        }

        private void Continue()
        {
            {
                if (!Local.AllReady) { Problem("Everyone presses " + (InputMode.Pad ? "A" : "Space") + " when they're ready."); return; }
                var colours = Local.AssignTeams();
                if (colours > 2) Problem("Only two teams: red and blue.");
                else if (colours < 2 && Local.Slots.Count > 1) Problem("Everyone's on the same team: pick another colour, or add a bot with " + (InputMode.Pad ? "RB" : "="));
                else if (Local.Slots.Count < 2) Problem("Add a bot (" + (InputMode.Pad ? "RB" : "=") + ") or another player to play against.");
                else { Sound.Select(); Open(Page.Map); }
            }
        }

        private void MapTick()
        {
            var m = Local.MenuJoined();
            if (m.Back) { Sound.Back(); Open(Page.Join); return; }
            if (m.X != 0) { Runner.StepMap(m.X); Sound.Step(m.X); }
            if (m.Confirm || Local.StartPressed()) { Sound.Select(); Open(Page.Rules); }
        }

        private void RulesTick()
        {
            var m = Local.MenuJoined();
            if (m.Back) { Sound.Back(); Open(Page.Map); return; }
            if (m.Y != 0) { _rulesRow = (_rulesRow + 2 - m.Y) % 2; Sound.Move(); }
            if (m.X != 0)
            {
                Settings.MatchMode = _rulesRow;
                if (_rulesRow == 0) Settings.GoalsToWin += m.X;
                else Settings.MatchMinutes += m.X;
                Sound.Step(m.X);
            }
            if (m.Confirm || Local.StartPressed())
            {
                Sound.Select();
                Settings.MatchMode = _rulesRow;
                PlayerPrefs.Save();
                Open(Page.None);
                Runner.StartLocalMatch();
            }
        }

        private void Problem(string text) { _problem = text; _problemAt = Time.unscaledTime; Sound.Deny(); }

        // ------------------------------------------------------------------ the name keyboard

        private static readonly string[] Keys = { "1234567890", "QWERTYUIOP", "ASDFGHJKL-", "ZXCVBNM_.!" };
        private const int SpecialRow = 4; // Aa, space, delete, OK
        private LocalPlayers.Slot _typist;
        private string _buffer = "";
        private int _kx, _ky;
        private bool _lower;

        /// <summary>Someone's typing a name on the on-screen keyboard.</summary>
        public bool Typing { get { return _typist != null; } }

        /// <summary>The device a player types their name with: the one that asked (the keyboard types for real).</summary>
        private static InputDevice Typer(LocalPlayers.Slot s) { return s.TypingDevice != null && s.Has(s.TypingDevice) ? s.TypingDevice : s.Device; }

        /// <summary>The name keyboard for a player (also used in the online lobby).</summary>
        public void OpenKeyboard(LocalPlayers.Slot s)
        {
            if (s == null || s.Demo) return;
            CloseKeyboard(false);
            _typist = s;
            s.Typing = true;
            _buffer = s.Name ?? "";
            _kx = 0; _ky = 1;
            _lower = true;
            var kb = Typer(s) as Keyboard;
            if (kb != null) kb.onTextInput += OnText;
        }

        private void CloseKeyboard(bool keep)
        {
            if (_typist == null) return;
            var s = _typist;
            _typist = null;
            s.Typing = false;
            var kb = Typer(s) as Keyboard;
            if (kb != null) kb.onTextInput -= OnText;
            if (!keep) return;
            var name = Settings.Clean(_buffer);
            if (name.Length == 0) return;
            s.Name = name;
            // the first person on this PC: that's you, so it's your name everywhere
            var first = Local.Slots.Find(o => !o.Demo);
            if (first == s) Settings.PlayerName = name;
            PlayerPrefs.Save();
        }

        private void OnText(char c)
        {
            if (_typist == null || char.IsControl(c) || c == '<' || c == '>') return;
            if (_buffer.Length < Settings.MaxNameLength) _buffer += c;
        }

        private void KeyboardTick()
        {
            if (!Local.Slots.Contains(_typist)) { CloseKeyboard(false); return; }
            var kb = Typer(_typist) as Keyboard;
            if (kb != null)
            {
                // a real keyboard: just type
                if (kb.backspaceKey.wasPressedThisFrame && _buffer.Length > 0) _buffer = _buffer.Substring(0, _buffer.Length - 1);
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) { Sound.Select(); CloseKeyboard(true); }
                else if (kb.escapeKey.wasPressedThisFrame) { Sound.Back(); CloseKeyboard(false); }
                return;
            }
            var slot = Local.Slots.IndexOf(_typist);
            var m = Local.MenuFor(slot);
            var g = Typer(_typist) as Gamepad;
            if (m.X != 0) _kx = (_kx + m.X + 10) % 10;
            if (m.Y != 0) _ky = (_ky - m.Y + SpecialRow + 1) % (SpecialRow + 1);
            if (m.X != 0 || m.Y != 0) Sound.Move();
            if (g != null && g.startButton.wasPressedThisFrame) { Sound.Select(); CloseKeyboard(true); return; }
            if (g != null && g.buttonNorth.wasPressedThisFrame) { _lower = !_lower; Sound.Move(); return; }
            if (m.Back)
            {
                // B: rub out a letter; with nothing left, put the keyboard away
                Sound.Back();
                if (_buffer.Length > 0) _buffer = _buffer.Substring(0, _buffer.Length - 1);
                else CloseKeyboard(false);
                return;
            }
            if (m.Confirm) Press(_kx, _ky);
        }

        private void Press(int x, int y)
        {
            if (y < SpecialRow)
            {
                var ch = Keys[y][x];
                if (_lower && char.IsLetter(ch)) ch = char.ToLowerInvariant(ch);
                if (_buffer.Length < Settings.MaxNameLength) { _buffer += ch; Sound.Step(1); }
                else Sound.Deny();
                return;
            }
            // the bottom row: Aa (0-1), space (2-6), delete (7-8), OK (9)
            if (x <= 1) { _lower = !_lower; Sound.Move(); }
            else if (x <= 6) { if (_buffer.Length > 0 && _buffer.Length < Settings.MaxNameLength) { _buffer += ' '; Sound.Step(1); } }
            else if (x <= 8) { if (_buffer.Length > 0) { _buffer = _buffer.Substring(0, _buffer.Length - 1); Sound.Back(); } }
            else { Sound.Select(); CloseKeyboard(true); }
        }

        // ------------------------------------------------------------------ drawing

        private void OnGUI()
        {
            if (Current == Page.None && _typist == null) return;
            GUI.depth = 3;
            Ui.Begin();
            if (Current == Page.None) { DrawKeyboard(Ui.W, Ui.H); return; }
            float w = Ui.W, h = Ui.H;
            var appear = Ui.EaseOut((Time.unscaledTime - _openedAt) / 0.3f);
            switch (Current)
            {
                case Page.Join: Ui.Flat = true; try { DrawJoin(w, h, appear); } finally { Ui.Flat = false; } break;
                case Page.Map: DrawMap(w, h, appear); break;
                case Page.Rules: DrawRules(w, h, appear); break;
            }
            if (!string.IsNullOrEmpty(_problem) && Time.unscaledTime - _problemAt < 4f)
            {
                var a = Mathf.Clamp01(4f - (Time.unscaledTime - _problemAt));
                Ui.Box(new Rect(w / 2 - 330, 150, 660, 46), new Color(0.6f, 0.15f, 0.15f, 0.92f * a), 12f);
                Ui.Label(new Rect(w / 2 - 330, 150, 660, 46), _problem, Ui.Font(17, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, a));
            }
            if (_typist != null) DrawKeyboard(w, h);
        }

        public static void Title(float w, string title, string sub, float appear)
        {
            Ui.Label(new Rect(0, 26 - (1f - appear) * 20f, w, 60), title, Ui.Font(44, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, appear));
            if (sub != null) Ui.Label(new Rect(0, 82, w, 30), sub, Ui.Font(17, false, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.6f * appear));
        }

        // the join screen on this PC: the two teams side by side, like the online lobby
        private void DrawJoin(float w, float h, float appear)
        {
            Ui.Box(new Rect(0, 0, w, h), Palette.Background, 0f); // a plain backdrop: the rink moving behind is too busy here
            Title(w, "PLAYERS", "Up to " + LocalPlayers.MaxPlayers + " players, red against blue", appear);

            var tiles = new List<Tile>();
            var person = 0;
            var bot = 0;
            for (var i = 0; i < Local.Slots.Count; i++)
            {
                var s = Local.Slots[i];
                var tag = s.Demo ? "Bot" : "P" + (++person) + "  ·  " + s.Label;
                tiles.Add(new Tile
                {
                    Team = (s.Colour & 7) == 1 ? 1 : 0, Symbol = s.Symbol, Name = string.IsNullOrEmpty(s.Name) ? (s.Demo ? "Bot" : "P" + person) : s.Name,
                    Tag = tag, Mine = !s.Demo, Ready = s.Demo || s.Ready, Bot = s.Demo, Device = s.Device, BotIndex = s.Demo ? bot++ : -1,
                });
            }
            MarkCursors(tiles, Local);
            TeamsView(w, 120f + (1f - appear) * 30f, h - 150f, tiles, Local.Slots.Count < LocalPlayers.MaxPlayers, appear);

            var fy = h - 128f;
            var ready = Local.AllReady;
            if (Chip(new Rect(w / 2f - 250f, fy, 240f, 46f), ready ? "{start} Continue" : Local.HumanCount == 0 ? "Press {join} to join" : "Press {join} when ready", ready, true, true) && ready) Continue();
            if (Chip(new Rect(w / 2f + 10f, fy, 240f, 46f), "{settings} Settings", true, false, true)) { Sound.Select(); Runner.OpenSettings(); }

            Hud.LeavingBar(new Rect(w / 2 - 150, h - 100, 300, 22), Local.NobodyLeaving, "Back to the main menu");
            HintLines(w, h, LobbyHint(Local, true));
        }

        // ------------------------------------------------------------------ the teams (this PC's join screen and the online lobby)

        /// <summary>One player on the players screen.</summary>
        public struct Tile
        {
            public int Team, Symbol;
            public string Name, Tag;
            /// <summary>Played on this PC (their tile is outlined and shows their buttons).</summary>
            public bool Mine;
            public bool Ready, Bot;
            public InputDevice Device;
            /// <summary>Bots: which bot this is (for the readied players' cursors), else -1.</summary>
            public int BotIndex;
            /// <summary>A readied player here is working on this bot (their number is shown on it).</summary>
            public string CursorBy;
        }

        /// <summary>Put each readied player's bot cursor on the bot it's on.</summary>
        public static void MarkCursors(List<Tile> tiles, LocalPlayers local)
        {
            if (local.Bots == null) return;
            var person = 0;
            foreach (var s in local.Slots)
            {
                if (s.Demo) continue;
                person++;
                if (!s.Ready) continue;
                for (var k = 0; k < tiles.Count; k++)
                {
                    if (tiles[k].BotIndex != s.Cursor) continue;
                    var t = tiles[k];
                    t.CursorBy = t.CursorBy == null ? "P" + person : t.CursorBy + " P" + person;
                    tiles[k] = t;
                }
            }
        }

        /// <summary>The button hints along the bottom: what the people here can do right now.</summary>
        public static string LobbyHint(LocalPlayers local, bool canEditBots)
        {
            var anyReady = false;
            var anyNot = false;
            foreach (var s in local.Slots) { if (s.Demo) continue; if (s.Ready) anyReady = true; else anyNot = true; }
            if (local.HumanCount == 0) return "{join} Join      Hold {leave} Leave      {settings} " + "Options";
            var notReady = "{join} Ready      {side} Switch team      {shape} Symbol      {leave} Leave seat";
            var ready = canEditBots && local.Bots != null
                ? "{nav} Pick a bot      {side} Its team      {shape} Its symbol      {bots} Remove / add bot      {leave} Not ready"
                : "{leave} Not ready";
            return anyReady && !anyNot ? ready : anyNot && !anyReady ? notReady : notReady + "\n" + "Ready:   " + ready;
        }

        /// <summary>The hint along the bottom: one line, or two (a line break in it) stacked.</summary>
        public static void HintLines(float w, float h, string hint)
        {
            var lines = hint.Split('\n');
            for (var k = 0; k < lines.Length; k++)
                Glyphs.Hint(new Rect(0, h - 44 - (lines.Length - 1 - k) * 26f, w, 26), lines[k], 15, new Color(1f, 1f, 1f, 0.6f));
        }

        /// <summary>The two teams side by side, between <paramref name="top"/> and <paramref name="bottom"/>.</summary>
        public static void TeamsView(float w, float top, float bottom, List<Tile> tiles, bool canJoin, float appear)
        {
            const float gap = 64f;
            var pw = Mathf.Min(500f, (w - 80f - gap) / 2f);
            var red = new List<Tile>();
            var blue = new List<Tile>();
            foreach (var t in tiles) (t.Team == 1 ? blue : red).Add(t);
            TeamPanel(new Rect(w / 2f - gap / 2f - pw, top, pw, bottom - top), 0, red, canJoin);
            TeamPanel(new Rect(w / 2f + gap / 2f, top, pw, bottom - top), 1, blue, canJoin);
            Ui.Label(new Rect(w / 2f - gap / 2f, top + 7f, gap, 40f), "VS", Ui.Font(20, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.35f * appear));
        }

        private static void TeamPanel(Rect r, int team, List<Tile> tiles, bool canJoin)
        {
            var colour = Palette.Colours[team];
            Ui.Box(r, new Color(colour.r * 0.25f, colour.g * 0.25f, colour.b * 0.25f, 0.55f), 18f);
            var head = new Rect(r.x, r.y, r.width, 54f);
            Ui.Box(head, colour, 18f);
            Ui.Box(new Rect(r.x, head.yMax - 18f, r.width, 18f), colour, 0f);
            Ui.Label(new Rect(r.x + 22f, head.y, r.width - 44f, head.height), Palette.ColourNames[team].ToUpperInvariant(), Ui.Font(26, true), Color.white);
            Ui.Label(new Rect(r.x + 22f, head.y, r.width - 44f, head.height), tiles.Count == 1 ? "1 player" : tiles.Count + " players", Ui.Font(16, false, TextAnchor.MiddleRight), new Color(1f, 1f, 1f, 0.8f));

            var shown = Mathf.Max(4, tiles.Count);
            const float gap = 8f;
            var area = new Rect(r.x + 14f, head.yMax + 12f, r.width - 28f, r.height - head.height - 26f);
            var th = Mathf.Min(70f, (area.height - gap * (shown - 1)) / shown);
            var joinShown = false;
            for (var i = 0; i < shown; i++)
            {
                var tr = new Rect(area.x, area.y + i * (th + gap), area.width, th);
                if (i < tiles.Count) { TileView(tr, team, tiles[i]); continue; }
                Ui.Box(tr, new Color(1f, 1f, 1f, 0.04f), 12f);
                if (canJoin && !joinShown)
                {
                    joinShown = true;
                    var pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f + team);
                    Glyphs.Hint(tr, "Press {join} to join", 16, new Color(1f, 1f, 1f, 0.4f + 0.35f * pulse));
                }
                else Ui.Label(tr, "Open", Ui.Font(15, false, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.22f));
            }
        }

        private static readonly Color ReadyGreen = new Color(0.45f, 0.9f, 0.5f);
        private static readonly Color CursorYellow = new Color(1f, 0.85f, 0.25f);

        private static void TileView(Rect r, int team, Tile t)
        {
            var colour = Palette.Colours[team];
            if (t.CursorBy != null)
            {
                // a readied player is working on this bot: a pulsing yellow frame with their number
                var pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f);
                Ui.Box(new Rect(r.x - 4f, r.y - 4f, r.width + 8f, r.height + 8f), new Color(CursorYellow.r, CursorYellow.g, CursorYellow.b, pulse), 15f);
            }
            else if (t.Mine) Ui.Box(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), new Color(1f, 1f, 1f, 0.9f), 14f);
            Ui.Box(r, Palette.Darker(colour, t.Mine ? 0.7f : 0.5f), 12f);
            var d = r.height - 14f;
            var disc = new Rect(r.x + 10f, r.y + 7f, d, d);
            Ui.Box(new Rect(disc.x - 2f, disc.y - 2f, d + 4f, d + 4f), Palette.PlayerRim, (d + 4f) / 2f);
            Ui.Box(disc, colour, d / 2f);
            Ui.Symbol(new Rect(disc.x + d * 0.2f, disc.y + d * 0.2f, d * 0.6f, d * 0.6f), t.Symbol, Color.white);
            var x = disc.xMax + 14f;
            var right = 150f;
            Ui.Label(new Rect(x, r.y + 4f, r.width - (x - r.x) - right, r.height * 0.58f), t.Name ?? "", Ui.Font(20, true), Color.white);
            Ui.Label(new Rect(x, r.y + r.height * 0.52f, r.width - (x - r.x) - right, r.height * 0.42f), t.Tag ?? "", Ui.Font(13), new Color(1f, 1f, 1f, 0.6f));
            var side = new Rect(r.xMax - right, r.y, right - 14f, r.height);
            if (t.CursorBy != null)
            {
                Ui.Label(new Rect(side.x, side.y, side.width, side.height * 0.55f), t.CursorBy, Ui.Font(16, true, TextAnchor.LowerRight), CursorYellow);
                Ui.Label(new Rect(side.x, side.y + side.height * 0.5f, side.width, side.height * 0.45f), Symbols.Names[t.Symbol & 7], Ui.Font(12, false, TextAnchor.UpperRight), new Color(1f, 1f, 1f, 0.6f));
            }
            else if (t.Bot) Ui.Label(side, Symbols.Names[t.Symbol & 7], Ui.Font(13, false, TextAnchor.MiddleRight), new Color(1f, 1f, 1f, 0.45f));
            else if (t.Ready) Ui.Label(side, "READY", Ui.Font(18, true, TextAnchor.MiddleRight), ReadyGreen, false);
            else if (t.Mine) Glyphs.Hint(side, (team == 0 ? "" : "‹ ") + "{join} Ready" + (team == 0 ? " ›" : ""), 14, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleRight, t.Device);
            else Ui.Label(side, "Not ready", Ui.Font(14, false, TextAnchor.MiddleRight), new Color(1f, 1f, 1f, 0.4f));
        }

        /// <summary>A button along the bottom of the players screen; returns true when it's clicked.</summary>
        public static bool Chip(Rect r, string text, bool on, bool main, bool clickable)
        {
            var hover = on && r.Contains(Ui.Mouse);
            var lit = false; // ready or not, the main button stays the same grey as the others (no white)
            var bg = lit ? new Color(1f, 1f, 1f, hover ? 1f : 0.9f) : new Color(1f, 1f, 1f, hover ? 0.2f : on ? 0.12f : 0.05f);
            Ui.Box(r, bg, 14f);
            // on the white button: dark bold text, no shadow (a shadow smudges dark text)
            Glyphs.Hint(r, text, 17, lit ? new Color(0.06f, 0.08f, 0.12f, 1f) : new Color(1f, 1f, 1f, on ? 0.9f : 0.45f), TextAnchor.MiddleCenter, null, true, !lit);
            return clickable && on && r.Contains(Ui.Mouse) && Ui.TakeClick();
        }

        // ------------------------------------------------------------------ player cards (the join screen and the online lobby)

        /// <summary>Where card <paramref name="i"/> goes: rows of four, as big as fits in <paramref name="area"/>.</summary>
        public static Rect CardRect(Rect area, int i, int count)
        {
            const int cols = 4;
            const float gap = 14f;
            var rows = (count + cols - 1) / cols;
            var cw = Mathf.Min(270f, (area.width - gap * (cols - 1)) / cols);
            var ch = Mathf.Min(cw * 0.95f, (area.height - gap * (rows - 1)) / rows);
            var x0 = area.center.x - (cw * cols + gap * (cols - 1)) / 2;
            var y0 = area.center.y - (ch * rows + gap * (rows - 1)) / 2;
            return new Rect(x0 + (i % cols) * (cw + gap), y0 + (i / cols) * (ch + gap), cw, ch);
        }

        /// <summary>
        /// A player's card: their symbol on a disc in their team colour, the colour's name (with arrows if they can
        /// change it), their name, and a line at the bottom (button hints for <paramref name="device"/>, or plain text).
        /// </summary>
        public static void Card(Rect r, string tag, int colourIndex, int symbol, string name, bool arrows, string footer, bool footerIsHint, UnityEngine.InputSystem.InputDevice device)
        {
            var colour = Palette.Colours[colourIndex & 7];
            var band = r.height * 0.56f;
            Ui.Box(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(1f, 1f, 1f, 0.9f), 18f);
            Ui.Box(r, Palette.Darker(colour, 0.55f), 16f);
            Ui.Box(new Rect(r.x, r.y, r.width, band), colour, 16f);
            Ui.Box(new Rect(r.x, r.y + band - 16f, r.width, 16f), colour, 0f);
            // player number and kind
            Ui.Box(new Rect(r.x + 10, r.y + 10, 50, 26), new Color(0f, 0f, 0f, 0.35f), 8f);
            Ui.Label(new Rect(r.x + 10, r.y + 10, 50, 26), tag, Ui.Font(16, true, TextAnchor.MiddleCenter), Color.white);
            // the symbol, big, on a puck-like disc in the team colour
            var d = Mathf.Min(band - 22f, r.width * 0.42f, 110f);
            var disc = new Rect(r.center.x - d / 2, r.y + band / 2 - d / 2, d, d);
            Ui.Box(new Rect(disc.x - 4, disc.y - 4, d + 8, d + 8), Palette.PlayerRim, (d + 8) / 2f);
            Ui.Box(disc, Palette.Darker(colour, 0.8f), d / 2f);
            Ui.Symbol(new Rect(disc.x + d * 0.18f, disc.y + d * 0.18f, d * 0.64f, d * 0.64f), symbol, Color.white);
            // colour (left / right changes it), name, and the line at the bottom
            var cy = r.y + band + 2f;
            var cname = Palette.ColourNames[colourIndex & 7];
            Ui.Label(new Rect(r.x, cy, r.width, 24), arrows ? "‹   " + cname + "   ›" : cname, Ui.Font(17, true, TextAnchor.MiddleCenter), Color.white);
            Ui.Label(new Rect(r.x + 6, cy + 24, r.width - 12, 32), name, Ui.Font(21, true, TextAnchor.MiddleCenter), Color.white);
            var foot = new Rect(r.x, r.yMax - 26, r.width, 22);
            if (footerIsHint) Glyphs.Hint(foot, footer, 12, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleCenter, device);
            else Ui.Label(foot, footer, Ui.Font(13, false, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.6f));
        }

        /// <summary>An empty seat: "Press A to join" (or just "Open" when nobody here can join).</summary>
        public static void EmptyCard(Rect r, string tag, int i, bool canJoin)
        {
            var pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f + i);
            Ui.Box(r, new Color(1f, 1f, 1f, 0.05f + 0.03f * pulse), 16f);
            Ui.Box(new Rect(r.x + 10, r.y + 10, 50, 26), new Color(1f, 1f, 1f, 0.06f), 8f);
            Ui.Label(new Rect(r.x + 10, r.y + 10, 50, 26), tag, Ui.Font(16, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.35f));
            if (!canJoin) { Ui.Label(new Rect(r.x, r.center.y - 15, r.width, 30), "Open", Ui.Font(18, false, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.35f)); return; }
            Glyphs.Hint(new Rect(r.x, r.center.y - 28, r.width, 32), "Press {join}", 18, new Color(1f, 1f, 1f, 0.55f + 0.35f * pulse));
            Ui.Label(new Rect(r.x, r.center.y + 4, r.width, 26), "to join", Ui.Font(16, false, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.45f));
        }

        // the rink: a big preview of the one chosen, with its neighbours either side
        private void DrawMap(float w, float h, float appear)
        {
            Ui.Box(new Rect(0, 0, w, h), Palette.Background, 0f);
            Title(w, "CHOOSE A RINK", null, appear);
            var count = Runner.MapCount;
            var cur = Runner.MapIndex;
            for (var k = -1; k <= 1; k++)
            {
                if (count < 2 && k != 0) continue;
                var i = ((cur + k) % count + count) % count;
                var big = k == 0;
                var cw = big ? 620f : 300f;
                var chh = cw * 9f / 16f;
                var cx = w / 2 + k * 520f;
                var r = new Rect(cx - cw / 2, h / 2 - chh / 2 - (big ? 10f : -20f), cw, chh);
                var a = big ? 1f : 0.45f;
                if (big) Ui.Box(new Rect(r.x - 4, r.y - 4, r.width + 8, r.height + 8), new Color(1f, 1f, 1f, 0.9f), 18f);
                Ui.Box(r, new Color(0.08f, 0.08f, 0.09f, 1f), 14f);
                var tex = Runner.MapPreview(i);
                if (tex != null && Event.current.type == EventType.Repaint)
                {
                    var old = GUI.color; GUI.color = new Color(1f, 1f, 1f, a);
                    GUI.DrawTexture(new Rect(r.x + 10, r.y + 10, r.width - 20, r.height - 20), tex, ScaleMode.ScaleToFit);
                    GUI.color = old;
                }
                Ui.Label(new Rect(r.x, r.yMax + 8, r.width, 40), Runner.MapName(i), Ui.Font(big ? 28 : 18, big, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, a));
            }
            if (count > 1)
            {
                Ui.Label(new Rect(w / 2 - 380, h / 2 - 40, 40, 60), "‹", Ui.Font(54, true, TextAnchor.MiddleCenter), Color.white);
                Ui.Label(new Rect(w / 2 + 340, h / 2 - 40, 40, 60), "›", Ui.Font(54, true, TextAnchor.MiddleCenter), Color.white);
            }
            Glyphs.Hint(new Rect(0, h - 52, w, 30), "{side} Rink      {confirm} Choose      {back} Back", 17, new Color(1f, 1f, 1f, 0.8f));
        }

        // the rules: first to so many goals, or a time limit
        private void DrawRules(float w, float h, float appear)
        {
            Ui.Box(new Rect(0, 0, w, h), Palette.Background, 0f);
            Title(w, "HOW TO WIN", Runner.MapName(Runner.MapIndex), appear);
            var labels = new[] { "Score", "Time" };
            var values = new[] { "First to  ‹ " + Settings.GoalsToWin + " ›  goals", "‹ " + Settings.MatchMinutes + " ›  " + (Settings.MatchMinutes == 1 ? "minute" : "minutes") };
            var notes = new[] { "The first team to score this many wins.", "Most goals when time runs out wins. Level: next goal wins." };
            for (var k = 0; k < 2; k++)
            {
                var on = k == _rulesRow;
                var r = new Rect(w / 2 - 340, h / 2 - 120 + k * 130, 680, 110);
                if (on) Ui.Box(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(1f, 1f, 1f, 0.9f), 18f);
                Ui.Box(r, on ? new Color(0.16f, 0.16f, 0.18f, 1f) : new Color(1f, 1f, 1f, 0.06f), 16f);
                Ui.Label(new Rect(r.x + 30, r.y + 12, 200, 50), labels[k], Ui.Font(32, true), new Color(1f, 1f, 1f, on ? 1f : 0.6f));
                Ui.Label(new Rect(r.x + 230, r.y + 12, 420, 50), values[k], Ui.Font(28, on, TextAnchor.MiddleRight), new Color(1f, 1f, 1f, on ? 1f : 0.6f));
                Ui.Label(new Rect(r.x + 30, r.y + 62, 620, 30), notes[k], Ui.Font(16), new Color(1f, 1f, 1f, on ? 0.65f : 0.35f));
            }
            Glyphs.Hint(new Rect(0, h - 52, w, 30), "{nav} Score or time      {side} Change      {confirm} Start the match      {back} Back", 17, new Color(1f, 1f, 1f, 0.8f));
        }

        private void DrawKeyboard(float w, float h)
        {
            Ui.Box(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.6f), 0f);
            var pw = 720f; var ph = 420f;
            var p = new Rect(w / 2 - pw / 2, h / 2 - ph / 2, pw, ph);
            var colour = Palette.Colours[_typist.Colour & 7];
            Ui.Box(new Rect(p.x - 3, p.y - 3, pw + 6, ph + 6), colour, 20f);
            Ui.Box(p, Ui.Panel, 18f);
            var i = Local.Slots.IndexOf(_typist);
            Ui.Label(new Rect(p.x, p.y + 14, pw, 34), "P" + (i + 1) + "'s name", Ui.Font(22, true, TextAnchor.MiddleCenter), Color.white);
            // the name so far
            var field = new Rect(p.x + 60, p.y + 56, pw - 120, 52);
            Ui.Box(field, new Color(1f, 1f, 1f, 0.1f), 12f);
            var caret = (Time.unscaledTime * 2f) % 2f < 1.2f ? "|" : " ";
            Ui.Label(field, _buffer + caret, Ui.Font(30, true, TextAnchor.MiddleCenter), Color.white);

            var real = Typer(_typist) is Keyboard;
            if (real)
            {
                Ui.Label(new Rect(p.x, p.y + 150, pw, 40), "Type your name", Ui.Font(22, false, TextAnchor.MiddleCenter), Ui.TextDim);
                Glyphs.Hint(new Rect(p.x, p.yMax - 50, pw, 30), "{confirm} Done      {back} Cancel", 17, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleCenter, Typer(_typist));
                return;
            }
            const float kw = 58f, kh = 48f, gap = 6f;
            var x0 = p.x + pw / 2 - (10 * kw + 9 * gap) / 2;
            var y0 = p.y + 126;
            for (var ky = 0; ky < SpecialRow; ky++)
                for (var kx = 0; kx < 10; kx++)
                {
                    var r = new Rect(x0 + kx * (kw + gap), y0 + ky * (kh + gap), kw, kh);
                    var on = kx == _kx && ky == _ky;
                    Ui.Box(r, on ? Color.white : new Color(1f, 1f, 1f, 0.08f), 8f);
                    var ch = Keys[ky][kx];
                    if (_lower && char.IsLetter(ch)) ch = char.ToLowerInvariant(ch);
                    Ui.Label(r, ch.ToString(), Ui.Font(22, true, TextAnchor.MiddleCenter), on ? new Color(0.08f, 0.08f, 0.09f) : Color.white, !on);
                }
            // bottom row: Aa, space, delete, OK
            var by = y0 + SpecialRow * (kh + gap);
            var specials = new[] { new[] { 0, 1 }, new[] { 2, 6 }, new[] { 7, 8 }, new[] { 9, 9 } };
            var names = new[] { _lower ? "ABC" : "abc", "Space", "Delete", "OK" };
            for (var k = 0; k < 4; k++)
            {
                var a = specials[k][0]; var b = specials[k][1];
                var r = new Rect(x0 + a * (kw + gap), by, (b - a + 1) * kw + (b - a) * gap, kh);
                var on = _ky == SpecialRow && _kx >= a && _kx <= b;
                Ui.Box(r, on ? Color.white : (k == 3 ? new Color(colour.r, colour.g, colour.b, 0.5f) : new Color(1f, 1f, 1f, 0.08f)), 8f);
                Ui.Label(r, names[k], Ui.Font(19, true, TextAnchor.MiddleCenter), on ? new Color(0.08f, 0.08f, 0.09f) : Color.white, !on);
            }
            Glyphs.Hint(new Rect(p.x, p.yMax - 40, pw, 30), "{confirm} Type      {back} Delete      {profile} abc / ABC      {start} Done", 15, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleCenter, Typer(_typist));
        }
    }
}
