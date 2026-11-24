using System.Collections.Generic;
using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// Small pop-ups in the top-right corner ("Sam left the game.") that fade out after a few seconds, over
    /// everything else (menus and the match).
    /// </summary>
    public sealed class Toasts : MonoBehaviour
    {
        private sealed class Toast { public string Text; public float At, Life; }
        private static readonly List<Toast> _shown = new List<Toast>();
        private const float DefaultLife = 4f, Fade = 0.4f;

        /// <summary>Shows <paramref name="text"/> for <paramref name="seconds"/> (4 by default; longer for something to read and do).</summary>
        public static void Show(string text, float seconds = DefaultLife)
        {
            if (string.IsNullOrEmpty(text)) return;
            _shown.Add(new Toast { Text = text, At = Time.unscaledTime, Life = Mathf.Max(Fade * 2f, seconds) });
            if (_shown.Count > 4) _shown.RemoveAt(0);
        }

        private void OnGUI()
        {
            if (_shown.Count == 0) return;
            _shown.RemoveAll(t => Time.unscaledTime - t.At > t.Life);
            if (_shown.Count == 0) return;
            GUI.depth = -50;
            Ui.Begin();
            var style = new GUIStyle(Ui.Font(15, true, TextAnchor.MiddleLeft)) { wordWrap = true };
            var y = 18f;
            const float w = 320f;
            foreach (var t in _shown)
            {
                var age = Time.unscaledTime - t.At;
                var a = Mathf.Clamp01(Mathf.Min(age / Fade, (t.Life - age) / Fade));
                var slide = (1f - Mathf.Clamp01(age / Fade)) * 30f;
                var h = Mathf.Max(44f, style.CalcHeight(new GUIContent(t.Text), w - 28f) + 18f);
                var r = new Rect(Ui.W - w - 18f + slide, y, w, h);
                Ui.Box(r, new Color(0.08f, 0.09f, 0.12f, 0.88f * a), 10f);
                Ui.Box(new Rect(r.x, r.y, 5f, r.height), new Color(0.95f, 0.45f, 0.4f, a), 2f);
                Ui.Label(new Rect(r.x + 16f, r.y, r.width - 28f, r.height), t.Text, style, new Color(1f, 1f, 1f, a), false);
                y += h + 8f;
            }
        }
    }
}
