using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// Full-screen wipe used going into a goal replay and coming back out. Horizontal or diagonal grey blinds; the
    /// match picks one style at random for both ends of the replay.
    /// </summary>
    internal sealed class ReplayTransition
    {
        public enum Style { Horizontal = 0, Diagonal = 1 }

        private enum Phase { Idle, Cover, Hold, Reveal }

        private Phase _phase;
        private Style _style;
        private Color[] _colours = GreyStripeColours();
        private float _t;
        private float _holdLeft;
        private const int Stripes = 4;
        private const float CoverSecs = 0.55f;
        private const float RevealSecs = 0.55f;
        private const float HoldSecs = 0.08f;

        /// <summary>True while a wipe is on screen (cover, brief hold, or reveal).</summary>
        public bool Active { get { return _phase != Phase.Idle; } }

        /// <summary>True in the brief fully-covered beat (safe to swap what's under the wipe).</summary>
        public bool Covered { get { return _phase == Phase.Hold; } }

        /// <summary>True during the opening wipe (gameplay → covered).</summary>
        public bool Covering { get { return _phase == Phase.Cover; } }

        private float PhaseSecs { get { return _phase == Phase.Reveal ? RevealSecs : CoverSecs; } }

        /// <summary>Pick a style from a seed (goal tick + scorer) so every PC in an online match matches.</summary>
        public static Style PickStyle(int seed)
        {
            return (seed & 1) == 0 ? Style.Horizontal : Style.Diagonal;
        }

        /// <summary>Grey / dark-grey stripes for the wipe (no team colours for now).</summary>
        public static Color[] GreyStripeColours()
        {
            return new[] { new Color(0.48f, 0.48f, 0.48f, 1f), new Color(0.22f, 0.22f, 0.22f, 1f) };
        }

        public void Begin(Style style, Color[] colours)
        {
            _style = style;
            if (colours != null && colours.Length > 0) _colours = colours;
            else _colours = GreyStripeColours();
            _phase = Phase.Cover;
            _t = 0f;
            _holdLeft = HoldSecs;
        }

        public void Stop()
        {
            _phase = Phase.Idle;
            _t = 0f;
        }

        public void Tick(float dt)
        {
            if (_phase == Phase.Idle) return;
            if (_phase == Phase.Hold)
            {
                _holdLeft -= dt;
                if (_holdLeft <= 0f) { _phase = Phase.Reveal; _t = 0f; }
                return;
            }
            _t += dt;
            if (_t >= PhaseSecs)
            {
                if (_phase == Phase.Cover) { _phase = Phase.Hold; _holdLeft = HoldSecs; _t = 1f; }
                else { _phase = Phase.Idle; _t = 0f; }
            }
        }

        /// <summary>0 = open, 1 = fully covered. Reveal reverses the blinds back open.</summary>
        private float CoverAmount()
        {
            if (_phase == Phase.Idle) return 0f;
            if (_phase == Phase.Hold) return 1f;
            var u = Ui.EaseOut(Mathf.Clamp01(_t / PhaseSecs));
            return _phase == Phase.Cover ? u : 1f - u;
        }

        public void Draw()
        {
            if (_phase == Phase.Idle) return;
            if (Event.current.type != EventType.Repaint) return;
            var cover = CoverAmount();
            if (cover <= 0.001f) return;
            DrawBlinds(Ui.W, Ui.H, cover, diagonal: _style == Style.Diagonal);
        }

        private Color Stripe(int i) { return _colours[i % _colours.Length]; }

        private void DrawBlinds(float w, float h, float cover, bool diagonal)
        {
            var m = GUI.matrix;
            if (diagonal)
            {
                // rotate around the screen centre; pads so corners stay covered
                var pad = Mathf.Max(w, h) * 0.35f;
                GUI.matrix = m * Matrix4x4.TRS(new Vector3(w * 0.5f, h * 0.5f, 0f), Quaternion.Euler(0f, 0f, -28f), Vector3.one)
                    * Matrix4x4.TRS(new Vector3(-w * 0.5f - pad, -h * 0.5f - pad, 0f), Quaternion.identity, Vector3.one);
                w += pad * 2f;
                h += pad * 2f;
            }
            var stripeH = h / Stripes;
            for (var i = 0; i < Stripes; i++)
            {
                // stagger: top stripes lead slightly
                var local = Mathf.Clamp01((cover - i * 0.06f) / 0.78f);
                local = Ui.EaseOut(local);
                if (local <= 0.001f) continue;
                var fromLeft = (i & 1) == 0;
                var bw = w * local;
                var x = fromLeft ? 0f : w - bw;
                Ui.Box(new Rect(x, i * stripeH, bw, stripeH + 1.5f), Stripe(i), 0f);
            }
            GUI.matrix = m;
        }
    }
}
