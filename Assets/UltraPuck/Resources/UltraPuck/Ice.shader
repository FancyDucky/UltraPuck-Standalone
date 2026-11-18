// Ice Trail: the mesh's light blue, showing the arena's hidden frozen surface. The surface (thin frozen lines and
// tiny sparkle points) is fixed to the rink, so it's only seen where ice covers it and trails laid over the same
// spot line up. A reflection slides over it as the camera moves (_UltraPuckCam, set by the game every frame): a soft
// glossy band across the ice, and wherever it passes the lines and sparkles flare up. Surface still, light moving:
// that's what makes it read as shiny.
Shader "UltraPuck/Ice"
{
    Properties
    {
        _Follow ("How fast the reflection moves with the camera", Float) = 1.8
        _Drift ("Drift on its own (units a second)", Float) = 0.25
        _Spacing ("Gap between reflections (units)", Float) = 11
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _UltraPuckCam;
            float _Follow, _Drift, _Spacing;

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float2 world : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.world = mul(unity_ObjectToWorld, v.vertex).xy;
                return o;
            }

            float Hash(float n) { return frac(sin(n * 12.9898) * 43758.5453); }
            float Hash2(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            // a family of thin straight lines across the rink, each its own strength (some missing), unevenly spaced
            float Lines(float2 p, float2 dir, float spacing, float width, float seed)
            {
                float along = dot(p, dir) / spacing;
                float band = floor(along);
                float k = Hash(band + seed);
                float centre = 0.5 + (Hash(band + seed + 17.0) - 0.5) * 0.6;
                float d = abs(frac(along) - centre) * spacing;
                float stripe = 1.0 - smoothstep(width * 0.5, width, d);
                return stripe * step(0.35, k) * (0.5 + 0.5 * k);
            }

            // tiny bright points scattered over the rink (one in some cells of a fine grid)
            float Sparkles(float2 p)
            {
                float2 cell = floor(p * 2.2);
                float2 f = frac(p * 2.2);
                float h = Hash2(cell);
                float2 at = float2(Hash2(cell + 3.1), Hash2(cell + 7.7)) * 0.7 + 0.15;
                float d = length(f - at);
                return step(0.82, h) * (1.0 - smoothstep(0.03, 0.09, d));
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 p = i.world;
                // the frozen surface, fixed to the rink
                float lines = Lines(p, normalize(float2(0.80, 0.60)), 1.9, 0.07, 3.0)
                            + Lines(p, normalize(float2(0.25, 0.97)), 3.3, 0.05, 41.0) * 0.6;
                float glow = Lines(p, normalize(float2(0.80, 0.60)), 1.9, 0.35, 3.0);
                float sparkle = Sparkles(p);

                // the reflection: bands across the rink placed by the camera more than by the ice, so they sweep over
                // the surface as the view moves (and drift a little by themselves)
                const float2 light = float2(0.6, -0.8);
                float along = dot(p, light) - dot(_UltraPuckCam.xy, light) * _Follow + _Time.y * _Drift;
                float d = (frac(along / _Spacing) - 0.5) * _Spacing;            // units from the middle of the nearest band
                float band = exp(-d * d / 4.0);                                   // the wide soft reflection
                float core = exp(-d * d / 0.35);                                  // its bright middle

                fixed4 c = i.color;
                float3 white = float3(1, 1, 1);
                c.rgb = lerp(c.rgb, white, band * 0.16 + core * 0.10);           // gloss over the ice itself
                c.rgb = lerp(c.rgb, white, saturate(lines * (0.22 + band * 0.9) + glow * band * 0.25));
                c.rgb = lerp(c.rgb, white, saturate(sparkle * (0.25 + core * 1.2 + band * 0.4)));
                return c;
            }
            ENDCG
        }
    }
}
