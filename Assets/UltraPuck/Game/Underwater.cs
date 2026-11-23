using System.Collections.Generic;
using UltraPuck.Core;
using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// The "underwater" look for a rink (a map whose theme is underwater). The dressing round the rink (water,
    /// sun rays, surface, sea life, bubbles) is a prefab made in Unity: Resources/UltraPuck/Themes/&lt;dressing&gt;.prefab,
    /// named by the map file's "dressing". Its root has an <see cref="UnderwaterTheme"/> with the floor and wall
    /// settings. Edit it in Unity like any prefab (Ultra Puck menu: Open Underwater Hockey). Only looks: the rink
    /// plays exactly as its base map.
    /// </summary>
    internal static class Underwater
    {
        public const string Theme = "underwater";
        // (for the little rink pictures on the rink screen)
        public static readonly Color Wall = new Color(0.43f, 0.6f, 0.62f), Sand = new Color(0.8f, 0.71f, 0.46f);
        public static readonly Color DeepWater = new Color(0.06f, 0.2f, 0.25f), ShallowWater = new Color(0.11f, 0.52f, 0.62f);

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
