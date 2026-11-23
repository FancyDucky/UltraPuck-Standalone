using System.Collections.Generic;
using UltraPuck.Core;
using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// Editor only: draws a map's walls and nets as an outline (in the Scene view and prefab mode), so you can see
    /// where the rink is while placing things round it. Not drawn in the game.
    /// </summary>
    public sealed class RinkGuide : MonoBehaviour
    {
        [Tooltip("The map file (in StreamingAssets/Maps) to outline.")]
        public string Map = "hockey-rink.json";
        public Color Colour = new Color(1f, 0.9f, 0.3f, 0.9f);
#if UNITY_EDITOR
        private string _loaded;
        private MapDef _map;

        private void OnDrawGizmos()
        {
            if (_loaded != Map)
            {
                _loaded = Map;
                _map = null;
                try { _map = MapDef.FromJson(System.IO.File.ReadAllText(System.IO.Path.Combine(Application.streamingAssetsPath, "Maps", Map))); }
                catch (System.Exception) { }
            }
            if (_map == null) return;
            Gizmos.color = Colour;
            foreach (var s in _map.Segments)
                Gizmos.DrawLine(transform.TransformPoint(new Vector3(s.A.X.ToFloat(), s.A.Y.ToFloat(), 0f)), transform.TransformPoint(new Vector3(s.B.X.ToFloat(), s.B.Y.ToFloat(), 0f)));
            foreach (var g in _map.Goals)
            {
                var c = new Vector3((g.Min.X + g.Max.X).ToFloat() / 2f, (g.Min.Y + g.Max.Y).ToFloat() / 2f, 0f);
                Gizmos.DrawWireCube(transform.TransformPoint(c), new Vector3((g.Max.X - g.Min.X).ToFloat(), (g.Max.Y - g.Min.Y).ToFloat(), 0f));
            }
        }
#endif
    }
}
