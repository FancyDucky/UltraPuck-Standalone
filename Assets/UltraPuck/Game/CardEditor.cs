using System;
using System.Collections.Generic;
using UltraPuck.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UltraPuck.Game
{
    /// <summary>
    /// Free play's card editor: every card in a grid four wide (three rows on screen, scrolling), to try cards out.
    /// A adds a copy of the card you're on, B takes one away, Start applies the changes, and holding B cancels them.
    /// LB / RB switches between the people playing on this PC. The mouse works too: left click adds, right click
    /// takes away, the wheel scrolls.
    /// </summary>
    internal sealed class CardEditor : MonoBehaviour
    {
        public LocalPlayers Local;
        /// <summary>Card pictures (shared with the card screen's cache).</summary>
        public Func<string, Texture2D> Art;

        public bool IsOpen { get; private set; }

        private const int Cols = 4, VisibleRows = 3;
        private const float HoldToCancel = 0.6f;

        private Simulation _sim;
        private GameState _state;
        private readonly List<int> _people = new List<int>();
        private int _who;                        // index into _people
        private int[][] _counts;                 // per person, copies of each card (the edit, not applied yet)
        private int _focus, _top;                // the card you're on; the first row on screen
        private float _scrollY = -1f, _openedAt, _cancelHeld;
        private GUIStyle _text;

        /// <summary>Open the editor for everyone on this PC (not the bots) in <paramref name="s"/>.</summary>
        public void Open(Simulation sim, GameState s, IList<int> people)
        {
            if (sim.Cards == null || sim.Cards.Cards.Count == 0 || people.Count == 0) return;
            _sim = sim;
            _state = s;
            _people.Clear();
            _people.AddRange(people);
            _counts = new int[_people.Count][];
            for (var k = 0; k < _people.Count; k++)
            {
                _counts[k] = new int[sim.Cards.Cards.Count];
                foreach (var c in s.Players[_people[k]].Cards) if (c >= 0 && c < _counts[k].Length) _counts[k][c]++;
            }
            _who = 0;
            _focus = Mathf.Clamp(_focus, 0, sim.Cards.Cards.Count - 1);
            _scrollY = -1f;
            _cancelHeld = 0f;
            _openedAt = Time.unscaledTime;
            IsOpen = true;
        }

        private int Count { get { return _sim.Cards.Cards.Count; } }
        private int Rows { get { return (Count + Cols - 1) / Cols; } }

        private int MaxOf(int card)
        {
            var m = _sim.Cards.Cards[card].MaxPerPlayer;
            return m > 0 ? m : 9;
        }

        // ------------------------------------------------------------------ input

        /// <summary>One frame of the editor (the match stays paused while it's open).</summary>
        public void Tick()
        {
            if (!IsOpen) return;
            if (_state == null || _people.Exists(i => i >= _state.Players.Length)) { IsOpen = false; return; }
            var m = Local.MenuJoined();
            if (m.Tab != 0 && _people.Count > 1) _who = (_who + m.Tab + _people.Count) % _people.Count;
            if (m.X != 0) _focus = Mathf.Clamp(_focus + m.X, 0, Count - 1);
            if (m.Y != 0)
            {
                var to = _focus - m.Y * Cols;
                if (to >= 0 && to < Count) _focus = to;
                else if (m.Y < 0 && _focus / Cols < Rows - 1) _focus = Count - 1; // down onto a short last row
            }
            if (m.Confirm) Add(_focus, 1);
            if (m.Back) Add(_focus, -1);

            // hold B (or Escape) to cancel; Start (or Enter) to apply
            var held = false;
            foreach (var s in Local.Slots)
            {
                if (s.Demo) continue;
                var g = s.Device as Gamepad;
                if (g != null && g.buttonEast.isPressed) held = true;
                var kb = s.Device as Keyboard;
                if (kb != null && (kb.escapeKey.isPressed || kb.backspaceKey.isPressed)) held = true;
            }
            _cancelHeld = held ? _cancelHeld + Time.unscaledDeltaTime : 0f;
            if (_cancelHeld >= HoldToCancel) { Close(false); return; }
            if (Local.StartPressed()) Close(true);
        }

        private void Add(int card, int d)
        {
            var c = _counts[_who];
            c[card] = Mathf.Clamp(c[card] + d, 0, MaxOf(card));
        }

        private void Close(bool apply)
        {
            IsOpen = false;
            foreach (var s in Local.Slots) s.Latched = 0; // the buttons pressed here don't carry into play
            if (!apply) return;
            for (var k = 0; k < _people.Count; k++)
            {
                var p = _state.Players[_people[k]];
                // keep the cards they still have in the order they got them, then the new ones
                var left = (int[])_counts[k].Clone();
                var list = new List<int>();
                foreach (var c in p.Cards) if (c >= 0 && c < left.Length && left[c] > 0) { list.Add(c); left[c]--; }
                for (var c = 0; c < left.Length; c++) for (var n = 0; n < left[c]; n++) list.Add(c);
                p.Cards = list.ToArray();
            }
            _sim.Cards.Recompute(_state);
            _sim.PlacePylons(_state); // Pylon Goalies show up (or go) straight away
        }

        // ------------------------------------------------------------------ drawing

        private void OnGUI()
        {
            if (!IsOpen) return;
            GUI.depth = 1;
            Ui.Begin();
            float w = Ui.W, h = Ui.H;
            Ui.Box(new Rect(0, 0, w, h), new Color(0.04f, 0.04f, 0.05f, 0.94f), 0f);
            var appear = Ui.EaseOut((Time.unscaledTime - _openedAt) / 0.25f);
            var who = _people[_who];
            var name = who < Local.Slots.Count && !string.IsNullOrEmpty(Local.Slots[who].Name) ? Local.Slots[who].Name : "P" + (who + 1);
            var total = 0;
            foreach (var n in _counts[_who]) total += n;
            PlayScreens.Title(w, "CARDS", name + "   ·   " + (total == 1 ? "1 card" : total + " cards") + (_people.Count > 1 ? "   ·   LB / RB: another player" : ""), appear);

            // the grid: three rows on screen, scrolled to keep the card you're on in view
            const float gap = 14f;
            var cw = Mathf.Min(260f, (w - 120f - gap * (Cols - 1)) / Cols);
            var top = 128f;
            var ch = Mathf.Min(170f, (h - top - 70f - gap * (VisibleRows - 1)) / VisibleRows);
            var row = _focus / Cols;
            if (row < _top) _top = row;
            if (row >= _top + VisibleRows) _top = row - VisibleRows + 1;
            _top = Mathf.Clamp(_top, 0, Mathf.Max(0, Rows - VisibleRows));
            var want = _top * (ch + gap);
            _scrollY = _scrollY < 0f ? want : Mathf.Lerp(_scrollY, want, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 18f));
            var x0 = w / 2 - (cw * Cols + gap * (Cols - 1)) / 2;
            var view = new Rect(x0 - 8f, top - 8f, cw * Cols + gap * (Cols - 1) + 16f, VisibleRows * (ch + gap) - gap + 16f);

            var e = Event.current;
            var mouse = Ui.Mouse;
            if (e.type == EventType.ScrollWheel && view.Contains(mouse))
            {
                var r0 = Mathf.Clamp(_focus / Cols + (e.delta.y > 0 ? 1 : -1), 0, Rows - 1);
                _focus = Mathf.Min(Count - 1, r0 * Cols + _focus % Cols);
                e.Use();
            }

            GUI.BeginGroup(view);
            for (var i = 0; i < Count; i++)
            {
                var r = new Rect(8f + (i % Cols) * (cw + gap), 8f + (i / Cols) * (ch + gap) - _scrollY, cw, ch);
                if (r.yMax < 0f || r.y > view.height) continue;
                var local = new Vector2(mouse.x - view.x, mouse.y - view.y);
                if (r.Contains(local) && local.y >= 0f && local.y <= view.height)
                {
                    if (Ui.MouseMoved) _focus = i;
                    if (e.type == EventType.MouseDown && (e.button == 0 || e.button == 1)) { _focus = i; Add(i, e.button == 0 ? 1 : -1); e.Use(); }
                }
                Tile(r, i, i == _focus);
            }
            GUI.EndGroup();

            // the scroll bar
            if (Rows > VisibleRows)
            {
                var track = new Rect(view.xMax + 10f, view.y + 8f, 6f, view.height - 16f);
                Ui.Box(track, new Color(1f, 1f, 1f, 0.1f), 3f);
                var span = (Rows - VisibleRows) * (ch + gap);
                var k = span > 0f ? Mathf.Clamp01(_scrollY / span) : 0f;
                var thumbH = track.height * VisibleRows / Rows;
                Ui.Box(new Rect(track.x, track.y + (track.height - thumbH) * k, track.width, thumbH), new Color(1f, 1f, 1f, 0.6f), 3f);
            }

            Glyphs.Hint(new Rect(0, h - 44, w, 28), "{nav} Card      {confirm} Add      {back} Remove      {start} Apply      Hold {back} Cancel", 16, new Color(1f, 1f, 1f, 0.8f));
            Hud.LeavingBar(new Rect(w / 2 - 150, h - 78, 300, 22), Mathf.Clamp01(_cancelHeld / HoldToCancel), "Cancel the changes");
        }

        private void Tile(Rect r, int i, bool focused)
        {
            var def = _sim.Cards.Cards[i];
            var n = _counts[_who][i];
            Color accent;
            if (string.IsNullOrEmpty(def.Color) || !ColorUtility.TryParseHtmlString(def.Color, out accent)) accent = new Color(0.6f, 0.6f, 0.65f);
            var owned = n > 0;
            if (focused) Ui.Box(new Rect(r.x - 4, r.y - 4, r.width + 8, r.height + 8), Color.white, 16f);
            Ui.Box(r, owned ? Palette.Darker(accent, 0.45f) : new Color(0.12f, 0.12f, 0.14f, 1f), 13f);
            // the picture, small, at the left; the name and tier along the top
            var art = Art != null ? Art(def.Art) : null;
            var pic = new Rect(r.x + 10, r.y + 10, 52, 52);
            Ui.Box(new Rect(pic.x - 2, pic.y - 2, pic.width + 4, pic.height + 4), accent, 8f);
            if (art != null && Event.current.type == EventType.Repaint)
            {
                var old = GUI.color; GUI.color = owned || focused ? Color.white : new Color(1f, 1f, 1f, 0.6f);
                GUI.DrawTexture(pic, art, ScaleMode.ScaleAndCrop);
                GUI.color = old;
            }
            var a = owned || focused ? 1f : 0.7f;
            Ui.Label(new Rect(r.x + 72, r.y + 8, r.width - 82, 30), def.Name, Ui.Font(18, true), new Color(1f, 1f, 1f, a));
            var max = def.MaxPerPlayer > 0 ? "   max " + def.MaxPerPlayer : "";
            Ui.Label(new Rect(r.x + 72, r.y + 34, r.width - 82, 22), "Tier " + def.Tier + max, Ui.Font(13), new Color(1f, 1f, 1f, 0.55f * a));
            // what it does
            if (_text == null) _text = new GUIStyle(Ui.Font(12)) { wordWrap = true, richText = true, alignment = TextAnchor.UpperLeft };
            var old2 = GUI.color; GUI.color = new Color(1f, 1f, 1f, a);
            GUI.Label(new Rect(r.x + 10, r.y + 68, r.width - 20, r.height - 74), Markup(def.Text), _text);
            GUI.color = old2;
            // how many copies
            if (owned)
            {
                var badge = new Rect(r.xMax - 46, r.y - 8, 52, 28);
                Ui.Box(badge, Color.white, 14f);
                Ui.Label(badge, "×" + n, Ui.Font(17, true, TextAnchor.MiddleCenter), new Color(0.08f, 0.08f, 0.09f), false);
            }
        }

        private static string Markup(string s)
        {
            return (s ?? "").Replace("[good]", "<color=#7BE38A>").Replace("[bad]", "<color=#FF7A7A>").Replace("[note]", "<color=#A0A0A8>").Replace("[/]", "</color>");
        }
    }
}
