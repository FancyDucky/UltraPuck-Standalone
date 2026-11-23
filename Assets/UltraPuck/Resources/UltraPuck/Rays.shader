// Sun rays coming down through the water from the top of the screen (the Underwater rink): soft shafts of light
// fanning out from a point above the surface, swaying slowly, brightest near the top and fading as they go down.
// Added on top (light), over the sand, walls and sea life, under the players.
Shader "UltraPuck/Rays"
{
    Properties
    {
        _Colour ("Light", Color) = (0.62, 0.92, 1.0, 1)
        _Origin ("Where the light comes from (world xy)", Vector) = (0, 30, 0, 0)
        _Reach ("How far down it reaches (world units)", Float) = 40
        _Strength ("Strength", Float) = 0.55
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        Cull Off
        Blend SrcAlpha One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Colour, _Origin;
            float _Reach, _Strength;

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
                float2 d = i.world - _Origin.xy;
                float down = -d.y;
                if (down <= 0.0) return fixed4(0, 0, 0, 0);
                float a = atan2(d.x, down);
                float t = _Time.y;
                // a handful of shafts of different widths, drifting a little
                float s = 0.0;
                s += pow(saturate(sin(a * 9.0 + t * 0.21) * 0.5 + 0.5), 6.0);
                s += 0.7 * pow(saturate(sin(a * 15.0 - t * 0.17 + 1.3) * 0.5 + 0.5), 8.0);
                s += 0.5 * pow(saturate(sin(a * 23.0 + t * 0.13 + 4.1) * 0.5 + 0.5), 10.0);
                // brightest towards the middle, fading down and out to the sides
                float fall = saturate(1.0 - down / _Reach);
                float spread = saturate(1.0 - abs(a) / 1.25);
                float alpha = s * fall * spread * _Strength;
                return fixed4(_Colour.rgb, alpha);
            }
            ENDCG
        }
    }
}
