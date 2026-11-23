// A soft shadow on the Underwater rink's sand (walls lit from above): drawn only where the sand is (the sand marks
// its pixels in the stencil, see Sand.shader) and only once per pixel, so where pieces of the shadow overlap (the
// joints round a curve) it doesn't get darker, and it never spills off the rink onto the water.
Shader "UltraPuck/Shadow"
{
    Properties { }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        Stencil
        {
            Ref 1
            Comp Equal
            Pass IncrSat
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target { return i.color; }
            ENDCG
        }
    }
}
