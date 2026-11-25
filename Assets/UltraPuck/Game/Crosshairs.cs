using System.IO;
using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// The game's own mouse pointer and the crosshairs to pick from (Kenney's Cursor Pack and Crosshair Pack, CC0),
    /// PNGs in StreamingAssets/Cursors and StreamingAssets/Crosshairs, loaded the first time they're needed.
    /// </summary>
    internal static class Crosshairs
    {
        /// <summary>
        /// The crosshairs on offer, in the order the picker shows them (crosshair-NNN.png); <see cref="Original"/> is the
        /// game's first crosshair, drawn in code (a ring, a cross and a dot).
        /// </summary>
        public static readonly string[] Ids = { "000", "001", Original, "004", "005", "011", "008", "019", "023", "033", "030", "031", "059", "140" };

        public const string Original = "original";

        /// <summary>Crosshair <paramref name="i"/> is the original one, drawn in code rather than a picture.</summary>
        public static bool IsOriginal(int i) { return i >= 0 && i < Ids.Length && Ids[i] == Original; }

        private static readonly Texture2D[] _tex = new Texture2D[Ids.Length];
        private static readonly bool[] _tried = new bool[Ids.Length];

        /// <summary>Crosshair <paramref name="i"/>'s picture (white on clear, to be tinted), or null if it's missing.</summary>
        public static Texture2D Get(int i)
        {
            if (i < 0 || i >= Ids.Length) i = 0;
            if (_tried[i] || Ids[i] == Original) return _tex[i];
            _tried[i] = true;
            _tex[i] = Load(Path.Combine(Application.streamingAssetsPath, Path.Combine("Crosshairs", "crosshair-" + Ids[i] + ".png")), true);
            return _tex[i];
        }

        private static Texture2D _pointer;
        private static bool _pointerTried;
        private static int _cursorShown = -1;

        /// <summary>
        /// Use the game's own mouse pointer instead of the system's, at the size in Settings (Graphics: Mouse cursor
        /// size). Cheap to call again: it only makes a new picture when the size has changed.
        /// </summary>
        public static void UseGameCursor()
        {
            var pct = Settings.CursorSize;
            if (pct == _cursorShown) return;
            _cursorShown = pct;
            if (!_pointerTried)
            {
                _pointerTried = true;
                // the 64 x 64 one (twice the normal size), so bigger sizes stay sharp
                _pointer = Load(Path.Combine(Application.streamingAssetsPath, Path.Combine("Cursors", "pointer-2x.png")), true);
            }
            if (_pointer == null) return;
            var size = Mathf.Max(8, Mathf.RoundToInt(32f * pct / 100f));
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                    px[y * size + x] = _pointer.GetPixelBilinear((x + 0.5f) / size, (y + 0.5f) / size);
            tex.SetPixels(px);
            tex.Apply(false);
            // the arrow's tip: (9, 7) in the normal 32 x 32 picture
            Cursor.SetCursor(tex, new Vector2(9f, 7f) * (size / 32f), CursorMode.Auto);
        }

        private static Texture2D Load(string path, bool smooth)
        {
            if (!File.Exists(path)) { Debug.LogWarning("Ultra Puck: missing " + path); return null; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = smooth ? FilterMode.Bilinear : FilterMode.Point };
            if (!tex.LoadImage(File.ReadAllBytes(path))) { Debug.LogWarning("Ultra Puck: couldn't read " + path); return null; }
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }
    }
}
