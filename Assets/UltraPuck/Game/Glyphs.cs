using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UltraPuck.Game
{
    /// <summary>
    /// Which kind of input the person last used: the keyboard and mouse, or a controller (and which family of
    /// controller). On-screen hints show only those controls, with that controller's own button pictures, and the
    /// mouse cursor hides while a controller is in use (a resting mouse can't steal the menu focus either).
    /// </summary>
    internal static class InputMode
    {
        public enum Family { Keyboard, Xbox, PlayStation, Switch, Generic }

        public static Family Current { get; private set; }
        /// <summary>A controller was used last (not the keyboard or mouse).</summary>
        public static bool Pad { get { return Current != Family.Keyboard; } }
        /// <summary>PlayStation 5 (DualSense) rather than 4: their "select" buttons are named differently.</summary>
        public static bool DualSense { get; private set; }
        /// <summary>The mouse moved (properly, not a jitter) or clicked this frame.</summary>
        public static bool MouseUsed { get; private set; }

        private static int _frame = -1;

        static InputMode()
        {
            // start with a connected controller's pictures if there is one and no keyboard has been touched yet
            Current = Family.Keyboard;
            if (Gamepad.current != null) Use(Gamepad.current);
        }

        /// <summary>Once a frame (more calls in the same frame do nothing): notice which device was just used.</summary>
        public static void Update()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            MouseUsed = false;
            if (LocalPlayers.Blocked) return;
            foreach (var g in Gamepad.all) if (Active(g)) { Use(g); return; }
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) { Set(Family.Keyboard); return; }
            var m = Mouse.current;
            if (m != null && (m.delta.ReadValue().sqrMagnitude > 9f || m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame
                              || Mathf.Abs(m.scroll.ReadValue().y) > 0.1f))
            {
                MouseUsed = true;
                Set(Family.Keyboard);
            }
        }

        private static void Use(Gamepad g)
        {
            var f = FamilyOf(g);
            DualSense = f == Family.PlayStation && (g.GetType().Name.Contains("DualSense") || Product(g).Contains("dualsense"));
            Set(f);
        }

        private static void Set(Family f)
        {
            if (f == Current && _cursorSet) return;
            Current = f;
            _cursorSet = true;
            Cursor.visible = f == Family.Keyboard;
        }

        private static bool _cursorSet;

        private static string Product(InputDevice d)
        {
            return ((d.description.product ?? "") + " " + (d.description.manufacturer ?? "") + " " + d.displayName).ToLowerInvariant();
        }

        public static Family FamilyOf(InputDevice d)
        {
            if (d == null || d is Keyboard || d is Mouse) return Family.Keyboard;
            // the Input System's own controller types first, then the name the device reports
            var type = d.GetType();
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                var n = t.Name;
                if (n.Contains("DualShock") || n.Contains("DualSense")) return Family.PlayStation;
                if (n.Contains("XInput") || n.Contains("XboxOne") || n.Contains("XboxGamepad")) return Family.Xbox;
                if (n.Contains("SwitchPro") || n.Contains("Switch")) return Family.Switch;
            }
            var p = Product(d);
            if (p.Contains("dualsense") || p.Contains("dualshock") || p.Contains("wireless controller") || p.Contains("playstation") || p.Contains("sony")) return Family.PlayStation;
            if (p.Contains("xbox") || p.Contains("xinput")) return Family.Xbox;
            if (p.Contains("pro controller") || p.Contains("nintendo") || p.Contains("joy-con")) return Family.Switch;
            return Family.Generic;
        }

        /// <summary>Did this controller do something deliberate this frame (a button, or a stick pushed well over)?</summary>
        private static bool Active(Gamepad g)
        {
            if (g.buttonSouth.wasPressedThisFrame || g.buttonEast.wasPressedThisFrame || g.buttonWest.wasPressedThisFrame || g.buttonNorth.wasPressedThisFrame) return true;
            if (g.leftShoulder.wasPressedThisFrame || g.rightShoulder.wasPressedThisFrame || g.leftTrigger.wasPressedThisFrame || g.rightTrigger.wasPressedThisFrame) return true;
            if (g.startButton.wasPressedThisFrame || g.selectButton.wasPressedThisFrame) return true;
            if (g.dpad.up.wasPressedThisFrame || g.dpad.down.wasPressedThisFrame || g.dpad.left.wasPressedThisFrame || g.dpad.right.wasPressedThisFrame) return true;
            // sticks: only a real push (a drifting stick mustn't flip the mode back and forth)
            float l = g.leftStick.ReadValue().sqrMagnitude, r = g.rightStick.ReadValue().sqrMagnitude;
            var was = _stickHeld.Contains(g);
            if (l > 0.5f || r > 0.5f) { _stickHeld.Add(g); return !was; }
            if (l < 0.2f && r < 0.2f) _stickHeld.Remove(g);
            return false;
        }

        private static readonly HashSet<Gamepad> _stickHeld = new HashSet<Gamepad>();
    }

    /// <summary>
    /// Hints with button pictures. Write the action in braces and it's drawn as the right button for whatever the
    /// person is using: "{confirm} Choose   {back} Back" shows an A and a B on an Xbox pad, a cross and a circle on
    /// a PlayStation pad, or Enter and Esc keys on the keyboard.
    /// </summary>
    internal static class Glyphs
    {
        private enum B { South, East, West, North, LB, RB, Start, Select, LS, RS, DPad, DPadLR }

        /// <summary>Controller button(s) for an action.</summary>
        private static B[] PadFor(string action)
        {
            switch (action)
            {
                case "confirm": case "join": case "shoot": return new[] { B.South };
                case "back": case "charge": case "leave": return new[] { B.East };
                case "utility": case "freeplay": return new[] { B.North };
                case "start": case "menu": return new[] { B.Start };
                case "settings": case "reset": return new[] { B.Select };
                case "tabs": return new[] { B.LB, B.RB };
                case "move": return new[] { B.LS };
                case "aim": case "look": return new[] { B.RS };
                case "side": return new[] { B.DPadLR };
                case "nav": return new[] { B.DPad };
                default: return new B[0];
            }
        }

        /// <summary>Keyboard key(s) for an action.</summary>
        private static string[] KeysFor(string action)
        {
            switch (action)
            {
                case "confirm": return new[] { "Enter" };
                case "join": case "shoot": return new[] { "Space" };
                case "back": case "leave": return new[] { "Esc" };
                case "charge": return new[] { "Shift" };
                case "utility": return new[] { "E" };
                case "freeplay": return new[] { "F" };
                case "start": return new[] { "Enter" };
                case "menu": return new[] { "Esc" };
                case "settings": return new[] { "Tab" };
                case "reset": return new[] { "R" };
                case "tabs": return new[] { "Q", "E" };
                case "move": return new[] { "WASD" };
                case "aim": return new[] { "Arrows" };
                case "look": case "side": return new[] { "←", "→" };
                case "nav": return new[] { "↑", "↓" };
                default: return new[] { action };
            }
        }

        // ------------------------------------------------------------------ drawing a hint line

        private static InputMode.Family _f;

        private struct Part { public string Text; public string Action; public float W; }
        private static readonly List<Part> _parts = new List<Part>();

        /// <summary>Draw a hint with button pictures in <paramref name="r"/> (aligned left, centre or right as the style says).</summary>
        /// <param name="device">Show this controller's (or the keyboard's) buttons instead of the last used device's.</param>
        public static void Hint(Rect r, string text, int size, Color c, TextAnchor align = TextAnchor.MiddleCenter, InputDevice device = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            InputMode.Update();
            _f = device != null ? InputMode.FamilyOf(device) : InputMode.Current;
            var style = Ui.Font(size);
            var h = size * 1.45f;
            Split(text, style, h);
            var total = 0f;
            foreach (var p in _parts) total += p.W;
            var x = align == TextAnchor.MiddleLeft || align == TextAnchor.UpperLeft ? r.x
                : align == TextAnchor.MiddleRight || align == TextAnchor.UpperRight ? r.xMax - total
                : r.center.x - total / 2f;
            var cy = r.center.y;
            foreach (var p in _parts)
            {
                if (p.Action == null) Ui.Label(new Rect(x, cy - h / 2f, p.W + 4f, h), p.Text, style, c);
                else DrawAction(new Rect(x, cy - h / 2f, p.W, h), p.Action, c.a, size);
                x += p.W;
            }
            style.alignment = TextAnchor.MiddleLeft;
        }

        /// <summary>How wide a hint will be.</summary>
        public static float Width(string text, int size)
        {
            _f = InputMode.Current;
            Split(text, Ui.Font(size), size * 1.45f);
            var total = 0f;
            foreach (var p in _parts) total += p.W;
            return total;
        }

        private static void Split(string text, GUIStyle style, float h)
        {
            _parts.Clear();
            style.alignment = TextAnchor.MiddleLeft;
            var i = 0;
            while (i < text.Length)
            {
                var open = text.IndexOf('{', i);
                if (open < 0) { AddText(text.Substring(i), style); break; }
                if (open > i) AddText(text.Substring(i, open - i), style);
                var close = text.IndexOf('}', open);
                if (close < 0) { AddText(text.Substring(open), style); break; }
                var action = text.Substring(open + 1, close - open - 1);
                _parts.Add(new Part { Action = action, W = ActionWidth(action, h, style.fontSize) + 6f });
                i = close + 1;
            }
        }

        private static void AddText(string s, GUIStyle style)
        {
            _parts.Add(new Part { Text = s, W = style.CalcSize(new GUIContent(Ui.StripColors(s))).x });
        }

        private static float ActionWidth(string action, float h, int size)
        {
            var w = 0f;
            if ((_f != InputMode.Family.Keyboard)) foreach (var b in PadFor(action)) w += ButtonWidth(b, h, size) + 3f;
            else foreach (var k in KeysFor(action)) w += KeyWidth(k, h, size) + 3f;
            return Mathf.Max(0f, w - 3f);
        }

        private static void DrawAction(Rect r, string action, float alpha, int size)
        {
            var x = r.x + 3f;
            if ((_f != InputMode.Family.Keyboard))
                foreach (var b in PadFor(action)) { var w = ButtonWidth(b, r.height, size); DrawButton(new Rect(x, r.y, w, r.height), b, alpha, size); x += w + 3f; }
            else
                foreach (var k in KeysFor(action)) { var w = KeyWidth(k, r.height, size); DrawKey(new Rect(x, r.y, w, r.height), k, alpha, size); x += w + 3f; }
        }

        // ------------------------------------------------------------------ keyboard keys

        private static float KeyWidth(string key, float h, int size)
        {
            var w = Ui.Font(Mathf.RoundToInt(size * 0.8f), true).CalcSize(new GUIContent(key)).x + size * 0.7f;
            return Mathf.Max(w, h * 0.95f);
        }

        private static void DrawKey(Rect r, string key, float alpha, int size)
        {
            var cap = new Rect(r.x, r.y + r.height * 0.06f, r.width, r.height * 0.88f);
            Ui.Box(new Rect(cap.x, cap.y + 2f, cap.width, cap.height), new Color(0f, 0f, 0f, 0.45f * alpha), 6f);
            Ui.Box(cap, new Color(0.92f, 0.92f, 0.94f, 0.95f * alpha), 6f);
            Ui.Box(new Rect(cap.x + 2f, cap.yMax - 5f, cap.width - 4f, 3f), new Color(0f, 0f, 0f, 0.12f * alpha), 1.5f);
            Ui.Label(cap, key, Ui.Font(Mathf.RoundToInt(size * 0.8f), true, TextAnchor.MiddleCenter), new Color(0.1f, 0.1f, 0.12f, alpha), false);
        }

        // ------------------------------------------------------------------ controller buttons

        private static float ButtonWidth(B b, float h, int size)
        {
            switch (b)
            {
                case B.LB: case B.RB: return h * 1.5f;
                case B.Start: case B.Select:
                    var label = PillLabel(b);
                    return label.Length <= 1 ? h : Ui.Font(Mathf.RoundToInt(size * 0.72f), true).CalcSize(new GUIContent(label)).x + size * 0.9f;
                default: return h;
            }
        }

        private static string PillLabel(B b)
        {
            var f = _f;
            switch (b)
            {
                case B.LB: return f == InputMode.Family.PlayStation ? "L1" : f == InputMode.Family.Switch ? "L" : "LB";
                case B.RB: return f == InputMode.Family.PlayStation ? "R1" : f == InputMode.Family.Switch ? "R" : "RB";
                case B.Start: return f == InputMode.Family.PlayStation ? "OPTIONS" : f == InputMode.Family.Switch ? "+" : f == InputMode.Family.Xbox ? "MENU" : "START";
                case B.Select: return f == InputMode.Family.PlayStation ? (InputMode.DualSense ? "CREATE" : "SHARE") : f == InputMode.Family.Switch ? "−" : f == InputMode.Family.Xbox ? "VIEW" : "SELECT";
                default: return "";
            }
        }

        private static readonly Color Body = new Color(0.13f, 0.13f, 0.15f);

        private static void DrawButton(Rect r, B b, float alpha, int size)
        {
            var d = r.height * 0.9f;
            var circle = new Rect(r.center.x - d / 2f, r.center.y - d / 2f, d, d);
            var f = _f;
            switch (b)
            {
                case B.South: case B.East: case B.West: case B.North:
                    Ui.Box(new Rect(circle.x, circle.y + 1.5f, d, d), new Color(0f, 0f, 0f, 0.4f * alpha), d / 2f);
                    Ui.Box(circle, WithAlpha(Body, 0.95f * alpha), d / 2f);
                    if (f == InputMode.Family.PlayStation) Shape(circle, b, alpha);
                    else
                    {
                        string letter; Color col;
                        Face(b, f, out letter, out col);
                        Ui.Label(circle, letter, Ui.Font(Mathf.RoundToInt(size * 0.85f), true, TextAnchor.MiddleCenter), WithAlpha(col, alpha), false);
                    }
                    break;
                case B.LB: case B.RB: case B.Start: case B.Select:
                    var pill = new Rect(r.x, r.y + r.height * 0.14f, r.width, r.height * 0.72f);
                    Ui.Box(new Rect(pill.x, pill.y + 1.5f, pill.width, pill.height), new Color(0f, 0f, 0f, 0.4f * alpha), pill.height / 2f);
                    Ui.Box(pill, WithAlpha(Body, 0.95f * alpha), pill.height / 2f);
                    var small = b == B.Start || b == B.Select;
                    Ui.Label(pill, PillLabel(b), Ui.Font(Mathf.RoundToInt(size * (small ? 0.6f : 0.75f)), true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, alpha), false);
                    break;
                case B.LS: case B.RS:
                    Ui.Box(new Rect(circle.x, circle.y + 1.5f, d, d), new Color(0f, 0f, 0f, 0.4f * alpha), d / 2f);
                    Ui.Box(circle, WithAlpha(Body, 0.95f * alpha), d / 2f);
                    Ui.Box(new Rect(circle.x + d * 0.14f, circle.y + d * 0.14f, d * 0.72f, d * 0.72f), new Color(1f, 1f, 1f, 0.16f * alpha), d * 0.36f);
                    Ui.Label(circle, b == B.LS ? "L" : "R", Ui.Font(Mathf.RoundToInt(size * 0.75f), true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, alpha), false);
                    break;
                case B.DPad: case B.DPadLR:
                    var arm = d * 0.32f;
                    var dim = new Color(1f, 1f, 1f, 0.35f * alpha);
                    var lit = new Color(1f, 1f, 1f, 0.95f * alpha);
                    Ui.Box(new Rect(circle.center.x - arm / 2f, circle.y + d * 0.04f, arm, d * 0.92f), b == B.DPad ? lit : dim, 2.5f);
                    Ui.Box(new Rect(circle.x + d * 0.04f, circle.center.y - arm / 2f, d * 0.92f, arm), b == B.DPadLR ? lit : dim, 2.5f);
                    break;
            }
        }

        private static void Face(B b, InputMode.Family f, out string letter, out Color col)
        {
            if (f == InputMode.Family.Switch)
            {
                // Nintendo's layout: B at the bottom, A on the right
                letter = b == B.South ? "B" : b == B.East ? "A" : b == B.West ? "Y" : "X";
                col = Color.white;
                return;
            }
            switch (b)
            {
                case B.South: letter = "A"; col = new Color(0.45f, 0.82f, 0.3f); break;
                case B.East: letter = "B"; col = new Color(0.95f, 0.33f, 0.3f); break;
                case B.West: letter = "X"; col = new Color(0.3f, 0.6f, 1f); break;
                default: letter = "Y"; col = new Color(1f, 0.8f, 0.2f); break;
            }
        }

        private static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        // PlayStation shapes, drawn from small textures made once (a cross, circle, square and triangle outline)
        private static Texture2D[] _shapes;

        private static void Shape(Rect circle, B b, float alpha)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (_shapes == null) _shapes = new[] { MakeShape(0), MakeShape(1), MakeShape(2), MakeShape(3) };
            int k; Color col;
            switch (b)
            {
                case B.South: k = 0; col = new Color(0.55f, 0.7f, 1f); break;     // cross
                case B.East: k = 1; col = new Color(1f, 0.42f, 0.45f); break;     // circle
                case B.West: k = 2; col = new Color(0.95f, 0.55f, 0.85f); break;  // square
                default: k = 3; col = new Color(0.35f, 0.9f, 0.75f); break;       // triangle
            }
            var old = GUI.color;
            GUI.color = WithAlpha(col, alpha);
            var m = circle.width * 0.2f;
            GUI.DrawTexture(new Rect(circle.x + m, circle.y + m, circle.width - 2f * m, circle.height - 2f * m), _shapes[k]);
            GUI.color = old;
        }

        private static Texture2D MakeShape(int kind)
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            const float w = 0.075f; // half the stroke width
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    // u right, v down (textures are drawn with row 0 at the bottom)
                    var u = (x + 0.5f) / n;
                    var v = 1f - (y + 0.5f) / n;
                    var p = new Vector2(u, v);
                    float d;
                    switch (kind)
                    {
                        case 0: d = Mathf.Min(Seg(p, new Vector2(0.15f, 0.15f), new Vector2(0.85f, 0.85f)), Seg(p, new Vector2(0.85f, 0.15f), new Vector2(0.15f, 0.85f))); break;
                        case 1: d = Mathf.Abs((p - new Vector2(0.5f, 0.5f)).magnitude - 0.36f); break;
                        case 2: d = Mathf.Abs(Mathf.Max(Mathf.Abs(u - 0.5f), Mathf.Abs(v - 0.5f)) - 0.33f); break;
                        default:
                            Vector2 a = new Vector2(0.5f, 0.12f), b = new Vector2(0.88f, 0.8f), c = new Vector2(0.12f, 0.8f);
                            d = Mathf.Min(Seg(p, a, b), Mathf.Min(Seg(p, b, c), Seg(p, c, a)));
                            break;
                    }
                    var alpha = Mathf.Clamp01((w - d) * n + 0.5f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        private static float Seg(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).magnitude;
        }
    }
}
