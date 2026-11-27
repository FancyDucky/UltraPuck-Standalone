// Sunlight through leaves on the Forest rink: soft shadows of the canopy overhead, drifting as the branches sway,
// with warmer sunlit gaps between. Drawn over the grass and everyone on it (the puck is drawn above it, so it stays
// easy to see). Multiplies what's under it: 0.5 leaves a pixel as it is, less darkens, more brightens.
Shader "UltraPuck/Dapple"
{
    Properties
    {
        _Shade ("Shade colour", Color) = (0.62, 0.7, 0.78, 1)
        _Sun ("Sun colour", Color) = (1.0, 0.95, 0.8, 1)
        _Strength ("Shade strength (0..1)", Float) = 0.2
        _SunStrength ("Sun strength (0..1)", Float) = 0.12
        _Scale ("Leaf clumps per unit", Float) = 0.2
        _Cover ("How much is in shade (0..1)", Float) = 0.38
        _Sway ("Sway (world units)", Float) = 0.35
        _Speed ("Sway speed", Float) = 0.5
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Off
        Blend DstColor SrcColor

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Caustics.cginc"

            float4 _Shade, _Sun;
            float _Strength, _SunStrength, _Scale, _Cover, _Sway, _Speed;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float2 world : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.world = mul(unity_ObjectToWorld, v.vertex).xy;
                return o;
            }

            float Leaves(float2 q)
            {
                return Noise(q) * 0.5 + Noise(q * 2.1 + 3.3) * 0.3 + Noise(q * 4.7 + 9.1) * 0.2;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = _Time.y * _Speed;
                // the canopy sways: the big clumps slowly, the small leaves a little faster
                float2 sway = float2(sin(t) + 0.4 * sin(t * 2.3 + 1.0), cos(t * 0.8) * 0.6) * _Sway;
                float2 q = (i.world + sway) * _Scale;
                float n = Leaves(q + float2(sin(t * 1.7 + q.y) , cos(t * 1.3 + q.x)) * 0.04);
                float shade = smoothstep(1.0 - _Cover - 0.06, 1.0 - _Cover + 0.06, n);
                float sun = 1.0 - smoothstep(0.25, 0.45, n);
                float3 m = lerp(float3(1, 1, 1), _Shade.rgb, shade * _Strength);
                m *= lerp(float3(1, 1, 1), _Sun.rgb * 1.25, sun * _SunStrength);
                return fixed4(m * 0.5, 1.0);
            }
            ENDCG
        }
    }
}
