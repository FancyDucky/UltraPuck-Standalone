using System.Collections.Generic;
using UltraPuck.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using PlayerInput = UltraPuck.Core.PlayerInput;

namespace UltraPuck.Game
{
    /// <summary>
    /// The people playing on this PC: each one is a controller or the keyboard. Joining, leaving, team choice, and
    /// turning their buttons into <see cref="PlayerInput"/> once per tick.
    ///
    /// Controls (the same on Xbox and PlayStation pads; Unity names the face buttons by position):
    /// <list type="bullet">
    /// <item>Left stick: move. Right stick: aim (when pushed you face where it points; let go and you face where you move).</item>
    /// <item>Shoot / check: A / Cross, or RB / R1. With the puck it shoots; without it, it checks.</item>
    /// <item>Charge / brake: B / Circle, or LB / L1. With the puck hold to charge, let go to fire; without it, brake.</item>
    /// <item>Utility: Y / Triangle (for power-up cards later).</item>
    /// <item>Swap to a bot teammate: X / Square.</item>
    /// <item>Puck back (free play): View / Select. Menu: Start / Options.</item>
    /// <item>Keyboard: WASD move, arrow keys aim, Space or J shoot/check, Left Shift or K charge/brake, E or L utility, R puck back, Esc menu.</item>
    /// </list>
    /// </summary>
    internal sealed class LocalPlayers
    {
        public const int MaxPlayers = 8;

        public sealed class Slot
        {
            public InputDevice Device;
            /// <summary>
            /// A second device playing the same player: with shared controls, the keyboard and the first controller
            /// are both player 1 (see <see cref="Settings.SharedControls"/>). Null otherwise.
            /// </summary>
            public InputDevice Extra;
            /// <summary>The device that asked to type a name (so the keyboard types for real, a controller gets the on-screen keys).</summary>
            public InputDevice TypingDevice;
            /// <summary>Team 0 or 1, worked out from the colours picked (see <see cref="AssignTeams"/>).</summary>
            public int Team;
            /// <summary>The team colour picked (index into <see cref="Palette.Colours"/>) and the player's symbol.</summary>
            public int Colour, Symbol;
            /// <summary>The name shown over the player (set on the join screen).</summary>
            public string Name = "";
            /// <summary>Typing a name on the join screen: their controls go to the keyboard, not the lobby.</summary>
            public bool Typing;
            /// <summary>Buttons pressed since the last tick, so a tap shorter than a tick still counts.</summary>
            public byte Latched;
            public bool PrevLeft, PrevRight, PrevUp, PrevDown;
            /// <summary>A computer player (free play bots, or the ULTRAPUCK_DEMO players).</summary>
            public bool Demo;
            /// <summary>How long B / Escape has been held (holding it long enough leaves).</summary>
            public float BackHeld;
            /// <summary>Readied up on the players screen: from then on their controls work the bots instead.</summary>
            public bool Ready;
            /// <summary>Which bot a readied player is working on (index among the bots).</summary>
            public int Cursor;
            /// <summary>The frame they joined on (the press that joined them mustn't also ready them).</summary>
            public int JoinedFrame;
            public string Label { get { return Demo ? "Bot" : Extra != null ? Name1(Device) + " + " + Name1(Extra) : Device != null ? Name1(Device) : "Disconnected"; } }
            private static string Name1(InputDevice d) { return d is Keyboard ? "Keyboard" : d.displayName; }
            public bool Has(InputDevice d) { return d != null && (Device == d || Extra == d); }
        }

        public readonly List<Slot> Slots = new List<Slot>();

        public int[] Teams()
        {
            var t = new int[Slots.Count];
            for (var i = 0; i < t.Length; i++) t[i] = Slots[i].Team;
            return t;
        }

        private Slot Find(InputDevice d) { return Slots.Find(s => s.Has(d)); }

        // ------------------------------------------------------------------ shared keyboard and controller

        /// <summary>
        /// Shared controls: the keyboard and the first free controller play the first person here (player 1). A
        /// controller plugged in later joins them; one unplugged is let go. Separate controls: nobody shares. Call
        /// every frame (cheap).
        /// </summary>
        public void PairDevices()
        {
            foreach (var s in Slots) if (s.Extra != null && (!Settings.SharedControls || !s.Extra.added)) s.Extra = null;
            if (!Settings.SharedControls) return;
            var first = Slots.Find(s => !s.Demo);
            if (first == null || first.Extra != null || first.Device == null) return;
            var kb = Keyboard.current;
            if (first.Device is Keyboard)
            {
                foreach (var g in Gamepad.all) if (Find(g) == null) { first.Extra = g; Debug.Log("Ultra Puck: " + g.displayName + " plays player 1 with the keyboard"); break; }
            }
            else if (first.Device is Gamepad && kb != null && Find(kb) == null)
            {
                first.Extra = kb;
                Debug.Log("Ultra Puck: the keyboard plays player 1 with " + first.Device.displayName);
            }
        }

        /// <summary>Both of a player's devices (the second may be null).</summary>
        private static bool Any(Slot s, System.Func<Gamepad, bool> pad, System.Func<Keyboard, bool> keys)
        {
            return One(s.Device, pad, keys) || One(s.Extra, pad, keys);
        }

        private static bool One(InputDevice d, System.Func<Gamepad, bool> pad, System.Func<Keyboard, bool> keys)
        {
            var g = d as Gamepad;
            if (g != null) return pad(g);
            var k = d as Keyboard;
            return k != null && keys(k);
        }

        /// <summary>Is a button held on any of this player's devices (a controller button, or a key)?</summary>
        public bool AnyHeld(Slot s, System.Func<Gamepad, bool> pad, System.Func<Keyboard, bool> keys) { return !Blocked && Any(s, pad, keys); }

        /// <summary>The device a player last pressed something on (for button pictures and typing), or their first.</summary>
        private static InputDevice Pressed(Slot s, System.Func<Gamepad, bool> pad, System.Func<Keyboard, bool> keys)
        {
            if (One(s.Device, pad, keys)) return s.Device;
            if (One(s.Extra, pad, keys)) return s.Extra;
            return null;
        }

        private static Vector2 StickOf(Slot s) { return Vector2.ClampMagnitude(Stick(s.Device) + Stick(s.Extra), 1f); }

        private static Vector2 AimOfSlot(Slot s)
        {
            var a = Aim(s.Device);
            var b = Aim(s.Extra);
            return b.sqrMagnitude > a.sqrMagnitude ? b : a;
        }

        private static bool Plugged(InputDevice d) { return d != null && d.added; }

        // ------------------------------------------------------------------ lobby

        /// <summary>The Steam overlay is open: the game ignores controllers and the keyboard until it closes.</summary>
        public static bool Blocked;

        /// <summary>Seconds B / Escape must be held (by someone who hasn't joined) to leave the lobby.</summary>
        public const float HoldToLeave = 0.8f;

        /// <summary>How far an unjoined controller or keyboard has got with holding B / Escape (0..1): leaving the lobby itself.</summary>
        public float NobodyLeaving { get; private set; }

        /// <summary>
        /// The bots on the players screen, for readied players to work on: on this PC they're slots, online they're
        /// the host's (null when you can't change them).
        /// </summary>
        public interface IBotEditor
        {
            int Count { get; }
            /// <summary>Bot <paramref name="i"/> to the other team.</summary>
            void SwitchTeam(int i);
            void NextSymbol(int i);
            bool Add();
            void Remove(int i);
        }

        public IBotEditor Bots;

        /// <summary>The bots here are slots like the people (the default).</summary>
        public sealed class SlotBots : IBotEditor
        {
            private readonly LocalPlayers _l;
            public SlotBots(LocalPlayers l) { _l = l; }
            private int Index(int i) { for (var k = 0; k < _l.Slots.Count; k++) if (_l.Slots[k].Demo && i-- == 0) return k; return -1; }
            public int Count { get { return _l.BotCount; } }
            public void SwitchTeam(int i) { var k = Index(i); if (k < 0) return; var s = _l.Slots[k]; s.Colour = s.Colour == 1 ? 0 : 1; _l.AssignTeams(); }
            public void NextSymbol(int i) { var k = Index(i); if (k < 0) return; _l.Slots[k].Symbol = (_l.Slots[k].Symbol + 1) % Symbols.Count; }
            public bool Add() { return _l.AddBot(); }
            public void Remove(int i) { var k = Index(i); if (k < 0) return; _l.Slots.RemoveAt(k); _l.AssignTeams(); }
        }

        public LocalPlayers() { Bots = new SlotBots(this); }

        /// <summary>Everyone here (not the bots) has readied up, and there's someone.</summary>
        public bool AllReady { get { var any = false; foreach (var s in Slots) { if (s.Demo) continue; any = true; if (!s.Ready) return false; } return any; } }

        public void UnreadyAll() { foreach (var s in Slots) s.Ready = false; }

        /// <summary>
        /// Lobby: join with A / Space; left / right switches team, Y / N types a name, X / M changes the symbol, A again
        /// readies up. Readied, up / down picks a bot, left / right moves it to the other team, X / M changes its symbol,
        /// LB / RB (- / =) removes or adds one, and B / Escape un-readies. B / Escape before that gives up the seat;
        /// held by someone without a seat, it leaves.
        /// </summary>
        public void LobbyUpdate()
        {
            var dt = Time.unscaledDeltaTime;
            var nobody = 0f;
            if (Blocked) { NobodyLeaving = 0f; return; }
            PairDevices();
            ReorderOnJoin = true;
            foreach (var g in Gamepad.all)
            {
                var s = Find(g);
                if (s == null && g.buttonSouth.wasPressedThisFrame) Join(g);
                else if (s != null) { if (!s.Typing && s.Device == g) Hold(s, g.buttonEast.isPressed, dt); }
                else if (g.buttonEast.isPressed) nobody = Mathf.Max(nobody, Held(g, dt));
                else _nobodyHeld.Remove(g);
            }
            var kb = Keyboard.current;
            if (kb != null)
            {
                var s = Find(kb);
                if (s == null && kb.spaceKey.wasPressedThisFrame) Join(kb);
                else if (s != null) { if (!s.Typing && s.Device == kb) Hold(s, kb.escapeKey.isPressed, dt); }
                else if (kb.escapeKey.isPressed) nobody = Mathf.Max(nobody, Held(kb, dt));
                else _nobodyHeld.Remove(kb);
            }
            ReorderOnJoin = false;
            NobodyLeaving = Mathf.Clamp01(nobody / HoldToLeave);
            PairDevices();
            BotRequest = 0;
            foreach (var s in Slots.ToArray())
            {
                if (s.Demo || s.Typing || !Slots.Contains(s)) continue;
                var nav = Nav(s);
                // either of the player's devices (the keyboard and a controller, when they share)
                var confirm = s.JoinedFrame != Time.frameCount && Any(s, g => g.buttonSouth.wasPressedThisFrame, k => k.spaceKey.wasPressedThisFrame);
                var namer = Pressed(s, g => g.buttonNorth.wasPressedThisFrame, k => k.nKey.wasPressedThisFrame);
                var name = namer != null;
                var symbol = Any(s, g => g.buttonWest.wasPressedThisFrame, k => k.mKey.wasPressedThisFrame);
                var less = Any(s, g => g.leftShoulder.wasPressedThisFrame, k => k.minusKey.wasPressedThisFrame);
                var more = Any(s, g => g.rightShoulder.wasPressedThisFrame, k => k.equalsKey.wasPressedThisFrame);
                if (name) s.TypingDevice = namer;
                if (!s.Ready)
                {
                    if (nav.x != 0) { s.Colour = ((s.Colour + nav.x) % Palette.Choosable + Palette.Choosable) % Palette.Choosable; AssignTeams(); Sound.Step(nav.x); }
                    if (name) { NameRequest = s; Sound.Select(); }
                    if (symbol) { s.Symbol = (s.Symbol + 1) % Symbols.Count; Sound.Move(); }
                    if (confirm) { s.Ready = true; s.Cursor = 0; Sound.Select(); }
                    continue;
                }
                // readied: working on the bots
                var bots = Bots;
                if (bots == null) continue;
                if (more) { if (bots.Add()) { s.Cursor = bots.Count - 1; Sound.Select(); } else Sound.Deny(); }
                if (bots.Count == 0) { s.Cursor = 0; continue; }
                s.Cursor = Mathf.Clamp(s.Cursor, 0, bots.Count - 1);
                if (nav.y != 0) { s.Cursor = (s.Cursor - nav.y + bots.Count) % bots.Count; if (bots.Count > 1) Sound.Move(); }
                if (nav.x != 0) { bots.SwitchTeam(s.Cursor); Sound.Step(nav.x); }
                if (symbol) { bots.NextSymbol(s.Cursor); Sound.Move(); }
                if (less) { bots.Remove(s.Cursor); s.Cursor = Mathf.Clamp(s.Cursor, 0, Mathf.Max(0, bots.Count - 1)); Sound.Back(); }
            }
        }

        private readonly Dictionary<InputDevice, float> _nobodyHeld = new Dictionary<InputDevice, float>();

        private float Held(InputDevice d, float dt)
        {
            float t;
            _nobodyHeld.TryGetValue(d, out t);
            t += dt;
            _nobodyHeld[d] = t;
            return t;
        }

        /// <summary>
        /// B / Escape gives up your seat straight away. Holding it on after that doesn't also leave the lobby: that
        /// takes a fresh press and hold (the hold counts from minus a lot until it's let go).
        /// </summary>
        private void Hold(Slot s, bool down, float dt)
        {
            s.BackHeld = 0f;
            var pressed = Any(s, g => g.buttonEast.wasPressedThisFrame, k => k.escapeKey.wasPressedThisFrame);
            if (!pressed) return;
            Sound.Back();
            if (s.Ready) { s.Ready = false; return; } // B un-readies first
            Slots.Remove(s);
            AssignTeams();
            _nobodyHeld[s.Device] = -1000f;
            if (s.Extra != null) _nobodyHeld[s.Extra] = -1000f;
        }

        /// <summary>Someone who hasn't joined held B / Escape long enough: leave the lobby.</summary>
        public bool LeaveLobbyHeld() { return NobodyLeaving >= 1f; }

        /// <summary>Joining may put a newcomer ahead of others (on the players screen only: mid-game, slots are fixed to players).</summary>
        private bool ReorderOnJoin;

        private bool Join(InputDevice d)
        {
            if (Slots.Count >= MaxPlayers) return false;
            var s = new Slot { Device = d, Colour = ColourForNewcomer(), Symbol = FreeSymbol(), JoinedFrame = Time.frameCount };
            var humans = 0;
            foreach (var o in Slots) if (!o.Demo) humans++;
            s.Name = humans == 0 ? Settings.PlayerName : "P" + (Slots.Count + 1);
            Slots.Add(s);
            // separate keyboard and controller players: the one chosen in the settings is player 1 when both join
            var firstHuman = Slots.Find(o => !o.Demo);
            if (ReorderOnJoin && !Settings.SharedControls && firstHuman != s && firstHuman != null)
            {
                var wantPad = Settings.ControllerIsPlayer1;
                if ((d is Gamepad) == wantPad && (firstHuman.Device is Gamepad) != wantPad && firstHuman.Device != null)
                {
                    Slots.Remove(s);
                    Slots.Insert(Slots.IndexOf(firstHuman), s);
                    var n = s.Name; s.Name = firstHuman.Name; firstHuman.Name = n;
                }
            }
            AssignTeams();
            Sound.Select();
            Debug.Log("Ultra Puck: P" + Slots.Count + " joined with " + d.displayName + " (" + d.layout + ")");
            return true;
        }

        // ------------------------------------------------------------------ colours, teams, symbols

        /// <summary>The first colour picked (in slot order) is team 0, the second team 1. Returns how many colours there are.</summary>
        public int AssignTeams()
        {
            var colours = Colours();
            foreach (var s in Slots) { var i = colours.IndexOf(s.Colour); s.Team = i < 0 ? 0 : i & 1; }
            if (colours.Count >= 2) Palette.SetTeams(colours[0], colours[1]);
            else if (colours.Count == 1) Palette.SetTeams(colours[0], colours[0] == 1 ? 0 : 1);
            return colours.Count;
        }

        /// <summary>The colours picked, in slot order, each once.</summary>
        public List<int> Colours()
        {
            var colours = new List<int>();
            foreach (var s in Slots) if (!colours.Contains(s.Colour)) colours.Add(s.Colour);
            return colours;
        }

        /// <summary>A newcomer goes on the smaller team, in its colour (or the other default colour if nobody's there yet).</summary>
        private int ColourForNewcomer()
        {
            var colours = Colours();
            if (colours.Count == 0) return 0;
            if (colours.Count == 1) return colours[0] == 1 ? 0 : 1;
            int a = 0, b = 0;
            foreach (var s in Slots) if (s.Colour == colours[0]) a++; else if (s.Colour == colours[1]) b++;
            return a <= b ? colours[0] : colours[1];
        }

        private int FreeSymbol()
        {
            for (var k = 0; k < Symbols.Count; k++) if (!Slots.Exists(s => s.Symbol == k)) return k;
            return Slots.Count % Symbols.Count;
        }

        /// <summary>Join screen: a player asked to type their name (Triangle / Y, or N on the keyboard). Read once.</summary>
        public Slot NameRequest;
        /// <summary>Join screen: LB / RB (or - / =) asked for one bot fewer (-1) or more (+1).</summary>
        public int BotRequest;

        private int SmallerTeam()
        {
            int a = 0, b = 0;
            foreach (var s in Slots) if (s.Team == 0) a++; else b++;
            return a <= b ? 0 : 1;
        }

        /// <summary>Start / Enter from anyone who has joined.</summary>
        public bool StartPressed()
        {
            if (Blocked) return false;
            foreach (var s in Slots)
                if (Any(s, g => g.startButton.wasPressedThisFrame, k => k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame)) return true;
            return false;
        }

        /// <summary>B / Escape from anyone who has joined.</summary>
        public bool BackPressed()
        {
            if (Blocked) return false;
            foreach (var s in Slots)
                if (Any(s, g => g.buttonEast.wasPressedThisFrame, k => k.escapeKey.wasPressedThisFrame)) return true;
            return false;
        }

        /// <summary>Lobby: Y / F starts free play. Whoever presses it joins first if they haven't yet.</summary>
        public bool FreePlayPressed()
        {
            if (Blocked) return false;
            foreach (var g in Gamepad.all)
                if (g.buttonNorth.wasPressedThisFrame) { if (Find(g) == null) Join(g); return Find(g) != null; }
            var kb = Keyboard.current;
            if (kb != null && kb.fKey.wasPressedThisFrame) { if (Find(kb) == null) Join(kb); return Find(kb) != null; }
            return false;
        }

        // ------------------------------------------------------------------ free play

        /// <summary>Free play: A / Space on a device that isn't playing yet joins in. Returns how many joined.</summary>
        public int JoinMidGame()
        {
            var joined = 0;
            if (Blocked) return 0;
            foreach (var g in Gamepad.all)
                if (Find(g) == null && g.buttonSouth.wasPressedThisFrame && Join(g)) joined++;
            var kb = Keyboard.current;
            if (kb != null && Find(kb) == null && kb.spaceKey.wasPressedThisFrame && Join(kb)) joined++;
            PairDevices();
            return joined;
        }

        /// <summary>Start / Options / Escape from anyone playing (opens the menu).</summary>
        public bool MenuPressed()
        {
            if (Blocked) return false;
            foreach (var s in Slots)
                if (Any(s, g => g.startButton.wasPressedThisFrame, k => k.escapeKey.wasPressedThisFrame)) return true;
            return false;
        }

        /// <summary>LB / RB or Q / E on any controller or the keyboard: -1, +1, or 0.</summary>
        public int TabPressed()
        {
            if (Blocked) return 0;
            foreach (var g in Gamepad.all)
            {
                if (g.leftShoulder.wasPressedThisFrame) return -1;
                if (g.rightShoulder.wasPressedThisFrame) return 1;
            }
            var kb = Keyboard.current;
            if (kb == null) return 0;
            return kb.qKey.wasPressedThisFrame ? -1 : kb.eKey.wasPressedThisFrame ? 1 : 0;
        }

        /// <summary>
        /// "Press any button": the controller or keyboard something was just pressed on (any key, any controller
        /// button, a mouse button, which counts as the keyboard), or null. Pushing a stick doesn't count: a worn
        /// stick that drifts would press it by itself.
        /// </summary>
        public static InputDevice AnyButton()
        {
            if (Blocked) return null;
            foreach (var g in Gamepad.all)
            {
                if (g.buttonSouth.wasPressedThisFrame || g.buttonEast.wasPressedThisFrame || g.buttonWest.wasPressedThisFrame || g.buttonNorth.wasPressedThisFrame
                    || g.startButton.wasPressedThisFrame || g.selectButton.wasPressedThisFrame
                    || g.leftShoulder.wasPressedThisFrame || g.rightShoulder.wasPressedThisFrame || g.leftTrigger.wasPressedThisFrame || g.rightTrigger.wasPressedThisFrame
                    || g.leftStickButton.wasPressedThisFrame || g.rightStickButton.wasPressedThisFrame
                    || g.dpad.up.wasPressedThisFrame || g.dpad.down.wasPressedThisFrame || g.dpad.left.wasPressedThisFrame || g.dpad.right.wasPressedThisFrame) return g;
            }
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) return kb;
            var m = Mouse.current;
            if (m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame)) return kb ?? (InputDevice)m;
            return null;
        }

        /// <summary>Lobby: View (Select) / Tab on any controller or the keyboard opens the settings.</summary>
        public bool SettingsPressed()
        {
            if (Blocked) return false;
            foreach (var g in Gamepad.all) if (g.selectButton.wasPressedThisFrame) return true;
            var kb = Keyboard.current;
            return kb != null && kb.tabKey.wasPressedThisFrame;
        }

        /// <summary>A player's aim stick right now (right stick, or arrow keys), for the card screen's tilt.</summary>
        public Vector2 AimOf(int slot)
        {
            if (Blocked) return Vector2.zero;
            return slot >= 0 && slot < Slots.Count ? AimOfSlot(Slots[slot]) : Vector2.zero;
        }

        /// <summary>While picking cards: the pick a local player has confirmed on the card screen (1-based), or 0.</summary>
        public System.Func<int, byte> PickFor;
        /// <summary>While picking cards: the card a local player is looking at (1-based), or 0.</summary>
        public System.Func<int, byte> HoverFor;

        public int BotCount { get { var n = 0; foreach (var s in Slots) if (s.Demo) n++; return n; } }
        public int HumanCount { get { return Slots.Count - BotCount; } }

        /// <summary>A computer player on the smaller team. Returns false when the rink is full.</summary>
        public bool AddBot()
        {
            if (Slots.Count >= MaxPlayers) return false;
            Slots.Add(new Slot { Demo = true, Colour = ColourForNewcomer(), Symbol = FreeSymbol(), Name = "Bot" });
            AssignTeams();
            return true;
        }

        /// <summary>Index of the last computer player, or -1.</summary>
        public int LastBot()
        {
            for (var i = Slots.Count - 1; i >= 0; i--) if (Slots[i].Demo) return i;
            return -1;
        }

        // ------------------------------------------------------------------ menus

        /// <summary>
        /// One frame of menu input: stick / d-pad / WASD / arrows as steps (held, they repeat), confirm, back, and
        /// tab left / right (LB / RB, Q / E).
        /// </summary>
        public struct MenuInput { public int X, Y, Tab; public bool Confirm, Back; public InputDevice Device; }

        private sealed class NavState { public int Dir; public float Held, Next; }
        private readonly Dictionary<InputDevice, NavState> _nav = new Dictionary<InputDevice, NavState>();

        /// <summary>Menu input from every controller and the keyboard, joined or not (anyone can work the menus).</summary>
        public MenuInput Menu()
        {
            var m = new MenuInput();
            foreach (var g in Gamepad.all) Merge(ref m, MenuFrom(g));
            if (Keyboard.current != null) Merge(ref m, MenuFrom(Keyboard.current));
            return m;
        }

        /// <summary>Menu input from the people who have joined only (so pressing A to join doesn't also press a button).</summary>
        public MenuInput MenuJoined()
        {
            var m = new MenuInput();
            foreach (var s in Slots)
            {
                if (s.Demo) continue;
                if (s.Device != null) Merge(ref m, MenuFrom(s.Device));
                if (s.Extra != null) Merge(ref m, MenuFrom(s.Extra));
            }
            return m;
        }

        /// <summary>B / Escape on a controller or keyboard that hasn't joined (leaving a screen nobody has joined yet).</summary>
        public bool BackFromNobody()
        {
            foreach (var g in Gamepad.all) if (Find(g) == null && g.buttonEast.wasPressedThisFrame) return true;
            var kb = Keyboard.current;
            return kb != null && Find(kb) == null && kb.escapeKey.wasPressedThisFrame;
        }

        /// <summary>Add a person on a specific device (the one that chose free play from the main menu, say).</summary>
        public void JoinDevice(InputDevice d) { if (d != null && Find(d) == null) Join(d); }

        /// <summary>Menu input from one player's own controller or keyboard only (the card screen: nobody picks for someone else).</summary>
        public MenuInput MenuFor(int slot)
        {
            if (slot < 0 || slot >= Slots.Count) return new MenuInput();
            var m = new MenuInput();
            if (Slots[slot].Device != null) Merge(ref m, MenuFrom(Slots[slot].Device));
            if (Slots[slot].Extra != null) Merge(ref m, MenuFrom(Slots[slot].Extra));
            return m;
        }

        private static void Merge(ref MenuInput m, MenuInput d)
        {
            if (d.X != 0) m.X = d.X;
            if (d.Y != 0) m.Y = d.Y;
            if (d.Tab != 0) m.Tab = d.Tab;
            if (d.Confirm && m.Device == null) m.Device = d.Device;
            m.Confirm |= d.Confirm;
            m.Back |= d.Back;
        }

        private MenuInput MenuFrom(InputDevice d)
        {
            var m = new MenuInput();
            if (Blocked) return m;
            var v = Stick(d);
            var g = d as Gamepad;
            var kb = d as Keyboard;
            if (kb != null) v += Aim(kb);
            // one direction at a time; a push steps once, holding it repeats after a moment
            var dir = v.y > 0.6f ? 1 : v.y < -0.6f ? 2 : v.x < -0.6f ? 3 : v.x > 0.6f ? 4 : 0;
            NavState st;
            if (!_nav.TryGetValue(d, out st)) _nav[d] = st = new NavState();
            var step = false;
            if (dir != st.Dir) { st.Dir = dir; st.Held = 0f; st.Next = 0.38f; step = dir != 0; }
            else if (dir != 0)
            {
                st.Held += Time.unscaledDeltaTime;
                if (st.Held >= st.Next) { st.Next += 0.07f; step = true; }
            }
            if (step)
            {
                if (dir == 1) m.Y = 1; else if (dir == 2) m.Y = -1; else if (dir == 3) m.X = -1; else if (dir == 4) m.X = 1;
            }
            if (g != null)
            {
                m.Confirm = g.buttonSouth.wasPressedThisFrame;
                m.Back = g.buttonEast.wasPressedThisFrame || g.startButton.wasPressedThisFrame;
                m.Tab = g.leftShoulder.wasPressedThisFrame ? -1 : g.rightShoulder.wasPressedThisFrame ? 1 : 0;
            }
            if (kb != null)
            {
                m.Confirm = kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
                m.Back = kb.escapeKey.wasPressedThisFrame;
                m.Tab = kb.qKey.wasPressedThisFrame ? -1 : kb.eKey.wasPressedThisFrame ? 1 : 0;
            }
            if (m.Confirm) m.Device = d;
            return m;
        }

        /// <summary>Stick / d-pad / WASD / arrows turned into single steps (one per push), for menus.</summary>
        private static Vector2Int Nav(Slot s)
        {
            var v = StickOf(s);
            if (s.Device is Keyboard) v += Aim(s.Device);
            if (s.Extra is Keyboard) v += Aim(s.Extra);
            bool left = v.x < -0.6f, right = v.x > 0.6f, up = v.y > 0.6f, down = v.y < -0.6f;
            var r = new Vector2Int((left && !s.PrevLeft) ? -1 : (right && !s.PrevRight) ? 1 : 0, (up && !s.PrevUp) ? 1 : (down && !s.PrevDown) ? -1 : 0);
            s.PrevLeft = left; s.PrevRight = right; s.PrevUp = up; s.PrevDown = down;
            return r;
        }

        // ------------------------------------------------------------------ playing

        /// <summary>Every frame: remember presses until the next tick reads them.</summary>
        public void LatchPresses()
        {
            if (Blocked) return;
            PairDevices();
            foreach (var s in Slots) s.Latched |= (byte)(Buttons(s.Device, true) | Buttons(s.Extra, true));
        }

        /// <summary>
        /// Square (PlayStation) / X (Xbox): take over the next bot teammate. Cycles round the bots on your team.
        /// Returns true if a swap happened.
        /// </summary>
        public bool SwapToBotTeammate(int human)
        {
            if (human < 0 || human >= Slots.Count || Slots[human].Demo) return false;
            var team = Slots[human].Team & 1;
            var bots = new List<int>();
            for (var i = 0; i < Slots.Count; i++)
                if (Slots[i].Demo && (Slots[i].Team & 1) == team) bots.Add(i);
            if (bots.Count == 0) return false;
            // next bot after this human's index, wrapping
            var pick = bots[0];
            foreach (var b in bots) if (b > human) { pick = b; break; }
            SwapControl(human, pick);
            return true;
        }

        /// <summary>
        /// Hand the human's device to a bot slot (and make the old slot a bot). The look (colour, symbol, team) stays
        /// with each player on the ice; only who steers them changes.
        /// </summary>
        public void SwapControl(int human, int bot)
        {
            if (human < 0 || bot < 0 || human >= Slots.Count || bot >= Slots.Count) return;
            if (Slots[human].Demo || !Slots[bot].Demo) return;
            var h = Slots[human];
            var b = Slots[bot];
            var device = h.Device; var extra = h.Extra; var name = h.Name; var latched = h.Latched;
            h.Device = null; h.Extra = null; h.Demo = true; h.Name = "Bot"; h.Latched = 0;
            b.Device = device; b.Extra = extra; b.Demo = false; b.Name = name; b.Latched = latched;
            Debug.Log("Ultra Puck: " + (string.IsNullOrEmpty(name) ? "a player" : name) + " now steers player " + (bot + 1));
        }

        /// <summary>
        /// A bot on <paramref name="team"/> just got the puck: hand a random human on that team over to them (so you
        /// always drive whoever has it when you're the only person, or one of you at random when there are several).
        /// </summary>
        public bool AutoSwapToCarrier(int carrier, int team)
        {
            if (carrier < 0 || carrier >= Slots.Count || !Slots[carrier].Demo) return false;
            var humans = new List<int>();
            for (var i = 0; i < Slots.Count; i++)
                if (!Slots[i].Demo && (Slots[i].Team & 1) == (team & 1)) humans.Add(i);
            if (humans.Count == 0) return false;
            var who = humans[UnityEngine.Random.Range(0, humans.Count)];
            SwapControl(who, carrier);
            return true;
        }

        /// <summary>Square / X was pressed this frame on this slot's device (not used as a game button).</summary>
        private static bool SwapPressed(Slot s)
        {
            return One(s.Device, g => g.buttonWest.wasPressedThisFrame, k => false)
                || One(s.Extra, g => g.buttonWest.wasPressedThisFrame, k => false);
        }

        /// <summary>Once per tick: everyone's controls, in slot order. <paramref name="state"/> is only read by computer players.</summary>
        public PlayerInput[] Sample(Simulation sim, GameState state)
        {
            // Square / X: swap onto a bot teammate before reading sticks (so this press doesn't do anything else)
            if (!Blocked)
                for (var i = 0; i < Slots.Count; i++)
                    if (!Slots[i].Demo && SwapPressed(Slots[i])) SwapToBotTeammate(i);

            var inputs = new PlayerInput[Slots.Count];
            for (var i = 0; i < Slots.Count; i++)
            {
                var s = Slots[i];
                if (s.Demo) { inputs[i] = Bot.Think(sim, state, i); continue; }
                if (!(Plugged(s.Device) || Plugged(s.Extra)) || Blocked) continue; // unplugged, or the Steam overlay is open: stands still
                var m = StickOf(s);
                var a = AimOfSlot(s);
                inputs[i] = new PlayerInput
                {
                    MoveX = PlayerInput.Axis(m.x),
                    MoveY = PlayerInput.Axis(m.y),
                    AimX = PlayerInput.Axis(a.x),
                    AimY = PlayerInput.Axis(a.y),
                    Buttons = (byte)(Buttons(s.Device, false) | Buttons(s.Extra, false) | s.Latched),
                    Pick = state != null && state.Phase == Phase.Picking && PickFor != null ? PickFor(i) : (byte)0,
                    Hover = state != null && state.Phase == Phase.Picking && HoverFor != null ? HoverFor(i) : (byte)0,
                };
                s.Latched = 0;
            }
            return inputs;
        }

        // ------------------------------------------------------------------ computer players

        /// <summary>ULTRAPUCK_DEMO=n: n computer players join and the match starts by itself.</summary>
        public int AddDemoPlayers()
        {
            int n;
            if (!int.TryParse(System.Environment.GetEnvironmentVariable("ULTRAPUCK_DEMO"), out n)) return 0;
            for (var i = 0; i < Mathf.Clamp(n, 0, MaxPlayers); i++) Slots.Add(new Slot { Team = i & 1, Demo = true });
            return Slots.Count;
        }

        // ------------------------------------------------------------------ devices

        /// <summary>The move stick: left stick (or d-pad) on a controller, WASD on the keyboard.</summary>
        private static Vector2 Stick(InputDevice d)
        {
            var g = d as Gamepad;
            if (g != null)
            {
                var v = g.leftStick.ReadValue();
                if (v.sqrMagnitude < 0.01f) v = g.dpad.ReadValue();
                return Vector2.ClampMagnitude(v, 1f);
            }
            var kb = d as Keyboard;
            if (kb == null) return Vector2.zero;
            var x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            var y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }

        /// <summary>The aim stick: right stick on a controller, arrow keys on the keyboard. Zero when not pushed.</summary>
        private static Vector2 Aim(InputDevice d)
        {
            var g = d as Gamepad;
            if (g != null) return Vector2.ClampMagnitude(g.rightStick.ReadValue(), 1f);
            var kb = d as Keyboard;
            if (kb == null) return Vector2.zero;
            var x = (kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f);
            var y = (kb.upArrowKey.isPressed ? 1f : 0f) - (kb.downArrowKey.isPressed ? 1f : 0f);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }

        private static byte Buttons(InputDevice d, bool pressedThisFrame)
        {
            byte b = 0;
            var g = d as Gamepad;
            if (g != null)
            {
                if (Btn(g.buttonSouth, pressedThisFrame) || Btn(g.rightShoulder, pressedThisFrame)) b |= PlayerInput.ShootCheck;
                if (Btn(g.buttonEast, pressedThisFrame) || Btn(g.leftShoulder, pressedThisFrame)) b |= PlayerInput.ChargeBrake;
                if (Btn(g.buttonNorth, pressedThisFrame)) b |= PlayerInput.Utility;
                if (Btn(g.selectButton, pressedThisFrame)) b |= PlayerInput.ResetPuck;
                return b;
            }
            var kb = d as Keyboard;
            if (kb == null) return 0;
            if (Btn(kb.spaceKey, pressedThisFrame) || Btn(kb.jKey, pressedThisFrame)) b |= PlayerInput.ShootCheck;
            if (Btn(kb.leftShiftKey, pressedThisFrame) || Btn(kb.kKey, pressedThisFrame)) b |= PlayerInput.ChargeBrake;
            if (Btn(kb.eKey, pressedThisFrame) || Btn(kb.lKey, pressedThisFrame)) b |= PlayerInput.Utility;
            if (Btn(kb.rKey, pressedThisFrame)) b |= PlayerInput.ResetPuck;
            return b;
        }

        private static bool Btn(UnityEngine.InputSystem.Controls.ButtonControl c, bool pressedThisFrame)
        {
            return c != null && (pressedThisFrame ? c.wasPressedThisFrame : c.isPressed);
        }
    }
}
