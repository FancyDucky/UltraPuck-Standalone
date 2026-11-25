using System;
using System.Collections.Generic;
using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// A player's look (Profile > Edit character): their dash trail, the banner shown with their name when they score
    /// (in goal replays), and their goal explosion. Looks only: the game never reads them. They're packed into one int
    /// (<see cref="Pack"/>) to send online, so everyone sees everyone's.
    /// </summary>
    public static class Cosmetics
    {
        public static readonly string[] DashNames = { "Classic", "Comet", "Sparkle", "Bubbles", "Flames", "Lightning", "Rainbow", "Shadow", "Pixels" };
        public static readonly string[] BoomNames = { "Classic", "Fireworks", "Supernova", "Confetti", "Black Hole", "Thunder", "Bubbles", "Pixel Blast", "Starburst" };
        /// <summary>Banner colours: the team's, or one of these (the pictures with their own colours are shifted to it).</summary>
        public static readonly string[] BannerColourNames = { "Team", "Red", "Orange", "Gold", "Green", "Teal", "Sky", "Blue", "Purple", "Pink", "White", "Black" };
        private static readonly Color[] BannerColours =
        {
            Color.clear, new Color(0.88f, 0.2f, 0.22f), new Color(1f, 0.5f, 0.12f), new Color(1f, 0.78f, 0.2f), new Color(0.25f, 0.78f, 0.35f),
            new Color(0.1f, 0.72f, 0.7f), new Color(0.35f, 0.72f, 1f), new Color(0.2f, 0.4f, 0.95f), new Color(0.58f, 0.3f, 0.9f),
            new Color(1f, 0.42f, 0.72f), new Color(0.92f, 0.92f, 0.95f), new Color(0.12f, 0.12f, 0.14f),
        };

        /// <summary>Banner colour <paramref name="index"/> (0, Team: the team's colour, <paramref name="team"/>).</summary>
        public static Color BannerColour(int index, Color team) { return index <= 0 || index >= BannerColours.Length ? team : BannerColours[index]; }

        /// <summary>
        /// Titles, shown over a scorer's name on their banner and under it on the scoreboard. The last, Developer, is
        /// only for the game's developer's Steam account (<see cref="DeveloperAccount"/>): anyone else wearing it is shown none.
        /// </summary>
        public static readonly string[] TitleNames = { "None", "Legend", "Master", "Unstoppable", "Rat", "Destroyer", "Developer" };
        public const int DeveloperTitle = 6;
        public const ulong DeveloperAccount = 76561198089817775UL;

        /// <summary>The titles this PC's player can pick (Developer only on the developer's account).</summary>
        public static string[] TitleChoices
        {
            get
            {
                if (SteamNet.MyId == DeveloperAccount) return TitleNames;
                var some = new string[DeveloperTitle];
                Array.Copy(TitleNames, some, DeveloperTitle);
                return some;
            }
        }

        public static readonly string[] BannerNames = { "Plain", "Team Stripes", "Flames", "Waves", "Starfield", "Circuit", "Checkered", "Sunburst", "Storm", "Honeycomb", "Sunset" };

        // ------------------------------------------------------------------ this PC's own (the first person playing here)

        public static int MyDash { get { return ShotLook >= 0 ? Dash(ShotLook) : Clamp(PlayerPrefs.GetInt("look_dash", 0), DashNames); } set { PlayerPrefs.SetInt("look_dash", Clamp(value, DashNames)); } }
        public static int MyBanner { get { return ShotLook >= 0 ? Banner(ShotLook) : Clamp(PlayerPrefs.GetInt("look_banner", 0), BannerNames); } set { PlayerPrefs.SetInt("look_banner", Clamp(value, BannerNames)); } }
        public static int MyBannerColour { get { return ShotLook >= 0 ? BannerTint(ShotLook) : Clamp(PlayerPrefs.GetInt("look_banner_colour", 0), BannerColourNames); } set { PlayerPrefs.SetInt("look_banner_colour", Clamp(value, BannerColourNames)); } }
        public static int MyTitle
        {
            get { var t = ShotLook >= 0 ? Title(ShotLook) : Clamp(PlayerPrefs.GetInt("look_title", 0), TitleNames); return ShotLook < 0 && t == DeveloperTitle && SteamNet.MyId != DeveloperAccount ? 0 : t; }
            set { PlayerPrefs.SetInt("look_title", Clamp(value, TitleNames)); }
        }
        public static int MyBoom { get { return ShotLook >= 0 ? Boom(ShotLook) : Clamp(PlayerPrefs.GetInt("look_boom", 0), BoomNames); } set { PlayerPrefs.SetInt("look_boom", Clamp(value, BoomNames)); } }

        /// <summary>Screenshot runs: a look to wear instead of the saved one (-1: the saved one). Never saved.</summary>
        public static int ShotLook = -1;

        /// <summary>This PC's look, packed.</summary>
        public static int Mine { get { return Pack(MyDash, MyBanner, MyBoom, MyBannerColour, MyTitle); } }

        /// <summary>Bits 0-7 the dash, 8-15 the banner, 16-23 the explosion, 24-27 the banner colour, 28-30 the title.</summary>
        public static int Pack(int dash, int banner, int boom, int bannerColour = 0, int title = 0) { return (dash & 255) | ((banner & 255) << 8) | ((boom & 255) << 16) | ((bannerColour & 15) << 24) | ((title & 7) << 28); }
        /// <summary>The banner's colour (0: the team's).</summary>
        public static int BannerTint(int style) { return Clamp((style >> 24) & 15, BannerColourNames); }
        public static int Title(int style) { return Clamp((style >> 28) & 7, TitleNames); }
        /// <summary><paramref name="style"/> as it may be shown for Steam account <paramref name="account"/>: without the Developer title unless it's theirs.</summary>
        public static int Allowed(int style, ulong account) { return Title(style) == DeveloperTitle && account != DeveloperAccount ? style & ~(7 << 28) : style; }

        /// <summary>The Black Hole explosion: it pulls everyone in, so it goes with the controls (<see cref="Core.PlayerInput.LookPull"/>).</summary>
        public const int BlackHoleBoom = 4;

        /// <summary>
        /// A title in <paramref name="r"/>: white, and the Developer's in glowing green. <paramref name="size"/> is its
        /// font size; faded by <paramref name="a"/>.
        /// </summary>
        public static void DrawTitle(Rect r, int title, int size, TextAnchor align, float a)
        {
            if (title <= 0 || title >= TitleNames.Length || a <= 0f) return;
            var text = TitleNames[title].ToUpperInvariant();
            var font = Ui.Font(size, true, align);
            if (title != DeveloperTitle) { Ui.Label(r, text, font, new Color(1f, 1f, 1f, a)); return; }
            // a soft green glow behind, breathing a little, then the letters bright green over it
            var pulse = 0.8f + 0.2f * Mathf.Sin(Clock.Now * 3f);
            var glow = new Color(0.2f, 1f, 0.35f, 0.16f * pulse * a);
            var reach = Mathf.Max(1.5f, size * 0.16f);
            for (var k = 0; k < 12; k++)
            {
                var ang = k * Mathf.PI / 6f;
                var d = reach * (k % 2 == 0 ? 1f : 0.55f);
                Ui.Label(new Rect(r.x + Mathf.Cos(ang) * d, r.y + Mathf.Sin(ang) * d, r.width, r.height), text, font, glow, false);
            }
            Ui.Label(r, text, font, new Color(0.55f, 1f, 0.6f, a), false);
        }
        public static int Dash(int style) { return Clamp(style & 255, DashNames); }
        public static int Banner(int style) { return Clamp((style >> 8) & 255, BannerNames); }
        public static int Boom(int style) { return Clamp((style >> 16) & 255, BoomNames); }

        private static int Clamp(int v, string[] names) { return v < 0 || v >= names.Length ? 0 : v; }

        /// <summary>The colour of a dash's streaks and rim in each trail (white for the classic one; the team's for a few).</summary>
        public static Color DashTint(int dash, Color team)
        {
            switch (dash)
            {
                case 1: return Color.Lerp(team, Color.white, 0.35f);
                case 2: return new Color(1f, 0.88f, 0.45f);
                case 3: return new Color(0.75f, 0.95f, 1f);
                case 4: return new Color(1f, 0.62f, 0.2f);
                case 5: return new Color(0.55f, 0.8f, 1f);
                case 6: return Color.HSVToRGB(Mathf.Repeat(Clock.Now * 0.8f, 1f), 0.65f, 1f);
                case 7: return new Color(0.55f, 0.35f, 0.75f);
                case 8: return Color.Lerp(team, Color.white, 0.2f);
                default: return Color.white;
            }
        }
    }

    /// <summary>
    /// The particles behind the Profile looks: dash trails and goal explosions (the classic ones are the game's own
    /// <see cref="Effects"/>). Pooled; stepped with real time, so they look the same paused or in a replay.
    /// </summary>
    internal sealed class StyleFx
    {
        private enum Shape { Disc, Square, Ring, Star, Line }

        private sealed class Bit
        {
            public GameObject Go; public Mesh Mesh; public Shape Shape;
            public Vector2 Pos, Vel, Gravity;
            public float Age, Life, Size0, Size1, Drag, Spin, Angle, Grid, Stretch;
            public Color From, To;
            public bool Twinkle;
            /// <summary>Pulled into <see cref="Centre"/> this hard, swirling round it this fast (0: neither); gone on reaching it.</summary>
            public Vector2 Centre; public float Pull, Swirl;
        }

        private readonly Transform _parent;
        private readonly Effects _fx;
        private readonly List<Bit> _live = new List<Bit>();
        private readonly Dictionary<Shape, List<Bit>> _spare = new Dictionary<Shape, List<Bit>>();
        private struct Later { public float When; public Action Do; }
        private readonly List<Later> _later = new List<Later>();
        private const int MaxBits = 1600;

        /// <summary>Added to every particle's draw order (the dash preview draws over a dimmed screen).</summary>
        public int OrderBoost;

        public StyleFx(Transform parent, Effects fx) { _parent = parent; _fx = fx; }

        /// <summary>Drop everything on screen (a jump in a replay, leaving a screen).</summary>
        public void Clear()
        {
            foreach (var b in _live) { b.Go.SetActive(false); Spare(b.Shape).Add(b); }
            _live.Clear();
            _later.Clear();
            foreach (var l in _lenses) l.Go.SetActive(false);
            _spareLenses.AddRange(_lenses);
            _lenses.Clear();
        }

        private List<Bit> Spare(Shape s)
        {
            List<Bit> l;
            if (!_spare.TryGetValue(s, out l)) { l = new List<Bit>(); _spare[s] = l; }
            return l;
        }

        private Bit Take(Shape shape)
        {
            if (_live.Count >= MaxBits) return null;
            var spare = Spare(shape);
            Bit b;
            if (spare.Count > 0) { b = spare[spare.Count - 1]; spare.RemoveAt(spare.Count - 1); }
            else
            {
                b = new Bit { Shape = shape, Mesh = MeshOf(shape) };
                b.Go = Draw.Make("Style" + shape, _parent, b.Mesh, (shape == Shape.Line ? 24 : 25) + OrderBoost).gameObject;
            }
            b.Go.SetActive(true);
            b.Age = 0f; b.Gravity = Vector2.zero; b.Drag = 0f; b.Spin = 0f; b.Angle = 0f; b.Grid = 0f; b.Twinkle = false; b.Stretch = 1f; b.Pull = 0f; b.Swirl = 0f;
            _live.Add(b);
            return b;
        }

        private static Mesh MeshOf(Shape s)
        {
            switch (s)
            {
                case Shape.Square: return Draw.Rect(new Vector2(-0.5f, -0.5f), new Vector2(0.5f, 0.5f), Color.white);
                case Shape.Ring: return Draw.Ring(0.78f, 1f, 1f, Color.white, null, 20);
                case Shape.Line: return Draw.Rect(new Vector2(-0.5f, -0.06f), new Vector2(0.5f, 0.06f), Color.white);
                case Shape.Star:
                    var pts = new List<Vector2>();
                    for (var k = 0; k < 8; k++)
                    {
                        var a = k * Mathf.PI / 4f + Mathf.PI / 2f;
                        var r = k % 2 == 0 ? 1f : 0.32f;
                        pts.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                    }
                    return Draw.Polygon(pts, Color.white);
                default: return Draw.Disc(1f, Color.white, 14);
            }
        }

        private Bit Add(Shape shape, Vector2 at, Vector2 vel, float life, float size0, float size1, Color from, Color to)
        {
            var b = Take(shape);
            if (b == null) return null;
            b.Pos = at; b.Vel = vel; b.Life = Mathf.Max(0.02f, life); b.Size0 = size0; b.Size1 = size1; b.From = from; b.To = to;
            Step(b, 0f);
            return b;
        }

        private void After(float delay, Action a) { _later.Add(new Later { When = Clock.Now + delay, Do = a }); }

        public void Update()
        {
            var dt = Mathf.Min(Clock.Dt, 0.1f);
            for (var k = _later.Count - 1; k >= 0; k--)
            {
                if (Clock.Now < _later[k].When) continue;
                var a = _later[k].Do;
                _later.RemoveAt(k);
                a();
            }
            for (var i = _live.Count - 1; i >= 0; i--)
            {
                var b = _live[i];
                b.Age += dt;
                if (b.Age >= b.Life || (b.Pull > 0f && (b.Centre - b.Pos).sqrMagnitude < 0.04f)) { b.Go.SetActive(false); _live.RemoveAt(i); Spare(b.Shape).Add(b); continue; }
                Step(b, dt);
            }
            StepLenses(dt);
        }

        // ---- the Black Hole's lens: a quad that bends the picture round a dark middle (shader UltraPuck/Lens)
        private sealed class Lens { public GameObject Go; public MeshRenderer Renderer; public float Age, Life, Size; public Color Colour; }
        private readonly List<Lens> _lenses = new List<Lens>(), _spareLenses = new List<Lens>();
        private static Material _lensMat;
        private static Mesh _lensQuad;
        private readonly MaterialPropertyBlock _lensBlock = new MaterialPropertyBlock();

        private void AddLens(Vector2 at, float size, float life, Color colour)
        {
            if (_lensMat == null)
            {
                var shader = Resources.Load<Shader>("UltraPuck/Lens");
                if (shader == null || !shader.isSupported) return;
                _lensMat = new Material(shader);
            }
            if (_lensQuad == null)
            {
                _lensQuad = new Mesh { name = "Lens" };
                _lensQuad.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f) };
                _lensQuad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
                _lensQuad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                _lensQuad.RecalculateBounds();
            }
            Lens l;
            if (_spareLenses.Count > 0) { l = _spareLenses[_spareLenses.Count - 1]; _spareLenses.RemoveAt(_spareLenses.Count - 1); }
            else
            {
                l = new Lens { Go = new GameObject("BlackHole") };
                l.Go.transform.SetParent(_parent, false);
                l.Go.AddComponent<MeshFilter>().sharedMesh = _lensQuad;
                l.Renderer = l.Go.AddComponent<MeshRenderer>();
                l.Renderer.sharedMaterial = _lensMat;
                l.Renderer.sortingOrder = 27;
                l.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                l.Renderer.receiveShadows = false;
            }
            l.Go.SetActive(true);
            l.Go.transform.localPosition = new Vector3(at.x, at.y, 0f);
            l.Age = 0f; l.Life = life; l.Size = size; l.Colour = colour;
            _lenses.Add(l);
            StepLens(l);
        }

        private void StepLenses(float dt)
        {
            for (var k = _lenses.Count - 1; k >= 0; k--)
            {
                var l = _lenses[k];
                l.Age += dt;
                if (l.Age >= l.Life) { l.Go.SetActive(false); _lenses.RemoveAt(k); _spareLenses.Add(l); continue; }
                StepLens(l);
            }
        }

        private void StepLens(Lens l)
        {
            // opens quickly, holds (breathing a little), then snaps shut
            var t = l.Age / l.Life;
            var open = t < 0.15f ? Mathf.SmoothStep(0f, 1f, t / 0.15f) : t > 0.82f ? Mathf.SmoothStep(1f, 0f, (t - 0.82f) / 0.18f) : 1f;
            open *= 1f + 0.04f * Mathf.Sin(l.Age * 9f);
            var size = l.Size * Mathf.Max(0.001f, open);
            l.Go.transform.localScale = new Vector3(size, size, 1f);
            l.Go.transform.localRotation = Quaternion.Euler(0f, 0f, l.Age * 40f);
            _lensBlock.SetFloat("_Open", open);
            _lensBlock.SetColor("_Tint", l.Colour);
            l.Renderer.SetPropertyBlock(_lensBlock);
        }

        private static void Step(Bit b, float dt)
        {
            b.Vel += b.Gravity * dt;
            if (b.Pull > 0f)
            {
                // drawn in and round, faster the closer it gets (like the portals' motes, but falling in)
                var to = b.Centre - b.Pos;
                var d = Mathf.Max(0.3f, to.magnitude);
                var n = to / d;
                b.Vel += n * (b.Pull / d) * dt;
                b.Vel = Vector2.Lerp(b.Vel, n * b.Vel.magnitude * 0.6f + new Vector2(-n.y, n.x) * (b.Swirl / d), Mathf.Min(1f, 4f * dt));
            }
            if (b.Drag > 0f) b.Vel *= Mathf.Exp(-b.Drag * dt);
            b.Pos += b.Vel * dt;
            b.Angle += b.Spin * dt;
            var k = Mathf.Clamp01(b.Age / b.Life);
            var c = Color.Lerp(b.From, b.To, k);
            var fadeIn = Mathf.Clamp01(b.Age / 0.05f);
            c.a *= (1f - k * k) * fadeIn;
            if (b.Twinkle) c.a *= 0.55f + 0.45f * Mathf.Sin(b.Age * 30f + b.Size0 * 50f);
            Draw.Recolor(b.Mesh, c);
            var size = Mathf.Lerp(b.Size0, b.Size1, k);
            var p = b.Pos;
            if (b.Grid > 0f) p = new Vector2(Mathf.Round(p.x / b.Grid) * b.Grid, Mathf.Round(p.y / b.Grid) * b.Grid);
            b.Go.transform.localPosition = new Vector3(p.x, p.y, 0f);
            var turn = b.Shape == Shape.Line && b.Vel.sqrMagnitude > 1e-4f ? Mathf.Atan2(b.Vel.y, b.Vel.x) * Mathf.Rad2Deg : b.Angle;
            b.Go.transform.localRotation = Quaternion.Euler(0f, 0f, turn);
            b.Go.transform.localScale = new Vector3(size * b.Stretch, size, 1f);
        }

        private static Vector2 Rnd(float r) { return UnityEngine.Random.insideUnitCircle * r; }
        private static Vector2 Dir(float a) { return new Vector2(Mathf.Cos(a), Mathf.Sin(a)); }
        private static float R(float a, float b) { return UnityEngine.Random.Range(a, b); }

        // ------------------------------------------------------------------ dash trails

        /// <summary>
        /// A dashing player's trail, for this frame: <paramref name="dash"/> is how much of the dash is left (0..1),
        /// <paramref name="acc"/> the player's own running count (so the trail is as thick at any frame rate).
        /// </summary>
        public void DashTrail(int style, Vector2 pos, Vector2 dir, float radius, float dash, Color team, ref float acc)
        {
            if (style <= 0 || dash <= 0f) { acc = 0f; return; }
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.right;
            dir.Normalize();
            var side = new Vector2(-dir.y, dir.x);
            var back = pos - dir * radius * 0.9f;
            var rate = 110f * Mathf.Lerp(0.4f, 1f, dash);
            acc += Mathf.Min(Clock.Dt, 0.1f) * rate;
            while (acc >= 1f)
            {
                acc -= 1f;
                var at = back + side * R(-0.7f, 0.7f) * radius;
                switch (style)
                {
                    case 1: // Comet: a glowing tail in the team colour
                        Add(Shape.Disc, at, -dir * R(1f, 3f) + Rnd(0.5f), R(0.3f, 0.45f), radius * R(0.35f, 0.5f), 0f, Color.Lerp(team, Color.white, 0.5f), team);
                        break;
                    case 2: // Sparkle: twinkling gold and white stars
                        var s = Add(UnityEngine.Random.value < 0.35f ? Shape.Star : Shape.Disc, at + Rnd(radius * 0.4f), -dir * R(0.5f, 2f) + Rnd(1f), R(0.35f, 0.6f), radius * R(0.12f, 0.28f), 0f,
                            UnityEngine.Random.value < 0.5f ? Color.white : new Color(1f, 0.85f, 0.35f), new Color(1f, 0.7f, 0.2f));
                        if (s != null) { s.Twinkle = true; s.Spin = R(-200f, 200f); }
                        break;
                    case 3: // Bubbles: little rings that drift up and away
                        if (UnityEngine.Random.value < 0.5f) break;
                        var bu = Add(Shape.Ring, at, -dir * R(0.5f, 1.5f) + Rnd(0.4f), R(0.5f, 0.9f), radius * R(0.1f, 0.2f), radius * R(0.2f, 0.32f), new Color(0.85f, 0.97f, 1f, 0.85f), new Color(0.6f, 0.9f, 1f, 0.6f));
                        if (bu != null) bu.Gravity = new Vector2(0f, 1.6f);
                        break;
                    case 4: // Flames: yellow to orange to smoky red, rising a little
                        var f = Add(Shape.Disc, at, -dir * R(2f, 4f) + Rnd(0.8f), R(0.22f, 0.36f), radius * R(0.28f, 0.42f), radius * 0.05f, new Color(1f, 0.92f, 0.4f), new Color(0.85f, 0.15f, 0.05f, 0.6f));
                        if (f != null) { f.Gravity = new Vector2(0f, 2.5f); f.Drag = 2f; }
                        break;
                    case 5: // Lightning: blue sparks and the odd little bolt
                        var l = Add(Shape.Line, at, (-dir + Rnd(1f)).normalized * R(4f, 8f), R(0.08f, 0.16f), radius * R(0.4f, 0.7f), radius * 0.2f, new Color(0.85f, 0.95f, 1f), new Color(0.3f, 0.6f, 1f));
                        if (l != null) l.Drag = 6f;
                        if (UnityEngine.Random.value < 0.08f) _fx.SmallBolt(at, Mathf.Atan2(-dir.y, -dir.x) + R(-0.9f, 0.9f), radius * R(1.2f, 2f), new Color(0.4f, 0.7f, 1f));
                        break;
                    case 6: // Rainbow: a ribbon that runs through every colour
                        var hue = Mathf.Repeat(Clock.Now * 1.2f + R(0f, 0.08f), 1f);
                        var c = Color.HSVToRGB(hue, 0.75f, 1f);
                        Add(Shape.Disc, back + side * R(-0.35f, 0.35f) * radius, -dir * R(0.3f, 0.8f), R(0.4f, 0.55f), radius * 0.34f, radius * 0.2f, c, c);
                        break;
                    case 7: // Shadow: dark smoke that spreads as it fades
                        if (UnityEngine.Random.value < 0.4f) break;
                        var sm = Add(Shape.Disc, at, -dir * R(0.5f, 1.5f) + Rnd(0.6f), R(0.45f, 0.7f), radius * R(0.3f, 0.45f), radius * R(0.7f, 0.95f), new Color(0.22f, 0.1f, 0.3f, 0.75f), new Color(0.05f, 0.02f, 0.08f, 0.35f));
                        if (sm != null) sm.Drag = 3f;
                        break;
                    case 8: // Pixels: blocky squares in the team colour, snapped to a grid
                        if (UnityEngine.Random.value < 0.5f) break;
                        var px = Add(Shape.Square, at, -dir * R(1f, 2.5f), R(0.3f, 0.5f), radius * 0.32f, radius * 0.32f, UnityEngine.Random.value < 0.3f ? Color.white : team, Palette.Darker(team, 0.7f));
                        if (px != null) px.Grid = radius * 0.32f;
                        break;
                }
            }
        }

        // ------------------------------------------------------------------ goal explosions

        /// <summary>A goal goes off at <paramref name="at"/> for the scoring team (<paramref name="team"/>, its colour) in <paramref name="style"/>.</summary>
        public void Goal(int style, Vector2 at, Color team)
        {
            switch (style)
            {
                case 1: Fireworks(at, team); break;
                case 2: Supernova(at, team); break;
                case 3: Confetti(at, team); break;
                case 4: BlackHole(at, team); break;
                case 5: Thunder(at, team); break;
                case 6: BubbleBurst(at, team); break;
                case 7: PixelBlast(at, team); break;
                case 8: Starburst(at, team); break;
                default: _fx.Explosion(at, team); break;
            }
        }

        private static readonly Color Gold = new Color(1f, 0.82f, 0.3f), Pink = new Color(1f, 0.45f, 0.75f), Mint = new Color(0.45f, 1f, 0.7f), Sky = new Color(0.45f, 0.75f, 1f);

        private void Fireworks(Vector2 at, Color team)
        {
            Color[] colours = { team, Gold, Pink, Mint, Sky, Color.white };
            for (var n = 0; n < 6; n++)
            {
                var where = at + Rnd(3.5f);
                var col = colours[n % colours.Length];
                After(n * 0.2f, () =>
                {
                    // a rocket's pop: a ring of sparks that droop as they slow, crackling at the end
                    var count = 44;
                    var spin = R(0f, Mathf.PI * 2f);
                    for (var k = 0; k < count; k++)
                    {
                        var b = Add(Shape.Line, where, Dir(spin + k * Mathf.PI * 2f / count) * R(6f, 9f), R(0.7f, 1.1f), 0.5f, 0.15f, Color.white, col);
                        if (b != null) { b.Drag = 2.6f; b.Gravity = new Vector2(0f, -3f); }
                    }
                    for (var k = 0; k < 10; k++)
                    {
                        var s = Add(Shape.Disc, where + Rnd(2.2f), Vector2.zero, R(0.3f, 0.6f), 0.09f, 0.02f, Color.white, col);
                        if (s != null) s.Twinkle = true;
                    }
                    _fx.Ring(where, 0.6f, col, 1.4f, 0.3f);
                });
            }
        }

        private void Supernova(Vector2 at, Color team)
        {
            _fx.Ripple(at, Color.white, 15f, 1.6f, 0.08f);
            _fx.Ripple(at, team, 8f, 1f, 0.05f);
            Add(Shape.Disc, at, Vector2.zero, 0.7f, 0.5f, 7f, new Color(1f, 1f, 0.9f, 0.9f), Palette.Clear(Gold));
            Add(Shape.Ring, at, Vector2.zero, 0.8f, 0.6f, 9f, Color.white, team);
            Add(Shape.Ring, at, Vector2.zero, 1.1f, 0.3f, 6f, Gold, Palette.Clear(team));
            for (var k = 0; k < 70; k++)
            {
                var b = Add(Shape.Line, at, Dir(R(0f, 7f)) * R(8f, 20f), R(0.5f, 1f), R(0.6f, 1.2f), 0.1f, Color.white, UnityEngine.Random.value < 0.5f ? Gold : team);
                if (b != null) b.Drag = 3f;
            }
        }

        private void Confetti(Vector2 at, Color team)
        {
            Color[] colours = { team, Gold, Pink, Mint, Sky, Color.white, new Color(1f, 0.5f, 0.2f) };
            for (var k = 0; k < 150; k++)
            {
                var col = colours[UnityEngine.Random.Range(0, colours.Length)];
                var b = Add(Shape.Square, at, Dir(R(0f, 7f)) * R(5f, 17f), R(1.8f, 3f), R(0.16f, 0.26f), R(0.14f, 0.22f), col, col);
                if (b == null) continue;
                b.Drag = 3.2f; b.Gravity = new Vector2(R(-0.4f, 0.4f), -1.4f); b.Spin = R(-540f, 540f); b.Angle = R(0f, 360f); b.Stretch = R(0.45f, 0.8f);
            }
        }

        /// <summary>How long the Black Hole stays open.</summary>
        public const float BlackHoleTime = 1.7f;

        private void BlackHole(Vector2 at, Color team)
        {
            // a hole opens where the puck went in: the picture bends round its dark middle (a touch of colour split at
            // the rim), and glowing motes, the portals' kind, spiral in from all round, then it snaps shut with a pop
            var violet = new Color(0.62f, 0.42f, 1f);
            var rim = Color.Lerp(team, violet, 0.5f);
            AddLens(at, 3.4f, BlackHoleTime, rim);
            // the glow round it, breathing like a portal's
            Add(Shape.Disc, at, Vector2.zero, BlackHoleTime, 1.2f, 3.4f, new Color(rim.r, rim.g, rim.b, 0.28f), Palette.Clear(rim));
            Add(Shape.Ring, at, Vector2.zero, 0.5f, 0.4f, 2.4f, new Color(0.85f, 0.75f, 1f, 0.9f), Palette.Clear(rim));
            for (var n = 0; n < 14; n++)
            {
                After(n * 0.09f, () =>
                {
                    for (var k = 0; k < 9; k++)
                    {
                        var from = at + Dir(R(0f, 7f)) * R(3.2f, 7f);
                        var to = at - from;
                        var col = UnityEngine.Random.value < 0.3f ? Color.Lerp(rim, Color.white, 0.6f) : UnityEngine.Random.value < 0.5f ? team : violet;
                        var m = Add(Shape.Disc, from, new Vector2(-to.y, to.x).normalized * R(1f, 2.5f), R(1.1f, 1.6f), R(0.05f, 0.1f), 0.02f, col, Color.white);
                        if (m != null) { m.Centre = at; m.Pull = R(14f, 22f); m.Swirl = R(5f, 9f); }
                    }
                });
            }
            After(BlackHoleTime - 0.12f, () =>
            {
                // shut: a quick flash and a ring, sparks flung a little way out
                Add(Shape.Disc, at, Vector2.zero, 0.25f, 0.3f, 2f, new Color(0.95f, 0.9f, 1f, 0.9f), Palette.Clear(rim));
                Add(Shape.Ring, at, Vector2.zero, 0.5f, 0.4f, 5f, Color.white, Palette.Clear(rim));
                for (var k = 0; k < 30; k++)
                {
                    var b2 = Add(Shape.Line, at, Dir(R(0f, 7f)) * R(5f, 11f), R(0.3f, 0.6f), R(0.3f, 0.6f), 0.08f, new Color(0.85f, 0.75f, 1f), rim);
                    if (b2 != null) b2.Drag = 4f;
                }
            });
        }

        private void Thunder(Vector2 at, Color team)
        {
            Add(Shape.Disc, at, Vector2.zero, 0.25f, 1f, 6f, new Color(0.9f, 0.95f, 1f, 0.8f), Palette.Clear(Sky));
            for (var n = 0; n < 7; n++)
            {
                var where = at + (n == 0 ? Vector2.zero : Rnd(4f));
                var col = n % 2 == 0 ? team : Sky;
                After(n * 0.11f, () =>
                {
                    _fx.Lightning(where, 1f, col);
                    Add(Shape.Disc, where, Vector2.zero, 0.15f, 0.4f, 2.2f, new Color(1f, 1f, 1f, 0.85f), Palette.Clear(Sky));
                    _fx.Sparks(where, 0.6f, Sky, 10);
                });
            }
        }

        private void BubbleBurst(Vector2 at, Color team)
        {
            for (var k = 0; k < 90; k++)
            {
                var size = R(0.12f, 0.45f);
                var b = Add(Shape.Ring, at + Rnd(0.8f), Dir(R(0f, 7f)) * R(2f, 9f), R(1.2f, 2.4f), size * 0.4f, size, new Color(0.85f, 0.97f, 1f, 0.95f), Color.Lerp(team, new Color(0.6f, 0.9f, 1f), 0.6f));
                if (b != null) { b.Drag = 2.5f; b.Gravity = new Vector2(R(-0.3f, 0.3f), R(1.5f, 3f)); }
            }
        }

        private void PixelBlast(Vector2 at, Color team)
        {
            for (var k = 0; k < 110; k++)
            {
                var col = UnityEngine.Random.value < 0.25f ? Color.white : UnityEngine.Random.value < 0.5f ? team : Palette.Darker(team, 0.6f);
                var b = Add(Shape.Square, at, Dir(R(0f, 7f)) * R(3f, 12f), R(0.7f, 1.3f), 0.32f, 0.32f, col, col);
                if (b == null) continue;
                b.Drag = 2.2f; b.Grid = 0.32f;
            }
        }

        private void Starburst(Vector2 at, Color team)
        {
            var spin = R(0f, Mathf.PI * 2f);
            for (var k = 0; k < 16; k++)
            {
                var b = Add(Shape.Line, at, Dir(spin + k * Mathf.PI / 8f) * 14f, 0.55f, 3f, 0.5f, k % 2 == 0 ? Gold : Color.white, team);
                if (b != null) { b.Drag = 3.5f; b.Stretch = 1.4f; }
            }
            for (var k = 0; k < 40; k++)
            {
                var s = Add(Shape.Star, at, Dir(R(0f, 7f)) * R(4f, 11f), R(0.6f, 1.1f), R(0.2f, 0.4f), 0.05f, UnityEngine.Random.value < 0.5f ? Gold : Color.white, team);
                if (s != null) { s.Drag = 2.8f; s.Spin = R(-360f, 360f); s.Twinkle = true; }
            }
        }
    }

    /// <summary>
    /// Replay banners: a strip with a picture behind a scorer's name, shown while their goal is replayed. Every picture
    /// is drawn here in code (no image files), once, then slid along for a little movement.
    /// </summary>
    internal static class Banners
    {
        private static readonly Dictionary<long, Texture2D> _made = new Dictionary<long, Texture2D>();

        /// <summary>Styles drawn as one whole picture (the others repeat along the strip and scroll).</summary>
        private static bool Whole(int style) { return style == 7 || style == 10; }

        /// <summary>
        /// A banner: <paramref name="name"/> big, their <paramref name="title"/> small over it ("GOAL" without one), and
        /// <paramref name="below"/> under it (e.g. who assisted), in <paramref name="r"/>; faded by <paramref name="a"/>.
        /// </summary>
        public static void Draw(Rect r, int style, int tint, int team, string name, int title, string below, float a, int symbol = -1, Texture picture = null)
        {
            if (Event.current.type != EventType.Repaint) return;
            var colour = Cosmetics.BannerColour(tint, Palette.Team(team));
            var keep = GUI.color;
            // an edge round the picture in its colour
            Ui.Box(new Rect(r.x - 3f, r.y - 3f, r.width + 6f, r.height + 6f), new Color(colour.r, colour.g, colour.b, a), 6f);
            var tex = Texture(style, colour, tint > 0);
            GUI.color = new Color(1f, 1f, 1f, a);
            if (Whole(style)) GUI.DrawTexture(r, tex, ScaleMode.ScaleAndCrop);
            else
            {
                var across = r.width / r.height * tex.height / tex.width;
                var slide = Mathf.Repeat(Clock.Now * 0.05f, 1f);
                GUI.DrawTextureWithTexCoords(r, tex, new Rect(slide, 0f, across, 1f));
            }
            // a dark wash on the left so the name always reads
            GUI.color = new Color(0f, 0f, 0f, 0.45f * a);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * 0.75f, r.height), Fade);
            GUI.color = keep;
            var x = r.x + 18f;
            if (picture != null)
            {
                // their Steam picture, in a thin white frame
                var s = r.height * 0.72f;
                var pr = new Rect(x - 6f, r.center.y - s / 2f, s, s);
                Ui.Box(new Rect(pr.x - 1.5f, pr.y - 1.5f, s + 3f, s + 3f), new Color(1f, 1f, 1f, 0.9f * a), 3f);
                var was = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, a);
                GUI.DrawTexture(pr, picture, ScaleMode.ScaleAndCrop);
                GUI.color = was;
                x += s + 4f;
            }
            else if (symbol >= 0)
            {
                var s = r.height * 0.56f;
                Ui.Symbol(new Rect(x, r.center.y - s / 2f, s, s), symbol, new Color(1f, 1f, 1f, a));
                x += s + 14f;
            }
            var big = Mathf.RoundToInt(Mathf.Clamp(r.height * 0.36f, 16f, 60f));
            // their title over the name ("GOAL" without one)
            var over = new Rect(x, r.y + r.height * 0.06f, r.width - x + r.x, r.height * 0.26f);
            if (title > 0) Cosmetics.DrawTitle(new Rect(over.x, over.y - 2f, over.width, over.height + 2f), title, Mathf.RoundToInt(big * 0.52f), TextAnchor.MiddleLeft, a);
            else Ui.Label(over, "GOAL", Ui.Font(Mathf.RoundToInt(big * 0.42f), true), new Color(1f, 0.85f, 0.3f, a));
            Ui.Label(new Rect(x, r.y + r.height * 0.24f, r.width - x + r.x - 10f, r.height * 0.5f), name, Ui.Font(big, true), new Color(1f, 1f, 1f, a));
            if (!string.IsNullOrEmpty(below)) Ui.Label(new Rect(x, r.y + r.height * 0.72f, r.width - x + r.x, r.height * 0.24f), below, Ui.Font(Mathf.RoundToInt(big * 0.42f), true), new Color(1f, 1f, 1f, 0.85f * a));
        }

        private static Texture2D _fade;
        /// <summary>Solid on the left, clear on the right.</summary>
        private static Texture2D Fade
        {
            get
            {
                if (_fade != null) return _fade;
                _fade = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                for (var x = 0; x < 64; x++) _fade.SetPixel(x, 0, new Color(1f, 1f, 1f, 1f - x / 63f));
                _fade.Apply();
                return _fade;
            }
        }

        private static Texture2D Texture(int style, Color colour, bool chosen)
        {
            var key = (long)style * 100000000L + Mathf.RoundToInt(colour.r * 255f) * 65536 + Mathf.RoundToInt(colour.g * 255f) * 256 + Mathf.RoundToInt(colour.b * 255f) + (chosen ? 50000000L : 0L);
            Texture2D t;
            if (_made.TryGetValue(key, out t) && t != null) return t;
            var whole = Whole(style);
            int w = whole ? 512 : 256, h = whole ? 128 : 64;
            t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = whole ? TextureWrapMode.Clamp : TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[w * h];
            var team = colour;
            // pictures with their own colours: turned round to the chosen colour's hue (greys and black: no colour)
            float hue, sat, val;
            Color.RGBToHSV(colour, out hue, out sat, out val);
            var own = OwnHue(style);
            var rnd = new System.Random(style * 97 + 13);
            // a few things some pictures scatter about (stars, bolts)
            var dots = new List<Vector3>();
            for (var k = 0; k < 70; k++) dots.Add(new Vector3((float)rnd.NextDouble() * w, (float)rnd.NextDouble() * h, (float)rnd.NextDouble()));
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var u = x / (float)w; var v = y / (float)h;
                    var c = Pixel(style, x, y, u, v, w, h, team, dots);
                    if (chosen && own >= 0f)
                    {
                        float ch, cs, cv;
                        Color.RGBToHSV(c, out ch, out cs, out cv);
                        c = sat < 0.15f ? Color.HSVToRGB(0f, 0f, val < 0.3f ? cv * 0.5f : cv) : Color.HSVToRGB(Mathf.Repeat(ch - own + hue, 1f), cs, cv);
                    }
                    px[y * w + x] = c;
                }
            t.SetPixels32(px);
            t.Apply();
            _made[key] = t;
            return t;
        }

        /// <summary>The hue a picture with its own colours is built around (-1: it uses the banner colour already).</summary>
        private static float OwnHue(int style)
        {
            switch (style)
            {
                case 2: return 0.05f;  // flames
                case 3: return 0.58f;  // waves
                case 4: return 0.72f;  // starfield
                case 5: return 0.42f;  // circuit
                case 8: return 0.77f;  // storm
                case 10: return 0.06f; // sunset (built round its orange, so the sun stays sunny)
                default: return -1f;
            }
        }

        private static float Wave(float u, float k, float phase) { return Mathf.Sin((u * k + phase) * Mathf.PI * 2f); }

        private static Color Pixel(int style, int x, int y, float u, float v, int w, int h, Color team, List<Vector3> dots)
        {
            var dark = new Color(0.06f, 0.07f, 0.11f);
            switch (style)
            {
                case 1: // team stripes, slanting
                {
                    var s = Mathf.Repeat((x + y) / 32f, 1f);
                    var c = s < 0.5f ? team : Palette.Darker(team, 0.55f);
                    return Color.Lerp(c, Color.black, 0.15f + 0.35f * (1f - v));
                }
                case 2: // flames rising from the bottom
                {
                    var tongue = 0.35f + 0.18f * Wave(u, 5f, 0.1f) + 0.1f * Wave(u, 11f, 0.37f) + 0.06f * Wave(u, 23f, 0.71f);
                    var heat = Mathf.Clamp01((tongue + 0.25f - v) / 0.55f);
                    var c = heat > 0.8f ? new Color(1f, 0.95f, 0.55f) : heat > 0.45f ? Color.Lerp(new Color(1f, 0.45f, 0.1f), new Color(1f, 0.9f, 0.4f), (heat - 0.45f) / 0.35f) : Color.Lerp(new Color(0.12f, 0.02f, 0.02f), new Color(0.85f, 0.15f, 0.05f), heat / 0.45f);
                    return c;
                }
                case 3: // waves
                {
                    var c = Color.Lerp(new Color(0.02f, 0.15f, 0.35f), new Color(0.1f, 0.45f, 0.75f), v);
                    for (var k = 0; k < 3; k++)
                    {
                        var line = 0.25f + k * 0.25f + 0.07f * Wave(u, 2f + k, k * 0.3f);
                        var d = Mathf.Abs(v - line);
                        if (d < 0.05f) c = Color.Lerp(c, new Color(0.7f, 0.92f, 1f), 1f - d / 0.05f);
                    }
                    return c;
                }
                case 4: // stars
                {
                    var c = Color.Lerp(new Color(0.02f, 0.01f, 0.08f), new Color(0.15f, 0.06f, 0.3f), v);
                    foreach (var d in dots)
                    {
                        var dx = Mathf.Abs(x - d.x); dx = Mathf.Min(dx, w - dx);
                        var dist = Mathf.Sqrt(dx * dx + (y - d.y) * (y - d.y));
                        var size = 0.6f + d.z * 1.8f;
                        if (dist < size + 1f) c = Color.Lerp(c, Color.white, Mathf.Clamp01(size + 1f - dist));
                    }
                    return c;
                }
                case 5: // circuit
                {
                    var c = Color.Lerp(new Color(0.01f, 0.08f, 0.05f), new Color(0.02f, 0.16f, 0.1f), v);
                    var gx = x % 32; var gy = y % 16;
                    var trace = (gy == 8 && (x / 32) % 2 == 0) || (gx == 16 && (y / 16) % 2 == 1) || (gy == 3 && gx > 20);
                    if (trace) c = new Color(0.2f, 0.95f, 0.55f);
                    var pad = (gx - 16) * (gx - 16) + (gy - 8) * (gy - 8);
                    if (pad < 7) c = new Color(0.6f, 1f, 0.8f);
                    return c;
                }
                case 6: // checkered
                {
                    var check = ((x / 16) + (y / 16)) % 2 == 0;
                    var c = check ? new Color(0.92f, 0.92f, 0.92f) : new Color(0.08f, 0.08f, 0.08f);
                    var sheen = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Repeat(u * 2f + v * 0.3f, 1f) - 0.5f) * 4f);
                    return Color.Lerp(Color.Lerp(c, team, 0.25f), Color.white, sheen * 0.15f);
                }
                case 7: // sunburst from the right
                {
                    var dx = (u - 0.85f) * w / h; var dy = v - 0.5f;
                    var ang = Mathf.Atan2(dy, dx);
                    var ray = Mathf.Repeat(ang / (Mathf.PI * 2f) * 24f, 1f) < 0.5f;
                    var c = ray ? team : Color.Lerp(team, Color.white, 0.35f);
                    var dist = Mathf.Sqrt(dx * dx + dy * dy);
                    return Color.Lerp(c, Palette.Darker(team, 0.4f), Mathf.Clamp01(dist / 3.5f));
                }
                case 8: // storm: dark purple with bolts
                {
                    var c = Color.Lerp(new Color(0.05f, 0.02f, 0.1f), new Color(0.2f, 0.08f, 0.32f), v);
                    for (var k = 0; k < 4; k++)
                    {
                        var bx = (k + 0.5f) * w / 4f + 14f * Mathf.Sign(Wave(v, 3f, k * 0.2f)) * Mathf.Abs(Wave(v, 3f, k * 0.2f));
                        var d = Mathf.Abs(x - bx);
                        if (d < 1.6f) c = Color.Lerp(c, Color.white, 1f - d / 1.6f);
                        else if (d < 6f) c = Color.Lerp(c, new Color(0.6f, 0.45f, 1f), (6f - d) / 6f * 0.5f);
                    }
                    return c;
                }
                case 9: // honeycomb
                {
                    const float s = 10f;
                    var qy = y / (s * 1.5f);
                    var row = Mathf.FloorToInt(qy);
                    var qx = (x + (row % 2) * s * 0.866f) / (s * 1.732f);
                    var cx = Mathf.Repeat(qx, 1f) - 0.5f; var cy = Mathf.Repeat(qy, 1f) - 0.5f;
                    var edge = Mathf.Max(Mathf.Abs(cx) * 2f, Mathf.Abs(cy) * 2f);
                    var c = Color.Lerp(Palette.Darker(team, 0.35f), Palette.Darker(team, 0.6f), v);
                    return edge > 0.86f ? Color.Lerp(team, Color.white, 0.3f) : c;
                }
                case 10: // sunset
                {
                    var c = v < 0.5f ? Color.Lerp(new Color(1f, 0.55f, 0.15f), new Color(1f, 0.35f, 0.45f), v * 2f) : Color.Lerp(new Color(1f, 0.35f, 0.45f), new Color(0.3f, 0.1f, 0.4f), (v - 0.5f) * 2f);
                    var dx = (u - 0.78f) * w / h; var dy = v - 0.38f;
                    var dist = Mathf.Sqrt(dx * dx + dy * dy);
                    if (dist < 0.28f && Mathf.Repeat(v * 14f, 1f) > 0.25f * (1f - v * 2f)) c = Color.Lerp(new Color(1f, 0.95f, 0.6f), new Color(1f, 0.6f, 0.3f), dist / 0.28f);
                    if (v < 0.18f) c = Color.Lerp(c, new Color(0.15f, 0.05f, 0.2f), 0.8f);
                    return c;
                }
                default: // plain: the colour, deeper and lighter in soft bands, with a gentle sheen along the top
                {
                    var across = 0.5f + 0.5f * Mathf.Sin(u * Mathf.PI * 2f);
                    var c = Color.Lerp(Palette.Darker(team, 0.4f), Palette.Darker(team, 0.8f), across * 0.5f + v * 0.5f);
                    return Color.Lerp(c, Color.white, 0.1f * Mathf.SmoothStep(0.55f, 1f, v));
                }
            }
        }
    }
}
