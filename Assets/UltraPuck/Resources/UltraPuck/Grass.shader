// The Forest rink's grass (and, with the stripes off and darker colours, the forest floor round it): mown stripes
// across the rink, slow lighter and darker patches, a fine blade grain, a few tiny flowers, and soft gusts of wind
// that brighten the grass as they sweep across. World space, so it stays put while the camera moves.
Shader "UltraPuck/Grass"
{
    Properties
    {
        _Light ("Light stripe", Color) = (0.42, 0.66, 0.28, 1)
        _Dark ("Dark stripe", Color) = (0.34, 0.57, 0.22, 1)
        _Stripes ("Stripe width (world units, 0 = no stripes)", Float) = 2.6
        _Patches ("Patches (0..1)", Float) = 0.12
        _Grain ("Blade grain (0..1)", Float) = 0.08
        _FlowerA ("Flower colour", Color) = (1, 1, 0.94, 1)
        _FlowerB ("Second flower colour", Color) = (1, 0.86, 0.3, 1)
        _FlowerChance ("Share of spots with a flower", Float) = 0.05
        _FlowerCell ("Flower spacing (world units)", Float) = 1.1
        _Wind ("Wind gust brightness", Float) = 0.05
        _WindSpeed ("Wind speed", Float) = 0.35
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        // mark the grass's pixels: the wall shadows (Shadow.shader) fall only there
        Stencil
        {
            Ref 1
            Comp Always
            Pass Replace
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Caustics.cginc"

            float4 _Light, _Dark, _FlowerA, _FlowerB;
            float _Stripes, _Patches, _Grain, _FlowerChance, _FlowerCell, _Wind, _WindSpeed;

            // a tiny five-petal flower (or none) near p: its cover (0..1) and which colour
            float Flower(float2 p, out float which)
            {
                which = 0.0;
                float cover = 0.0;
                float2 c = floor(p / _FlowerCell);
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 cell = c + float2(x, y);
                    if (Hash(cell + 3.7) > _FlowerChance) continue;
                    float2 at = (cell + 0.2 + 0.6 * Hash2(cell + 1.3)) * _FlowerCell;
                    float2 d = p - at;
                    float a = atan2(d.y, d.x) + Hash(cell) * 6.28;
                    float r = 0.07 * (0.75 + 0.25 * cos(a * 5.0));
                    float f = 1.0 - smoothstep(r * 0.75, r, length(d));
                    if (f > cover) { cover = f; which = Hash(cell + 8.1); }
                }
                return cover;
            }

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float2 world : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xy;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 p = i.world;
                // mown stripes, their edges a little soft and wavy
                float s = 0.5;
                if (_Stripes > 0.0)
                {
                    float x = p.x / _Stripes + (Noise(p * 0.35) - 0.5) * 0.08;
                    s = smoothstep(0.42, 0.58, abs(frac(x) - 0.5) * 2.0);
                }
                float3 g = lerp(_Dark.rgb, _Light.rgb, s);
                // patches, and blades: grain stretched up and down like grass
                float patch = Noise(p * 0.16) * 0.65 + Noise(p * 0.45 + 5.0) * 0.35;
                g *= 1.0 + (patch - 0.5) * 2.0 * _Patches;
                float blade = Hash(floor(float2(p.x * 34.0, p.y * 9.0))) - 0.5;
                g *= 1.0 + blade * _Grain;
                // a gust of wind sweeping across: a soft bright band moving diagonally
                float gust = Noise(float2(p.x * 0.09 - _Time.y * _WindSpeed, p.y * 0.12 + _Time.y * _WindSpeed * 0.4));
                g *= 1.0 + smoothstep(0.55, 0.85, gust) * _Wind;
                // tiny flowers
                float which;
                float f = Flower(p, which);
                g = lerp(g, which < 0.5 ? _FlowerA.rgb : _FlowerB.rgb, f);
                return fixed4(g, 1.0);
            }
            ENDCG
        }
    }
}
