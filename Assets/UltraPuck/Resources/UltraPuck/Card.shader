// Ultra Puck card surface: a rounded card shape with optional trading-card finishes that react to the card's tilt.
//   _Foil     holographic rainbow foil (bands slide across as the card tilts) — S tier
//   _Pearl    soft mother-of-pearl sheen (pastel white/cream interference) — A tier
//   _Metal    "foil" finish: brushed metal with a bright sheen that sweeps across as the card tilts (per card)
//   _Poly     "polychrome" finish: every colour on the card slowly shifts round the rainbow, in drifting waves (per card)
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
        _Pearl ("Pearlescent", Range(0,1)) = 0
        _Metal ("Metal foil finish", Range(0,1)) = 0
        _Poly ("Polychrome finish", Range(0,1)) = 0
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
            float _Border, _Foil, _Pearl, _Metal, _Poly, _Glare, _Sparkle, _Alpha, _Shadow, _Sweep, _SweepStrength, _Rim;

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
            // turns a colour round the colour wheel (keeps its brightness)
            float3 HueTurn(float3 col, float a)
            {
                const float3 k = float3(0.57735, 0.57735, 0.57735);
                float ca = cos(a);
                return col * ca + cross(k, col) * sin(a) + k * dot(k, col) * (1.0 - ca);
            }
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

                // holo foil: smooth rainbow bands that slide with the tilt, strongest in the mid-tones (S tier)
                float band = dot(i.uv, float2(0.85, 1.35)) * 1.8 + t.x * 1.1 + t.y * 0.7 + _Time.y * 0.02;
                float3 rainbow = Hue(band);
                float foilMask = 0.35 + 0.65 * smoothstep(0.05, 0.7, lum);
                c.rgb = lerp(c.rgb, c.rgb * 0.55 + rainbow * 0.75, saturate(_Foil * foilMask * 0.7));

                // pearlescent: soft mother-of-pearl — milky pastels and a shifting sheen (A tier; not rainbow holo)
                if (_Pearl > 0.001)
                {
                    float ang = i.uv.x * 2.8 + i.uv.y * 2.0 + t.x * 2.2 + t.y * 1.6 + _Time.y * 0.12;
                    float3 pA = Hue(ang * 0.18 + 0.05);
                    float3 pB = Hue(ang * 0.18 + 0.42);
                    float3 pearl = lerp(pA, pB, 0.5 + 0.5 * sin(ang * 2.8 + t.x * 2.4));
                    pearl = lerp(float3(0.92, 0.94, 0.97), pearl, 0.4); // milky, soft pastel
                    float pearlMask = 0.25 + 0.75 * smoothstep(0.05, 0.8, lum);
                    float sheen = pow(saturate(0.5 + 0.5 * sin(dot(i.uv, float2(3.8, 5.8)) + t.x * 3.4 + t.y * 2.6)), 2.0);
                    float broad = pow(saturate(1.0 - length((i.uv - 0.5 - t * 0.15) * float2(1.1, 0.85)) * 1.4), 1.6);
                    float mixAmt = saturate(_Pearl * pearlMask * (0.35 + 0.4 * sheen + 0.45 * broad));
                    c.rgb = lerp(c.rgb, c.rgb * 0.72 + pearl * 0.65 + sheen * 0.28 + broad * 0.12, mixAmt);
                }

                // metal foil: brushed metal (fine horizontal grain) whose bright sheen slides across with the tilt
                if (_Metal > 0.001)
                {
                    float grain = 0.86 + 0.14 * Hash(float2(floor(i.uv.y * 260.0), 3.7)) + 0.05 * sin(i.uv.x * 90.0 + Hash(float2(floor(i.uv.y * 90.0), 1.3)) * 6.0);
                    float across = dot(i.uv - 0.5, normalize(float2(0.45, 1.0)));
                    float at = t.y * 0.85 + t.x * 0.45 + sin(_Time.y * 0.35) * 0.06;
                    float sheen = exp(-pow((across - at) * 4.2, 2.0)) + 0.45 * exp(-pow((across - at + 0.55) * 6.0, 2.0));
                    float3 steel = lerp(float3(0.62, 0.66, 0.74), float3(1.0, 0.96, 0.86), saturate(sheen));
                    float3 metal = (0.25 + lum * 0.85) * steel * grain * (0.55 + 0.75 * sheen);
                    float metalMask = 0.45 + 0.55 * smoothstep(0.02, 0.6, lum);
                    c.rgb = lerp(c.rgb, c.rgb * 0.5 + metal * 0.75, saturate(_Metal * metalMask * 0.85));
                    c.rgb += sheen * sheen * _Metal * 0.18 * grain;
                }

                // polychrome: the whole card's colours turn round the rainbow in slow waves; greys pick up a tint
                if (_Poly > 0.001)
                {
                    float2 q = i.uv - 0.5 - t * 0.25;
                    float wave = length(q * float2(1.0, 0.8)) * 2.6 + sin(q.x * 5.0 + _Time.y * 0.4) * 0.35 + q.y * 1.2;
                    float turn = (wave + t.x * 0.9 - t.y * 0.6 + _Time.y * 0.08) * 6.2831853;
                    float3 turned = HueTurn(c.rgb, turn);
                    float3 tint = Hue(wave * 0.5 + t.x * 0.3 + _Time.y * 0.013) * (0.18 + lum * 0.9);
                    float3 poly = lerp(turned, saturate(turned * 0.6 + tint * 0.75), 0.55);
                    c.rgb = lerp(c.rgb, poly, saturate(_Poly * 0.9));
                }

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
