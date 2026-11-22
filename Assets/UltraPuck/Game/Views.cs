using System.Collections.Generic;
using UltraPuck.Core;
using UnityEngine;

namespace UltraPuck.Game
{
    internal static class V
    {
        public static Vector2 Of(FixVec2 v) { return new Vector2(v.X.ToFloat(), v.Y.ToFloat()); }
        public static Vector2 Of3(Vector3 v) { return new Vector2(v.x, v.y); }
    }

    /// <summary>The rink: floor, markings, goals and walls, built once from the map.</summary>
    internal sealed class RinkView
    {
        public readonly GameObject Root;

        public RinkView(MapDef map, float wallThickness)
        {
            Root = new GameObject("Rink");
            var outline = new List<Vector2>();
            foreach (var p in map.WallLines[0]) outline.Add(V.Of(p));
            Draw.Make("Floor", Root.transform, Draw.Polygon(outline, Palette.Floor), 0);

            var min = V.Of(map.BoundsMin); var max = V.Of(map.BoundsMax);
            var size = max - min;
            var cx = (min.x + max.x) / 2f;

            // markings: the circle round the puck's spot; on a straight rink also a centre line and a line across each
            // half (a bent rink, one with a route, has no "across", so no lines)
            Draw.Make("CentreCircle", Root.transform, Draw.Ring(2.4f, 2.54f, 1f, Palette.FloorLines), 2).transform.localPosition = V.Of(map.PuckSpot);
            Draw.Make("CentreDot", Root.transform, Draw.Disc(0.25f, Palette.FloorLines), 2).transform.localPosition = V.Of(map.PuckSpot);
            if (map.Route.Count < 2)
            {
                AcrossLine(map, cx, min.y, max.y, Palette.FloorLines, "CentreLine");
                // each half's line in a dark shade of the team that defends that end (the centre line stays grey)
                foreach (var side in new[] { -1f, 1f })
                {
                    var team = side < 0f ? 0 : 1;
                    foreach (var g in map.Goals) if ((V.Of(g.Min).x + V.Of(g.Max).x) / 2f < cx == side < 0f) { team = g.Team; break; }
                    AcrossLine(map, cx + side * size.x * 0.19f, min.y, max.y, Palette.Darker(Palette.Team(team), 0.3f), "SideLine");
                }
            }

            // goals in a dark shade of the colour of the team that defends them
            foreach (var g in map.Goals)
                Draw.Make("Goal" + g.Team, Root.transform, Draw.Rect(V.Of(g.Min), V.Of(g.Max), Palette.Goal(g.Team)), 3);

            for (var w = 0; w < map.WallLines.Count; w++)
            {
                var pts = new List<Vector2>();
                foreach (var p in map.WallLines[w]) pts.Add(V.Of(p));
                Draw.Make("Wall" + w, Root.transform, Draw.Line(pts, map.WallClosed[w], wallThickness, Palette.Wall), 4);
            }
        }

        /// <summary>A line across the rink at <paramref name="x"/>, only where there's floor (not through gaps in the shape).</summary>
        private void AcrossLine(MapDef map, float x, float y0, float y1, Color colour, string name)
        {
            const float step = 0.25f;
            float? from = null;
            for (var y = y0; y <= y1 + step; y += step)
            {
                var inside = y <= y1 && map.Inside(new FixVec2(Fix.FromDecimal((decimal)x), Fix.FromDecimal((decimal)y)));
                if (inside && from == null) from = y;
                if (!inside && from != null)
                {
                    Draw.Make(name, Root.transform, Draw.Rect(new Vector2(x - 0.07f, from.Value), new Vector2(x + 0.07f, y - step), colour), 2);
                    from = null;
                }
            }
        }
    }

    /// <summary>
    /// A player: team-coloured body with the team's symbol, a facing arrow, a dash bar above them (like the mod's)
    /// and a ring for the shot charge. Hits play a squash-and-flash animation.
    /// </summary>
    internal sealed class PlayerView
    {
        public readonly GameObject Root;
        private readonly Transform _body, _squash, _unsquash;
        private readonly MeshFilter _chargeRing, _overRing;
        private readonly Transform _chargeRoot;

        // speed trail: fading copies of the body left behind while going faster than skating speed (a dash, a boost)
        private const int TrailLength = 5;
        private const float TrailStep = 1f / 60f, TrailFrom = 9.5f, TrailFull = 15f;
        private readonly Transform[] _trail = new Transform[TrailLength];
        private readonly Mesh[] _trailMesh = new Mesh[TrailLength];
        private readonly Vector2[] _trailAt = new Vector2[TrailLength];
        private int _trailCount;
        private float _trailClock, _trailAlpha;
        private Vector2 _lastPos;
        private bool _hasLast;
        private readonly Color _trailColour;
        private readonly Mesh _chargeMesh = new Mesh(), _overMesh = new Mesh();
        /// <summary>The charge meter's colours: a normal charge, then each +100% of Over Charge past full.</summary>
        private static readonly Color[] ChargeColours = { Palette.Charge, new Color(1f, 0.55f, 0.1f), new Color(0.25f, 0.6f, 1f), new Color(0.75f, 0.4f, 1f), Color.white };
        private float _chargeShown = -1f;
        private readonly float _r;

        // check bar: one segment per stored check (like the mod's), a dark track with a white fill, shown while
        // recharging (or always, by setting); a segment pulses when it fills
        private const float BarLength = 1.0f, BarThickness = 0.17f, SegGap = 0.08f;
        private readonly Transform _barRoot;
        private readonly List<Mesh> _segBack = new List<Mesh>(), _segFill = new List<Mesh>();
        private readonly List<Transform> _segFillT = new List<Transform>();
        private float[] _segPulse = new float[0], _segLast = new float[0];
        private float _barAlpha;

        // card effects: slowed / boosted rings, a faint outline while invisible (to teammates), small cooldown bars
        private readonly GameObject _boostRing, _ghost, _ghostWedge, _shadow;
        // with the puck: a short line out of the wedge showing which way a shot would go (length from Settings.AimLine)
        private readonly Transform _aimRoot;
        private readonly float _aimFull;

        // slowed: a few blue down arrows drifting down over the player, on a loop
        private readonly Transform _slowRoot;
        private readonly Transform[] _slowArrows = new Transform[3];
        private readonly Mesh[] _slowMesh = new Mesh[3], _slowOutline = new Mesh[3];
        private float _slowAlpha;
        // a Shockwave being held: a see-through blue disc at the size it'll go off at
        private GameObject _shockRing;
        // sped up: the same arrows, pointing up, white, drifting up
        private readonly Transform _fastRoot;
        private readonly Transform[] _fastArrows = new Transform[3];
        private readonly Mesh[] _fastMesh = new Mesh[3], _fastOutline = new Mesh[3];
        private float _fastAlpha;

        // stunned: little stars circling the player
        private readonly Transform _stunRoot;
        private readonly Mesh[] _stunMesh = new Mesh[3];
        private float _stunAlpha;

        // dashing: speed streaks behind and a bright rim, fading as the dash ends
        private readonly Transform _dashRoot;
        private readonly Mesh[] _dashMesh = new Mesh[4];
        private readonly Transform[] _streak = new Transform[3];
        private float _dashShown = -1f;
        private readonly Transform _cdRoot;
        private readonly Mesh[] _cdBack = new Mesh[3], _cdFill = new Mesh[3];
        private readonly Transform[] _cdFillT = new Transform[3];
        private static readonly Color[] CooldownColours = { new Color(1f, 0.42f, 0.37f), new Color(0.65f, 0.79f, 1f), new Color(0.35f, 0.65f, 1f) };

        // hit animation
        private readonly Mesh _flashMesh;
        private readonly GameObject _flash;
        private int _hitsSeen = -1;
        private float _hitAge = 10f, _hitPower, _hitAngle;
        private const float HitTime = 0.32f;

        public PlayerView(int team, float radius, Transform parent, int symbol)
        {
            _r = radius;
            var c = Palette.Team(team);
            Root = new GameObject("Player");
            Root.transform.SetParent(parent, false);
            // a drop shadow that doesn't turn with the player, so the light always comes from the same side
            _shadow = Draw.Make("Shadow", Root.transform, Draw.Disc(radius * 1.04f, Palette.Shadow), 7).gameObject;
            _shadow.transform.localPosition = new Vector3(radius * 0.18f, -radius * 0.32f, 0f);
            _boostRing = Draw.Make("Boosted", Root.transform, Draw.Ring(radius * 1.55f, radius * 1.72f, 1f, new Color(1f, 0.62f, 0.3f, 0.85f)), 9).gameObject;
            _shockRing = Draw.Make("ShockCharge", Root.transform, Draw.Disc(1f, new Color(0.3f, 0.6f, 1f, 0.22f), 72), 6).gameObject;
            _shockRing.SetActive(false);

            _slowRoot = new GameObject("Slowed").transform;
            _slowRoot.SetParent(Root.transform, false);
            for (var k = 0; k < 3; k++)
            {
                // a down arrow: a stem and a head
                var w = radius * 0.22f;
                var pts = new List<Vector2>
                {
                    new Vector2(-w * 0.45f, w * 1.4f), new Vector2(w * 0.45f, w * 1.4f), new Vector2(w * 0.45f, 0f), new Vector2(w * 1.1f, 0f),
                    new Vector2(0f, -w * 1.1f), new Vector2(-w * 1.1f, 0f), new Vector2(-w * 0.45f, 0f),
                };
                _slowMesh[k] = Draw.Polygon(pts, new Color(0.45f, 0.75f, 1f, 0f));
                _slowArrows[k] = Draw.Make("Arrow" + k, _slowRoot, _slowMesh[k], 25).transform;
                // a black outline behind it, so it reads on any colour: the same arrow, a little bigger
                var centre = new Vector2(0f, w * 0.15f);
                var outline = new List<Vector2>();
                foreach (var p in pts) outline.Add(centre + (p - centre) * 1.45f);
                _slowOutline[k] = Draw.Polygon(outline, new Color(0f, 0f, 0f, 0f));
                Draw.Make("Outline", _slowArrows[k], _slowOutline[k], 24);
            }
            _slowRoot.gameObject.SetActive(false);

            _fastRoot = new GameObject("SpedUp").transform;
            _fastRoot.SetParent(Root.transform, false);
            for (var k = 0; k < 3; k++)
            {
                // an up arrow: the slow arrow turned over, a bit bigger so it stands out
                var w = radius * 0.3f;
                var pts = new List<Vector2>
                {
                    new Vector2(-w * 0.45f, -w * 1.4f), new Vector2(-w * 0.45f, 0f), new Vector2(-w * 1.1f, 0f), new Vector2(0f, w * 1.1f),
                    new Vector2(w * 1.1f, 0f), new Vector2(w * 0.45f, 0f), new Vector2(w * 0.45f, -w * 1.4f),
                };
                _fastMesh[k] = Draw.Polygon(pts, new Color(1f, 1f, 1f, 0f));
                _fastArrows[k] = Draw.Make("Arrow" + k, _fastRoot, _fastMesh[k], 25).transform;
                var centre = new Vector2(0f, -w * 0.15f);
                var outline = new List<Vector2>();
                foreach (var p in pts) outline.Add(centre + (p - centre) * 1.3f);
                _fastOutline[k] = Draw.Polygon(outline, new Color(0f, 0f, 0f, 0f));
                Draw.Make("Edge", _fastArrows[k], _fastOutline[k], 24);
            }
            _fastRoot.gameObject.SetActive(false);

            _stunRoot = new GameObject("Stunned").transform;
            _stunRoot.SetParent(Root.transform, false);
            for (var k = 0; k < 3; k++)
            {
                var star = new List<Vector2>();
                for (var j = 0; j < 10; j++)
                {
                    var a = Mathf.PI / 2f + j * Mathf.PI / 5f;
                    var rr = (j % 2 == 0 ? 1f : 0.45f) * radius * 0.26f;
                    star.Add(new Vector2(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr));
                }
                _stunMesh[k] = Draw.Polygon(star, new Color(1f, 0.9f, 0.35f, 0f));
                var t = Draw.Make("Star" + k, _stunRoot, _stunMesh[k], 25).transform;
                var ang = k * Mathf.PI * 2f / 3f;
                t.localPosition = new Vector3(Mathf.Cos(ang) * radius * 1.2f, Mathf.Sin(ang) * radius * 1.2f, 0f);
            }
            _stunRoot.gameObject.SetActive(false);

            _dashRoot = new GameObject("Dashing").transform;
            _dashRoot.SetParent(Root.transform, false);
            for (var k = 0; k < 3; k++)
            {
                var y = (k - 1) * radius * 0.55f;
                var len = k == 1 ? radius * 1.6f : radius * 1.1f;
                // each streak hangs off the back of the player, so it can be drawn growing outwards from there
                _streak[k] = new GameObject("Streak" + k).transform;
                _streak[k].SetParent(_dashRoot, false);
                _streak[k].localPosition = new Vector3(-radius * 1.15f, y, 0f);
                _dashMesh[k] = Draw.Rect(new Vector2(-len, -radius * 0.06f), new Vector2(0f, radius * 0.06f), Color.clear);
                Draw.Make("Line", _streak[k], _dashMesh[k], 9);
            }
            _dashMesh[3] = Draw.Ring(radius * 1.02f, radius * 1.18f, 1f, Color.clear);
            Draw.Make("DashRim", _dashRoot, _dashMesh[3], 12);
            _dashRoot.gameObject.SetActive(false);
            var tint = Palette.TeamTint(team); tint.a = 0.45f;
            _ghost = Draw.Make("Ghost", Root.transform, Draw.Ring(radius * 0.86f, radius, 1f, tint), 10).gameObject;
            _boostRing.SetActive(false); _ghost.SetActive(false);

            // squash: stretched along the hit direction, then turned back so the body still faces its own way
            _squash = new GameObject("Squash").transform;
            _squash.SetParent(Root.transform, false);
            _unsquash = new GameObject("Unsquash").transform;
            _unsquash.SetParent(_squash, false);
            _body = new GameObject("Body").transform;
            _body.SetParent(_unsquash, false);
            Draw.Make("Rim", _body, Draw.Disc(radius, Palette.PlayerRim), 10);
            Draw.Make("Fill", _body, Draw.Disc(radius * 0.86f, c), 11);
            // facing marker: a big arrowhead at the front in a light team tint, easy to read on the dark rink
            Draw.Make("Front", _body, Draw.Triangle(new Vector2(radius * 1.8f, 0f), new Vector2(radius * 0.6f, radius * 0.7f), new Vector2(radius * 0.6f, -radius * 0.7f), Palette.TeamTint(team)), 8);
            _flashMesh = Draw.Disc(radius, Color.clear);
            _flash = Draw.Make("Flash", _unsquash, _flashMesh, 12).gameObject;
            _flash.SetActive(false);
            // the player's own symbol (doesn't turn with the player, so it always reads the same)
            Draw.Make("Symbol", _unsquash, Symbols.Mesh(symbol, radius * 0.95f, Color.white), 13);
            // the charge bar turns with the player: it starts behind them and wraps round both sides to the front
            _chargeRoot = new GameObject("ChargeBar").transform;
            _chargeRoot.SetParent(Root.transform, false);
            _chargeRing = Draw.Make("Charge", _chargeRoot, _chargeMesh, 14);
            _overRing = Draw.Make("OverCharge", _chargeRoot, _overMesh, 14);
            // invisible (seen by your own team, you included): an outline of the wedge, turning with you
            var wedge = new List<Vector2> { new Vector2(radius * 1.8f, 0f), new Vector2(radius * 0.6f, radius * 0.7f), new Vector2(radius * 0.6f, -radius * 0.7f) };
            var ghostTint = Palette.TeamTint(team); ghostTint.a = 0.7f;
            _ghostWedge = Draw.Make("GhostWedge", _chargeRoot, Draw.Line(wedge, true, radius * 0.12f, ghostTint), 10).gameObject;
            _ghostWedge.SetActive(false);
            // shot aim: from the tip of the wedge outwards; scale.x follows Settings.AimLine (1 = full original length)
            _aimFull = radius * 2.8f;
            var tip = radius * 1.8f;
            var aimTint = Palette.TeamTint(team); aimTint.a = 0.85f;
            _aimRoot = new GameObject("Aim").transform;
            _aimRoot.SetParent(_chargeRoot, false);
            _aimRoot.localPosition = new Vector3(tip, 0f, 0f);
            Draw.Make("Line", _aimRoot, Draw.Line(new List<Vector2> { Vector2.zero, new Vector2(_aimFull, 0f) }, false, radius * 0.12f, aimTint), 15);
            _aimRoot.gameObject.SetActive(false);

            _trailColour = Palette.TeamTint(team);
            for (var i = 0; i < TrailLength; i++)
            {
                _trailMesh[i] = Draw.Disc(radius * (0.95f - i * 0.12f), Color.clear);
                _trail[i] = Draw.Make("Trail" + i, Root.transform, _trailMesh[i], 6).transform;
                _trail[i].gameObject.SetActive(false);
            }

            _barRoot = new GameObject("DashBar").transform;
            _barRoot.SetParent(Root.transform, false);
            _barRoot.localPosition = new Vector3(0f, radius + 0.42f, 0f);
            _barRoot.gameObject.SetActive(false);

            _cdRoot = new GameObject("Cooldowns").transform;
            _cdRoot.SetParent(Root.transform, false);
            _cdRoot.localPosition = new Vector3(0f, radius + 0.24f, 0f);
            for (var k = 0; k < 3; k++)
            {
                const float h = 0.08f;
                var y = -k * 0.13f;
                _cdBack[k] = Draw.Rect(new Vector2(-BarLength / 2f, y - h / 2f), new Vector2(BarLength / 2f, y + h / 2f), new Color(0.08f, 0.08f, 0.1f, 0.75f));
                Draw.Make("Track" + k, _cdRoot, _cdBack[k], 15);
                _cdFill[k] = Draw.Rect(new Vector2(0f, -0.5f), new Vector2(1f, 0.5f), CooldownColours[k]);
                _cdFillT[k] = Draw.Make("Fill" + k, _cdRoot, _cdFill[k], 16).transform;
                _cdFillT[k].localPosition = new Vector3(-BarLength / 2f, y, 0f);
                _cdFillT[k].gameObject.SetActive(false);
                _cdRoot.Find("Track" + k).gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Draw this frame. Returns the power (0..1) of a hit that just happened, or 0, so the caller can add an
        /// impact ring and screen shake; <paramref name="hitAt"/> is where it happened.
        /// </summary>
        public float Show(Vector2 pos, Vector2 facing, float dashCharge, int dashMax, float charge, int hits, float hitPower, Vector2 hitDir, out Vector2 hitAt)
        {
            Root.transform.localPosition = new Vector3(pos.x, pos.y, 0f);
            if (facing.sqrMagnitude > 1e-6f)
            {
                var turn = Quaternion.Euler(0f, 0f, Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg);
                _body.localRotation = turn;
                _chargeRoot.localRotation = turn;
            }

            charge = Mathf.Round(Mathf.Clamp(charge, 0f, ChargeColours.Length - 1f) * 48f) / 48f;
            if (charge != _chargeShown)
            {
                _chargeShown = charge;
                _chargeRing.gameObject.SetActive(charge > 0f);
                // centred behind the player (local angle 180 degrees), growing round both sides until it meets at the front.
                // Past full (Over Charge) the ring changes colour the same way: a new colour grows from the back over the
                // old one, orange for the first +100%, then blue.
                var level = charge <= 1f ? 0 : Mathf.Min(Mathf.CeilToInt(charge - 1f), ChargeColours.Length - 1);
                var fill = level == 0 ? charge : charge - level;
                _overRing.gameObject.SetActive(level > 0 && fill < 1f);
                if (level == 0) { if (charge > 0f) Draw.Arc(_r * 1.3f, _r * 1.5f, Mathf.PI, charge * Mathf.PI, ChargeColours[0], _chargeMesh); }
                else
                {
                    Draw.Arc(_r * 1.3f, _r * 1.5f, Mathf.PI, fill * Mathf.PI, ChargeColours[level], _chargeMesh);
                    if (fill < 1f) Draw.Arc(_r * 1.3f, _r * 1.5f, 0f, (1f - fill) * Mathf.PI, ChargeColours[level - 1], _overMesh);
                }
            }

            SpeedTrail(pos);

            DashBar(Mathf.Clamp(dashCharge, 0f, dashMax), Mathf.Max(1, dashMax));

            // a new hit starts the animation
            var fresh = 0f;
            hitAt = pos;
            if (_hitsSeen >= 0 && hits > _hitsSeen)
            {
                _hitAge = 0f;
                _hitPower = Mathf.Clamp01(hitPower);
                _hitAngle = hitDir.sqrMagnitude > 1e-6f ? Mathf.Atan2(hitDir.y, hitDir.x) * Mathf.Rad2Deg : 0f;
                fresh = Mathf.Max(0.05f, _hitPower);
                hitAt = pos + hitDir.normalized * _r;
            }
            _hitsSeen = hits;
            HitAnimation();
            return fresh;
        }

        private void SpeedTrail(Vector2 pos)
        {
            var dt = Time.deltaTime;
            var speed = _hasLast && dt > 1e-5f ? (pos - _lastPos).magnitude / dt : 0f;
            // a jump (face-off, respawn) isn't speed
            if (_hasLast && (pos - _lastPos).sqrMagnitude > 4f) { speed = 0f; _trailCount = 0; }
            _lastPos = pos; _hasLast = true;
            var want = Mathf.Clamp01((speed - TrailFrom) / (TrailFull - TrailFrom));
            _trailAlpha = Mathf.MoveTowards(_trailAlpha, want, dt * (want > _trailAlpha ? 12f : 4f));

            _trailClock += dt;
            if (_trailClock >= TrailStep)
            {
                _trailClock = 0f;
                for (var i = TrailLength - 1; i > 0; i--) _trailAt[i] = _trailAt[i - 1];
                _trailAt[0] = pos;
                _trailCount = Mathf.Min(TrailLength, _trailCount + 1);
            }
            for (var i = 0; i < TrailLength; i++)
            {
                var on = _trailAlpha > 0.01f && i < _trailCount && (_trailAt[i] - pos).sqrMagnitude > _r * _r * 0.2f;
                if (_trail[i].gameObject.activeSelf != on) _trail[i].gameObject.SetActive(on);
                if (!on) continue;
                _trail[i].position = new Vector3(_trailAt[i].x, _trailAt[i].y, 0f);
                var c = _trailColour;
                c.a = 0.4f * _trailAlpha * (1f - (i + 1f) / (TrailLength + 1f));
                Draw.Recolor(_trailMesh[i], c);
            }
        }

        /// <summary>
        /// Card effects on this player. <paramref name="hidden"/>: invisible and the viewer is on the other team (not
        /// drawn at all); <paramref name="faint"/>: invisible but the viewer is a teammate (an outline only).
        /// <paramref name="cooldowns"/>: Puck Gun, Invisibility and Shockwave readiness (0..1, or -1 without the card).
        /// </summary>
        /// <param name="stunned">Dazed (a check or a Puck Gun hit): stars circle them.</param>
        /// <param name="dash">How much of a dash is left (0..1, 0 = not dashing): streaks behind and a bright rim.</param>
        /// <param name="lagged">Frozen by hit lag: the body shakes.</param>
        /// <param name="aiming">Carrying the puck: show the short aim line out of the wedge.</param>
        /// <summary>A Shockwave being held: show the ring it'll go off at (0 hides it).</summary>
        public void ShockCharge(float radius)
        {
            var on = radius > 0f;
            if (_shockRing.activeSelf != on) _shockRing.SetActive(on);
            if (on) _shockRing.transform.localScale = new Vector3(radius, radius, 1f);
        }

        public void Status(bool hidden, bool faint, bool slowed, bool boosted, float[] cooldowns, bool stunned = false, float dash = 0f, bool lagged = false, bool aiming = false, Vector2 dashDir = default(Vector2))
        {
            if (Root.activeSelf == hidden) Root.SetActive(!hidden);
            if (hidden) return;
            if (_squash.gameObject.activeSelf == faint) _squash.gameObject.SetActive(!faint);
            if (_shadow.activeSelf == faint) _shadow.SetActive(!faint);
            if (_ghost.activeSelf != faint) _ghost.SetActive(faint);
            if (_ghostWedge.activeSelf != faint) _ghostWedge.SetActive(faint);
            var len = Settings.AimLine;
            var showAim = aiming && !faint && len > 0.01f;
            if (_aimRoot.gameObject.activeSelf != showAim) _aimRoot.gameObject.SetActive(showAim);
            if (showAim) _aimRoot.localScale = new Vector3(len, 1f, 1f);
            if (_boostRing.activeSelf) _boostRing.SetActive(false); // (sped up shows arrows now)
            var dt = Time.unscaledDeltaTime;
            var t = Time.unscaledTime;

            // slowed: three solid black down arrows drifting down through the player and fading, one after another
            _slowAlpha = Mathf.MoveTowards(_slowAlpha, slowed ? 1f : 0f, dt * 5f);
            var showSlow = _slowAlpha > 0.01f && !faint;
            if (_slowRoot.gameObject.activeSelf != showSlow) _slowRoot.gameObject.SetActive(showSlow);
            if (showSlow)
                for (var k = 0; k < 3; k++)
                {
                    var phase = (t * 1.1f + k / 3f) % 1f;
                    var x = (k - 1) * _r * 0.75f;
                    _slowArrows[k].localPosition = new Vector3(x, _r * (0.9f - 1.6f * phase), 0f);
                    var a = Mathf.Sin(phase * Mathf.PI) * 0.95f * _slowAlpha;
                    Draw.Recolor(_slowMesh[k], new Color(0f, 0f, 0f, a));
                    Draw.Recolor(_slowOutline[k], new Color(0f, 0f, 0f, a));
                }

            // sped up: three white up arrows drifting up through the player
            _fastAlpha = Mathf.MoveTowards(_fastAlpha, boosted ? 1f : 0f, dt * 5f);
            var showFast = _fastAlpha > 0.01f && !faint;
            if (_fastRoot.gameObject.activeSelf != showFast) _fastRoot.gameObject.SetActive(showFast);
            if (showFast)
                for (var k = 0; k < 3; k++)
                {
                    var phase = (t * 1.3f + k / 3f) % 1f;
                    var x = (k - 1) * _r * 0.75f;
                    _fastArrows[k].localPosition = new Vector3(x, _r * (-0.9f + 1.6f * phase), 0f);
                    var a = Mathf.Min(1f, 0.3f + 1.4f * Mathf.Sin(phase * Mathf.PI)) * _fastAlpha; // solid most of the way
                    Draw.Recolor(_fastMesh[k], new Color(1f, 1f, 1f, a));
                    Draw.Recolor(_fastOutline[k], new Color(0f, 0f, 0f, 0.7f * a));
                }

            // stunned: stars circling
            _stunAlpha = Mathf.MoveTowards(_stunAlpha, stunned ? 1f : 0f, dt * 6f);
            var showStun = _stunAlpha > 0.01f && !faint;
            if (_stunRoot.gameObject.activeSelf != showStun) _stunRoot.gameObject.SetActive(showStun);
            if (showStun)
            {
                _stunRoot.localRotation = Quaternion.Euler(0f, 0f, t * 260f);
                for (var k = 0; k < 3; k++)
                {
                    var twinkle = 0.75f + 0.25f * Mathf.Sin(t * 14f + k * 2f);
                    Draw.Recolor(_stunMesh[k], new Color(1f, 0.9f, 0.35f, _stunAlpha * twinkle));
                }
            }

            // dashing: streaks behind (along the way the dash went) and a bright rim
            dash = faint ? 0f : Mathf.Clamp01(dash);
            dash = Mathf.Round(dash * 60f) / 60f; // fine steps: the streaks grow smoothly
            if (dash != _dashShown)
            {
                _dashShown = dash;
                _dashRoot.gameObject.SetActive(dash > 0f);
                if (dash > 0f)
                {
                    for (var k = 0; k < 3; k++)
                    {
                        Draw.Recolor(_dashMesh[k], new Color(1f, 1f, 1f, 0.75f * dash));
                        // drawn outwards from the player over the first part of the dash (the middle one a touch first)
                        var grow = Ui.EaseOut(Mathf.Clamp01((1f - dash) / 0.4f - (k == 1 ? 0f : 0.15f)));
                        _streak[k].localScale = new Vector3(Mathf.Max(0.001f, grow), 1f, 1f);
                    }
                    Draw.Recolor(_dashMesh[3], new Color(1f, 1f, 1f, 0.9f * dash));
                }
            }
            if (dash > 0f)
                _dashRoot.localRotation = dashDir.sqrMagnitude > 1e-6f
                    ? Quaternion.Euler(0f, 0f, Mathf.Atan2(dashDir.y, dashDir.x) * Mathf.Rad2Deg)
                    : _body.localRotation;

            // hit lag: a quick shake while frozen
            _squash.localPosition = lagged ? (Vector3)(Random.insideUnitCircle * _r * 0.12f) : Vector3.zero;
            // the cooldown bars, stacked under the check bar, while recharging (or always, by setting)
            var row = 0;
            for (var k = 0; k < 3; k++)
            {
                var v = cooldowns != null && k < cooldowns.Length ? cooldowns[k] : -1f;
                var show = v >= 0f && (v < 1f || Settings.DashBarAlways);
                var track = _cdRoot.Find("Track" + k).gameObject;
                if (!show) { if (track.activeSelf) track.SetActive(false); if (_cdFillT[k].gameObject.activeSelf) _cdFillT[k].gameObject.SetActive(false); continue; }
                track.SetActive(true);
                _cdFillT[k].gameObject.SetActive(true);
                // bars that are showing close up the gaps, so they always stack neatly
                var y = -row * 0.13f;
                track.transform.localPosition = new Vector3(0f, y + k * 0.13f, 0f);
                _cdFillT[k].localPosition = new Vector3(-BarLength / 2f, y, 0f);
                _cdFillT[k].localScale = new Vector3(Mathf.Max(0.001f, BarLength * Mathf.Clamp01(v)), 0.08f, 1f);
                row++;
            }
            _cdRoot.localPosition = new Vector3(0f, _barRoot.gameObject.activeSelf ? _r + 0.24f : _r + 0.42f, 0f);
        }

        private void DashBar(float value, int segments)
        {
            if (_segBack.Count != segments) BuildSegments(segments);
            var full = value >= segments - 0.001f;
            var pulsing = false;
            for (var i = 0; i < segments; i++)
            {
                var fill = Mathf.Clamp01(value - i);
                if (fill >= 1f && _segLast[i] < 1f) _segPulse[i] = 1f; // this check just became ready
                _segLast[i] = fill;
                _segPulse[i] = Mathf.MoveTowards(_segPulse[i], 0f, Time.deltaTime * 3f);
                pulsing |= _segPulse[i] > 0.01f;
            }
            var target = !full || Settings.DashBarAlways ? 1f : 0f;
            var fade = target > _barAlpha ? 10f : (pulsing ? 0f : 2.5f); // stay up while a pulse plays
            _barAlpha = Mathf.MoveTowards(_barAlpha, target, Time.deltaTime * fade);
            var on = _barAlpha > 0.01f;
            if (_barRoot.gameObject.activeSelf != on) _barRoot.gameObject.SetActive(on);
            if (!on) return;
            var segLen = (BarLength - SegGap * (segments - 1)) / segments;
            for (var i = 0; i < segments; i++)
            {
                var fill = Mathf.Clamp01(value - i);
                Draw.Recolor(_segBack[i], new Color(0.08f, 0.08f, 0.1f, 0.75f * _barAlpha));
                var col = fill >= 1f ? Color.Lerp(Palette.Dash, Color.white, _segPulse[i]) : Palette.Dash;
                col.a *= _barAlpha;
                Draw.Recolor(_segFill[i], col);
                var x0 = -BarLength / 2f + i * (segLen + SegGap);
                _segFillT[i].localPosition = new Vector3(x0, 0f, 0f);
                _segFillT[i].localScale = new Vector3(Mathf.Max(0.001f, segLen * fill), BarThickness * (1f + _segPulse[i] * 0.8f), 1f);
            }
        }

        private void BuildSegments(int segments)
        {
            foreach (Transform child in _barRoot) Object.Destroy(child.gameObject);
            _segBack.Clear(); _segFill.Clear(); _segFillT.Clear();
            var segLen = (BarLength - SegGap * (segments - 1)) / segments;
            for (var i = 0; i < segments; i++)
            {
                var x0 = -BarLength / 2f + i * (segLen + SegGap);
                var back = Draw.Rect(new Vector2(x0, -BarThickness / 2f), new Vector2(x0 + segLen, BarThickness / 2f), Color.clear);
                Draw.Make("Track", _barRoot, back, 15);
                var fill = Draw.Rect(new Vector2(0f, -0.5f), new Vector2(1f, 0.5f), Color.clear);
                _segFillT.Add(Draw.Make("Fill", _barRoot, fill, 16).transform);
                _segBack.Add(back); _segFill.Add(fill);
            }
            _segPulse = new float[segments];
            _segLast = new float[segments];
            for (var i = 0; i < segments; i++) _segLast[i] = 1f;
        }

        private void HitAnimation()
        {
            _hitAge += Time.deltaTime;
            if (_hitAge >= HitTime)
            {
                if (_flash.activeSelf) _flash.SetActive(false);
                _squash.localScale = Vector3.one;
                return;
            }
            if (!Settings.HitEffects) { _hitAge = HitTime; return; }
            var t = _hitAge;
            // squashed flat against the hit, then a quick wobble back
            var wobble = Mathf.Exp(-t * 13f) * Mathf.Cos(t * 38f);
            var amount = (0.18f + 0.22f * _hitPower) * wobble;
            _squash.localRotation = Quaternion.Euler(0f, 0f, _hitAngle);
            _unsquash.localRotation = Quaternion.Euler(0f, 0f, -_hitAngle);
            _squash.localScale = new Vector3(1f - amount, 1f + amount * 0.6f, 1f);
            // a white flash that fades fast
            var flash = Mathf.Clamp01(1f - t / 0.16f) * (0.45f + 0.45f * _hitPower);
            _flash.SetActive(flash > 0.01f);
            if (flash > 0.01f) Draw.Recolor(_flashMesh, new Color(1f, 1f, 1f, flash));
        }
    }

    /// <summary>Rings that burst out from where players hit each other, then fade.</summary>
    internal sealed class Effects
    {
        private sealed class Burst
        {
            public GameObject Go; public Mesh Mesh;
            public float Age, Life, Size;
            public Color Colour = Color.white;
            public Transform Follow; // shockwave: stay on the caster while it expands
            public bool Exact;       // grow 0 → Size (hit radius), not the impact overshoot curve
        }
        private readonly List<Burst> _live = new List<Burst>(), _spare = new List<Burst>();
        private readonly Transform _parent;

        public Effects(Transform parent) { _parent = parent; }

        public void Impact(Vector2 at, float power) { Ring(at, power, Color.white, 0.35f + 0.6f * power, 0.18f + 0.14f * power); }

        // sparks: thin bright streaks that fly out of a hit and fade, slowing as they go
        private sealed class Spark { public GameObject Go; public Mesh Mesh; public Vector2 Pos, Vel; public float Age, Life, Length, Width; public Color Colour; }
        private readonly List<Spark> _sparks = new List<Spark>(), _spareSparks = new List<Spark>();

        // lightning: jagged forked bolts that crackle out from a hard hit for a moment (like a fighting game's big hits)
        private sealed class Bolt
        {
            public GameObject Go, Glow;
            public float Age, Life;
            public Color Colour, Core;
            public Mesh Mesh, GlowMesh;
            public Transform Follow; // shockwave: move with the caster
            public Vector2 Anchor;   // Follow's position when the bolt was drawn
        }
        private readonly List<Bolt> _bolts = new List<Bolt>();

        /// <summary>Sparks out of a collision: more, faster and longer the harder it was.</summary>
        public void Sparks(Vector2 at, float power, Color colour, int count = 0)
        {
            var n = count > 0 ? count : Mathf.RoundToInt(5 + 14 * power);
            for (var k = 0; k < n; k++)
            {
                Spark s;
                if (_spareSparks.Count > 0) { s = _spareSparks[_spareSparks.Count - 1]; _spareSparks.RemoveAt(_spareSparks.Count - 1); }
                else
                {
                    s = new Spark { Mesh = Draw.Rect(new Vector2(-1f, -0.5f), new Vector2(0f, 0.5f), Color.white) };
                    s.Go = Draw.Make("Spark", _parent, s.Mesh, 23).gameObject;
                }
                var a = Random.Range(0f, Mathf.PI * 2f);
                var speed = Random.Range(4f, 9f + 12f * power);
                s.Pos = at;
                s.Vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * speed;
                s.Age = 0f;
                s.Life = Random.Range(0.18f, 0.3f + 0.25f * power);
                s.Length = Random.Range(0.25f, 0.45f + 0.4f * power);
                s.Width = Random.Range(0.05f, 0.09f);
                s.Colour = Random.value < 0.45f ? Color.white : colour;
                s.Go.SetActive(true);
                _sparks.Add(s);
                StepSpark(s, 0f);
            }
        }

        /// <summary>Forked lightning crackling out of a hard hit.</summary>
        public void Lightning(Vector2 at, float power, Color colour) { Lightning(at, power, colour, Color.white, null); }

        /// <summary>Lightning with its own core colour (hits: a near-black red core in a red glow).</summary>
        public void Lightning(Vector2 at, float power, Color colour, Color core) { Lightning(at, power, colour, core, null); }

        /// <summary>Lightning that stays glued to <paramref name="follow"/> (shockwave on a moving player).</summary>
        public void Lightning(Vector2 at, float power, Color colour, Color core, Transform follow)
        {
            var bolts = Random.Range(3, 5 + Mathf.RoundToInt(2f * power));
            var start = Random.Range(0f, Mathf.PI * 2f);
            for (var k = 0; k < bolts; k++)
            {
                var a = start + k * Mathf.PI * 2f / bolts + Random.Range(-0.35f, 0.35f);
                var length = Random.Range(1.1f, 1.8f) * (0.8f + power);
                AddBolt(at, a, length, colour, core, 0.075f, 2, follow);
            }
        }

        private void AddBolt(Vector2 from, float angle, float length, Color colour, Color core, float width, int forks, Transform follow = null)
        {
            var pts = new List<Vector2> { from };
            var steps = 6;
            var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var side = new Vector2(-dir.y, dir.x);
            var p = from;
            for (var i = 1; i <= steps; i++)
            {
                p = from + dir * (length * i / steps) + side * Random.Range(-0.22f, 0.22f) * length * 0.35f;
                pts.Add(p);
                // a fork off the side now and then
                if (forks > 0 && i > 1 && i < steps && Random.value < 0.35f)
                    AddBolt(p, angle + Random.Range(0.5f, 0.9f) * (Random.value < 0.5f ? -1f : 1f), length * Random.Range(0.3f, 0.5f), colour, core, width * 0.7f, forks - 1, follow);
            }
            var b = new Bolt { Age = 0f, Life = Random.Range(0.16f, 0.26f), Colour = colour, Core = core, Follow = follow };
            if (follow != null) b.Anchor = new Vector2(follow.localPosition.x, follow.localPosition.y);
            else b.Anchor = from;
            b.GlowMesh = Draw.Line(pts, false, width * 3.2f, Palette.Clear(colour));
            b.Glow = Draw.Make("BoltGlow", _parent, b.GlowMesh, 22).gameObject;
            b.Mesh = Draw.Line(pts, false, width, Color.white);
            b.Go = Draw.Make("Bolt", _parent, b.Mesh, 24).gameObject;
            _bolts.Add(b);
            StickBolt(b);
        }

        /// <summary>Shift a bolt so it stays on its Follow (mesh verts were drawn in rink space at fire time).</summary>
        private static void StickBolt(Bolt b)
        {
            if (b.Follow == null) return;
            var d = new Vector2(b.Follow.localPosition.x, b.Follow.localPosition.y) - b.Anchor;
            var p = new Vector3(d.x, d.y, 0f);
            b.Go.transform.localPosition = p;
            b.Glow.transform.localPosition = p;
        }

        /// <summary>
        /// The puck going in: a shockwave that bends the whole rink around the goal (the card ripple's water, much
        /// stronger, tinted the scoring team's colour), a quicker one inside it, sparks and lightning.
        /// </summary>
        public void Explosion(Vector2 at, Color colour)
        {
            Ripple(at, colour, 11f, 1.3f, 0.06f);
            Ripple(at, Color.white, 5f, 0.6f, 0.045f);
            Sparks(at, 1f, colour, 50);
            Sparks(at, 0.6f, Color.white, 16);
            Lightning(at, 1f, colour);
        }

        private static void StepSpark(Spark s, float dt)
        {
            s.Pos += s.Vel * dt;
            s.Vel *= Mathf.Exp(-dt * 5f);
            var k = s.Age / s.Life;
            var len = s.Length * (1f - k * 0.6f) * Mathf.Clamp01(s.Vel.magnitude / 3f + 0.3f);
            s.Go.transform.localPosition = new Vector3(s.Pos.x, s.Pos.y, 0f);
            s.Go.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(s.Vel.y, s.Vel.x) * Mathf.Rad2Deg);
            s.Go.transform.localScale = new Vector3(Mathf.Max(0.01f, len), s.Width, 1f);
            var c = s.Colour; c.a = 1f - k * k;
            Draw.Recolor(s.Mesh, c);
        }

        /// <summary>A ring that bursts out to <paramref name="size"/> (radius) in <paramref name="colour"/> over <paramref name="life"/> seconds.</summary>
        public void Ring(Vector2 at, float power, Color colour, float size, float life)
        {
            var b = TakeBurst();
            b.Go.SetActive(true);
            b.Go.transform.localPosition = new Vector3(at.x, at.y, 0f);
            b.Age = 0f;
            b.Life = life;
            b.Size = size;
            b.Colour = colour;
            b.Follow = null;
            b.Exact = false;
            _live.Add(b);
            Step(b);
        }

        /// <summary>
        /// Swap teleport: dark-blue particle bursts at both spots (Todo / Boogie Woogie vibe), no lightning.
        /// </summary>
        public void SwapBurst(Vector2 from, Vector2 to)
        {
            var blue = new Color(0.25f, 0.35f, 0.95f);
            var deep = new Color(0.12f, 0.15f, 0.55f);
            Sparks(from, 0.9f, blue, 22);
            Sparks(from, 0.55f, deep, 12);
            Sparks(to, 0.9f, blue, 22);
            Sparks(to, 0.55f, deep, 12);
            Ring(from, 0.7f, blue, 1.1f, 0.28f);
            Ring(to, 0.7f, blue, 1.1f, 0.28f);
        }

        /// <summary>
        /// Shockwave (style A): one thin bright ring that expands to <paramref name="radius"/> while stuck to
        /// <paramref name="follow"/>. No lightning, no sparks.
        /// </summary>
        public void Shockwave(Transform follow, float radius, Color colour, float life)
        {
            if (follow == null) return;
            var at = new Vector2(follow.localPosition.x, follow.localPosition.y);
            var b = TakeBurst(true);
            b.Go.SetActive(true);
            b.Go.transform.localPosition = new Vector3(at.x, at.y, 0f);
            b.Age = 0f;
            b.Life = life;
            b.Size = radius;
            b.Colour = colour;
            b.Follow = follow;
            b.Exact = true;
            _live.Add(b);
            Step(b);
        }

        /// <param name="thin">Shockwave uses a thinner ring than hit impacts.</param>
        private Burst TakeBurst(bool thin = false)
        {
            Burst b;
            if (_spare.Count > 0) { b = _spare[_spare.Count - 1]; _spare.RemoveAt(_spare.Count - 1); }
            else
            {
                b = new Burst { Mesh = Draw.Ring(thin ? 0.94f : 0.82f, 1f, 1f, Color.white) };
                b.Go = Draw.Make(thin ? "Shockwave" : "Impact", _parent, b.Mesh, 17).gameObject;
                return b;
            }
            // reuse: rebuild the ring mesh if thickness doesn't match what we need
            var wantThin = thin;
            var isThin = b.Go.name == "Shockwave";
            if (wantThin != isThin)
            {
                Object.Destroy(b.Mesh);
                b.Mesh = Draw.Ring(wantThin ? 0.94f : 0.82f, 1f, 1f, Color.white);
                b.Go.GetComponent<MeshFilter>().sharedMesh = b.Mesh;
                b.Go.name = wantThin ? "Shockwave" : "Impact";
            }
            return b;
        }

        // water ripples: a quad that bends the picture behind it (shader UltraPuck/Ripple)
        private sealed class Wave { public GameObject Go; public MeshRenderer Renderer; public float Age, Life, Strength; public Color Colour; }
        private readonly List<Wave> _waves = new List<Wave>(), _spareWaves = new List<Wave>();
        private static Material _rippleMat;
        private static Mesh _quad;
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();

        /// <summary>A water ripple <paramref name="size"/> units across (radius) that spreads and settles over <paramref name="life"/> seconds.</summary>
        public void Ripple(Vector2 at, Color colour, float size, float life, float strength = 0.022f)
        {
            if (_rippleMat == null)
            {
                var shader = Resources.Load<Shader>("UltraPuck/Ripple");
                if (shader == null || !shader.isSupported) return;
                _rippleMat = new Material(shader);
            }
            if (_quad == null)
            {
                _quad = new Mesh { name = "Ripple" };
                _quad.vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f) };
                _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
                _quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                _quad.RecalculateBounds();
            }
            Wave w;
            if (_spareWaves.Count > 0) { w = _spareWaves[_spareWaves.Count - 1]; _spareWaves.RemoveAt(_spareWaves.Count - 1); }
            else
            {
                w = new Wave { Go = new GameObject("Ripple") };
                w.Go.transform.SetParent(_parent, false);
                w.Go.AddComponent<MeshFilter>().sharedMesh = _quad;
                w.Renderer = w.Go.AddComponent<MeshRenderer>();
                w.Renderer.sharedMaterial = _rippleMat;
                w.Renderer.sortingOrder = 26;
                w.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                w.Renderer.receiveShadows = false;
            }
            w.Go.SetActive(true);
            w.Go.transform.localPosition = new Vector3(at.x, at.y, 0f);
            w.Go.transform.localScale = new Vector3(size, size, 1f);
            w.Age = 0f;
            w.Life = life;
            w.Colour = colour;
            w.Strength = strength;
            _waves.Add(w);
            StepWave(w);
        }

        private void StepWave(Wave w)
        {
            _block.Clear();
            _block.SetFloat("_Age", Mathf.Clamp01(w.Age / w.Life));
            _block.SetFloat("_Strength", w.Strength);
            _block.SetColor("_Tint", w.Colour);
            w.Renderer.SetPropertyBlock(_block);
        }

        public void Update()
        {
            var dt = Time.unscaledDeltaTime;
            for (var i = _waves.Count - 1; i >= 0; i--)
            {
                var w = _waves[i];
                w.Age += dt;
                if (w.Age >= w.Life) { w.Go.SetActive(false); _waves.RemoveAt(i); _spareWaves.Add(w); continue; }
                StepWave(w);
            }
            for (var i = _live.Count - 1; i >= 0; i--)
            {
                var b = _live[i];
                b.Age += dt;
                if (b.Age >= b.Life) { b.Go.SetActive(false); b.Follow = null; _live.RemoveAt(i); _spare.Add(b); continue; }
                if (b.Follow != null)
                    b.Go.transform.localPosition = b.Follow.localPosition;
                Step(b);
            }
            for (var i = _sparks.Count - 1; i >= 0; i--)
            {
                var s = _sparks[i];
                s.Age += dt;
                if (s.Age >= s.Life) { s.Go.SetActive(false); _sparks.RemoveAt(i); _spareSparks.Add(s); continue; }
                StepSpark(s, dt);
            }
            for (var i = _bolts.Count - 1; i >= 0; i--)
            {
                var b = _bolts[i];
                b.Age += dt;
                if (b.Age >= b.Life)
                {
                    Object.Destroy(b.Go); Object.Destroy(b.Glow); Object.Destroy(b.Mesh); Object.Destroy(b.GlowMesh);
                    _bolts.RemoveAt(i);
                    continue;
                }
                StickBolt(b);
                // a crackle: flickers as it fades
                var k = b.Age / b.Life;
                var flicker = Random.value < 0.25f ? 0.35f : 1f;
                var c = b.Core; c.a = (1f - k) * flicker;
                Draw.Recolor(b.Mesh, c);
                var g = b.Colour; g.a = 0.55f * (1f - k) * flicker;
                Draw.Recolor(b.GlowMesh, g);
            }
        }

        private static void Step(Burst b)
        {
            var k = Mathf.Clamp01(b.Age / b.Life);
            var ease = 1f - (1f - k) * (1f - k);
            // Exact (shockwave): outer edge reaches Size at the end, matching the hit radius. Impacts overshoot a little.
            b.Go.transform.localScale = Vector3.one * (b.Exact ? ease * b.Size : (0.25f + ease) * b.Size);
            var c = b.Colour;
            c.a *= (b.Exact ? 1f : 0.85f) * (1f - k);
            Draw.Recolor(b.Mesh, c);
        }
    }

    /// <summary>
    /// The puck: white with a thin dark edge and a soft glow, so it's easy to find. The glow and the trail behind a
    /// fast puck take the colour of the team that last shot or carried it (white after a face-off).
    /// </summary>
    internal sealed class PuckView
    {
        public readonly GameObject Root;
        private const int TrailLength = 6;
        private const float TrailStep = 1f / 60f; // one trail dot per sixtieth of a second
        private const float TrailMinSpeed = 9f;   // units per second before the trail shows
        private readonly Transform[] _trail = new Transform[TrailLength];
        private readonly Mesh[] _trailMesh = new Mesh[TrailLength];
        private readonly Vector2[] _history = new Vector2[TrailLength + 1];
        private int _historyCount;
        private float _sinceSample;
        private readonly float _r;
        private readonly Mesh _glow;
        private Color _colour = Color.white, _target = Color.white;
        private int _team = -2;

        public PuckView(float radius, Transform parent)
        {
            _r = radius;
            Root = new GameObject("Puck");
            Root.transform.SetParent(parent, false);
            // above players (and their bars / stun stars), so a carried puck clipped into you still reads clearly
            for (var i = 0; i < TrailLength; i++)
            {
                _trailMesh[i] = Draw.Disc(radius * (0.92f - i * 0.1f), Color.clear);
                _trail[i] = Draw.Make("Trail" + i, parent, _trailMesh[i], 30).transform;
                _trail[i].gameObject.SetActive(false);
            }
            _glow = Draw.SoftDisc(radius * 1.6f, Color.white, Palette.Clear(Color.white));
            _solid[0] = Draw.Make("Glow", Root.transform, _glow, 31).gameObject;
            _solid[1] = Draw.Make("Edge", Root.transform, Draw.Disc(radius, Palette.PuckEdge), 32).gameObject;
            _solid[2] = Draw.Make("Fill", Root.transform, Draw.Disc(radius * 0.68f, Palette.Puck), 33).gameObject;
            // Invisible Shot: just an outline (for the shooter's team)
            _ghost = Draw.Make("Ghost", Root.transform, Draw.Ring(radius * 0.8f, radius, 1f, new Color(1f, 1f, 1f, 0.8f)), 33).gameObject;
            _ghost.SetActive(false);
        }

        private readonly GameObject[] _solid = new GameObject[3];
        private readonly GameObject _ghost;
        private int _ghostMode;

        /// <summary>Invisible Shot: 0 the puck as normal, 1 an outline only (your team's shot), 2 not drawn at all (the other team's).</summary>
        public void Ghost(int mode)
        {
            if (mode == _ghostMode) return;
            _ghostMode = mode;
            foreach (var g in _solid) g.SetActive(mode == 0);
            _ghost.SetActive(mode == 1);
            if (mode != 0) { foreach (var t in _trail) t.gameObject.SetActive(false); _historyCount = 0; }
        }

        /// <summary>The glow and trail colour: the team's light tint, or white for nobody (-1).</summary>
        private void Tint(int team)
        {
            if (team != _team) { _team = team; _target = team < 0 ? Color.white : Palette.TeamTint(team); }
            _colour = Color.Lerp(_colour, _target, 1f - Mathf.Exp(-Time.deltaTime * 14f)); // a quick fade, not a snap
            var centre = _colour; centre.a = _team < 0 ? 0.3f : 0.55f; // a smaller glow, so a little stronger
            var cols = _glow.colors;
            cols[0] = centre;
            for (var k = 1; k < cols.Length; k++) cols[k] = Palette.Clear(_colour);
            _glow.colors = cols;
            for (var i = 0; i < TrailLength; i++)
            {
                var c = _colour;
                c.a = (_team < 0 ? 0.3f : 0.55f) * (1f - (i + 1f) / (TrailLength + 1f));
                Draw.Recolor(_trailMesh[i], c);
            }
        }

        /// <summary>Hide the puck (and its trail) for a moment, e.g. once it's blown up in the goal.</summary>
        public void Visible(bool on)
        {
            if (Root.activeSelf == on) return;
            Root.SetActive(on);
            if (!on) { foreach (var t in _trail) t.gameObject.SetActive(false); _historyCount = 0; }
        }

        public void Show(Vector2 pos, int team)
        {
            if (!Root.activeSelf) return;
            Tint(team);
            // a jump (face-off) starts the trail again instead of drawing a streak across the rink
            if (_historyCount > 0 && (pos - _history[0]).sqrMagnitude > 4f) _historyCount = 0;
            Root.transform.localPosition = new Vector3(pos.x, pos.y, 0f);

            _sinceSample += Time.deltaTime;
            if (_historyCount == 0 || _sinceSample >= TrailStep)
            {
                _sinceSample = 0f;
                for (var i = Mathf.Min(_historyCount, TrailLength); i > 0; i--) _history[i] = _history[i - 1];
                _history[0] = pos;
                _historyCount = Mathf.Min(_historyCount + 1, TrailLength + 1);
            }
            for (var i = 0; i < TrailLength; i++)
            {
                var on = i + 1 < _historyCount;
                if (on)
                {
                    var p = _history[i + 1];
                    var speed = (_history[i] - p).magnitude / TrailStep;
                    on = Settings.PuckTrail && _ghostMode == 0 && speed > TrailMinSpeed && (pos - p).sqrMagnitude > _r * _r;
                    if (on) _trail[i].localPosition = new Vector3(p.x, p.y, 0f);
                }
                _trail[i].gameObject.SetActive(on);
            }
        }
    }

    /// <summary>Puck Gun shots: small glowing discs in the shooter's team colour.</summary>
    internal sealed class ShotViews
    {
        private readonly Transform _parent;
        private readonly List<GameObject> _pool = new List<GameObject>();
        private readonly List<int> _team = new List<int>();

        public ShotViews(Transform parent) { _parent = parent; }

        public void Show(GameState s, float radius)
        {
            for (var k = 0; k < s.Shots.Length; k++)
            {
                var sh = s.Shots[k];
                var team = sh.Owner >= 0 && sh.Owner < s.Players.Length ? s.Players[sh.Owner].Team : -1;
                if (k >= _pool.Count) { _pool.Add(null); _team.Add(-2); }
                if (_pool[k] == null || _team[k] != team)
                {
                    if (_pool[k] != null) Object.Destroy(_pool[k]);
                    var go = new GameObject("Shot");
                    go.transform.SetParent(_parent, false);
                    var tint = team < 0 ? Color.white : Palette.TeamTint(team);
                    var glow = tint; glow.a = 0.45f;
                    Draw.Make("Glow", go.transform, Draw.SoftDisc(radius * 2.6f, glow, Palette.Clear(glow)), 22);
                    Draw.Make("Body", go.transform, Draw.Disc(radius, tint), 23);
                    Draw.Make("Core", go.transform, Draw.Disc(radius * 0.5f, Color.white), 24);
                    _pool[k] = go;
                    _team[k] = team;
                }
                _pool[k].SetActive(true);
                _pool[k].transform.localPosition = new Vector3(sh.Pos.X.ToFloat(), sh.Pos.Y.ToFloat(), 0f);
            }
            for (var k = s.Shots.Length; k < _pool.Count; k++) if (_pool[k] != null && _pool[k].activeSelf) _pool[k].SetActive(false);
        }
    }

    /// <summary>
    /// Ice Trail: each trail as a strip of light blue ice on the floor, under the players, in one mesh per trail. Its
    /// shader (UltraPuck/Ice) shows the arena's hidden frozen surface through it, fixed to the rink, with a reflection
    /// that moves with the camera.
    /// </summary>
    internal sealed class IceViews
    {
        private static readonly Color Body = Color.Lerp(Palette.Floor, new Color(0.66f, 0.87f, 1f), 0.72f);
        private static Material _mat;
        private readonly Transform _parent;
        private readonly Dictionary<int, MeshFilter> _trails = new Dictionary<int, MeshFilter>();
        private readonly Dictionary<int, long> _drawn = new Dictionary<int, long>();
        private readonly List<int> _gone = new List<int>();
        private readonly List<Vector2> _pts = new List<Vector2>();

        public IceViews(Transform parent) { _parent = parent; }

        private static Material Mat
        {
            get
            {
                if (_mat == null)
                {
                    var shader = Resources.Load<Shader>("UltraPuck/Ice");
                    _mat = shader != null && shader.isSupported ? new Material(shader) : Draw.Flat;
                }
                return _mat;
            }
        }

        public void Show(GameState s, float radius)
        {
            var seen = new HashSet<int>();
            var i = 0;
            while (i < s.Ice.Length)
            {
                var id = s.Ice[i].Trail;
                _pts.Clear();
                var j = i;
                for (; j < s.Ice.Length && s.Ice[j].Trail == id; j++) _pts.Add(new Vector2(s.Ice[j].Pos.X.ToFloat(), s.Ice[j].Pos.Y.ToFloat()));
                seen.Add(id);
                // build it again only when it grew or melted
                long key = ((long)_pts.Count << 32) ^ s.Ice[i].Pos.X.Raw ^ (s.Ice[j - 1].Pos.Y.Raw << 7);
                long old;
                if (!_drawn.TryGetValue(id, out old) || old != key)
                {
                    MeshFilter mf;
                    if (_trails.TryGetValue(id, out mf)) Object.Destroy(mf.sharedMesh);
                    var mesh = _pts.Count < 2 ? Draw.Disc(radius, Body) : Draw.Line(_pts, false, radius * 2f, Body);
                    if (mesh != null && _pts.Count < 2) { var v = mesh.vertices; for (var k = 0; k < v.Length; k++) v[k] += (Vector3)_pts[0]; mesh.vertices = v; mesh.RecalculateBounds(); }
                    if (mf == null)
                    {
                        mf = Draw.Make("Ice" + id, _parent, mesh, 3);
                        mf.GetComponent<MeshRenderer>().sharedMaterial = Mat;
                        _trails[id] = mf;
                    }
                    else mf.sharedMesh = mesh;
                    _drawn[id] = key;
                }
                i = j;
            }
            _gone.Clear();
            foreach (var id in _trails.Keys) if (!seen.Contains(id)) _gone.Add(id);
            foreach (var id in _gone)
            {
                Object.Destroy(_trails[id].sharedMesh);
                Object.Destroy(_trails[id].gameObject);
                _trails.Remove(id);
                _drawn.Remove(id);
            }
        }
    }

    /// <summary>
    /// Stone Walls: a row of chunky grey stones along each piece of wall. A piece pops up as it's raised, and shakes,
    /// sinks and bursts into grit as it crumbles.
    /// </summary>
    internal sealed class StoneViews
    {
        private readonly Transform _parent;
        private readonly Effects _effects;
        private readonly Dictionary<long, GameObject> _drawn = new Dictionary<long, GameObject>();
        private readonly Dictionary<long, Vector2> _mid = new Dictionary<long, Vector2>();
        private readonly Dictionary<long, float> _life = new Dictionary<long, float>();
        private readonly HashSet<long> _seen = new HashSet<long>();
        private readonly List<long> _gone = new List<long>();
        private const float Rise = 0.12f, Crumble = 0.4f;

        public StoneViews(Transform parent, Effects effects) { _parent = parent; _effects = effects; }

        public void Show(GameState s, float halfWidth, float lifeTime)
        {
            _seen.Clear();
            foreach (var st in s.Stones)
            {
                var key = st.A.X.Raw * 73856093L ^ st.A.Y.Raw * 19349663L ^ (long)st.Wall * 83492791L;
                _seen.Add(key);
                GameObject go;
                if (!_drawn.TryGetValue(key, out go))
                {
                    var a = V.Of(st.A);
                    var b = V.Of(st.B);
                    go = Build(a, b, halfWidth, key);
                    _drawn[key] = go;
                    _mid[key] = (a + b) * 0.5f;
                }
                var life = st.Life.ToFloat();
                _life[key] = life;
                var age = lifeTime - life;
                var up = Mathf.Clamp01(age / Rise);
                up = 1f - (1f - up) * (1f - up);
                var crumbling = 1f - Mathf.Clamp01(life / Crumble);
                var size = Mathf.Min(up, 1f - 0.45f * crumbling);
                var shake = crumbling > 0f ? Random.insideUnitCircle * 0.06f * crumbling : Vector2.zero;
                go.transform.localPosition = (Vector3)(_mid[key] + shake);
                go.transform.localScale = new Vector3(size, size, 1f);
            }
            _gone.Clear();
            foreach (var key in _drawn.Keys) if (!_seen.Contains(key)) _gone.Add(key);
            foreach (var key in _gone)
            {
                // crumbled (not just undone by a rewind): grit flies off it
                float last;
                if (_life.TryGetValue(key, out last) && last < 0.15f) _effects.Sparks(_mid[key], 0.55f, new Color(0.62f, 0.62f, 0.66f), 9);
                Object.Destroy(_drawn[key]);
                _drawn.Remove(key);
                _mid.Remove(key);
                _life.Remove(key);
            }
        }

        private GameObject Build(Vector2 a, Vector2 b, float halfWidth, long key)
        {
            var go = new GameObject("Stone");
            go.transform.SetParent(_parent, false);
            var mid = (a + b) * 0.5f;
            var la = a - mid;
            var lb = b - mid;
            var rng = new System.Random((int)(key ^ (key >> 32)));
            Draw.Make("Shadow", go.transform, Draw.Line(new[] { la, lb }, false, halfWidth * 2.3f, Palette.Shadow), 7)
                .transform.localPosition = new Vector3(0.1f, -0.16f, 0f);
            Draw.Make("Base", go.transform, Draw.Line(new[] { la, lb }, false, halfWidth * 2f, new Color(0.2f, 0.2f, 0.23f)), 8);
            var len = (lb - la).magnitude;
            var count = Mathf.Max(1, Mathf.RoundToInt(len / (halfWidth * 1.9f)));
            for (var k = 0; k < count; k++)
            {
                var at = Vector2.Lerp(la, lb, (k + 0.5f) / count);
                var r = halfWidth * (1.3f + 0.25f * (float)rng.NextDouble());
                var turn = (float)rng.NextDouble() * Mathf.PI * 2f;
                var shade = 0.44f + 0.14f * (float)rng.NextDouble();
                var pts = new List<Vector2>();
                var sides = 5 + rng.Next(3);
                for (var j = 0; j < sides; j++)
                {
                    var ang = turn + j * Mathf.PI * 2f / sides + ((float)rng.NextDouble() - 0.5f) * 0.5f;
                    var rr = r * (0.82f + 0.18f * (float)rng.NextDouble());
                    pts.Add(at + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr);
                }
                Draw.Make("Stone", go.transform, Draw.Polygon(pts, new Color(shade, shade, shade + 0.04f)), 9);
                Draw.Make("Edge", go.transform, Draw.Line(pts, true, 0.06f, new Color(0.13f, 0.13f, 0.15f)), 10);
                // a lighter top face, up and to the left
                var top = new List<Vector2>();
                foreach (var p in pts) top.Add(at + (p - at) * 0.55f + new Vector2(-0.05f, 0.06f));
                var hi = shade + 0.16f;
                Draw.Make("Top", go.transform, Draw.Polygon(top, new Color(hi, hi, hi + 0.04f)), 11);
            }
            return go;
        }
    }

    /// <summary>Pylon goalies: dark dummies with a ring in their team's colour.</summary>
    internal sealed class PylonViews
    {
        private readonly Transform _parent;
        private readonly List<GameObject> _pool = new List<GameObject>();
        private readonly List<int> _team = new List<int>();

        public PylonViews(Transform parent) { _parent = parent; }

        public void Show(GameState s, float radius)
        {
            for (var k = 0; k < s.Pylons.Length; k++)
            {
                var py = s.Pylons[k];
                if (k >= _pool.Count) { _pool.Add(null); _team.Add(-1); }
                if (_pool[k] == null || _team[k] != py.Team)
                {
                    if (_pool[k] != null) Object.Destroy(_pool[k]);
                    var go = new GameObject("Pylon");
                    go.transform.SetParent(_parent, false);
                    Draw.Make("Shadow", go.transform, Draw.Disc(radius * 1.04f, Palette.Shadow), 6).transform.localPosition = new Vector3(radius * 0.18f, -radius * 0.32f, 0f);
                    Draw.Make("Rim", go.transform, Draw.Disc(radius, Palette.Team(py.Team)), 8);
                    Draw.Make("Body", go.transform, Draw.Disc(radius * 0.8f, new Color(0.2f, 0.2f, 0.22f)), 9);
                    Draw.Make("Top", go.transform, Draw.Disc(radius * 0.32f, new Color(0.42f, 0.42f, 0.45f)), 10);
                    _pool[k] = go;
                    _team[k] = py.Team;
                }
                _pool[k].SetActive(true);
                _pool[k].transform.localPosition = new Vector3(py.Pos.X.ToFloat(), py.Pos.Y.ToFloat(), 0f);
                // drawn at the base size, scaled to this one's (it grows with every Pylon Goalie card)
                var size = py.Radius.Raw > 0 && radius > 0f ? py.Radius.ToFloat() / radius : 1f;
                _pool[k].transform.localScale = new Vector3(size, size, 1f);
            }
            for (var k = s.Pylons.Length; k < _pool.Count; k++) if (_pool[k] != null && _pool[k].activeSelf) _pool[k].SetActive(false);
        }
    }

    /// <summary>Swap rocks: the thrower's symbol on a darker shade of their colour, so you can tell whose it is.</summary>
    internal sealed class RockViews
    {
        private readonly Transform _parent;
        private readonly List<GameObject> _pool = new List<GameObject>();
        private readonly List<string> _keys = new List<string>();
        private readonly List<MeshFilter> _lines = new List<MeshFilter>();
        private readonly List<Mesh> _lineMeshes = new List<Mesh>();
        private readonly List<Vector3> _lineVerts = new List<Vector3>(64);
        private readonly List<int> _lineTris = new List<int>(128);
        private readonly List<Color> _lineCols = new List<Color>(64);

        public RockViews(Transform parent) { _parent = parent; }

        private readonly List<Mesh> _dims = new List<Mesh>();

        /// <summary>
        /// How dark a rock is while it's running out: nothing until <paramref name="from"/> seconds are left, then flashing
        /// dim, about 1.5 times a second at first and 8 at the end.
        /// </summary>
        private static float FlashDim(float left, float from)
        {
            if (left >= from || from <= 0f) return 0f;
            left = Mathf.Max(0f, left);
            // flashes so far: the rate goes from 1.5 to 8 a second as the time runs out (its integral, so it speeds up smoothly)
            float hi = 8f, lo = 1.5f;
            var gone = from - left;
            var turns = hi * gone - (hi - lo) / (2f * from) * (from * from - left * left);
            var on = 0.5f - 0.5f * Mathf.Cos(turns * Mathf.PI * 2f);
            return 0.65f * on;
        }

        public void Show(GameState s, float radius, System.Func<int, int> symbolOf, System.Func<int, Color> colourOf, System.Func<int, bool> mine = null, float flashFrom = 3f)
        {
            for (var k = 0; k < s.Rocks.Length; k++)
            {
                var rk = s.Rocks[k];
                var owner = rk.Owner;
                var symbol = owner >= 0 ? symbolOf(owner) : 0;
                var col = owner >= 0 && owner < s.Players.Length ? colourOf(owner) : new Color(0.35f, 0.35f, 0.4f);
                var body = Palette.Darker(col, 0.42f);
                var rim = Palette.Darker(col, 0.62f);
                var key = "Rock" + symbol + "_" + Mathf.RoundToInt(radius * 1000f) + "_" + ColorUtility.ToHtmlStringRGB(body);
                while (_pool.Count <= k) { _pool.Add(null); _keys.Add(""); }
                if (_pool[k] == null || _keys[k] != key)
                {
                    if (_pool[k] != null) Object.Destroy(_pool[k]);
                    var go = new GameObject(key);
                    go.transform.SetParent(_parent, false);
                    Draw.Make("Shadow", go.transform, Draw.Disc(radius * 1.05f, Palette.Shadow), 7).transform.localPosition = new Vector3(radius * 0.15f, -radius * 0.28f, 0f);
                    Draw.Make("Rim", go.transform, Draw.Disc(radius, rim), 11);
                    Draw.Make("Body", go.transform, Draw.Disc(radius * 0.82f, body), 12);
                    Draw.Make("Symbol", go.transform, Symbols.Mesh(symbol, radius * 0.95f, Color.white), 13);
                    while (_dims.Count <= k) _dims.Add(null);
                    _dims[k] = Draw.Disc(radius * 1.02f, new Color(0f, 0f, 0f, 0f));
                    Draw.Make("Dim", go.transform, _dims[k], 14);
                    _pool[k] = go;
                    _keys[k] = key;
                }
                _pool[k].SetActive(true);
                _pool[k].transform.localPosition = new Vector3(rk.Pos.X.ToFloat(), rk.Pos.Y.ToFloat(), 0f);
                if (k < _dims.Count && _dims[k] != null) Draw.Recolor(_dims[k], new Color(0f, 0f, 0f, FlashDim(rk.Life.ToFloat(), flashFrom)));
            }
            for (var k = s.Rocks.Length; k < _pool.Count; k++) if (_pool[k] != null && _pool[k].activeSelf) _pool[k].SetActive(false);

            // optional dotted line from the Swap owner to their rock, only for the person who owns it (one reused mesh each — no Destroy)
            var lineN = 0;
            if (Settings.RockLine)
            {
                for (var k = 0; k < s.Rocks.Length; k++)
                {
                    var rk = s.Rocks[k];
                    var o = rk.Owner;
                    if (o < 0 || o >= s.Players.Length || s.Players[o].Stats.Swap <= 0 || mine == null || !mine(o)) continue;
                    var from = V.Of(s.Players[o].Pos);
                    var to = V.Of(rk.Pos);
                    var col = colourOf(o);
                    col.a = 0.35f;
                    SetDashedLine(lineN, from, to, col);
                    lineN++;
                }
            }
            for (var k = lineN; k < _lines.Count; k++) if (_lines[k] != null && _lines[k].gameObject.activeSelf) _lines[k].gameObject.SetActive(false);
        }

        private void SetDashedLine(int i, Vector2 from, Vector2 to, Color colour)
        {
            while (_lines.Count <= i)
            {
                var mesh = new Mesh { name = "RockLine" };
                var mf = Draw.Make("RockLine", _parent, mesh, 8);
                _lines.Add(mf);
                _lineMeshes.Add(mesh);
            }
            var delta = to - from;
            var len = delta.magnitude;
            if (len < 0.35f) { _lines[i].gameObject.SetActive(false); return; }
            var dir = delta / len;
            var n = new Vector2(-dir.y, dir.x) * 0.032f;
            const float dash = 0.38f, gap = 0.26f;
            _lineVerts.Clear();
            _lineTris.Clear();
            _lineCols.Clear();
            var t = 0.45f;
            var end = len - 0.4f;
            while (t < end)
            {
                var a = from + dir * t;
                var b = from + dir * Mathf.Min(t + dash, end);
                if ((b - a).sqrMagnitude > 0.0001f)
                {
                    var i0 = _lineVerts.Count;
                    _lineVerts.Add(a + n); _lineVerts.Add(a - n); _lineVerts.Add(b - n); _lineVerts.Add(b + n);
                    _lineTris.Add(i0); _lineTris.Add(i0 + 2); _lineTris.Add(i0 + 1);
                    _lineTris.Add(i0); _lineTris.Add(i0 + 3); _lineTris.Add(i0 + 2);
                    for (var c = 0; c < 4; c++) _lineCols.Add(colour);
                }
                t += dash + gap;
            }
            var m = _lineMeshes[i];
            m.Clear();
            if (_lineVerts.Count == 0) { _lines[i].gameObject.SetActive(false); return; }
            m.SetVertices(_lineVerts);
            m.SetTriangles(_lineTris, 0);
            m.SetColors(_lineCols);
            m.RecalculateBounds();
            _lines[i].sharedMesh = m;
            _lines[i].gameObject.SetActive(true);
        }
    }
}
