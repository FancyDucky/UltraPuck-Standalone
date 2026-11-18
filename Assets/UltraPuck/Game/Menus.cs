using System;
using System.Collections.Generic;
using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// The pause menu (Resume, Settings, Main menu, plus free play's extras) and the settings screen with its
    /// Graphics / Gameplay / Camera tabs. Works with a controller (stick or d-pad, A, B, LB / RB for tabs), the
    /// keyboard (arrows or WASD, Enter or Space, Esc, Q / E for tabs) and the mouse (hover, click, drag sliders).
    /// <see cref="GameRunner"/> opens it and fills in what the buttons do.
    /// </summary>
    internal sealed class Menus : MonoBehaviour
    {
        public enum Page { None, Pause, Settings, Cards, Match }

        /// <summary>The card list for the Toggle Cards page (set by GameRunner).</summary>
        public UltraPuck.Core.CardLibrary Cards;

        public Page Current { get; private set; }
        public bool IsOpen { get { return Current != Page.None; } }

        // filled in by GameRunner
        public Action OnResume, OnMainMenu, OnGetCard;
        public Func<int> BotCount, MaxBots;
        public Action<int> SetBots;
        public bool FreePlay;
        /// <summary>The pause menu offers the match settings (the host online, anyone in a match on this PC).</summary>
        public Func<bool> CanEditMatch;
        public Func<int> MapCount, CurrentMap;
        public Func<int, string> MapName;
        /// <summary>Restart the match with the match settings as they are now, on this rink.</summary>
        public Action<int> OnRestart;
        private int _pendingMap;

        private bool _settingsFromLobby;
        private int _focus, _tab;
        private float _openedAt, _focusY = -1f, _tabX = -1f;
        private string _subtitle = "";

        private static readonly string[] Tabs = { "Graphics", "Gameplay", "Camera", "Match" };
        /// <summary>Rows shown at once; longer pages scroll with the focus.</summary>
        private const int MaxVisible = 8;
        private int _first;

        // ------------------------------------------------------------------ rows

        private sealed class Row
        {
            public string Label;
            public Func<string> Value;            // choices: the current value's name
            public Action<int> Step;              // choices and sliders: left / right
            public Action Press;                  // buttons (and choices: next value)
            public Func<float> Get01;             // sliders: 0..1
            public Action<float> Set01;
            public Func<bool> Toggle;             // switches: on or off
            public Func<bool> Enabled = () => true;
            public string Hint;
            public bool Text;                     // a text box (your name): press to type
        }

        // typing your name
        private bool _typing;
        private string _buffer = "";
        private Action<string> _typed;

        /// <summary>A text box in the settings has the keyboard (other keyboard shortcuts should leave it alone).</summary>
        public static bool Typing { get; private set; }

        private readonly List<Row> _rows = new List<Row>();

        private static Row Button(string label, Action press) { return new Row { Label = label, Press = press }; }

        private static Row Switch(string label, Func<bool> get, Action<bool> set, string hint = null)
        {
            return new Row { Label = label, Toggle = get, Press = () => set(!get()), Step = d => set(d > 0), Hint = hint };
        }

        private static Row Choice(string label, string[] names, Func<int> get, Action<int> set, string hint = null)
        {
            return new Row
            {
                Label = label, Hint = hint,
                Value = () => names[Mathf.Clamp(get(), 0, names.Length - 1)],
                Step = d => set((get() + d + names.Length) % names.Length),
                Press = () => set((get() + 1) % names.Length),
            };
        }

        private static Row Slider(string label, Func<float> get, Action<float> set, Func<float, string> text, string hint = null)
        {
            return new Row { Label = label, Hint = hint, Get01 = get, Set01 = set, Value = () => text(get()), Step = d => set(Mathf.Clamp01(get() + d * 0.05f)) };
        }

        private void BuildRows()
        {
            _rows.Clear();
            if (Current == Page.Pause)
            {
                _rows.Add(Button("Resume", Close));
                if (FreePlay)
                {
                    _rows.Add(new Row
                    {
                        Label = "Bots",
                        Value = () => BotCount() == 0 ? "None" : BotCount().ToString(),
                        Step = d => SetBots(Mathf.Clamp(BotCount() + d, 0, MaxBots())),
                        Press = () => SetBots(BotCount() >= MaxBots() ? 0 : BotCount() + 1),
                    });
                    _rows.Add(Button("Cards", () => { Close(); if (OnGetCard != null) OnGetCard(); }));
                }
                if (!FreePlay && CanEditMatch != null && CanEditMatch()) _rows.Add(Button("Match settings", OpenMatch));
                _rows.Add(Button("Settings", () => OpenSettings(false)));
                _rows.Add(Button("Main menu", () => { Close(); if (OnMainMenu != null) OnMainMenu(); }));
                return;
            }
            if (Current == Page.Match)
            {
                _rows.Add(new Row { Label = "Restart match with these settings", Press = () => { var map = _pendingMap; Close(); if (OnRestart != null) OnRestart(map); },
                    Hint = "Starts the match again from 0 : 0 with the settings below. Online, for everyone." });
                if (MapCount != null && MapCount() > 0)
                    _rows.Add(new Row
                    {
                        Label = "Rink", Value = () => MapName(_pendingMap),
                        Step = d => _pendingMap = (_pendingMap + d + MapCount()) % MapCount(),
                        Press = () => _pendingMap = (_pendingMap + 1) % MapCount(),
                        Hint = "Changes when the match restarts.",
                    });
                _rows.Add(new Row { Label = "Match", Value = () => Settings.MatchText, Step = Settings.StepMatch, Press = () => Settings.StepMatch(1), Hint = "First to so many goals, or a time limit." });
                MatchRows();
                _rows.Add(Button("Back", () => { Current = Page.Pause; _focus = 0; _first = 0; _openedAt = Time.unscaledTime; _focusY = -1f; }));
                return;
            }
            if (Current == Page.Cards)
            {
                if (Cards != null)
                    foreach (var c in Cards.Cards)
                    {
                        var id = c.Id;
                        _rows.Add(Switch(c.Name + "  (" + c.Tier + ")", () => Settings.CardOn(id), v => Settings.SetCardOn(id, v), CleanText(c.Text)));
                    }
                _rows.Add(Button("Back", () => { Current = _cardsFrom; _focus = 0; _first = 0; _focusY = -1f; }));
                return;
            }
            if (Current != Page.Settings) return;
            switch (_tab)
            {
                case 0:
                    _rows.Add(Choice("Window", Settings.WindowNames, () => (int)Settings.WindowMode, v => Settings.WindowMode = (Settings.Window)v));
                    _rows.Add(Switch("V-Sync", () => Settings.VSync, v => Settings.VSync = v, "Matches your monitor's refresh rate and stops tearing."));
                    var caps = new string[Settings.FpsCaps.Length];
                    for (var k = 0; k < caps.Length; k++) caps[k] = Settings.FpsCaps[k] == 0 ? "Unlimited" : Settings.FpsCaps[k] + " fps";
                    var cap = Choice("Frame rate limit", caps, () => Mathf.Max(0, Array.IndexOf(Settings.FpsCaps, Settings.FpsCap)), v => Settings.FpsCap = Settings.FpsCaps[v], "Only used with V-Sync off.");
                    cap.Enabled = () => !Settings.VSync;
                    _rows.Add(cap);
                    _rows.Add(Slider("UI size", () => (Settings.UiScale - 0.6f) / 1.4f, v => Settings.UiScale = 0.6f + v * 1.4f, v => Mathf.RoundToInt((0.6f + v * 1.4f) * 100f) + "%"));
                    _rows.Add(Switch("Puck trail", () => Settings.PuckTrail, v => Settings.PuckTrail = v));
                    _rows.Add(Switch("Card effects", () => Settings.CardEffects, v => Settings.CardEffects = v, "Holo foil, glare and sparkle on cards."));
                    break;
                case 1:
                    _rows.Add(new Row
                    {
                        Label = "Your name", Text = true,
                        Value = () => _typing ? _buffer : Settings.PlayerName,
                        Press = () => StartTyping(Settings.PlayerName, v => Settings.PlayerName = v),
                        Hint = "Shown over your player and in online games. Leave it empty to use your Steam name.",
                    });
                    _rows.Add(Choice("Keyboard and controller", new[] { "Both play player 1", "Separate players" }, () => Settings.SharedControls ? 0 : 1, v => Settings.SharedControls = v == 0,
                        "Both play player 1: the keyboard and your controller move the same player, whichever you pick up (other controllers are players 2, 3...). Separate: each joins as its own player."));
                    var p1 = Choice("Player 1", new[] { "Controller", "Keyboard" }, () => Settings.ControllerIsPlayer1 ? 0 : 1, v => Settings.ControllerIsPlayer1 = v == 0,
                        "With separate players: which one is player 1 when both join.");
                    p1.Enabled = () => !Settings.SharedControls;
                    _rows.Add(p1);
                    _rows.Add(Switch("Name tags", () => Settings.NameTags, v => Settings.NameTags = v, "Names floating over the players."));
                    _rows.Add(Slider("Screen shake", () => Settings.ShakeAmount, v => Settings.ShakeAmount = v, v => v < 0.01f ? "Off" : Mathf.RoundToInt(v * 100f) + "%"));
                    _rows.Add(Switch("Hit effects", () => Settings.HitEffects, v => Settings.HitEffects = v, "Squash, flash and impact rings when players collide."));
                    _rows.Add(Choice("Check bar", new[] { "While recharging", "Always" }, () => Settings.DashBarAlways ? 1 : 0, v => Settings.DashBarAlways = v == 1));
                    _rows.Add(Choice("Corner card list", new[] { "Shown", "Hidden" }, () => Settings.CornerCards ? 0 : 1, v => Settings.CornerCards = v == 0,
                        "Everyone's cards in the lower-left corner during a match. Tab / View in a match hides or shows it."));
                    _rows.Add(Switch("Auto-update", () => Settings.AutoUpdate, v => Settings.AutoUpdate = v, "Check for a new version when the game starts, and install it."));
                    break;
                case 2:
                    _rows.Add(Choice("Camera", new[] { "Follow the action", "Whole rink" }, () => Settings.FollowCamera ? 0 : 1, v => Settings.FollowCamera = v == 0));
                    var zoom = Slider("Auto-camera zoom", () => Settings.CameraZoom, v => Settings.CameraZoom = v, ZoomText, "How far the follow camera pulls back from the action.");
                    zoom.Enabled = () => Settings.FollowCamera;
                    _rows.Add(zoom);
                    var smooth = Slider("Camera smoothing", () => Settings.CameraSmoothing, v => Settings.CameraSmoothing = v, v => v < 0.2f ? "Snappy" : v > 0.75f ? "Floaty" : Mathf.RoundToInt(v * 100f) + "%");
                    smooth.Enabled = () => Settings.FollowCamera;
                    _rows.Add(smooth);
                    _rows.Add(Switch("Charge zoom", () => Settings.ChargeZoom, v => Settings.ChargeZoom = v, "While you charge a shot, the camera pulls back until the goal you're aiming at is in view."));
                    var caps2 = new string[Settings.ChargeZoomCaps.Length];
                    for (var k = 0; k < caps2.Length; k++) caps2[k] = Settings.ChargeZoomCaps[k] == 0 ? "No limit" : Settings.ChargeZoomCaps[k] + "%";
                    var cz = Choice("Max zoom out", caps2, () => Mathf.Max(0, Array.IndexOf(Settings.ChargeZoomCaps, Settings.ChargeZoomMax)), v => Settings.ChargeZoomMax = Settings.ChargeZoomCaps[v],
                        "How far charge zoom may pull back from the normal view.");
                    cz.Enabled = () => Settings.ChargeZoom && Settings.FollowCamera;
                    _rows.Add(cz);
                    break;
                case 3:
                    MatchRows();
                    break;
            }
            _rows.Add(Button("Back", Back));
        }

        /// <summary>The match's rules (the Match tab, and the pause menu's match settings).</summary>
        private void MatchRows()
        {
            _rows.Add(Switch("Rounds mode", () => Settings.Rounds, v => Settings.Rounds = v, "After each goal, the team that conceded picks a card that powers them up for the rest of the match."));
            _rows.Add(Switch("Goal replays", () => Settings.Replays, v => Settings.Replays = v, "Every goal is shown again, up to 10 seconds back, in slow motion on the big moments. Everyone pressing A skips it (bots always agree)."));
            var vote = Choice("Picking", new[] { "Each player", "Team vote" }, () => Settings.TeamVote ? 1 : 0, v => Settings.TeamVote = v == 1,
                "Each player: everyone gets their own card. Team vote: each team votes on one card the whole team gets (a tie is random).");
            vote.Enabled = () => Settings.Rounds;
            _rows.Add(vote);
            var offered = new string[Settings.OfferCounts.Length];
            for (var k = 0; k < offered.Length; k++) offered[k] = Settings.OfferCounts[k].ToString();
            _rows.Add(Choice("Cards offered", offered, () => Mathf.Max(0, Array.IndexOf(Settings.OfferCounts, Settings.CardsOffered)), v => Settings.CardsOffered = Settings.OfferCounts[v]));
            var times = new string[Settings.PickTimes.Length];
            for (var k = 0; k < times.Length; k++) times[k] = Settings.PickTimes[k] == 0 ? "No limit" : Settings.PickTimes[k] + " s";
            _rows.Add(Choice("Pick time", times, () => Mathf.Max(0, Array.IndexOf(Settings.PickTimes, Settings.PickTime)), v => Settings.PickTime = Settings.PickTimes[v],
                "How long players have to pick. Anyone who hasn't picked in time gets a random card."));
            _rows.Add(new Row { Label = "Toggle cards", Value = CardsOnText, Press = () => { _cardsFrom = Current; Current = Page.Cards; _focus = 0; _first = 0; _focusY = -1f; },
                Hint = "Choose which cards can be offered. Online, the host's choices apply." });
        }

        private Page _cardsFrom = Page.Settings;

        /// <summary>Open the match settings page (for the screenshots).</summary>
        public void ShowMatchSettings() { OpenMatch(); }

        private void OpenMatch()
        {
            Current = Page.Match;
            _pendingMap = CurrentMap != null ? CurrentMap() : 0;
            _focus = 0; _first = 0; _focusY = -1f;
            _openedAt = Time.unscaledTime;
        }

        /// <summary>Settings tabs: from a match, the Match tab is left out (it's the pause menu's Match settings there).</summary>
        private int TabCount { get { return _settingsFromLobby ? Tabs.Length : Tabs.Length - 1; } }

        private int _settingsRows;

        /// <summary>The most rows any settings tab has.</summary>
        private int SettingsRows()
        {
            if (_settingsRows > 0) return Mathf.Max(_settingsRows, _rows.Count);
            var keep = _tab;
            for (_tab = 0; _tab < Tabs.Length; _tab++) { BuildRows(); _settingsRows = Mathf.Max(_settingsRows, _rows.Count); }
            _tab = Mathf.Min(keep, TabCount - 1);
            _tab = keep;
            BuildRows();
            return _settingsRows;
        }

        // ------------------------------------------------------------------ typing

        private void StartTyping(string current, Action<string> done)
        {
            // a controller: Steam's on-screen keyboard if it's available (Big Picture, Steam Deck); otherwise the keyboard
            if (InputMode.Pad && SteamNet.AskText("Your name", current, Settings.MaxNameLength, done)) return;
            _typing = Typing = true;
            _buffer = current ?? "";
            _typed = done;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null) kb.onTextInput += OnText;
        }

        private void OnText(char c)
        {
            if (!_typing || char.IsControl(c) || c == '<' || c == '>') return;
            if (_buffer.Length < Settings.MaxNameLength) _buffer += c;
        }

        private void TypingTick()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (LocalPlayers.Blocked) return;
            if (kb != null && kb.backspaceKey.wasPressedThisFrame && _buffer.Length > 0) _buffer = _buffer.Substring(0, _buffer.Length - 1);
            var done = kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
            var cancel = kb != null && kb.escapeKey.wasPressedThisFrame;
            foreach (var g in UnityEngine.InputSystem.Gamepad.all)
            {
                done |= g.buttonSouth.wasPressedThisFrame;
                cancel |= g.buttonEast.wasPressedThisFrame;
            }
            if (done) StopTyping(true);
            else if (cancel) StopTyping(false);
        }

        private void StopTyping(bool keep)
        {
            if (!_typing) return;
            _typing = Typing = false;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null) kb.onTextInput -= OnText;
            if (keep && _typed != null) _typed(_buffer);
            _typed = null;
            PlayerPrefs.Save();
        }

        private string CardsOnText()
        {
            if (Cards == null) return "";
            var on = 0;
            foreach (var c in Cards.Cards) if (Settings.CardOn(c.Id)) on++;
            return on + " of " + Cards.Cards.Count + " on";
        }

        /// <summary>Card text without its colour tags, for a hint line.</summary>
        private static string CleanText(string t)
        {
            if (string.IsNullOrEmpty(t)) return null;
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < t.Length; i++)
            {
                if (t[i] == '[') { var end = t.IndexOf(']', i); if (end > i) { i = end; continue; } }
                sb.Append(t[i] == '\n' ? ' ' : t[i]);
            }
            return sb.ToString();
        }

        private static string ZoomText(float v) { return v < 0.15f ? "Close" : v > 0.85f ? "Wide" : Mathf.RoundToInt(v * 100f) + "%"; }

        // ------------------------------------------------------------------ opening and closing

        public void OpenPause(string subtitle)
        {
            Current = Page.Pause;
            _subtitle = subtitle ?? "";
            _focus = 0;
            _openedAt = Time.unscaledTime;
            _focusY = -1f;
        }

        /// <summary>Which settings tab is showing (0 Graphics, 1 Gameplay, 2 Camera).</summary>
        public int Tab { get { return _tab; } set { _tab = Mathf.Clamp(value, 0, Tabs.Length - 1); _focus = 0; } }

        public void OpenSettings(bool fromLobby)
        {
            _settingsFromLobby = fromLobby;
            _tab = Mathf.Min(_tab, TabCount - 1);
            Current = Page.Settings;
            _focus = 0;
            _openedAt = Time.unscaledTime;
            _focusY = -1f;
        }

        public void Close()
        {
            StopTyping(false);
            Current = Page.None;
            PlayerPrefs.Save();
            if (OnResume != null) OnResume();
        }

        private void Back()
        {
            StopTyping(false);
            PlayerPrefs.Save();
            if (Current == Page.Settings && !_settingsFromLobby) { Current = Page.Pause; _focus = 0; _openedAt = Time.unscaledTime; _focusY = -1f; }
            else Close();
        }

        // ------------------------------------------------------------------ input

        /// <summary>Controller / keyboard input for this frame (from <see cref="LocalPlayers.Menu"/>).</summary>
        public void Tick(LocalPlayers.MenuInput m)
        {
            if (!IsOpen) { StopTyping(false); return; }
            if (_typing) { TypingTick(); return; }
            BuildRows();
            if (_rows.Count == 0) return;
            if (m.Back)
            {
                if (Current == Page.Cards) { Current = _cardsFrom; _focus = 0; _first = 0; _focusY = -1f; }
                else if (Current == Page.Match) { Current = Page.Pause; _focus = 0; _first = 0; _openedAt = Time.unscaledTime; _focusY = -1f; }
                else Back();
                return;
            }
            if (Current == Page.Settings && m.Tab != 0) { _tab = (_tab + m.Tab + TabCount) % TabCount; _focus = 0; _first = 0; return; }
            if (m.Y != 0)
            {
                // skip greyed-out rows
                for (var k = 0; k < _rows.Count; k++)
                {
                    _focus = (_focus - m.Y + _rows.Count) % _rows.Count;
                    if (_rows[_focus].Enabled()) break;
                }
            }
            _focus = Mathf.Clamp(_focus, 0, _rows.Count - 1);
            var row = _rows[_focus];
            if (!row.Enabled()) return;
            if (m.X != 0 && row.Step != null) row.Step(m.X);
            else if (m.Confirm && row.Press != null) row.Press();
        }

        // ------------------------------------------------------------------ drawing

        private void OnGUI()
        {
            if (!IsOpen) return;
            GUI.depth = 0;
            Ui.Begin();
            BuildRows();
            var t = Time.unscaledTime - _openedAt;
            var appear = Ui.EaseOutBack(t / 0.28f);
            // over a match: a dim, so you can still see it; from the menus: a plain backdrop
            if (_settingsFromLobby && Current != Page.Pause) Ui.Fill(Palette.Background);
            else Ui.Fill(new Color(0f, 0f, 0f, 0.55f * Ui.EaseOut(t / 0.2f)));

            var pause = Current == Page.Pause;
            var rowH = pause ? 54f : 50f;
            var panelW = pause ? 440f : 780f;
            var top = pause ? 110f : 150f;
            // the settings panel is as tall as its longest tab, so it doesn't jump about between tabs
            var matchPage = Current == Page.Match;
            var panelH = top + (pause ? _rows.Count : matchPage ? Mathf.Min(MaxVisible, _rows.Count) : Mathf.Min(MaxVisible, SettingsRows())) * (rowH + 8f) + (pause ? 30f : 92f);
            // long pages (the card list) scroll: keep the focused row in view
            var visible = pause ? _rows.Count : Mathf.Min(MaxVisible, _rows.Count);
            _first = Mathf.Clamp(_first, Mathf.Max(0, _focus - visible + 1), Mathf.Min(_focus, Mathf.Max(0, _rows.Count - visible)));
            var x = Ui.W / 2f - panelW / 2f;
            var y = Ui.H / 2f - panelH / 2f + (1f - appear) * 40f;

            // the panel pops in from slightly lower and smaller
            var pivot = new Vector2(Ui.W / 2f, Ui.H / 2f);
            var oldMatrix = GUI.matrix;
            var s = 0.94f + 0.06f * appear;
            GUIUtility.ScaleAroundPivot(new Vector2(s, s), pivot * (GUI.matrix.m00));
            Ui.Box(new Rect(x - 1f, y - 1f, panelW + 2f, panelH + 2f), new Color(1f, 1f, 1f, 0.06f), 22f);
            Ui.Box(new Rect(x, y, panelW, panelH), Ui.Panel, 21f);

            var cardsPage = Current == Page.Cards;
            Ui.Label(new Rect(x, y + 22f, panelW, 50f), pause ? "Paused" : cardsPage ? "Toggle cards" : matchPage ? "Match settings" : "Settings", Ui.Font(38, true, TextAnchor.MiddleCenter), Ui.Text);
            if (pause && _subtitle.Length > 0) Ui.Label(new Rect(x, y + 66f, panelW, 26f), _subtitle, Ui.Font(17, false, TextAnchor.MiddleCenter), Ui.TextDim);
            if (cardsPage) Ui.Label(new Rect(x, y + 82f, panelW, 40f), "Which cards can be offered" + (_rows.Count > visible ? "   (" + (_first + 1) + "-" + (_first + visible) + " of " + (_rows.Count - 1) + ")" : ""), Ui.Font(17, false, TextAnchor.MiddleCenter), Ui.TextDim);
            else if (matchPage) Ui.Label(new Rect(x, y + 82f, panelW, 40f), "Restart the match to use new settings", Ui.Font(17, false, TextAnchor.MiddleCenter), Ui.TextDim);
            else if (!pause) Tabs_(x, y + 82f, panelW);

            var mouse = Ui.Mouse;
            var rowsTop = y + top;
            var rowW = panelW - 60f;
            var last = Mathf.Min(_rows.Count, _first + visible);
            for (var i = _first; i < last; i++)
            {
                var r = new Rect(x + 30f, rowsTop + (i - _first) * (rowH + 8f), rowW, rowH);
                if (r.Contains(mouse) && Ui.MouseMoved && _rows[i].Enabled()) _focus = i;
            }
            if (Event.current.type == EventType.ScrollWheel && _rows.Count > visible)
                _focus = Mathf.Clamp(_focus + (Event.current.delta.y > 0 ? 1 : -1), 0, _rows.Count - 1);
            // the focus highlight glides between rows
            var focusTarget = rowsTop + (_focus - _first) * (rowH + 8f);
            _focusY = _focusY < 0f ? focusTarget : Mathf.Lerp(_focusY, focusTarget, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 22f));
            for (var i = _first; i < last; i++)
            {
                var r = new Rect(x + 30f, rowsTop + (i - _first) * (rowH + 8f), rowW, rowH);
                Ui.Box(r, Ui.Row, 12f);
            }
            Ui.Box(new Rect(x + 30f, _focusY, rowW, rowH), Ui.RowFocus, 12f);

            for (var i = _first; i < last; i++)
            {
                var r = new Rect(x + 30f, rowsTop + (i - _first) * (rowH + 8f), rowW, rowH);
                DrawRow(_rows[i], r, i == _focus, pause, mouse);
            }

            if (!pause)
            {
                var hint = _focus < _rows.Count && _rows[_focus].Hint != null ? _rows[_focus].Hint : "";
                Ui.Label(new Rect(x, y + panelH - 70f, panelW, 24f), hint, Ui.Font(16, false, TextAnchor.MiddleCenter), Ui.TextDim);
                Glyphs.Hint(new Rect(x, y + panelH - 40f, panelW, 24f),
                    _typing ? (InputMode.Pad ? "Type on the keyboard    {confirm} Done    {back} Cancel" : "Type your name    {confirm} Done    {back} Cancel")
                            : cardsPage ? "{nav} Scroll      {confirm} On / off      {back} Back" : matchPage ? "{nav} Choose      {side} Change      {back} Back" : "{tabs} Tabs      {back} Back      Changes save straight away",
                    14, new Color(1f, 1f, 1f, 0.45f));
            }
            GUI.matrix = oldMatrix;
        }

        private void Tabs_(float x, float y, float panelW)
        {
            const float tabW = 150f, tabH = 40f, gap = 10f;
            var total = TabCount * tabW + (TabCount - 1) * gap;
            var x0 = x + panelW / 2f - total / 2f;
            var target = x0 + _tab * (tabW + gap);
            _tabX = _tabX < 0f ? target : Mathf.Lerp(_tabX, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 20f));
            Ui.Box(new Rect(x0 - 6f, y - 6f, total + 12f, tabH + 12f), new Color(1f, 1f, 1f, 0.05f), 14f);
            Ui.Box(new Rect(_tabX, y, tabW, tabH), new Color(1f, 1f, 1f, 0.92f), 10f);
            for (var i = 0; i < TabCount; i++)
            {
                var r = new Rect(x0 + i * (tabW + gap), y, tabW, tabH);
                if (r.Contains(Ui.Mouse) && Ui.Clicked && Event.current.type == EventType.Repaint) { _tab = i; _focus = 0; }
                var on = i == _tab;
                Ui.Label(r, Tabs[i], Ui.Font(18, on, TextAnchor.MiddleCenter), on ? new Color(0.08f, 0.08f, 0.09f) : Ui.TextDim, !on);
            }
        }

        private void DrawRow(Row row, Rect r, bool focused, bool pause, Vector2 mouse)
        {
            var enabled = row.Enabled();
            var textCol = enabled ? (focused ? Ui.Text : new Color(1f, 1f, 1f, 0.8f)) : new Color(1f, 1f, 1f, 0.3f);
            var clicked = enabled && Ui.Clicked && r.Contains(mouse) && Event.current.type == EventType.Repaint;

            if (pause)
            {
                Ui.Label(r, row.Label + (row.Value != null ? ":  " + row.Value() : ""), Ui.Font(21, focused, TextAnchor.MiddleCenter), textCol);
                if (clicked)
                {
                    if (row.Step != null && row.Value != null) row.Step(mouse.x < r.center.x ? -1 : 1);
                    else if (row.Press != null) row.Press();
                }
                return;
            }

            Ui.Label(new Rect(r.x + 22f, r.y, r.width * 0.45f, r.height), row.Label, Ui.Font(19, focused), textCol);
            var right = new Rect(r.x + r.width * 0.48f, r.y, r.width * 0.52f - 22f, r.height);

            if (row.Toggle != null)
            {
                var on = row.Toggle();
                var sw = new Rect(right.xMax - 64f, r.center.y - 15f, 64f, 30f);
                Ui.Box(sw, on ? new Color(1f, 1f, 1f, enabled ? 0.9f : 0.3f) : Ui.Track, 15f);
                var knobX = on ? sw.xMax - 27f : sw.x + 3f;
                Ui.Box(new Rect(knobX, sw.y + 3f, 24f, 24f), on ? new Color(0.08f, 0.08f, 0.09f) : new Color(1f, 1f, 1f, enabled ? 0.85f : 0.3f), 12f);
                Ui.Label(new Rect(right.x, r.y, right.width - 80f, r.height), on ? "On" : "Off", Ui.Font(17, false, TextAnchor.MiddleRight), textCol);
                if (clicked) row.Press();
            }
            else if (row.Get01 != null)
            {
                var v = row.Get01();
                var track = new Rect(right.x + 10f, r.center.y - 4f, right.width - 110f, 8f);
                Ui.Box(track, Ui.Track, 4f);
                Ui.Box(new Rect(track.x, track.y, Mathf.Max(8f, track.width * v), track.height), new Color(1f, 1f, 1f, enabled ? 0.9f : 0.3f), 4f);
                var knob = new Rect(track.x + track.width * v - 11f, r.center.y - 11f, 22f, 22f);
                Ui.Box(knob, enabled ? Color.white : new Color(1f, 1f, 1f, 0.35f), 11f);
                Ui.Label(new Rect(track.xMax + 14f, r.y, 90f, r.height), row.Value(), Ui.Font(17, false, TextAnchor.MiddleRight), textCol);
                // click or drag along the track
                var grab = new Rect(track.x - 12f, r.y, track.width + 24f, r.height);
                if (enabled && Ui.MouseHeld && grab.Contains(mouse) && Event.current.type == EventType.Repaint)
                    row.Set01(Mathf.Clamp01((mouse.x - track.x) / track.width));
            }
            else if (row.Text)
            {
                var box = new Rect(right.x, r.y + 8f, right.width, r.height - 16f);
                var editing = _typing && focused;
                Ui.Box(box, new Color(1f, 1f, 1f, editing ? 0.16f : 0.06f), 10f);
                var caret = editing && (Time.unscaledTime * 2f) % 2f < 1.2f ? "|" : "";
                Ui.Label(new Rect(box.x + 14f, box.y, box.width - 28f, box.height), row.Value() + caret, Ui.Font(18, focused), textCol);
                if (clicked) row.Press();
            }
            else if (row.Value != null)
            {
                var col = textCol;
                Ui.Label(new Rect(right.x, r.y, right.width, r.height), "‹", Ui.Font(26, false, TextAnchor.MiddleLeft), new Color(col.r, col.g, col.b, col.a * 0.6f));
                Ui.Label(new Rect(right.x, r.y, right.width, r.height), row.Value(), Ui.Font(18, focused, TextAnchor.MiddleCenter), col);
                Ui.Label(new Rect(right.x, r.y, right.width, r.height), "›", Ui.Font(26, false, TextAnchor.MiddleRight), new Color(col.r, col.g, col.b, col.a * 0.6f));
                if (clicked) row.Step(mouse.x < right.center.x ? -1 : 1);
            }
            else if (clicked && row.Press != null) row.Press();
        }
    }
}
