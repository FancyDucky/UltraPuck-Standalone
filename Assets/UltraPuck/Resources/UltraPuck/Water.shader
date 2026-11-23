// The deep water round the Underwater rink: lighter towards the surface (_Top) and darker below (_Bottom), with
// faint drifting light lines like the ones on the sand, so the water never sits still.
Shader "UltraPuck/Water"
{
    Properties
    {
        _Shallow ("Near the surface", Color) = (0.11, 0.52, 0.62, 1)
        _Deep ("Deep down", Color) = (0.06, 0.2, 0.25, 1)
        _Top ("Surface (world y)", Float) = 20
        _Bottom ("Deep (world y)", Float) = -20
        _Strength ("Light strength", Float) = 0.12
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

            float4 _Shallow, _Deep;
            float _Top, _Bottom, _Strength;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float2 world : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xy;
                return o;
            }

            float2 Hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            float Edge(float2 p, float t)
            {
                float2 i = floor(p), f = frac(p);
                float d1 = 8.0, d2 = 8.0;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 g = float2(x, y);
                    float2 o = Hash2(i + g);
                    o = 0.5 + 0.42 * sin(t + 6.2831 * o);
                    float d = length(g + o - f);
                    if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) d2 = d;
                }
                return d2 - d1;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 p = i.world;
                float t = _Time.y * 0.5;
                float h = saturate((p.y - _Bottom) / max(0.01, _Top - _Bottom));
                float3 col = lerp(_Deep.rgb, _Shallow.rgb, h * h);
                float light = exp(-Edge(p * 0.3 + float2(t * 0.04, -t * 0.03), t) * 10.0);
                col += float3(0.55, 0.85, 1.0) * light * _Strength * (0.35 + 0.65 * h);
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
