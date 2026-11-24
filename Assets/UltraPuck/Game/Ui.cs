using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// Drawing helpers for menus and on-screen text: rounded panels, labels with shadows, and the colours they use.
    /// Everything is laid out on a 1280 x 720 canvas scaled to the window and the UI size setting (see <see cref="Begin"/>).
    /// </summary>
    internal static class Ui
    {
        public static readonly Color Text = new Color(0.95f, 0.95f, 0.95f);
        public static readonly Color TextDim = new Color(1f, 1f, 1f, 0.55f);
        public static readonly Color Panel = new Color(0.075f, 0.075f, 0.08f, 0.96f);
        public static readonly Color Row = new Color(1f, 1f, 1f, 0.045f);
        public static readonly Color RowFocus = new Color(1f, 1f, 1f, 0.13f);
        public static readonly Color Accent = new Color(0.96f, 0.96f, 0.96f);
        public static readonly Color Track = new Color(1f, 1f, 1f, 0.14f);
        public static readonly Color Dim = new Color(0f, 0f, 0f, 0.6f);

        private static Texture2D _round, _white, _roundBig;
        private static GUIStyle _roundStyle, _roundBigStyle;
        private static GUIStyle[] _fonts = new GUIStyle[0];

        /// <summary>Width and height of the canvas this frame (1280 x 720 at UI size 100% on a 16:9 window).</summary>
        public static float W, H;

        /// <summary>Set up scaling for this OnGUI call. <paramref name="scale"/> multiplies the UI size setting.</summary>
        public static void Begin(float scale = 1f)
        {
            var k = Screen.height / 720f * Settings.UiScale * scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
            W = Screen.width / k;
            H = Screen.height / k;
        }

        /// <summary>The mouse position on the canvas.</summary>
        public static Vector2 Mouse
        {
            get
            {
                var k = Screen.height / 720f * Settings.UiScale;
                var m = UnityEngine.InputSystem.Mouse.current;
                if (m == null) return new Vector2(-1f, -1f);
                var p = m.position.ReadValue();
                return new Vector2(p.x / k, (Screen.height - p.y) / k);
            }
        }

        public static bool MouseMoved
        {
            // a real move only: a resting mouse (or a bumped desk) mustn't pull the focus away from a controller
            get { InputMode.Update(); return InputMode.MouseUsed && !LocalPlayers.Blocked; }
        }

        public static bool Clicked
        {
            get { var m = UnityEngine.InputSystem.Mouse.current; return m != null && m.leftButton.wasPressedThisFrame && !LocalPlayers.Blocked; }
        }

        /// <summary>
        /// True once per frame when the left mouse was pressed (consumes the click so OnGUI can't fire it twice).
        /// Use this for menu buttons instead of <see cref="Clicked"/> + Repaint.
        /// </summary>
        public static bool TakeClick()
        {
            if (!Clicked || _clickFrame == Time.frameCount) return false;
            _clickFrame = Time.frameCount;
            return true;
        }
        private static int _clickFrame = -1;

        public static bool MouseHeld
        {
            get { var m = UnityEngine.InputSystem.Mouse.current; return m != null && m.leftButton.isPressed && !LocalPlayers.Blocked; }
        }

        // ------------------------------------------------------------------ shapes

        private static void Init()
        {
            if (_round != null) return;
            _white = Texture2D.whiteTexture;
            // a white rounded square, drawn 9-sliced so corners stay round at any size
            const int size = 64, r = 16;
            _round = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, r, size - r), cy = Mathf.Clamp(y + 0.5f, r, size - r);
                    var d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    var a = Mathf.Clamp01(r - d + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            _round.SetPixels32(px);
            _round.Apply();
            _roundStyle = new GUIStyle { normal = { background = _round }, border = new RectOffset(r, r, r, r) };
            // a big one for big curves (circles on the player cards), so they stay sharp
            const int bigSize = 256, br = 96;
            _roundBig = new Texture2D(bigSize, bigSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var bpx = new Color32[bigSize * bigSize];
            for (var y = 0; y < bigSize; y++)
                for (var x = 0; x < bigSize; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, br, bigSize - br), cy = Mathf.Clamp(y + 0.5f, br, bigSize - br);
                    var d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    var a = Mathf.Clamp01(br - d + 0.5f);
                    bpx[y * bigSize + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            _roundBig.SetPixels32(bpx);
            _roundBig.Apply();
            _roundBigStyle = new GUIStyle { normal = { background = _roundBig }, border = new RectOffset(br, br, br, br) };
        }

        /// <summary>A filled rounded rectangle. <paramref name="radius"/> is in canvas units (0 = square corners).</summary>
        public static void Box(Rect r, Color c, float radius = 10f)
        {
            if (Event.current.type != EventType.Repaint) return;
            Init();
            var old = GUI.color;
            GUI.color = c;
            if (radius <= 0.5f) GUI.DrawTexture(r, _white);
            else
            {
                // the 9-slice border is 16 px (96 for big curves); scale the drawn rect so the corner comes out at the asked radius
                var big = radius > 20f;
                var k = radius / (big ? 96f : 16f);
                var m = GUI.matrix;
                GUI.matrix = m * Matrix4x4.TRS(new Vector3(r.x, r.y, 0f), Quaternion.identity, new Vector3(k, k, 1f));
                (big ? _roundBigStyle : _roundStyle).Draw(new Rect(0f, 0f, r.width / k, r.height / k), false, false, false, false);
                GUI.matrix = m;
            }
            GUI.color = old;
        }

        public static void Fill(Color c) { Box(new Rect(0f, 0f, W, H), c, 0f); }

        // ------------------------------------------------------------------ text

        /// <summary>While set (the players screens): every label is bold and has no drop shadow.</summary>
        public static bool Flat;

        public static GUIStyle Font(int size, bool bold = false, TextAnchor align = TextAnchor.MiddleLeft)
        {
            if (Flat) bold = true;
            var key = size * 4 + (bold ? 1 : 0);
            foreach (var f in _fonts) if (f.fontSize == size && (f.fontStyle == FontStyle.Bold) == bold) { f.alignment = align; return f; }
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, alignment = align, wordWrap = false, richText = true, clipping = TextClipping.Overflow };
            s.normal.textColor = Color.white;
            var list = new System.Collections.Generic.List<GUIStyle>(_fonts) { s };
            _fonts = list.ToArray();
            return s;
        }

        /// <summary>Text with a soft shadow, so it reads over anything.</summary>
        public static void Label(Rect r, string text, GUIStyle style, Color c, bool shadow = true)
        {
            Label(r, text, style, c, shadow ? new Vector2(1.5f, 2f) : Vector2.zero);
        }

        /// <summary>
        /// Text with its shadow this far down and right (zero: none). Small text wants a smaller offset than the usual
        /// 1.5 / 2, or the shadow sits apart from the letters instead of hugging them.
        /// </summary>
        public static void Label(Rect r, string text, GUIStyle style, Color c, Vector2 shadowOffset)
        {
            var old = style.normal.textColor;
            if (shadowOffset != Vector2.zero && !Flat)
            {
                style.normal.textColor = new Color(0f, 0f, 0f, 0.6f * c.a);
                GUI.Label(new Rect(r.x + shadowOffset.x, r.y + shadowOffset.y, r.width, r.height), StripColors(text), style);
            }
            style.normal.textColor = c;
            GUI.Label(r, text, style);
            style.normal.textColor = old;
        }

        /// <summary>The same text without colour tags (for shadows).</summary>
        public static string StripColors(string s)
        {
            if (s.IndexOf('<') < 0) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] == '<')
                {
                    var end = s.IndexOf('>', i);
                    var tag = end > i ? s.Substring(i, end - i + 1) : "";
                    if (tag.StartsWith("<color") || tag == "</color>") { i = end; continue; }
                }
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        /// <summary>A team's colour, as a small rounded square (with the team's colour name shown next to it wherever it matters).</summary>
        public static void Swatch(Rect r, int team)
        {
            Box(r, Palette.Team(team), r.width * 0.22f);
        }

        /// <summary>A player symbol (see <see cref="Symbols"/>) in a colour.</summary>
        public static void Symbol(Rect r, int symbol, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Symbols.Texture(symbol));
            GUI.color = old;
        }

        /// <summary>Ease-out curve (fast start, gentle stop) for 0..1.</summary>
        public static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t) * (1f - t); }

        /// <summary>Overshoots a little before settling: the "pop" used on menus and cards.</summary>
        public static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }
    }
}
