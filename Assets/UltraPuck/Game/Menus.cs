using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using PlayerInput = UltraPuck.Core.PlayerInput;

namespace UltraPuck.Game
{
    /// <summary>
    /// The pause menu (Resume, Settings, Main menu, plus free play's extras) and the settings screen with its
    /// Graphics / Gameplay / Camera / Sound / Controls tabs. Works with a controller (stick or d-pad, A, B, LB / RB for tabs), the
    /// keyboard (arrows or WASD, Enter or Space, Esc, Q / E for tabs) and the mouse (hover, click, drag sliders).
    /// <see cref="GameRunner"/> opens it and fills in what the buttons do.
    /// </summary>
    internal sealed class Menus : MonoBehaviour
    {
        public enum Page { None, Pause, Settings, Cards, Match, Team, Crosshair }

        /// <summary>The card list for the Toggle Cards page (set by GameRunner).</summary>
        public UltraPuck.Core.CardLibrary Cards;

        public Page Current { get; private set; }
        public bool IsOpen { get { return Current != Page.None; } }

        /// <summary>
        /// True while the Music slider is the focused row on the Sound tab: the music plays at full volume then, so you
        /// can hear the level you're setting (elsewhere in the pause menu it plays quieter).
        /// </summary>
        public bool HearingMusic
        {
            get
            {
                if (Current != Page.Settings || _tab != 3 || _focus < 0 || _focus >= _rows.Count) return false;
                return _rows[_focus].Label == "Music";
            }
        }

        /// <summary>Waiting for a key or button to rebind (Controls tab).</summary>
        public bool Listening { get { return _listen.HasValue; } }
        private Controls.Act? _listen;

        // filled in by GameRunner
        public Action OnResume, OnMainMenu, OnGetCard;
        public Func<int> BotCount, MaxBots;
        public Action<int> SetBots;
        public bool FreePlay;
        /// <summary>The pause menu offers the match settings (the host online, anyone in a match on this PC).</summary>
        public Func<bool> CanEditMatch;
        /// <summary>Match settings: add a bot to a team now (0 red, 1 blue), and whether there's room for one.</summary>
        public Action<int> AddBotNow;
        public Func<bool> CanAddBot;
        /// <summary>Match settings: take a bot off a team (and whether one can go).</summary>
        public Action<int> RemoveBotNow;
        public Func<int, bool> CanRemoveBot;
        /// <summary>How many bots a team has right now.</summary>
        public Func<int, int> TeamBots;
        public Func<int, int> TeamCount;
        public Func<int, string> TeamName;
        /// <summary>Online host: the game, for the match settings' "Remove a player".</summary>
        public Func<UltraPuck.Core.Net.NetSession> HostSession;
        /// <summary>Match settings: send a host command (kick-off, score fix), and the score as it stands.</summary>
        public Action<byte> HostCommand;
        public Func<int, int> Score;
        public Func<int> MapCount, CurrentMap;
        public Func<int, string> MapName;
        /// <summary>Restart the match with the match settings as they are now, on this rink.</summary>
        public Action<int> OnRestart;
        /// <summary>The pause menu shows the match settings to look at (online, for everyone who isn't the host).</summary>
        public Func<bool> CanViewMatch;
        /// <summary>
        /// Not null: the match settings are someone else's (the online host's, as sent), shown but not changed here.
        /// </summary>
        public Func<byte[]> ShownRules;
        /// <summary>The match being played (null in the menus): Carry-through as it is right now.</summary>
        public Func<UltraPuck.Core.GameState> LiveState;
        /// <summary>Change team: this PC's people (by number), their names, teams (-1 watching), and changing it.</summary>
        public Func<List<int>> TeamPlayers;
        public Func<int, string> TeamPlayerName;
        public Func<int, int> TeamOfPlayer;
        public Action<int, int> SetTeamOf;
        /// <summary>Watching instead of playing is offered (online).</summary>
        public Func<bool> CanWatch;
        private int _sentCarry = -1, _sentKnock = -1;
        private int _pendingMap;

        private bool _settingsFromLobby;
        private int _focus, _tab;
        private float _openedAt, _focusY = -1f, _tabX = -1f;
        private string _subtitle = "";

        private static readonly string[] Tabs = { "Graphics", "Gameplay", "Camera", "Sound", "Controls", "Match" };
        /// <summary>Rows shown at once; longer pages scroll with the focus.</summary>
        private const int MaxVisible = 8;
        private int _first;
        // the focus the scroll last followed: the view only jumps to the focus when it moves (the wheel scrolls freely)
        private int _firstFocus = -1;

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
            public Func<string> GlyphText;        // Controls rebinds: the binding as button pictures (instead of Value)
            public Action Clear;                  // Controls rebinds: Y / Triangle takes the binding off
            public Action<Rect> Picture;          // drawn on the right instead of Value (the crosshair in use)
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
                    if (MapCount != null && MapCount() > 0)
                    {
                        _rows.Add(new Row
                        {
                            Label = "Rink",
                            Value = () => MapName != null ? MapName(_pendingMap) : "",
                            Step = d => FreePlayRink(d),
                            Press = () => FreePlayRink(1),
                            Hint = "Switches rink right away (starts free play again on the new one).",
                        });
                    }
                    _rows.Add(Button("Cards", () => { Close(); if (OnGetCard != null) OnGetCard(); }));
                }
                if (!FreePlay && ((CanEditMatch != null && CanEditMatch()) || (CanViewMatch != null && CanViewMatch()))) _rows.Add(Button("Match settings", OpenMatch));
                if (!FreePlay && TeamPlayers != null && TeamPlayers().Count > 0) _rows.Add(Button("Change team", OpenTeam));
                _rows.Add(Button("Settings", () => OpenSettings(false)));
                _rows.Add(Button("Main menu", () => { Close(); if (OnMainMenu != null) OnMainMenu(); }));
                return;
            }
            if (Current == Page.Team)
            {
                TeamRows();
                _rows.Add(Button("Back", ToPause));
                return;
            }
            if (Current == Page.Match && ReadOnly)
            {
                // someone else's (the host's): to look at
                if (MapCount != null && MapCount() > 0 && CurrentMap != null)
                    _rows.Add(new Row { Label = "Rink", Value = () => MapName(CurrentMap()), Hint = "Only the host can change the match settings." });
                MatchRows();
                _rows.Add(Button("Back", ToPause));
                return;
            }
            if (Current == Page.Match)
            {
                _rows.Add(new Row { Label = "Restart match with these settings", Press = () => { var map = _pendingMap; Close(); if (OnRestart != null) OnRestart(map); },
                    Hint = "Starts the match again from 0 : 0 with the settings below. Online, for everyone." });
                if (HostCommand != null)
                {
                    _rows.Add(new Row { Label = "Restart from the kick-off", Press = () => { Sound.Select(); HostCommand(PlayerInput.KickOff); Close(); },
                        Hint = "Everyone back to their spots and the puck to the middle, then the countdown. The score and cards stay." });
                    for (var t = 0; t < 2; t++)
                    {
                        var team = t;
                        _rows.Add(new Row
                        {
                            Label = (TeamName != null ? TeamName(team) : team == 0 ? "Red" : "Blue") + " score",
                            Value = () => Score != null ? Score(team).ToString() : "",
                            Step = d =>
                            {
                                if (d < 0 && Score != null && Score(team) <= 0) return;
                                HostCommand(team == 0 ? (d > 0 ? PlayerInput.RedUp : PlayerInput.RedDown) : (d > 0 ? PlayerInput.BlueUp : PlayerInput.BlueDown));
                            },
                            Press = () => HostCommand(team == 0 ? PlayerInput.RedUp : PlayerInput.BlueUp),
                            Hint = "Fix the score if something went wrong (left / right). Changes straight away.",
                        });
                    }
                }
                if (MapCount != null && MapCount() > 0)
                    _rows.Add(new Row
                    {
                        Label = "Rink", Value = () => MapName(_pendingMap),
                        Step = d => _pendingMap = (_pendingMap + d + MapCount()) % MapCount(),
                        Press = () => _pendingMap = (_pendingMap + 1) % MapCount(),
                        Hint = "Changes when the match restarts.",
                    });
                _rows.Add(new Row { Label = "Match", Value = () => Settings.MatchText, Step = Settings.StepMatch, Press = () => Settings.StepMatch(1), Hint = "First to so many goals, or a time limit." });
                if (AddBotNow != null && CanAddBot != null)
                    for (var t = 0; t < 2; t++)
                    {
                        var team = t;
                        System.Action<int> step = d =>
                        {
                            if (d > 0) { if (CanAddBot()) { Sound.Select(); AddBotNow(team); } else Sound.Deny(); }
                            else if (d < 0) { if (RemoveBotNow != null && CanRemoveBot != null && CanRemoveBot(team)) { Sound.Select(); RemoveBotNow(team); } else Sound.Deny(); }
                        };
                        _rows.Add(new Row
                        {
                            Label = (TeamName != null ? TeamName(team) : team == 0 ? "Red" : "Blue") + " bots",
                            Value = () => { var n = TeamBots != null ? TeamBots(team) : 0; return (n == 1 ? "1 bot" : n + " bots") + (CanAddBot() ? "" : "  (match full)"); },
                            Step = step,
                            Press = () => step(1),
                            Hint = "Right adds a bot to this team, left takes one off. Straight away.",
                        });
                    }
                var hosting = HostSession != null ? HostSession() : null;
                if (hosting != null)
                    _rows.Add(new Row
                    {
                        Label = "Remove a player", Value = () => MainMenu.KickRow.Value(hosting), Step = MainMenu.KickRow.Step,
                        Press = () => MainMenu.KickRow.Press(hosting),
                        Hint = "Left / right picks someone on another PC; press twice to remove them from the game. A bot plays for them for a moment, then their player goes.",
                    });
                MatchRows();
                _rows.Add(Button("Back", ToPause));
                return;
            }
            if (Current == Page.Cards)
            {
                if (Cards != null)
                {
                    _rows.Add(Button("All on", () => { foreach (var c in Cards.Cards) Settings.SetCardOn(c.Id, true); }));
                    _rows[_rows.Count - 1].Hint = "Every card can be offered.";
                    _rows.Add(Button("All off", () => { foreach (var c in Cards.Cards) Settings.SetCardOn(c.Id, false); }));
                    _rows[_rows.Count - 1].Hint = "No card is offered (then turn on the ones you want). With none on, there's no card pick.";
                }
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
                    var sizes = Settings.Resolutions();
                    var sizeNames = new string[sizes.Length];
                    for (var k = 0; k < sizes.Length; k++) sizeNames[k] = sizes[k].x + " × " + sizes[k].y;
                    Func<int> sizeAt = () =>
                    {
                        var now = Settings.Resolution.x > 0 ? Settings.Resolution : new Vector2Int(Screen.width, Screen.height);
                        var at = Array.IndexOf(sizes, now);
                        return at >= 0 ? at : sizes.Length - 1;
                    };
                    var res = Choice("Resolution", sizeNames, sizeAt, v => Settings.Resolution = sizes[v], "How many pixels the game draws. Lower is faster on slow PCs.");
                    // left is lower, right is higher, and it stops at each end (no jumping from the biggest to the smallest)
                    res.Step = d => { var to = Mathf.Clamp(sizeAt() + d, 0, sizes.Length - 1); if (to != sizeAt()) Settings.Resolution = sizes[to]; };
                    _rows.Add(res);
                    const float cursorSpan = Settings.CursorMax - Settings.CursorMin;
                    var cursor = Slider("Mouse cursor size", () => (Settings.CursorSize - Settings.CursorMin) / cursorSpan,
                        v => Settings.CursorSize = Settings.CursorMin + Mathf.RoundToInt(v * cursorSpan / 10f) * 10,
                        v => Settings.CursorSize + "%", "How big the mouse pointer is in the menus: 100% is the normal size.");
                    cursor.Step = d => Settings.CursorSize = Settings.CursorSize + d * 10;
                    _rows.Add(cursor);
                    _rows.Add(Switch("V-Sync", () => Settings.VSync, v => Settings.VSync = v, "Matches your monitor's refresh rate and stops tearing."));
                    var caps = new string[Settings.FpsCaps.Length];
                    for (var k = 0; k < caps.Length; k++) caps[k] = Settings.FpsCaps[k] == 0 ? "Unlimited" : Settings.FpsCaps[k] + " fps";
                    var cap = Choice("Frame rate limit", caps, () => Mathf.Max(0, Array.IndexOf(Settings.FpsCaps, Settings.FpsCap)), v => Settings.FpsCap = Settings.FpsCaps[v], "Only used with V-Sync off.");
                    cap.Enabled = () => !Settings.VSync;
                    _rows.Add(cap);
                    _rows.Add(Switch("Show FPS", () => Settings.ShowFps, v => Settings.ShowFps = v, "A small frame-rate number in the top-left corner."));
                    _rows.Add(Slider("UI size", () => (Settings.UiScale - 0.6f) / 1.4f, v => Settings.UiScale = 0.6f + v * 1.4f, v => Mathf.RoundToInt((0.6f + v * 1.4f) * 100f) + "%"));
                    _rows.Add(Switch("Puck trail", () => Settings.PuckTrail, v => Settings.PuckTrail = v));
                    _rows.Add(Switch("Card effects", () => Settings.CardEffects, v => Settings.CardEffects = v, "Holo, foil and polychrome finishes, glare and sparkle on cards."));
                    _rows.Add(Switch("Other players' arenas", () => Settings.OtherArenas, v => Settings.OtherArenas = v,
                        "Online: see the host's custom arena (downloaded from them the first time). Off: you see the plain rink."));
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
                    _rows.Add(Switch("Swap trail", () => Settings.SwapTrail, v => Settings.SwapTrail = v, "When you swap to a bot teammate, a trail shows where you went."));
                    _rows.Add(Choice("Check bar", new[] { "While recharging", "Always" }, () => Settings.DashBarAlways ? 1 : 0, v => Settings.DashBarAlways = v == 1));
                    _rows.Add(Choice("Corner card list", new[] { "Shown", "Hidden" }, () => Settings.CornerCards ? 0 : 1, v => Settings.CornerCards = v == 0,
                        "Everyone's cards in the lower-left corner during a match. They always show while you hold Tab / View for the scoreboard."));
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
                    _rows.Add(Slider("Aim line", () => Settings.AimLine, v => Settings.AimLine = v,
                        v => v < 0.01f ? "Off" : Mathf.RoundToInt(v * 100f) + "%",
                        "How long the shot aim line is while you have the puck. 100% is the original length."));
                    _rows.Add(Switch("Swap rock line", () => Settings.RockLine, v => Settings.RockLine = v,
                        "A faint dotted line from you to your Swap rock while it's on the ice."));
                    break;
                case 3:
                    _rows.Add(Slider("Volume", () => Settings.Volume, v => Settings.Volume = v, Percent, "How loud the whole game is."));
                    _rows.Add(Slider("Music", () => Settings.MusicVolume, v => Settings.MusicVolume = v, Percent, "How loud the music is next to everything else."));
                    _rows.Add(Slider("Sound effects", () => Settings.SoundVolume, v => Settings.SoundVolume = v, Percent, "Hits, shots, goals, cards and menu sounds."));
                    // (each choice plays as you pick it: a soft bounce, then a hard one)
                    _rows.Add(Choice("Puck bounce", Settings.PuckBounceNames, () => Settings.PuckBounceSound,
                        v => { Settings.PuckBounceSound = v; Sound.Bounce(1f, 0f); },
                        "The sound of the puck bouncing off the boards: lower and quieter when it's slow, higher when it's fast."));
                    _rows.Add(Choice("Chaos card", Settings.ChaosSoundNames, () => Settings.ChaosSound,
                        v => { Settings.ChaosSound = v; Sound.Chaos(v); },
                        "The sound a chaos card makes when it flies in from the corner."));
                    _rows.Add(Switch("Pause music when unfocused", () => Settings.PauseMusicWhenUnfocused, v => Settings.PauseMusicWhenUnfocused = v,
                        "When the game window isn't focused: pause the music. Leave this off and the next song still starts if one ends while you're looking elsewhere."));
                    _rows.Add(Switch("Music in clips", () => Settings.MusicInClips, v => Settings.MusicInClips = v,
                        "Replay clips you save have the song that was playing in them (as loud as the Music slider). Off: only the game's sounds."));
                    break;
                case 4:
                    ControlsRows();
                    break;
                case 5:
                    MatchRows();
                    break;
            }
            _rows.Add(Button("Back", Back));
        }

        private void ControlsRows()
        {
            _rows.Add(Switch("Auto swap to bots", () => Settings.AutoSwapBots, v => Settings.AutoSwapBots = v,
                "When a bot on your team picks up the puck, you take over that bot."));
            _rows.Add(Choice("Dash direction", new[] { "Where you move", "Where you look" }, () => Settings.DashWhereMoving ? 0 : 1, v => Settings.DashWhereMoving = v == 0,
                "Which way a check dashes. Where you move: along the move stick (where you look if it's still)."));
            _rows.Add(new Row
            {
                Label = "Change crosshair",
                Value = () => "",
                Press = OpenCrosshair,
                Picture = r =>
                {
                    if (Event.current.type != EventType.Repaint) return;
                    var c = Settings.ReticleTint(0); c.a = 1f;
                    Hud.DrawCrosshair(Settings.Crosshair, new Vector2(r.xMax - 22f, r.center.y), 36f, c);
                },
                Hint = "The aim marker while you play with keyboard and mouse.",
            });
            const float span = Settings.CrosshairMax - Settings.CrosshairMin;
            var size = Slider("Crosshair size", () => (Settings.CrosshairSize - Settings.CrosshairMin) / span,
                v => Settings.CrosshairSize = Settings.CrosshairMin + Mathf.RoundToInt(v * span / 10f) * 10,
                v => Settings.CrosshairSize + "%", "How big the crosshair is: 100% is the normal size, up to 1000%.");
            size.Step = d => Settings.CrosshairSize = Settings.CrosshairSize + d * 10;
            _rows.Add(size);
            _rows.Add(Choice("Crosshair colour", Settings.ReticleColourNames, () => Settings.ReticleColour, v => Settings.ReticleColour = v));
            foreach (Controls.Act a in Enum.GetValues(typeof(Controls.Act)))
            {
                var act = a;
                _rows.Add(new Row
                {
                    Label = Controls.Label(act),
                    Value = () => _listen == act ? "Press a key or button…" : Controls.BindingText(act),
                    GlyphText = () => _listen == act ? null : Glyphs.BindingHint(act),
                    Press = () => { _listen = act; },
                    Clear = () => Controls.Clear(act),
                    Hint = Controls.Hint(act),
                });
            }
            _rows.Add(new Row
            {
                Label = "Reset to defaults",
                Press = () => { Controls.ResetDefaults(); _listen = null; },
                Hint = "Put every binding back the way it ships.",
            });
        }

        /// <summary>The match settings are the online host's: shown, not changed here.</summary>
        private bool ReadOnly { get { return ShownRules != null && ShownRules() != null; } }

        private void ToPause() { Current = Page.Pause; _focus = 0; _first = 0; _openedAt = Time.unscaledTime; _focusY = -1f; }

        public void OpenTeam()
        {
            Current = Page.Team;
            _focus = 0; _first = 0; _focusY = -1f;
            _openedAt = Time.unscaledTime;
        }

        /// <summary>Change team: a row for each person on this PC (left / right picks a side, or watching online).</summary>
        private void TeamRows()
        {
            var people = TeamPlayers != null ? TeamPlayers() : new List<int>();
            var watch = CanWatch != null && CanWatch();
            for (var n = 0; n < people.Count; n++)
            {
                var k = n;
                Func<int> get = () => TeamOfPlayer(k);
                Func<int, string> name = t => t < 0 ? "Just watching" : TeamName != null ? TeamName(t) : t == 0 ? "Red" : "Blue";
                // the choices in order: red, blue, (watching)
                var count = watch ? 3 : 2;
                Func<int> at = () => get() < 0 ? 2 : get();
                Action<int> pick = i => { var team = i == 2 ? -1 : i; if (team != get()) SetTeamOf(k, team); };
                _rows.Add(new Row
                {
                    Label = people.Count == 1 ? "Your team" : TeamPlayerName(k),
                    Value = () => name(get()),
                    Step = d => pick(((at() + d) % count + count) % count),
                    Press = () => pick((at() + 1) % count),
                    Hint = watch ? "Left / right: the other team, or just watch the match (pick a team again to come back in)." : "Left / right: play for the other team.",
                });
            }
        }

        private static string OnOff(bool v) { return v ? "On" : "Off"; }

        /// <summary>The host's settings, to look at: plain rows (nothing to change).</summary>
        private void ShownMatchRows()
        {
            var r = RulesView.Read(ShownRules());
            const string hint = "Only the host can change the match settings.";
            if (r == null) { _rows.Add(new Row { Label = "Waiting for the host's settings...", Hint = hint }); return; }
            _rows.Add(new Row { Label = "Match", Value = () => r.MatchText, Hint = hint });
            _rows.Add(new Row { Label = "Rounds mode", Value = () => OnOff(r.Rounds), Hint = hint });
            _rows.Add(new Row { Label = "Goal replays", Value = () => OnOff(r.Replays), Hint = hint });
            _rows.Add(new Row { Label = "Picking", Value = () => Settings.PickModes[Mathf.Clamp(r.PickMode, 0, Settings.PickModes.Length - 1)], Hint = hint });
            _rows.Add(new Row { Label = "Cards offered", Value = () => r.CardsOffered.ToString(), Hint = hint });
            _rows.Add(new Row { Label = "Pick time", Value = () => r.PickTime == 0 ? "No limit" : r.PickTime + " s", Hint = hint });
            _rows.Add(new Row { Label = "Multiple utilities", Value = () => OnOff(r.MultiUtility), Hint = hint });
            _rows.Add(new Row { Label = "Chaos cards stack", Value = () => OnOff(r.ChaosStack), Hint = hint });
            if (Cards != null) _rows.Add(new Row { Label = "Cards", Value = () => Mathf.Max(0, Cards.Cards.Count - r.CardsOff) + " of " + Cards.Cards.Count + " on", Hint = hint });
            // Carry-through can change mid-match: the match's own when there is one
            _rows.Add(new Row { Label = "Carry-through", Value = () => { var s = Live(); return OnOff(s != null ? s.CarryOn : r.Carry); }, Hint = hint });
            _rows.Add(new Row { Label = "Carry-through strength", Value = () => { var s = Live(); return (s != null ? s.CarryPercent : r.Carry ? r.CarryPercent : Settings.CarryPercent) + "%"; }, Hint = hint });
            _rows.Add(new Row { Label = "Knock-away", Value = () => { var s = Live(); return (s != null ? s.KnockPercent : r.KnockPercent) + "%"; }, Hint = hint });
        }

        private UltraPuck.Core.GameState Live() { return LiveState != null ? LiveState() : null; }

        /// <summary>Pause menu's match settings, mid-match: Carry-through changes for everyone straight away (a host command).</summary>
        private void SendCarry(bool strength)
        {
            if (Current != Page.Match || HostCommand == null || Live() == null) return;
            if (!strength) { HostCommand(Settings.CarryThrough ? PlayerInput.CarryOn : PlayerInput.CarryOff); return; }
            var pct = Settings.CarryPercent;
            if (pct == _sentCarry && Live().CarryPercent == pct) return;
            _sentCarry = pct;
            HostCommand((byte)(PlayerInput.CarryStrength + pct / 5));
        }

        /// <summary>Pause menu's match settings, mid-match: Knock-away changes for everyone straight away (a host command).</summary>
        private void SendKnock()
        {
            if (Current != Page.Match || HostCommand == null || Live() == null) return;
            var pct = Settings.KnockAwayPercent;
            if (pct == _sentKnock && Live().KnockPercent == pct) return;
            _sentKnock = pct;
            HostCommand((byte)(PlayerInput.KnockAway + pct / 25));
        }

        /// <summary>The match's rules (the Match tab, and the pause menu's match settings).</summary>
        private void MatchRows()
        {
            if (ReadOnly) { ShownMatchRows(); return; }
            _rows.Add(Switch("Rounds mode", () => Settings.Rounds, v => Settings.Rounds = v, "After each goal, the team that conceded picks a card that powers them up for the rest of the match."));
            _rows.Add(Switch("Goal replays", () => Settings.Replays, v => Settings.Replays = v, "Every goal is shown again, up to 10 seconds back, in slow motion on the big moments. Everyone pressing A skips it (bots always agree)."));
            var vote = Choice("Picking", Settings.PickModes, () => Settings.PickMode, v => Settings.PickMode = v,
                "Each player: everyone draws and takes their own card. Same draw: your team is offered the same cards, each takes their own. Team vote: each team votes on one card the whole team gets (a tie is random).");
            vote.Enabled = () => Settings.Rounds;
            _rows.Add(vote);
            var offered = new string[Settings.OfferCounts.Length];
            for (var k = 0; k < offered.Length; k++) offered[k] = Settings.OfferCounts[k].ToString();
            _rows.Add(Choice("Cards offered", offered, () => Mathf.Max(0, Array.IndexOf(Settings.OfferCounts, Settings.CardsOffered)), v => Settings.CardsOffered = Settings.OfferCounts[v]));
            var times = new string[Settings.PickTimes.Length];
            for (var k = 0; k < times.Length; k++) times[k] = Settings.PickTimes[k] == 0 ? "No limit" : Settings.PickTimes[k] + " s";
            _rows.Add(Choice("Pick time", times, () => Mathf.Max(0, Array.IndexOf(Settings.PickTimes, Settings.PickTime)), v => Settings.PickTime = Settings.PickTimes[v],
                "How long players have to pick. Anyone who hasn't picked in time gets a random card."));
            _rows.Add(Switch("Multiple utilities", () => Settings.MultiUtility, v => Settings.MultiUtility = v,
                "Off: you can only hold one utility card (Puck Gun, Invisibility, Shockwave, Swap). Taking another replaces the one you have. On: you can hold several."));
            _rows.Add(Switch("Chaos cards stack", () => Settings.ChaosStack, v => Settings.ChaosStack = v,
                "Off: each chaos card comes once a match (once someone has it, it isn't offered again). On: they can come again and add up (two More Pucks: two extra pucks). Changes when the match restarts."));
            _rows.Add(new Row { Label = "Toggle cards", Value = CardsOnText, Press = () => { _cardsFrom = Current; Current = Page.Cards; _focus = 0; _first = 0; _focusY = -1f; },
                Hint = "Choose which cards can be offered. Online, the host's choices apply." });
            _rows.Add(Switch("Carry-through", () => Settings.CarryThrough, v => { Settings.CarryThrough = v; SendCarry(false); },
                "A pass or shot that reaches another player (on either team) takes them along with some of the puck's speed, like Cannon Ball. Mid-match it changes straight away."));
            var carry = Slider("Carry-through strength", () => (Settings.CarryPercent - 5) / 95f,
                v => { Settings.CarryPercent = Mathf.RoundToInt((5f + v * 95f) / 5f) * 5; SendCarry(true); },
                v => Settings.CarryPercent + "%",
                "How much of the puck's speed goes into the player who gets it (for trying out values).");
            carry.Step = d => { Settings.CarryPercent = Mathf.Clamp(Settings.CarryPercent + d * 5, 5, 100); SendCarry(true); };
            carry.Enabled = () => Settings.CarryThrough;
            _rows.Add(carry);
            var knock = Slider("Knock-away", () => Settings.KnockAwayPercent / 500f,
                v => { Settings.KnockAwayPercent = Mathf.RoundToInt(v * 20f) * 25; SendKnock(); },
                v => Settings.KnockAwayPercent + "%",
                "How far the puck flies away from its carrier when they get checked (100% is a plain check, 350% the default; for trying out values). Mid-match it changes straight away.");
            knock.Step = d => { Settings.KnockAwayPercent = Mathf.Clamp(Settings.KnockAwayPercent + d * 25, 0, 500); SendKnock(); };
            _rows.Add(knock);
        }

        private Page _cardsFrom = Page.Settings;

        /// <summary>"12 of 20 on", for the card list's heading.</summary>
        private string CardsOnCount()
        {
            if (Cards == null) return "";
            var on = 0;
            foreach (var c in Cards.Cards) if (Settings.CardOn(c.Id)) on++;
            return on + " of " + Cards.Cards.Count + " on";
        }

        /// <summary>Open the match settings page (for the screenshots).</summary>
        public void ShowMatchSettings() { OpenMatch(); }

        /// <summary>Screenshots: the focus on the row with this label ("": the last row).</summary>
        public void ShotFocus(string label) { _shotFocus = label; }
        private string _shotFocus;

        /// <summary>Open the card list from the match settings (for the screenshots).</summary>
        public void ShowCardList() { _cardsFrom = Current; Current = Page.Cards; _focus = 0; _first = 0; _focusY = -1f; }

        public void OpenMatch()
        {
            _sentCarry = -1; _sentKnock = -1;
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

        private static string Percent(float v) { return v < 0.005f ? "Off" : Mathf.RoundToInt(v * 100f) + "%"; }

        /// <summary>The sound for pressing a row: going back sounds different from choosing.</summary>
        private static void PressSound(Row row) { if (row.Label == "Back") Sound.Back(); else Sound.Select(); }

        // ------------------------------------------------------------------ opening and closing

        public void OpenPause(string subtitle)
        {
            Current = Page.Pause;
            _subtitle = subtitle ?? "";
            _pendingMap = CurrentMap != null ? CurrentMap() : 0;
            _focus = 0;
            _openedAt = Time.unscaledTime;
            _focusY = -1f;
        }

        /// <summary>Free play pause: cycle the rink and switch to it straight away (no Confirm button).</summary>
        private void FreePlayRink(int delta)
        {
            if (MapCount == null || MapCount() <= 0) return;
            _pendingMap = (_pendingMap + delta + MapCount()) % MapCount();
            if (OnRestart != null) OnRestart(_pendingMap);
            // keep the pause menu on the new rink (StartMatch doesn't close it)
            _pendingMap = CurrentMap != null ? CurrentMap() : _pendingMap;
        }

        /// <summary>Which settings tab is showing (0 Graphics … 4 Controls, 5 Match).</summary>
        public int Tab { get { return _tab; } set { _tab = Mathf.Clamp(value, 0, Tabs.Length - 1); _focus = 0; _listen = null; } }

        public void OpenSettings(bool fromLobby)
        {
            _settingsFromLobby = fromLobby;
            _tab = Mathf.Min(_tab, TabCount - 1);
            Current = Page.Settings;
            _focus = 0;
            _listen = null;
            _openedAt = Time.unscaledTime;
            _focusY = -1f;
        }

        public void Close()
        {
            StopTyping(false);
            _listen = null;
            Current = Page.None;
            PlayerPrefs.Save();
            if (OnResume != null) OnResume();
        }

        private void Back()
        {
            StopTyping(false);
            _listen = null;
            PlayerPrefs.Save();
            if (Current == Page.Settings && !_settingsFromLobby) { Current = Page.Pause; _focus = 0; _openedAt = Time.unscaledTime; _focusY = -1f; }
            else Close();
        }

        // ------------------------------------------------------------------ input

        /// <summary>Controller / keyboard input for this frame (from <see cref="LocalPlayers.Menu"/>).</summary>
        public void Tick(LocalPlayers.MenuInput m)
        {
            if (!IsOpen) { StopTyping(false); _listen = null; return; }
            if (_typing) { TypingTick(); return; }
            if (_listen.HasValue) { ListenTick(m); return; }
            if (Current == Page.Crosshair) { CrosshairTick(m); return; }
            BuildRows();
            if (_rows.Count == 0) return;
            if (m.Back)
            {
                Sound.Back();
                if (Current == Page.Cards) { Current = _cardsFrom; _focus = 0; _first = 0; _focusY = -1f; }
                else if (Current == Page.Match || Current == Page.Team) ToPause();
                else Back();
                return;
            }
            if (Current == Page.Settings && m.Tab != 0) { _tab = (_tab + m.Tab + TabCount) % TabCount; _focus = 0; _first = 0; Sound.Move(); return; }
            if (m.Y != 0)
            {
                var was = _focus;
                // skip greyed-out rows
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
            if (row.Clear != null && NorthPressed()) { row.Clear(); Sound.Back(); return; }
            if (m.X != 0 && row.Step != null) { row.Step(m.X); Sound.Step(m.X); } // after the change: a volume slider ticks at its new volume
            else if (m.Confirm && row.Press != null) { PressSound(row); row.Press(); }
        }

        private void ListenTick(LocalPlayers.MenuInput m)
        {
            if (!_listen.HasValue) return;
            var act = _listen.Value;
            // only Esc cancels: B / Circle is a button like any other here (it can be bound)
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                Sound.Back();
                _listen = null;
                return;
            }
            var key = Controls.ReadKeyPress(kb);
            if (key == Key.None) key = Controls.ReadMousePress(); // a mouse button (the side ones too)
            if (key != Key.None && key != Key.Escape)
            {
                Controls.SetKey(act, key);
                Sound.Select();
                _listen = null;
                return;
            }
            if (Controls.KeyboardOnly(act))
            {
                // pad sticks cover move/aim: a controller can't bind these, so its B just cancels
                if (m.Back) { Sound.Back(); _listen = null; }
                return;
            }
            foreach (var g in Gamepad.all)
            {
                var p = Controls.ReadPadPress(g);
                if (!p.HasValue) continue;
                Controls.SetPad(act, p.Value);
                Sound.Select();
                _listen = null;
                return;
            }
        }

        // ------------------------------------------------------------------ drawing

        private void OnGUI()
        {
            if (!IsOpen) return;
            GUI.depth = 0;
            Ui.Begin();
            BuildRows();
            if (_shotFocus != null && _rows.Count > 0)
            {
                var k = _shotFocus == "" ? _rows.Count - 1 : _rows.FindIndex(r => r.Label == _shotFocus);
                if (k >= 0) { _focus = k; _focusY = -1f; }
                _shotFocus = null;
            }
            var t = Time.unscaledTime - _openedAt;
            if (Current == Page.Crosshair) { CrosshairPage(t); return; }
            var appear = Ui.EaseOutBack(t / 0.28f);
            // over a match: a dim, so you can still see it; from the menus: a plain backdrop
            if (_settingsFromLobby && Current != Page.Pause) Ui.Fill(Palette.Background);
            else Ui.Fill(new Color(0f, 0f, 0f, 0.55f * Ui.EaseOut(t / 0.2f)));

            var pause = Current == Page.Pause;
            var rowH = pause ? 54f : 50f;
            var panelW = pause ? 440f : 780f;
            var top = pause ? 110f : 150f;
            // the settings panel is as tall as its longest tab, so it doesn't jump about between tabs
            var matchPage = Current == Page.Match || Current == Page.Team;
            var teamPage = Current == Page.Team;
            var panelH = top + (pause ? _rows.Count : matchPage ? Mathf.Min(MaxVisible, _rows.Count) : Mathf.Min(MaxVisible, SettingsRows())) * (rowH + 8f) + (pause ? 30f : 92f);
            // long pages (the card list) scroll: keep the focused row in view
            var visible = pause ? _rows.Count : Mathf.Min(MaxVisible, _rows.Count);
            if (_focus != _firstFocus)
            {
                _first = Mathf.Clamp(_first, Mathf.Max(0, _focus - visible + 1), Mathf.Min(_focus, Mathf.Max(0, _rows.Count - visible)));
                _firstFocus = _focus;
            }
            // the mouse wheel scrolls the rows (the focus stays where it is)
            if (Event.current.type == EventType.ScrollWheel && _rows.Count > visible)
            {
                _first += Event.current.delta.y > 0 ? 1 : -1;
                Event.current.Use();
            }
            _first = Mathf.Clamp(_first, 0, Mathf.Max(0, _rows.Count - visible));
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
            Ui.Label(new Rect(x, y + 22f, panelW, 50f), pause ? "Paused" : cardsPage ? "Toggle cards" : teamPage ? "Change team" : matchPage ? "Match settings" : "Settings", Ui.Font(38, true, TextAnchor.MiddleCenter), Ui.Text);
            if (pause && _subtitle.Length > 0) Ui.Label(new Rect(x, y + 66f, panelW, 26f), _subtitle, Ui.Font(17, false, TextAnchor.MiddleCenter), Ui.TextDim);
            if (cardsPage) Ui.Label(new Rect(x, y + 82f, panelW, 40f), "Which cards can be offered   (" + CardsOnCount() + ")", Ui.Font(17, false, TextAnchor.MiddleCenter), Ui.TextDim);
            else if (matchPage) Ui.Label(new Rect(x, y + 82f, panelW, 40f),
                teamPage ? "Pick a side for each of you" : ReadOnly ? "The host's settings (only the host can change them)" : "Restart the match to use new settings",
                Ui.Font(17, false, TextAnchor.MiddleCenter), Ui.TextDim);
            else if (!pause) Tabs_(x, y + 82f, panelW);

            var mouse = Ui.Mouse;
            var rowsTop = y + top;
            var rowW = panelW - 60f;
            var last = Mathf.Min(_rows.Count, _first + visible);
            for (var i = _first; i < last; i++)
            {
                var r = new Rect(x + 30f, rowsTop + (i - _first) * (rowH + 8f), rowW, rowH);
                // hover focuses; a resting mouse doesn't steal focus from the pad
                if (r.Contains(mouse) && (Ui.MouseMoved || Ui.Clicked) && _rows[i].Enabled() && _focus != i) { _focus = i; if (Ui.MouseMoved) Sound.Move(); }
            }
            // the focus highlight glides between rows
            var focusTarget = rowsTop + (_focus - _first) * (rowH + 8f);
            _focusY = _focusY < 0f ? focusTarget : Mathf.Lerp(_focusY, focusTarget, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 22f));
            for (var i = _first; i < last; i++)
            {
                var r = new Rect(x + 30f, rowsTop + (i - _first) * (rowH + 8f), rowW, rowH);
                Ui.Box(r, Ui.Row, 12f);
            }
            if (_focus >= _first && _focus < last) Ui.Box(new Rect(x + 30f, _focusY, rowW, rowH), Ui.RowFocus, 12f); // (scrolled out of view: no highlight)

            for (var i = _first; i < last; i++)
            {
                var r = new Rect(x + 30f, rowsTop + (i - _first) * (rowH + 8f), rowW, rowH);
                DrawRow(_rows[i], r, i == _focus, pause, mouse);
            }
            // more rows than fit: a scroll bar down the right
            if (_rows.Count > visible)
            {
                var trackH = visible * (rowH + 8f) - 8f;
                var track = new Rect(x + panelW - 20f, rowsTop, 6f, trackH);
                Ui.Box(track, new Color(1f, 1f, 1f, 0.1f), 3f);
                var thumbH = Mathf.Max(24f, trackH * visible / _rows.Count);
                var at = (float)_first / Mathf.Max(1, _rows.Count - visible);
                Ui.Box(new Rect(track.x, track.y + (trackH - thumbH) * at, track.width, thumbH), new Color(1f, 1f, 1f, 0.55f), 3f);
            }

            if (!pause)
            {
                var hint = _focus < _rows.Count && _rows[_focus].Hint != null ? _rows[_focus].Hint : "";
                Ui.Label(new Rect(x, y + panelH - 70f, panelW, 24f), hint, Ui.Font(16, false, TextAnchor.MiddleCenter), Ui.TextDim);
                Glyphs.Hint(new Rect(x, y + panelH - 40f, panelW, 24f),
                    _typing ? (InputMode.Pad ? "Type on the keyboard    {confirm} Done    {back} Cancel" : "Type your name    {confirm} Done    {back} Cancel")
                            : cardsPage ? "{nav} Scroll      {confirm} On / off      {back} Back" : matchPage && ReadOnly && !teamPage ? "{nav} Scroll      {back} Back" : matchPage ? "{nav} Choose      {side} Change      {back} Back" : _tab == 4 && InputMode.Pad && _focus < _rows.Count && _rows[_focus].Clear != null ? "{tabs} Tabs      {freeplay} Clear      {back} Back" : "{tabs} Tabs      {back} Back",
                    14, new Color(1f, 1f, 1f, 0.45f));
            }
            GUI.matrix = oldMatrix;
        }

        // ------------------------------------------------------------------ crosshair picker

        // the picker: a grid of crosshairs, then Apply and Cancel. _xhFocus: a crosshair (0..), Apply, or Cancel.
        private const int XhPerRow = 7;
        private int _xhPick, _xhFocus;
        private int XhApply { get { return Crosshairs.Ids.Length; } }
        private int XhCancel { get { return Crosshairs.Ids.Length + 1; } }

        /// <summary>Screenshots: the crosshair picker, the third one under the focus (not picked).</summary>
        public void ShowCrosshairs() { OpenCrosshair(); _xhFocus = 2; }

        private void OpenCrosshair()
        {
            Current = Page.Crosshair;
            _xhPick = _xhFocus = Settings.Crosshair;
            _openedAt = Time.unscaledTime;
        }

        /// <summary>Back to the Controls tab, on the Change crosshair row (and keep the pick, or not).</summary>
        private void CloseCrosshair(bool apply)
        {
            if (apply) { Settings.Crosshair = _xhPick; PlayerPrefs.Save(); Sound.Select(); }
            else Sound.Back();
            Current = Page.Settings;
            _tab = 4;
            _openedAt = Time.unscaledTime;
            BuildRows();
            _focus = Mathf.Max(0, _rows.FindIndex(r => r.Label == "Change crosshair"));
            _first = 0; _focusY = -1f;
        }

        private void CrosshairTick(LocalPlayers.MenuInput m)
        {
            if (m.Back) { CloseCrosshair(false); return; }
            var n = Crosshairs.Ids.Length;
            var was = _xhFocus;
            if (_xhFocus < n)
            {
                if (m.X != 0) _xhFocus = Mathf.Clamp(_xhFocus + m.X, 0, n - 1);
                if (m.Y < 0) _xhFocus = _xhFocus + XhPerRow < n ? _xhFocus + XhPerRow : XhApply;
                if (m.Y > 0 && _xhFocus >= XhPerRow) _xhFocus -= XhPerRow;
            }
            else
            {
                if (m.X != 0) _xhFocus = m.X > 0 ? XhCancel : XhApply;
                if (m.Y > 0) _xhFocus = Mathf.Min(n - 1, (n - 1) / XhPerRow * XhPerRow + (_xhFocus == XhCancel ? XhPerRow - 1 : 0));
            }
            if (_xhFocus != was) Sound.Move();
            if (!m.Confirm) return;
            if (_xhFocus == XhApply) CloseCrosshair(true);
            else if (_xhFocus == XhCancel) CloseCrosshair(false);
            else { _xhPick = _xhFocus; Sound.Select(); }
        }

        private void CrosshairPage(float t)
        {
            var appear = Ui.EaseOutBack(t / 0.28f);
            if (_settingsFromLobby) Ui.Fill(Palette.Background);
            else Ui.Fill(new Color(0f, 0f, 0f, 0.55f));
            const float cell = 86f, gap = 12f;
            var rows = (Crosshairs.Ids.Length + XhPerRow - 1) / XhPerRow;
            var gridW = XhPerRow * cell + (XhPerRow - 1) * gap;
            var panelW = gridW + 80f;
            var panelH = 130f + rows * (cell + gap) + 130f;
            var x = Ui.W / 2f - panelW / 2f;
            var y = Ui.H / 2f - panelH / 2f + (1f - appear) * 40f;
            Ui.Box(new Rect(x - 1f, y - 1f, panelW + 2f, panelH + 2f), new Color(1f, 1f, 1f, 0.06f), 22f);
            Ui.Box(new Rect(x, y, panelW, panelH), Ui.Panel, 21f);
            Ui.Label(new Rect(x, y + 22f, panelW, 50f), "Crosshair", Ui.Font(38, true, TextAnchor.MiddleCenter), Ui.Text);
            Ui.Label(new Rect(x, y + 74f, panelW, 30f), "Pick the aim marker for keyboard and mouse", Ui.Font(17, false, TextAnchor.MiddleCenter), Ui.TextDim);

            var mouse = Ui.Mouse;
            var tint = Settings.ReticleTint(0); tint.a = 1f;
            var gx = x + 40f;
            var gy = y + 120f;
            for (var i = 0; i < Crosshairs.Ids.Length; i++)
            {
                var r = new Rect(gx + (i % XhPerRow) * (cell + gap), gy + (i / XhPerRow) * (cell + gap), cell, cell);
                if (r.Contains(mouse) && Ui.MouseMoved && _xhFocus != i) { _xhFocus = i; Sound.Move(); }
                if (r.Contains(mouse) && Ui.TakeClick()) { _xhFocus = i; _xhPick = i; Sound.Select(); }
                var picked = i == _xhPick;
                Ui.Box(r, picked ? new Color(1f, 1f, 1f, 0.2f) : Ui.Row, 12f);
                if (i == _xhFocus) Ui.Box(r, Ui.RowFocus, 12f);
                if (picked)
                {
                    // a white ring round the one picked
                    Ui.Box(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, 3f), Color.white, 1.5f);
                    Ui.Box(new Rect(r.x - 3f, r.yMax, r.width + 6f, 3f), Color.white, 1.5f);
                    Ui.Box(new Rect(r.x - 3f, r.y, 3f, r.height), Color.white, 1.5f);
                    Ui.Box(new Rect(r.xMax, r.y, 3f, r.height), Color.white, 1.5f);
                }
                if (Event.current.type == EventType.Repaint) Hud.DrawCrosshair(i, r.center, cell * 0.62f, tint);
            }

            // Apply and Cancel
            const float bw = 200f, bh = 52f;
            var by = gy + rows * (cell + gap) + 18f;
            for (var k = 0; k < 2; k++)
            {
                var idx = k == 0 ? XhApply : XhCancel;
                var r = new Rect(x + panelW / 2f + (k == 0 ? -bw - 10f : 10f), by, bw, bh);
                if (r.Contains(mouse) && Ui.MouseMoved && _xhFocus != idx) { _xhFocus = idx; Sound.Move(); }
                Ui.Box(r, _xhFocus == idx ? new Color(1f, 1f, 1f, 0.92f) : Ui.Row, 12f);
                Ui.Label(r, k == 0 ? "Apply" : "Cancel", Ui.Font(20, _xhFocus == idx, TextAnchor.MiddleCenter), _xhFocus == idx ? new Color(0.08f, 0.08f, 0.09f) : Ui.Text, _xhFocus != idx);
                if (r.Contains(mouse) && Ui.TakeClick()) { CloseCrosshair(k == 0); return; }
            }
            Glyphs.Hint(new Rect(x, y + panelH - 40f, panelW, 24f), "{nav} Choose      {confirm} Pick      {back} Cancel", 14, new Color(1f, 1f, 1f, 0.45f));
        }

        /// <summary>Y / Triangle pressed on any controller this frame.</summary>
        private static bool NorthPressed()
        {
            if (LocalPlayers.Blocked) return false;
            foreach (var g in Gamepad.all) if (g.buttonNorth.wasPressedThisFrame) return true;
            return false;
        }

        private void Tabs_(float x, float y, float panelW)
        {
            const float tabH = 40f, gap = 10f;
            var tabW = TabCount > 5 ? 112f : TabCount > 4 ? 132f : 150f; // six tabs (with Controls + Match) have to fit
            var total = TabCount * tabW + (TabCount - 1) * gap;
            var x0 = x + panelW / 2f - total / 2f;
            var target = x0 + _tab * (tabW + gap);
            _tabX = _tabX < 0f ? target : Mathf.Lerp(_tabX, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 20f));
            Ui.Box(new Rect(x0 - 6f, y - 6f, total + 12f, tabH + 12f), new Color(1f, 1f, 1f, 0.05f), 14f);
            Ui.Box(new Rect(_tabX, y, tabW, tabH), new Color(1f, 1f, 1f, 0.92f), 10f);
            for (var i = 0; i < TabCount; i++)
            {
                var r = new Rect(x0 + i * (tabW + gap), y, tabW, tabH);
                if (r.Contains(Ui.Mouse) && Ui.TakeClick()) { if (_tab != i) Sound.Move(); _tab = i; _focus = 0; _listen = null; }
                var on = i == _tab;
                Ui.Label(r, Tabs[i], Ui.Font(18, on, TextAnchor.MiddleCenter), on ? new Color(0.08f, 0.08f, 0.09f) : Ui.TextDim, !on);
            }
        }

        private void DrawRow(Row row, Rect r, bool focused, bool pause, Vector2 mouse)
        {
            var enabled = row.Enabled();
            var textCol = enabled ? (focused ? Ui.Text : new Color(1f, 1f, 1f, 0.8f)) : new Color(1f, 1f, 1f, 0.3f);
            // TakeClick: once per frame, so Layout + Repaint can't double-fire a button
            var clicked = enabled && r.Contains(mouse) && Ui.TakeClick();

            if (pause && row.Value != null && row.Step != null)
            {
                // (free play's bots and rink) "Bots:  ‹ 3 ›": small arrows either side of the value, so it's clear it changes
                var style = Ui.Font(21, focused, TextAnchor.MiddleLeft);
                var lab = row.Label + ":  ";
                var val = row.Value();
                var wl = style.CalcSize(new GUIContent(lab)).x;
                var wv = style.CalcSize(new GUIContent(val)).x;
                const float arrowW = 16f, gapA = 8f;
                var x0 = r.center.x - (wl + arrowW + gapA + wv + gapA + arrowW) / 2f;
                var arrowCol = new Color(textCol.r, textCol.g, textCol.b, textCol.a * 0.6f);
                Ui.Label(new Rect(x0, r.y, wl + 4f, r.height), lab, style, textCol);
                Ui.Label(new Rect(x0 + wl, r.y - 1f, arrowW, r.height), "‹", Ui.Font(24, false, TextAnchor.MiddleCenter), arrowCol);
                Ui.Label(new Rect(x0 + wl + arrowW + gapA, r.y, wv + 4f, r.height), val, style, textCol);
                Ui.Label(new Rect(x0 + wl + arrowW + gapA + wv + gapA, r.y - 1f, arrowW, r.height), "›", Ui.Font(24, false, TextAnchor.MiddleCenter), arrowCol);
                if (clicked) { var d = mouse.x < r.center.x ? -1 : 1; row.Step(d); Sound.Step(d); }
                return;
            }
            if (pause)
            {
                Ui.Label(r, row.Label + (row.Value != null ? ":  " + row.Value() : ""), Ui.Font(21, focused, TextAnchor.MiddleCenter), textCol);
                if (clicked)
                {
                    if (row.Step != null && row.Value != null) { var d = mouse.x < r.center.x ? -1 : 1; row.Step(d); Sound.Step(d); }
                    else if (row.Press != null) { PressSound(row); row.Press(); }
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
                if (clicked) { Sound.Select(); row.Press(); }
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
                {
                    var to = Mathf.Clamp01((mouse.x - track.x) / track.width);
                    row.Set01(to);
                    if (Mathf.Abs(to - v) > 0.001f) Sound.Drag(); // a tick now and then as it moves (so you hear a volume as you set it)
                }
            }
            else if (row.Text)
            {
                var box = new Rect(right.x, r.y + 8f, right.width, r.height - 16f);
                var editing = _typing && focused;
                Ui.Box(box, new Color(1f, 1f, 1f, editing ? 0.16f : 0.06f), 10f);
                var caret = editing && (Time.unscaledTime * 2f) % 2f < 1.2f ? "|" : "";
                Ui.Label(new Rect(box.x + 14f, box.y, box.width - 28f, box.height), row.Value() + caret, Ui.Font(18, focused), textCol);
                if (clicked) { Sound.Select(); row.Press(); }
            }
            else if (row.Value != null && row.Step != null)
            {
                var col = textCol;
                Ui.Label(new Rect(right.x, r.y, right.width, r.height), "‹", Ui.Font(26, false, TextAnchor.MiddleLeft), new Color(col.r, col.g, col.b, col.a * 0.6f));
                Ui.Label(new Rect(right.x, r.y, right.width, r.height), row.Value(), Ui.Font(18, focused, TextAnchor.MiddleCenter), col);
                Ui.Label(new Rect(right.x, r.y, right.width, r.height), "›", Ui.Font(26, false, TextAnchor.MiddleRight), new Color(col.r, col.g, col.b, col.a * 0.6f));
                if (clicked) { var d = mouse.x < right.center.x ? -1 : 1; row.Step(d); Sound.Step(d); }
            }
            else if (row.Value != null)
            {
                // value + press (Controls rebinds): show the binding (as button pictures for the device in use), click to rebind
                var glyph = row.GlyphText != null ? row.GlyphText() : null;
                if (row.Picture != null) row.Picture(right);
                else if (glyph != null) Glyphs.Hint(right, glyph, 22, textCol, TextAnchor.MiddleRight, null, focused);
                else Ui.Label(right, row.Value(), Ui.Font(17, focused, TextAnchor.MiddleRight), textCol);
                if (clicked && row.Press != null) { PressSound(row); row.Press(); }
            }
            else if (clicked && row.Press != null) { PressSound(row); row.Press(); }
        }
    }
}
