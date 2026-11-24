using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UltraPuck.Game
{
    /// <summary>
    /// Kenney Input Prompts (CC0) under StreamingAssets/InputPrompts. Loads PNGs for the device the person is using
    /// so hints and card text can show the real button pictures.
    /// </summary>
    internal static class InputIcons
    {
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        private static bool _tried;
        private static string _root;

        public static string Folder
        {
            get
            {
                if (_root == null) _root = Path.Combine(Application.streamingAssetsPath, "InputPrompts");
                return _root;
            }
        }

        /// <summary>Call once at start so the first hint doesn't hitch.</summary>
        public static void Warm()
        {
            if (_tried) return;
            _tried = true;
            if (!Directory.Exists(Folder))
            {
                Debug.LogWarning("Ultra Puck: no InputPrompts folder at " + Folder + " (Kenney button pictures missing)");
                return;
            }
            // touch a few common ones so they're ready for the menus
            Get(InputMode.Family.Xbox, "xbox_button_color_a.png");
            Get(InputMode.Family.PlayStation, "playstation_button_color_triangle.png");
            Get(InputMode.Family.Keyboard, "keyboard_e.png");
            Debug.Log("Ultra Puck: Kenney input prompts from " + Folder);
        }

        /// <summary>Picture for a hint action ({utility}, {confirm}, …) for the given device family.</summary>
        public static Texture2D ForAction(string action, InputMode.Family family)
        {
            Warm();
            if (family == InputMode.Family.Keyboard)
            {
                var keys = Glyphs.KeyLabels(action);
                if (keys.Length == 1) return ForKeyLabel(keys[0]);
                return null; // multi-key (WASD / arrows): drawn as text caps
            }
            var pads = Glyphs.PadButtons(action);
            if (pads.Length == 0) return null;
            return ForPad(pads[0], family);
        }

        /// <summary>Picture for one controller button.</summary>
        public static Texture2D ForPad(Glyphs.PadButton b, InputMode.Family family)
        {
            Warm();
            var file = FileForPad(b, family);
            if (file == null) return null;
            return Get(family, file);
        }

        /// <summary>Picture for a keyboard key label ("E", "Space", "Esc", …).</summary>
        public static Texture2D ForKeyLabel(string label)
        {
            Warm();
            if (string.IsNullOrEmpty(label)) return null;
            var file = FileForKey(label);
            if (file == null) return null;
            return Get(InputMode.Family.Keyboard, file);
        }

        private static readonly Dictionary<Texture2D, Rect> Bounds = new Dictionary<Texture2D, Rect>();

        /// <summary>
        /// The part of a picture that's drawn (its non-transparent box), as 0..1 texture coordinates. Kenney's wide
        /// keys (Space, Shift) sit small in a square picture; drawing just this part keeps them readable.
        /// </summary>
        public static Rect Used(Texture2D tex)
        {
            if (tex == null) return new Rect(0f, 0f, 1f, 1f);
            Rect r;
            if (Bounds.TryGetValue(tex, out r)) return r;
            r = new Rect(0f, 0f, 1f, 1f);
            try
            {
                var px = tex.GetPixels32();
                int w = tex.width, h = tex.height, x0 = w, y0 = h, x1 = -1, y1 = -1;
                for (var y = 0; y < h; y++)
                    for (var x = 0; x < w; x++)
                    {
                        if (px[y * w + x].a < 16) continue;
                        if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
                    }
                if (x1 >= x0 && y1 >= y0) r = new Rect((float)x0 / w, (float)y0 / h, (float)(x1 - x0 + 1) / w, (float)(y1 - y0 + 1) / h);
            }
            catch { }
            Bounds[tex] = r;
            return r;
        }

        private static Texture2D Get(InputMode.Family family, string file)
        {
            var sub = Subfolder(family);
            var key = sub + "/" + file;
            Texture2D tex;
            if (Cache.TryGetValue(key, out tex)) return tex;
            var path = Path.Combine(Folder, Path.Combine(sub, file));
            if (!File.Exists(path)) { Cache[key] = null; return null; }
            try
            {
                var bytes = File.ReadAllBytes(path);
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = file };
                if (!tex.LoadImage(bytes)) { Object.Destroy(tex); Cache[key] = null; return null; }
                Cache[key] = tex;
                return tex;
            }
            catch
            {
                Cache[key] = null;
                return null;
            }
        }

        private static string Subfolder(InputMode.Family f)
        {
            switch (f)
            {
                case InputMode.Family.PlayStation: return "PlayStation";
                case InputMode.Family.Switch: return "Switch";
                case InputMode.Family.Keyboard: return "Keyboard";
                case InputMode.Family.Generic: return "Xbox"; // (Steam Deck, unknown pads) A/B/X/Y like an Xbox pad; the Generic folder has only plain shapes
                default: return "Xbox";
            }
        }

        private static string FileForPad(Glyphs.PadButton b, InputMode.Family f)
        {
            if (f == InputMode.Family.PlayStation)
            {
                switch (b)
                {
                    case Glyphs.PadButton.South: return "playstation_button_color_cross.png";
                    case Glyphs.PadButton.East: return "playstation_button_color_circle.png";
                    case Glyphs.PadButton.West: return "playstation_button_color_square.png";
                    case Glyphs.PadButton.North: return "playstation_button_color_triangle.png";
                    case Glyphs.PadButton.LB: return "playstation_trigger_l1.png";
                    case Glyphs.PadButton.RB: return "playstation_trigger_r1.png";
                    case Glyphs.PadButton.LT: return "playstation_trigger_l2.png";
                    case Glyphs.PadButton.RT: return "playstation_trigger_r2.png";
                    case Glyphs.PadButton.Start: return InputMode.DualSense ? "playstation5_button_options.png" : "playstation4_button_options.png";
                    case Glyphs.PadButton.Select: return InputMode.DualSense ? "playstation5_button_create.png" : "playstation4_button_share.png";
                    case Glyphs.PadButton.LS: return "playstation_stick_l.png";
                    case Glyphs.PadButton.RS: return "playstation_stick_r.png";
                    case Glyphs.PadButton.DPad: return "playstation_dpad.png";
                    case Glyphs.PadButton.DPadLR: return "playstation_dpad_horizontal.png";
                }
            }
            if (f == InputMode.Family.Switch)
            {
                // Nintendo face layout: bottom = B, right = A
                switch (b)
                {
                    case Glyphs.PadButton.South: return "switch_button_b.png";
                    case Glyphs.PadButton.East: return "switch_button_a.png";
                    case Glyphs.PadButton.West: return "switch_button_y.png";
                    case Glyphs.PadButton.North: return "switch_button_x.png";
                    case Glyphs.PadButton.LB: return "switch_button_l.png";
                    case Glyphs.PadButton.RB: return "switch_button_r.png";
                    case Glyphs.PadButton.LT: return "switch_button_zl.png";
                    case Glyphs.PadButton.RT: return "switch_button_zr.png";
                    case Glyphs.PadButton.Start: return "switch_button_plus.png";
                    case Glyphs.PadButton.Select: return "switch_button_minus.png";
                    case Glyphs.PadButton.LS: return "switch_stick_l.png";
                    case Glyphs.PadButton.RS: return "switch_stick_r.png";
                    case Glyphs.PadButton.DPad: return "switch_dpad.png";
                    case Glyphs.PadButton.DPadLR: return "switch_dpad_horizontal.png";
                }
            }
            // Xbox + Generic
            switch (b)
            {
                case Glyphs.PadButton.South: return "xbox_button_color_a.png";
                case Glyphs.PadButton.East: return "xbox_button_color_b.png";
                case Glyphs.PadButton.West: return "xbox_button_color_x.png";
                case Glyphs.PadButton.North: return "xbox_button_color_y.png";
                case Glyphs.PadButton.LB: return "xbox_lb.png";
                case Glyphs.PadButton.RB: return "xbox_rb.png";
                case Glyphs.PadButton.LT: return "xbox_lt.png";
                case Glyphs.PadButton.RT: return "xbox_rt.png";
                case Glyphs.PadButton.Start: return "xbox_button_menu.png";
                case Glyphs.PadButton.Select: return "xbox_button_view.png";
                case Glyphs.PadButton.LS: return "xbox_stick_l.png";
                case Glyphs.PadButton.RS: return "xbox_stick_r.png";
                case Glyphs.PadButton.DPad: return "xbox_dpad.png";
                case Glyphs.PadButton.DPadLR: return "xbox_dpad_horizontal.png";
                default: return null;
            }
        }

        private static string FileForKey(string label)
        {
            var s = label.Trim().ToLowerInvariant();
            switch (s)
            {
                case "enter": case "return": return "keyboard_enter.png";
                case "esc": case "escape": return "keyboard_escape.png";
                case "space": return "keyboard_space.png";
                case "shift": case "left shift": case "right shift": return "keyboard_shift.png";
                case "tab": return "keyboard_tab.png";
                case "left ctrl": case "right ctrl": case "ctrl": return "keyboard_ctrl.png";
                case "←": case "left": return "keyboard_arrow_left.png";
                case "→": case "right": return "keyboard_arrow_right.png";
                case "↑": case "up": return "keyboard_arrow_up.png";
                case "↓": case "down": return "keyboard_arrow_down.png";
                case "arrows": return "keyboard_arrows.png";
                case "wasd": return null; // no single Kenney glyph — keep text
                default:
                    if (s.Length == 1 && ((s[0] >= 'a' && s[0] <= 'z') || (s[0] >= '0' && s[0] <= '9')))
                        return "keyboard_" + s + ".png";
                    return null;
            }
        }

        /// <summary>Map a rebindable pad control to the glyph button.</summary>
        public static Glyphs.PadButton FromControls(Controls.Pad p)
        {
            switch (p)
            {
                case Controls.Pad.South: return Glyphs.PadButton.South;
                case Controls.Pad.East: return Glyphs.PadButton.East;
                case Controls.Pad.West: return Glyphs.PadButton.West;
                case Controls.Pad.North: return Glyphs.PadButton.North;
                case Controls.Pad.LeftShoulder: return Glyphs.PadButton.LB;
                case Controls.Pad.RightShoulder: return Glyphs.PadButton.RB;
                case Controls.Pad.LeftTrigger: return Glyphs.PadButton.LT;
                case Controls.Pad.RightTrigger: return Glyphs.PadButton.RT;
                case Controls.Pad.Select: return Glyphs.PadButton.Select;
                case Controls.Pad.Start: return Glyphs.PadButton.Start;
                case Controls.Pad.LeftStick: return Glyphs.PadButton.LS;
                case Controls.Pad.RightStick: return Glyphs.PadButton.RS;
                default: return Glyphs.PadButton.North;
            }
        }
    }
}
