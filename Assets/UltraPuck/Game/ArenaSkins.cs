using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UltraPuck.Core;
using UltraPuck.Core.Net;
using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// A custom arena skin: a zip from the Custom_Arenas folder (next to the game) holding a Background, a Floor, a
    /// Foreground and / or a Shadows picture, plus an optional arena.json naming it and the rink it's for. Only a look: the
    /// background goes behind the rink, the floor over the ice (inside the walls, under the lines), the shadows on the ice under the players, the foreground over everything on
    /// the ice. Pictures are laid out like the rink templates (see <see cref="Canvas"/>).
    /// </summary>
    public sealed class ArenaSkin
    {
        public string Name;
        /// <summary>The map file of the rink it's for ("hockey-rink.json").</summary>
        public string Rink;
        /// <summary>The zip's file name, and its bytes (sent as they are to players who join a game hosted with it).</summary>
        public string File;
        public byte[] Bytes;
        public ulong Hash;
        private Texture2D _back, _front, _shade, _floor;
        private bool _loaded;

        public Texture2D Back { get { LoadPictures(); return _back; } }
        public Texture2D Front { get { LoadPictures(); return _front; } }
        public Texture2D Shade { get { LoadPictures(); return _shade; } }
        public Texture2D Floor { get { LoadPictures(); return _floor; } }

        /// <summary>The pictures, read from the zip the first time they're needed (a big PNG takes a moment).</summary>
        private void LoadPictures()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                using (var zip = new ZipArchive(new MemoryStream(Bytes), ZipArchiveMode.Read))
                    foreach (var e in zip.Entries)
                    {
                        var kind = KindOf(e.FullName);
                        if (kind < 0) continue;
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = Name + " " + e.Name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 2 };
                        if (!tex.LoadImage(ReadAll(e), true)) { Kill(tex); continue; }
                        if (kind == 0 && _back == null) _back = tex;
                        else if (kind == 1 && _front == null) _front = tex;
                        else if (kind == 2 && _shade == null) _shade = tex;
                        else if (kind == 3 && _floor == null) _floor = tex;
                        else Kill(tex);
                    }
            }
            catch (Exception ex) { Debug.LogWarning("Ultra Puck: couldn't read the arena " + File + ": " + ex.Message); }
        }

        /// <summary>Destroy (DestroyImmediate outside play mode, as in the tests).</summary>
        internal static void Kill(UnityEngine.Object o) { if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o); }

        /// <summary>Lets the pictures go (another arena is showing now).</summary>
        public void Unload()
        {
            if (_back != null) Kill(_back);
            if (_front != null) Kill(_front);
            if (_shade != null) Kill(_shade);
            if (_floor != null) Kill(_floor);
            _back = _front = _shade = _floor = null;
            _loaded = false;
        }

        /// <summary>0 background, 1 foreground, 2 shadows, 3 floor, -1 something else (by the picture's file name).</summary>
        private static int KindOf(string path)
        {
            var n = Path.GetFileName(path).ToLowerInvariant();
            if (n.StartsWith(".") || path.Contains("__MACOSX")) return -1;
            if (!(n.EndsWith(".png") || n.EndsWith(".jpg") || n.EndsWith(".jpeg"))) return -1;
            if (n.Contains("guide")) return -1; // the template's guide, zipped by mistake
            if (n.Contains("shadow")) return 2;
            if (n.Contains("floor")) return 3;
            if (n.Contains("foreground") || n.StartsWith("front") || n.StartsWith("fore")) return 1;
            if (n.Contains("background") || n.StartsWith("back")) return 0;
            return -1;
        }

        private static byte[] ReadAll(ZipArchiveEntry e)
        {
            using (var s = e.Open())
            using (var m = new MemoryStream())
            {
                s.CopyTo(m);
                return m.ToArray();
            }
        }

        /// <summary>
        /// An arena from a zip's bytes, or null if it has none of the four pictures. Which rink it's for: arena.json's
        /// "rink" (a map file or a rink's name), else a rink named in the zip's file name, else the Hockey Rink.
        /// </summary>
        public static ArenaSkin FromZip(byte[] bytes, string file, IList<string> mapFiles, Func<string, string> rinkName)
        {
            string name = null, rink = null;
            var pictures = 0;
            using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
                foreach (var e in zip.Entries)
                {
                    if (KindOf(e.FullName) >= 0) pictures++;
                    else if (Path.GetFileName(e.FullName).ToLowerInvariant() == "arena.json")
                    {
                        var text = System.Text.Encoding.UTF8.GetString(ReadAll(e));
                        var o = Json.Parse(text) as Dictionary<string, object>;
                        if (o != null) { name = Json.Str(Json.Get(o, "name"), null); rink = Json.Str(Json.Get(o, "rink"), null); }
                    }
                }
            if (pictures == 0) return null;
            var stem = Path.GetFileNameWithoutExtension(file);
            return new ArenaSkin
            {
                Name = string.IsNullOrEmpty(name) ? stem : name,
                Rink = MatchRink(rink, mapFiles, rinkName) ?? MatchRink(stem, mapFiles, rinkName, true) ?? (mapFiles.Contains("hockey-rink.json") ? "hockey-rink.json" : null),
                File = file,
                Bytes = bytes,
                Hash = NetSession.HashBytes(bytes),
            };
        }

        private static string Squash(string s)
        {
            var b = new System.Text.StringBuilder();
            foreach (var c in (s ?? "").ToLowerInvariant()) if (char.IsLetterOrDigit(c)) b.Append(c);
            return b.ToString();
        }

        /// <summary>The map file <paramref name="said"/> names (exactly, or as a part of it when <paramref name="within"/>).</summary>
        private static string MatchRink(string said, IList<string> mapFiles, Func<string, string> rinkName, bool within = false)
        {
            var s = Squash(said);
            if (s.Length == 0) return null;
            string best = null;
            foreach (var f in mapFiles)
            {
                foreach (var k in new[] { Squash(Path.GetFileNameWithoutExtension(f)), Squash(rinkName(f)) })
                {
                    if (k.Length == 0) continue;
                    var hit = within ? s.Contains(k) : s == k;
                    // the longest name that fits ("big hockey rink" is the Big Rink, not the Hockey Rink... whichever is longer)
                    if (hit && (best == null || k.Length > Squash(Path.GetFileNameWithoutExtension(best)).Length)) best = f;
                }
            }
            return best;
        }

        // ------------------------------------------------------------------ where the pictures go

        // the whole-rink camera: the rink plus this margin, under the score bar (as GameRunner.UpdateCamera frames it)
        private const float Margin = 1.2f, BelowBar = 0.91f, Bleed = 0.03f;

        /// <summary>
        /// The part of the world a picture covers (the rink templates' canvas): every screen shape's whole-rink view
        /// (16:9, 16:10, 21:9) put together, plus a little extra all round. A picture of another size is stretched
        /// to its width and kept centred.
        /// </summary>
        public static Rect Canvas(MapDef map)
        {
            Vector2 lo = V.Of(map.BoundsMin) - Vector2.one * Margin, hi = V.Of(map.BoundsMax) + Vector2.one * Margin;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var aspect in new[] { 16f / 9f, 16f / 10f, 21f / 9f })
            {
                float rw = hi.x - lo.x, rh = hi.y - lo.y;
                var h = Mathf.Max(rw / aspect, rh / BelowBar);
                var w = h * aspect;
                float cx = (lo.x + hi.x) / 2f, cy = (lo.y + hi.y) / 2f + h * (1f - BelowBar) / 2f;
                x0 = Mathf.Min(x0, cx - w / 2f); x1 = Mathf.Max(x1, cx + w / 2f);
                y0 = Mathf.Min(y0, cy - h / 2f); y1 = Mathf.Max(y1, cy + h / 2f);
            }
            float bw = (x1 - x0) * Bleed, bh = (y1 - y0) * Bleed;
            return Rect.MinMaxRect(x0 - bw, y0 - bh, x1 + bw, y1 + bh);
        }

        // ------------------------------------------------------------------ the folder

        /// <summary>Custom_Arenas, next to the game (the project folder in the editor).</summary>
        public static string Folder { get { return Path.Combine(Path.GetDirectoryName(Application.dataPath), "Custom_Arenas"); } }
        /// <summary>Arenas downloaded from hosts (kept so the next game with them starts straight away; not offered to pick).</summary>
        public static string Downloads { get { return Path.Combine(Folder, "_downloaded"); } }

        private const string ReadMe =
            "ULTRA PUCK - CUSTOM ARENAS\r\n\r\n" +
            "Drop arena skins here as .zip files. Each zip holds up to four pictures (PNG or JPG):\r\n" +
            "  Background.png  everything behind the rink\r\n" +
            "  Floor.png       the rink's floor in place of the ice (only inside the walls shows; the lines go on top)\r\n" +
            "  Foreground.png  things nearer the camera than the ice (transparent elsewhere; keep it off the playing area)\r\n" +
            "  Shadows.png     soft shadows on the ice under the players (transparent elsewhere)\r\n" +
            "and, if you like, arena.json saying its name and which rink it's for:\r\n" +
            "  { \"name\": \"Backyard\", \"rink\": \"hockey-rink\" }\r\n" +
            "(rink: hockey-rink, big-rink, s-rink, u-rink, backwards-rink... or the rink's name. Without it: a rink named in\r\n" +
            "the zip's file name, else the Hockey Rink.) Make the pictures from the rink templates so they line up.\r\n\r\n" +
            "In the game: on the rink screen, up / down picks the rink's look, custom arenas included. Online, the host's\r\n" +
            "arena is sent to everyone who joins (Settings > Graphics > Other players' arenas turns that off).\r\n" +
            "Arenas you got from other players are kept in _downloaded.\r\n\r\n" +
            "sample arena.json is one to copy: rename it arena.json, change its name and rink, and zip it with the pictures.\r\n";

        /// <summary>A sample arena.json (the game only reads zips, so this one is never loaded itself).</summary>
        public const string SampleJson =
            "{\r\n" +
            "  \"name\": \"My Arena\",\r\n" +
            "  \"rink\": \"hockey-rink\",\r\n" +
            "  \"rinks you can use\": [\"hockey-rink\", \"backwards-rink\", \"big-rink\", \"s-rink\", \"u-rink\"]\r\n" +
            "}\r\n";

        /// <summary>Every arena in the folder (made, with a read-me, if it isn't there yet).</summary>
        public static List<ArenaSkin> LoadAll(IList<string> mapFiles, Func<string, string> rinkName)
        {
            var list = new List<ArenaSkin>();
            try
            {
                if (!Directory.Exists(Folder)) Directory.CreateDirectory(Folder);
                var readMe = Path.Combine(Folder, "README.txt");
                if (!System.IO.File.Exists(readMe)) System.IO.File.WriteAllText(readMe, ReadMe);
                var sample = Path.Combine(Folder, "sample arena.json");
                if (!System.IO.File.Exists(sample)) System.IO.File.WriteAllText(sample, SampleJson);
            }
            catch (Exception ex) { Debug.LogWarning("Ultra Puck: couldn't make " + Folder + ": " + ex.Message); }
            if (!Directory.Exists(Folder)) return list;
            var files = Directory.GetFiles(Folder, "*.zip");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                try
                {
                    var a = FromZip(System.IO.File.ReadAllBytes(f), Path.GetFileName(f), mapFiles, rinkName);
                    if (a == null) { Debug.LogWarning("Ultra Puck: " + Path.GetFileName(f) + " has no Background, Floor, Foreground or Shadows picture"); continue; }
                    if (a.Rink == null) { Debug.LogWarning("Ultra Puck: couldn't tell which rink " + Path.GetFileName(f) + " is for"); continue; }
                    list.Add(a);
                }
                catch (Exception ex) { Debug.LogWarning("Ultra Puck: couldn't load the arena " + Path.GetFileName(f) + ": " + ex.Message); }
            }
            if (list.Count > 0) Debug.Log("Ultra Puck: custom arenas: " + string.Join(", ", list.ConvertAll(a => a.Name + " (" + a.Rink + ")").ToArray()));
            return list;
        }

        /// <summary>A downloaded arena already kept from before, by its hash, or null.</summary>
        public static ArenaSkin FromDownloads(ulong hash, IList<string> mapFiles, Func<string, string> rinkName)
        {
            try
            {
                if (!Directory.Exists(Downloads)) return null;
                foreach (var f in Directory.GetFiles(Downloads, "*" + hash.ToString("x16") + ".zip"))
                {
                    var a = FromZip(System.IO.File.ReadAllBytes(f), Path.GetFileName(f), mapFiles, rinkName);
                    if (a != null && a.Hash == hash) return a;
                }
            }
            catch (Exception ex) { Debug.LogWarning("Ultra Puck: couldn't read a downloaded arena: " + ex.Message); }
            return null;
        }

        /// <summary>Keeps a downloaded arena (named after it and its hash).</summary>
        public static void Keep(ArenaSkin a)
        {
            try
            {
                Directory.CreateDirectory(Downloads);
                var safe = new System.Text.StringBuilder();
                foreach (var c in a.Name ?? "arena") safe.Append(char.IsLetterOrDigit(c) || c == ' ' || c == '-' ? c : '_');
                System.IO.File.WriteAllBytes(Path.Combine(Downloads, safe.ToString().Trim() + " " + a.Hash.ToString("x16") + ".zip"), a.Bytes);
            }
            catch (Exception ex) { Debug.LogWarning("Ultra Puck: couldn't keep the downloaded arena: " + ex.Message); }
        }
    }

    /// <summary>An arena's pictures on the rink: background behind it, floor over the ice, shadows on it, foreground over the players.</summary>
    internal sealed class ArenaView
    {
        // the rink's floor is order 0, its lines 2, nets 3, walls 4, players and the puck up to about 43 (the dash preview is 60)
        public const int BackOrder = -50, FloorOrder = 1, ShadeOrder = 5, FrontOrder = 45;
        private Material _floorMat;

        public readonly GameObject Root;

        public ArenaView(MapDef map, ArenaSkin skin)
        {
            Root = new GameObject("Arena " + skin.Name);
            var at = ArenaSkin.Canvas(map);
            Layer(skin.Back, at, BackOrder);
            if (skin.Floor != null)
            {
                // the ice's own shape, the picture laid over it as over the canvas: nothing shows outside the walls
                var outline = new List<Vector2>();
                foreach (var p in map.WallLines[0]) outline.Add(V.Of(p));
                var mesh = Draw.Polygon(outline, Color.white);
                var uv = new Vector2[mesh.vertexCount];
                var vs = mesh.vertices;
                for (var k = 0; k < uv.Length; k++) uv[k] = new Vector2((vs[k].x - at.xMin) / at.width, (vs[k].y - at.yMin) / at.height);
                mesh.uv = uv;
                _floorMat = new Material(Draw.Flat) { mainTexture = skin.Floor };
                Draw.Make("Floor", Root.transform, mesh, FloorOrder).GetComponent<MeshRenderer>().sharedMaterial = _floorMat;
            }
            Layer(skin.Shade, at, ShadeOrder);
            Layer(skin.Front, at, FrontOrder);
        }

        private void Layer(Texture2D tex, Rect at, int order)
        {
            if (tex == null) return;
            var go = new GameObject(tex.name);
            go.transform.SetParent(Root.transform, false);
            go.transform.localPosition = new Vector3(at.center.x, at.center.y, 0f);
            var sr = go.AddComponent<SpriteRenderer>();
            // as wide as the canvas, whatever the picture's size; centred up and down
            sr.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width / Mathf.Max(0.01f, at.width), 0, SpriteMeshType.FullRect);
            sr.sortingOrder = order;
        }

        public void Destroy()
        {
            foreach (var sr in Root.GetComponentsInChildren<SpriteRenderer>(true)) if (sr.sprite != null) ArenaSkin.Kill(sr.sprite);
            foreach (var mf in Root.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null) ArenaSkin.Kill(mf.sharedMesh);
            if (_floorMat != null) ArenaSkin.Kill(_floorMat);
            ArenaSkin.Kill(Root);
        }
    }
}
