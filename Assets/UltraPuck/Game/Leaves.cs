using System.Collections.Generic;
using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// Leaves drifting down through <see cref="Area"/> (the Forest rink), turning and swinging side to side as they
    /// fall, blown along by a light breeze. A few over the rink (faint, under the puck), the rest behind it.
    /// </summary>
    public sealed class Leaves : MonoBehaviour
    {
        public Sprite[] Pictures;
        [Range(0, 80)] public int Count = 22;
        [Tooltip("Where they fall (world units): from the top of this to the bottom.")]
        public Rect Area = new Rect(-34f, -19f, 68f, 41f);
        public Vector2 Size = new Vector2(0.22f, 0.38f), Speed = new Vector2(0.35f, 0.8f);
        [Tooltip("The breeze: how fast they drift sideways (world units a second).")]
        public float Breeze = 0.45f;
        public int FrontOrder = 27, BackOrder = -9;
        [Range(0f, 1f)] public float FrontAlpha = 0.75f, BackAlpha = 1f;
        [Range(0f, 1f)] public float FrontShare = 0.3f;

        private sealed class One { public SpriteRenderer R; public float Speed, Swing, Phase, Spin; public Vector2 Pos; }
        private readonly List<One> _all = new List<One>();
        private System.Random _rng;

        private void Start()
        {
            if (Pictures == null || Pictures.Length == 0) return;
            _rng = new System.Random(777);
            for (var k = 0; k < Count; k++)
            {
                var go = new GameObject("Leaf");
                go.transform.SetParent(transform, false);
                var r = go.AddComponent<SpriteRenderer>();
                r.sortingOrder = Next() < FrontShare ? FrontOrder : BackOrder;
                var b = new One { R = r };
                Respawn(b, true);
                _all.Add(b);
            }
        }

        private float Next() { return (float)_rng.NextDouble(); }

        private void Respawn(One b, bool anywhere)
        {
            var pic = Pictures[_rng.Next(Pictures.Length)];
            b.R.sprite = pic;
            var size = Mathf.Lerp(Size.x, Size.y, Next());
            b.Speed = Mathf.Lerp(Speed.x, Speed.y, Next());
            b.Swing = 0.3f + Next() * 0.5f;
            b.Phase = Next() * 6.3f;
            b.Spin = (Next() - 0.5f) * 160f;
            b.Pos = new Vector2(Area.xMin - 2f + Next() * (Area.width + 2f), anywhere ? Area.yMin + Next() * Area.height : Area.yMax);
            var s = size / (pic.rect.height / pic.pixelsPerUnit);
            b.R.transform.localScale = new Vector3(s, s, 1f);
            b.R.color = new Color(1f, 1f, 1f, b.R.sortingOrder > 0 ? FrontAlpha : BackAlpha);
        }

        private void Update()
        {
            var t = Time.time;
            foreach (var b in _all)
            {
                b.Pos.y -= b.Speed * Time.deltaTime;
                b.Pos.x += Breeze * Time.deltaTime;
                if (b.Pos.y < Area.yMin || b.Pos.x > Area.xMax + 2f) Respawn(b, false);
                var swing = Mathf.Sin(t * 1.3f + b.Phase);
                b.R.transform.localPosition = new Vector3(b.Pos.x + swing * b.Swing, b.Pos.y + Mathf.Abs(swing) * 0.08f, 0f);
                b.R.transform.localRotation = Quaternion.Euler(0f, 0f, b.Spin * t * 0.2f + swing * 35f);
            }
        }
    }
}
