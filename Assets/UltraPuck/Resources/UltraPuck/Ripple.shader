// A water ripple: bends whatever is behind it (the rink, the players) with rings of waves travelling outwards
// from the middle, like a drop landing in water. Drawn on a quad with uv 0..1; _Age goes 0..1 over its life.
Shader "UltraPuck/Ripple"
{
    Properties
    {
        _Age ("Age", Float) = 0
        _Strength ("Strength", Float) = 0.02
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Aberration ("Colour split", Float) = 0.7
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        ZTest Always
        Cull Off

        // the screen so far, grabbed once a frame for every ripple (they don't bend each other)
        GrabPass { "_RippleGrab" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _RippleGrab;
            float _Age, _Strength, _Aberration;
            float4 _Tint;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 grab : TEXCOORD1; };

            v2f vert (appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy * 2 - 1;
                o.grab = ComputeGrabScreenPos(o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float r = length(i.uv);
                if (r > 1) discard;
                // the wave front runs out to the edge, easing off; behind it a train of waves that dies away
                float front = 1 - (1 - _Age) * (1 - _Age);
                float behind = front - r;
                float env = saturate(behind / 0.04) * saturate(1 - behind / 0.42);
                env *= (1 - _Age) * (1 - _Age) * saturate((1 - r) / 0.15);
                float phase = behind * 46;
                float wave = sin(phase) * env;
                float slope = cos(phase) * env;
                float2 dir = r > 1e-4 ? i.uv / r : float2(0, 0);

                // chromatic aberration: red bends a bit further than green, blue a bit less, strongest on the waves
                float2 uv = i.grab.xy / i.grab.w + dir * wave * _Strength;
                float2 split = dir * abs(wave) * _Strength * _Aberration;
                fixed4 col = tex2D(_RippleGrab, uv);
                col.r = tex2D(_RippleGrab, uv + split).r;
                col.b = tex2D(_RippleGrab, uv - split).b;
                // light catches the crests, a hint of the card's colour in it
                col.rgb += lerp(float3(1, 1, 1), _Tint.rgb, 0.45) * max(0, slope) * 0.22;
                col.rgb -= max(0, -slope) * 0.06;
                col.a = 1;
                return col;
            }
            ENDCG
        }
    }
}
