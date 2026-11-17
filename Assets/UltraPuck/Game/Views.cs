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

        public RinkView(MapDef map)
        {
            Root = new GameObject("Rink");
            var outline = new List<Vector2>();
            foreach (var p in map.WallLines[0]) outline.Add(V.Of(p));
            Draw.Make("Floor", Root.transform, Draw.Polygon(outline, Palette.Floor), 0);

            var min = V.Of(map.BoundsMin); var max = V.Of(map.BoundsMax);
            var size = max - min;
            var cx = (min.x + max.x) / 2f;

            // markings: centre line and circle, plus a line across each half
            Draw.Make("CentreLine", Root.transform, Draw.Rect(new Vector2(cx - 0.07f, min.y), new Vector2(cx + 0.07f, max.y), Palette.FloorLines), 2);
            Draw.Make("CentreCircle", Root.transform, Draw.Ring(2.4f, 2.54f, 1f, Palette.FloorLines), 2).transform.localPosition = V.Of(map.PuckSpot);
            Draw.Make("CentreDot", Root.transform, Draw.Disc(0.25f, Palette.FloorLines), 2).transform.localPosition = V.Of(map.PuckSpot);
            foreach (var side in new[] { -1f, 1f })
            {
                var x = cx + side * size.x * 0.19f;
                Draw.Make("SideLine", Root.transform, Draw.Rect(new Vector2(x - 0.07f, min.y), new Vector2(x + 0.07f, max.y), Palette.FloorLinesSide), 2);
            }

            // goals in a dark shade of the colour of the team that defends them
            foreach (var g in map.Goals)
                Draw.Make("Goal" + g.Team, Root.transform, Draw.Rect(V.Of(g.Min), V.Of(g.Max), Palette.Goal(g.Team)), 3);

            for (var w = 0; w < map.WallLines.Count; w++)
            {
                var pts = new List<Vector2>();
                foreach (var p in map.WallLines[w]) pts.Add(V.Of(p));
                Draw.Make("Wall" + w, Root.transform, Draw.Line(pts, map.WallClosed[w], 0.34f, Palette.Wall), 4);
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
        private readonly MeshFilter _chargeRing;
        private readonly Mesh _chargeMesh = new Mesh();
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

        // hit animation
        private readonly Mesh _flashMesh;
        private readonly GameObject _flash;
        private int _hitsSeen = -1;
        private float _hitAge = 10f, _hitPower, _hitAngle;
        private const float HitTime = 0.32f;

        public PlayerView(int team, float radius, Transform parent)
        {
            _r = radius;
            var c = Palette.Team(team);
            Root = new GameObject("Player");
            Root.transform.SetParent(parent, false);
            // a drop shadow that doesn't turn with the player, so the light always comes from the same side
            Draw.Make("Shadow", Root.transform, Draw.Disc(radius * 1.04f, Palette.Shadow), 7).transform.localPosition = new Vector3(radius * 0.18f, -radius * 0.32f, 0f);

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
            // team symbol (doesn't turn with the player, so it always reads the same)
            var sym = radius * 0.38f;
            var symbol = team == 0
                ? Draw.Triangle(new Vector2(0f, sym * 1.15f), new Vector2(sym, -sym * 0.75f), new Vector2(-sym, -sym * 0.75f), Color.white)
                : Draw.Rect(new Vector2(-sym * 0.8f, -sym * 0.8f), new Vector2(sym * 0.8f, sym * 0.8f), Color.white);
            Draw.Make("Symbol", _unsquash, symbol, 13);
            _chargeRing = Draw.Make("Charge", Root.transform, _chargeMesh, 14);

            _barRoot = new GameObject("DashBar").transform;
            _barRoot.SetParent(Root.transform, false);
            _barRoot.localPosition = new Vector3(0f, radius + 0.42f, 0f);
            _barRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// Draw this frame. Returns the power (0..1) of a hit that just happened, or 0, so the caller can add an
        /// impact ring and screen shake; <paramref name="hitAt"/> is where it happened.
        /// </summary>
        public float Show(Vector2 pos, Vector2 facing, float dashCharge, int dashMax, float charge, int hits, float hitPower, Vector2 hitDir, out Vector2 hitAt)
        {
            Root.transform.localPosition = new Vector3(pos.x, pos.y, 0f);
            if (facing.sqrMagnitude > 1e-6f) _body.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg);

            charge = Mathf.Round(charge * 48f) / 48f;
            if (charge != _chargeShown)
            {
                _chargeShown = charge;
                _chargeRing.gameObject.SetActive(charge > 0f);
                if (charge > 0f) Draw.Ring(_r * 1.3f, _r * 1.5f, charge, Palette.Charge, _chargeMesh);
            }

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
        private sealed class Burst { public GameObject Go; public Mesh Mesh; public float Age, Life, Size; }
        private readonly List<Burst> _live = new List<Burst>(), _spare = new List<Burst>();
        private readonly Transform _parent;

        public Effects(Transform parent) { _parent = parent; }

        public void Impact(Vector2 at, float power)
        {
            Burst b;
            if (_spare.Count > 0) { b = _spare[_spare.Count - 1]; _spare.RemoveAt(_spare.Count - 1); }
            else
            {
                b = new Burst { Mesh = Draw.Ring(0.82f, 1f, 1f, Color.white) };
                b.Go = Draw.Make("Impact", _parent, b.Mesh, 17).gameObject;
            }
            b.Go.SetActive(true);
            b.Go.transform.localPosition = new Vector3(at.x, at.y, 0f);
            b.Age = 0f;
            b.Life = 0.18f + 0.14f * power;
            b.Size = 0.35f + 0.6f * power;
            _live.Add(b);
            Step(b);
        }

        public void Update()
        {
            for (var i = _live.Count - 1; i >= 0; i--)
            {
                var b = _live[i];
                b.Age += Time.deltaTime;
                if (b.Age >= b.Life) { b.Go.SetActive(false); _live.RemoveAt(i); _spare.Add(b); continue; }
                Step(b);
            }
        }

        private static void Step(Burst b)
        {
            var k = b.Age / b.Life;
            var ease = 1f - (1f - k) * (1f - k);
            b.Go.transform.localScale = Vector3.one * (0.25f + ease) * b.Size;
            Draw.Recolor(b.Mesh, new Color(1f, 1f, 1f, 0.85f * (1f - k)));
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
            for (var i = 0; i < TrailLength; i++)
            {
                _trailMesh[i] = Draw.Disc(radius * (0.92f - i * 0.1f), Color.clear);
                _trail[i] = Draw.Make("Trail" + i, parent, _trailMesh[i], 18).transform;
                _trail[i].gameObject.SetActive(false);
            }
            _glow = Draw.SoftDisc(radius * 2.4f, Color.white, Palette.Clear(Color.white));
            Draw.Make("Glow", Root.transform, _glow, 19);
            Draw.Make("Edge", Root.transform, Draw.Disc(radius, Palette.PuckEdge), 20);
            Draw.Make("Fill", Root.transform, Draw.Disc(radius * 0.84f, Palette.Puck), 21);
        }

        /// <summary>The glow and trail colour: the team's light tint, or white for nobody (-1).</summary>
        private void Tint(int team)
        {
            if (team != _team) { _team = team; _target = team < 0 ? Color.white : Palette.TeamTint(team); }
            _colour = Color.Lerp(_colour, _target, 1f - Mathf.Exp(-Time.deltaTime * 14f)); // a quick fade, not a snap
            var centre = _colour; centre.a = _team < 0 ? 0.22f : 0.42f;
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

        public void Show(Vector2 pos, int team)
        {
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
                    on = Settings.PuckTrail && speed > TrailMinSpeed && (pos - p).sqrMagnitude > _r * _r;
                    if (on) _trail[i].localPosition = new Vector3(p.x, p.y, 0f);
                }
                _trail[i].gameObject.SetActive(on);
            }
        }
    }
}
