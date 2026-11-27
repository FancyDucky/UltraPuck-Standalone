using System;
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
    /// <item>Swap to a bot teammate: X / Square (or the key in Settings &gt; Controls).</item>
    /// <item>Puck back (free play): View / Select. Menu: Start / Options.</item>
    /// <item>Keyboard: WASD move, mouse (or arrows) aim, Space shoot/check, Left Shift charge/brake, E utility, F swap, R puck back, Esc menu. Mouse left/right also shoot / charge.</item>
    /// <item>Bindings are editable in Settings &gt; Controls.</item>
    /// </list>
    /// </summary>
    internal sealed class LocalPlayers
    {
        public const int MaxPlayers = 8;

        /// <summary>World point under the mouse cursor, or null. Set by <see cref="GameRunner"/> for mouse aiming.</summary>
        public System.Func<Vector2?> MouseWorld;
        /// <summary>World position of player slot <c>i</c>. Set by <see cref="GameRunner"/> for mouse aiming.</summary>
        public System.Func<int, Vector2> PlayerWorld;
        /// <summary>
        /// Way from player slot <c>i</c>'s skater to the middle of the net they attack, or null (carrying the puck, no
        /// match). Set by <see cref="GameRunner"/>: a controller player holding charge without the puck faces it.
        /// </summary>
        public System.Func<int, Vector2?> NetAim;
        /// <summary>Mouse left/right count as shoot/charge (only while a match is playable — not in menus).</summary>
        public bool AllowMouseButtons;

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
            /// <summary>Free play: a d-pad puck drill pressed since the last tick (a <see cref="PlayerInput.PuckToMe"/>-style Pick).</summary>
            public byte PuckDrill;
            /// <summary>Swap-to-bot pressed since the last tick (latched like buttons so a short tap still counts).</summary>
            public bool WantSwap;
            public bool PrevLeft, PrevRight, PrevUp, PrevDown;
            /// <summary>A computer player (free play bots, or the ULTRAPUCK_DEMO players).</summary>
            public bool Demo;
            /// <summary>How long B / Escape has been held (holding it long enough leaves).</summary>
            public float BackHeld;
            /// <summary>Readied up on the players screen: from then on their controls work the bots instead.</summary>
            public bool Ready;
            /// <summary>Online, mid-match: chose to watch from the pause menu (gives up their seat until they pick a team).</summary>
            public bool Watching;
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
            var g = Live(d) as Gamepad;
            if (g != null) return pad(g);
            var k = Live(d) as Keyboard;
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

        private static bool HasKeyboard(Slot s)
        {
            return s.Device is Keyboard || s.Extra is Keyboard;
        }

        /// <summary>True if any human here is on the keyboard (for the mouse reticle).</summary>
        public bool AnyKeyboardHuman()
        {
            foreach (var s in Slots) if (!s.Demo && HasKeyboard(s)) return true;
            return false;
        }

        private Vector2 AimOfSlot(Slot s, int i)
        {
            // right stick wins when pushed; mouse only in keyboard/mouse mode (last device used); else aim keys
            var pad = AimPad(s.Device);
            var pad2 = AimPad(s.Extra);
            var stick = pad2.sqrMagnitude > pad.sqrMagnitude ? pad2 : pad;
            if (stick.sqrMagnitude > 0.04f) return stick;

            if (!InputMode.Pad && HasKeyboard(s) && MouseWorld != null && PlayerWorld != null)
            {
                var w = MouseWorld();
                if (w.HasValue)
                {
                    var d = w.Value - PlayerWorld(i);
                    if (d.sqrMagnitude > 0.0001f) return d.normalized;
                }
            }

            var a = AimKeys(s.Device);
            var b = AimKeys(s.Extra);
            return b.sqrMagnitude > a.sqrMagnitude ? b : a;
        }

        private static bool Plugged(InputDevice d) { return d != null && d.added; }

        /// <summary>
        /// The device if it's still in the input system, else null. A controller that drops out and comes back can
        /// leave a slot holding the old, dead device: reading it throws every frame, which used to stop the frame
        /// before the game stepped (online, everyone else then waited on this PC).
        /// </summary>
        private static InputDevice Live(InputDevice d) { return d != null && d.added ? d : null; }

        /// <summary>
        /// A controller came (back): a slot still holding a dead controller takes this one (the same kind first), so
        /// a dropped-out controller keeps playing its player.
        /// </summary>
        public void DeviceAdded(InputDevice d)
        {
            if (!(d is Gamepad) || Find(d) != null) return;
            Slot pick = null;
            foreach (var s in Slots)
            {
                if (s.Demo || !(s.Device is Gamepad) || s.Device.added) continue;
                if (pick == null || (s.Device.layout == d.layout && pick.Device.layout != d.layout)) pick = s;
            }
            if (pick == null) return;
            Debug.Log("Ultra Puck: " + d.displayName + " takes back its player");
            pick.Device = d;
        }

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
        /// Lobby: join with A / Space; left / right switches team, X / M changes the symbol, A again readies up.
        /// Readied, up / down picks a bot, left / right moves it to the other team, X / M changes its symbol,
        /// LB / RB (- / =) removes or adds one, and B / Escape un-readies. B / Escape before that gives up the seat;
        /// held by someone without a seat, it leaves. (Renaming from the lobby is off for now.)
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
            NameRequest = null; // renaming from the lobby is off for now
            foreach (var s in Slots.ToArray())
            {
                if (s.Demo || s.Typing || !Slots.Contains(s)) continue;
                var nav = Nav(s);
                // either of the player's devices (the keyboard and a controller, when they share)
                var confirm = s.JoinedFrame != Time.frameCount && Any(s, g => g.buttonSouth.wasPressedThisFrame, k => k.spaceKey.wasPressedThisFrame);
                var symbol = Any(s, g => g.buttonWest.wasPressedThisFrame, k => k.mKey.wasPressedThisFrame);
                var less = Any(s, g => g.leftShoulder.wasPressedThisFrame, k => k.minusKey.wasPressedThisFrame);
                var more = Any(s, g => g.rightShoulder.wasPressedThisFrame, k => k.equalsKey.wasPressedThisFrame);
                // D-pad up / down (arrow keys): a new colour for your whole side (not the stick, so it's never by accident)
                var colourUp = Any(s, g => g.dpad.up.wasPressedThisFrame, k => k.upArrowKey.wasPressedThisFrame);
                var colourDown = Any(s, g => g.dpad.down.wasPressedThisFrame, k => k.downArrowKey.wasPressedThisFrame);
                if (!s.Ready)
                {
                    // left / right: over to the other side (in its colour)
                    if (nav.x != 0) { var sides = SideColours(); s.Colour = s.Colour == sides[0] ? sides[1] : sides[0]; AssignTeams(); Sound.Step(nav.x); }
                    if (TeamColourButtons && (colourUp || colourDown)) RecolourSide(s, colourUp ? 1 : -1);
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

        /// <summary>D-pad up / down changes your side's colour. Off for now: it isn't working right yet (to look at again).</summary>
        public const bool TeamColourButtons = false;

        /// <summary>Online: the two sides' colours come from the game (set by GameRunner); here, from the players.</summary>
        public System.Func<int[]> OnlineSides;
        /// <summary>Online: asking for a side's new colour goes to the game (from, to); here it's done straight away.</summary>
        public System.Action<int, int> OnlineRecolour;

        /// <summary>The colours of side 0 and side 1.</summary>
        public int[] SideColours()
        {
            if (OnlineSides != null) return OnlineSides();
            var colours = Colours();
            var a = colours.Count > 0 ? colours[0] : 0;
            var b = colours.Count > 1 ? colours[1] : a == 1 ? 0 : 1;
            return new[] { a, b };
        }

        /// <summary>Step <paramref name="s"/>'s side to the next colour (skipping the other side's), for everyone on it.</summary>
        private void RecolourSide(Slot s, int dir)
        {
            var sides = SideColours();
            var from = s.Colour;
            var other = from == sides[0] ? sides[1] : sides[0];
            var n = Palette.Colours.Length;
            var to = from;
            do to = ((to + dir) % n + n) % n; while (to == other && to != from);
            if (to == from) return;
            Sound.Step(dir);
            if (OnlineRecolour != null) { OnlineRecolour(from, to); return; }
            foreach (var x in Slots) if (x.Colour == from) x.Colour = to;
            AssignTeams();
        }

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

        /// <summary>After a match: Y / R from anyone who has joined saves its replay.</summary>
        public bool SaveReplayPressed()
        {
            if (Blocked) return false;
            foreach (var s in Slots)
                if (Any(s, g => g.buttonNorth.wasPressedThisFrame, k => k.rKey.wasPressedThisFrame)) return true;
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

        /// <summary>Is a joined person holding Start / Options (Esc)? (The host holds it to end a card pick.)</summary>
        public bool MenuHeld()
        {
            if (Blocked) return false;
            foreach (var s in Slots)
                if (Any(s, g => g.startButton.isPressed, k => k.escapeKey.isPressed)) return true;
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
        /// <summary>View / Select on any controller, or Tab, held right now.</summary>
        public bool SettingsHeld()
        {
            if (Blocked) return false;
            foreach (var g in Gamepad.all) if (g.added && g.selectButton.isPressed) return true;
            var kb = Keyboard.current;
            return kb != null && kb.tabKey.isPressed;
        }

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
            return slot >= 0 && slot < Slots.Count ? AimOfSlot(Slots[slot], slot) : Vector2.zero;
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

        /// <summary>A bot on <paramref name="team"/> (mid-match, from Match settings). False when the match is full.</summary>
        public bool AddBotTo(int team)
        {
            if (Slots.Count >= MaxPlayers) return false;
            // the colour another player on that team already has (so it lands on that team)
            var colour = team & 1;
            foreach (var s in Slots) if ((s.Team & 1) == (team & 1)) { colour = s.Colour; break; }
            Slots.Add(new Slot { Demo = true, Colour = colour, Symbol = FreeSymbol(), Name = "Bot", Team = team & 1 });
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
            var g = Live(d) as Gamepad;
            var kb = Live(d) as Keyboard;
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

        /// <summary>
        /// Free play: the d-pad runs the puck drills (down: puck to you, up: at you, right: at their net, left: at yours)
        /// instead of moving you. Set by the game while free play is being played (no menu open).
        /// </summary>
        public static bool PuckDrills;

        private static byte DrillPressed(InputDevice d)
        {
            var g = Live(d) as Gamepad;
            if (g == null) return 0;
            if (g.dpad.down.wasPressedThisFrame) return PlayerInput.PuckToMe;
            if (g.dpad.up.wasPressedThisFrame) return PlayerInput.PuckAtMe;
            if (g.dpad.right.wasPressedThisFrame) return PlayerInput.PuckAtTheirNet;
            if (g.dpad.left.wasPressedThisFrame) return PlayerInput.PuckAtOurNet;
            return 0;
        }

        /// <summary>Every frame: remember presses until the next tick reads them.</summary>
        public void LatchPresses()
        {
            if (Blocked) return;
            PairDevices();
            foreach (var s in Slots)
            {
                s.Latched |= (byte)(Buttons(s.Device, true) | Buttons(s.Extra, true));
                if (HasKeyboard(s)) s.Latched |= MouseButtons(true);
                if (PuckDrills)
                {
                    var d = DrillPressed(s.Device);
                    if (d == 0) d = DrillPressed(s.Extra);
                    if (d != 0) s.PuckDrill = d;
                }
                if (SwapPressed(s, true)) s.WantSwap = true;
            }
        }

        /// <summary>
        /// Swap-to-bot binding, plus Square / X and F as a fallback so teammate swap still works if the rebind was
        /// cleared or shared with another action.
        /// </summary>
        private static bool SwapPressed(Slot s, bool pressedThisFrame)
        {
            return SwapOn(s.Device, pressedThisFrame) || SwapOn(s.Extra, pressedThisFrame);
        }

        private static bool SwapOn(InputDevice d, bool pressedThisFrame)
        {
            if (d == null) return false;
            var g = Live(d) as Gamepad;
            if (g != null)
            {
                if (pressedThisFrame ? Controls.PadPressed(g, Controls.Act.Swap) : Controls.PadHeld(g, Controls.Act.Swap)) return true;
                // hard fallback: X / Square always cycles bots (even if Controls.Act.Swap was rebound away)
                var west = g.buttonWest;
                return west != null && (pressedThisFrame ? west.wasPressedThisFrame : west.isPressed);
            }
            var kb = Live(d) as Keyboard;
            if (kb == null) return false;
            if (pressedThisFrame ? Controls.KeyPressed(kb, Controls.Act.Swap) : Controls.KeyHeld(kb, Controls.Act.Swap)) return true;
            var f = kb.fKey;
            return f != null && (pressedThisFrame ? f.wasPressedThisFrame : f.isPressed);
        }

        /// <summary>The bits of slot <c>i</c>'s look that change the game (<see cref="PlayerInput.Look"/>).</summary>
        public System.Func<int, byte> LookFor;

        /// <summary>Once per tick: everyone's controls, in slot order. <paramref name="state"/> is only read by computer players.</summary>
        public PlayerInput[] Sample(Simulation sim, GameState state)
        {
            // swapping onto a bot teammate is part of the game (Simulation.Route): a press here is the Swap bit for
            // one tick (latched every frame so a short tap counts). Bots' entries stay empty: Route thinks for them.
            var inputs = new PlayerInput[Slots.Count];
            for (var i = 0; i < Slots.Count; i++)
            {
                var s = Slots[i];
                var swap = s.WantSwap && !Blocked;
                s.WantSwap = false;
                if (s.Demo) continue;
                var setting = (byte)((Settings.DashWhereMoving ? PlayerInput.DashMove : 0) | (Settings.AutoSwapBots ? 0 : PlayerInput.NoAutoSwap));
                var look = LookFor != null ? LookFor(i) : (byte)0;
                if (!(Plugged(s.Device) || Plugged(s.Extra)) || Blocked) { inputs[i].Buttons = setting; inputs[i].Look = look; continue; } // unplugged, or the Steam overlay is open: stands still
                var m = StickOf(s);
                var a = AimOfSlot(s, i);
                // controllers: holding charge without the puck (a pre-charge) faces the middle of their net, unless
                // the aim stick is pushed (the stick always wins). Just an aim like the stick's, so the rules don't change.
                if (a.sqrMagnitude <= 0.04f && NetAim != null && PadCharging(s))
                {
                    var net = NetAim(i);
                    if (net.HasValue) a = net.Value;
                }
                inputs[i] = new PlayerInput
                {
                    MoveX = PlayerInput.Axis(m.x),
                    MoveY = PlayerInput.Axis(m.y),
                    AimX = PlayerInput.Axis(a.x),
                    AimY = PlayerInput.Axis(a.y),
                    Buttons = (byte)(Buttons(s.Device, false) | Buttons(s.Extra, false) | (HasKeyboard(s) ? MouseButtons(false) : 0) | s.Latched
                        | setting | (swap ? PlayerInput.Swap : 0)),
                    // (outside the card screen too, for someone catching up on cards after joining part-way)
                    Pick = state != null && PickFor != null && (state.Phase == Phase.Picking || PickFor(i) != 0) ? PickFor(i) : PuckDrills ? s.PuckDrill : (byte)0,
                    Hover = state != null && HoverFor != null ? HoverFor(i) : (byte)0,
                    Look = look,
                };
                s.Latched = 0;
                s.PuckDrill = 0;
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

        /// <summary>The move stick: left stick (or d-pad) on a controller, move keys on the keyboard.</summary>
        private static Vector2 Stick(InputDevice d)
        {
            var g = Live(d) as Gamepad;
            if (g != null)
            {
                var v = g.leftStick.ReadValue();
                if (v.sqrMagnitude < 0.01f && !PuckDrills) v = g.dpad.ReadValue(); // (free play: the d-pad is for the puck drills)
                return Vector2.ClampMagnitude(v, 1f);
            }
            var kb = Live(d) as Keyboard;
            if (kb == null) return Vector2.zero;
            var x = (Controls.KeyHeld(kb, Controls.Act.MoveRight) ? 1f : 0f) - (Controls.KeyHeld(kb, Controls.Act.MoveLeft) ? 1f : 0f);
            var y = (Controls.KeyHeld(kb, Controls.Act.MoveUp) ? 1f : 0f) - (Controls.KeyHeld(kb, Controls.Act.MoveDown) ? 1f : 0f);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }

        /// <summary>Charge / brake held on one of the slot's controllers (not the keyboard or mouse).</summary>
        private static bool PadCharging(Slot s)
        {
            return (Live(s.Device) is Gamepad && (Buttons(s.Device, false) & PlayerInput.ChargeBrake) != 0)
                || (Live(s.Extra) is Gamepad && (Buttons(s.Extra, false) & PlayerInput.ChargeBrake) != 0);
        }

        /// <summary>Right stick on a controller only (keyboard aim is keys or mouse).</summary>
        private static Vector2 AimPad(InputDevice d)
        {
            var g = Live(d) as Gamepad;
            return g != null ? Vector2.ClampMagnitude(g.rightStick.ReadValue(), 1f) : Vector2.zero;
        }

        /// <summary>Aim keys on the keyboard. Zero when not pushed.</summary>
        private static Vector2 AimKeys(InputDevice d)
        {
            var kb = Live(d) as Keyboard;
            if (kb == null) return Vector2.zero;
            var x = (Controls.KeyHeld(kb, Controls.Act.AimRight) ? 1f : 0f) - (Controls.KeyHeld(kb, Controls.Act.AimLeft) ? 1f : 0f);
            var y = (Controls.KeyHeld(kb, Controls.Act.AimUp) ? 1f : 0f) - (Controls.KeyHeld(kb, Controls.Act.AimDown) ? 1f : 0f);
            return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
        }

        /// <summary>Did slot <paramref name="slot"/>'s person press the utility button this frame (New Hand's re-roll)?</summary>
        public bool UtilityPressed(int slot)
        {
            if (slot < 0 || slot >= Slots.Count || Slots[slot].Demo || Blocked) return false;
            return ((Buttons(Slots[slot].Device, true) | Buttons(Slots[slot].Extra, true)) & PlayerInput.Utility) != 0;
        }

        /// <summary>Aim for menu nav: pad stick or keyboard aim keys (same as the old Aim helper).</summary>
        private static Vector2 Aim(InputDevice d)
        {
            var pad = AimPad(d);
            if (pad.sqrMagnitude > 0.01f) return pad;
            return AimKeys(d);
        }

        private static byte Buttons(InputDevice d, bool pressedThisFrame)
        {
            byte b = 0;
            var g = Live(d) as Gamepad;
            if (g != null)
            {
                if (pressedThisFrame ? Controls.PadPressed(g, Controls.Act.Shoot) : Controls.PadHeld(g, Controls.Act.Shoot)) b |= PlayerInput.ShootCheck;
                if (pressedThisFrame ? Controls.PadPressed(g, Controls.Act.Charge) : Controls.PadHeld(g, Controls.Act.Charge)) b |= PlayerInput.ChargeBrake;
                if (pressedThisFrame ? Controls.PadPressed(g, Controls.Act.Utility) : Controls.PadHeld(g, Controls.Act.Utility)) b |= PlayerInput.Utility;
                if (pressedThisFrame ? Controls.PadPressed(g, Controls.Act.ResetPuck) : Controls.PadHeld(g, Controls.Act.ResetPuck)) b |= PlayerInput.ResetPuck;
                return b;
            }
            var kb = Live(d) as Keyboard;
            if (kb == null) return 0;
            if (pressedThisFrame ? Controls.KeyPressed(kb, Controls.Act.Shoot) : Controls.KeyHeld(kb, Controls.Act.Shoot)) b |= PlayerInput.ShootCheck;
            if (pressedThisFrame ? Controls.KeyPressed(kb, Controls.Act.Charge) : Controls.KeyHeld(kb, Controls.Act.Charge)) b |= PlayerInput.ChargeBrake;
            if (pressedThisFrame ? Controls.KeyPressed(kb, Controls.Act.Utility) : Controls.KeyHeld(kb, Controls.Act.Utility)) b |= PlayerInput.Utility;
            if (pressedThisFrame ? Controls.KeyPressed(kb, Controls.Act.ResetPuck) : Controls.KeyHeld(kb, Controls.Act.ResetPuck)) b |= PlayerInput.ResetPuck;
            return b;
        }

        /// <summary>Mouse left = shoot/check, right = charge/brake (keyboard/mouse mode only).</summary>
        private byte MouseButtons(bool pressedThisFrame)
        {
            if (!AllowMouseButtons || InputMode.Pad) return 0;
            var m = Mouse.current;
            if (m == null) return 0;
            byte b = 0;
            if (pressedThisFrame ? m.leftButton.wasPressedThisFrame : m.leftButton.isPressed) b |= PlayerInput.ShootCheck;
            if (pressedThisFrame ? m.rightButton.wasPressedThisFrame : m.rightButton.isPressed) b |= PlayerInput.ChargeBrake;
            return b;
        }
    }
}
