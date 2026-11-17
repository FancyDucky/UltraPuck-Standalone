// Ultra Puck card surface: a rounded card shape with optional trading-card finishes that react to the card's tilt.
//   _Foil     holographic rainbow foil (bands slide across as the card tilts)
//   _Glare    a soft light streak and hotspot that move opposite the tilt, like light on a glossy card
//   _Sparkle  tiny glints that twinkle as the card moves
//   _Sweep    a bright band of light crossing the card (its position; animated from C#), _SweepStrength how bright
//   _Rim      a sheen that slides around the frame as the card tilts
// Additive blending (for glows and sparks): set _SrcBlend 5 (SrcAlpha) and _DstBlend 1 (One).
// _Size is the quad's width, height and corner radius in the same units, so corners stay round at any size.
Shader "UltraPuck/Card"
{
    Properties
    {
        _MainTex ("Picture", 2D) = "white" {}
        _Color ("Top colour", Color) = (1,1,1,1)
        _Color2 ("Bottom colour", Color) = (1,1,1,1)
        _Size ("Width, height, corner radius", Vector) = (2.5, 3.5, 0.16, 0)
        _Border ("Border width", Float) = 0
        _BorderColor ("Border colour", Color) = (1,1,1,1)
        _Foil ("Holo foil", Range(0,1)) = 0
        _Glare ("Glare", Range(0,1)) = 0.35
        _Sparkle ("Sparkle", Range(0,1)) = 0
        _Tilt ("Tilt (x, y)", Vector) = (0,0,0,0)
        _Alpha ("Alpha", Range(0,1)) = 1
        _Shadow ("Soft shadow mode (blur)", Float) = 0
        _Sweep ("Shine sweep position", Float) = -3
        _SweepStrength ("Shine sweep strength", Range(0,2)) = 0
        _Rim ("Frame sheen", Range(0,1)) = 0
        [HideInInspector] _SrcBlend ("", Float) = 5
        [HideInInspector] _DstBlend ("", Float) = 10
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            fixed4 _Color, _Color2, _BorderColor;
            float4 _Size, _Tilt;
            float _Border, _Foil, _Glare, _Sparkle, _Alpha, _Shadow, _Sweep, _SweepStrength, _Rim;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            float RoundedBox(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return length(max(q, 0)) + min(max(q.x, q.y), 0) - r;
            }

            float3 Hue(float h) { return saturate(abs(frac(h + float3(0, 2.0 / 3.0, 1.0 / 3.0)) * 6 - 3) - 1); }
            float Hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * _Size.xy;
                float d = RoundedBox(p, _Size.xy * 0.5, _Size.z);

                if (_Shadow > 0)
                {
                    // a soft blurred shape for drop shadows
                    float a = saturate(-d / _Shadow);
                    return fixed4(_Color.rgb, _Color.a * a * a * _Alpha);
                }

                float aa = max(fwidth(d), 1e-4);
                float alpha = saturate(0.5 - d / aa);
                fixed4 c = tex2D(_MainTex, TRANSFORM_TEX(i.uv, _MainTex)) * lerp(_Color2, _Color, i.uv.y);

                // border
                float inside = saturate(0.5 - (d + _Border) / aa);
                if (_Border > 0) c.rgb = lerp(_BorderColor.rgb, c.rgb, inside);

                float2 t = _Tilt.xy;
                float lum = dot(c.rgb, float3(0.299, 0.587, 0.114));

                // holo foil: smooth rainbow bands that slide with the tilt, strongest in the mid-tones
                float band = dot(i.uv, float2(0.85, 1.35)) * 1.8 + t.x * 1.1 + t.y * 0.7 + _Time.y * 0.02;
                float3 rainbow = Hue(band);
                float foilMask = 0.35 + 0.65 * smoothstep(0.05, 0.7, lum);
                c.rgb = lerp(c.rgb, c.rgb * 0.55 + rainbow * 0.75, saturate(_Foil * foilMask * 0.7));

                // shine sweep: a bright diagonal band passing across
                float along = dot(i.uv - 0.5, normalize(float2(1.0, 0.65))) + 0.5;
                float sweep = exp(-pow((along - _Sweep) * 9.0, 2.0));
                c.rgb += sweep * _SweepStrength * (0.35 + 0.25 * lum);

                // frame sheen: light catching the border on the side facing the light
                if (_Border > 0 && _Rim > 0)
                {
                    float2 dir = normalize(p + 1e-5);
                    float facing = saturate(dot(dir, normalize(-t + float2(0.35, 0.6))));
                    c.rgb += (1 - inside) * pow(facing, 3.0) * _Rim * 0.8;
                }

                // glare: a soft streak and a hotspot that move opposite the tilt
                float2 light = float2(0.5, 0.55) - t * float2(0.95, 0.95);
                float2 dl = i.uv - light;
                float streak = exp(-pow(dot(dl, normalize(float2(1.0, -0.55))) * 4.5, 2.0));
                float spot = exp(-dot(dl * float2(1.0, 0.75), dl * float2(1.0, 0.75)) * 7.0);
                c.rgb += (streak * 0.22 + spot * 0.28) * _Glare;

                // sparkles: tiny glints in a grid of cells, each twinkling at its own moment
                float2 grid = i.uv * float2(46.0, 64.0);
                float2 cell = floor(grid);
                float h = Hash(cell);
                float twinkle = pow(saturate(sin(h * 60.0 + t.x * 11.0 + t.y * 8.0 + _Time.y * 1.6)), 24.0);
                float2 f = frac(grid) - 0.5;
                float star = saturate(1.0 - length(f) * 3.2) + saturate(1.0 - abs(f.x) * 12.0) * saturate(1.0 - abs(f.y) * 2.5) * 0.5;
                c.rgb += step(0.86, h) * twinkle * star * _Sparkle * 1.4;

                c.a *= alpha * _Alpha;
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
