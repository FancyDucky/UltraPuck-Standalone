using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace UltraPuck.Game
{
    /// <summary>
    /// Keyboard and controller bindings for playing (saved in PlayerPrefs). The Controls settings tab edits these;
    /// <see cref="LocalPlayers"/> reads them every tick.
    /// </summary>
    internal static class Controls
    {
        /// <summary>Things you can rebind. Move and aim on a controller stay on the sticks.</summary>
        public enum Act
        {
            Shoot, Charge, Utility, Swap, ResetPuck,
            MoveLeft, MoveRight, MoveUp, MoveDown,
            AimLeft, AimRight, AimUp, AimDown,
        }

        /// <summary>Controller face / shoulder / trigger / menu buttons we allow as bindings.</summary>
        public enum Pad
        {
            South, East, West, North,
            LeftShoulder, RightShoulder, LeftTrigger, RightTrigger,
            Select, Start, LeftStick, RightStick,
            /// <summary>Not bound (cleared with Y / Triangle on the Controls tab).</summary>
            None,
        }

        private static readonly Act[] All = (Act[])Enum.GetValues(typeof(Act));

        private static readonly Key[] DefaultKeys =
        {
            Key.Space, Key.LeftShift, Key.E, Key.F, Key.R,
            Key.A, Key.D, Key.W, Key.S,
            Key.LeftArrow, Key.RightArrow, Key.UpArrow, Key.DownArrow,
        };

        private static readonly Pad[] DefaultPads =
        {
            Pad.South, Pad.East, Pad.North, Pad.West, Pad.Select,
            Pad.South, Pad.South, Pad.South, Pad.South, // move: unused on pad (sticks)
            Pad.South, Pad.South, Pad.South, Pad.South, // aim: unused on pad
        };

        // second pad binding for shoot / charge (shoulders), so the defaults match what people already use
        private static readonly Pad[] DefaultPads2 =
        {
            Pad.RightShoulder, Pad.LeftShoulder, Pad.North, Pad.West, Pad.Select,
            Pad.South, Pad.South, Pad.South, Pad.South,
            Pad.South, Pad.South, Pad.South, Pad.South,
        };

        public static string Label(Act a)
        {
            switch (a)
            {
                case Act.Shoot: return "Shoot / check";
                case Act.Charge: return "Charge / brake";
                case Act.Utility: return "Utility";
                case Act.Swap: return "Swap to teammate";
                case Act.ResetPuck: return "Puck back";
                case Act.MoveLeft: return "Move left";
                case Act.MoveRight: return "Move right";
                case Act.MoveUp: return "Move up";
                case Act.MoveDown: return "Move down";
                case Act.AimLeft: return "Aim left";
                case Act.AimRight: return "Aim right";
                case Act.AimUp: return "Aim up";
                case Act.AimDown: return "Aim down";
                default: return a.ToString();
            }
        }

        public static string Hint(Act a)
        {
            switch (a)
            {
                case Act.Shoot: return "With the puck: shoot. Without: check (dash).";
                case Act.Charge: return "With the puck: hold to charge, let go to fire. Without: brake, and pre-charge a shot for when the puck comes to you.";
                case Act.Utility: return "Power-up cards that need a button (Puck Gun, invisibility...).";
                case Act.Swap: return "Take over a bot on your team (cycles through them).";
                case Act.ResetPuck: return "Free play: put the puck back in the middle.";
                default: return a.ToString().StartsWith("Move") ? "Keyboard only: controllers use the left stick."
                    : "Keyboard only: controllers use the right stick.";
            }
        }

        /// <summary>Actions that only make sense on the keyboard (sticks cover them on a pad).</summary>
        public static bool KeyboardOnly(Act a)
        {
            return a >= Act.MoveLeft;
        }

        public static Key KeyOf(Act a)
        {
            var i = (int)a;
            var v = PlayerPrefs.GetInt("bind_key_" + i, (int)DefaultKeys[i]);
            return Enum.IsDefined(typeof(Key), v) ? (Key)v : DefaultKeys[i];
        }

        public static void SetKey(Act a, Key k)
        {
            // clear the same key off anyone else
            foreach (var o in All) if (o != a && KeyOf(o) == k) PlayerPrefs.SetInt("bind_key_" + (int)o, (int)Key.None);
            PlayerPrefs.SetInt("bind_key_" + (int)a, (int)k);
        }

        public static Pad PadOf(Act a)
        {
            var i = (int)a;
            var v = PlayerPrefs.GetInt("bind_pad_" + i, (int)DefaultPads[i]);
            return Enum.IsDefined(typeof(Pad), v) ? (Pad)v : DefaultPads[i];
        }

        public static void SetPad(Act a, Pad p)
        {
            foreach (var o in All)
            {
                if (o == a || KeyboardOnly(o)) continue;
                if (PadOf(o) == p) PlayerPrefs.SetInt("bind_pad_" + (int)o, (int)DefaultPads[(int)o]);
                if (Pad2Of(o) == p) PlayerPrefs.SetInt("bind_pad2_" + (int)o, DefaultPads2[(int)o] == p ? (int)Pad.South : (int)DefaultPads2[(int)o]);
            }
            PlayerPrefs.SetInt("bind_pad_" + (int)a, (int)p);
        }

        /// <summary>Optional second pad button (shoulders for shoot / charge by default).</summary>
        public static Pad Pad2Of(Act a)
        {
            var i = (int)a;
            var v = PlayerPrefs.GetInt("bind_pad2_" + i, (int)DefaultPads2[i]);
            return Enum.IsDefined(typeof(Pad), v) ? (Pad)v : DefaultPads2[i];
        }

        /// <summary>Take the controller button(s) off <paramref name="a"/> (a keyboard-only action: its key).</summary>
        public static void Clear(Act a)
        {
            if (KeyboardOnly(a)) { PlayerPrefs.SetInt("bind_key_" + (int)a, (int)Key.None); return; }
            PlayerPrefs.SetInt("bind_pad_" + (int)a, (int)Pad.None);
            PlayerPrefs.SetInt("bind_pad2_" + (int)a, (int)Pad.None);
        }

        public static void SetPad2(Act a, Pad p)
        {
            PlayerPrefs.SetInt("bind_pad2_" + (int)a, (int)p);
        }

        public static void ResetDefaults()
        {
            foreach (var a in All)
            {
                var i = (int)a;
                PlayerPrefs.DeleteKey("bind_key_" + i);
                PlayerPrefs.DeleteKey("bind_pad_" + i);
                PlayerPrefs.DeleteKey("bind_pad2_" + i);
            }
        }

        public static string KeyName(Key k)
        {
            if (k == Key.None) return "—";
            switch (k)
            {
                case Key.LeftArrow: return "←";
                case Key.RightArrow: return "→";
                case Key.UpArrow: return "↑";
                case Key.DownArrow: return "↓";
                case Key.LeftShift: return "Left Shift";
                case Key.RightShift: return "Right Shift";
                case Key.LeftCtrl: return "Left Ctrl";
                case Key.RightCtrl: return "Right Ctrl";
                case Key.Space: return "Space";
                default: return k.ToString();
            }
        }

        public static string PadName(Pad p)
        {
            switch (p)
            {
                case Pad.South: return "A / Cross";
                case Pad.East: return "B / Circle";
                case Pad.West: return "X / Square";
                case Pad.North: return "Y / Triangle";
                case Pad.LeftShoulder: return "LB / L1";
                case Pad.RightShoulder: return "RB / R1";
                case Pad.LeftTrigger: return "LT / L2";
                case Pad.RightTrigger: return "RT / R2";
                case Pad.Select: return "View / Select";
                case Pad.Start: return "Menu / Start";
                case Pad.LeftStick: return "Left stick click";
                case Pad.RightStick: return "Right stick click";
                case Pad.None: return "—";
                default: return p.ToString();
            }
        }

        public static string BindingText(Act a)
        {
            if (KeyboardOnly(a)) return KeyName(KeyOf(a));
            var p1 = PadOf(a);
            var p2 = Pad2Of(a);
            var pad = p1 == p2 || p2 == Pad.None ? PadName(p1) : p1 == Pad.None ? PadName(p2) : PadName(p1) + "  or  " + PadName(p2);
            return KeyName(KeyOf(a)) + "   ·   " + pad;
        }

        // ------------------------------------------------------------------ reading

        public static bool KeyHeld(Keyboard kb, Act a)
        {
            if (kb == null) return false;
            var k = KeyOf(a);
            if (k == Key.None) return false;
            var c = kb[k];
            return c != null && c.isPressed;
        }

        public static bool KeyPressed(Keyboard kb, Act a)
        {
            if (kb == null) return false;
            var k = KeyOf(a);
            if (k == Key.None) return false;
            var c = kb[k];
            return c != null && c.wasPressedThisFrame;
        }

        public static bool PadHeld(Gamepad g, Act a)
        {
            return PadDown(g, PadOf(a), false) || PadDown(g, Pad2Of(a), false);
        }

        public static bool PadPressed(Gamepad g, Act a)
        {
            return PadDown(g, PadOf(a), true) || PadDown(g, Pad2Of(a), true);
        }

        private static bool PadDown(Gamepad g, Pad p, bool pressedThisFrame)
        {
            if (g == null) return false;
            var c = Button(g, p);
            return c != null && (pressedThisFrame ? c.wasPressedThisFrame : c.isPressed);
        }

        public static ButtonControl Button(Gamepad g, Pad p)
        {
            if (g == null) return null;
            switch (p)
            {
                case Pad.South: return g.buttonSouth;
                case Pad.East: return g.buttonEast;
                case Pad.West: return g.buttonWest;
                case Pad.North: return g.buttonNorth;
                case Pad.LeftShoulder: return g.leftShoulder;
                case Pad.RightShoulder: return g.rightShoulder;
                case Pad.LeftTrigger: return g.leftTrigger;
                case Pad.RightTrigger: return g.rightTrigger;
                case Pad.Select: return g.selectButton;
                case Pad.Start: return g.startButton;
                case Pad.LeftStick: return g.leftStickButton;
                case Pad.RightStick: return g.rightStickButton;
                default: return null;
            }
        }

        /// <summary>Which pad button was pressed this frame on <paramref name="g"/>, or null.</summary>
        public static Pad? ReadPadPress(Gamepad g)
        {
            if (g == null) return null;
            foreach (Pad p in Enum.GetValues(typeof(Pad)))
            {
                var c = Button(g, p);
                if (c != null && c.wasPressedThisFrame) return p;
            }
            return null;
        }

        /// <summary>Which key was pressed this frame, or None.</summary>
        public static Key ReadKeyPress(Keyboard kb)
        {
            if (kb == null) return Key.None;
            foreach (Key k in Enum.GetValues(typeof(Key)))
            {
                if (k == Key.None) continue;
                try
                {
                    var c = kb[k];
                    if (c != null && c.wasPressedThisFrame) return k;
                }
                catch { /* some Key values aren't real keys */ }
            }
            return Key.None;
        }
    }
}
