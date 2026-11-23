// Flat vertex-coloured shapes (like the game's Flat material), cut off along a line: whatever's on the far side of
// _Clip (world xy: dot(p, _Clip.xy) < _Clip.z) isn't drawn, when _Clip.w is on. Used for something half way through a
// portal: the part that's gone in is hidden, and a copy at the other portal shows the part that's come out.
Shader "UltraPuck/Clip"
{
    Properties
    {
        _Clip ("Cut line (normal xy, offset, on)", Vector) = (0, 0, 0, 0)
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

            float4 _Clip;

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

            fixed4 frag (v2f i) : SV_Target
            {
                if (_Clip.w > 0.5 && dot(i.world, _Clip.xy) < _Clip.z) discard;
                return i.color;
            }
            ENDCG
        }
    }
}
