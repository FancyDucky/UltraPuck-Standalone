using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>Bobs up and down a little (the water's surface).</summary>
    public sealed class SurfaceBob : MonoBehaviour
    {
        public float Height = 0.12f, Speed = 1.1f;
        private Vector3 _home;
        private float _phase;

        private void Start() { _home = transform.localPosition; _phase = _home.x * 0.08f; }
        private void Update() { transform.localPosition = _home + new Vector3(0f, Mathf.Sin(Time.time * Speed + _phase) * Height, 0f); }
    }
}
