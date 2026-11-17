using System.Collections.Generic;
using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>Builds simple flat-coloured meshes (discs, rings, filled shapes, thick lines) for the placeholder art.</summary>
    internal static class Draw
    {
        private static Material _flat;

        /// <summary>One unlit material that shows each mesh's vertex colours (Resources/Flat, made by the build script).</summary>
        public static Material Flat
        {
            get
            {
                if (_flat == null)
                {
                    _flat = Resources.Load<Material>("Flat");
                    if (_flat == null) _flat = new Material(Shader.Find("Sprites/Default"));
                }
                return _flat;
            }
        }

        /// <summary>A child object that shows <paramref name="mesh"/>; higher <paramref name="order"/> draws on top.</summary>
        public static MeshFilter Make(string name, Transform parent, Mesh mesh, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Flat;
            mr.sortingOrder = order;
            return mf;
        }

        public static Mesh Disc(float r, Color c, int segments = 40)
        {
            var v = new List<Vector3> { Vector3.zero };
            var t = new List<int>();
            for (var k = 0; k <= segments; k++)
            {
                var a = k * Mathf.PI * 2f / segments;
                v.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
                if (k > 0) { t.Add(0); t.Add(k + 1); t.Add(k); }
            }
            return Build(v, t, c);
        }

        /// <summary>Paint every vertex of <paramref name="m"/> one colour (cheap for the small meshes used for bars and flashes).</summary>
        public static void Recolor(Mesh m, Color c)
        {
            var n = m.vertexCount;
            if (_colors.Length != n) _colors = new Color[n];
            for (var k = 0; k < n; k++) _colors[k] = c;
            m.colors = _colors;
        }
        private static Color[] _colors = new Color[0];

        /// <summary>A soft disc that fades from <paramref name="centre"/> in the middle to <paramref name="edge"/> at the rim (glows, light pools).</summary>
        public static Mesh SoftDisc(float r, Color centre, Color edge, int segments = 48)
        {
            var v = new List<Vector3> { Vector3.zero };
            var t = new List<int>();
            for (var k = 0; k <= segments; k++)
            {
                var a = k * Mathf.PI * 2f / segments;
                v.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
                if (k > 0) { t.Add(0); t.Add(k + 1); t.Add(k); }
            }
            var m = Build(v, t, edge);
            var cols = m.colors;
            cols[0] = centre;
            m.colors = cols;
            return m;
        }

        /// <summary>A band between two radii, <paramref name="fraction"/> of the way round, clockwise from the top.</summary>
        public static Mesh Ring(float inner, float outer, float fraction, Color c, Mesh reuse = null, int segments = 48)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            var n = Mathf.Max(1, Mathf.CeilToInt(segments * Mathf.Clamp01(fraction)));
            for (var k = 0; k <= n; k++)
            {
                var a = Mathf.PI / 2f - Mathf.Clamp01(fraction) * Mathf.PI * 2f * k / n;
                var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v.Add(d * inner); v.Add(d * outer);
                if (k > 0)
                {
                    var i = k * 2;
                    t.Add(i - 2); t.Add(i - 1); t.Add(i + 1);
                    t.Add(i - 2); t.Add(i + 1); t.Add(i);
                }
            }
            return Build(v, t, c, reuse);
        }

        /// <summary>
        /// Part of a ring, centred on the angle <paramref name="centre"/> (radians, 0 = +x) and reaching
        /// <paramref name="half"/> radians either side of it: an arc that grows out both ways from one point.
        /// </summary>
        public static Mesh Arc(float inner, float outer, float centre, float half, Color c, Mesh reuse = null, int segments = 48)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            half = Mathf.Clamp(half, 0f, Mathf.PI);
            var n = Mathf.Max(1, Mathf.CeilToInt(segments * half / Mathf.PI));
            for (var k = 0; k <= n; k++)
            {
                var a = centre - half + 2f * half * k / n;
                var d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v.Add(d * inner); v.Add(d * outer);
                if (k > 0)
                {
                    var i = k * 2;
                    t.Add(i - 2); t.Add(i - 1); t.Add(i + 1);
                    t.Add(i - 2); t.Add(i + 1); t.Add(i);
                }
            }
            return Build(v, t, c, reuse);
        }

        /// <summary>A filled polygon (any simple outline, either winding).</summary>
        public static Mesh Polygon(IList<Vector2> pts, Color c)
        {
            var v = new List<Vector3>();
            foreach (var p in pts) v.Add(p);
            return Build(v, Triangulate(pts), c);
        }

        /// <summary>A line of the given width through the points, with rounded joints.</summary>
        public static Mesh Line(IList<Vector2> pts, bool closed, float width, Color c)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            var h = width / 2f;
            var count = closed ? pts.Count : pts.Count - 1;
            for (var k = 0; k < count; k++)
            {
                Vector2 a = pts[k], b = pts[(k + 1) % pts.Count];
                var d = b - a;
                if (d.sqrMagnitude < 1e-8f) continue;
                var n = new Vector2(-d.y, d.x).normalized * h;
                var i = v.Count;
                v.Add(a + n); v.Add(a - n); v.Add(b - n); v.Add(b + n);
                t.Add(i); t.Add(i + 2); t.Add(i + 1);
                t.Add(i); t.Add(i + 3); t.Add(i + 2);
            }
            // round joints
            foreach (var p in pts)
            {
                var centre = v.Count;
                v.Add(p);
                const int seg = 10;
                for (var k = 0; k <= seg; k++)
                {
                    var a = k * Mathf.PI * 2f / seg;
                    v.Add(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * h);
                    if (k > 0) { t.Add(centre); t.Add(centre + k + 1); t.Add(centre + k); }
                }
            }
            return Build(v, t, c);
        }

        public static Mesh Triangle(Vector2 a, Vector2 b, Vector2 cc, Color c)
        {
            return Build(new List<Vector3> { a, b, cc }, new List<int> { 0, 2, 1 }, c);
        }

        public static Mesh Rect(Vector2 min, Vector2 max, Color c)
        {
            return Polygon(new[] { min, new Vector2(max.x, min.y), max, new Vector2(min.x, max.y) }, c);
        }

        private static Mesh Build(List<Vector3> v, List<int> t, Color c, Mesh reuse = null)
        {
            var m = reuse != null ? reuse : new Mesh();
            m.Clear();
            m.SetVertices(v);
            var cols = new Color[v.Count];
            for (var k = 0; k < cols.Length; k++) cols[k] = c;
            m.colors = cols;
            // make every triangle face the camera, whatever order it was built in
            for (var k = 0; k + 2 < t.Count; k += 3)
            {
                Vector3 a = v[t[k]], b = v[t[k + 1]], d = v[t[k + 2]];
                if ((b.x - a.x) * (d.y - a.y) - (b.y - a.y) * (d.x - a.x) > 0f) { var tmp = t[k + 1]; t[k + 1] = t[k + 2]; t[k + 2] = tmp; }
            }
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Ear clipping: splits a simple polygon into triangles.</summary>
        private static List<int> Triangulate(IList<Vector2> pts)
        {
            var result = new List<int>();
            var idx = new List<int>();
            for (var k = 0; k < pts.Count; k++) idx.Add(k);
            var area = 0f;
            for (var k = 0; k < pts.Count; k++) { var a = pts[k]; var b = pts[(k + 1) % pts.Count]; area += a.x * b.y - b.x * a.y; }
            var ccw = area > 0f;
            var guard = 0;
            while (idx.Count > 3 && guard++ < 10000)
            {
                var clipped = false;
                for (var k = 0; k < idx.Count; k++)
                {
                    int i0 = idx[(k + idx.Count - 1) % idx.Count], i1 = idx[k], i2 = idx[(k + 1) % idx.Count];
                    Vector2 a = pts[i0], b = pts[i1], c = pts[i2];
                    var cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                    if (ccw ? cross <= 0f : cross >= 0f) continue; // not convex here
                    var inside = false;
                    foreach (var j in idx)
                    {
                        if (j == i0 || j == i1 || j == i2) continue;
                        if (InTri(pts[j], a, b, c)) { inside = true; break; }
                    }
                    if (inside) continue;
                    result.Add(i0); result.Add(i1); result.Add(i2);
                    idx.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped) break; // not a simple polygon; draw what we have
            }
            if (idx.Count == 3) { result.Add(idx[0]); result.Add(idx[1]); result.Add(idx[2]); }
            return result;
        }

        private static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            var neg = d1 < 0 || d2 < 0 || d3 < 0;
            var pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
    }
}
