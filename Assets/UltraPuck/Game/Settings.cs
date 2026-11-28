using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>Player settings, saved between sessions (PlayerPrefs). The settings screen edits these.</summary>
    internal static class Settings
    {
        // ------------------------------------------------------------------ graphics

        public enum Window { Windowed, Borderless, Fullscreen }
        public static readonly string[] WindowNames = { "Windowed", "Borderless", "Fullscreen" };

        public static Window WindowMode
        {
            get { return (Window)Mathf.Clamp(PlayerPrefs.GetInt("window_mode", (int)Window.Borderless), 0, 2); }
            set { PlayerPrefs.SetInt("window_mode", (int)value); ApplyGraphics(); }
        }

        /// <summary>The screen sizes this monitor offers (smallest first, each once: left in the menu is lower).</summary>
        public static Vector2Int[] Resolutions()
        {
            var list = new System.Collections.Generic.List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (v.x >= 640 && v.y >= 360 && !list.Contains(v)) list.Add(v);
            }
            if (list.Count == 0) list.Add(new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height));
            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return list.ToArray();
        }

        /// <summary>The chosen resolution, or (0, 0) for the monitor's own (the default).</summary>
        public static Vector2Int Resolution
        {
            get { return new Vector2Int(PlayerPrefs.GetInt("res_w", 0), PlayerPrefs.GetInt("res_h", 0)); }
            set { PlayerPrefs.SetInt("res_w", value.x); PlayerPrefs.SetInt("res_h", value.y); ApplyGraphics(true); }
        }

        public static bool VSync
        {
            get { return PlayerPrefs.GetInt("vsync", 1) != 0; }
            set { PlayerPrefs.SetInt("vsync", value ? 1 : 0); ApplyGraphics(); }
        }

        /// <summary>Frame rate cap when V-Sync is off (0 = none).</summary>
        public static readonly int[] FpsCaps = { 0, 60, 120, 144, 165, 240 };
        public static int FpsCap
        {
            get { return PlayerPrefs.GetInt("fps_cap", 0); }
            set { PlayerPrefs.SetInt("fps_cap", value); ApplyGraphics(); }
        }

        /// <summary>Size of on-screen text and menus (0.6..2).</summary>
        public static float UiScale
        {
            get { return Mathf.Clamp(PlayerPrefs.GetFloat("ui_scale", 1f), 0.6f, 2f); }
            set { PlayerPrefs.SetFloat("ui_scale", Mathf.Clamp(value, 0.6f, 2f)); }
        }

        public static bool PuckTrail
        {
            get { return PlayerPrefs.GetInt("puck_trail", 1) != 0; }
            set { PlayerPrefs.SetInt("puck_trail", value ? 1 : 0); }
        }

        /// <summary>Holo, foil and glare on cards (off: plain printed cards).</summary>
        /// <summary>Online: see (and download) the host's custom arena. On by default.</summary>
        public static bool OtherArenas
        {
            get { return PlayerPrefs.GetInt("other_arenas", 1) != 0; }
            set { PlayerPrefs.SetInt("other_arenas", value ? 1 : 0); }
        }

        public static bool CardEffects
        {
            get { return PlayerPrefs.GetInt("card_fx", 1) != 0; }
            set { PlayerPrefs.SetInt("card_fx", value ? 1 : 0); }
        }

        /// <summary>Small FPS number in the top-left corner (like Steam's overlay).</summary>
        public static bool ShowFps
        {
            get { return PlayerPrefs.GetInt("show_fps", 0) != 0; }
            set { PlayerPrefs.SetInt("show_fps", value ? 1 : 0); }
        }

        /// <summary>Put the window mode, V-Sync and frame cap into effect.</summary>
        public static void ApplyGraphics() { ApplyGraphics(false); }

        /// <param name="resize">The resolution was just changed: set it even if the window mode is the same.</param>
        public static void ApplyGraphics(bool resize)
        {
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = VSync || FpsCap <= 0 ? -1 : FpsCap;
            var mode = WindowMode == Window.Fullscreen ? FullScreenMode.ExclusiveFullScreen
                : WindowMode == Window.Borderless ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            var want = Resolution;
            if (want.x > 0 && want.y > 0)
            {
                if (resize || Screen.fullScreenMode != mode || Screen.width != want.x || Screen.height != want.y) Screen.SetResolution(want.x, want.y, mode);
                return;
            }
            if (Screen.fullScreenMode == mode) return;
            if (mode == FullScreenMode.Windowed) Screen.fullScreenMode = mode;
            else
            {
                var r = Screen.currentResolution;
                Screen.SetResolution(r.width, r.height, mode);
            }
        }

        // ------------------------------------------------------------------ sound

        /// <summary>How loud the whole game is, 0 (silent) .. 1 (full). Starts at 30%.</summary>
        public static float Volume
        {
            get { return Mathf.Clamp01(PlayerPrefs.GetFloat("volume", 0.3f)); }
            set { PlayerPrefs.SetFloat("volume", Mathf.Clamp01(value)); }
        }

        /// <summary>The music's share of <see cref="Volume"/>, 0 (off) .. 1.</summary>
        public static float MusicVolume
        {
            get { return Mathf.Clamp01(PlayerPrefs.GetFloat("music_volume", 1f)); }
            set { PlayerPrefs.SetFloat("music_volume", Mathf.Clamp01(value)); }
        }

        /// <summary>The sound effects' share of <see cref="Volume"/>, 0 (off) .. 1.</summary>
        /// <summary>
        /// The puck's wall-bounce sound: 0 = none, 1 = the dry snap (Sfx.BounceSnap, the usual one), 2..4 = the toks,
        /// 5 = the plink, 6..11 = the drum taps A..F.
        /// </summary>
        public static int PuckBounceSound
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt("puck_bounce_snap", 1), 0, PuckBounceNames.Length - 1); }
            set { PlayerPrefs.SetInt("puck_bounce_snap", Mathf.Clamp(value, 0, PuckBounceNames.Length - 1)); }
        }

        /// <summary>The sound a chaos card makes as it flies in: 0 = none, 1..8 = Sfx.ChaosA..H (default 8, H: the short stinger).</summary>
        public static int ChaosSound
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt("chaos_sound", 8), 0, ChaosSoundNames.Length - 1); }
            set { PlayerPrefs.SetInt("chaos_sound", Mathf.Clamp(value, 0, ChaosSoundNames.Length - 1)); }
        }

        public static readonly string[] ChaosSoundNames = { "Off", "A: glitch zap", "B: corrupted chime", "C: whoosh + stinger", "D: error bleeps",
            "E: deep stinger", "F: stuttering stinger", "G: sharp stinger", "H: short stinger" };

        public static readonly string[] PuckBounceNames = { "Off", "Snap", "Tok", "Tok: rounder", "Tok: brighter", "Plink",
            "Drum A: clean", "Drum B: tight", "Drum C: round", "Drum D: soft click", "Drum E: deep", "Drum F: snappy" };

        public static float SoundVolume
        {
            get { return Mathf.Clamp01(PlayerPrefs.GetFloat("sound_volume", 1f)); }
            set { PlayerPrefs.SetFloat("sound_volume", Mathf.Clamp01(value)); }
        }

        /// <summary>
        /// When the game window isn't focused: pause the music (and don't start the next song). Off by default, so a
        /// song that ends while you're looking at something else still queues the next one.
        /// </summary>
        /// <summary>Replay clips: the song that was playing goes in under the game's sounds.</summary>
        public static bool MusicInClips
        {
            get { return PlayerPrefs.GetInt("clip_music", 1) != 0; }
            set { PlayerPrefs.SetInt("clip_music", value ? 1 : 0); }
        }

        public static bool PauseMusicWhenUnfocused
        {
            get { return PlayerPrefs.GetInt("pause_music_unfocused", 0) != 0; }
            set { PlayerPrefs.SetInt("pause_music_unfocused", value ? 1 : 0); }
        }

        // ------------------------------------------------------------------ gameplay

        /// <summary>Screen shake on hits, 0 (off) .. 1 (full).</summary>
        public static float ShakeAmount
        {
            get { return Mathf.Clamp01(PlayerPrefs.GetFloat("shake_amount", 1f)); }
            set { PlayerPrefs.SetFloat("shake_amount", Mathf.Clamp01(value)); }
        }

        /// <summary>
        /// How long the shot aim line is, 0 (off) .. 1 (full). Full is the original length; default is 60% (40% shorter).
        /// </summary>
        public static float AimLine
        {
            get { return Mathf.Clamp01(PlayerPrefs.GetFloat("aim_line", 0.6f)); }
            set { PlayerPrefs.SetFloat("aim_line", Mathf.Clamp01(value)); }
        }

        /// <summary>
        /// A faint dotted line from you to your Swap rock while it's on the ice (Settings &gt; Camera).
        /// </summary>
        public static bool RockLine
        {
            get { return PlayerPrefs.GetInt("rock_line", 1) != 0; }
            set { PlayerPrefs.SetInt("rock_line", value ? 1 : 0); }
        }

        /// <summary>Which crosshair (of <see cref="Crosshairs.Ids"/>) to draw while aiming with the mouse (Settings &gt; Controls).</summary>
        public static int Crosshair
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt("crosshair", 6), 0, Crosshairs.Ids.Length - 1); } // 6: the ring with a cross, like the old one
            set { PlayerPrefs.SetInt("crosshair", Mathf.Clamp(value, 0, Crosshairs.Ids.Length - 1)); }
        }

        /// <summary>The mouse pointer's size in percent: 50 .. 400 (100 is the normal size).</summary>
        public const int CursorMin = 50, CursorMax = 400;
        public static int CursorSize
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt("cursor_size", 100), CursorMin, CursorMax); }
            set { PlayerPrefs.SetInt("cursor_size", Mathf.Clamp(value, CursorMin, CursorMax)); Crosshairs.UseGameCursor(); }
        }

        /// <summary>Crosshair size in percent: 50 .. 1000 (100 is the normal size).</summary>
        public const int CrosshairMin = 50, CrosshairMax = 1000;
        public static int CrosshairSize
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt("crosshair_size", 100), CrosshairMin, CrosshairMax); }
            set { PlayerPrefs.SetInt("crosshair_size", Mathf.Clamp(value, CrosshairMin, CrosshairMax)); }
        }

        /// <summary>Reticle colours.</summary>
        public static readonly string[] ReticleColourNames = { "White", "Team", "Green", "Cyan", "Yellow" };

        /// <summary>Reticle colour index into <see cref="ReticleColourNames"/>.</summary>
        public static int ReticleColour
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt("reticle_colour", 0), 0, ReticleColourNames.Length - 1); }
            set { PlayerPrefs.SetInt("reticle_colour", Mathf.Clamp(value, 0, ReticleColourNames.Length - 1)); }
        }

        /// <summary>Colour for the reticle; <paramref name="team"/> is used when set to Team.</summary>
        public static Color ReticleTint(int team)
        {
            switch (ReticleColour)
            {
                case 1: return team == 0 || team == 1 ? Palette.TeamTint(team) : Color.white;
                case 2: return new Color(0.45f, 1f, 0.55f);
                case 3: return new Color(0.4f, 0.95f, 1f);
                case 4: return new Color(1f, 0.92f, 0.4f);
                default: return Color.white;
            }
        }

        /// <summary>Squash, flash and impact rings on hits (off: only the bump itself).</summary>
        public static bool HitEffects
        {
            get { return PlayerPrefs.GetInt("hit_fx", 1) != 0; }
            set { PlayerPrefs.SetInt("hit_fx", value ? 1 : 0); }
        }


        /// <summary>Swapping to a bot teammate: a trail from where you were to where you are now (off by default).</summary>
        public static bool SwapTrail
        {
            get { return PlayerPrefs.GetInt("swap_trail", 0) != 0; }
            set { PlayerPrefs.SetInt("swap_trail", value ? 1 : 0); }
        }

        /// <summary>
        /// The keyboard and the first controller both play player 1 (true, the default), or are separate players.
        /// </summary>
        public static bool SharedControls
        {
            get { return PlayerPrefs.GetInt("shared_controls", 1) != 0; }
            set { PlayerPrefs.SetInt("shared_controls", value ? 1 : 0); }
        }

        /// <summary>A bot teammate that picks up the puck hands you over to them (true, the default).</summary>
        public static bool AutoSwapBots
        {
            get { return PlayerPrefs.GetInt("auto_swap_bots", 1) != 0; }
            set { PlayerPrefs.SetInt("auto_swap_bots", value ? 1 : 0); }
        }

        /// <summary>A check dashes the way you're moving when the move stick is pushed (true, the default), or always the way you face.</summary>
        public static bool DashWhereMoving
        {
            get { return PlayerPrefs.GetInt("dash_where_moving", 1) != 0; }
            set { PlayerPrefs.SetInt("dash_where_moving", value ? 1 : 0); }
        }

        /// <summary>With separate keyboard and controller players: player 1 is the controller (true) or the keyboard.</summary>
        public static bool ControllerIsPlayer1
        {
            get { return PlayerPrefs.GetInt("controller_p1", 1) != 0; }
            set { PlayerPrefs.SetInt("controller_p1", value ? 1 : 0); }
        }

        /// <summary>Check bars: shown only while recharging (false) or always (true).</summary>
        public static bool DashBarAlways
        {
            get { return PlayerPrefs.GetInt("dash_bar_always", 0) != 0; }
            set { PlayerPrefs.SetInt("dash_bar_always", value ? 1 : 0); }
        }

        /// <summary>Your name, shown over your player and in online lobbies. Empty: your Steam name.</summary>
        public static string PlayerName
        {
            get
            {
                var n = Clean(PlayerPrefs.GetString("player_name", ""));
                if (n.Length > 0) return n;
                n = Clean(SteamNet.Name);
                return n.Length > 0 ? n : "Player";
            }
            set { PlayerPrefs.SetString("player_name", Clean(value)); }
        }

        public const int MaxNameLength = 16;

        /// <summary>A name with no line breaks, rich-text tags or spaces at the ends, at most <see cref="MaxNameLength"/> long.</summary>
        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (var c in s) if (!char.IsControl(c) && c != '<' && c != '>') sb.Append(c);
            var t = sb.ToString().Trim();
            return t.Length > MaxNameLength ? t.Substring(0, MaxNameLength).Trim() : t;
        }

        /// <summary>Names floating over the players.</summary>
        public static bool NameTags
        {
            get { return PlayerPrefs.GetInt("name_tags", 1) != 0; }
            set { PlayerPrefs.SetInt("name_tags", value ? 1 : 0); }
        }

        /// <summary>The list of everyone's cards in the lower-left corner during a match.</summary>
        public static bool CornerCards
        {
            get { return PlayerPrefs.GetInt("corner_cards", 0) != 0; }
            set { PlayerPrefs.SetInt("corner_cards", value ? 1 : 0); }
        }

        /// <summary>Check for a new version when the game starts, and install it.</summary>
        public static bool AutoUpdate
        {
            get { return PlayerPrefs.GetInt("auto_update", 1) != 0; }
            set { PlayerPrefs.SetInt("auto_update", value ? 1 : 0); }
        }

        // ------------------------------------------------------------------ match rules (online, the host's apply)

        /// <summary>Rounds: after every goal the team that conceded picks a card.</summary>
        public static bool Rounds
        {
            get { return PlayerPrefs.GetInt("rounds", 1) != 0; }
            set { PlayerPrefs.SetInt("rounds", value ? 1 : 0); }
        }

        /// <summary>Every goal is shown again, with slow motion on the shot.</summary>
        public static bool Replays
        {
            get { return PlayerPrefs.GetInt("replays", 1) != 0; }
            set { PlayerPrefs.SetInt("replays", value ? 1 : 0); }
        }

        /// <summary>
        /// Picking: 0 each player draws their own cards and takes one; 1 Same Draw: everyone on a team is offered the
        /// same cards but each takes their own; 2 Team Vote: each team votes on one card for all of them.
        /// </summary>
        public static int PickMode
        {
            get
            {
                var v = PlayerPrefs.GetInt("pick_mode", -1);
                if (v < 0) v = PlayerPrefs.GetInt("team_vote", 0) != 0 ? 2 : 0; // from before Same Draw
                return Mathf.Clamp(v, 0, 2);
            }
            set { PlayerPrefs.SetInt("pick_mode", Mathf.Clamp(value, 0, 2)); }
        }
        public static readonly string[] PickModes = { "Each player", "Same draw", "Team vote" };
        public static bool TeamVote { get { return PickMode == 2; } }

        /// <summary>Allow more than one utility card per player. Off (default): taking a new utility replaces the one you have.</summary>
        public static bool MultiUtility
        {
            get { return PlayerPrefs.GetInt("multi_utility", 0) != 0; }
            set { PlayerPrefs.SetInt("multi_utility", value ? 1 : 0); }
        }

        /// <summary>
        /// Carry-through: a pass or shot that reaches another player takes them along with some of the puck's speed
        /// (like Cannon Ball, for every shot). <see cref="CarryPercent"/> is how much (5..100%, a temporary tuning slider).
        /// On at 40% unless changed (the "_v2" keys start everyone, older saves too, on those defaults).
        /// </summary>
        /// <summary>Chaos cards stack: one somebody has can still be offered (and its effect adds up). Off by default.</summary>
        public static bool ChaosStack
        {
            get { return PlayerPrefs.GetInt("chaos_stack", 0) != 0; }
            set { PlayerPrefs.SetInt("chaos_stack", value ? 1 : 0); }
        }

        /// <summary>Wavedash for everyone: every player has the Wavedash card's effect all match, and the card is never offered. Off by default.</summary>
        public static bool WavedashForAll
        {
            get { return PlayerPrefs.GetInt("wavedash_all", 0) != 0; }
            set { PlayerPrefs.SetInt("wavedash_all", value ? 1 : 0); }
        }

        public static bool CarryThrough
        {
            get { return PlayerPrefs.GetInt("carry_through_v2", 1) != 0; }
            set { PlayerPrefs.SetInt("carry_through_v2", value ? 1 : 0); }
        }

        public static int CarryPercent
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt("carry_percent_v2", 40) / 5 * 5, 5, 100); }
            set { PlayerPrefs.SetInt("carry_percent_v2", Mathf.Clamp(value / 5 * 5, 5, 100)); }
        }

        /// <summary>
        /// Knock-away: how hard a check knocks the puck away from its carrier, in % of the usual (0..500% in steps of 25,
        /// default 350; 100 is the plain check: a tuning slider).
        /// </summary>
        public static int KnockAwayPercent
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt("knock_away_v2", 350) / 25 * 25, 0, 500); }
            set { PlayerPrefs.SetInt("knock_away_v2", Mathf.Clamp(value / 25 * 25, 0, 500)); }
        }

        /// <summary>How a match is won: 0 = first to <see cref="GoalsToWin"/> goals, 1 = most goals when <see cref="MatchMinutes"/> run out.</summary>
        public static int MatchMode
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt("match_mode", 0), 0, 1); }
            set { PlayerPrefs.SetInt("match_mode", Mathf.Clamp(value, 0, 1)); }
        }

        public static int GoalsToWin
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt("goals_to_win", 5), 1, 20); }
            set { PlayerPrefs.SetInt("goals_to_win", Mathf.Clamp(value, 1, 20)); }
        }

        public static int MatchMinutes
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt("match_minutes", 3), 1, 15); }
            set { PlayerPrefs.SetInt("match_minutes", Mathf.Clamp(value, 1, 15)); }
        }

        private static readonly int[][] MatchPresets = { new[] { 0, 3 }, new[] { 0, 5 }, new[] { 0, 7 }, new[] { 0, 10 }, new[] { 1, 2 }, new[] { 1, 3 }, new[] { 1, 5 }, new[] { 1, 8 } };

        /// <summary>The online lobby's Match row: steps through a few common choices.</summary>
        public static void StepMatch(int d)
        {
            var at = 0;
            for (var k = 0; k < MatchPresets.Length; k++)
                if (MatchPresets[k][0] == MatchMode && MatchPresets[k][1] == (MatchMode == 0 ? GoalsToWin : MatchMinutes)) at = k;
            at = (at + d + MatchPresets.Length) % MatchPresets.Length;
            MatchMode = MatchPresets[at][0];
            if (MatchMode == 0) GoalsToWin = MatchPresets[at][1]; else MatchMinutes = MatchPresets[at][1];
        }

        /// <summary>The match rule as a few words: "First to 5" or "3 minutes".</summary>
        public static string MatchText { get { return MatchMode == 0 ? "First to " + GoalsToWin : MatchMinutes + (MatchMinutes == 1 ? " minute" : " minutes"); } }

        public static readonly int[] OfferCounts = { 3, 4, 5 };
        /// <summary>Cards in each offer: 5 unless changed (the "_v2" key starts everyone, older saves too, on that default).</summary>
        public static int CardsOffered
        {
            get { var v = PlayerPrefs.GetInt("cards_offered_v2", 5); return System.Array.IndexOf(OfferCounts, v) >= 0 ? v : 5; }
            set { PlayerPrefs.SetInt("cards_offered_v2", value); }
        }

        /// <summary>Seconds to pick a card (0 = no limit). Anyone who hasn't picked in time gets a random one.</summary>
        public static readonly int[] PickTimes = { 15, 30, 60, 0 };
        public static int PickTime
        {
            get { var v = PlayerPrefs.GetInt("pick_time_v2", 0); return System.Array.IndexOf(PickTimes, v) >= 0 ? v : 0; } // (no limit unless changed)
            set { PlayerPrefs.SetInt("pick_time_v2", value); }
        }

        /// <summary>Cards that are switched off (never offered), by id.</summary>
        public static bool CardOn(string id)
        {
            var off = "," + PlayerPrefs.GetString("cards_off", "") + ",";
            return off.IndexOf("," + id + ",", System.StringComparison.Ordinal) < 0;
        }

        public static void SetCardOn(string id, bool on)
        {
            var list = new System.Collections.Generic.List<string>(PlayerPrefs.GetString("cards_off", "").Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries));
            list.Remove(id);
            if (!on) list.Add(id);
            PlayerPrefs.SetString("cards_off", string.Join(",", list.ToArray()));
        }

        // ------------------------------------------------------------------ camera

        /// <summary>While you charge a shot, the camera pulls back until the goal you're attacking is in view.</summary>
        public static bool ChargeZoom
        {
            get { return PlayerPrefs.GetInt("charge_zoom", 1) != 0; }
            set { PlayerPrefs.SetInt("charge_zoom", value ? 1 : 0); }
        }

        /// <summary>How far Charge Zoom may pull back, in % of the normal view (0 = no limit).</summary>
        public static readonly int[] ChargeZoomCaps = { 150, 200, 250, 300, 0 };
        public static int ChargeZoomMax
        {
            get { var v = PlayerPrefs.GetInt("charge_zoom_max", 250); return System.Array.IndexOf(ChargeZoomCaps, v) >= 0 ? v : 250; }
            set { PlayerPrefs.SetInt("charge_zoom_max", value); }
        }

        /// <summary>The camera follows the action and zooms (on), or always shows the whole rink (off, the default).</summary>
        public static bool FollowCamera
        {
            get { return PlayerPrefs.GetInt("follow_camera", 0) != 0; }
            set { PlayerPrefs.SetInt("follow_camera", value ? 1 : 0); }
        }

        /// <summary>How far the follow camera pulls back: 0 = close to the action, 1 = wide.</summary>
        public static float CameraZoom
        {
            get { return Mathf.Clamp01(PlayerPrefs.GetFloat("camera_zoom", 0.55f)); }
            set { PlayerPrefs.SetFloat("camera_zoom", Mathf.Clamp01(value)); }
        }

        /// <summary>How smoothly the camera moves: 0 = snappy, 1 = floaty.</summary>
        public static float CameraSmoothing
        {
            get { return Mathf.Clamp01(PlayerPrefs.GetFloat("camera_smooth", 0.4f)); }
            set { PlayerPrefs.SetFloat("camera_smooth", Mathf.Clamp01(value)); }
        }
    }

    /// <summary>
    /// The host's match rules as they're sent online (GameRunner.PackRules), read back so everyone else can see them.
    /// </summary>
    internal sealed class RulesView
    {
        public bool Rounds, Replays, MultiUtility, Carry, ChaosStack, WavedashForAll;
        public int PickMode, CardsOffered, PickTime, MatchMode, GoalsToWin, MatchMinutes, CardsOff, CarryPercent, KnockPercent = 100;

        /// <summary>Null when there's nothing (yet) to read.</summary>
        public static RulesView Read(byte[] r)
        {
            if (r == null || r.Length < 7) return null;
            var v = new RulesView
            {
                Rounds = (r[0] & 1) != 0, Replays = (r[0] & 4) != 0, MultiUtility = (r[0] & 8) != 0,
                PickMode = (r[0] & 2) != 0 ? 2 : (r[0] & 16) != 0 ? 1 : 0,
                CardsOffered = r[1], PickTime = r[2], MatchMode = r[3], GoalsToWin = r[4], MatchMinutes = r[5],
            };
            var n = Mathf.Min(r[6], r.Length - 7);
            v.CardsOff = n;
            if (r.Length > 7 + n && r[7 + n] > 0) { v.Carry = true; v.CarryPercent = r[7 + n]; }
            if (r.Length > 8 + n) v.KnockPercent = r[8 + n] * 5;
            if (r.Length > 9 + n) { v.ChaosStack = (r[9 + n] & 1) != 0; v.WavedashForAll = (r[9 + n] & 2) != 0; }
            return v;
        }

        public string MatchText { get { return MatchMode == 0 ? "First to " + GoalsToWin : MatchMinutes + (MatchMinutes == 1 ? " minute" : " minutes"); } }
    }
}
