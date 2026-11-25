using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// The first thing on screen when the game starts: the bots' match turned down behind the game's name in big
    /// letters, and "Press any button". The letters drop in one after another, with a gap left between ULTRA and PUCK;
    /// a puck falls from the top through that gap (with the in-game trail, in white) and stops under the name, and a
    /// glowing red and blue beam shoot out from it either way. Pressing
    /// anything flies the name up to its place on the main menu (the gap closing on the way). It waits for the window to
    /// settle first (the game going fullscreen) so nothing is missed. <see cref="MainMenu"/> shows it (its Title page)
    /// and takes the button press; this draws it, and the name wherever else the menus show it.
    /// </summary>
    internal static class TitleScreen
    {
        /// <summary>Where the name sits on the menus (its left edge and top), at size 1.</summary>
        public const float MenuX = 80f, MenuY = 70f;
        /// <summary>Seconds the change from this screen to the main menu takes.</summary>
        public const float LeaveTime = 0.45f;

        private const string Name = "ULTRA PUCK";
        private const int Split = 5; // the space between ULTRA and PUCK
        private const int SmallFont = 76, BigFont = 132;
        /// <summary>Height of the name's line at size 1.</summary>
        private const float Line = 90f;
        /// <summary>The room left between ULTRA and PUCK for the puck on this screen, and the puck's size (size-1 pixels).</summary>
        private const float Gap = 40f, PuckSize = 30f;

        // before anything moves: at least this long after the screen appears, and the window the same size for this long
        // (the game goes fullscreen just after it starts, and the animation would be half over by then)
        private const float StartDelay = 0.7f, SettleTime = 0.45f;
        // when things happen, in seconds after the animation starts (slow enough to follow each beat)
        private const float LettersAt = 0.2f, LetterGap = 0.14f, LetterDrop = 0.95f;
        private const float PuckAt = 1.75f, PuckFall = 0.9f, BeamsAt = PuckAt + PuckFall, BeamGrow = 1.1f, PromptAt = BeamsAt + 1.1f;

        private static readonly string[] _letter = new string[Name.Length];
        private static float[] _smallAt, _bigAt;
        private static float _layoutFor;

        /// <summary>
        /// Where each letter starts along the line, then how wide the whole name is, then how wide "ULTRA" is, in a font
        /// size (no gap).
        /// </summary>
        private static float[] Layout(int font)
        {
            // measured again if the window or the UI size changes (text is fitted to the pixels it lands on)
            var key = Screen.height * 1000f + Settings.UiScale;
            if (_layoutFor != key) { _smallAt = _bigAt = null; _layoutFor = key; }
            var at = font == BigFont ? _bigAt : _smallAt;
            if (at != null) return at;
            var style = Ui.Font(font, true);
            at = new float[Name.Length + 2];
            for (var i = 0; i < Name.Length; i++)
            {
                if (_letter[i] == null) _letter[i] = Name.Substring(i, 1);
                if (Name[i] == ' ') continue;
                at[i] = style.CalcSize(new GUIContent(Name.Substring(0, i + 1))).x - style.CalcSize(new GUIContent(_letter[i])).x;
            }
            at[Name.Length] = style.CalcSize(new GUIContent(Name)).x;
            at[Name.Length + 1] = style.CalcSize(new GUIContent(Name.Substring(0, Split))).x;
            if (font == BigFont) _bigAt = at; else _smallAt = at;
            return at;
        }

        private static int FontFor(float scale) { return scale > 1.35f ? BigFont : SmallFont; }

        /// <summary>From font units to screen pixels at a size.</summary>
        private static float Px(float scale) { var font = FontFor(scale); return scale * SmallFont / font; }

        /// <summary>How wide the name is at a size (1 = as on the menus), with <paramref name="gap"/> (size-1 pixels) between the words.</summary>
        private static float Width(float scale, float gap = 0f)
        {
            return Layout(FontFor(scale))[Name.Length] * Px(scale) + gap * scale;
        }

        /// <summary>Where the gap between the words is centred, from the name's left edge (pixels).</summary>
        private static float GapCentre(float scale, float gap)
        {
            var at = Layout(FontFor(scale));
            var k = Px(scale);
            return (at[Name.Length + 1] * k + at[Split + 1] * k + gap * scale) / 2f;
        }

        /// <summary>
        /// The game's name with its top left at (<paramref name="x"/>, <paramref name="y"/>). <paramref name="scale"/> 1
        /// is the size on the menus. <paramref name="since"/>: seconds into the title animation, for the letters dropping
        /// in one by one (leave it out and they're simply there). <paramref name="gap"/>: extra room (size-1 pixels)
        /// between ULTRA and PUCK.
        /// </summary>
        public static void Logo(float x, float y, float scale, float alpha, float since = 100f, float gap = 0f)
        {
            if (Event.current.type != EventType.Repaint) return;
            var font = FontFor(scale);
            var at = Layout(font);
            var style = Ui.Font(font, true);
            // big letters are drawn with a big font (sharp), and the canvas stretched only for what's left over
            var k = scale * SmallFont / font;
            var h = Line * font / SmallFont;
            var push = gap * scale / k; // the gap, in the canvas's units
            var old = GUI.matrix;
            GUI.matrix = old * Matrix4x4.TRS(new Vector3(x, y, 0f), Quaternion.identity, new Vector3(k, k, 1f));
            for (var i = 0; i < Name.Length; i++)
            {
                if (Name[i] == ' ') continue;
                var t = since - LettersAt - i * LetterGap;
                if (t <= 0f) continue;
                var drop = (1f - Ui.EaseOutBack(t / LetterDrop)) * -0.55f * h;
                var lx = at[i] + (i > Split ? push : 0f);
                Ui.Label(new Rect(lx, drop, 300f, h), _letter[i], style, new Color(1f, 1f, 1f, alpha * Ui.EaseOut(t / 0.45f)));
            }
            GUI.matrix = old;
        }

        // ------------------------------------------------------------------ the clock

        private static float _shownAt = -1f, _startAt = -1f, _sizeAt = -100f;
        private static int _w, _h;

        /// <summary>
        /// Seconds into the animation (below 0 while it waits for the window to settle). <paramref name="t"/>: seconds
        /// since the screen appeared.
        /// </summary>
        private static float Clock(float t)
        {
            var now = Time.unscaledTime;
            var shown = now - t;
            if (Mathf.Abs(shown - _shownAt) > 0.05f) { _shownAt = shown; _startAt = -1f; } // shown again: start over
            if (Screen.width != _w || Screen.height != _h) { _w = Screen.width; _h = Screen.height; _sizeAt = now; }
            if (_startAt < 0f && t >= StartDelay && now - _sizeAt >= SettleTime) _startAt = now;
            return _startAt < 0f ? -1f : now - _startAt;
        }

        /// <summary>0 at the top of a fall, 1 landed: falls faster and faster and stops dead (no bounce).</summary>
        private static float Fall(float t) { t = Mathf.Clamp01(t); return t * t; }

        // the in-game puck's trail (PuckView): fading copies behind it, a little smaller each, one per step
        private const int TrailDots = 6;
        private const float TrailStep = 1f / 45f;

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
            var a = Clock(t);
            if (leave > 0f && a < 0f) a = 100f; // a button before it started: straight to the end

            // the match behind, turned down so the name stands out
            Ui.Box(new Rect(0f, 0f, w, h), new Color(0f, 0f, 0f, 0.45f * stay), 0f);

            // the name: as big as fits, a little above the middle, with the gap between the words in the very middle
            var big = Mathf.Min(1.75f, (w - 140f) / Width(1f, Gap));
            var wide = Width(big, Gap);
            var home = new Vector2(w / 2f - GapCentre(big, Gap), h * 0.36f - Line * big / 2f);
            float cx = w / 2f, cy = home.y + Line * big + 16f; // under the name, below the gap between the words
            var puck = PuckSize * big;
            const float thick = 6f;

            // the beams: out either way from the puck once it lands, under the name to both its ends
            var grow = a > BeamsAt ? Ui.EaseOut((a - BeamsAt) / BeamGrow) : 0f;
            if (grow > 0f && stay > 0f)
            {
                var from = puck / 2f + 12f;
                var left = (cx - home.x - from) * grow;
                var right = (home.x + wide - cx - from) * grow;
                var red = Palette.Colours[0]; red.a = stay;
                var blue = Palette.Colours[1]; blue.a = stay;
                // a soft glow round each (wider, fainter layers under the line), brightest as they shoot out
                var flare = 1f + 0.6f * (1f - Ui.EaseOut((a - BeamsAt) / 1.6f));
                const float halo = 26f;
                var gr = red; gr.a = stay * 0.55f * flare; var gb = blue; gb.a = stay * 0.55f * flare;
                if (left > 1f) Ui.Glow(new Rect(cx - from - left - halo, cy - halo, left + halo * 2f, halo * 2f), gr, halo);
                if (right > 1f) Ui.Glow(new Rect(cx + from - halo, cy - halo, right + halo * 2f, halo * 2f), gb, halo);
                if (left > 1f) Ui.Box(new Rect(cx - from - left, cy - thick / 2f, left, thick), red, thick / 2f);
                if (right > 1f) Ui.Box(new Rect(cx + from, cy - thick / 2f, right, thick), blue, thick / 2f);
            }

            var gap = Mathf.Lerp(Gap, 0f, move);
            var scale = Mathf.Lerp(big, 1f, move);
            var start = new Vector2(w / 2f - GapCentre(scale, gap), Mathf.Lerp(home.y, MenuY, move));
            Logo(Mathf.Lerp(start.x, MenuX, move), start.y, scale, 1f, a, gap);

            // the puck: falls from the top, down through the gap between the words, and stops dead under them; the
            // beams shoot out as it lands
            if (a > PuckAt && stay > 0f)
            {
                var f = (a - PuckAt) / PuckFall;
                var py = Mathf.Lerp(-puck, cy, Fall(f));
                // the in-game trail behind it on the way down, in white: where it was a step, two steps... ago
                for (var i = TrailDots - 1; i >= 0; i--)
                {
                    var was = Mathf.Lerp(-puck, cy, Fall((a - PuckAt - (i + 1) * TrailStep) / PuckFall));
                    if (py - was < puck * 0.5f) continue; // not moving (landed), or too close to show
                    var d = puck * (0.92f - i * 0.1f);
                    Ui.Disc(new Rect(cx - d / 2f, was - d / 2f, d, d), new Color(1f, 1f, 1f, 0.42f * (1f - i / (float)TrailDots) * stay));
                }
                // a ring of light off it as it lands
                var hit = (a - BeamsAt) / 0.9f;
                if (hit > 0f && hit < 1f)
                {
                    var d = puck + 170f * Ui.EaseOut(hit);
                    Ui.Disc(new Rect(cx - d / 2f, cy - d / 2f, d, d), new Color(1f, 1f, 1f, 0.22f * (1f - hit) * stay));
                }
                var size = puck;
                var edge = Palette.PuckEdge; edge.a = stay;
                var body = Palette.Puck; body.a = stay;
                var rim = 5f * big / 1.75f;
                Ui.Disc(new Rect(cx - size / 2f, py - size / 2f, size, size), edge);
                Ui.Disc(new Rect(cx - size / 2f + rim, py - size / 2f + rim, size - rim * 2f, size - rim * 2f), body);
            }

            // the prompt, breathing slowly so it catches the eye
            var show = Ui.EaseOut((a - PromptAt) / 0.8f) * stay;
            if (show > 0f)
            {
                var beat = 0.5f - 0.5f * Mathf.Cos((a - PromptAt) * Mathf.PI * 2f / 1.7f);
                var pill = new Rect(cx - 190f, h * 0.735f + (1f - show) * 14f, 380f, 60f);
                Ui.Box(pill, new Color(1f, 1f, 1f, (0.07f + 0.09f * beat) * show), 30f);
                Ui.Label(pill, InputMode.Pad ? "Press any button" : "Press any key", Ui.Font(26, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, (0.6f + 0.4f * beat) * show));
            }

            // the version, where the main menu has it (the menu draws its own once it takes over)
            if (leave <= 0f) Ui.Label(new Rect(w - 220f, h - 36f, 200f, 24f), "v" + Updater.Current, Ui.Font(14, false, TextAnchor.MiddleRight), new Color(1f, 1f, 1f, 0.35f * stay));
        }
    }
}
