using System.Collections.Generic;
using UltraPuck.Core;
using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// The "underwater" look for a rink (a map whose theme is underwater), and the themed looks in general (any map
    /// file naming a "dressing", like the Forest). The dressing round the rink (water,
    /// sun rays, surface, sea life, bubbles; trees, leaves...) is a prefab made in Unity: Resources/UltraPuck/Themes/&lt;dressing&gt;.prefab,
    /// named by the map file's "dressing". Its root has an <see cref="UnderwaterTheme"/> with the floor and wall
    /// settings. Edit it in Unity like any prefab (Ultra Puck menu: Open Underwater Hockey). Only looks: the rink
    /// plays exactly as its base map.
    /// </summary>
    internal static class Underwater
    {
        public const string Theme = "underwater";
        // (for the little rink pictures on the rink screen)
        public static readonly Color Wall = new Color(0.43f, 0.6f, 0.62f), Sand = new Color(0.73f, 0.645f, 0.415f);
        public static readonly Color DeepWater = new Color(0.06f, 0.2f, 0.25f), ShallowWater = new Color(0.11f, 0.52f, 0.62f);
        /// <summary>The Forest look (for its little rink picture): grass, wooden boards, the forest floor round it.</summary>
        public const string ForestTheme = "forest";
        public static readonly Color Grass = new Color(0.38f, 0.62f, 0.25f), Wood = new Color(0.5f, 0.35f, 0.22f);
        public static readonly Color ForestDark = new Color(0.12f, 0.24f, 0.13f), ForestLight = new Color(0.2f, 0.36f, 0.18f);

        /// <summary>Put <paramref name="map"/>'s dressing under <paramref name="root"/> (the rink); its settings, or null if there's none.</summary>
        public static UnderwaterTheme Dress(GameObject root, MapDef map)
        {
            var name = string.IsNullOrEmpty(map.Dressing) ? "Underwater Hockey" : map.Dressing;
            var prefab = Resources.Load<GameObject>("UltraPuck/Themes/" + name);
            if (prefab == null) { Debug.LogWarning("Ultra Puck: no dressing prefab \"" + name + "\" in Resources/UltraPuck/Themes"); return null; }
            var go = Object.Instantiate(prefab, root.transform, false);
            go.name = name;
            return go.GetComponent<UnderwaterTheme>();
        }
    }
}
