using System.Collections.Generic;
using UltraPuck.Core;
using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// Editor only: shows the rink in the Scene view and prefab mode, so you can see it while placing things round it:
    /// the real rink as the game builds it (sand, lines, nets, walls, the light and the water tint, from this object's
    /// <see cref="UnderwaterTheme"/>), or just a yellow outline of its walls and nets. Hidden objects that are never
    /// saved, and gone in Play mode (the game builds the real one then). Not gizmos: those stopped being drawn after
    /// the editor's Play mode, and the Gizmos toggle hid them too.
    /// </summary>
    [ExecuteAlways]
    public sealed class RinkGuide : MonoBehaviour
    {
        [Tooltip("The map file (in StreamingAssets/Maps) to show.")]
        public string Map = "hockey-rink.json";
        [Tooltip("On: the rink as it looks in the game. Off: just a yellow outline of its walls and nets.")]
        public bool ShowRink = true;
        public Color Colour = new Color(1f, 0.9f, 0.3f, 0.9f);
        [Tooltip("How thick the outline is (world units).")]
        public float Width = 0.12f;
#if UNITY_EDITOR
        private GameObject _lines;
        private Material _mat;
        private string _built;
        private Color _builtColour;
        private float _builtWidth;
        private bool _builtRink;

        private void OnEnable() { _built = null; }
        private void OnDisable() { Clear(); }
        private void OnDestroy() { Clear(); }

        private void Update()
        {
            if (Application.isPlaying) { Clear(); return; } // the game draws the real rink
            if (_lines == null || _built != Map || _builtColour != Colour || _builtWidth != Width || _builtRink != ShowRink) Build();
        }

        private void Clear()
        {
            if (_lines != null) DestroyImmediate(_lines);
            _lines = null;
            if (_mat != null) DestroyImmediate(_mat);
            _mat = null;
            _built = null;
        }

        private void Build()
        {
            Clear();
            _built = Map; _builtColour = Colour; _builtWidth = Width; _builtRink = ShowRink;
            MapDef map;
            try { map = MapDef.FromJson(System.IO.File.ReadAllText(System.IO.Path.Combine(Application.streamingAssetsPath, "Maps", Map))); }
            catch (System.Exception ex) { Debug.LogWarning("Rink Guide: couldn't load " + Map + ": " + ex.Message); return; }

            // hidden and never saved with the prefab or the scene
            _lines = new GameObject("Rink Guide (editor only)") { hideFlags = HideFlags.HideAndDontSave };
            _lines.transform.SetParent(transform, false);
            if (ShowRink)
            {
                // the game's own rink builder, dressed with this prefab's theme (not a second copy of the prefab)
                var view = new RinkView(map, new Tuning().WallThickness.ToFloat(), GetComponent<UnderwaterTheme>());
                view.Root.transform.SetParent(_lines.transform, false);
                foreach (var t in view.Root.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
                return;
            }
            _mat = new Material(Shader.Find("Sprites/Default")) { hideFlags = HideFlags.HideAndDontSave };
            foreach (var wall in map.WallLines)
            {
                var pts = new List<Vector3>();
                foreach (var p in wall) pts.Add(new Vector3(p.X.ToFloat(), p.Y.ToFloat(), 0f));
                Line(pts, false);
            }
            foreach (var g in map.Goals)
            {
                float x0 = g.Min.X.ToFloat(), y0 = g.Min.Y.ToFloat(), x1 = g.Max.X.ToFloat(), y1 = g.Max.Y.ToFloat();
                Line(new List<Vector3> { new Vector3(x0, y0, 0f), new Vector3(x1, y0, 0f), new Vector3(x1, y1, 0f), new Vector3(x0, y1, 0f) }, true);
            }
        }

        private void Line(List<Vector3> pts, bool loop)
        {
            if (pts.Count < 2) return;
            var go = new GameObject("line") { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(_lines.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.hideFlags = HideFlags.HideAndDontSave;
            lr.useWorldSpace = false;
            lr.loop = loop;
            lr.positionCount = pts.Count;
            lr.SetPositions(pts.ToArray());
            lr.widthMultiplier = Width;
            lr.sharedMaterial = _mat;
            lr.startColor = lr.endColor = Colour;
            lr.numCornerVertices = 2;
            lr.sortingOrder = 1000; // over everything in the dressing
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
        }
#endif
    }
}
