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
            get { return (Window)Mathf.Clamp(PlayerPrefs.GetInt("window_mode", (int)Window.Windowed), 0, 2); }
            set { PlayerPrefs.SetInt("window_mode", (int)value); ApplyGraphics(); }
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
        public static bool CardEffects
        {
            get { return PlayerPrefs.GetInt("card_fx", 1) != 0; }
            set { PlayerPrefs.SetInt("card_fx", value ? 1 : 0); }
        }

        /// <summary>Put the window mode, V-Sync and frame cap into effect.</summary>
        public static void ApplyGraphics()
        {
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = VSync || FpsCap <= 0 ? -1 : FpsCap;
            var mode = WindowMode == Window.Fullscreen ? FullScreenMode.ExclusiveFullScreen
                : WindowMode == Window.Borderless ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (Screen.fullScreenMode == mode) return;
            if (mode == FullScreenMode.Windowed) Screen.fullScreenMode = mode;
            else
            {
                var r = Screen.currentResolution;
                Screen.SetResolution(r.width, r.height, mode);
            }
        }

        // ------------------------------------------------------------------ gameplay

        /// <summary>Screen shake on hits, 0 (off) .. 1 (full).</summary>
        public static float ShakeAmount
        {
            get { return Mathf.Clamp01(PlayerPrefs.GetFloat("shake_amount", 1f)); }
            set { PlayerPrefs.SetFloat("shake_amount", Mathf.Clamp01(value)); }
        }

        /// <summary>Squash, flash and impact rings on hits (off: only the bump itself).</summary>
        public static bool HitEffects
        {
            get { return PlayerPrefs.GetInt("hit_fx", 1) != 0; }
            set { PlayerPrefs.SetInt("hit_fx", value ? 1 : 0); }
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
            get { return PlayerPrefs.GetInt("corner_cards", 1) != 0; }
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

        /// <summary>Picking: each player gets their own card (false), or each team votes on one card for all of them (true).</summary>
        public static bool TeamVote
        {
            get { return PlayerPrefs.GetInt("team_vote", 0) != 0; }
            set { PlayerPrefs.SetInt("team_vote", value ? 1 : 0); }
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
        public static int CardsOffered
        {
            get { var v = PlayerPrefs.GetInt("cards_offered", 3); return System.Array.IndexOf(OfferCounts, v) >= 0 ? v : 3; }
            set { PlayerPrefs.SetInt("cards_offered", value); }
        }

        /// <summary>Seconds to pick a card (0 = no limit). Anyone who hasn't picked in time gets a random one.</summary>
        public static readonly int[] PickTimes = { 15, 30, 60, 0 };
        public static int PickTime
        {
            get { var v = PlayerPrefs.GetInt("pick_time", 30); return System.Array.IndexOf(PickTimes, v) >= 0 ? v : 30; }
            set { PlayerPrefs.SetInt("pick_time", value); }
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

        /// <summary>The camera follows the action and zooms (on), or always shows the whole rink (off).</summary>
        public static bool FollowCamera
        {
            get { return PlayerPrefs.GetInt("follow_camera", 1) != 0; }
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
}
