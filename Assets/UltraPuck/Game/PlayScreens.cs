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
            if (Local.SettingsPressed()) { Runner.OpenSettings(); return; }
            Local.LobbyUpdate();
            if (Local.LeaveLobbyHeld()) { Runner.GoMainMenu(); return; }
            if (Local.NameRequest != null) { OpenKeyboard(Local.NameRequest); Local.NameRequest = null; return; }
            if (Local.BotRequest > 0) { if (!Local.AddBot()) Problem("The rink is full (" + LocalPlayers.MaxPlayers + " players)."); }
            if (Local.BotRequest < 0) { var b = Local.LastBot(); if (b >= 0) { Local.Slots.RemoveAt(b); Local.AssignTeams(); } }
            if (Local.HumanCount > 0 && Local.StartPressed())
            {
                var colours = Local.AssignTeams();
                if (colours > 2) Problem("Only two teams: pick two colours between you.");
                else if (colours < 2 && Local.Slots.Count > 1) Problem("Everyone's on the same team: pick another colour, or add a bot with " + (InputMode.Pad ? "RB" : "="));
                else if (Local.Slots.Count < 2) Problem("Add a bot (" + (InputMode.Pad ? "RB" : "=") + ") or another player to play against.");
                else Open(Page.Map);
            }
        }

        private void MapTick()
        {
            var m = Local.MenuJoined();
            if (m.Back) { Open(Page.Join); return; }
            if (m.X != 0) Runner.StepMap(m.X);
            if (m.Confirm || Local.StartPressed()) Open(Page.Rules);
        }

        private void RulesTick()
        {
            var m = Local.MenuJoined();
            if (m.Back) { Open(Page.Map); return; }
            if (m.Y != 0) _rulesRow = (_rulesRow + 2 - m.Y) % 2;
            if (m.X != 0)
            {
                Settings.MatchMode = _rulesRow;
                if (_rulesRow == 0) Settings.GoalsToWin += m.X;
                else Settings.MatchMinutes += m.X;
            }
            if (m.Confirm || Local.StartPressed())
            {
                Settings.MatchMode = _rulesRow;
                PlayerPrefs.Save();
                Open(Page.None);
                Runner.StartLocalMatch();
            }
        }

        private void Problem(string text) { _problem = text; _problemAt = Time.unscaledTime; }

        // ------------------------------------------------------------------ the name keyboard

        private static readonly string[] Keys = { "1234567890", "QWERTYUIOP", "ASDFGHJKL-", "ZXCVBNM_.!" };
        private const int SpecialRow = 4; // Aa, space, delete, OK
        private LocalPlayers.Slot _typist;
        private string _buffer = "";
        private int _kx, _ky;
        private bool _lower;

        /// <summary>Someone's typing a name on the on-screen keyboard.</summary>
        public bool Typing { get { return _typist != null; } }

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
            var kb = s.Device as Keyboard;
            if (kb != null) kb.onTextInput += OnText;
        }

        private void CloseKeyboard(bool keep)
        {
            if (_typist == null) return;
            var s = _typist;
            _typist = null;
            s.Typing = false;
            var kb = s.Device as Keyboard;
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
            var kb = _typist.Device as Keyboard;
            if (kb != null)
            {
                // a real keyboard: just type
                if (kb.backspaceKey.wasPressedThisFrame && _buffer.Length > 0) _buffer = _buffer.Substring(0, _buffer.Length - 1);
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) CloseKeyboard(true);
                else if (kb.escapeKey.wasPressedThisFrame) CloseKeyboard(false);
                return;
            }
            var slot = Local.Slots.IndexOf(_typist);
            var m = Local.MenuFor(slot);
            var g = _typist.Device as Gamepad;
            if (m.X != 0) _kx = (_kx + m.X + 10) % 10;
            if (m.Y != 0) _ky = (_ky - m.Y + SpecialRow + 1) % (SpecialRow + 1);
            if (g != null && g.startButton.wasPressedThisFrame) { CloseKeyboard(true); return; }
            if (g != null && g.buttonNorth.wasPressedThisFrame) { _lower = !_lower; return; }
            if (m.Back)
            {
                // B: rub out a letter; with nothing left, put the keyboard away
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
                if (_buffer.Length < Settings.MaxNameLength) _buffer += ch;
                return;
            }
            // the bottom row: Aa (0-1), space (2-6), delete (7-8), OK (9)
            if (x <= 1) _lower = !_lower;
            else if (x <= 6) { if (_buffer.Length > 0 && _buffer.Length < Settings.MaxNameLength) _buffer += ' '; }
            else if (x <= 8) { if (_buffer.Length > 0) _buffer = _buffer.Substring(0, _buffer.Length - 1); }
            else CloseKeyboard(true);
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
                case Page.Join: DrawJoin(w, h, appear); break;
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

        private static void Title(float w, string title, string sub, float appear)
        {
            Ui.Label(new Rect(0, 26 - (1f - appear) * 20f, w, 60), title, Ui.Font(44, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, appear));
            if (sub != null) Ui.Label(new Rect(0, 82, w, 30), sub, Ui.Font(17, false, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.6f * appear));
        }

        // the join screen: a card per player along the bottom, like a fighting game's character select
        private void DrawJoin(float w, float h, float appear)
        {
            Ui.Box(new Rect(0, 0, w, h), Palette.Background, 0f); // a plain backdrop: the rink moving behind is too busy here
            Title(w, "PLAYERS", "Same colour, same team", appear);
            Glyphs.Hint(new Rect(0, 112, w, 30), "{side} Team colour      {profile} Name      {shape} Symbol      {bots} Bots", 17, new Color(1f, 1f, 1f, 0.75f));

            var slots = LocalPlayers.MaxPlayers;
            const float gap = 16f;
            var cw = Mathf.Min(270f, (w - 80f - gap * (slots - 1)) / slots);
            var ch = 300f;
            var x0 = w / 2 - (cw * slots + gap * (slots - 1)) / 2;
            var y = h - ch - 70f + (1f - appear) * 60f;
            for (var i = 0; i < slots; i++)
            {
                var r = new Rect(x0 + i * (cw + gap), y, cw, ch);
                if (i < Local.Slots.Count) PlayerCard(r, i, Local.Slots[i]);
                else EmptyCard(r, i);
            }

            var ready = Local.HumanCount > 0;
            Glyphs.Hint(new Rect(0, h - 52, w, 30), (ready ? "{start} Continue      " : "") + "{leave} Leave      Hold {leave} Main menu      {settings} Settings", 17, new Color(1f, 1f, 1f, 0.8f));
            Hud.LeavingBar(new Rect(w / 2 - 150, h - 96, 300, 24), Local.NobodyLeaving, "Back to the main menu");
        }

        private void PlayerCard(Rect r, int i, LocalPlayers.Slot s)
        {
            var colour = Palette.Colours[s.Colour & 7];
            Ui.Box(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), new Color(1f, 1f, 1f, 0.9f), 18f);
            Ui.Box(r, Palette.Darker(colour, 0.55f), 16f);
            Ui.Box(new Rect(r.x, r.y, r.width, r.height * 0.62f), colour, 16f);
            Ui.Box(new Rect(r.x, r.y + r.height * 0.5f, r.width, r.height * 0.12f), colour, 0f);
            // player number and kind
            Ui.Box(new Rect(r.x + 12, r.y + 12, 54, 30), new Color(0f, 0f, 0f, 0.35f), 8f);
            Ui.Label(new Rect(r.x + 12, r.y + 12, 54, 30), s.Demo ? "CPU" : "P" + (i + 1), Ui.Font(18, true, TextAnchor.MiddleCenter), Color.white);
            // the symbol, big, on a puck-like disc in the team colour
            var d = Mathf.Min(r.width * 0.5f, 120f);
            var disc = new Rect(r.center.x - d / 2, r.y + r.height * 0.31f - d / 2, d, d);
            Ui.Box(new Rect(disc.x - 5, disc.y - 5, d + 10, d + 10), Palette.PlayerRim, (d + 10) / 2f);
            Ui.Box(disc, Palette.Darker(colour, 0.8f), d / 2f);
            Ui.Symbol(new Rect(disc.x + d * 0.18f, disc.y + d * 0.18f, d * 0.64f, d * 0.64f), s.Symbol, Color.white);
            // colour with arrows (left / right changes it)
            var cy = r.y + r.height * 0.62f + 8f;
            Ui.Label(new Rect(r.x, cy, r.width, 30), "‹   " + Palette.ColourNames[s.Colour & 7] + "   ›", Ui.Font(19, true, TextAnchor.MiddleCenter), Color.white);
            // name
            var name = string.IsNullOrEmpty(s.Name) ? (s.Demo ? "Bot" : "P" + (i + 1)) : s.Name;
            Ui.Label(new Rect(r.x + 8, cy + 34, r.width - 16, 40), name, Ui.Font(24, true, TextAnchor.MiddleCenter), Color.white);
            if (!s.Demo)
                Glyphs.Hint(new Rect(r.x, r.yMax - 38, r.width, 26), "{profile} Name   {shape} Symbol", 13, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleCenter, s.Device);
            else
                Ui.Label(new Rect(r.x, r.yMax - 38, r.width, 26), Symbols.Names[s.Symbol & 7], Ui.Font(14, false, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.6f));
        }

        private void EmptyCard(Rect r, int i)
        {
            var pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f + i);
            Ui.Box(r, new Color(1f, 1f, 1f, 0.05f + 0.03f * pulse), 16f);
            Ui.Box(new Rect(r.x + 12, r.y + 12, 54, 30), new Color(1f, 1f, 1f, 0.06f), 8f);
            Ui.Label(new Rect(r.x + 12, r.y + 12, 54, 30), "P" + (i + 1), Ui.Font(18, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.35f));
            Glyphs.Hint(new Rect(r.x, r.center.y - 30, r.width, 34), "Press {join}", 20, new Color(1f, 1f, 1f, 0.55f + 0.35f * pulse));
            Ui.Label(new Rect(r.x, r.center.y + 6, r.width, 30), "to join", Ui.Font(18, false, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.45f));
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

            var real = _typist.Device is Keyboard;
            if (real)
            {
                Ui.Label(new Rect(p.x, p.y + 150, pw, 40), "Type your name", Ui.Font(22, false, TextAnchor.MiddleCenter), Ui.TextDim);
                Glyphs.Hint(new Rect(p.x, p.yMax - 50, pw, 30), "{confirm} Done      {back} Cancel", 17, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleCenter, _typist.Device);
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
            Glyphs.Hint(new Rect(p.x, p.yMax - 40, pw, 30), "{confirm} Type      {back} Delete      {profile} abc / ABC      {start} Done", 15, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleCenter, _typist.Device);
        }
    }
}
