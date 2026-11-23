// Sunlight through the water (caustics) on the Underwater rink: a network of bright lines that drifts and wobbles,
// added over the floor and everything on it (players too; the puck is drawn above it). World space, so the pattern
// stays put on the rink while the camera moves.
Shader "UltraPuck/Caustics"
{
    Properties
    {
        _Light ("Light", Color) = (1.0, 0.96, 0.72, 1)
        _Strength ("Light strength", Float) = 0.2
        _Scale ("Light cells per unit", Float) = 0.2
        _Speed ("Speed", Float) = 0.7
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Off
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Caustics.cginc"

            float4 _Light;
            float _Strength, _Scale, _Speed;

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
                float light = CausticLight(i.world, _Time.y * _Speed, _Scale);
                return fixed4(_Light.rgb * light * _Strength, 0.0);
            }
            ENDCG
        }
    }
}
