using System;
using System.Collections.Generic;
using UltraPuck.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UltraPuck.Game
{
    /// <summary>
    /// Watching a saved match (Profile > Replays): play and pause, skip about, change speed and camera, and a time bar
    /// with the goals and saves marked on it. A clip is a stretch of it (handles on the time bar, like Steam's) saved as
    /// an MP4 or copied to the clipboard; <see cref="GameRunner"/> draws the frames and does the saving.
    /// </summary>
    internal sealed class ReplayViewer : MonoBehaviour
    {
        public MatchReplay Replay { get; private set; }
        public string FilePath { get; private set; }

        /// <summary>Where we are, in ticks of the match.</summary>
        public float Tick { get; private set; }
        public bool Paused { get; private set; }
        public int SpeedIndex { get; private set; }
        public static readonly float[] Speeds = { 0.1f, 0.25f, 0.5f, 1f, 2f, 4f };
        public float Speed { get { return Speeds[SpeedIndex]; } }

        /// <summary>The camera: 0 follows the play, 1 shows the whole rink, 2 and up follow one player (2 = player 0).</summary>
        public int CamMode { get; private set; }
        public bool HideUi;

        /// <summary>The stretch of the match marked for a clip (ticks), while making one.</summary>
        public bool Clipping { get; private set; }
        public float ClipFrom { get; private set; }
        public float ClipTo { get; private set; }

        /// <summary>A clip is being saved: nothing of this screen is drawn (it would end up in the video).</summary>
        public bool Exporting;

        /// <summary>The picture jumped this frame (a seek): no sounds, and the effects on screen go.</summary>
        public bool Jumped { get; private set; }
        /// <summary>Played forward at normal speed (or slower) this frame: its sounds are heard.</summary>
        public bool Audible { get { return !Paused && !Jumped && Speed <= 1.01f && !_dragging; } }

        /// <summary>The two frames to show and how far between them.</summary>
        public GameState A { get; private set; }
        public GameState B { get; private set; }
        public float T { get; private set; }

        /// <summary>Asked to save (true: and copy to the clipboard) the clip from / to these ticks.</summary>
        public Action<float, float, bool> OnExport;
        public Action OnClose;
        /// <summary>A player's name (for the camera label).</summary>
        public Func<int, string> NameOf;

        private int[] _afterPicking;
        private bool _dragging;
        private int _dragWhat; // 0: the playhead, 1: the clip's start, 2: its end
        private float _uiAt;

        public float First { get { return Replay.Ticks[0]; } }
        public float Last { get { return Replay.Ticks[Replay.Frames - 1]; } }
        private float Tps { get { return Replay.TicksPerSecond; } }

        public void Open(MatchReplay replay, string path)
        {
            Replay = replay;
            FilePath = path;
            Tick = First;
            Paused = false;
            SpeedIndex = Array.IndexOf(Speeds, 1f);
            CamMode = 0;
            HideUi = false;
            Clipping = false;
            Exporting = false;
            _dragging = false;
            _uiAt = Time.unscaledTime;
            // card picks are skipped while playing: the frame after each pick
            _afterPicking = new int[replay.Frames];
            var next = replay.Frames - 1;
            for (var f = replay.Frames - 1; f >= 0; f--)
            {
                if (replay.Phases[f] != (byte)Phase.Picking) next = f;
                _afterPicking[f] = next;
            }
            Fetch(true);
        }

        public void Close() { Replay = null; }

        /// <summary>Jump to <paramref name="tick"/> (a clip being saved starts here).</summary>
        public void Seek(float tick) { Tick = Mathf.Clamp(tick, First, Last); Fetch(true); }

        /// <summary>Play on (or stop) at normal speed: for saving a clip.</summary>
        public void PlayForExport() { Paused = false; SpeedIndex = Array.IndexOf(Speeds, 1f); }

        /// <summary>Move on by <paramref name="seconds"/> of real time (the clip saver steps it exactly).</summary>
        public void Advance(float seconds)
        {
            if (Paused || Replay == null) { Fetch(false); return; }
            Tick += seconds * Tps * Speed;
            var f = Replay.FrameAt(Tick);
            var jumped = false;
            if (!Exporting && Replay.Phases[f] == (byte)Phase.Picking)
            {
                // skip the card picks (they can take a while)
                var to = _afterPicking[f];
                if (to != f) { Tick = Replay.Ticks[to]; jumped = true; }
            }
            if (Tick >= Last) { Tick = Last; Paused = true; }
            Fetch(jumped);
        }

        private void Fetch(bool jumped)
        {
            var f = Replay.FrameAt(Tick);
            var g = Mathf.Min(f + 1, Replay.Frames - 1);
            A = Replay.FrameState(f);
            B = Replay.FrameState(g);
            var span = Replay.Ticks[g] - Replay.Ticks[f];
            T = span > 0 ? Mathf.Clamp01((Tick - Replay.Ticks[f]) / span) : 0f;
            Jumped = jumped;
        }

        // ------------------------------------------------------------------ controls

        private static bool Pad(Func<Gamepad, UnityEngine.InputSystem.Controls.ButtonControl> b)
        {
            if (LocalPlayers.Blocked) return false;
            foreach (var g in Gamepad.all) if (b(g).wasPressedThisFrame) return true;
            return false;
        }

        private static bool Key(Key k)
        {
            var kb = Keyboard.current;
            return kb != null && !LocalPlayers.Blocked && kb[k].wasPressedThisFrame;
        }

        /// <summary>Read the controls and move on a frame (call once per frame).</summary>
        public void Frame(LocalPlayers local)
        {
            if (Replay == null) return;
            if (Exporting) return; // the clip saver moves it on
            var m = local.Menu();
            Jumped = false;
            if (m.Back)
            {
                Sound.Back();
                if (Clipping) Clipping = false;
                else { if (OnClose != null) OnClose(); return; }
            }
            if (m.Confirm) { Paused = !Paused; if (!Paused && Tick >= Last) Tick = First; Sound.Select(); }
            if (Pad(g => g.selectButton) || Key(UnityEngine.InputSystem.Key.H)) HideUi = !HideUi;
            if (m.Y != 0)
            {
                SpeedIndex = Mathf.Clamp(SpeedIndex + m.Y, 0, Speeds.Length - 1);
                Sound.Step(m.Y);
            }
            LeftRight();
            // L1 / R1 (Q / E): the clip's start / end, here (the first press starts a clip)
            if (m.Tab < 0) SetClipStart();
            if (m.Tab > 0) SetClipEnd();
            if (!Clipping)
            {
                if (Pad(g => g.buttonNorth) || Key(UnityEngine.InputSystem.Key.C)) { CamMode = (CamMode + 1) % (2 + PlayerCount()); Sound.Move(); }
                if (Pad(g => g.buttonWest) || Key(UnityEngine.InputSystem.Key.X)) StartClip();
            }
            else
            {
                if (Pad(g => g.buttonWest) || Key(UnityEngine.InputSystem.Key.X)) Export(false);
                else if (Pad(g => g.buttonNorth) || Key(UnityEngine.InputSystem.Key.V)) Export(true);
            }
            var wasJump = Jumped;
            Advance(_dragging ? 0f : Time.unscaledDeltaTime);
            Jumped |= wasJump;
        }

        private int PlayerCount() { return B != null ? B.Players.Length : 0; }

        /// <summary>The player the camera follows (-1: none).</summary>
        public int Following { get { return CamMode >= 2 && B != null && CamMode - 2 < B.Players.Length ? CamMode - 2 : -1; } }

        // d-pad / stick / arrows left and right: a tap skips (or steps a frame when paused), holding goes goal to goal
        private const float HoldFor = 0.35f, HoldRepeat = 0.7f;
        private int _lrDir;
        private float _lrHeld, _lrNext;

        private static int RawLeftRight()
        {
            if (LocalPlayers.Blocked) return 0;
            var x = 0f;
            foreach (var g in Gamepad.all) { x += g.dpad.x.ReadValue(); var s = g.leftStick.x.ReadValue(); if (Mathf.Abs(s) > 0.6f) x += s; }
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) x -= 1f;
                if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) x += 1f;
            }
            return x < -0.5f ? -1 : x > 0.5f ? 1 : 0;
        }

        private void LeftRight()
        {
            var dir = RawLeftRight();
            if (dir != _lrDir)
            {
                // let go (or turned round) before it counted as a hold: that was a tap
                if (_lrDir != 0 && _lrHeld < HoldFor)
                {
                    Tick = Mathf.Clamp(Tick + (Paused ? _lrDir * 2f : _lrDir * 5f * Tps), First, Last);
                    Jumped = true;
                }
                _lrDir = dir; _lrHeld = 0f; _lrNext = HoldFor;
                return;
            }
            if (dir == 0) return;
            _lrHeld += Time.unscaledDeltaTime;
            if (_lrHeld >= _lrNext) { _lrNext += HoldRepeat; JumpToMarker(dir, true); Sound.Move(); }
        }

        private void SetClipStart()
        {
            if (!Clipping) { Clipping = true; ClipTo = Mathf.Min(Last, Tick + 10f * Tps); }
            ClipFrom = Mathf.Max(First, Tick);
            if (ClipTo < ClipFrom + Tps) ClipTo = Mathf.Min(Last, ClipFrom + Tps);
            Sound.Step(-1);
        }

        private void SetClipEnd()
        {
            if (!Clipping) { Clipping = true; ClipFrom = Mathf.Max(First, Tick - 10f * Tps); }
            ClipTo = Mathf.Min(Last, Tick);
            if (ClipFrom > ClipTo - Tps) ClipFrom = Mathf.Max(First, ClipTo - Tps);
            Sound.Step(1);
        }

        private void JumpToMarker(int dir, bool goalsOnly)
        {
            // a little before the goal (or save), so it can be seen coming
            var lead = 4f * Tps;
            float best = dir > 0 ? float.MaxValue : float.MinValue;
            foreach (var mk in Replay.Markers)
            {
                if (mk.Kind == MatchReplay.MarkerKind.Cards || (goalsOnly && mk.Kind != MatchReplay.MarkerKind.Goal)) continue;
                var at = Mathf.Max(First, Replay.Ticks[Mathf.Clamp(mk.Frame, 0, Replay.Frames - 1)] - lead);
                if (dir > 0 && at > Tick + 1f && at < best) best = at;
                if (dir < 0 && at < Tick - Tps && at > best) best = at;
            }
            if (best == float.MaxValue || best == float.MinValue) return;
            Tick = best;
            Paused = false;
            Jumped = true;
        }

        public void StartClip()
        {
            Clipping = true;
            // the last ten seconds up to here, to start with
            ClipTo = Mathf.Max(Tick, First + Tps);
            ClipFrom = Mathf.Max(First, ClipTo - 10f * Tps);
            Sound.Select();
        }

        private void Export(bool clipboard)
        {
            if (ClipTo - ClipFrom < Tps * 0.5f) { Sound.Deny(); return; }
            Sound.Select();
            if (OnExport != null) OnExport(ClipFrom, ClipTo, clipboard);
        }

        // ------------------------------------------------------------------ drawing

        private static string Time_(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            var m = Mathf.FloorToInt(seconds / 60f);
            return m + ":" + Mathf.FloorToInt(seconds - m * 60f).ToString("00");
        }

        private void OnGUI()
        {
            if (Replay == null || Exporting) return;
            GUI.depth = 4;
            Ui.Begin();
            float w = Ui.W, h = Ui.H;
            if (HideUi)
            {
                Glyphs.Hint(new Rect(16f, h - 34f, 400f, 26f), "{hideui} Show controls", 14, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleLeft);
                if (Ui.TakeClick()) HideUi = false;
                return;
            }
            var a = Ui.EaseOut((Time.unscaledTime - _uiAt) / 0.3f);

            // what it is, top left
            Ui.Box(new Rect(18f, 18f, 360f, 64f), new Color(0f, 0f, 0f, 0.55f * a), 10f);
            Ui.Box(new Rect(26f, 28f, 104f, 28f), new Color(0.85f, 0.15f, 0.15f, 0.95f * a), 6f);
            Ui.Label(new Rect(26f, 28f, 104f, 28f), "REPLAY", Ui.Font(17, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, a), false);
            Ui.Label(new Rect(140f, 24f, 236f, 34f), Replay.MapName, Ui.Font(17, true), new Color(1f, 1f, 1f, a));
            Ui.Label(new Rect(28f, 56f, 350f, 22f), Replay.When.ToString("d MMM yyyy, HH:mm"), Ui.Font(13), new Color(1f, 1f, 1f, 0.6f * a));

            // the panel along the bottom: time bar, then what's going on, then the controls
            var pw = Mathf.Min(w - 40f, 1400f);
            var px = w / 2f - pw / 2f;
            var py = h - 150f;
            Ui.Box(new Rect(px, py, pw, 136f), new Color(0.05f, 0.06f, 0.09f, 0.82f * a), 14f);
            var bar = new Rect(px + 24f, py + 40f, pw - 48f, 14f);
            TimeBar(bar, a);

            var now = (Tick - First) / Tps;
            var info = Time_(now) + " / " + Time_((Last - First) / Tps)
                + "      " + (Paused ? "Paused" : Speed == 1f ? "Playing" : "Playing at " + Speed + "x")
                + "      Camera: " + CamName();
            Ui.Label(new Rect(px + 24f, py + 8f, pw - 48f, 26f), info, Ui.Font(15, true), new Color(1f, 1f, 1f, 0.9f * a));
            if (Clipping)
            {
                var len = (ClipTo - ClipFrom) / Tps;
                Ui.Label(new Rect(px + 24f, py + 8f, pw - 48f, 26f), "Clip: " + Time_((ClipFrom - First) / Tps) + " to " + Time_((ClipTo - First) / Tps) + "  (" + len.ToString("0.0") + " s)",
                    Ui.Font(15, true, TextAnchor.MiddleRight), new Color(1f, 0.85f, 0.3f, a));
            }

            var hint = Clipping
                ? "{tabs} Start / end of the clip here      {side} Move (hold: next goal)      {confirm} Play / pause      {clip} Save as MP4      {copyclip} Copy to clipboard      {back} Cancel clip"
                : "{confirm} Play / pause      {side} " + (Paused ? "Step" : "Skip 5 s") + " (hold: next goal)      {updown} Speed      {tabs} Clip start / end      {camera} Camera      {clip} Make a clip      {hideui} Hide      {back} Back";
            Glyphs.Hint(new Rect(px + 16f, py + 96f, pw - 32f, 28f), hint, 14, new Color(1f, 1f, 1f, 0.7f * a));

            // the mouse: buttons along the top of the panel
            var bx = px + pw - 24f;
            if (Button(ref bx, py - 46f, Clipping ? "Copy to clipboard" : "Make a clip", a)) { if (Clipping) Export(true); else StartClip(); }
            if (Clipping && Button(ref bx, py - 46f, "Save as MP4", a)) Export(false);
            if (Button(ref bx, py - 46f, "Camera", a)) CamMode = (CamMode + 1) % (2 + PlayerCount());
            if (Button(ref bx, py - 46f, Paused ? "Play" : "Pause", a)) { Paused = !Paused; if (!Paused && Tick >= Last) Tick = First; }
        }

        private static bool Button(ref float right, float y, string text, float a)
        {
            var tw = Ui.Font(15, true).CalcSize(new GUIContent(text)).x + 30f;
            var r = new Rect(right - tw, y, tw, 36f);
            right -= tw + 8f;
            var hover = r.Contains(Ui.Mouse);
            Ui.Box(r, new Color(1f, 1f, 1f, (hover ? 0.22f : 0.1f) * a), 9f);
            Ui.Label(r, text, Ui.Font(15, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, a), false);
            return hover && Ui.TakeClick();
        }

        private string CamName()
        {
            if (CamMode == 0) return "Follow the play";
            if (CamMode == 1) return "Whole rink";
            var p = Following;
            return p >= 0 && NameOf != null ? "On " + NameOf(p) : "On a player";
        }

        private float XOf(Rect bar, float tick) { return bar.x + bar.width * Mathf.Clamp01((tick - First) / Mathf.Max(1f, Last - First)); }
        private float TickOf(Rect bar, float x) { return First + (Last - First) * Mathf.Clamp01((x - bar.x) / bar.width); }

        private void TimeBar(Rect bar, float a)
        {
            // the track, card picks shaded (they're skipped), what's been watched filled in
            Ui.Box(bar, new Color(1f, 1f, 1f, 0.14f * a), 7f);
            var f0 = -1;
            for (var f = 0; f <= Replay.Frames; f++)
            {
                var picking = f < Replay.Frames && Replay.Phases[f] == (byte)Phase.Picking;
                if (picking && f0 < 0) f0 = f;
                if (!picking && f0 >= 0)
                {
                    var x0 = XOf(bar, Replay.Ticks[f0]); var x1 = XOf(bar, Replay.Ticks[Mathf.Min(f, Replay.Frames - 1)]);
                    Ui.Box(new Rect(x0, bar.y, Mathf.Max(2f, x1 - x0), bar.height), new Color(0f, 0f, 0f, 0.45f * a), 3f);
                    f0 = -1;
                }
            }
            var head = XOf(bar, Tick);
            Ui.Box(new Rect(bar.x, bar.y, Mathf.Max(bar.height, head - bar.x), bar.height), new Color(1f, 1f, 1f, 0.5f * a), 7f);

            if (Clipping)
            {
                var c0 = XOf(bar, ClipFrom); var c1 = XOf(bar, ClipTo);
                Ui.Box(new Rect(c0, bar.y - 6f, c1 - c0, bar.height + 12f), new Color(1f, 0.8f, 0.2f, 0.35f * a), 4f);
                Ui.Box(new Rect(c0 - 4f, bar.y - 10f, 8f, bar.height + 20f), new Color(1f, 0.85f, 0.3f, a), 3f);
                Ui.Box(new Rect(c1 - 4f, bar.y - 10f, 8f, bar.height + 20f), new Color(1f, 0.85f, 0.3f, a), 3f);
            }

            // markers: goals (the scoring team's colour, tall), saves (white, short), card picks (a grey dot)
            foreach (var mk in Replay.Markers)
            {
                var x = XOf(bar, Replay.Ticks[Mathf.Clamp(mk.Frame, 0, Replay.Frames - 1)]);
                switch (mk.Kind)
                {
                    case MatchReplay.MarkerKind.Goal:
                        var gc = mk.Team >= 0 ? Palette.Team(mk.Team) : Color.white;
                        Ui.Box(new Rect(x - 2f, bar.y - 14f, 4f, bar.height + 28f), new Color(gc.r, gc.g, gc.b, a), 2f);
                        Ui.Box(new Rect(x - 7f, bar.y - 24f, 14f, 14f), new Color(gc.r, gc.g, gc.b, a), 7f);
                        break;
                    case MatchReplay.MarkerKind.Save:
                        Ui.Box(new Rect(x - 1.5f, bar.y - 6f, 3f, bar.height + 12f), new Color(1f, 1f, 1f, 0.85f * a), 1.5f);
                        break;
                    default:
                        Ui.Box(new Rect(x - 3f, bar.y + bar.height / 2f - 3f, 6f, 6f), new Color(0.7f, 0.7f, 0.75f, a), 3f);
                        break;
                }
            }
            // the playhead
            Ui.Box(new Rect(head - 8f, bar.y - 5f, 16f, bar.height + 10f), new Color(1f, 1f, 1f, a), 8f);

            // a little key under the bar
            var ky = bar.yMax + 12f;
            Ui.Box(new Rect(bar.x, ky + 5f, 10f, 10f), Palette.Team(0), 5f);
            Ui.Label(new Rect(bar.x + 16f, ky, 80f, 20f), "Goal", Ui.Font(12), new Color(1f, 1f, 1f, 0.6f * a));
            Ui.Box(new Rect(bar.x + 70f, ky + 3f, 3f, 14f), Color.white, 1.5f);
            Ui.Label(new Rect(bar.x + 80f, ky, 80f, 20f), "Save", Ui.Font(12), new Color(1f, 1f, 1f, 0.6f * a));
            Ui.Box(new Rect(bar.x + 130f, ky + 7f, 6f, 6f), new Color(0.7f, 0.7f, 0.75f), 3f);
            Ui.Label(new Rect(bar.x + 142f, ky, 160f, 20f), "Cards (skipped)", Ui.Font(12), new Color(1f, 1f, 1f, 0.6f * a));

            // the mouse: click or drag along the bar (or a clip's handles)
            var e = Event.current;
            var hit = new Rect(bar.x - 10f, bar.y - 26f, bar.width + 20f, bar.height + 46f);
            if (e.type == EventType.MouseDown && e.button == 0 && hit.Contains(Ui.Mouse))
            {
                _dragging = true;
                _dragWhat = 0;
                if (Clipping)
                {
                    if (Mathf.Abs(Ui.Mouse.x - XOf(bar, ClipFrom)) < 10f) _dragWhat = 1;
                    else if (Mathf.Abs(Ui.Mouse.x - XOf(bar, ClipTo)) < 10f) _dragWhat = 2;
                }
                e.Use();
            }
            if (_dragging)
            {
                var t = TickOf(bar, Ui.Mouse.x);
                if (_dragWhat == 1) ClipFrom = Mathf.Min(t, ClipTo - Tps);
                else if (_dragWhat == 2) ClipTo = Mathf.Max(t, ClipFrom + Tps);
                else { Tick = t; Fetch(true); }
                if (e.type == EventType.MouseUp || !Input_MouseHeld()) _dragging = false;
            }
        }

        private static bool Input_MouseHeld() { var m = Mouse.current; return m != null && m.leftButton.isPressed; }
    }
}
