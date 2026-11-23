using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>Sways back and forth about its own pivot (seaweed in the current), starting from how it's rotated.</summary>
    public sealed class Sway : MonoBehaviour
    {
        [Tooltip("How far it leans each way (degrees).")] public float Degrees = 4f;
        [Tooltip("How fast (about once every 2π / Speed seconds).")] public float Speed = 1f;
        [Tooltip("Where in the sway it starts (0-6.28). Left at 0, each picks its own.")] public float Phase;
        private Quaternion _home;

        private void Start()
        {
            _home = transform.localRotation;
            if (Phase == 0f) Phase = Random.value * 6.28f;
        }

        private void Update() { transform.localRotation = _home * Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * Speed + Phase) * Degrees); }
    }
}
