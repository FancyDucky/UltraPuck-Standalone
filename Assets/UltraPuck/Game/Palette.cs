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
        public static readonly Color Puck = Hex(0xC4C8CF);     // light grey
        public static readonly Color PuckEdge = Hex(0xFFFFFF); // with a white outline
        public static readonly Color Dash = new Color(1f, 1f, 1f, 0.85f);
        public static readonly Color Charge = Hex(0xF0E442); // Okabe-Ito yellow

        /// <summary>
        /// The team colours. Red and blue come first and are the defaults; every team also shows its colour's name
        /// and players have their own symbols, so colour is never the only way to tell teams apart.
        /// </summary>
        public static readonly Color[] Colours =
        {
            Hex(0xE5484D), Hex(0x2F7FD8), Hex(0xE69F00), Hex(0x2FB463),
            Hex(0xF2D024), Hex(0xA25FE0), Hex(0xEC5FA6), Hex(0x2BB8AE),
        };
        public static readonly string[] ColourNames = { "Red", "Blue", "Orange", "Green", "Yellow", "Purple", "Pink", "Teal" };
        /// <summary>How many of <see cref="Colours"/> players can pick for now: just red and blue, so teams are clear.</summary>
        public const int Choosable = 2;

        private static readonly int[] _teamColour = { 0, 1 };
        /// <summary>Goes up whenever the teams' colours change (things drawn in team colours are built again).</summary>
        public static int Version { get; private set; }

        /// <summary>The colours of team 0 and team 1 (indexes into <see cref="Colours"/>).</summary>
        public static void SetTeams(int colour0, int colour1)
        {
            colour0 &= 7; colour1 &= 7;
            if (colour1 == colour0) colour1 = colour0 == 1 ? 0 : 1;
            if (_teamColour[0] == colour0 && _teamColour[1] == colour1) return;
            _teamColour[0] = colour0; _teamColour[1] = colour1;
            Version++;
        }

        public static int TeamColour(int t) { return _teamColour[t & 1]; }
        public static Color Team(int t) { return Colours[_teamColour[t & 1]]; }
        /// <summary>Light tint for things drawn in team colour on the dark rink (the facing arrow, trails).</summary>
        public static Color TeamTint(int t) { return Tint(_teamColour[t & 1]); }
        /// <summary>The goal mouths, a dark shade of the defending team's colour.</summary>
        public static Color Goal(int t) { return Darker(Colours[_teamColour[t & 1]], 0.38f); }
        public static string TeamName(int t) { return ColourNames[_teamColour[t & 1]]; }
        public static Color Tint(int colour) { return Color.Lerp(Colours[colour & 7], Color.white, 0.45f); }
        public static Color Clear(Color c) { c.a = 0f; return c; }

        public static Color Darker(Color c, float k = 0.6f) { return new Color(c.r * k, c.g * k, c.b * k, c.a); }

        private static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
        }
    }
}
