using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// The clock the drawing and effects run on: real time, except while a replay clip is being saved, when it moves
    /// exactly one video frame per frame drawn (however long that frame really takes), so explosions, sparks, the
    /// camera and the HUD's animations play at the clip's pace. (Unity's unscaled time ignores Time.captureDeltaTime.)
    /// </summary>
    internal static class Clock
    {
        private static bool _fixed;
        private static float _now;
        private static int _frame = -1;

        /// <summary>Seconds since the last frame.</summary>
        public static float Dt { get { return _fixed ? Time.captureDeltaTime : Time.unscaledDeltaTime; } }

        /// <summary>Seconds since the game started (jumps back to real time when a clip has been saved).</summary>
        public static float Now { get { Step(); return _fixed ? _now : Time.unscaledTime; } }

        private static void Step()
        {
            if (!_fixed || _frame == Time.frameCount) return;
            _frame = Time.frameCount;
            _now += Time.captureDeltaTime;
        }

        /// <summary>Saving a clip: from now on a frame is <see cref="Time.captureDeltaTime"/> long.</summary>
        public static void BeginFixed() { _now = Time.unscaledTime; _frame = Time.frameCount; _fixed = true; }

        public static void EndFixed() { _fixed = false; }
    }
}
