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

        // ------------------------------------------------------------------ camera

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
