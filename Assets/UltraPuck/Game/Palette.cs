using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// Colours and team symbols. Team colours come from the Okabe-Ito set (readable with the common kinds of colour
    /// blindness), and every team also has its own shape, so colour is never the only way to tell teams apart.
    /// </summary>
    internal static class Palette
    {
        // "Dark Clean": flat shapes on a flat dark grey rink (neutral greys, no blue tint, no gradients, no glows).
        public static readonly Color Background = Hex(0x111111);
        public static readonly Color Floor = Hex(0x1F1F1F);
        public static readonly Color FloorLines = Hex(0x343434);
        public static readonly Color FloorLinesSide = Hex(0x2C2C2C);
        public static readonly Color Wall = Hex(0x4A4A4A);
        public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.35f);
        public static readonly Color PlayerRim = Hex(0xF4F6FB);
        public static readonly Color Puck = Hex(0xFFFFFF);
        public static readonly Color PuckEdge = Hex(0x1B1E27);
        public static readonly Color Dash = new Color(1f, 1f, 1f, 0.85f);
        public static readonly Color Charge = Hex(0xF0E442); // Okabe-Ito yellow

        private static readonly Color[] TeamColors = { Hex(0xE69F00), Hex(0x0072B2) }; // orange, blue
        /// <summary>Light tints for things drawn in team colour on the dark rink (the facing arrow).</summary>
        private static readonly Color[] TeamLight = { Hex(0xFFD27A), Hex(0x7CC8F2) };
        /// <summary>The goal mouths, a dark shade of the defending team's colour.</summary>
        private static readonly Color[] GoalColors = { Hex(0x5C4A26), Hex(0x1F4664) };
        private static readonly string[] TeamNames = { "Orange", "Blue" };
        private static readonly string[] SymbolNames = { "triangle", "square" };

        public static Color Team(int t) { return TeamColors[t & 1]; }
        public static Color TeamTint(int t) { return TeamLight[t & 1]; }
        public static Color Goal(int t) { return GoalColors[t & 1]; }
        public static Color Clear(Color c) { c.a = 0f; return c; }
        public static string TeamName(int t) { return TeamNames[t & 1]; }
        public static string SymbolName(int t) { return SymbolNames[t & 1]; }

        public static Color Darker(Color c, float k = 0.6f) { return new Color(c.r * k, c.g * k, c.b * k, c.a); }

        private static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
        }
    }
}
