using System;
using System.Collections.Generic;
using System.IO;
using UltraPuck.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace UltraPuck.Game
{
    /// <summary>The rinks made in the rink maker: map files in the Custom_Rinks folder next to the game.</summary>
    internal static class CustomRinks
    {
        public static string Folder
        {
            get
            {
                // (screenshot runs use their own folder: ULTRAPUCK_RINK_DIR)
                var dir = Environment.GetEnvironmentVariable("ULTRAPUCK_RINK_DIR");
                return !string.IsNullOrEmpty(dir) ? dir : Path.Combine(Path.GetDirectoryName(Application.dataPath), "Custom_Rinks");
            }
        }

        public sealed class Entry
        {
            /// <summary>The file's name in the folder.</summary>
            public string File;
            public RinkPlan Plan;
            /// <summary>The rink as a map, or null while it's unfinished (see <see cref="Problem"/>).</summary>
            public MapDef Map;
            public string Problem;
            public DateTime When;
        }

        /// <summary>Every rink in the folder, the newest first.</summary>
        public static List<Entry> Load()
        {
            var list = new List<Entry>();
            try
            {
                if (!Directory.Exists(Folder)) return list;
                foreach (var f in Directory.GetFiles(Folder, "*.json"))
                {
                    try
                    {
                        var text = System.IO.File.ReadAllText(f);
                        var plan = RinkPlan.FromJson(text);
                        if (plan == null) { Debug.LogWarning("Ultra Puck: " + Path.GetFileName(f) + " isn't a rink from the rink maker"); continue; }
                        var e = new Entry { File = Path.GetFileName(f), Plan = plan, Problem = plan.Problem(), When = System.IO.File.GetLastWriteTimeUtc(f) };
                        if (e.Problem == null)
                        {
                            try { e.Map = MapDef.FromJson(text); }
                            catch (Exception ex) { e.Problem = "Couldn't load it: " + ex.Message; }
                        }
                        list.Add(e);
                    }
                    catch (Exception ex) { Debug.LogWarning("Ultra Puck: couldn't read the rink " + Path.GetFileName(f) + ": " + ex.Message); }
                }
            }
            catch (Exception ex) { Debug.LogWarning("Ultra Puck: couldn't read " + Folder + ": " + ex.Message); }
            list.Sort((a, b) => b.When != a.When ? b.When.CompareTo(a.When) : string.CompareOrdinal(a.File, b.File));
            return list;
        }

        /// <summary>Writes the rink (to <paramref name="file"/>, or a new file named after it) and returns the file's name, or null.</summary>
        public static string Save(RinkPlan plan, string file)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                if (string.IsNullOrEmpty(file)) file = FreeFile(plan.Name);
                System.IO.File.WriteAllText(Path.Combine(Folder, file), plan.ToFile());
                return file;
            }
            catch (Exception ex) { Debug.LogWarning("Ultra Puck: couldn't save the rink: " + ex.Message); return null; }
        }

        public static void Delete(string file)
        {
            try { System.IO.File.Delete(Path.Combine(Folder, file)); }
            catch (Exception ex) { Debug.LogWarning("Ultra Puck: couldn't delete " + file + ": " + ex.Message); }
        }

        /// <summary>A name for a new rink no other rink has yet ("My Rink 3").</summary>
        public static string NewName(List<Entry> have)
        {
            for (var n = 1; ; n++)
            {
                var name = "My Rink " + n;
                if (!have.Exists(e => string.Equals(e.Plan.Name, name, StringComparison.OrdinalIgnoreCase))) return name;
            }
        }

        private static string FreeFile(string name)
        {
            var stem = "";
            foreach (var c in name ?? "") stem += char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' ? c : '_';
            stem = stem.Trim();
            if (stem.Length == 0) stem = "rink";
            var file = stem + ".json";
            for (var n = 2; System.IO.File.Exists(Path.Combine(Folder, file)); n++) file = stem + " " + n + ".json";
            return file;
        }
    }

    /// <summary>
    /// The rink maker, in the style of a level maker: paint ice on a grid, drop the two nets and the puck's spot, test
    /// it straight away against a bot, save it. Works with a controller (a cursor on the left stick or d-pad) and with
    /// a mouse and keyboard. Also the Custom Rinks screen: a grid of the rinks made so far, a + first to start a new one
    /// (from the main menu), or to pick one to play on (from the rink screen, LB / RB).
    /// </summary>
    internal sealed class RinkMaker : MonoBehaviour
    {
        public enum State { Closed, Library, Editing, Testing }
        public State Current { get; private set; }

        public GameRunner Runner;
        /// <summary>The Custom Rinks screen was opened from the rink screen, to pick one to play on (not to make one).</summary>
        public bool PickMode { get; private set; }

        // ------------------------------------------------------------------ the parts

        private enum Part { Ice, BigIce, Wall, Eraser, RedNet, BlueNet, Spot }
        private static readonly string[] PartNames = { "Ice", "Big ice", "Wall", "Eraser", "Red net", "Blue net", "Puck spot" };
        private Part _part;
        private bool _mirror = true;

        private RinkPlan _plan;
        private string _file;
        private RinkPlan _saved; // as last saved (or opened), to tell whether there are changes
        private readonly List<RinkPlan> _undo = new List<RinkPlan>(), _redo = new List<RinkPlan>();
        private const int UndoMax = 100;

        // the picture of the rink being made: the game's own drawing of it, rebuilt (a few times a second at most) as it changes
        private RinkView _view;
        private bool _dirty;
        private float _builtAt;
        private bool[] _kept;
        private string _problem;

        // the cursor: where on the grid (game units); squares for painting, corners for the nets and the spot
        private Vector2 _at;
        private Vector2 _stickCarry;
        private float _repeatAt;
        private Vector2Int _lastCell, _lastCorner;
        private bool _painting, _erasing, _changed;
        private Camera _cam;
        private float _openedAt;

        // the menu (Select / View / Esc)
        private bool _menu;
        private int _menuRow;
        private readonly List<KeyValuePair<string, Action>> _menuRows = new List<KeyValuePair<string, Action>>();
        private bool _naming;
        private string _nameBuffer;

        private string _say;
        private float _sayAt;

        // ------------------------------------------------------------------ opening and closing

        public void OpenLibrary(bool pick)
        {
            PickMode = pick;
            Current = State.Library;
            _entries = CustomRinks.Load();
            if (pick) _entries.RemoveAll(e => e.Map == null); // (to play on: only the finished ones)
            foreach (var t in _thumbs.Values) if (t != null) Destroy(t);
            _thumbs.Clear();
            _libFocus = Mathf.Clamp(_libFocus, 0, Mathf.Max(0, LibCount - 1));
            _deleteArmed = -1;
            _openedAt = Time.unscaledTime;
        }

        public void CloseLibrary() { if (Current == State.Library) Current = State.Closed; }

        /// <summary>Start making a rink: <paramref name="file"/> null for a new one.</summary>
        public void OpenEditor(RinkPlan plan, string file)
        {
            _plan = plan.Copy();
            _file = file;
            _saved = file != null ? plan.Copy() : null;
            _undo.Clear(); _redo.Clear();
            _part = Part.Ice;
            try { _mirror = PlayerPrefs.GetInt("rinkmaker_mirror", 1) == 1; } catch { _mirror = true; }
            _at = new Vector2(0.5f, 0.5f);
            _menu = false; _naming = false;
            _painting = _erasing = false;
            _dirty = true;
            Current = State.Editing;
            _openedAt = Time.unscaledTime;
        }

        /// <summary>Back from testing the rink: carry on where it was left.</summary>
        public void ResumeEditing()
        {
            Current = State.Editing;
            _dirty = true;
            _painting = _erasing = false;
            _openedAt = Time.unscaledTime;
        }

        /// <summary>Testing: the maker hides (its picture too) while the rink is played.</summary>
        public void StartTesting()
        {
            Current = State.Testing;
            DropView();
        }

        private void Leave()
        {
            DropView();
            Current = State.Closed;
            Runner.MakerClosed();
        }

        private void DropView()
        {
            if (_view != null) { Destroy(_view.Root); _view = null; }
        }

        private bool Changed { get { return _saved == null || !_plan.SameAs(_saved) || _plan.Name != _saved.Name; } }

        private void Say(string text) { _say = text; _sayAt = Time.unscaledTime; }

        // ------------------------------------------------------------------ input helpers

        private static Keyboard Kb { get { return LocalPlayers.Blocked ? null : Keyboard.current; } }
        private static Mouse Ms { get { return LocalPlayers.Blocked ? null : Mouse.current; } }

        private static bool PadPressed(Func<Gamepad, ButtonControl> b)
        {
            if (LocalPlayers.Blocked) return false;
            foreach (var p in Gamepad.all) if (b(p).wasPressedThisFrame) return true;
            return false;
        }

        private static bool PadHeld(Func<Gamepad, ButtonControl> b)
        {
            if (LocalPlayers.Blocked) return false;
            foreach (var p in Gamepad.all) if (b(p).isPressed) return true;
            return false;
        }

        private static Vector2 Stick()
        {
            var best = Vector2.zero;
            if (LocalPlayers.Blocked) return best;
            foreach (var p in Gamepad.all) { var v = p.leftStick.ReadValue(); if (v.sqrMagnitude > best.sqrMagnitude) best = v; }
            return best;
        }

        /// <summary>A step from the d-pad, the arrow keys or WASD (pressed, then repeating while held).</summary>
        private Vector2Int Steps()
        {
            var held = Vector2Int.zero;
            var pressed = false;
            var kb = Kb;
            if (kb != null && !(kb.ctrlKey.isPressed))
            {
                if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) held.x -= 1;
                if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) held.x += 1;
                if (kb.upArrowKey.isPressed || kb.wKey.isPressed) held.y += 1;
                if (kb.downArrowKey.isPressed || kb.sKey.isPressed) held.y -= 1;
                pressed |= kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame
                           || kb.aKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame;
            }
            if (!LocalPlayers.Blocked)
                foreach (var p in Gamepad.all)
                {
                    if (p.dpad.left.isPressed) held.x -= 1;
                    if (p.dpad.right.isPressed) held.x += 1;
                    if (p.dpad.up.isPressed) held.y += 1;
                    if (p.dpad.down.isPressed) held.y -= 1;
                    pressed |= p.dpad.left.wasPressedThisFrame || p.dpad.right.wasPressedThisFrame || p.dpad.up.wasPressedThisFrame || p.dpad.down.wasPressedThisFrame;
                }
            held.x = Math.Sign(held.x); held.y = Math.Sign(held.y);
            if (held == Vector2Int.zero) return held;
            if (pressed) { _repeatAt = Time.unscaledTime + 0.28f; return held; }
            if (Time.unscaledTime >= _repeatAt) { _repeatAt = Time.unscaledTime + 0.045f; return held; }
            return Vector2Int.zero;
        }

        /// <summary>The last device used: a controller (if one was), else the keyboard.</summary>
        private static InputDevice LastDevice()
        {
            if (InputMode.Pad)
            {
                Gamepad best = null;
                foreach (var p in Gamepad.all) if (best == null || p.lastUpdateTime > best.lastUpdateTime) best = p;
                if (best != null) return best;
            }
            return Keyboard.current;
        }

        // ------------------------------------------------------------------ every frame

        public void Tick()
        {
            if (Current == State.Library) { LibraryTick(); return; }
            if (Current != State.Editing) return;
            if (_naming) { NameTick(); return; }
            if (_menu) { MenuTick(); return; }
            EditTick();
            Rebuild();
        }

        private void EditTick()
        {
            var kb = Kb;
            var ms = Ms;
            var ready = Time.unscaledTime - _openedAt > 0.25f; // the press that opened the maker doesn't paint

            // the menu, testing
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || PadPressed(p => p.selectButton)) { OpenMenu(); return; }
            if ((kb != null && kb.pKey.wasPressedThisFrame) || PadPressed(p => p.startButton)) { Test(); return; }

            // parts: LB / RB, Q / E, the wheel, 1-6
            var step = 0;
            if (PadPressed(p => p.leftShoulder) || (kb != null && kb.qKey.wasPressedThisFrame)) step = -1;
            if (PadPressed(p => p.rightShoulder) || (kb != null && kb.eKey.wasPressedThisFrame)) step = 1;
            if (ms != null && !_painting && !_erasing)
            {
                var wheel = ms.scroll.ReadValue().y;
                if (wheel > 0.1f) step = -1; else if (wheel < -0.1f) step = 1;
            }
            if (step != 0) SetPart((Part)(((int)_part + step + PartNames.Length) % PartNames.Length));
            if (kb != null)
            {
                var digits = new[] { kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key, kb.digit6Key, kb.digit7Key };
                for (var k = 0; k < digits.Length; k++) if (digits[k].wasPressedThisFrame) SetPart((Part)k);
            }

            // mirror (Y / M), undo (X / Ctrl+Z), redo (Ctrl+Y)
            if (PadPressed(p => p.buttonNorth) || (kb != null && kb.mKey.wasPressedThisFrame)) ToggleMirror();
            if (PadPressed(p => p.buttonWest) || (kb != null && kb.ctrlKey.isPressed && kb.zKey.wasPressedThisFrame && !kb.shiftKey.isPressed)) Undo();
            if (kb != null && kb.ctrlKey.isPressed && (kb.yKey.wasPressedThisFrame || (kb.zKey.wasPressedThisFrame && kb.shiftKey.isPressed))) Redo();

            // the cursor: the mouse where it is; the stick smoothly; the d-pad and keys a square at a time
            var mouseOverBar = false;
            if (ms != null && _cam != null)
            {
                var g = Ui.Mouse;
                mouseOverBar = g.y < BarBottom || g.y > Ui.H - HintBand;
                if (Ui.MouseMovedNow && !mouseOverBar) _at = Clamp(MouseWorld());
            }
            var stick = Stick();
            if (stick.magnitude > 0.25f)
            {
                var speed = Mathf.Lerp(6f, 38f, Mathf.InverseLerp(0.25f, 1f, stick.magnitude));
                _at = Clamp(_at + stick.normalized * speed * Mathf.Min(Time.unscaledDeltaTime, 0.05f));
            }
            var steps = Steps();
            if (steps != Vector2Int.zero) _at = Snap(Clamp(_at + (Vector2)steps));

            // place / paint: A, left click, Space or Enter (held to paint); erase: B, right click, Backspace or Delete
            var mouseLeft = ms != null && !mouseOverBar && ms.leftButton.isPressed;
            var mouseRight = ms != null && !mouseOverBar && ms.rightButton.isPressed;
            var place = ready && (PadHeld(p => p.buttonSouth) || mouseLeft || (kb != null && (kb.spaceKey.isPressed || kb.enterKey.isPressed)));
            var erase = ready && (PadHeld(p => p.buttonEast) || mouseRight || (kb != null && (kb.backspaceKey.isPressed || kb.deleteKey.isPressed)));
            if (place && _part == Part.Eraser) { erase = true; place = false; }
            if (place && !_painting) { Begin(); _painting = true; Act(false, true); }
            else if (place && _painting) Act(false, false);
            else if (!place && _painting) { _painting = false; End(); }
            if (!place)
            {
                if (erase && !_erasing) { Begin(); _erasing = true; Act(true, true); }
                else if (erase && _erasing) Act(true, false);
                else if (!erase && _erasing) { _erasing = false; End(); }
            }
        }

        private void SetPart(Part p)
        {
            if (p == _part) return;
            _part = p;
            Sound.Move();
            if (!MouseInUse()) _at = Snap(_at);
        }

        private static bool MouseInUse() { return !InputMode.Pad; }

        private void ToggleMirror()
        {
            _mirror = !_mirror;
            try { PlayerPrefs.SetInt("rinkmaker_mirror", _mirror ? 1 : 0); } catch { }
            Sound.Select();
            Say(_mirror ? "Mirror on: both halves match" : "Mirror off");
        }

        private Vector2 MouseWorld()
        {
            var m = Ms.position.ReadValue();
            var w = _cam.ScreenToWorldPoint(new Vector3(m.x, m.y, -_cam.transform.position.z));
            return new Vector2(w.x, w.y);
        }

        private static Vector2 Clamp(Vector2 v)
        {
            return new Vector2(Mathf.Clamp(v.x, RinkPlan.MinX + 0.01f, RinkPlan.MaxX + 0.99f), Mathf.Clamp(v.y, RinkPlan.MinY + 0.01f, RinkPlan.MaxY + 0.99f));
        }

        /// <summary>Onto the middle of a square (painting) or a corner (nets, the spot).</summary>
        private Vector2 Snap(Vector2 v)
        {
            if (CornerPart) return Clamp(new Vector2(Mathf.Round(v.x), Mathf.Round(v.y)));
            return new Vector2(Mathf.Floor(v.x) + 0.5f, Mathf.Floor(v.y) + 0.5f);
        }

        private bool CornerPart { get { return _part == Part.RedNet || _part == Part.BlueNet || _part == Part.Spot || _part == Part.Wall; } }
        private Vector2Int Cell { get { return new Vector2Int(Mathf.FloorToInt(_at.x), Mathf.FloorToInt(_at.y)); } }
        private Vector2Int Corner { get { return new Vector2Int(Mathf.RoundToInt(_at.x), Mathf.RoundToInt(_at.y)); } }

        // ------------------------------------------------------------------ changing the rink

        private void Begin()
        {
            _undo.Add(_plan.Copy());
            if (_undo.Count > UndoMax) _undo.RemoveAt(0);
            _redo.Clear();
            _changed = false;
            _lastCell = Cell;
            _lastCorner = Corner;
        }

        private void End()
        {
            if (!_changed && _undo.Count > 0) _undo.RemoveAt(_undo.Count - 1);
            _dirty = true;
        }

        private void Undo()
        {
            if (_undo.Count == 0) { Sound.Deny(); return; }
            _redo.Add(_plan.Copy());
            _plan = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _dirty = true;
            Sound.Back();
        }

        private void Redo()
        {
            if (_redo.Count == 0) { Sound.Deny(); return; }
            _undo.Add(_plan.Copy());
            _plan = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            _dirty = true;
            Sound.Move();
        }

        /// <summary>The part at the cursor: <paramref name="first"/> on the press (nets and the spot only then), then along the drag.</summary>
        private void Act(bool erase, bool first)
        {
            if (erase && _part == Part.Wall) { WallAlong(false); return; } // with the wall part, erasing takes walls away
            if (erase)
            {
                // a net or the spot under the cursor goes first; otherwise the ice
                if (first && EraseThing()) return;
                PaintAlong(false);
                return;
            }
            switch (_part)
            {
                case Part.Ice: case Part.BigIce: PaintAlong(true); break;
                case Part.Wall: WallAlong(true); break;
                case Part.RedNet: case Part.BlueNet: if (first) PlaceNet(_part == Part.RedNet ? 0 : 1); break;
                case Part.Spot: if (first) PlaceSpot(); break;
            }
        }

        /// <summary>Paints (or clears) every square from the last one to the cursor's, so a quick drag leaves no gaps.</summary>
        private void PaintAlong(bool ice)
        {
            var to = Cell;
            var from = _lastCell;
            var n = Mathf.Max(Mathf.Abs(to.x - from.x), Mathf.Abs(to.y - from.y));
            for (var k = n == 0 ? 0 : 1; k <= n; k++)
            {
                var c = n == 0 ? to : new Vector2Int(Mathf.RoundToInt(Mathf.Lerp(from.x, to.x, (float)k / n)), Mathf.RoundToInt(Mathf.Lerp(from.y, to.y, (float)k / n)));
                Brush(c, ice);
            }
            _lastCell = to;
        }

        /// <summary>
        /// Draws (or takes away) wall lines along the grid from the last corner to the cursor's: across first, then up or
        /// down. A wall only goes where there's ice both sides and no net.
        /// </summary>
        private void WallAlong(bool on)
        {
            var to = Corner;
            var at = _lastCorner;
            var any = false;
            var refused = false;
            while (at != to)
            {
                bool up;
                int ex, ey;
                if (at.x != to.x) { var nx = at.x + Math.Sign(to.x - at.x); up = false; ex = Mathf.Min(at.x, nx); ey = at.y; at = new Vector2Int(nx, at.y); }
                else { var ny = at.y + Math.Sign(to.y - at.y); up = true; ex = at.x; ey = Mathf.Min(at.y, ny); at = new Vector2Int(at.x, ny); }
                if (on && !_plan.WallFits(ex, ey, up)) { refused = true; continue; }
                any |= SetWall(ex, ey, up, on);
                if (_mirror)
                {
                    var m = RinkPlan.MirrorEdge(ex, ey, up);
                    if (!on || _plan.WallFits(m[0], m[1], up)) any |= SetWall(m[0], m[1], up, on);
                }
            }
            _lastCorner = to;
            if (any)
            {
                _changed = true;
                _dirty = true;
                Sound.Play(Sfx.UiMove, 0.2f, on ? 0.95f + UnityEngine.Random.value * 0.1f : 0.75f, 0f, 0.05f);
            }
            else if (refused && Time.unscaledTime - _sayAt > 2.5f) Say("Walls go on the ice, not through a net");
        }

        private bool SetWall(int x, int y, bool up, bool on)
        {
            if (_plan.Wall(x, y, up) == on) return false;
            _plan.SetWall(x, y, up, on);
            return true;
        }

        private void Brush(Vector2Int c, bool ice)
        {
            var big = _part == Part.BigIce;
            var r = big ? 1 : 0;
            var any = false;
            for (var dy = -r; dy <= r; dy++)
                for (var dx = -r; dx <= r; dx++)
                {
                    any |= Set(c.x + dx, c.y + dy, ice);
                    if (_mirror) any |= Set(RinkPlan.MirrorX(c.x + dx), c.y + dy, ice);
                }
            if (any)
            {
                _changed = true;
                _dirty = true;
                Sound.Play(Sfx.UiMove, 0.18f, ice ? 1.25f + UnityEngine.Random.value * 0.1f : 0.8f, 0f, 0.05f);
            }
        }

        private bool Set(int cx, int cy, bool ice)
        {
            if (!RinkPlan.InGrid(cx, cy) || _plan.Ice(cx, cy) == ice) return false;
            if (!ice && _plan.UnderNet(cx, cy)) return false; // the nets keep their ice (take the net away first)
            _plan.SetIce(cx, cy, ice);
            if (!ice)
            {
                // the square's walls go with it
                _plan.SetWall(cx, cy, false, false); _plan.SetWall(cx, cy + 1, false, false);
                _plan.SetWall(cx, cy, true, false); _plan.SetWall(cx + 1, cy, true, false);
            }
            return true;
        }

        private void PlaceNet(int team)
        {
            var c = Corner;
            var old = _plan.Nets[team];
            var n = new RinkPlan.Net { Placed = true, X = c.x, Y = c.y };
            // facing the middle; on the same spot again: turned a quarter
            if (old.Placed && old.X == n.X && old.Y == n.Y) n.Dir = (old.Dir + 1) % 4;
            else n.Dir = c.x < 0 ? 0 : c.x > 0 ? 2 : (old.Placed ? old.Dir : 1);
            var other = _plan.Nets[1 - team];
            RinkPlan.Net twin = null;
            if (_mirror)
            {
                twin = RinkPlan.Mirror(n);
                if (!_plan.NetFits(twin, n)) { Sound.Deny(); Say("No room for its mirror on the other side"); return; }
                other = twin;
            }
            if (!_plan.NetFits(n, other)) { Sound.Deny(); Say("A net needs ice under it and room in front"); return; }
            _plan.Nets[team] = n;
            if (twin != null) _plan.Nets[1 - team] = twin;
            _changed = true;
            _dirty = true;
            Sound.Select();
        }

        private void PlaceSpot()
        {
            var c = Corner;
            if (_mirror) c.x = 0;
            if (!(_plan.Ice(c.x, c.y) && _plan.Ice(c.x - 1, c.y) && _plan.Ice(c.x, c.y - 1) && _plan.Ice(c.x - 1, c.y - 1))) { Sound.Deny(); Say("The puck's spot has to be on the ice"); return; }
            _plan.SpotPlaced = true;
            _plan.SpotX = c.x;
            _plan.SpotY = c.y;
            _changed = true;
            _dirty = true;
            Sound.Select();
        }

        /// <summary>Erasing over a net takes the net away (both, with the mirror on); over the spot, puts it back half way between the nets.</summary>
        private bool EraseThing()
        {
            var cell = Cell;
            for (var t = 0; t < 2; t++)
            {
                var n = _plan.Nets[t];
                if (!n.Placed) continue;
                foreach (var c in RinkPlan.NetCells(n))
                {
                    if (c[0] != cell.x || c[1] != cell.y) continue;
                    n.Placed = false;
                    if (_mirror) _plan.Nets[1 - t].Placed = false;
                    _changed = true; _dirty = true;
                    Sound.Back();
                    return true;
                }
            }
            if (_plan.SpotPlaced && Mathf.Abs(_at.x - _plan.SpotX) < 0.8f && Mathf.Abs(_at.y - _plan.SpotY) < 0.8f)
            {
                _plan.SpotPlaced = false;
                _changed = true; _dirty = true;
                Sound.Back();
                return true;
            }
            return false;
        }

        /// <summary>Redraws the rink after a change (a few times a second at most while painting).</summary>
        private void Rebuild()
        {
            if (!_dirty) return;
            if ((_painting || _erasing) && Time.unscaledTime - _builtAt < 0.08f) return;
            _dirty = false;
            _builtAt = Time.unscaledTime;
            _kept = _plan.Kept();
            _problem = _plan.Problem();
            DropView();
            MapDef map = null;
            try { map = _plan.PreviewMap(); }
            catch (Exception ex) { Debug.LogWarning("Ultra Puck: rink maker picture: " + ex.Message); }
            if (map != null) _view = new RinkView(map, Runner.WallThickness);
        }

        // ------------------------------------------------------------------ testing, saving

        private void Test()
        {
            var problem = _plan.Problem();
            if (problem != null) { Sound.Deny(); Say(problem); return; }
            Sound.Select();
            Runner.TestRink(_plan.Copy(), LastDevice());
        }

        private bool Save()
        {
            var file = CustomRinks.Save(_plan, _file);
            if (file == null) { Sound.Deny(); Say("Couldn't save it (see the log)"); return false; }
            _file = file;
            _saved = _plan.Copy();
            Sound.Select();
            Say(_plan.Problem() == null ? "Saved" : "Saved (unfinished: " + _plan.Problem().ToLowerInvariant() + ")");
            return true;
        }

        private void OpenMenu()
        {
            _menu = true;
            _menuRow = 0;
            _menuRows.Clear();
            _menuRows.Add(new KeyValuePair<string, Action>("Keep building", () => { _menu = false; }));
            _menuRows.Add(new KeyValuePair<string, Action>("Test it", () => { _menu = false; Test(); }));
            _menuRows.Add(new KeyValuePair<string, Action>("Save", () => { _menu = false; Save(); }));
            _menuRows.Add(new KeyValuePair<string, Action>("Rename", () =>
            {
                if (Kb == null || InputMode.Pad) { Sound.Deny(); Say("Renaming needs a keyboard for now"); return; }
                _menu = false; _naming = true; _nameBuffer = _plan.Name; Sound.Select();
            }));
            if (Changed)
            {
                _menuRows.Add(new KeyValuePair<string, Action>("Save and leave", () => { _menu = false; if (Save()) Leave(); }));
                _menuRows.Add(new KeyValuePair<string, Action>("Leave without saving", () => { _menu = false; Sound.Back(); Leave(); }));
            }
            else _menuRows.Add(new KeyValuePair<string, Action>("Leave", () => { _menu = false; Sound.Back(); Leave(); }));
            Sound.Select();
        }

        private void MenuTick()
        {
            var kb = Kb;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || PadPressed(p => p.buttonEast) || PadPressed(p => p.selectButton)) { _menu = false; Sound.Back(); return; }
            var steps = Steps();
            var stick = Stick();
            if (steps.y != 0) { _menuRow = (_menuRow - steps.y + _menuRows.Count) % _menuRows.Count; Sound.Move(); }
            else if (Mathf.Abs(stick.y) > 0.6f && Time.unscaledTime >= _repeatAt) { _menuRow = (_menuRow + (stick.y > 0 ? -1 : 1) + _menuRows.Count) % _menuRows.Count; _repeatAt = Time.unscaledTime + 0.22f; Sound.Move(); }
            if (Ui.MouseMovedNow) for (var k = 0; k < _menuRows.Count; k++) if (MenuRect(k).Contains(Ui.Mouse)) _menuRow = k;
            var click = Ms != null && Ms.leftButton.wasPressedThisFrame && MenuRect(_menuRow).Contains(Ui.Mouse);
            if (click || PadPressed(p => p.buttonSouth) || (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))) _menuRows[_menuRow].Value();
        }

        private Rect MenuRect(int k)
        {
            var h = 46f;
            var top = Ui.H / 2f - _menuRows.Count * h / 2f + 20f;
            return new Rect(Ui.W / 2f - 180f, top + k * h, 360f, h - 6f);
        }

        private void NameTick()
        {
            var kb = Kb;
            if (kb == null) { _naming = false; return; }
            if (kb.escapeKey.wasPressedThisFrame) { _naming = false; Sound.Back(); return; }
            if (kb.enterKey.wasPressedThisFrame)
            {
                var n = _nameBuffer.Trim();
                if (n.Length > 0) { _plan.Name = n; Sound.Select(); }
                _naming = false;
                return;
            }
            if (kb.backspaceKey.wasPressedThisFrame && _nameBuffer.Length > 0) _nameBuffer = _nameBuffer.Substring(0, _nameBuffer.Length - 1);
        }

        private void OnEnable() { if (Keyboard.current != null) Keyboard.current.onTextInput += OnText; }
        private void OnDisable() { if (Keyboard.current != null) Keyboard.current.onTextInput -= OnText; }

        private void OnText(char c)
        {
            if (!_naming || char.IsControl(c) || _nameBuffer.Length >= 28) return;
            _nameBuffer += c;
        }

        // ------------------------------------------------------------------ the camera

        // the part bar along the top and a line of controls along the bottom (GUI units)
        private const float BarBottom = 96f, HintBand = 44f;

        /// <summary>Frames the whole grid between the part bar and the controls.</summary>
        public void Frame(Camera cam)
        {
            _cam = cam;
            if (cam == null) return;
            var guiH = 720f / Settings.UiScale;
            var free = guiH - BarBottom - HintBand;
            var perGui = Mathf.Max((RinkPlan.H + 2f) / free, (RinkPlan.W + 2f) / (guiH * Mathf.Max(0.5f, cam.aspect)));
            cam.orthographicSize = guiH * perGui / 2f;
            var mid = BarBottom + free / 2f;
            cam.transform.position = new Vector3(0f, (mid - guiH / 2f) * perGui, -10f);
        }

        private Vector2 ToGui(Vector2 world)
        {
            var s = _cam.WorldToScreenPoint(new Vector3(world.x, world.y, 0f));
            var k = Screen.height / 720f * Settings.UiScale;
            return new Vector2(s.x / k, (Screen.height - s.y) / k);
        }

        private Rect GuiRect(float x0, float y0, float x1, float y1)
        {
            Vector2 a = ToGui(new Vector2(x0, y1)), b = ToGui(new Vector2(x1, y0));
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }

        // ------------------------------------------------------------------ drawing

        private void OnGUI()
        {
            if (Current == State.Closed) return;
            GUI.depth = 2;
            Ui.Begin();
            if (Current == State.Library) { DrawLibrary(); return; }
            if (Current == State.Testing)
            {
                Glyphs.Hint(new Rect(16f, Ui.H - 40f, 600f, 28f), "Testing your rink      {menu} Back to the maker", 16, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleLeft);
                return;
            }
            if (_cam == null) return;
            DrawGrid();
            DrawBar();
            DrawHints();
            if (_say != null && Time.unscaledTime - _sayAt < 2.5f)
            {
                var a = Mathf.Clamp01(2.5f - (Time.unscaledTime - _sayAt));
                var r = new Rect(Ui.W / 2f - 260f, BarBottom + 8f, 520f, 38f);
                Ui.Box(r, new Color(0f, 0f, 0f, 0.7f * a), 10f);
                Ui.Label(r, _say, Ui.Font(17, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, a));
            }
            if (_menu) DrawMenu();
            if (_naming) DrawNaming();
        }

        private void DrawGrid()
        {
            // the grid's lines (every fifth a little brighter), the middle line when mirroring
            for (var x = RinkPlan.MinX; x <= RinkPlan.MaxX + 1; x++)
            {
                Vector2 a = ToGui(new Vector2(x, RinkPlan.MinY)), b = ToGui(new Vector2(x, RinkPlan.MaxY + 1));
                var alpha = x == 0 && _mirror ? 0.35f : x % 5 == 0 ? 0.09f : 0.04f;
                Ui.Box(new Rect(a.x - 0.5f, b.y, x == 0 && _mirror ? 2f : 1f, a.y - b.y), x == 0 && _mirror ? new Color(0.45f, 0.75f, 1f, alpha) : new Color(1f, 1f, 1f, alpha), 0f);
            }
            for (var y = RinkPlan.MinY; y <= RinkPlan.MaxY + 1; y++)
            {
                Vector2 a = ToGui(new Vector2(RinkPlan.MinX, y)), b = ToGui(new Vector2(RinkPlan.MaxX + 1, y));
                Ui.Box(new Rect(a.x, a.y - 0.5f, b.x - a.x, 1f), new Color(1f, 1f, 1f, y % 5 == 0 ? 0.09f : 0.04f), 0f);
            }
            // ice that doesn't count (not joined to the rest), in red
            if (_kept != null)
                for (var cy = RinkPlan.MinY; cy <= RinkPlan.MaxY; cy++)
                    for (var cx = RinkPlan.MinX; cx <= RinkPlan.MaxX; cx++)
                        if (RinkPlan.Stray(_plan, _kept, cx, cy)) Ui.Box(GuiRect(cx, cy, cx + 1, cy + 1), new Color(1f, 0.3f, 0.3f, 0.35f), 0f);
            // while painting, the squares as they are now (the smooth picture catches up a moment later)
            if (_painting || _erasing || _dirty)
                for (var cy = RinkPlan.MinY; cy <= RinkPlan.MaxY; cy++)
                    for (var cx = RinkPlan.MinX; cx <= RinkPlan.MaxX; cx++)
                    {
                        var now = _plan.Ice(cx, cy);
                        var was = _kept != null && _kept[(cy - RinkPlan.MinY) * RinkPlan.W + (cx - RinkPlan.MinX)];
                        if (now && !was) Ui.Box(GuiRect(cx, cy, cx + 1, cy + 1), new Color(0.6f, 0.8f, 1f, 0.25f), 0f);
                        else if (!now && was) Ui.Box(GuiRect(cx, cy, cx + 1, cy + 1), new Color(0f, 0f, 0f, 0.55f), 0f);
                    }
            DrawWalls();
            // where a net with no picture yet would go, and the cursor
            if (_menu || _naming) return;
            var col = new Color(1f, 0.85f, 0.3f, 0.95f);
            if (_part == Part.RedNet || _part == Part.BlueNet)
            {
                var team = _part == Part.RedNet ? 0 : 1;
                var c = Corner;
                var old = _plan.Nets[team];
                var n = new RinkPlan.Net { Placed = true, X = c.x, Y = c.y, Dir = old.Placed && old.X == c.x && old.Y == c.y ? (old.Dir + 1) % 4 : c.x < 0 ? 0 : c.x > 0 ? 2 : (old.Placed ? old.Dir : 1) };
                var fits = _plan.NetFits(n, _mirror ? RinkPlan.Mirror(n) : _plan.Nets[1 - team]);
                DrawNetGhost(n, fits ? Palette.Team(team) : new Color(1f, 0.25f, 0.25f), fits);
                if (_mirror) DrawNetGhost(RinkPlan.Mirror(n), fits ? Palette.Team(1 - team) : new Color(1f, 0.25f, 0.25f), fits);
            }
            else if (_part == Part.Spot)
            {
                var c = Corner;
                if (_mirror) c.x = 0;
                var p = ToGui(new Vector2(c.x, c.y));
                Ui.Box(new Rect(p.x - 7f, p.y - 7f, 14f, 14f), col, 7f);
            }
            else if (_part == Part.Wall)
            {
                // the corner the wall draws from (and its mirror)
                var c = Corner;
                var p = ToGui(new Vector2(c.x, c.y));
                Ui.Box(new Rect(p.x - 6f, p.y - 6f, 12f, 12f), col, 6f);
                if (_mirror && c.x != 0) { var q = ToGui(new Vector2(-c.x, c.y)); Ui.Box(new Rect(q.x - 5f, q.y - 5f, 10f, 10f), new Color(col.r, col.g, col.b, 0.45f), 5f); }
            }
            else
            {
                var cell = Cell;
                var r = _part == Part.BigIce ? 1 : 0;
                var erase = _part == Part.Eraser;
                var c = erase ? new Color(1f, 0.4f, 0.35f, 0.95f) : col;
                Outline(GuiRect(cell.x - r, cell.y - r, cell.x + r + 1, cell.y + r + 1), c, 2f);
                if (_mirror) Outline(GuiRect(RinkPlan.MirrorX(cell.x) - r, cell.y - r, RinkPlan.MirrorX(cell.x) + r + 1, cell.y + r + 1), new Color(c.r, c.g, c.b, 0.45f), 1.5f);
            }
        }

        /// <summary>The wall lines: ones that don't count (no ice that counts both sides) in red; while the picture is catching up, new ones too.</summary>
        private void DrawWalls()
        {
            if (_kept == null) return;
            var fresh = _painting || _erasing || _dirty;
            var w = Mathf.Max(2f, (ToGui(new Vector2(1f, 0f)).x - ToGui(Vector2.zero).x) * 0.22f);
            for (var pass = 0; pass < 2; pass++)
            {
                var up = pass == 1;
                int x1 = up ? RinkPlan.MaxX + 1 : RinkPlan.MaxX, y1 = up ? RinkPlan.MaxY : RinkPlan.MaxY + 1;
                for (var y = RinkPlan.MinY; y <= y1; y++)
                    for (var x = RinkPlan.MinX; x <= x1; x++)
                    {
                        if (!_plan.Wall(x, y, up)) continue;
                        int ax = up ? x - 1 : x, ay = up ? y : y - 1;
                        var counts = KeptAt(ax, ay) && KeptAt(x, y);
                        if (counts && !fresh) continue; // drawn by the rink's own picture
                        var c = counts ? new Color(0.85f, 0.88f, 0.95f, 0.9f) : new Color(1f, 0.3f, 0.3f, 0.8f);
                        Vector2 a = ToGui(new Vector2(x, y)), b = ToGui(up ? new Vector2(x, y + 1) : new Vector2(x + 1, y));
                        Ui.Box(up ? new Rect(a.x - w / 2f, b.y - w / 2f, w, a.y - b.y + w) : new Rect(a.x - w / 2f, a.y - w / 2f, b.x - a.x + w, w), c, w / 2f);
                    }
            }
        }

        private bool KeptAt(int cx, int cy)
        {
            return RinkPlan.InGrid(cx, cy) && _kept[(cy - RinkPlan.MinY) * RinkPlan.W + (cx - RinkPlan.MinX)];
        }

        private void DrawNetGhost(RinkPlan.Net n, Color c, bool fits)
        {
            var cells = RinkPlan.NetCells(n);
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (var k = 6; k < cells.Count; k++) // the net itself (not the row in front)
            {
                x0 = Mathf.Min(x0, cells[k][0]); y0 = Mathf.Min(y0, cells[k][1]);
                x1 = Mathf.Max(x1, cells[k][0] + 1); y1 = Mathf.Max(y1, cells[k][1] + 1);
            }
            var r = GuiRect(x0, y0, x1, y1);
            Ui.Box(r, new Color(c.r, c.g, c.b, fits ? 0.35f : 0.25f), 3f);
            Outline(r, new Color(c.r, c.g, c.b, 0.95f), 2f);
            // the mouth: a bright bar along the open side
            var m = ToGui(new Vector2(n.X, n.Y));
            var horizontal = n.Dir == 0 || n.Dir == 2;
            var span = r.height * 0.7f;
            if (horizontal) Ui.Box(new Rect(m.x - 2f, m.y - span / 2f, 4f, span), Color.white, 2f);
            else { span = r.width * 0.7f; Ui.Box(new Rect(m.x - span / 2f, m.y - 2f, span, 4f), Color.white, 2f); }
        }

        private static void Outline(Rect r, Color c, float t)
        {
            Ui.Box(new Rect(r.x, r.y, r.width, t), c, 0f);
            Ui.Box(new Rect(r.x, r.yMax - t, r.width, t), c, 0f);
            Ui.Box(new Rect(r.x, r.y, t, r.height), c, 0f);
            Ui.Box(new Rect(r.xMax - t, r.y, t, r.height), c, 0f);
        }

        private void DrawBar()
        {
            Ui.Box(new Rect(0f, 0f, Ui.W, BarBottom - 10f), new Color(0.04f, 0.04f, 0.05f, 0.92f), 0f);
            // the rink's name, top left
            Ui.Label(new Rect(20f, 10f, 280f, 30f), _plan.Name + (Changed ? "  •" : ""), Ui.Font(20, true), new Color(1f, 1f, 1f, 0.95f));
            var ready = _problem == null;
            Ui.Label(new Rect(20f, 40f, 280f, 44f), ready ? "Ready to play" : _problem, Ui.Font(14, false), ready ? new Color(0.55f, 0.95f, 0.6f, 0.9f) : new Color(1f, 0.75f, 0.5f, 0.9f));
            // the parts, in the middle
            const float pw = 84f, ph = 62f, gap = 7f;
            var total = PartNames.Length * pw + (PartNames.Length - 1) * gap;
            var x0 = Ui.W / 2f - total / 2f;
            var pad = InputMode.Pad;
            for (var k = 0; k < PartNames.Length; k++)
            {
                var r = new Rect(x0 + k * (pw + gap), 12f, pw, ph);
                var on = (int)_part == k;
                var hover = r.Contains(Ui.Mouse) && !_menu;
                Ui.Box(r, on ? new Color(0.45f, 0.7f, 1f, 0.35f) : new Color(1f, 1f, 1f, hover ? 0.14f : 0.06f), 10f);
                if (on) Outline(r, new Color(0.55f, 0.8f, 1f, 0.95f), 2f);
                PartIcon((Part)k, new Rect(r.x, r.y + 6f, r.width, 28f));
                Ui.Label(new Rect(r.x, r.y + 36f, r.width, 22f), PartNames[k], Ui.Font(13, on, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, on ? 1f : 0.75f));
                if (!pad) Ui.Label(new Rect(r.x + 5f, r.y + 2f, 16f, 16f), (k + 1).ToString(), Ui.Font(11, false), new Color(1f, 1f, 1f, 0.4f));
                if (hover && !_menu && Ui.TakeClick()) SetPart((Part)k);
            }
            Glyphs.Hint(new Rect(x0 - 64f, 12f, 56f, ph), pad ? "{tabs}" : "", 15, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleRight);
            // mirror, undo, test: on the right
            var right = Ui.W - 20f;
            var test = new Rect(right - 120f, 14f, 120f, 58f);
            Ui.Box(test, new Color(0.25f, 0.75f, 0.45f, test.Contains(Ui.Mouse) ? 0.95f : 0.8f), 12f);
            Glyphs.Hint(test, pad ? "{start} Test" : "Test  (P)", 18, Color.white, TextAnchor.MiddleCenter, null, true);
            if (test.Contains(Ui.Mouse) && !_menu && Ui.TakeClick()) Test();
            var mir = new Rect(test.x - 132f, 14f, 124f, 27f);
            Ui.Box(mir, _mirror ? new Color(0.45f, 0.75f, 1f, 0.35f) : new Color(1f, 1f, 1f, 0.08f), 8f);
            Glyphs.Hint(mir, (pad ? "{freeplay} " : "M  ") + "Mirror " + (_mirror ? "on" : "off"), 14, Color.white, TextAnchor.MiddleCenter);
            if (mir.Contains(Ui.Mouse) && !_menu && Ui.TakeClick()) ToggleMirror();
            var und = new Rect(test.x - 132f, 45f, 124f, 27f);
            Ui.Box(und, new Color(1f, 1f, 1f, _undo.Count > 0 ? 0.1f : 0.04f), 8f);
            Glyphs.Hint(und, (pad ? "{clip} " : "Ctrl+Z  ") + "Undo", 14, new Color(1f, 1f, 1f, _undo.Count > 0 ? 1f : 0.4f), TextAnchor.MiddleCenter);
            if (und.Contains(Ui.Mouse) && !_menu && Ui.TakeClick()) Undo();
        }

        private void PartIcon(Part p, Rect r)
        {
            var c = r.center;
            switch (p)
            {
                case Part.Ice: Ui.Box(new Rect(c.x - 9f, c.y - 9f, 18f, 18f), new Color(0.7f, 0.85f, 1f, 0.9f), 2f); break;
                case Part.BigIce:
                    for (var y = -1; y <= 1; y++) for (var x = -1; x <= 1; x++) Ui.Box(new Rect(c.x + x * 9f - 4f, c.y + y * 9f - 4f, 8f, 8f), new Color(0.7f, 0.85f, 1f, 0.9f), 1f);
                    break;
                case Part.Eraser: Ui.Box(new Rect(c.x - 12f, c.y - 7f, 24f, 14f), new Color(1f, 0.55f, 0.6f, 0.95f), 4f); break;
                case Part.Wall:
                    Ui.Box(new Rect(c.x - 11f, c.y - 11f, 5f, 22f), new Color(0.8f, 0.82f, 0.88f, 0.95f), 2.5f);
                    Ui.Box(new Rect(c.x - 11f, c.y + 6f, 22f, 5f), new Color(0.8f, 0.82f, 0.88f, 0.95f), 2.5f);
                    break;
                case Part.RedNet: case Part.BlueNet:
                    var tc = Palette.Team(p == Part.RedNet ? 0 : 1);
                    Ui.Box(new Rect(c.x - 9f, c.y - 11f, 18f, 22f), tc, 3f);
                    Ui.Box(new Rect(c.x - 4f, c.y - 7f, 13f, 14f), new Color(0.05f, 0.05f, 0.06f, 1f), 2f);
                    break;
                case Part.Spot:
                    Ui.Box(new Rect(c.x - 11f, c.y - 11f, 22f, 22f), new Color(1f, 1f, 1f, 0.5f), 11f);
                    Ui.Box(new Rect(c.x - 8f, c.y - 8f, 16f, 16f), new Color(0.08f, 0.08f, 0.09f, 1f), 8f);
                    Ui.Box(new Rect(c.x - 3f, c.y - 3f, 6f, 6f), Color.white, 3f);
                    break;
            }
        }

        private void DrawHints()
        {
            var r = new Rect(0f, Ui.H - HintBand + 6f, Ui.W, 30f);
            string line;
            if (InputMode.Pad)
                line = "{move} {nav} Cursor      {confirm} " + Doing + "      {back} Erase      {tabs} Part      {freeplay} Mirror      {clip} Undo      {start} Test      {settings} Menu";
            else
                line = "Left click " + Doing + "      Right click Erase      Scroll / Q E Part      M Mirror      Ctrl+Z Undo      P Test      Esc Menu";
            Glyphs.Hint(r, line, 15, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleCenter);
        }

        /// <summary>What A / left click does with the part in hand.</summary>
        private string Doing
        {
            get
            {
                switch (_part)
                {
                    case Part.Wall: return InputMode.Pad ? "Hold and move: draw walls" : "Drag: draw walls";
                    case Part.Eraser: return "Erase";
                    case Part.RedNet: case Part.BlueNet: return "Place (again: turn)";
                    case Part.Spot: return "Place";
                    default: return "Paint";
                }
            }
        }

        private void DrawMenu()
        {
            Ui.Box(new Rect(0f, 0f, Ui.W, Ui.H), new Color(0f, 0f, 0f, 0.6f), 0f);
            var top = MenuRect(0);
            Ui.Label(new Rect(0f, top.y - 64f, Ui.W, 44f), _plan.Name, Ui.Font(28, true, TextAnchor.MiddleCenter), Color.white);
            for (var k = 0; k < _menuRows.Count; k++)
            {
                var r = MenuRect(k);
                var on = k == _menuRow;
                Ui.Box(r, new Color(1f, 1f, 1f, on ? 0.2f : 0.07f), 10f);
                Ui.Label(r, _menuRows[k].Key, Ui.Font(18, on, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, on ? 1f : 0.75f));
            }
        }

        private void DrawNaming()
        {
            Ui.Box(new Rect(0f, 0f, Ui.W, Ui.H), new Color(0f, 0f, 0f, 0.6f), 0f);
            var r = new Rect(Ui.W / 2f - 240f, Ui.H / 2f - 30f, 480f, 56f);
            Ui.Label(new Rect(0f, r.y - 50f, Ui.W, 40f), "Name your rink", Ui.Font(24, true, TextAnchor.MiddleCenter), Color.white);
            Ui.Box(r, new Color(1f, 1f, 1f, 0.12f), 10f);
            var caret = (int)(Time.unscaledTime * 2f) % 2 == 0 ? "|" : " ";
            Ui.Label(r, _nameBuffer + caret, Ui.Font(22, true, TextAnchor.MiddleCenter), Color.white);
            Ui.Label(new Rect(0f, r.yMax + 12f, Ui.W, 26f), "Enter  Keep it      Esc  Cancel", Ui.Font(15, false, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.65f));
        }

        // ------------------------------------------------------------------ the Custom Rinks screen

        private List<CustomRinks.Entry> _entries = new List<CustomRinks.Entry>();
        private readonly Dictionary<string, Texture2D> _thumbs = new Dictionary<string, Texture2D>();
        private int _libFocus, _libTop, _deleteArmed = -1;
        private float _deleteAt;
        private const int LibCols = 4, LibRows = 3;

        /// <summary>Places on the screen: the + first (when making rinks), then each rink.</summary>
        private int LibCount { get { return _entries.Count + (PickMode ? 0 : 1); } }
        private CustomRinks.Entry LibEntry(int k) { var i = PickMode ? k : k - 1; return i >= 0 && i < _entries.Count ? _entries[i] : null; }

        private void LibraryTick()
        {
            var kb = Kb;
            var ready = Time.unscaledTime - _openedAt > 0.2f;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || PadPressed(p => p.buttonEast)
                || (PickMode && ready && (PadPressed(p => p.leftShoulder) || PadPressed(p => p.rightShoulder) || (kb != null && (kb.qKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)))))
            {
                Sound.Back();
                Current = State.Closed;
                Runner.LibraryClosed(PickMode);
                return;
            }
            var count = LibCount;
            if (count == 0) return;
            var steps = Steps();
            var stick = Stick();
            if (steps == Vector2Int.zero && stick.magnitude > 0.6f && Time.unscaledTime >= _repeatAt)
            {
                steps = Mathf.Abs(stick.x) > Mathf.Abs(stick.y) ? new Vector2Int(stick.x > 0 ? 1 : -1, 0) : new Vector2Int(0, stick.y > 0 ? 1 : -1);
                _repeatAt = Time.unscaledTime + 0.2f;
            }
            if (steps.x != 0 || steps.y != 0)
            {
                var to = _libFocus + steps.x - steps.y * LibCols;
                if (to >= 0 && to < count) { _libFocus = to; Sound.Move(); _deleteArmed = -1; }
            }
            if (Ui.MouseMovedNow)
                for (var k = 0; k < count; k++) if (TileRect(k).Contains(Ui.Mouse)) { if (k != _libFocus) _deleteArmed = -1; _libFocus = k; }
            var ms = Ms;
            if (ms != null)
            {
                var wheel = ms.scroll.ReadValue().y;
                if (wheel != 0f) _libTop = Mathf.Clamp(_libTop + (wheel < 0f ? 1 : -1), 0, Mathf.Max(0, (count + LibCols - 1) / LibCols - LibRows));
            }
            var row = _libFocus / LibCols;
            if (steps != Vector2Int.zero) { if (row < _libTop) _libTop = row; if (row >= _libTop + LibRows) _libTop = row - LibRows + 1; }

            var click = ms != null && ms.leftButton.wasPressedThisFrame && TileRect(_libFocus).Contains(Ui.Mouse);
            var confirm = ready && (click || PadPressed(p => p.buttonSouth) || (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)));
            if (confirm)
            {
                var e = LibEntry(_libFocus);
                if (PickMode)
                {
                    if (e == null) return;
                    if (e.Map == null) { Sound.Deny(); Say("Unfinished: " + e.Problem.ToLowerInvariant() + ". Finish it in Custom rinks on the main menu."); return; }
                    Sound.Select();
                    Current = State.Closed;
                    Runner.CustomRinkPicked(e.File);
                    return;
                }
                Sound.Select();
                Current = State.Closed;
                if (e == null)
                {
                    var plan = RinkPlan.Starter();
                    plan.Name = CustomRinks.NewName(_entries);
                    Runner.EditRink(plan, null);
                }
                else Runner.EditRink(e.Plan, e.File);
                return;
            }
            // X / Delete: delete (press again to be sure)
            if (!PickMode && ready && (PadPressed(p => p.buttonWest) || (kb != null && kb.deleteKey.wasPressedThisFrame)))
            {
                var e = LibEntry(_libFocus);
                if (e == null) return;
                if (_deleteArmed == _libFocus && Time.unscaledTime - _deleteAt < 3f)
                {
                    CustomRinks.Delete(e.File);
                    Sound.Back();
                    Say("Deleted " + e.Plan.Name);
                    _deleteArmed = -1;
                    Runner.CustomRinksChanged();
                    OpenLibrary(false);
                }
                else { _deleteArmed = _libFocus; _deleteAt = Time.unscaledTime; Sound.Deny(); Say("Press again to delete " + e.Plan.Name); }
            }
        }

        private Rect TileRect(int k)
        {
            const float tw = 268f, th = 151f + 34f, gap = 22f;
            var x0 = Ui.W / 2f - (LibCols * tw + (LibCols - 1) * gap) / 2f;
            var row = k / LibCols - _libTop;
            return new Rect(x0 + (k % LibCols) * (tw + gap), 150f + row * (th + gap), tw, th);
        }

        private void DrawLibrary()
        {
            var appear = Ui.EaseOut((Time.unscaledTime - _openedAt) / 0.3f);
            Ui.Box(new Rect(0f, 0f, Ui.W, Ui.H), Palette.Background, 0f);
            PlayScreens.Title(Ui.W, PickMode ? "CUSTOM RINKS" : "CUSTOM RINKS", PickMode ? "Pick one of your rinks to play on" : "Make your own rinks, then pick them on the rink screen", appear);
            var count = LibCount;
            if (count == 0)
                Ui.Label(new Rect(0f, Ui.H / 2f - 40f, Ui.W, 80f), "No custom rinks yet.\nMake one from Custom rinks on the main menu.", Ui.Font(20, false, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.7f));
            for (var k = 0; k < count; k++)
            {
                var row = k / LibCols - _libTop;
                if (row < 0 || row >= LibRows) continue;
                var r = TileRect(k);
                var on = k == _libFocus;
                var pic = new Rect(r.x, r.y, r.width, r.width * 9f / 16f);
                if (on) Ui.Box(new Rect(pic.x - 4f, pic.y - 4f, pic.width + 8f, pic.height + 8f), new Color(1f, 1f, 1f, 0.9f), 14f);
                Ui.Box(pic, new Color(0.07f, 0.07f, 0.08f, 1f), 12f);
                var e = LibEntry(k);
                if (e == null)
                {
                    // the +: a new rink
                    var c = pic.center;
                    Ui.Box(new Rect(c.x - 34f, c.y - 6f, 68f, 12f), new Color(1f, 1f, 1f, on ? 1f : 0.6f), 6f);
                    Ui.Box(new Rect(c.x - 6f, c.y - 34f, 12f, 68f), new Color(1f, 1f, 1f, on ? 1f : 0.6f), 6f);
                    Ui.Label(new Rect(r.x, pic.yMax + 6f, r.width, 26f), "New rink", Ui.Font(17, on, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, on ? 1f : 0.7f));
                    continue;
                }
                var tex = Thumb(e);
                if (tex != null && Event.current.type == EventType.Repaint)
                {
                    var old = GUI.color; GUI.color = new Color(1f, 1f, 1f, e.Map != null ? 1f : 0.45f);
                    GUI.DrawTexture(new Rect(pic.x + 8f, pic.y + 8f, pic.width - 16f, pic.height - 16f), tex, ScaleMode.ScaleToFit);
                    GUI.color = old;
                }
                if (e.Map == null)
                {
                    var tag = new Rect(pic.x + 10f, pic.y + 10f, 100f, 22f);
                    Ui.Box(tag, new Color(0.85f, 0.5f, 0.2f, 0.9f), 11f);
                    Ui.Label(tag, "Unfinished", Ui.Font(12, true, TextAnchor.MiddleCenter), Color.white);
                }
                if (_deleteArmed == k && Time.unscaledTime - _deleteAt < 3f)
                    Ui.Box(pic, new Color(0.9f, 0.2f, 0.2f, 0.35f), 12f);
                Ui.Label(new Rect(r.x, pic.yMax + 6f, r.width, 26f), e.Plan.Name, Ui.Font(17, on, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, on ? 1f : 0.75f));
            }
            var pad = InputMode.Pad;
            string hint;
            if (PickMode) hint = pad ? "{nav} Rink      {confirm} Choose      {tabs} {back} Built-in rinks" : "Click or Enter  Choose      Q E / Esc  Built-in rinks";
            else hint = pad ? "{nav} Rink      {confirm} Make / change      {clip} Delete      {back} Back" : "Click or Enter  Make / change      Del  Delete      Esc  Back";
            Glyphs.Hint(new Rect(0f, Ui.H - 52f, Ui.W, 30f), hint, 17, new Color(1f, 1f, 1f, 0.8f));
            if (_say != null && Time.unscaledTime - _sayAt < 3f)
            {
                var a = Mathf.Clamp01(3f - (Time.unscaledTime - _sayAt));
                var sr = new Rect(Ui.W / 2f - 330f, 112f, 660f, 32f);
                Ui.Box(sr, new Color(0f, 0f, 0f, 0.7f * a), 10f);
                Ui.Label(sr, _say, Ui.Font(16, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, a));
            }
        }

        /// <summary>A little picture of a rink's grid: its ice, nets and (on unfinished ones) any stray ice.</summary>
        private Texture2D Thumb(CustomRinks.Entry e)
        {
            Texture2D t;
            if (_thumbs.TryGetValue(e.File, out t) && t != null) return t;
            const int w = RinkPlan.W, h = RinkPlan.H;
            t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            var kept = e.Plan.Kept();
            Color32 ice = new Color(0.36f, 0.4f, 0.46f), stray = new Color(0.6f, 0.25f, 0.25f), clear = new Color(0f, 0f, 0f, 0f);
            for (var k = 0; k < px.Length; k++)
            {
                int cx = RinkPlan.MinX + k % w, cy = RinkPlan.MinY + k / w;
                px[k] = kept[k] ? ice : e.Plan.Ice(cx, cy) ? stray : clear;
            }
            for (var team = 0; team < 2; team++)
            {
                var n = e.Plan.Nets[team];
                if (!n.Placed) continue;
                var cells = RinkPlan.NetCells(n);
                for (var k = 6; k < cells.Count; k++)
                {
                    int cx = cells[k][0], cy = cells[k][1];
                    if (RinkPlan.InGrid(cx, cy)) px[(cy - RinkPlan.MinY) * w + (cx - RinkPlan.MinX)] = Palette.Team(team);
                }
            }
            t.SetPixels32(px);
            t.Apply();
            _thumbs[e.File] = t;
            return t;
        }
    }
}
