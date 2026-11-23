using System.Collections.Generic;
using UltraPuck.Core;
using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// On the root of an underwater dressing prefab: the rink's own look under the sea. The floor's material (sand with
    /// moving light), the walls' colour and their shadow on the sand, and the floor lines' colours.
    /// </summary>
    public sealed class UnderwaterTheme : MonoBehaviour
    {
        [Tooltip("The rink floor: sand with moving light on it (shader UltraPuck/Sand).")]
        public Material Floor;
        [Tooltip("The moving light through the water (shader UltraPuck/Caustics), over the floor and the players; the puck stays above it.")]
        public Material Caustics;
        [Tooltip("The walls' shadow on the sand (shader UltraPuck/Shadow: only on the sand, once a pixel).")]
        public Material WallShadow;
        [Tooltip("How far the walls' shadow falls (world units).")]
        public Vector2 ShadowOffset = new Vector2(0.1f, -0.32f);
        [Tooltip("The shadow's darkness and colour.")]
        public Color ShadowColour = new Color(0.02f, 0.08f, 0.1f, 0.3f);
        public Color Walls = new Color(0.43f, 0.6f, 0.62f);
        [Tooltip("The centre line and circle.")]
        public Color Lines = new Color(1f, 1f, 1f, 0.55f);
        [Tooltip("How see-through the red and blue lines are.")]
        [Range(0f, 1f)] public float TeamLines = 0.8f;
        [Tooltip("The water's colour over the rink and players (not the puck or the cards). Alpha is how strong.")]
        public Color ScreenTint = new Color(0.16f, 0.42f, 0.6f, 0.18f);
        [Tooltip("The puck's middle here (it's drawn above the water's tint and light, so this is the colour you see).")]
        public Color Puck = Color.white;
        [Tooltip("The puck's rim here.")]
        public Color PuckEdge = new Color(0.05f, 0.14f, 0.22f);
    }

}
