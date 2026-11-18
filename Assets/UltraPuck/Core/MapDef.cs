using System;
using System.Collections.Generic;

namespace UltraPuck.Core
{
    /// <summary>
    /// One straight piece of wall. Curves are made of several short ones. The ends are stored in a fixed order
    /// (nearer the middle line first), so a wall and its mirror image are worked out with exactly mirrored maths.
    /// <see cref="Side"/> is a unit vector off the side the wall was drawn with (left of A→B as written in the map),
    /// used only when something sits exactly on the line.
    /// </summary>
    public struct WallSegment
    {
        public FixVec2 A, B, Side;

        public WallSegment(FixVec2 a, FixVec2 b)
        {
            var ab = b - a;
            Side = new FixVec2(-ab.Y, ab.X).Normalized;
            if (Before(b, a)) { A = b; B = a; } else { A = a; B = b; }
        }

        /// <summary>Order by distance from the middle line, then height, then side: the same for a point and its mirror.</summary>
        private static bool Before(FixVec2 p, FixVec2 q)
        {
            long ap = Math.Abs(p.X.Raw), aq = Math.Abs(q.X.Raw);
            if (ap != aq) return ap < aq;
            if (p.Y.Raw != q.Y.Raw) return p.Y.Raw < q.Y.Raw;
            return p.X.Raw < q.X.Raw;
        }

        /// <summary>
        /// Sorting key for the list of walls, so walls are always checked in an order that is the same on both halves
        /// of a mirrored map (by distance of the middle from the centre line, then height).
        /// </summary>
        internal static int Compare(WallSegment s, WallSegment t)
        {
            long sx = Math.Abs(s.A.X.Raw + s.B.X.Raw), tx = Math.Abs(t.A.X.Raw + t.B.X.Raw);
            if (sx != tx) return sx < tx ? -1 : 1;
            long sy = s.A.Y.Raw + s.B.Y.Raw, ty = t.A.Y.Raw + t.B.Y.Raw;
            if (sy != ty) return sy < ty ? -1 : 1;
            long sa = s.A.X.Raw + s.B.X.Raw, ta = t.A.X.Raw + t.B.X.Raw;
            if (sa != ta) return sa < ta ? -1 : 1;
            long dy = (s.B.Y.Raw - s.A.Y.Raw) - (t.B.Y.Raw - t.A.Y.Raw);
            return dy < 0 ? -1 : dy > 0 ? 1 : 0;
        }
    }

    /// <summary>A goal: the puck touching the box scores against <see cref="Team"/>.</summary>
    public sealed class GoalDef
    {
        public int Team;
        public FixVec2 Min, Max;
        /// <summary>Which way the net's mouth opens (towards the ice), if the map says; bots skate round to that side to shoot.</summary>
        public FixVec2 Facing;
        public bool HasFacing;
        public bool Contains(FixVec2 p) { return p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y; }

        /// <summary>Whether a circle (the puck) at <paramref name="p"/> with radius <paramref name="r"/> touches the box.</summary>
        public bool Touches(FixVec2 p, Fix r)
        {
            var near = new FixVec2(Fix.Clamp(p.X, Min.X, Max.X), Fix.Clamp(p.Y, Min.Y, Max.Y));
            return (p - near).SqrLength <= r * r;
        }
    }

    public sealed class SpawnDef
    {
        public int Team;
        public FixVec2 Pos;
        public FixVec2 Facing;
    }

    /// <summary>
    /// A map, read from a .json file (see Assets/StreamingAssets/Maps). The future map editor writes the same format.
    /// Walls are lists of points joined by straight lines ("closed": true joins the last point back to the first);
    /// the floor is the area inside the first wall loop.
    /// </summary>
    public sealed class MapDef
    {
        public string Name = "Untitled";
        public string Author = "";
        /// <summary>Each wall as its points (for drawing); <see cref="Segments"/> is the same walls split into pieces.</summary>
        public readonly List<FixVec2[]> WallLines = new List<FixVec2[]>();
        public readonly List<bool> WallClosed = new List<bool>();
        public readonly List<WallSegment> Segments = new List<WallSegment>();
        public readonly List<GoalDef> Goals = new List<GoalDef>();
        public readonly List<SpawnDef> Spawns = new List<SpawnDef>();
        public FixVec2 PuckSpot = FixVec2.Zero;
        /// <summary>
        /// Points down the middle of the ice from team 0's goal to team 1's, for maps where the goals can't be seen
        /// from everywhere (an S, a U): bots follow it when a wall is in the way. Empty on open rinks.
        /// </summary>
        public readonly List<FixVec2> Route = new List<FixVec2>();
        /// <summary>Area that holds the whole map (for the camera).</summary>
        public FixVec2 BoundsMin, BoundsMax;
        private bool _hasBounds;

        public static MapDef FromJson(string json)
        {
            var root = Json.Obj(Json.Parse(json), "map");
            var m = new MapDef();
            m.Name = Json.Str(Json.Get(root, "name"), m.Name);
            m.Author = Json.Str(Json.Get(root, "author"), m.Author);

            var walls = Json.Arr(Json.Get(root, "walls"), "walls");
            for (var w = 0; w < walls.Count; w++)
            {
                var what = "walls[" + w + "]";
                var wo = Json.Obj(walls[w], what);
                var pts = Json.Arr(Json.Get(wo, "points"), what + ".points");
                if (pts.Count < 2) throw new FormatException(what + ": needs at least 2 points");
                var line = new FixVec2[pts.Count];
                for (var k = 0; k < pts.Count; k++) line[k] = Json.Vec(pts[k], what + ".points[" + k + "]");
                var closed = Json.Get(wo, "closed") is bool && (bool)Json.Get(wo, "closed");
                m.AddWall(line, closed);
            }

            var goals = Json.Arr(Json.Get(root, "goals"), "goals");
            for (var g = 0; g < goals.Count; g++)
            {
                var what = "goals[" + g + "]";
                var go = Json.Obj(goals[g], what);
                var a = Json.Vec(Json.Get(go, "min"), what + ".min");
                var b = Json.Vec(Json.Get(go, "max"), what + ".max");
                var goal = new GoalDef
                {
                    Team = Json.Int(Json.Get(go, "team"), what + ".team"),
                    Min = new FixVec2(Fix.Min(a.X, b.X), Fix.Min(a.Y, b.Y)),
                    Max = new FixVec2(Fix.Max(a.X, b.X), Fix.Max(a.Y, b.Y)),
                };
                if (Json.Get(go, "facing") != null)
                {
                    goal.Facing = Json.Vec(Json.Get(go, "facing"), what + ".facing").Normalized;
                    goal.HasFacing = goal.Facing.SqrLength.Raw > 0;
                }
                m.Goals.Add(goal);
            }

            var spawns = Json.Arr(Json.Get(root, "spawns"), "spawns");
            for (var s = 0; s < spawns.Count; s++)
            {
                var what = "spawns[" + s + "]";
                var so = Json.Obj(spawns[s], what);
                var facing = Json.Get(so, "facing") != null ? Json.Vec(Json.Get(so, "facing"), what + ".facing").Normalized : FixVec2.Right;
                m.Spawns.Add(new SpawnDef
                {
                    Team = Json.Int(Json.Get(so, "team"), what + ".team"),
                    Pos = Json.Vec(Json.Get(so, "pos"), what + ".pos"),
                    Facing = facing.SqrLength.Raw > 0 ? facing : FixVec2.Right,
                });
            }

            if (Json.Get(root, "puck") != null) m.PuckSpot = Json.Vec(Json.Get(root, "puck"), "puck");
            if (Json.Get(root, "route") != null)
            {
                var route = Json.Arr(Json.Get(root, "route"), "route");
                for (var k = 0; k < route.Count; k++) m.Route.Add(Json.Vec(route[k], "route[" + k + "]"));
            }
            m.Validate();
            return m;
        }

        /// <summary>
        /// Is <paramref name="p"/> on the floor, inside the first closed wall (the rink's outline)? Counts how many
        /// outline edges a line from the point crosses: odd means inside. A map with no closed wall has no outside.
        /// </summary>
        public bool Inside(FixVec2 p)
        {
            FixVec2[] outline = null;
            for (var w = 0; w < WallLines.Count; w++) if (WallClosed[w]) { outline = WallLines[w]; break; }
            if (outline == null) return true;
            var inside = false;
            for (int k = 0, j = outline.Length - 1; k < outline.Length; j = k++)
            {
                FixVec2 a = outline[k], b = outline[j];
                if ((a.Y > p.Y) == (b.Y > p.Y)) continue;
                var x = a.X + (p.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                if (p.X < x) inside = !inside;
            }
            return inside;
        }

        public void AddWall(FixVec2[] line, bool closed)
        {
            WallLines.Add(line);
            WallClosed.Add(closed);
            for (var k = 0; k + 1 < line.Length; k++) Segments.Add(new WallSegment(line[k], line[k + 1]));
            if (closed && line.Length > 2) Segments.Add(new WallSegment(line[line.Length - 1], line[0]));
            Segments.Sort(WallSegment.Compare);
            foreach (var p in line)
            {
                if (!_hasBounds) { BoundsMin = p; BoundsMax = p; _hasBounds = true; }
                BoundsMin = new FixVec2(Fix.Min(BoundsMin.X, p.X), Fix.Min(BoundsMin.Y, p.Y));
                BoundsMax = new FixVec2(Fix.Max(BoundsMax.X, p.X), Fix.Max(BoundsMax.Y, p.Y));
            }
        }

        private void Validate()
        {
            if (Segments.Count == 0) throw new FormatException("map: no walls");
            for (var t = 0; t < 2; t++)
            {
                if (!Goals.Exists(g => g.Team == t)) throw new FormatException("map: no goal for team " + t);
                if (!Spawns.Exists(s => s.Team == t)) throw new FormatException("map: no spawn for team " + t);
            }
        }

        /// <summary>Does the straight line from <paramref name="a"/> to <paramref name="b"/> cross a wall?</summary>
        public bool Blocked(FixVec2 a, FixVec2 b)
        {
            var d = b - a;
            foreach (var w in Segments)
            {
                var e = w.B - w.A;
                var den = FixVec2.Cross(d, e);
                if (den.Raw == 0) continue;
                var t = FixVec2.Cross(w.A - a, e) / den;
                var u = FixVec2.Cross(w.A - a, d) / den;
                if (t >= Fix.Zero && t <= Fix.One && u >= Fix.Zero && u <= Fix.One) return true;
            }
            return false;
        }

        /// <summary>The route point nearest <paramref name="p"/> (its index), or -1 with no route.</summary>
        public int NearestOnRoute(FixVec2 p)
        {
            var best = -1;
            var bestD = Fix.Zero;
            for (var k = 0; k < Route.Count; k++)
            {
                var dd = (Route[k] - p).SqrLength;
                if (best < 0 || dd < bestD) { best = k; bestD = dd; }
            }
            return best;
        }

        /// <summary>Where the <paramref name="index"/>-th player of a team starts (extra players line up behind the last spawn).</summary>
        public SpawnDef SpawnFor(int team, int index)
        {
            var list = Spawns.FindAll(s => s.Team == team);
            if (index < list.Count) return list[index];
            var last = list[list.Count - 1];
            var extra = index - list.Count + 1;
            var side = new FixVec2(-last.Facing.Y, last.Facing.X);
            return new SpawnDef { Team = team, Facing = last.Facing, Pos = last.Pos + side * Fix.FromInt(((extra & 1) == 1 ? 1 : -1) * ((extra + 1) / 2) * 2) };
        }
    }
}
