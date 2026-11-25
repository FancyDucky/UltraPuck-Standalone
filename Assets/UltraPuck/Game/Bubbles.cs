using System.Collections.Generic;
using UltraPuck.Core;
using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// Bubbles rising through <see cref="Area"/>, mostly in streams at <see cref="Streams"/> (x positions), a few
    /// anywhere. Some in front of the floor (faint), most behind it.
    /// </summary>
    public sealed class Bubbles : MonoBehaviour
    {
        public Sprite Bubble;
        [Range(0, 120)] public int Count = 34;
        [Tooltip("Where they rise (world units): from the bottom of this to the top.")]
        public Rect Area = new Rect(-34f, -19f, 68f, 41f);
        [Tooltip("The streams' x positions; about 70% of the bubbles rise in one of these.")]
        public float[] Streams = { -30f, 30f };
        public Vector2 Size = new Vector2(0.14f, 0.44f), Speed = new Vector2(0.8f, 2.4f);
        public int FrontOrder = 3, BackOrder = -9;
        [Range(0f, 1f)] public float FrontAlpha = 0.18f, BackAlpha = 0.4f;

        private sealed class One { public SpriteRenderer R; public float Speed, Wobble, Phase; public Vector2 Pos; }
        private readonly List<One> _all = new List<One>();
        private System.Random _rng;

        private void Start()
        {
            if (Bubble == null) return;
            _rng = new System.Random(4242);
            for (var k = 0; k < Count; k++)
            {
                var go = new GameObject("Bubble");
                go.transform.SetParent(transform, false);
                var r = go.AddComponent<SpriteRenderer>();
                r.sprite = Bubble;
                r.sortingOrder = k % 2 == 0 ? FrontOrder : BackOrder;
                var b = new One { R = r };
                Respawn(b, true);
                _all.Add(b);
            }
        }

        private float Next() { return (float)_rng.NextDouble(); }

        private void Respawn(One b, bool anywhere)
        {
            var size = Mathf.Lerp(Size.x, Size.y, Next());
            b.Speed = Mathf.Lerp(Speed.x, Speed.y, Next());
            b.Wobble = 0.15f + Next() * 0.25f;
            b.Phase = Next() * 6.3f;
            float x;
            if (Streams != null && Streams.Length > 0 && Next() < 0.7f) x = Streams[_rng.Next(Streams.Length)] + (Next() - 0.5f) * 1.2f;
            else x = Area.xMin + Next() * Area.width;
            b.Pos = new Vector2(x, anywhere ? Area.yMin + Next() * Area.height : Area.yMin);
            var s = size / (Bubble.rect.height / Bubble.pixelsPerUnit);
            b.R.transform.localScale = new Vector3(s, s, 1f);
            b.R.color = new Color(1f, 1f, 1f, b.R.sortingOrder > 0 ? FrontAlpha : BackAlpha);
        }

        private void Update()
        {
            var t = Time.time;
            foreach (var b in _all)
            {
                b.Pos.y += b.Speed * Time.deltaTime;
                if (b.Pos.y > Area.yMax) Respawn(b, false);
                b.R.transform.localPosition = new Vector3(b.Pos.x + Mathf.Sin(t * 2f + b.Phase) * b.Wobble, b.Pos.y, 0f);
            }
        }
    }
}
