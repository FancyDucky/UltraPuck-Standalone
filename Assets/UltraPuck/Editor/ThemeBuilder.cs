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
        private static SpriteRenderer Piece(Transform parent, string name, Vector2 foot, float height, int order, float haze, bool flip)
        {
            var sprite = Sprite(name);
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

        private static Sprite Sprite(string name) { return AssetDatabase.LoadAssetAtPath<Sprite>(Art + "/" + name + ".png"); }

        /// <summary>The pictures as sprites: standing on their bottom middle (bubbles on their middle), 100 pixels a unit.</summary>
        private static void Sprites()
        {
            foreach (var path in Directory.GetFiles(Art, "*.png"))
            {
                var p = path.Replace('\\', '/');
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti == null) continue;
                var wantPivot = Path.GetFileNameWithoutExtension(p) == "bubble" ? SpriteAlignment.Center : SpriteAlignment.BottomCenter;
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
        private static Material Material(string name, string shader)
        {
            Directory.CreateDirectory(Mats);
            var path = Mats + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
