// The Black Hole goal explosion: a dark middle with the picture round it bent inwards (things near it look drawn in
// and stretched round it), a thin bright rim, and a light colour split along that rim. Drawn on a quad with uv 0..1;
// _Open goes 0..1 as it opens and back as it shuts.
Shader "UltraPuck/Lens"
{
    Properties
    {
        _Open ("Open", Float) = 1
        _Strength ("Strength", Float) = 0.035
        _Tint ("Tint", Color) = (0.6, 0.4, 1, 1)
        _Aberration ("Colour split", Float) = 0.35
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        ZWrite Off
        ZTest Always
        Cull Off

        GrabPass { "_LensGrab" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _LensGrab;
            float _Open, _Strength, _Aberration;
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
                const float core = 0.34;
                float2 dir = r > 1e-4 ? i.uv / r : float2(0, 0);
                // bending: strongest just outside the middle, nothing at the quad's edge (so it blends in)
                float edge = saturate((1 - r) / 0.45);
                float bend = _Strength * _Open * core / max(r, core) * edge * edge;
                float2 uv = i.grab.xy / i.grab.w + dir * bend;
                // the colour split: only near the rim
                float nearRim = exp(-pow((r - core * 1.12) / 0.12, 2));
                float2 split = dir * _Strength * _Aberration * nearRim * _Open;
                fixed4 col = tex2D(_LensGrab, uv);
                col.r = tex2D(_LensGrab, uv + split).r;
                col.b = tex2D(_LensGrab, uv - split).b;
                // a thin bright rim (photon ring) in the tint, red and blue a hair apart like the split
                float ring = exp(-pow((r - core * 1.08) / 0.035, 2));
                float ringR = exp(-pow((r - core * 1.08 - 0.02) / 0.035, 2));
                float ringB = exp(-pow((r - core * 1.08 + 0.02) / 0.035, 2));
                float3 rim = lerp(_Tint.rgb, float3(1, 1, 1), 0.45);
                col.rgb += rim * ring * 0.55;
                col.r += ringR * 0.18 * _Aberration;
                col.b += ringB * 0.22 * _Aberration;
                // the dark middle, soft-edged
                float hole = 1 - smoothstep(core * 0.86, core * 1.02, r);
                col.rgb = lerp(col.rgb, float3(0.01, 0, 0.02), hole);
                col.a = 1;
                return col;
            }
            ENDCG
        }
    }
}
