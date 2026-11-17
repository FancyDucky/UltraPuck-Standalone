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
    /// <item>Puck back (free play): View / Select. Menu: Start / Options.</item>
    /// <item>Keyboard: WASD move, arrow keys aim, Space or J shoot/check, Left Shift or K charge/brake, E or L utility, R puck back, Esc menu.</item>
    /// </list>
    /// </summary>
    internal sealed class LocalPlayers
    {
        public const int MaxPlayers = 4;

        public sealed class Slot
        {
            public InputDevice Device;
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
            public string Label { get { return Demo ? "Bot" : Device is Keyboard ? "Keyboard" : Device != null ? Device.displayName : "Disconnected"; } }
        }

        public readonly List<Slot> Slots = new List<Slot>();

        public int[] Teams()
        {
            var t = new int[Slots.Count];
            for (var i = 0; i < t.Length; i++) t[i] = Slots[i].Team;
            return t;
        }

        private Slot Find(InputDevice d) { return Slots.Find(s => s.Device == d); }

        // ------------------------------------------------------------------ lobby

        /// <summary>The Steam overlay is open: the game ignores controllers and the keyboard until it closes.</summary>
        public static bool Blocked;

        /// <summary>Seconds B / Escape must be held (by someone who hasn't joined) to leave the lobby.</summary>
        public const float HoldToLeave = 0.8f;

        /// <summary>How far an unjoined controller or keyboard has got with holding B / Escape (0..1): leaving the lobby itself.</summary>
        public float NobodyLeaving { get; private set; }

        /// <summary>Lobby: join with A / Space, pick a side with left / right, hold B / Escape to leave.</summary>
        public void LobbyUpdate()
        {
            var dt = Time.unscaledDeltaTime;
            var nobody = 0f;
            if (Blocked) { NobodyLeaving = 0f; return; }
            foreach (var g in Gamepad.all)
            {
                var s = Find(g);
                if (s == null && g.buttonSouth.wasPressedThisFrame) Join(g);
                else if (s != null) { if (!s.Typing) Hold(s, g.buttonEast.isPressed, dt); }
                else if (g.buttonEast.isPressed) nobody = Mathf.Max(nobody, Held(g, dt));
                else _nobodyHeld.Remove(g);
            }
            var kb = Keyboard.current;
            if (kb != null)
            {
                var s = Find(kb);
                if (s == null && kb.spaceKey.wasPressedThisFrame) Join(kb);
                else if (s != null) { if (!s.Typing) Hold(s, kb.escapeKey.isPressed, dt); }
                else if (kb.escapeKey.isPressed) nobody = Mathf.Max(nobody, Held(kb, dt));
                else _nobodyHeld.Remove(kb);
            }
            NobodyLeaving = Mathf.Clamp01(nobody / HoldToLeave);
            BotRequest = 0;
            foreach (var s in Slots.ToArray())
            {
                if (s.Demo || s.Typing || !Slots.Contains(s)) continue;
                var nav = Nav(s);
                if (nav.x != 0) { s.Colour = (s.Colour + nav.x + Palette.Colours.Length) % Palette.Colours.Length; AssignTeams(); }
                var pad = s.Device as Gamepad;
                var keys = s.Device as Keyboard;
                var name = pad != null ? pad.buttonNorth.wasPressedThisFrame : keys != null && keys.nKey.wasPressedThisFrame;
                var symbol = pad != null ? pad.buttonWest.wasPressedThisFrame : keys != null && keys.mKey.wasPressedThisFrame;
                if (name) NameRequest = s;
                if (symbol) s.Symbol = (s.Symbol + 1) % Symbols.Count;
                if (pad != null) { if (pad.leftShoulder.wasPressedThisFrame) BotRequest = -1; if (pad.rightShoulder.wasPressedThisFrame) BotRequest = 1; }
                if (keys != null) { if (keys.minusKey.wasPressedThisFrame) BotRequest = -1; if (keys.equalsKey.wasPressedThisFrame) BotRequest = 1; }
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
            var g = s.Device as Gamepad;
            var kb = s.Device as Keyboard;
            var pressed = g != null ? g.buttonEast.wasPressedThisFrame : kb != null && kb.escapeKey.wasPressedThisFrame;
            if (!pressed) return;
            Slots.Remove(s);
            AssignTeams();
            _nobodyHeld[s.Device] = -1000f;
        }

        /// <summary>Someone who hasn't joined held B / Escape long enough: leave the lobby.</summary>
        public bool LeaveLobbyHeld() { return NobodyLeaving >= 1f; }

        private bool Join(InputDevice d)
        {
            if (Slots.Count >= MaxPlayers) return false;
            var s = new Slot { Device = d, Colour = ColourForNewcomer(), Symbol = FreeSymbol() };
            var humans = 0;
            foreach (var o in Slots) if (!o.Demo) humans++;
            s.Name = humans == 0 ? Settings.PlayerName : "P" + (Slots.Count + 1);
            Slots.Add(s);
            AssignTeams();
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
            {
                var g = s.Device as Gamepad;
                if (g != null && g.startButton.wasPressedThisFrame) return true;
                var kb = s.Device as Keyboard;
                if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) return true;
            }
            return false;
        }

        /// <summary>B / Escape from anyone who has joined.</summary>
        public bool BackPressed()
        {
            if (Blocked) return false;
            foreach (var s in Slots)
            {
                var g = s.Device as Gamepad;
                if (g != null && g.buttonEast.wasPressedThisFrame) return true;
                var kb = s.Device as Keyboard;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) return true;
            }
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
            return joined;
        }

        /// <summary>Start / Options / Escape from anyone playing (opens the menu).</summary>
        public bool MenuPressed()
        {
            if (Blocked) return false;
            foreach (var s in Slots)
            {
                var g = s.Device as Gamepad;
                if (g != null && g.startButton.wasPressedThisFrame) return true;
                var kb = s.Device as Keyboard;
                if (kb != null && kb.escapeKey.wasPressedThisFrame) return true;
            }
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
            return slot >= 0 && slot < Slots.Count && Slots[slot].Device != null ? Aim(Slots[slot].Device) : Vector2.zero;
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
            foreach (var s in Slots) if (!s.Demo && s.Device != null) Merge(ref m, MenuFrom(s.Device));
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
            if (slot < 0 || slot >= Slots.Count || Slots[slot].Device == null) return new MenuInput();
            return MenuFrom(Slots[slot].Device);
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
            var v = Stick(s.Device);
            var kb = s.Device as Keyboard;
            if (kb != null) v += Aim(kb);
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
            foreach (var s in Slots) s.Latched |= Buttons(s.Device, true);
        }

        /// <summary>Once per tick: everyone's controls, in slot order. <paramref name="state"/> is only read by computer players.</summary>
        public PlayerInput[] Sample(Simulation sim, GameState state)
        {
            var inputs = new PlayerInput[Slots.Count];
            for (var i = 0; i < Slots.Count; i++)
            {
                var s = Slots[i];
                if (s.Demo) { inputs[i] = Bot.Think(sim, state, i); continue; }
                if (s.Device == null || !s.Device.added || Blocked) continue; // unplugged, or the Steam overlay is open: stands still
                var m = Stick(s.Device);
                var a = Aim(s.Device);
                inputs[i] = new PlayerInput
                {
                    MoveX = PlayerInput.Axis(m.x),
                    MoveY = PlayerInput.Axis(m.y),
                    AimX = PlayerInput.Axis(a.x),
                    AimY = PlayerInput.Axis(a.y),
                    Buttons = (byte)(Buttons(s.Device, false) | s.Latched),
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
