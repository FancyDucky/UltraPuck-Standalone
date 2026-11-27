using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UltraPuck.Core
{
    /// <summary>
    /// A rink made in the game's rink maker: a grid of squares (one game unit each) painted with ice, two nets and the
    /// puck's spot. <see cref="ToJson"/> turns it into an ordinary map file (the walls traced round the ice with rounded
    /// corners, the nets' walls and goals, spawns and, on a bent rink, a route for the bots), with the grid itself kept
    /// in an "editor" block so it can be opened and changed again. Only the biggest connected patch of ice counts;
    /// squares left empty inside it become pillars.
    /// </summary>
    public sealed class RinkPlan
    {
        /// <summary>The grid: <see cref="W"/> by <see cref="H"/> squares, centred on (0, 0); square (cx, cy) covers x cx..cx+1, y cy..cy+1.</summary>
        public const int W = 100, H = 56;
        public const int MinX = -W / 2, MaxX = W / 2 - 1, MinY = -H / 2, MaxY = H / 2 - 1;
        /// <summary>How round the traced corners are (game units, at most).</summary>
        public const double CornerRadius = 2.5;

        public string Name = "My Rink", Author = "";
        private readonly bool[] _ice = new bool[W * H];
        // wall lines along the grid: across (x..x+1 at height y, y from MinY to MaxY + 1) and up (y..y+1 at x, x from MinX to MaxX + 1)
        private readonly bool[] _across = new bool[W * (H + 1)], _up = new bool[(W + 1) * H];

        /// <summary>
        /// A net: the middle of its mouth on the grid corner (<see cref="X"/>, <see cref="Y"/>), facing <see cref="Dir"/>
        /// (0 +x, 1 +y, 2 -x, 3 -y), the net behind it.
        /// </summary>
        public sealed class Net
        {
            public bool Placed;
            public int X, Y, Dir;
            public Net Copy() { return new Net { Placed = Placed, X = X, Y = Y, Dir = Dir }; }
        }

        /// <summary>The nets: [0] red's own (team 0 defends it), [1] blue's.</summary>
        public readonly Net[] Nets = { new Net(), new Net() };
        /// <summary>The puck's spot, on a grid corner, if placed (else half way between the nets).</summary>
        public bool SpotPlaced;
        public int SpotX, SpotY;

        // the net's size (as the built-in rinks' nets)
        private const double NetHalfWidth = 2.085, NetDepth = 2.368, GoalHalfWidth = 1.745, GoalDepth = 2.028;

        // ------------------------------------------------------------------ the grid

        public static bool InGrid(int cx, int cy) { return cx >= MinX && cx <= MaxX && cy >= MinY && cy <= MaxY; }
        private static int Index(int cx, int cy) { return (cy - MinY) * W + (cx - MinX); }

        public bool Ice(int cx, int cy) { return InGrid(cx, cy) && _ice[Index(cx, cy)]; }
        public void SetIce(int cx, int cy, bool on) { if (InGrid(cx, cy)) _ice[Index(cx, cy)] = on; }

        /// <summary>The square on the other side of the middle line (x = 0).</summary>
        public static int MirrorX(int cx) { return -cx - 1; }

        // ------------------------------------------------------------------ wall lines

        /// <summary>
        /// A wall line on one grid edge: <paramref name="up"/> false, across from corner (x, y) to (x + 1, y); true, up
        /// from (x, y) to (x, y + 1). Walls only count between two squares of ice that count (see <see cref="WallCounts"/>).
        /// </summary>
        public static bool EdgeInGrid(int x, int y, bool up)
        {
            return up ? x >= MinX && x <= MaxX + 1 && y >= MinY && y <= MaxY : x >= MinX && x <= MaxX && y >= MinY && y <= MaxY + 1;
        }

        private static int EdgeIndex(int x, int y, bool up) { return up ? (y - MinY) * (W + 1) + (x - MinX) : (y - MinY) * W + (x - MinX); }

        public bool Wall(int x, int y, bool up) { return EdgeInGrid(x, y, up) && (up ? _up : _across)[EdgeIndex(x, y, up)]; }
        public void SetWall(int x, int y, bool up, bool on) { if (EdgeInGrid(x, y, up)) (up ? _up : _across)[EdgeIndex(x, y, up)] = on; }

        /// <summary>The two squares either side of an edge: below / above it (across), left / right of it (up).</summary>
        public static int[][] EdgeSides(int x, int y, bool up)
        {
            return up ? new[] { new[] { x - 1, y }, new[] { x, y } } : new[] { new[] { x, y - 1 }, new[] { x, y } };
        }

        /// <summary>The same edge on the other side of the middle line.</summary>
        public static int[] MirrorEdge(int x, int y, bool up) { return up ? new[] { -x, y } : new[] { -x - 1, y }; }

        /// <summary>Can a wall go on this edge: ice both sides, and not through a net's squares?</summary>
        public bool WallFits(int x, int y, bool up)
        {
            if (!EdgeInGrid(x, y, up)) return false;
            foreach (var s in EdgeSides(x, y, up)) if (!Ice(s[0], s[1]) || UnderNet(s[0], s[1])) return false;
            return true;
        }

        /// <summary>A wall that's part of the rink: between two squares of ice that count.</summary>
        public bool WallCounts(bool[] kept, int x, int y, bool up)
        {
            if (!Wall(x, y, up)) return false;
            foreach (var s in EdgeSides(x, y, up)) if (!At(kept, s[0], s[1])) return false;
            return true;
        }

        /// <summary>Any wall on the four edges of a square.</summary>
        private bool Walled(int cx, int cy) { return Wall(cx, cy, false) || Wall(cx, cy + 1, false) || Wall(cx, cy, true) || Wall(cx + 1, cy, true); }

        /// <summary>The wall lines that count, joined into straight runs: each [x0, y0, x1, y1].</summary>
        public List<double[]> WallRuns(bool[] kept)
        {
            var runs = new List<double[]>();
            for (var y = MinY; y <= MaxY + 1; y++)
                for (var x = MinX; x <= MaxX; x++)
                {
                    if (!WallCounts(kept, x, y, false) || WallCounts(kept, x - 1, y, false)) continue;
                    var end = x;
                    while (WallCounts(kept, end + 1, y, false)) end++;
                    runs.Add(new double[] { x, y, end + 1, y });
                }
            for (var x = MinX; x <= MaxX + 1; x++)
                for (var y = MinY; y <= MaxY; y++)
                {
                    if (!WallCounts(kept, x, y, true) || WallCounts(kept, x, y - 1, true)) continue;
                    var end = y;
                    while (WallCounts(kept, x, end + 1, true)) end++;
                    runs.Add(new double[] { x, y, x, end + 1 });
                }
            return runs;
        }

        /// <summary>Can a skater get from square a to the next square b (side by side) without a wall in the way?</summary>
        private bool Passable(bool[] kept, int ax, int ay, int bx, int by)
        {
            if (bx != ax) return !WallCounts(kept, Math.Max(ax, bx), ay, true);
            return !WallCounts(kept, ax, Math.Max(ay, by), false);
        }

        public RinkPlan Copy()
        {
            var p = new RinkPlan { Name = Name, Author = Author, SpotPlaced = SpotPlaced, SpotX = SpotX, SpotY = SpotY };
            Array.Copy(_ice, p._ice, _ice.Length);
            Array.Copy(_across, p._across, _across.Length);
            Array.Copy(_up, p._up, _up.Length);
            for (var t = 0; t < 2; t++) p.Nets[t] = Nets[t].Copy();
            return p;
        }

        public bool SameAs(RinkPlan o)
        {
            if (o == null || SpotPlaced != o.SpotPlaced || SpotX != o.SpotX || SpotY != o.SpotY) return false;
            for (var t = 0; t < 2; t++)
            {
                Net a = Nets[t], b = o.Nets[t];
                if (a.Placed != b.Placed || (a.Placed && (a.X != b.X || a.Y != b.Y || a.Dir != b.Dir))) return false;
            }
            for (var k = 0; k < _ice.Length; k++) if (_ice[k] != o._ice[k]) return false;
            for (var k = 0; k < _across.Length; k++) if (_across[k] != o._across[k]) return false;
            for (var k = 0; k < _up.Length; k++) if (_up[k] != o._up[k]) return false;
            return true;
        }

        /// <summary>A new rink to start from: a rounded box of ice about the size of the Hockey Rink, nets at the ends.</summary>
        public static RinkPlan Starter()
        {
            var p = new RinkPlan();
            const int hw = 30, hh = 13, cut = 4;
            for (var cy = -hh; cy < hh; cy++)
                for (var cx = -hw; cx < hw; cx++)
                {
                    // cut the corners off in steps, so they trace round
                    int dx = cx < 0 ? -hw - cx - 1 + cut : cx - hw + cut, dy = cy < 0 ? -hh - cy - 1 + cut : cy - hh + cut;
                    if (dx > 0 && dy > 0 && dx + dy > cut) continue;
                    p.SetIce(cx, cy, true);
                }
            p.Nets[0] = new Net { Placed = true, X = -hw + 7, Y = 0, Dir = 0 };
            p.Nets[1] = new Net { Placed = true, X = hw - 7, Y = 0, Dir = 2 };
            return p;
        }

        // ------------------------------------------------------------------ nets

        /// <summary>The net facing the other way across the middle line (the mirror of <paramref name="n"/>).</summary>
        public static Net Mirror(Net n)
        {
            var dir = n.Dir == 0 ? 2 : n.Dir == 2 ? 0 : n.Dir;
            return new Net { Placed = n.Placed, X = -n.X, Y = n.Y, Dir = dir };
        }

        /// <summary>The squares a net stands on (three deep behind the mouth, six wide) and the row in front of its mouth.</summary>
        public static List<int[]> NetCells(Net n)
        {
            var list = new List<int[]>();
            for (var depth = -1; depth < 3; depth++) // -1: the row in front of the mouth
                for (var across = -3; across <= 2; across++)
                {
                    int cx, cy;
                    switch (n.Dir)
                    {
                        case 0: cx = n.X - 1 - depth; cy = n.Y + across; break;
                        case 2: cx = n.X + depth; cy = n.Y + across; break;
                        case 1: cx = n.X + across; cy = n.Y - 1 - depth; break;
                        default: cx = n.X + across; cy = n.Y + depth; break;
                    }
                    list.Add(new[] { cx, cy });
                }
            return list;
        }

        /// <summary>Does a placed net cover this square (or the row in front of its mouth)? Painting can't take that ice away.</summary>
        public bool UnderNet(int cx, int cy)
        {
            foreach (var n in Nets)
            {
                if (!n.Placed) continue;
                foreach (var c in NetCells(n)) if (c[0] == cx && c[1] == cy) return true;
            }
            return false;
        }

        /// <summary>Would net <paramref name="n"/> fit: all its squares on ice, none under the other net (<paramref name="other"/>, if placed)?</summary>
        public bool NetFits(Net n, Net other)
        {
            var cells = NetCells(n);
            foreach (var c in cells) if (!Ice(c[0], c[1]) || Walled(c[0], c[1])) return false;
            if (other != null && other.Placed)
            {
                var theirs = NetCells(other);
                foreach (var c in cells) foreach (var d in theirs) if (c[0] == d[0] && c[1] == d[1]) return false;
            }
            return true;
        }

        private static double[] Facing(int dir) { return dir == 0 ? new[] { 1.0, 0.0 } : dir == 1 ? new[] { 0.0, 1.0 } : dir == 2 ? new[] { -1.0, 0.0 } : new[] { 0.0, -1.0 }; }

        /// <summary>The middle of a net's mouth (game units).</summary>
        public static double[] Mouth(Net n)
        {
            return new double[] { n.X, n.Y };
        }

        /// <summary>The puck's spot: where it was put, else the grid corner half way between the nets.</summary>
        public double[] Spot()
        {
            if (SpotPlaced) return new double[] { SpotX, SpotY };
            if (Nets[0].Placed && Nets[1].Placed)
            {
                double[] a = Mouth(Nets[0]), b = Mouth(Nets[1]);
                return new[] { Math.Round((a[0] + b[0]) / 2, MidpointRounding.AwayFromZero), Math.Round((a[1] + b[1]) / 2, MidpointRounding.AwayFromZero) };
            }
            return new[] { 0.0, 0.0 };
        }

        // ------------------------------------------------------------------ the ice that counts

        /// <summary>
        /// The squares that count: the biggest patch of ice joined edge to edge (another patch can't be reached, so it's
        /// left out). The others are <see cref="Stray"/>.
        /// </summary>
        public bool[] Kept()
        {
            var seen = new int[W * H];
            var bestLabel = 0;
            var bestSize = 0;
            var label = 0;
            var stack = new Stack<int>();
            for (var k = 0; k < _ice.Length; k++)
            {
                if (!_ice[k] || seen[k] != 0) continue;
                label++;
                var size = 0;
                stack.Push(k); seen[k] = label;
                while (stack.Count > 0)
                {
                    var i = stack.Pop();
                    size++;
                    int x = i % W, y = i / W;
                    if (x > 0 && _ice[i - 1] && seen[i - 1] == 0) { seen[i - 1] = label; stack.Push(i - 1); }
                    if (x < W - 1 && _ice[i + 1] && seen[i + 1] == 0) { seen[i + 1] = label; stack.Push(i + 1); }
                    if (y > 0 && _ice[i - W] && seen[i - W] == 0) { seen[i - W] = label; stack.Push(i - W); }
                    if (y < H - 1 && _ice[i + W] && seen[i + W] == 0) { seen[i + W] = label; stack.Push(i + W); }
                }
                if (size > bestSize) { bestSize = size; bestLabel = label; }
            }
            var kept = new bool[W * H];
            if (bestLabel != 0) for (var k = 0; k < kept.Length; k++) kept[k] = seen[k] == bestLabel;
            return kept;
        }

        /// <summary>Is the square ice that doesn't count (not joined to the main patch)?</summary>
        public static bool Stray(RinkPlan p, bool[] kept, int cx, int cy) { return p.Ice(cx, cy) && !kept[Index(cx, cy)]; }

        private static bool At(bool[] kept, int cx, int cy) { return InGrid(cx, cy) && kept[Index(cx, cy)]; }

        // ------------------------------------------------------------------ the walls

        /// <summary>
        /// The walls round the ice that counts: the outline first (anticlockwise), then any pillars (clockwise), each a
        /// closed list of points with rounded corners. Empty with no ice.
        /// </summary>
        public static List<List<double[]>> Outline(bool[] kept)
        {
            // every edge between a square that counts and one that doesn't, with the ice on its left
            var edges = new Dictionary<long, List<int>>(); // from corner → directions (0 +x, 1 +y, 2 -x, 3 -y)
            Action<int, int, int> add = (x, y, d) =>
            {
                var key = Key(x, y);
                List<int> l;
                if (!edges.TryGetValue(key, out l)) edges[key] = l = new List<int>();
                l.Add(d);
            };
            for (var cy = MinY; cy <= MaxY; cy++)
                for (var cx = MinX; cx <= MaxX; cx++)
                {
                    if (!At(kept, cx, cy)) continue;
                    if (!At(kept, cx, cy - 1)) add(cx, cy, 0);
                    if (!At(kept, cx + 1, cy)) add(cx + 1, cy, 1);
                    if (!At(kept, cx, cy + 1)) add(cx + 1, cy + 1, 2);
                    if (!At(kept, cx - 1, cy)) add(cx, cy + 1, 3);
                }
            var loops = new List<List<double[]>>();
            // start each loop from its lowest-then-leftmost corner, so the result doesn't depend on dictionary order
            var starts = new List<long>(edges.Keys);
            starts.Sort((a, b) => { int ay = (int)(a >> 32), by = (int)(b >> 32); if (ay != by) return ay.CompareTo(by); return ((int)a).CompareTo((int)b); });
            foreach (var start in starts)
            {
                List<int> outs;
                while (edges.TryGetValue(start, out outs) && outs.Count > 0)
                {
                    var corners = new List<int[]>();
                    int x = (int)start, y = (int)(start >> 32);
                    var first = outs[0];
                    var dir = first;
                    outs.RemoveAt(0);
                    var guard = 0;
                    while (guard++ < W * H * 4)
                    {
                        corners.Add(new[] { x, y });
                        x += dir == 0 ? 1 : dir == 2 ? -1 : 0;
                        y += dir == 1 ? 1 : dir == 3 ? -1 : 0;
                        List<int> here;
                        edges.TryGetValue(Key(x, y), out here);
                        var back = Key(x, y) == start; // the way this loop began counts as a way out here
                        Func<int, bool> has = d => (here != null && here.Contains(d)) || (back && d == first);
                        // where two ways out meet at a corner (squares touching only at a corner), turn left: it keeps
                        // to the squares this loop goes round
                        var next = has(Left(dir)) ? Left(dir) : has(dir) ? dir : has(Right(dir)) ? Right(dir) : -1;
                        if (next < 0 || (back && next == first)) break; // round to the start: closed
                        here.Remove(next);
                        dir = next;
                    }
                    var pts = new List<double[]>();
                    // only the corners where the way turns
                    for (var k = 0; k < corners.Count; k++)
                    {
                        int[] p = corners[(k + corners.Count - 1) % corners.Count], q = corners[k], r = corners[(k + 1) % corners.Count];
                        if ((q[0] - p[0]) * (r[1] - q[1]) - (q[1] - p[1]) * (r[0] - q[0]) == 0) continue;
                        pts.Add(new double[] { q[0], q[1] });
                    }
                    if (pts.Count >= 3) loops.Add(pts);
                }
            }
            // the outline first (the loop with the most area going anticlockwise), then the pillars
            loops.Sort((a, b) => Area(b).CompareTo(Area(a)));
            var round = new List<List<double[]>>();
            foreach (var l in loops) round.Add(Round(Unstep(l)));
            return round;
        }

        /// <summary>
        /// Painted slopes come out as stairs of short steps: the corners in the middle of a staircase (short sides both
        /// ways, turning the other way from a neighbour) are dropped, so the stairs become one straight slope. Decided
        /// for every corner at once from its own surroundings, so a mirrored rink stays mirrored.
        /// </summary>
        private static List<double[]> Unstep(List<double[]> pts)
        {
            var n = pts.Count;
            if (n < 5) return pts;
            var turn = new int[n];
            for (var k = 0; k < n; k++)
            {
                double[] p = pts[(k + n - 1) % n], v = pts[k], q = pts[(k + 1) % n];
                turn[k] = Math.Sign((v[0] - p[0]) * (q[1] - v[1]) - (v[1] - p[1]) * (q[0] - v[0]));
            }
            var keep = new List<double[]>();
            for (var k = 0; k < n; k++)
            {
                double[] p = pts[(k + n - 1) % n], v = pts[k], q = pts[(k + 1) % n];
                var stair = Dist(p, v) <= 2.01 && Dist(v, q) <= 2.01 && (turn[(k + n - 1) % n] != turn[k] || turn[(k + 1) % n] != turn[k]);
                if (!stair) keep.Add(v);
            }
            return keep.Count >= 3 ? keep : pts;
        }

        private static long Key(int x, int y) { return ((long)y << 32) | (uint)x; }
        private static int Left(int d) { return (d + 1) % 4; }
        private static int Right(int d) { return (d + 3) % 4; }

        private static double Area(List<double[]> pts)
        {
            var a = 0.0;
            for (var k = 0; k < pts.Count; k++) { double[] p = pts[k], q = pts[(k + 1) % pts.Count]; a += p[0] * q[1] - q[0] * p[1]; }
            return a / 2;
        }

        /// <summary>
        /// Rounds each corner: cut back along both sides (up to <see cref="CornerRadius"/>, at most half of either side)
        /// and joined with a curve through the corner, so steps of single squares become smooth slopes.
        /// </summary>
        private static List<double[]> Round(List<double[]> pts)
        {
            var n = pts.Count;
            var outPts = new List<double[]>();
            for (var k = 0; k < n; k++)
            {
                double[] p = pts[(k + n - 1) % n], v = pts[k], q = pts[(k + 1) % n];
                double lp = Dist(p, v), lq = Dist(v, q);
                var r = Math.Min(CornerRadius, Math.Min(lp, lq) / 2);
                var a = new[] { v[0] + (p[0] - v[0]) / lp * r, v[1] + (p[1] - v[1]) / lp * r };
                var b = new[] { v[0] + (q[0] - v[0]) / lq * r, v[1] + (q[1] - v[1]) / lq * r };
                var steps = r >= 1.5 ? 6 : r >= 0.75 ? 3 : 2;
                for (var s = 0; s <= steps; s++)
                {
                    double t = (double)s / steps, u = (double)(steps - s) / steps;
                    outPts.Add(new[] { u * u * a[0] + 2 * t * u * v[0] + t * t * b[0], u * u * a[1] + 2 * t * u * v[1] + t * t * b[1] });
                }
            }
            // rounded to the map file's precision, then any repeats (where two cuts met) dropped
            var clean = new List<double[]>();
            foreach (var p in outPts)
            {
                var c = new[] { R3(p[0]), R3(p[1]) };
                if (clean.Count > 0 && Math.Abs(clean[clean.Count - 1][0] - c[0]) < 1e-9 && Math.Abs(clean[clean.Count - 1][1] - c[1]) < 1e-9) continue;
                clean.Add(c);
            }
            while (clean.Count > 1 && Math.Abs(clean[0][0] - clean[clean.Count - 1][0]) < 1e-9 && Math.Abs(clean[0][1] - clean[clean.Count - 1][1]) < 1e-9) clean.RemoveAt(clean.Count - 1);
            return clean;
        }

        private static double Dist(double[] a, double[] b) { return Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1])); }
        private static double R3(double v) { return Math.Round(v, 3, MidpointRounding.AwayFromZero); }

        // ------------------------------------------------------------------ checks

        /// <summary>What stops this rink being played (to say in the maker), or null if it's ready.</summary>
        public string Problem()
        {
            var kept = Kept();
            var any = false;
            foreach (var k in kept) if (k) { any = true; break; }
            if (!any) return "Paint some ice first";
            for (var t = 0; t < 2; t++)
            {
                var name = t == 0 ? "red" : "blue";
                var n = Nets[t];
                if (!n.Placed) return "Place the " + name + " net";
                foreach (var c in NetCells(n)) if (!At(kept, c[0], c[1])) return "The " + name + " net needs ice all round it";
            }
            foreach (var a in NetCells(Nets[0])) foreach (var b in NetCells(Nets[1])) if (a[0] == b[0] && a[1] == b[1]) return "The nets are too close together";
            for (var t = 0; t < 2; t++) foreach (var c in NetCells(Nets[t])) if (Walled(c[0], c[1])) return "A wall runs through the " + (t == 0 ? "red" : "blue") + " net";
            // the spot sits on a grid corner: the four squares round it must be ice
            var spot = Spot();
            int sx = (int)Math.Round(spot[0]), sy = (int)Math.Round(spot[1]);
            if (!At(kept, sx, sy) || !At(kept, sx - 1, sy) || !At(kept, sx, sy - 1) || !At(kept, sx - 1, sy - 1))
                return "The puck's spot has to be on the ice";
            if (Wall(sx, sy, false) || Wall(sx - 1, sy, false) || Wall(sx, sy, true) || Wall(sx, sy - 1, true)) return "The puck's spot is on a wall";
            // walls mustn't shut one net off from the other
            double[] fa = Ahead(Nets[0], 0.5), fb = Ahead(Nets[1], 0.5); // the squares just in front of the mouths
            if (Path(kept, new[] { (int)Math.Floor(fa[0]), (int)Math.Floor(fa[1]) }, new[] { (int)Math.Floor(fb[0]), (int)Math.Floor(fb[1]) }, false) == null)
                return "Walls shut one net off from the other";
            return null;
        }

        // ------------------------------------------------------------------ the map

        /// <summary>The rink as a map (for the maker's picture, even unfinished: no spawns needed), or null with no ice.</summary>
        public MapDef PreviewMap()
        {
            var kept = Kept();
            var loops = Outline(kept);
            if (loops.Count == 0) return null;
            var m = new MapDef { Name = Name };
            foreach (var l in loops) m.AddWall(ToFix(l), true);
            foreach (var r in WallRuns(kept)) m.AddWall(ToFix(new List<double[]> { new[] { r[0], r[1] }, new[] { r[2], r[3] } }), false);
            for (var t = 0; t < 2; t++)
            {
                if (!Nets[t].Placed) continue;
                m.AddWall(ToFix(NetWall(Nets[t])), false);
                m.Goals.Add(Goal(Nets[t], t));
            }
            var s = Spot();
            m.PuckSpot = new FixVec2(F(s[0]), F(s[1]));
            return m;
        }

        /// <summary>The rink as a map file (null if it can't be played yet: see <see cref="Problem"/>).</summary>
        public string ToJson()
        {
            if (Problem() != null) return null;
            var kept = Kept();
            var loops = Outline(kept);
            var b = new StringBuilder();
            b.Append("// Made in Ultra Puck's rink maker (the \"editor\" block at the end is the grid it was painted on).\n");
            b.Append("{\n");
            b.Append("  \"name\": ").Append(Quote(Name)).Append(",\n");
            b.Append("  \"author\": ").Append(Quote(Author)).Append(",\n");
            b.Append("  \"walls\": [\n");
            var walls = new List<string>();
            foreach (var l in loops) walls.Add("    { \"closed\": true, \"points\": " + Points(l) + " }");
            var inner = WallRuns(kept);
            foreach (var r in inner) walls.Add("    { \"closed\": false, \"points\": " + Points(new List<double[]> { new[] { r[0], r[1] }, new[] { r[2], r[3] } }) + " }");
            for (var t = 0; t < 2; t++) walls.Add("    { \"closed\": false, \"points\": " + Points(NetWall(Nets[t])) + " }");
            b.Append(string.Join(",\n", walls.ToArray())).Append("\n  ],\n");
            b.Append("  \"goals\": [\n");
            for (var t = 0; t < 2; t++)
            {
                var g = GoalBox(Nets[t]);
                var f = Facing(Nets[t].Dir);
                b.Append("    { \"team\": ").Append(t).Append(", \"min\": ").Append(Pt(g[0])).Append(", \"max\": ").Append(Pt(g[1])).Append(", \"facing\": ").Append(Pt(f)).Append(" }").Append(t == 0 ? ",\n" : "\n");
            }
            b.Append("  ],\n");
            var spot = Spot();
            b.Append("  \"spawns\": [\n");
            var spawns = new List<string>();
            for (var t = 0; t < 2; t++)
                foreach (var sp in Spawns(kept, t, spot))
                    spawns.Add("    { \"team\": " + t + ", \"pos\": " + Pt(new[] { sp[0], sp[1] }) + ", \"facing\": " + Pt(new[] { sp[2], sp[3] }) + " }");
            b.Append(string.Join(",\n", spawns.ToArray())).Append("\n  ],\n");
            b.Append("  \"puck\": ").Append(Pt(spot)).Append(",\n");
            // (the wall lines block the bots' sight too: each one as a two-point "loop")
            var blocking = new List<List<double[]>>(loops);
            foreach (var r in inner) blocking.Add(new List<double[]> { new[] { r[0], r[1] }, new[] { r[2], r[3] } });
            var route = Route(kept, blocking);
            if (route.Count >= 2) b.Append("  \"route\": ").Append(Points(route)).Append(",\n");
            b.Append("  \"editor\": ").Append(EditorBlock()).Append("\n");
            b.Append("}\n");
            return b.ToString();
        }

        /// <summary>
        /// What to save: the map file (<see cref="ToJson"/>), or for a rink that can't be played yet just its name and
        /// grid (marked "unfinished": the game lists it to carry on with, but can't load it as a map).
        /// </summary>
        public string ToFile()
        {
            var json = ToJson();
            if (json != null) return json;
            return "// An unfinished rink from Ultra Puck's rink maker: open it there to carry on.\n{\n  \"name\": " + Quote(Name) + ",\n  \"author\": " + Quote(Author)
                   + ",\n  \"unfinished\": true,\n  \"editor\": " + EditorBlock() + "\n}\n";
        }

        /// <summary>A net's walls: the two sides and the back, open at the mouth.</summary>
        private static List<double[]> NetWall(Net n)
        {
            double[] m = Mouth(n), f = Facing(n.Dir);
            double[] s = { -f[1], f[0] };
            return new List<double[]>
            {
                new[] { R3(m[0] + s[0] * NetHalfWidth), R3(m[1] + s[1] * NetHalfWidth) },
                new[] { R3(m[0] - f[0] * NetDepth + s[0] * NetHalfWidth), R3(m[1] - f[1] * NetDepth + s[1] * NetHalfWidth) },
                new[] { R3(m[0] - f[0] * NetDepth - s[0] * NetHalfWidth), R3(m[1] - f[1] * NetDepth - s[1] * NetHalfWidth) },
                new[] { R3(m[0] - s[0] * NetHalfWidth), R3(m[1] - s[1] * NetHalfWidth) },
            };
        }

        /// <summary>A net's goal box (min, max).</summary>
        private static double[][] GoalBox(Net n)
        {
            double[] m = Mouth(n), f = Facing(n.Dir);
            double[] s = { -f[1], f[0] };
            double ax = m[0] + s[0] * GoalHalfWidth, ay = m[1] + s[1] * GoalHalfWidth;
            double bx = m[0] - f[0] * GoalDepth - s[0] * GoalHalfWidth, by = m[1] - f[1] * GoalDepth - s[1] * GoalHalfWidth;
            return new[] { new[] { R3(Math.Min(ax, bx)), R3(Math.Min(ay, by)) }, new[] { R3(Math.Max(ax, bx)), R3(Math.Max(ay, by)) } };
        }

        private static GoalDef Goal(Net n, int team)
        {
            var g = GoalBox(n);
            var f = Facing(n.Dir);
            return new GoalDef { Team = team, Min = new FixVec2(F(g[0][0]), F(g[0][1])), Max = new FixVec2(F(g[1][0]), F(g[1][1])), Facing = new FixVec2(F(f[0]), F(f[1])), HasFacing = true };
        }

        /// <summary>
        /// Where a team starts (as the Hockey Rink: one well ahead of the net, two either side further back, one in
        /// front of the net), each moved onto the nearest clear ice if it lands off it; facing the puck's spot.
        /// Returns x, y, facing x, facing y.
        /// </summary>
        private List<double[]> Spawns(bool[] kept, int team, double[] spot)
        {
            var n = Nets[team];
            var m = Mouth(n);
            double dx = m[0] - spot[0], dy = m[1] - spot[1];
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6) { var f = Facing(n.Dir); dx = -f[0]; dy = -f[1]; len = 1; }
            double ux = dx / len, uy = dy / len; // from the spot towards this net
            var side = Math.Min(5.0, len * 0.22);
            // across the way to the net, turned the same way on both halves (so a mirrored rink's spawns mirror too)
            double px = -uy, py = ux;
            if (px * m[0] < 0 || (Math.Abs(px) < 1e-9 && py < 0)) { px = -px; py = -py; }
            var want = new[]
            {
                new[] { spot[0] + dx * 0.478, spot[1] + dy * 0.478 },
                new[] { spot[0] + dx * 0.657 + px * side, spot[1] + dy * 0.657 + py * side },
                new[] { spot[0] + dx * 0.657 - px * side, spot[1] + dy * 0.657 - py * side },
                new[] { spot[0] + dx * 0.866, spot[1] + dy * 0.866 },
            };
            var list = new List<double[]>();
            foreach (var w in want)
            {
                var at = Clear(kept, w);
                double fx = spot[0] - at[0], fy = spot[1] - at[1];
                var fl = Math.Sqrt(fx * fx + fy * fy);
                if (fl < 1e-6) { var f = Facing(n.Dir); fx = f[0]; fy = f[1]; fl = 1; }
                list.Add(new[] { at[0], at[1], R3(fx / fl), R3(fy / fl) });
            }
            return list;
        }

        /// <summary>The point, or the middle of the nearest square of ice (not under a net, with ice all round it) if it's off the ice.</summary>
        private double[] Clear(bool[] kept, double[] p)
        {
            int px = (int)Math.Floor(p[0]), py = (int)Math.Floor(p[1]);
            if (Open(kept, px, py)) return new[] { R3(p[0]), R3(p[1]) };
            for (var r = 1; r < Math.Max(W, H); r++)
            {
                int[] best = null;
                var bestD = double.MaxValue;
                for (var cy = py - r; cy <= py + r; cy++)
                    for (var cx = px - r; cx <= px + r; cx++)
                    {
                        if (Math.Max(Math.Abs(cx - px), Math.Abs(cy - py)) != r || !Open(kept, cx, cy)) continue;
                        double ex = cx + 0.5 - p[0], ey = cy + 0.5 - p[1], d = ex * ex + ey * ey;
                        if (d < bestD - 1e-9) { bestD = d; best = new[] { cx, cy }; }
                    }
                if (best != null) return new[] { best[0] + 0.5, best[1] + 0.5 };
            }
            return new[] { R3(p[0]), R3(p[1]) };
        }

        /// <summary>Ice that counts, not under a net, with ice that counts all round it.</summary>
        private bool Open(bool[] kept, int cx, int cy)
        {
            for (var oy = -1; oy <= 1; oy++) for (var ox = -1; ox <= 1; ox++) if (!At(kept, cx + ox, cy + oy)) return false;
            return !UnderNet(cx, cy) && !Walled(cx, cy);
        }

        /// <summary>
        /// On a rink where the nets can't see each other: a way from in front of red's net to in front of blue's,
        /// through the middle of the ice, for the bots (see <see cref="MapDef.Route"/>). Empty when they can.
        /// </summary>
        private List<double[]> Route(bool[] kept, List<List<double[]>> loops)
        {
            var route = new List<double[]>();
            double[] a = InFront(Nets[0]), b = InFront(Nets[1]);
            if (!Crosses(loops, a, b)) return route;
            // the shortest way over open squares (falling back to any ice)
            int[] from = { (int)Math.Floor(a[0]), (int)Math.Floor(a[1]) }, to = { (int)Math.Floor(b[0]), (int)Math.Floor(b[1]) };
            var path = Path(kept, from, to, true) ?? Path(kept, from, to, false);
            if (path == null) return route;
            // pulled tight: from each point, on to the furthest one still in plain sight
            route.Add(new[] { R3(a[0]), R3(a[1]) });
            var here = a;
            var k = 0;
            while (k < path.Count - 1)
            {
                var far = k + 1;
                for (var j = path.Count - 1; j > k + 1; j--)
                    if (!Crosses(loops, here, Mid(path[j]), 1.0)) { far = j; break; }
                here = Mid(path[far]);
                route.Add(new[] { R3(here[0]), R3(here[1]) });
                k = far;
            }
            route.Add(new[] { R3(b[0]), R3(b[1]) });
            return route;
        }

        private static double[] Mid(int[] c) { return new[] { c[0] + 0.5, c[1] + 0.5 }; }

        private static double[] Ahead(Net n, double by)
        {
            double[] m = Mouth(n), f = Facing(n.Dir);
            return new[] { m[0] + f[0] * by, m[1] + f[1] * by };
        }

        private static double[] InFront(Net n)
        {
            double[] m = Mouth(n), f = Facing(n.Dir);
            return new[] { m[0] + f[0] * 1.5, m[1] + f[1] * 1.5 };
        }

        private List<int[]> Path(bool[] kept, int[] from, int[] to, bool open)
        {
            Func<int, int, bool> ok = (x, y) => open ? Open(kept, x, y) || (x == from[0] && y == from[1]) || (x == to[0] && y == to[1]) : At(kept, x, y);
            if (!ok(from[0], from[1]) || !ok(to[0], to[1])) return null;
            var prev = new Dictionary<long, long>();
            var q = new Queue<int[]>();
            q.Enqueue(from);
            prev[Key(from[0], from[1])] = long.MinValue;
            int[][] steps = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } };
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                if (c[0] == to[0] && c[1] == to[1])
                {
                    var path = new List<int[]>();
                    var key = Key(c[0], c[1]);
                    while (key != long.MinValue) { path.Add(new[] { (int)key, (int)(key >> 32) }); key = prev[key]; }
                    path.Reverse();
                    return path;
                }
                foreach (var s in steps)
                {
                    int nx = c[0] + s[0], ny = c[1] + s[1];
                    var key = Key(nx, ny);
                    if (prev.ContainsKey(key) || !ok(nx, ny) || !Passable(kept, c[0], c[1], nx, ny)) continue;
                    prev[key] = Key(c[0], c[1]);
                    q.Enqueue(new[] { nx, ny });
                }
            }
            return null;
        }

        /// <summary>Does the line from <paramref name="a"/> to <paramref name="b"/> cross a wall (or pass within <paramref name="room"/> of one)?</summary>
        private static bool Crosses(List<List<double[]>> loops, double[] a, double[] b, double room = 0)
        {
            foreach (var l in loops)
                for (var k = 0; k < l.Count; k++)
                {
                    double[] p = l[k], q = l[(k + 1) % l.Count];
                    if (SegDist(a, b, p, q) <= room) return true;
                }
            return false;
        }

        private static double SegDist(double[] a, double[] b, double[] p, double[] q)
        {
            // crossing: 0
            double d1 = Cross(p, q, a), d2 = Cross(p, q, b), d3 = Cross(a, b, p), d4 = Cross(a, b, q);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return 0;
            return Math.Min(Math.Min(PointSeg(a, p, q), PointSeg(b, p, q)), Math.Min(PointSeg(p, a, b), PointSeg(q, a, b)));
        }

        private static double Cross(double[] o, double[] a, double[] b) { return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]); }

        private static double PointSeg(double[] p, double[] a, double[] b)
        {
            double ex = b[0] - a[0], ey = b[1] - a[1], l2 = ex * ex + ey * ey;
            var t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((p[0] - a[0]) * ex + (p[1] - a[1]) * ey) / l2));
            double x = a[0] + ex * t - p[0], y = a[1] + ey * t - p[1];
            return Math.Sqrt(x * x + y * y);
        }

        // ------------------------------------------------------------------ the file

        /// <summary>The grid, nets and spot, to open the rink in the maker again.</summary>
        private string EditorBlock()
        {
            var b = new StringBuilder();
            b.Append("{ \"version\": 1, \"w\": ").Append(W).Append(", \"h\": ").Append(H).Append(", \"ice\": \"").Append(Runs(_ice)).Append("\", \"nets\": [");
            for (var t = 0; t < 2; t++)
            {
                var n = Nets[t];
                b.Append("{ \"team\": ").Append(t).Append(", \"placed\": ").Append(n.Placed ? "true" : "false").Append(", \"x\": ").Append(n.X).Append(", \"y\": ").Append(n.Y).Append(", \"dir\": ").Append(n.Dir).Append(" }").Append(t == 0 ? ", " : "");
            }
            b.Append("]");
            if (SpotPlaced) b.Append(", \"spot\": [").Append(SpotX).Append(", ").Append(SpotY).Append("]");
            if (Array.IndexOf(_across, true) >= 0 || Array.IndexOf(_up, true) >= 0)
                b.Append(", \"across\": \"").Append(Runs(_across)).Append("\", \"up\": \"").Append(Runs(_up)).Append("\"");
            b.Append(" }");
            return b.ToString();
        }

        /// <summary>A grid as run lengths, row by row from the bottom: off, on, off, on...</summary>
        private static string Runs(bool[] cells)
        {
            var runs = new List<string>();
            var on = false;
            var count = 0;
            foreach (var c in cells)
            {
                if (c == on) { count++; continue; }
                runs.Add(count.ToString(CultureInfo.InvariantCulture));
                on = c; count = 1;
            }
            runs.Add(count.ToString(CultureInfo.InvariantCulture));
            return string.Join(" ", runs.ToArray());
        }

        /// <summary>Fills <paramref name="cells"/> from <see cref="Runs"/>; false if the text isn't run lengths.</summary>
        private static bool Unrun(string text, bool[] cells)
        {
            var at = 0;
            var on = false;
            foreach (var r in (text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int n;
                if (!int.TryParse(r, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return false;
                for (var k = 0; k < n && at < cells.Length; k++) cells[at++] = on;
                on = !on;
            }
            return true;
        }

        /// <summary>A rink from a map file the maker saved (null if the file has no grid to edit).</summary>
        public static RinkPlan FromJson(string json)
        {
            var root = Json.Parse(json) as Dictionary<string, object>;
            if (root == null) return null;
            var ed = Json.Get(root, "editor") as Dictionary<string, object>;
            if (ed == null) return null;
            if (Json.Int(Json.Get(ed, "w"), "editor.w") != W || Json.Int(Json.Get(ed, "h"), "editor.h") != H) return null;
            var p = new RinkPlan
            {
                Name = Json.Str(Json.Get(root, "name"), "My Rink"),
                Author = Json.Str(Json.Get(root, "author"), ""),
            };
            if (!Unrun(Json.Str(Json.Get(ed, "ice"), ""), p._ice)) return null;
            if (!Unrun(Json.Str(Json.Get(ed, "across"), ""), p._across) || !Unrun(Json.Str(Json.Get(ed, "up"), ""), p._up)) return null;
            var nets = Json.Get(ed, "nets") as List<object>;
            if (nets != null)
                foreach (var o in nets)
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    var t = Json.Int(Json.Get(d, "team"), "editor.nets.team");
                    if (t < 0 || t > 1) continue;
                    p.Nets[t] = new Net
                    {
                        Placed = Json.Get(d, "placed") is bool && (bool)Json.Get(d, "placed"),
                        X = Json.Int(Json.Get(d, "x"), "editor.nets.x"),
                        Y = Json.Int(Json.Get(d, "y"), "editor.nets.y"),
                        Dir = ((Json.Int(Json.Get(d, "dir"), "editor.nets.dir") % 4) + 4) % 4,
                    };
                }
            var spot = Json.Get(ed, "spot") as List<object>;
            if (spot != null && spot.Count == 2)
            {
                p.SpotPlaced = true;
                p.SpotX = Json.Int(spot[0], "editor.spot");
                p.SpotY = Json.Int(spot[1], "editor.spot");
            }
            return p;
        }

        private static string Quote(string s)
        {
            var b = new StringBuilder("\"");
            foreach (var c in s ?? "")
            {
                if (c == '"' || c == '\\') b.Append('\\').Append(c);
                else if (c < ' ') b.Append(' ');
                else b.Append(c);
            }
            return b.Append('"').ToString();
        }

        private static string Num(double v) { return R3(v).ToString("0.###", CultureInfo.InvariantCulture); }
        private static string Pt(double[] p) { return "[" + Num(p[0]) + ", " + Num(p[1]) + "]"; }
        private static string Points(List<double[]> pts)
        {
            var parts = new string[pts.Count];
            for (var k = 0; k < pts.Count; k++) parts[k] = Pt(pts[k]);
            return "[" + string.Join(", ", parts) + "]";
        }

        private static Fix F(double v) { return Fix.FromDecimal((decimal)R3(v)); }
        private static FixVec2[] ToFix(List<double[]> pts)
        {
            var a = new FixVec2[pts.Count];
            for (var k = 0; k < pts.Count; k++) a[k] = new FixVec2(F(pts[k][0]), F(pts[k][1]));
            return a;
        }
    }
}
