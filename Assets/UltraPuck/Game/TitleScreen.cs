using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// The first thing on screen when the game starts: the bots' match turned down behind the game's name in big
    /// letters, and "Press any button". The letters drop in one after another, a puck slides in under them and a
    /// red and a blue line shoot out from it. Pressing anything flies the name up to its place on the main menu.
    /// <see cref="MainMenu"/> shows it (its Title page) and takes the button press; this draws it, and the name
    /// wherever else the menus show it.
    /// </summary>
    internal static class TitleScreen
    {
        /// <summary>Where the name sits on the menus (its left edge and top), at size 1.</summary>
        public const float MenuX = 80f, MenuY = 70f;
        /// <summary>Seconds the change from this screen to the main menu takes.</summary>
        public const float LeaveTime = 0.45f;

        private const string Name = "ULTRA PUCK";
        private const int SmallFont = 76, BigFont = 132;
        /// <summary>Height of the name's line at size 1.</summary>
        private const float Line = 90f;

        // when things happen, in seconds after the screen appears
        private const float LettersAt = 0.2f, LetterGap = 0.055f, PuckAt = 0.8f, PuckSlide = 0.45f, LinesAt = 1.2f, PromptAt = 1.6f;

        private static readonly string[] _letter = new string[Name.Length];
        private static float[] _smallAt, _bigAt;
        private static float _layoutFor;

        /// <summary>Where each letter starts along the line (and, last, how wide the whole name is) in a font size.</summary>
        private static float[] Layout(int font)
        {
            // measured again if the window or the UI size changes (text is fitted to the pixels it lands on)
            var key = Screen.height * 1000f + Settings.UiScale;
            if (_layoutFor != key) { _smallAt = _bigAt = null; _layoutFor = key; }
            var at = font == BigFont ? _bigAt : _smallAt;
            if (at != null) return at;
            var style = Ui.Font(font, true);
            at = new float[Name.Length + 1];
            for (var i = 0; i < Name.Length; i++)
            {
                if (_letter[i] == null) _letter[i] = Name.Substring(i, 1);
                if (Name[i] == ' ') continue;
                at[i] = style.CalcSize(new GUIContent(Name.Substring(0, i + 1))).x - style.CalcSize(new GUIContent(_letter[i])).x;
            }
            at[Name.Length] = style.CalcSize(new GUIContent(Name)).x;
            if (font == BigFont) _bigAt = at; else _smallAt = at;
            return at;
        }

        private static int FontFor(float scale) { return scale > 1.35f ? BigFont : SmallFont; }

        /// <summary>How wide the name is at a size (1 = as on the menus).</summary>
        private static float Width(float scale)
        {
            var font = FontFor(scale);
            return Layout(font)[Name.Length] * scale * SmallFont / font;
        }

        /// <summary>
        /// The game's name with its top left at (<paramref name="x"/>, <paramref name="y"/>). <paramref name="scale"/> 1
        /// is the size on the menus. <paramref name="since"/>: seconds since it appeared, for the letters dropping in
        /// one by one (leave it out and they're simply there).
        /// </summary>
        public static void Logo(float x, float y, float scale, float alpha, float since = 100f)
        {
            if (Event.current.type != EventType.Repaint) return;
            var font = FontFor(scale);
            var at = Layout(font);
            var style = Ui.Font(font, true);
            // big letters are drawn with a big font (sharp), and the canvas stretched only for what's left over
            var k = scale * SmallFont / font;
            var h = Line * font / SmallFont;
            var old = GUI.matrix;
            GUI.matrix = old * Matrix4x4.TRS(new Vector3(x, y, 0f), Quaternion.identity, new Vector3(k, k, 1f));
            for (var i = 0; i < Name.Length; i++)
            {
                if (Name[i] == ' ') continue;
                var t = since - LettersAt - i * LetterGap;
                if (t <= 0f) continue;
                var drop = (1f - Ui.EaseOutBack(t / 0.42f)) * -0.55f * h;
                Ui.Label(new Rect(at[i], drop, 300f, h), _letter[i], style, new Color(1f, 1f, 1f, alpha * Ui.EaseOut(t / 0.2f)));
            }
            GUI.matrix = old;
        }

        /// <summary>
        /// The whole screen, <paramref name="t"/> seconds after it appeared. <paramref name="leave"/> is 0 while it's
        /// up, and runs to 1 as it hands over to the main menu (drawn on top of the menu, fading, while the name
        /// travels to its place there).
        /// </summary>
        public static void Draw(float t, float leave)
        {
            if (Event.current.type != EventType.Repaint) return;
            float w = Ui.W, h = Ui.H;
            leave = Mathf.Clamp01(leave);
            var stay = 1f - Ui.EaseOut(leave);           // how much of this screen is still here
            var move = leave * leave * (3f - 2f * leave); // how far the name has travelled (eases off at both ends)

            // the match behind, turned down so the name stands out
            Ui.Box(new Rect(0f, 0f, w, h), new Color(0f, 0f, 0f, 0.45f * stay), 0f);

            // the name: as big as fits, a little above the middle
            var big = Mathf.Min(1.75f, (w - 140f) / Width(1f));
            var wide = Width(big);
            var home = new Vector2((w - wide) / 2f, h * 0.36f - Line * big / 2f);
            Logo(Mathf.Lerp(home.x, MenuX, move), Mathf.Lerp(home.y, MenuY, move), Mathf.Lerp(big, 1f, move), 1f, t);

            // under it: the puck slides in from the left and stops in the middle, then a red line and a blue line
            // shoot out from it to the ends of the name
            float cx = w / 2f, ly = home.y + Line * big + 16f;
            const float puck = 52f, thick = 6f;
            if (t > PuckAt)
            {
                var slide = Ui.EaseOut((t - PuckAt) / PuckSlide);
                var px = Mathf.Lerp(-puck, cx, slide);
                var trail = (1f - slide) * 420f;
                if (trail > 1f) Ui.Box(new Rect(px - trail, ly - thick / 2f, trail, thick), new Color(1f, 1f, 1f, 0.3f * (1f - slide) * stay), thick / 2f);

                var grow = Ui.EaseOut((t - LinesAt) / 0.5f);
                var reach = (wide / 2f - puck / 2f - 12f) * grow;
                if (reach > 1f)
                {
                    var red = Palette.Colours[0]; red.a = stay;
                    var blue = Palette.Colours[1]; blue.a = stay;
                    Ui.Box(new Rect(cx - puck / 2f - 12f - reach, ly - thick / 2f, reach, thick), red, thick / 2f);
                    Ui.Box(new Rect(cx + puck / 2f + 12f, ly - thick / 2f, reach, thick), blue, thick / 2f);
                }

                // a ring of light off the puck as it stops, and a small bounce
                var hit = (t - LinesAt) / 0.5f;
                if (hit > 0f && hit < 1f)
                {
                    var d = puck + 150f * Ui.EaseOut(hit);
                    Ui.Box(new Rect(cx - d / 2f, ly - d / 2f, d, d), new Color(1f, 1f, 1f, 0.22f * (1f - hit) * stay), d / 2f);
                }
                var size = puck * (1f + 0.22f * Mathf.Sin(Mathf.Clamp01(hit) * Mathf.PI));
                var edge = Palette.PuckEdge; edge.a = stay;
                var body = Palette.Puck; body.a = stay;
                Ui.Box(new Rect(px - size / 2f, ly - size / 2f, size, size), edge, size / 2f);
                Ui.Box(new Rect(px - size / 2f + 5f, ly - size / 2f + 5f, size - 10f, size - 10f), body, size / 2f - 5f);
            }

            // the prompt, breathing slowly so it catches the eye
            var show = Ui.EaseOut((t - PromptAt) / 0.5f) * stay;
            if (show > 0f)
            {
                var beat = 0.5f - 0.5f * Mathf.Cos((t - PromptAt) * Mathf.PI * 2f / 1.7f);
                var pill = new Rect(cx - 190f, h * 0.735f + (1f - show) * 14f, 380f, 60f);
                Ui.Box(pill, new Color(1f, 1f, 1f, (0.07f + 0.09f * beat) * show), 30f);
                Ui.Label(pill, InputMode.Pad ? "Press any button" : "Press any key", Ui.Font(26, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, (0.6f + 0.4f * beat) * show));
            }

            // the version, where the main menu has it (the menu draws its own once it takes over)
            if (leave <= 0f) Ui.Label(new Rect(w - 220f, h - 36f, 200f, 24f), "v" + Updater.Current, Ui.Font(14, false, TextAnchor.MiddleRight), new Color(1f, 1f, 1f, 0.35f * stay));
        }
    }
}
