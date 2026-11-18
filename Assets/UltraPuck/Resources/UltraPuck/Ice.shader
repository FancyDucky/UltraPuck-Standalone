// Ice Trail: the mesh's light blue, with a sheen across it, a soft wide band and a thin bright line, like light
// on ice. The sheen is placed by the camera (_UltraPuckCam, set by the game every frame) more than by the ice, so
// as the view moves the light slides up and down the streaks; it also drifts a little on its own.
Shader "UltraPuck/Ice"
{
    Properties
    {
        _Spacing ("Gap between sheens (units)", Float) = 9
        _Follow ("How much the sheen moves with the camera", Float) = 1.6
        _Drift ("Drift (units a second)", Float) = 0.35
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
            float _Spacing, _Follow, _Drift;

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

            // how far (in units) from the nearest band middle, bands every _Spacing along the light's direction
            float Dist(float along, float shift)
            {
                float p = frac((along - shift) / _Spacing);
                return abs(p - 0.5) * _Spacing;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                const float2 light = float2(0.8, 0.6);
                float along = dot(i.world, light) - dot(_UltraPuckCam.xy, light) * _Follow + _Time.y * _Drift;
                float wide = Dist(along, 0);
                float thin = Dist(along, 1.15);
                float sheen = exp(-wide * wide / 0.7) * 0.38 + exp(-thin * thin / 0.012) * 0.6;
                fixed4 c = i.color;
                c.rgb = lerp(c.rgb, float3(1, 1, 1), saturate(sheen));
                return c;
            }
            ENDCG
        }
    }
}
