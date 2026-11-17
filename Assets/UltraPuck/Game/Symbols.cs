using System.Collections.Generic;
using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// The player symbols, drawn on each player's body (and on the join screen): eight simple shapes, so players on the
    /// same team can still be told apart, and nobody depends on colour alone.
    /// </summary>
    internal static class Symbols
    {
        public static readonly string[] Names = { "Triangle", "Square", "Circle", "Diamond", "Star", "Cross", "Hexagon", "Heart" };
        public const int Count = 8;

        /// <summary>The shape's outline, about 1 across (centred on 0,0, y up).</summary>
        public static List<Vector2> Outline(int symbol)
        {
            var pts = new List<Vector2>();
            switch (symbol & 7)
            {
                case 0: pts.Add(new Vector2(0f, 0.58f)); pts.Add(new Vector2(0.5f, -0.38f)); pts.Add(new Vector2(-0.5f, -0.38f)); break;
                case 1: pts.Add(new Vector2(-0.4f, -0.4f)); pts.Add(new Vector2(0.4f, -0.4f)); pts.Add(new Vector2(0.4f, 0.4f)); pts.Add(new Vector2(-0.4f, 0.4f)); break;
                case 2: for (var k = 0; k < 28; k++) { var a = k * Mathf.PI * 2f / 28f; pts.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.46f); } break;
                case 3: pts.Add(new Vector2(0f, 0.55f)); pts.Add(new Vector2(0.42f, 0f)); pts.Add(new Vector2(0f, -0.55f)); pts.Add(new Vector2(-0.42f, 0f)); break;
                case 4:
                    for (var k = 0; k < 10; k++)
                    {
                        var a = Mathf.PI / 2f + k * Mathf.PI / 5f;
                        var r = k % 2 == 0 ? 0.55f : 0.23f;
                        pts.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                    }
                    break;
                case 5:
                    {
                        const float w = 0.16f, l = 0.48f;
                        pts.AddRange(new[]
                        {
                            new Vector2(-w, l), new Vector2(w, l), new Vector2(w, w), new Vector2(l, w), new Vector2(l, -w), new Vector2(w, -w),
                            new Vector2(w, -l), new Vector2(-w, -l), new Vector2(-w, -w), new Vector2(-l, -w), new Vector2(-l, w), new Vector2(-w, w),
                        });
                        break;
                    }
                case 6: for (var k = 0; k < 6; k++) { var a = Mathf.PI / 6f + k * Mathf.PI / 3f; pts.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.5f); } break;
                default:
                    // a heart: two lobes on top, a point at the bottom
                    for (var k = 0; k < 32; k++)
                    {
                        var t = k * Mathf.PI * 2f / 32f;
                        var x = 16f * Mathf.Pow(Mathf.Sin(t), 3f);
                        var y = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t) - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t);
                        pts.Add(new Vector2(x, y + 2f) * 0.031f);
                    }
                    pts.Reverse();
                    break;
            }
            return pts;
        }

        /// <summary>The shape as a flat mesh, <paramref name="size"/> across.</summary>
        public static Mesh Mesh(int symbol, float size, Color c)
        {
            var pts = Outline(symbol);
            for (var k = 0; k < pts.Count; k++) pts[k] *= size;
            return Draw.Polygon(pts, c);
        }

        private static readonly Texture2D[] _tex = new Texture2D[Count];

        /// <summary>The shape as a white picture (tint it with GUI.color), for menus.</summary>
        public static Texture2D Texture(int symbol)
        {
            symbol &= 7;
            if (_tex[symbol] != null) return _tex[symbol];
            const int n = 64;
            var pts = Outline(symbol);
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    // 4x4 samples a pixel for smooth edges
                    var hits = 0;
                    for (var sy = 0; sy < 4; sy++)
                        for (var sx = 0; sx < 4; sx++)
                        {
                            var p = new Vector2((x + (sx + 0.5f) / 4f) / n - 0.5f, (y + (sy + 0.5f) / 4f) / n - 0.5f) * 1.15f;
                            if (Inside(pts, p)) hits++;
                        }
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(hits * 255 / 16));
                }
            tex.SetPixels32(px);
            tex.Apply();
            _tex[symbol] = tex;
            return tex;
        }

        private static bool Inside(List<Vector2> poly, Vector2 p)
        {
            var inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            }
            return inside;
        }
    }
}
