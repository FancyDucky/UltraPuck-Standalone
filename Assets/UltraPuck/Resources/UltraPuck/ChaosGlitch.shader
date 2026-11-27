// Ultra Puck chaos cards: a glitch drawn over the card, on a quad bigger than the card so it spills past the border.
// Always a strong chromatic aberration (red / blue split, ghosts past the border). Then, like a broken digital picture: horizontal slices of the card (frame and picture) jump sideways with the red and
// blue split apart, red / cyan ghost edges stick out of the frame, blocks of digital noise pop up, scan lines and
// static. Not all the time: a twitch of 0.5 to 2 seconds now and then (Twitch), calm in between. It never draws over _TextRect (the name, flavour line, rows and
// note stay clean), so the text is always easy to read.
//   _MainTex   the card's picture; _ArtRect its centre (x, y) and half size (z, w) in card units
//   _Card      the card's width, height, corner radius and border width
//   _BorderColor / _BodyColor  the frame's and the card stock's colours (for the torn slices)
//   _TextRect  where the text is (min x, min y, max x, max y): nothing drawn there
//   _Size      this quad's width and height; _Amt strength (0 = off); _Seed so cards don't glitch in step
Shader "UltraPuck/ChaosGlitch"
{
    Properties
    {
        _MainTex ("Picture", 2D) = "gray" {}
        _ArtRect ("Picture centre, half size", Vector) = (0, 0.6, 1.01, 1.01)
        _Card ("Card width, height, radius, border", Vector) = (2.5, 3.5, 0.17, 0.075)
        _BorderColor ("Frame colour", Color) = (1, 0.24, 0.66, 1)
        _BodyColor ("Card colour", Color) = (0.12, 0.12, 0.13, 1)
        _TextRect ("Text area (min x, min y, max x, max y)", Vector) = (-1.15, -1.68, 1.15, -0.43)
        _Size ("Quad width, height", Vector) = (3.6, 3.9, 0, 0)
        _Amt ("Strength", Range(0, 1)) = 1
        _Seed ("Seed", Float) = 0
        _Aberration ("Steady colour split (card units)", Float) = 0.05
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _ArtRect, _Card, _TextRect, _Size;
            fixed4 _BorderColor, _BodyColor;
            float _Amt, _Seed, _Aberration;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

            // chaos cards' twitch: calm most of the time, then now and then (every ~4.5 s, some skipped) a twitch of
            // 0.5 to 2 seconds; 1 during a twitch, else 0. The same in ChaosGlitch.shader and Card.shader, so the
            // picture tears in step with the glitch over the card.
            float Twitch(float t, float seed)
            {
                const float L = 4.5;
                float tt = t + seed * 3.1;
                float c = floor(tt / L);
                float at = tt - c * L;
                float dur = 0.5 + 1.5 * Hash(float2(c, 11.3 + seed));
                float start = 0.8 + Hash(float2(c, 5.7 + seed)) * (L - 0.8 - dur); // at least 0.8 s calm between
                return step(start, at) * step(at, start + dur) * step(0.3, Hash(float2(c, 2.9 + seed)));
            }

            float RoundedBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, 0)) + min(max(q.x, q.y), 0) - r;
            }

            // the card at q (card units, centre 0): its colour, and how much of it is there (0 off the card)
            float4 CardAt(float2 q, float aa)
            {
                float d = RoundedBox(q, _Card.xy * 0.5, _Card.z);
                float cov = saturate(0.5 - d / aa);
                float inner = saturate(0.5 - (d + _Card.w) / aa);
                float3 col = lerp(_BorderColor.rgb, _BodyColor.rgb, inner);
                float2 a = (q - _ArtRect.xy) / (_ArtRect.zw * 2.0) + 0.5;
                float inArt = step(0.0, a.x) * step(a.x, 1.0) * step(0.0, a.y) * step(a.y, 1.0);
                col = lerp(col, tex2D(_MainTex, saturate(a)).rgb, inArt * inner);
                return float4(col, cov);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (_Amt <= 0.001) return 0;
                float2 p = (i.uv - 0.5) * _Size.xy; // card units, the card's centre at 0
                float aa = max(fwidth(p.x), 1e-4);
                float2 hb = _Card.xy * 0.5;

                // ALWAYS: chromatic aberration. Red pulled one way and blue the other: red / blue ghosts of the frame
                // past the border, coloured fringes on every edge, the picture split into its colours. Steady (it
                // doesn't flicker), so the card is calm between twitches.
                float ab = _Aberration;
                float4 here = CardAt(p, aa);
                float4 cr = CardAt(p + float2(ab, 0), aa), cb = CardAt(p - float2(ab, 0), aa);
                float3 col = float3(cr.r * cr.a, here.g * here.a, cb.b * cb.a);
                float dif = max(max(abs(cr.r * cr.a - here.r * here.a), abs(cb.b * cb.a - here.b * here.a)), max(abs(cr.a - here.a), abs(cb.a - here.a)));
                float2 a = (p - _ArtRect.xy) / _ArtRect.zw;
                float inArt = step(abs(a.x), 1.0) * step(abs(a.y), 1.0) * here.a;
                float alpha = max(inArt, saturate(dif * 3.0));

                // NOW AND THEN: a twitch of 0.5 to 2 s (15 glitch frames a second, some of them bursts, a few held still)
                float slice = floor(_Time.y * 15.0) + _Seed * 101.0;
                if (Twitch(_Time.y, _Seed) > 0.5 && Hash(float2(slice, 7.7)) >= 0.22)
                {
                    float burst = step(0.78, Hash(float2(slice, 1.7)));
                    float k = saturate(0.35 + burst * 0.55) * _Amt;

                    // horizontal bands: some of them torn sideways this frame, their colours pulled far apart
                    float bands = lerp(9.0, 30.0, Hash(float2(slice, 8.8)));
                    float band = floor((p.y / _Card.y + 0.5) * bands);
                    float torn = step(0.62 - 0.3 * k, Hash(float2(band, slice + 3.0)));
                    float shift = (Hash(float2(band, slice)) - 0.5) * 2.0 * (0.08 + 0.42 * k) * torn;
                    float split = ab * 1.5 + 0.1 * k;
                    float3 gcol = 0;
                    float galpha = 0;
                    if (torn > 0.5)
                    {
                        float4 r = CardAt(p - float2(shift - split, 0), aa);
                        float4 g = CardAt(p - float2(shift, 0), aa);
                        float4 b = CardAt(p - float2(shift + split, 0), aa);
                        gcol = float3(r.r * r.a, g.g * g.a, b.b * b.a);
                        galpha = max(max(r.a, g.a), b.a) * (0.75 + 0.25 * k);
                        gcol += float3(1.0, 0.15, 0.45) * saturate(r.a - g.a) * 0.6 + float3(0.1, 0.9, 1.0) * saturate(b.a - g.a) * 0.6;
                    }
                    else
                    {
                        // wider red and cyan ghosts of the frame on either side
                        float gr = CardAt(p + float2(split * 1.6, 0), aa).a, gb = CardAt(p - float2(split * 1.6, 0), aa).a;
                        float outR = saturate(gr - here.a), outB = saturate(gb - here.a);
                        gcol = float3(1.0, 0.1, 0.4) * outR + float3(0.1, 0.95, 1.0) * outB;
                        galpha = max(outR, outB) * (0.55 + 0.4 * k);
                    }

                    // blocks of digital noise, near the card (in it and just round it), more in a burst
                    float2 cellSize = float2(lerp(0.14, 0.38, Hash(float2(slice, 5.1))), lerp(0.05, 0.14, Hash(float2(slice, 6.3))));
                    float2 cell = floor((p + float2(Hash(float2(slice, 2.2)), 0)) / cellSize);
                    float near = saturate(1.0 - max(0.0, RoundedBox(p, hb, _Card.z)) / 0.35);
                    float on = step(0.975 - 0.07 * k, Hash(cell + slice * 0.13)) * near;
                    float pick = Hash(cell * 1.7 + slice);
                    float3 bc = pick < 0.3 ? float3(1.0, 0.1, 0.55) : pick < 0.55 ? float3(0.1, 0.95, 1.0) : pick < 0.7 ? float3(0.6, 1.0, 0.2)
                              : pick < 0.85 ? float3(0.95, 0.95, 1.0) : float3(0.02, 0.02, 0.03);
                    float fine = Hash(floor(p * float2(90.0, 120.0)) + slice);
                    bc = lerp(bc, bc * (0.5 + fine), step(0.5, Hash(cell + 4.4)));
                    gcol = lerp(gcol, bc, on);
                    galpha = max(galpha, on * 0.85);

                    // a flicker now and then: the whole card blinks brighter
                    float flick = step(0.94, Hash(float2(slice, 9.9))) * here.a * 0.18 * _Amt;
                    gcol = lerp(gcol, float3(1.0, 1.0, 1.0), flick / max(galpha + flick, 1e-4));
                    galpha = max(galpha, flick);

                    // over the steady aberration, then scan lines and static over everything drawn
                    col = lerp(col, gcol, galpha);
                    alpha = max(alpha, galpha);
                    col *= 0.82 + 0.18 * (0.5 + 0.5 * sin(p.y * 220.0 - _Time.y * 6.0));
                    col += (Hash(floor(p * float2(160.0, 220.0)) + frac(_Time.y * 7.3) * 100.0) - 0.5) * 0.14;
                }

                // never over the text
                float2 lo = p - _TextRect.xy, hi = _TextRect.zw - p;
                float inText = saturate(min(min(lo.x, lo.y), min(hi.x, hi.y)) / 0.03 + 1.0);
                alpha *= 1.0 - inText;

                return fixed4(saturate(col), saturate(alpha) * _Amt);
            }
            ENDCG
        }
    }
    Fallback Off
}
