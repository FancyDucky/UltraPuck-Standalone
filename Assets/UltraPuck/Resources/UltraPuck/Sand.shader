// The Underwater rink's floor: warm sand with slow lighter and darker patches and a fine grain. The moving light on
// it (caustics) is drawn over the top by Caustics.shader, so it plays over the players too.
Shader "UltraPuck/Sand"
{
    Properties
    {
        _Sand ("Sand", Color) = (0.80, 0.71, 0.46, 1)
        _Tint ("Water tint", Color) = (0.16, 0.42, 0.6, 1)
        _TintAmount ("Water tint amount (the screen tint does this now)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        // mark the sand's pixels: the wall shadows (Shadow.shader) only fall there
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

            float4 _Sand, _Tint;
            float _TintAmount;

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; };
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
                float patches = Noise(p * 0.18) * 0.6 + Noise(p * 0.5 + 7.0) * 0.4;
                float grain = Hash(floor(p * 28.0)) - 0.5;
                float3 sand = _Sand.rgb * (0.9 + 0.16 * patches + 0.05 * grain);
                return fixed4(lerp(sand * 0.95, _Tint.rgb, _TintAmount), 1.0);
            }
            ENDCG
        }
    }
}
