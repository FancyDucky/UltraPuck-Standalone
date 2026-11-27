using System.IO;
using UltraPuck.Core;
using UltraPuck.Game;
using UnityEditor;
using UnityEngine;

namespace UltraPuck.Editor
{
    /// <summary>
    /// The Underwater Hockey dressing as an editable prefab (Resources/UltraPuck/Themes/Underwater Hockey.prefab): the
    /// water, sun rays, surface, sea life and bubbles round the Hockey Rink, as ordinary objects to move, scale, flip
    /// and add to in Unity, and materials (Art/Underwater/Materials) for the sand, water, rays and wall shadow.
    /// Menu: Ultra Puck > Underwater > Open Underwater Hockey. Rebuild puts the prefab back as first made (asks first;
    /// the materials are kept if they're there).
    /// </summary>
    public static class ThemeBuilder
    {
        private const string Art = "Assets/UltraPuck/Art/Underwater";
        private const string ForestArt = "Assets/UltraPuck/Art/Forest";
        private const string ForestPrefab = "Assets/UltraPuck/Resources/UltraPuck/Themes/Forest Hockey.prefab";
        private const string Mats = Art + "/Materials";
        private const string Prefab = "Assets/UltraPuck/Resources/UltraPuck/Themes/Underwater Hockey.prefab";

        [MenuItem("Ultra Puck/Underwater/Open Underwater Hockey")]
        public static void OpenUnderwaterHockey()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            if (go == null) { BuildUnderwaterHockey(); go = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab); }
            if (go == null) return;
            AssetDatabase.OpenAsset(go);
            // see it whole, and the moving water while editing
            EditorApplication.delayCall += () =>
            {
                var view = SceneView.lastActiveSceneView;
                if (view == null) return;
                view.in2DMode = true;
                view.sceneViewState.alwaysRefresh = true;
                view.Frame(new Bounds(Vector3.zero, new Vector3(76f, 44f, 1f)), false);
            };
        }

        [MenuItem("Ultra Puck/Underwater/Rebuild Underwater Hockey (replaces your edits)")]
        public static void RebuildMenu()
        {
            if (File.Exists(Prefab) && !EditorUtility.DisplayDialog("Rebuild Underwater Hockey?",
                    "This puts the Underwater Hockey prefab back as it was first made, losing your changes to it. The materials are kept.", "Rebuild", "Cancel"))
                return;
            BuildUnderwaterHockey();
        }

        /// <summary>Batch mode: -executeMethod UltraPuck.Editor.ThemeBuilder.BuildUnderwaterHockey (makes it again from scratch).</summary>
        public static void BuildUnderwaterHockey()
        {
            Sprites();
            var sand = Material("Sand", "UltraPuck/Sand");
            var caustics = Material("Caustics", "UltraPuck/Caustics");
            var water = Material("Water", "UltraPuck/Water");
            var rays = Material("Rays", "UltraPuck/Rays");
            var shadow = Material("Shadow", "UltraPuck/Shadow");

            var map = MapDef.FromJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Maps", "hockey-rink.json")));
            var min = new Vector2(map.BoundsMin.X.ToFloat(), map.BoundsMin.Y.ToFloat());
            var max = new Vector2(map.BoundsMax.X.ToFloat(), map.BoundsMax.Y.ToFloat());
            var mid = (min + max) / 2f;
            var w = max.x - min.x;
            // (the water's light and the rays work in world space: tell them where the surface and the rink are)
            if (!water.HasProperty("_Top") || water.GetFloat("_Top") == 20f) { water.SetFloat("_Top", max.y + 7f); water.SetFloat("_Bottom", min.y - 4f); }
            if (rays.GetVector("_Origin") == new Vector4(0f, 30f, 0f, 0f)) { rays.SetVector("_Origin", new Vector4(mid.x, max.y + 16f, 0f, 0f)); rays.SetFloat("_Reach", (max.y - min.y) + 22f); }
            EditorUtility.SetDirty(water); EditorUtility.SetDirty(rays);

            var root = new GameObject("Underwater Hockey");
            var theme = root.AddComponent<UnderwaterTheme>();
            theme.Floor = sand;
            theme.Caustics = caustics;
            theme.WallShadow = shadow;
            root.AddComponent<RinkGuide>();

            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            Quad(root.transform, "Water", quad, water, mid, new Vector2(w + 120f, (max.y - min.y) + 80f), -30);
            Quad(root.transform, "Sun Rays", quad, rays, new Vector2(mid.x, mid.y + 1f), new Vector2(w + 80f, (max.y - min.y) + 26f), 5);

            var surface = Group(root.transform, "Surface");
            for (var x = min.x - 6f; x < max.x + 6f; x += 11f)
            {
                var s = Piece(surface, "surface", new Vector2(x, max.y + 6.2f), 2.2f, -24, 0f, false);
                if (s != null) s.gameObject.AddComponent<SurfaceBob>();
            }

            // the sea life, laid out like the reference picture; each piece its own size, nudged, sometimes flipped
            var rng = new System.Random(12345);
            var top = max.y - 0.6f;
            var bottom = min.y - 3.8f;
            var above = Group(root.transform, "Reef (top)");
            var below = Group(root.transform, "Reef (bottom)");
            System.Action<Transform, string, float, float, float, int, float> put = (parent, name, u, y, h, order, haze) =>
            {
                var size = h * (0.78f + 0.44f * (float)rng.NextDouble());
                var nudge = ((float)rng.NextDouble() - 0.5f) * 1.2f;
                var flip = !name.StartsWith("clam") && rng.NextDouble() < 0.5;
                var r = Piece(parent, name, new Vector2(min.x + w * u + nudge, y + (size - h) * 0.15f), size, order, haze, flip);
                if (r != null && name.StartsWith("seaweed"))
                {
                    var sw = r.gameObject.AddComponent<Sway>();
                    sw.Degrees = 3f + (float)rng.NextDouble() * 3f;
                    sw.Speed = 0.8f + (float)rng.NextDouble() * 0.6f;
                }
            };
            put(above, "rock_big", -0.01f, top - 0.4f, 4.2f, -16, 0.35f);
            put(above, "seaweed_tall", 0.045f, top + 0.6f, 4.6f, -15, 0.25f);
            put(above, "seaweed_bushy", 0.105f, top + 0.4f, 3.4f, -14, 0.2f);
            put(above, "rock_moss", 0.17f, top + 0.2f, 2.0f, -15, 0.35f);
            put(above, "seaweed_wide", 0.27f, top + 0.5f, 3.0f, -15, 0.3f);
            put(above, "coral_red", 0.69f, top + 0.4f, 2.3f, -14, 0.25f);
            put(above, "coral_tubes", 0.79f, top + 0.5f, 2.0f, -15, 0.3f);
            put(above, "seaweed_tall", 0.88f, top + 0.6f, 4.4f, -15, 0.25f);
            put(above, "rock_big", 0.98f, top - 0.4f, 4.4f, -16, 0.35f);
            put(above, "seaweed_bushy", 0.95f, top + 1.2f, 3.2f, -17, 0.4f);
            put(below, "seaweed_small", -0.005f, bottom + 0.8f, 2.4f, -14, 0.25f);
            put(below, "rock_big", 0.1f, bottom - 0.3f, 3.6f, -13, 0.3f);
            put(below, "coral_red", 0.235f, bottom + 0.2f, 2.6f, -12, 0.2f);
            put(below, "rock_moss", 0.34f, bottom - 0.2f, 2.8f, -13, 0.3f);
            put(below, "rock_big", 0.5f, bottom - 0.6f, 3.8f, -13, 0.3f);
            put(below, "seaweed_tall", 0.63f, bottom + 0.1f, 3.2f, -12, 0.2f);
            put(below, "rock_moss", 0.73f, bottom - 0.3f, 3.0f, -13, 0.3f);
            put(below, "coral_red", 0.8f, bottom + 0.1f, 2.6f, -12, 0.2f);
            put(below, "rock_big", 0.9f, bottom - 0.4f, 3.6f, -13, 0.3f);
            put(below, "seaweed_small", 1.005f, bottom + 0.8f, 2.4f, -14, 0.25f);
            put(below, "clam_pearl", 0.02f, min.y - 1.6f, 2.2f, -8, 0f);
            put(below, "clam_pearl", 0.98f, min.y - 1.6f, 2.2f, -8, 0f);
            put(below, "rocks_small", 0.18f, bottom + 0.3f, 1.2f, -11, 0.25f);
            put(below, "seaweed_small", 0.42f, bottom + 0.5f, 1.9f, -11, 0.2f);
            put(below, "starfish", 0.58f, bottom + 0.4f, 0.9f, -10, 0.15f);
            put(below, "rocks_small", 0.67f, bottom + 0.2f, 1.0f, -11, 0.25f);
            put(below, "seaweed_leaves", 0.85f, bottom + 0.3f, 2.2f, -12, 0.2f);
            put(below, "scallop", 0.29f, bottom + 0.4f, 0.8f, -10, 0.15f);
            put(above, "rocks_small", 0.6f, top + 0.3f, 1.1f, -15, 0.35f);
            put(above, "seaweed_small", 0.36f, top + 0.4f, 1.8f, -15, 0.3f);
            put(above, "seaweed_leaves", 0.73f, top + 0.3f, 2.2f, -16, 0.35f);

            var bubbles = new GameObject("Bubbles").AddComponent<Bubbles>();
            bubbles.transform.SetParent(root.transform, false);
            bubbles.Bubble = Sprite("bubble");
            bubbles.Area = new Rect(min.x - 2f, min.y - 3.5f, w + 4f, (max.y - min.y) + 10f);
            bubbles.Streams = new[] { min.x + w * 0.04f, max.x - w * 0.04f };

            Directory.CreateDirectory(Path.GetDirectoryName(Prefab));
            PrefabUtility.SaveAsPrefabAsset(root, Prefab);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("Ultra Puck: made " + Prefab);
        }

        // ------------------------------------------------------------------ the forest

        [MenuItem("Ultra Puck/Forest/Open Forest Hockey")]
        public static void OpenForestHockey()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(ForestPrefab);
            if (go == null) { BuildForestHockey(); go = AssetDatabase.LoadAssetAtPath<GameObject>(ForestPrefab); }
            if (go == null) return;
            AssetDatabase.OpenAsset(go);
            EditorApplication.delayCall += () =>
            {
                var view = SceneView.lastActiveSceneView;
                if (view == null) return;
                view.in2DMode = true;
                view.sceneViewState.alwaysRefresh = true;
                view.Frame(new Bounds(Vector3.zero, new Vector3(76f, 44f, 1f)), false);
            };
        }

        [MenuItem("Ultra Puck/Forest/Rebuild Forest Hockey (replaces your edits)")]
        public static void RebuildForestMenu()
        {
            if (File.Exists(ForestPrefab) && !EditorUtility.DisplayDialog("Rebuild Forest Hockey?",
                    "This puts the Forest Hockey prefab back as it was first made, losing your changes to it. The materials are kept.", "Rebuild", "Cancel"))
                return;
            BuildForestHockey();
        }

        /// <summary>
        /// The Forest Hockey dressing (Resources/UltraPuck/Themes/Forest Hockey.prefab): the Hockey Rink on a mown
        /// grass field (shader UltraPuck/Grass) in a clearing, leaf shadows drifting over it (UltraPuck/Dapple), warm
        /// sun rays, a row of trees along the top, bushes, flowers, logs and mushrooms along the bottom, a tree in
        /// front at each top corner, and falling leaves. Pictures in Art/Forest, materials in Art/Forest/Materials.
        /// Batch mode: -executeMethod UltraPuck.Editor.ThemeBuilder.BuildForestHockey
        /// </summary>
        public static void BuildForestHockey()
        {
            AssetDatabase.Refresh();
            Sprites(ForestArt);
            var grass = Material("Grass", "UltraPuck/Grass", ForestArt);
            var ground = Material("Forest Floor", "UltraPuck/Grass", ForestArt);
            var dapple = Material("Dapple", "UltraPuck/Dapple", ForestArt);
            var rays = Material("Sun Rays", "UltraPuck/Rays", ForestArt);
            var shadow = Material("Shadow", "UltraPuck/Shadow", ForestArt);

            var map = MapDef.FromJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Maps", "hockey-rink.json")));
            var min = new Vector2(map.BoundsMin.X.ToFloat(), map.BoundsMin.Y.ToFloat());
            var max = new Vector2(map.BoundsMax.X.ToFloat(), map.BoundsMax.Y.ToFloat());
            var mid = (min + max) / 2f;
            var w = max.x - min.x;
            // the forest floor round the clearing: darker, no stripes, more little flowers (set the first time only)
            if (ground.GetFloat("_Stripes") > 0f)
            {
                ground.SetFloat("_Stripes", 0f);
                ground.SetColor("_Light", new Color(0.25f, 0.42f, 0.2f));
                ground.SetColor("_Dark", new Color(0.25f, 0.42f, 0.2f));
                ground.SetFloat("_Patches", 0.22f);
                ground.SetFloat("_FlowerChance", 0.09f);
                ground.SetFloat("_Wind", 0.03f);
            }
            // sunlight slanting in from the top left
            if (rays.GetVector("_Origin") == new Vector4(0f, 30f, 0f, 0f))
            {
                rays.SetColor("_Colour", new Color(1f, 0.9f, 0.62f));
                rays.SetVector("_Origin", new Vector4(min.x + w * 0.15f, max.y + 18f, 0f, 0f));
                rays.SetFloat("_Reach", (max.y - min.y) + 26f);
                rays.SetFloat("_Strength", 0.3f);
            }
            EditorUtility.SetDirty(ground); EditorUtility.SetDirty(rays);

            var root = new GameObject("Forest Hockey");
            var theme = root.AddComponent<UnderwaterTheme>();
            theme.Floor = grass;
            theme.Caustics = dapple;
            theme.WallShadow = shadow;
            theme.ShadowOffset = new Vector2(0.14f, -0.3f);
            theme.ShadowColour = new Color(0.05f, 0.12f, 0.03f, 0.32f);
            theme.Walls = new Color(0.5f, 0.35f, 0.22f);
            theme.Lines = new Color(1f, 1f, 1f, 0.7f);
            theme.TeamLines = 0.75f;
            theme.ScreenTint = new Color(1f, 0.95f, 0.8f, 0f);
            theme.Puck = Color.white;
            theme.PuckEdge = new Color(0.1f, 0.18f, 0.08f);
            root.AddComponent<RinkGuide>().Map = "hockey-rink.json";

            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            Quad(root.transform, "Forest Floor", quad, ground, mid, new Vector2(w + 120f, (max.y - min.y) + 80f), -30);
            Quad(root.transform, "Sun Rays", quad, rays, new Vector2(mid.x, mid.y + 1f), new Vector2(w + 80f, (max.y - min.y) + 26f), 5);

            var rng = new System.Random(2024);
            System.Func<float> next = () => (float)rng.NextDouble();
            System.Func<Transform, string, Vector2, float, int, float, SpriteRenderer> put = (parent, name, foot, h, order, haze) =>
            {
                var r = Piece(parent, name, foot, h * (0.88f + 0.24f * next()), order, 0f, next() < 0.5f, ForestArt);
                // further back: darker and bluer
                if (r != null && haze > 0f) r.color = Color.Lerp(Color.white, new Color(0.42f, 0.56f, 0.5f), haze);
                return r;
            };
            System.Action<SpriteRenderer, float> sway = (r, deg) =>
            {
                if (r == null) return;
                var s = r.gameObject.AddComponent<Sway>();
                s.Degrees = deg * (0.7f + 0.6f * next());
                s.Speed = 0.5f + next() * 0.4f;
            };

            // along the top: a row of trees at the edge of the clearing, darker ones behind, undergrowth in front
            var back = Group(root.transform, "Trees (behind)");
            for (var x = min.x - 10f; x < max.x + 10f; x += 4.2f + next() * 1.6f)
                sway(put(back, next() < 0.55f ? "tree_pine" : "tree_oak_dark", new Vector2(x, max.y + 2.6f + next() * 0.8f), 6f, -19, 0.45f), 1.2f);
            var top = Group(root.transform, "Trees (top)");
            for (var x = min.x - 8f; x < max.x + 8f; x += 3.6f + next() * 2.2f)
            {
                var name = next() < 0.35f ? "tree_pine" : next() < 0.5f ? "tree_oak" : "tree_oak_dark";
                sway(put(top, name, new Vector2(x, max.y + 0.2f + next() * 0.6f), name == "tree_pine" ? 5.2f : 5.6f, -16, 0.08f), 1.6f);
            }
            var tops = Group(root.transform, "Undergrowth (top)");
            for (var x = min.x - 6f; x < max.x + 6f; x += 2.2f + next() * 2.6f)
            {
                var pick = next();
                var name = pick < 0.4f ? "bush" : pick < 0.55f ? "bush_berries" : pick < 0.7f ? "grass_tuft" : pick < 0.8f ? "flowers_white" : pick < 0.9f ? "mushrooms" : "flowers_yellow";
                put(tops, name, new Vector2(x, max.y + 0.15f + next() * 0.4f), name.StartsWith("bush") ? 1.4f : 0.9f, -14, 0f);
            }

            // along the bottom: low things (bushes, flowers, logs, mushrooms, stumps, rocks), trees further out
            var bottom = Group(root.transform, "Undergrowth (bottom)");
            var low = new[] { "bush", "flowers_white", "log", "bush_berries", "flowers_pink", "mushrooms", "stump", "grass_tuft", "rock_moss", "flowers_yellow", "bush", "grass_tuft" };
            var k = 0;
            for (var x = min.x - 6f; x < max.x + 6f; x += 2.4f + next() * 1.8f, k++)
            {
                var name = low[k % low.Length];
                var h = name.StartsWith("bush") ? 1.9f : name == "stump" || name == "rock_moss" ? 1.3f : 1.1f;
                put(bottom, name, new Vector2(x, min.y - 2.6f - next() * 0.9f), h, -11, 0f);
            }
            var far = Group(root.transform, "Trees (bottom)");
            for (var x = min.x - 8f; x < max.x + 8f; x += 6f + next() * 3f)
                sway(put(far, next() < 0.5f ? "tree_pine" : "tree_oak", new Vector2(x, min.y - 5.6f - next() * 1.2f), 5.2f, -13, 0.1f), 1.4f);
            // and down the sides (seen on wide screens); lower ones in front of higher ones
            var sides = Group(root.transform, "Trees (sides)");
            foreach (var side in new[] { -1f, 1f })
                for (var y = min.y - 2f; y < max.y + 2f; y += 3.4f + next() * 1.4f)
                    sway(put(sides, next() < 0.5f ? "tree_pine" : "tree_oak", new Vector2(mid.x + side * (w / 2f + 3.6f + next() * 3f), y), 5.4f, -13 - Mathf.RoundToInt((y - min.y) / 4f), 0.1f), 1.4f);

            // in front of everything at the corners: a tree at each top corner, a bush at each bottom one
            var front = Group(root.transform, "In front");
            sway(put(front, "tree_oak", new Vector2(min.x - 1.4f, max.y - 3.8f), 7.4f, 44, 0f), 1.0f);
            sway(put(front, "tree_oak_dark", new Vector2(max.x + 1.4f, max.y - 3.8f), 7.4f, 44, 0f), 1.0f);
            put(front, "bush", new Vector2(min.x + 0.2f, min.y - 1.5f), 1.6f, 44, 0f);
            put(front, "bush_berries", new Vector2(max.x - 0.2f, min.y - 1.5f), 1.6f, 44, 0f);

            var leaves = new GameObject("Leaves").AddComponent<Leaves>();
            leaves.transform.SetParent(root.transform, false);
            leaves.Pictures = new[] { Sprite("leaf_green", ForestArt), Sprite("leaf_yellow", ForestArt), Sprite("leaf_orange", ForestArt) };
            leaves.Area = new Rect(min.x - 4f, min.y - 4f, w + 8f, (max.y - min.y) + 12f);

            Directory.CreateDirectory(Path.GetDirectoryName(ForestPrefab));
            PrefabUtility.SaveAsPrefabAsset(root, ForestPrefab);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("Ultra Puck: made " + ForestPrefab);
        }

        // ------------------------------------------------------------------ pieces

        private static Transform Group(Transform parent, string name)
        {
            var g = new GameObject(name).transform;
            g.SetParent(parent, false);
            return g;
        }

        private static void Quad(Transform parent, string name, Mesh mesh, Material mat, Vector2 centre, Vector2 size, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(centre.x, centre.y, 0f);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.sortingOrder = order;
        }

        /// <summary>A sprite standing on <paramref name="foot"/> (its bottom middle), <paramref name="height"/> tall.</summary>
        private static SpriteRenderer Piece(Transform parent, string name, Vector2 foot, float height, int order, float haze, bool flip, string art = Art)
        {
            var sprite = Sprite(name, art);
            if (sprite == null) return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(foot.x, foot.y, 0f);
            var s = height / (sprite.rect.height / sprite.pixelsPerUnit);
            go.transform.localScale = new Vector3(s, s, 1f);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.sortingOrder = order;
            r.flipX = flip;
            // further back: more of the water's colour over it
            if (haze > 0f) r.color = Color.Lerp(Color.white, new Color(0.35f, 0.6f, 0.68f), haze);
            return r;
        }

        private static Sprite Sprite(string name, string art = Art) { return AssetDatabase.LoadAssetAtPath<Sprite>(art + "/" + name + ".png"); }

        /// <summary>The pictures as sprites: standing on their bottom middle (bubbles on their middle), 100 pixels a unit.</summary>
        private static void Sprites(string art = Art)
        {
            foreach (var path in Directory.GetFiles(art, "*.png"))
            {
                var p = path.Replace('\\', '/');
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti == null) continue;
                var stem = Path.GetFileNameWithoutExtension(p);
                var wantPivot = stem == "bubble" || stem.StartsWith("leaf_") ? SpriteAlignment.Center : SpriteAlignment.BottomCenter;
                var settings = new TextureImporterSettings();
                ti.ReadTextureSettings(settings);
                if (ti.textureType == TextureImporterType.Sprite && settings.spriteAlignment == (int)wantPivot && Mathf.Approximately(settings.spritePixelsPerUnit, 100f)) continue;
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.ReadTextureSettings(settings);
                settings.spriteAlignment = (int)wantPivot;
                settings.spritePixelsPerUnit = 100f;
                settings.spriteMeshType = SpriteMeshType.FullRect;
                ti.SetTextureSettings(settings);
                ti.mipmapEnabled = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.alphaIsTransparency = true;
                ti.SaveAndReimport();
            }
        }

        /// <summary>A material for <paramref name="shader"/>, made the first time and kept after (your changes stay).</summary>
        private static Material Material(string name, string shader, string art = null)
        {
            var mats = art == null ? Mats : art + "/Materials";
            Directory.CreateDirectory(mats);
            var path = mats + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
