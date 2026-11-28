// Ultra Puck card surface: a rounded card shape with optional trading-card finishes that react to the card's tilt.
//   _Foil     holographic rainbow foil (bands slide across as the card tilts) — S tier
//   _Pearl    soft mother-of-pearl sheen (pastel white/cream interference) — A tier
//   _Metal    "foil" finish: brushed metal with a bright sheen that sweeps across as the card tilts (per card)
//   _Poly     "polychrome" finish: every colour on the card slowly shifts round the rainbow, in drifting waves (per card)
//   _Special  a card file's other finishes, by number (strength _SpecialAmt): 1 starfield (stars in three depths drift
//             against the tilt, faint nebula), 2 gilded (gold leaf on the dark parts and frame), 3 prism (crisp rainbow
//             shards that flash at different tilts), 4 frosted (frost creeping in from the edges, cold glints),
//             5 neon (the frame becomes a glowing tube in its colour that hums and flickers; body only),
//             6 aurora (green / violet curtains of light), 7 molten (glowing cracks that pulse), 8 glitch (bands tear sideways, outline and
//             frame included; colour split, coloured blocks, scan lines), 9 glass (see-through, bright edges, a light
//             streak; the picture shows faintly), 10 borealis (soft coloured glows drifting, turning and swelling
//             behind the card's things, on deep blue), 11 rainbow (a rainbow gradient drifting back and forth, with a
//             fine diagonal holographic grating over it). 10 and 11 replace the card's own fill (body only; the
//             picture, text and frame stay), 12 infinity (an infinity mirror: a dark mirror with a ring of lights in
//             _FxColor repeating smaller and dimmer into the distance, the tunnel bending away as the card tilts; on
//             the picture its own fainter tunnel over it); _FxSpeed, _FxBright, _FxSat, _FxFoil (11's grating) and _FxPhase tune them.
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
        _Special ("Special finish (1-9)", Float) = 0
        _SpecialAmt ("Special finish strength", Range(0,1)) = 0
        _FxSpeed ("Animated finish speed", Float) = 1
        _FxBright ("Animated finish brightness", Float) = 1
        _FxSat ("Animated finish saturation", Float) = 1
        _FxFoil ("Animated finish foil (grating)", Range(0,2)) = 1
        _FxPhase ("Animated finish phase (seconds)", Float) = 0
        _FxColor ("Animated finish light colour (infinity)", Color) = (1,1,1,1)
        _GlitchSeed ("Glitch seed (chaos cards: in step with their glitch)", Float) = 0
        _GlitchTwitch ("Glitch only in twitches (chaos cards)", Float) = 0
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
            fixed4 _Color, _Color2, _BorderColor, _FxColor;
            float4 _Size, _Tilt;
            float _Border, _Foil, _Pearl, _Metal, _Poly, _Special, _SpecialAmt, _Glare, _Sparkle, _Alpha, _Shadow, _Sweep, _SweepStrength, _Rim;
            float _FxSpeed, _FxBright, _FxSat, _FxFoil, _FxPhase, _GlitchSeed, _GlitchTwitch;

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

            // chaos cards' twitch: calm most of the time, then now and then (every ~4.5 s, some skipped) a twitch of
            // 0.5 to 2 seconds; 1 during a twitch, else 0. The same in ChaosGlitch.shader and Card.shader, so the
            // picture tears in step with the glitch over the card.
            float Twitch(float t, float seed)
            {
                const float L = 4.5;
                float tt = t + seed * 3.1;
                float c = floor(tt / L);
                float at = tt - c * L;
                float dur = 0.5 + 1.5 * Hash(float2(c, 11.3 + seed));
                float start = 0.8 + Hash(float2(c, 5.7 + seed)) * (L - 0.8 - dur); // at least 0.8 s calm between
                return step(start, at) * step(at, start + dur) * step(0.3, Hash(float2(c, 2.9 + seed)));
            }

            float Noise(float2 q)
            {
                float2 a = floor(q), f = frac(q);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(a), Hash(a + float2(1, 0)), f.x), lerp(Hash(a + float2(0, 1)), Hash(a + 1.0), f.x), f.y);
            }

            // tiny four-point glints in a grid, each twinkling at its own moment
            float Glints(float2 uv, float2 cells, float th, float speed, float2 t)
            {
                float2 g = uv * cells;
                float h = Hash(floor(g));
                float tw = pow(saturate(sin(h * 60.0 + t.x * 11.0 + t.y * 8.0 + _Time.y * speed)), 24.0);
                float2 f = frac(g) - 0.5;
                float star = saturate(1.0 - length(f) * 3.2) + saturate(1.0 - abs(f.x) * 12.0) * saturate(1.0 - abs(f.y) * 2.5) * 0.5;
                return step(th, h) * tw * star;
            }

            // one layer of stars; deeper layers (bigger depth) drift further against the tilt
            float Stars(float2 uv, float2 t, float sc, float depth, float th)
            {
                float2 g = (uv + t * depth) * float2(sc, sc * 1.4);
                float2 cl = floor(g);
                float r = Hash(cl);
                float2 o = float2(Hash(cl + 1.3), Hash(cl + 7.1)) * 0.6 + 0.2;
                float2 f = frac(g) - o;
                float tw = 0.55 + 0.45 * sin(_Time.y * (1.0 + r * 3.0) + r * 40.0);
                float dot1 = exp(-dot(f, f) * (1350.0 / sc));
                float rays = (exp(-abs(f.x) * 90.0) * exp(-abs(f.y) * 9.0) + exp(-abs(f.y) * 90.0) * exp(-abs(f.x) * 9.0)) * step(0.97, r);
                return step(th, r) * tw * (dot1 + rays * 0.25);
            }

            float3 Hex(float r, float g, float b) { return float3(r, g, b) / 255.0; }
            // CSS ease-in-out between the two ends of an "alternate" animation that takes `half` seconds each way
            float PingPong(float half)
            {
                float k = (_Time.y * _FxSpeed + _FxPhase) / half;
                float x = 1.0 - abs(frac(k * 0.5) * 2.0 - 1.0);
                return x * x * (3.0 - 2.0 * x);
            }
            float3 Grade(float3 col)
            {
                float l = dot(col, float3(0.299, 0.587, 0.114));
                return max(0.0, lerp(float3(l, l, l), col, _FxSat)) * _FxBright;
            }
            // radial-gradient(ellipse at c, col, transparent r): the ellipse's size is the farthest corner's (CSS)
            float3 Glow(float3 under, float2 u, float2 c, float3 col, float r)
            {
                float2 e = max(c, 1.0 - c) * 1.41421;
                float dn = length((u - c) / e) / r;
                float w = saturate(1.0 - dn);
                w = w * w * (3.0 - 2.0 * w); // softened edge (the CSS blur)
                return lerp(under, col, w);
            }

            // BOREALIS: four soft glows on deep blue, on a layer 45% bigger than the card on every side that turns
            // (-25 to 55 degrees), swells (0.9 to 1.15) and drifts, 4 s each way
            float3 Borealis(float2 uv)
            {
                float a = PingPong(4.0);
                float2 card = _Size.xy;
                float2 pc = (uv - 0.5) * card;
                pc.y = -pc.y; // CSS: y down
                float ang = radians(lerp(-25.0, 55.0, a));
                float sc = lerp(0.9, 1.15, a);
                float2 layer = card * 1.9;
                float2 tr = lerp(float2(-0.06, 0.05), float2(0.07, -0.05), a) * layer;
                // undo rotate(ang) scale(sc) translate(tr): rotate back, unscale, untranslate
                float cs = cos(ang), sn = sin(ang);
                float2 l = float2(cs * pc.x + sn * pc.y, -sn * pc.x + cs * pc.y) / sc - tr;
                float2 u = l / layer + 0.5; // 0..1 across the layer, y down
                float3 col = Hex(7, 21, 44);
                col = Glow(col, u, float2(0.70, 0.70), Hex(255, 122, 201), 0.30);
                col = Glow(col, u, float2(0.40, 0.65), Hex(19, 158, 255), 0.35);
                col = Glow(col, u, float2(0.65, 0.40), Hex(142, 92, 255), 0.35);
                col = Glow(col, u, float2(0.30, 0.40), Hex(83, 255, 216), 0.30);
                return Grade(col);
            }

            // RAINBOW: a 110-degree rainbow gradient on a background four times the card's size, drifting from its
            // corner to (100%, 85%), 5 s each way; then a fine 120-degree grating overlaid like holographic foil
            float3 Rainbow(float2 uv)
            {
                float a = PingPong(5.0);
                float2 css = float2(uv.x, 1.0 - uv.y); // 0..1, y down
                float2 b = (css + 3.0 * a * float2(1.0, 0.85)) / 4.0; // where on the big background this spot is
                float2 box = _Size.xy * 4.0;
                float2 pnt = (b - 0.5) * box;
                float th = radians(110.0);
                float2 dir = float2(sin(th), -cos(th));
                float len = abs(box.x * sin(th)) + abs(box.y * cos(th));
                float g = saturate(dot(pnt, dir) / len + 0.5) * 6.0;
                float3 c0 = Hex(255, 82, 124), c1 = Hex(251, 233, 154), c2 = Hex(79, 239, 181), c3 = Hex(75, 200, 255),
                       c4 = Hex(164, 119, 255), c5 = Hex(255, 82, 124), c6 = Hex(255, 232, 155);
                float f = frac(g);
                float3 col = g < 1.0 ? lerp(c0, c1, f) : g < 2.0 ? lerp(c1, c2, f) : g < 3.0 ? lerp(c2, c3, f)
                           : g < 4.0 ? lerp(c3, c4, f) : g < 5.0 ? lerp(c4, c5, f) : lerp(c5, c6, saturate(g - 5.0));
                // the grating: 8 "px" stripes (a card is about 250 px wide), dark with a bright line, overlay-blended
                float px = 250.0 / _Size.x;
                float2 cp = css * _Size.xy * px;
                float th2 = radians(120.0);
                float s = fmod(dot(cp, float2(sin(th2), -cos(th2))) + 800.0, 8.0);
                float4 st = s < 2.0 ? float4(0, 0, 0, 0.133)
                          : s < 3.0 ? lerp(float4(0, 0, 0, 0.133), float4(1, 1, 1, 0.533), s - 2.0)
                          : s < 4.0 ? lerp(float4(1, 1, 1, 0.533), float4(0, 0, 0, 0.133), s - 3.0)
                          : float4(0, 0, 0, 0.133);
                float3 over = lerp(2.0 * col * st.rgb, 1.0 - 2.0 * (1.0 - col) * (1.0 - st.rgb), step(0.5, col));
                col = lerp(col, over, saturate(st.a * _FxFoil));
                return Grade(col);
            }

            // INFINITY MIRROR: a ring of lights just inside the edge, reflected over and over between two mirrors: each
            // reflection a fifth smaller and dimmer, and the deeper ones slide further as the card tilts (parallax),
            // so the tunnel bends away from you. The lights are beads that a slow chase runs round.
            float3 Infinity(float2 uv, float2 t, float3 led, float quiet)
            {
                float2 q = (uv - 0.5) * _Size.xy;
                float2 hb = _Size.xy * 0.5 - 0.09;
                float rad = max(_Size.z - 0.04, 0.02);
                float3 acc = 0;
                float s = 1.0, fade = 1.0;
                for (int k = 0; k < 10; k++)
                {
                    float2 c = -t * (1.0 - s) * _Size.xy * 0.45;
                    float2 r = (q - c) / s;
                    float dist = abs(RoundedBox(r, hb, rad)) * s; // (in card units: deeper rings are thinner)
                    float w = 0.018 * s + 0.002;
                    float core = exp(-dist * dist / (w * w));
                    float halo = exp(-dist / (0.07 * s + 0.01)) * 0.35;
                    // beads along the ring, and a chase of brighter ones running round (each reflection a beat later)
                    float ang = atan2(r.y * hb.x / hb.y, r.x) / 6.2831853;
                    float bead = 0.45 + 0.55 * smoothstep(0.32, 0.0, abs(frac(ang * 56.0) - 0.5));
                    float chase = 0.65 + 0.35 * sin(ang * 6.2831853 * 3.0 - _Time.y * 2.2 * _FxSpeed + k * 0.55);
                    acc += led * (core * bead * 1.25 + halo) * chase * fade * (k == 0 ? 1.0 : 1.0 - quiet);
                    s *= 0.8;
                    fade *= 0.74;
                }
                // the mirror's own dim glow at the far end of the tunnel
                float2 end = q + t * _Size.xy * 0.45;
                acc += led * 0.06 * exp(-dot(end, end) * 1.2);
                return acc * _FxBright;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // glitch: bands of the card jump sideways now and then (the outline and border tear with them)
                float2 uv = i.uv;
                float gGate = _GlitchTwitch > 0.5 ? Twitch(_Time.y, _GlitchSeed) : 1.0; // chaos cards: only in a twitch
                float gAmt = round(_Special) == 8 ? _SpecialAmt * gGate : 0.0;
                float gSlice = 0.0, gBurst = 0.0, gSplit = 0.0;
                if (gAmt > 0.001)
                {
                    gSlice = floor(_Time.y * 15.0) + _GlitchSeed * 101.0;
                    gBurst = step(0.82, Hash(float2(gSlice, 1.7)));
                    float small = step(0.6, Hash(float2(gSlice, 4.2))) * 0.25;
                    float band = floor(uv.y * lerp(10.0, 34.0, Hash(float2(gSlice, 8.8))));
                    float tear = (Hash(float2(band, gSlice)) - 0.5) * step(0.55, Hash(float2(band, gSlice + 3.0)));
                    uv.x += tear * (0.09 * gBurst + 0.02 * small) * gAmt;
                    gSplit = (0.008 + 0.02 * gBurst) * gAmt;
                }
                float glass = 1.0;
                float2 p = (uv - 0.5) * _Size.xy;
                float d = RoundedBox(p, _Size.xy * 0.5, _Size.z);

                if (_Shadow > 0)
                {
                    // a soft blurred shape for drop shadows
                    float a = saturate(-d / _Shadow);
                    return fixed4(_Color.rgb, _Color.a * a * a * _Alpha);
                }

                float aa = max(fwidth(d), 1e-4);
                float alpha = saturate(0.5 - d / aa);
                fixed4 c = tex2D(_MainTex, TRANSFORM_TEX(uv, _MainTex)) * lerp(_Color2, _Color, uv.y);

                // border
                float inside = saturate(0.5 - (d + _Border) / aa);
                if (_Border > 0) c.rgb = lerp(_BorderColor.rgb, c.rgb, inside);

                float2 t = _Tilt.xy;
                float lum = dot(c.rgb, float3(0.299, 0.587, 0.114));

                // holo foil: smooth rainbow bands that slide with the tilt, strongest in the mid-tones (S tier)
                float band = dot(uv, float2(0.85, 1.35)) * 1.8 + t.x * 1.1 + t.y * 0.7 + _Time.y * 0.02;
                float3 rainbow = Hue(band);
                float foilMask = 0.35 + 0.65 * smoothstep(0.05, 0.7, lum);
                c.rgb = lerp(c.rgb, c.rgb * 0.55 + rainbow * 0.75, saturate(_Foil * foilMask * 0.7));

                // pearlescent: soft mother-of-pearl — milky pastels and a shifting sheen (A tier; not rainbow holo)
                if (_Pearl > 0.001)
                {
                    float ang = uv.x * 2.8 + uv.y * 2.0 + t.x * 2.2 + t.y * 1.6 + _Time.y * 0.12;
                    float3 pA = Hue(ang * 0.18 + 0.05);
                    float3 pB = Hue(ang * 0.18 + 0.42);
                    float3 pearl = lerp(pA, pB, 0.5 + 0.5 * sin(ang * 2.8 + t.x * 2.4));
                    pearl = lerp(float3(0.92, 0.94, 0.97), pearl, 0.4); // milky, soft pastel
                    float pearlMask = 0.25 + 0.75 * smoothstep(0.05, 0.8, lum);
                    float sheen = pow(saturate(0.5 + 0.5 * sin(dot(uv, float2(3.8, 5.8)) + t.x * 3.4 + t.y * 2.6)), 2.0);
                    float broad = pow(saturate(1.0 - length((uv - 0.5 - t * 0.15) * float2(1.1, 0.85)) * 1.4), 1.6);
                    float mixAmt = saturate(_Pearl * pearlMask * (0.35 + 0.4 * sheen + 0.45 * broad));
                    c.rgb = lerp(c.rgb, c.rgb * 0.72 + pearl * 0.65 + sheen * 0.28 + broad * 0.12, mixAmt);
                }

                // metal foil: brushed metal (fine horizontal grain) whose bright sheen slides across with the tilt
                if (_Metal > 0.001)
                {
                    float grain = 0.86 + 0.14 * Hash(float2(floor(uv.y * 260.0), 3.7)) + 0.05 * sin(uv.x * 90.0 + Hash(float2(floor(uv.y * 90.0), 1.3)) * 6.0);
                    float across = dot(uv - 0.5, normalize(float2(0.45, 1.0)));
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
                    float2 q = uv - 0.5 - t * 0.25;
                    float wave = length(q * float2(1.0, 0.8)) * 2.6 + sin(q.x * 5.0 + _Time.y * 0.4) * 0.35 + q.y * 1.2;
                    float turn = (wave + t.x * 0.9 - t.y * 0.6 + _Time.y * 0.08) * 6.2831853;
                    float3 turned = HueTurn(c.rgb, turn);
                    float3 tint = Hue(wave * 0.5 + t.x * 0.3 + _Time.y * 0.013) * (0.18 + lum * 0.9);
                    float3 poly = lerp(turned, saturate(turned * 0.6 + tint * 0.75), 0.55);
                    c.rgb = lerp(c.rgb, poly, saturate(_Poly * 0.9));
                }

                // the special finishes (a card file's "finish"; 0 = none)
                float spaceMask = 0; // starfield: how much of this spot is black space (the glare stays off it)
                if (_SpecialAmt > 0.001)
                {
                    float amt = _SpecialAmt;
                    float id = round(_Special);
                    float dark = 1.0 - smoothstep(0.12, 0.45, lum);
                    float frame = 1.0 - inside;
                    if (id == 1) // starfield: the dark parts become the black void of space, with stars in it
                    {
                        float2 q = uv - 0.5;
                        // the card body (strength 1; the picture gets less) turns everything but its bright text black,
                        // the picture only its darkest parts
                        float wide = 1.0 - smoothstep(0.38, 0.7, lum);
                        float space = saturate(amt * inside * (amt > 0.99 ? wide : dark));
                        c.rgb = lerp(c.rgb, c.rgb * 0.03, space * 0.97); // near black
                        spaceMask = space;
                        float3 neb = float3(0.25, 0.1, 0.45) * smoothstep(0.75, 0.0, length(q * float2(1.0, 0.8) + t * 0.08 + float2(0.15, -0.1)))
                                   + float3(0.05, 0.2, 0.4) * smoothstep(0.6, 0.0, length(q * float2(1.2, 0.9) + t * 0.12 - float2(0.2, 0.15)));
                        neb *= 0.6 + 0.4 * sin(_Time.y * 0.2 + q.x * 3.0);
                        float st = Stars(uv, t, 18.0, 0.02, 0.82) * 0.5 + Stars(uv, t, 32.0, 0.05, 0.86) * 0.8 + Stars(uv, t, 55.0, 0.1, 0.9);
                        c.rgb += (neb * 0.12 + st * float3(0.9, 0.92, 1.0) * 1.2) * space; // only a faint hint of nebula
                    }
                    else if (id == 2) // gilded
                    {
                        float leaf = 0.75 + 0.25 * Hash(floor(uv * float2(9.0, 12.0) + Noise(uv * 20.0) * 0.4));
                        float across = dot(uv - 0.5, normalize(float2(0.5, 1.0)));
                        float sheen = exp(-pow((across - (t.y * 0.8 + t.x * 0.5)) * 3.5, 2.0));
                        float3 gold = lerp(float3(0.55, 0.36, 0.12), float3(1.0, 0.85, 0.45), saturate(leaf * 0.6 + sheen * 0.8));
                        float m = saturate((dark * 0.85 + frame) * amt);
                        c.rgb = lerp(c.rgb, gold * (0.55 + 0.7 * sheen) * leaf, m);
                        c.rgb += Glints(uv, float2(40.0, 56.0), 0.88, 1.4, t) * m * float3(1.0, 0.9, 0.6) * 1.2;
                    }
                    else if (id == 3) // prism
                    {
                        float2 g = float2(uv.x * 5.0 + uv.y * 1.5, uv.y * 7.0);
                        float2 gf = frac(g);
                        float2 sid = floor(g) * 2.0 + step(gf.y, gf.x);
                        float hh = Hash(sid);
                        float flash = pow(saturate(sin(hh * 6.2831 + t.x * 4.5 + t.y * 3.5 + _Time.y * 0.3)), 6.0);
                        float3 col = Hue(hh + dot(t, float2(0.8, 0.5)));
                        float facet = 0.6 + 0.4 * Hash(sid + 2.1);
                        float edge = 1.0 - smoothstep(0.0, 0.04, min(min(min(gf.x, gf.y), min(1.0 - gf.x, 1.0 - gf.y)), abs(gf.x - gf.y)));
                        c.rgb = lerp(c.rgb, c.rgb * 0.7 + col * 0.45 * facet, amt * 0.45 * (0.4 + 0.6 * lum));
                        c.rgb += col * flash * amt * 0.55 * (0.3 + lum) + edge * amt * 0.12 * flash;
                    }
                    else if (id == 4) // frosted
                    {
                        float n = Noise(uv * float2(9.0, 12.0)) * 0.6 + Noise(uv * float2(23.0, 31.0)) * 0.4;
                        float frost = smoothstep(0.75, 0.05, -d + (n - 0.5) * 0.5 - 0.1 * sin(_Time.y * 0.3));
                        float veins = pow(saturate(1.0 - abs(Noise(uv * float2(30.0, 40.0)) - 0.5) * 12.0), 3.0);
                        c.rgb = lerp(c.rgb, c.rgb * float3(0.86, 0.95, 1.08), 0.4 * amt);
                        c.rgb = lerp(c.rgb, float3(0.8, 0.92, 1.0), saturate(frost * (0.45 + 0.3 * veins)) * amt);
                        c.rgb += Glints(uv, float2(46.0, 64.0), 0.8, 1.2, t) * frost * amt * 1.2 * float3(0.85, 0.95, 1.0);
                    }
                    else if (id == 5) // neon
                    {
                        float bw = max(_Border, 0.02);
                        float tube = saturate(1.0 - abs(d + bw * 0.5) / (bw * 0.5));
                        float flick = 1.0 - 0.5 * step(0.985, Hash(float2(floor(_Time.y * 14.0), 2.0)));
                        float hum = (0.9 + 0.1 * sin(_Time.y * 6.0)) * flick;
                        float3 nc = _BorderColor.rgb;
                        float inner = max(0.0, -(d + bw));
                        float halo = exp(-inner * 5.0);
                        c.rgb *= lerp(1.0, 0.8, amt * inside * (1.0 - halo));
                        c.rgb = lerp(c.rgb, lerp(nc * 1.3, float3(1, 1, 1), pow(tube, 3.0) * 0.7) * hum, saturate(tube * 1.5) * amt);
                        c.rgb += nc * halo * 0.45 * amt * hum * inside;
                    }
                    else if (id == 6) // aurora
                    {
                        float x = uv.x + t.x * 0.15;
                        float curtain = pow(saturate(0.5 + 0.5 * sin(x * 9.0 + 2.0 * sin(x * 2.3 + _Time.y * 0.4) + t.x * 3.0)), 3.0);
                        curtain *= smoothstep(0.15, 0.85, uv.y + 0.1 * sin(x * 5.0 + _Time.y * 0.5));
                        curtain *= 0.7 + 0.3 * sin(x * 80.0 + _Time.y * 1.3);
                        float3 col = lerp(float3(0.2, 1.0, 0.6), float3(0.6, 0.3, 1.0), saturate(uv.y * 1.2 - 0.2 + 0.3 * sin(x * 4.0 + _Time.y * 0.3)));
                        c.rgb += col * curtain * amt * (0.2 + 0.6 * dark) * inside;
                    }
                    else if (id == 7) // molten
                    {
                        float2 g = uv * float2(5.0, 7.0);
                        float2 gi = floor(g), gf = frac(g);
                        float f1 = 8.0, f2 = 8.0, cellH = 0.0;
                        for (int y = -1; y <= 1; y++)
                            for (int x = -1; x <= 1; x++)
                            {
                                float2 b = float2(x, y);
                                float2 r = b + float2(Hash(gi + b), Hash(gi + b + 5.3)) - gf;
                                float dd = dot(r, r);
                                if (dd < f1) { f2 = f1; f1 = dd; cellH = Hash(gi + b + 9.1); }
                                else if (dd < f2) f2 = dd;
                            }
                        float crack = 1.0 - smoothstep(0.0, 0.07, sqrt(f2) - sqrt(f1));
                        float pulse = 0.6 + 0.4 * sin(_Time.y * 2.0 + cellH * 6.2831);
                        float3 heat = lerp(float3(1.0, 0.3, 0.04), float3(1.0, 0.8, 0.3), crack * pulse);
                        float m = amt * (0.5 + 0.5 * max(dark, frame));
                        c.rgb *= 1.0 - 0.3 * amt * dark;
                        c.rgb += heat * crack * pulse * m;
                        c.rgb += float3(1.0, 0.35, 0.05) * frame * amt * 0.25 * pulse;
                    }
                    else if (id == 8) // glitch: colour split (frame too), coloured blocks, scan lines, pixel noise
                    {
                        amt *= gGate;
                        float3 gm = lerp(_Color2, _Color, uv.y).rgb;
                        float rr = tex2D(_MainTex, TRANSFORM_TEX(uv + float2(gSplit, 0), _MainTex)).r * gm.r;
                        float bb = tex2D(_MainTex, TRANSFORM_TEX(uv - float2(gSplit, 0), _MainTex)).b * gm.b;
                        float2 off = float2(gSplit * _Size.x, 0);
                        float inR = saturate(0.5 - (RoundedBox(p + off, _Size.xy * 0.5, _Size.z) + _Border) / aa);
                        float inB = saturate(0.5 - (RoundedBox(p - off, _Size.xy * 0.5, _Size.z) + _Border) / aa);
                        float r2 = _Border > 0 ? lerp(_BorderColor.r, rr, inR) : rr;
                        float b2 = _Border > 0 ? lerp(_BorderColor.b, bb, inB) : bb;
                        c.rgb = lerp(c.rgb, float3(r2, c.g, b2), amt);
                        float2 blk = floor(uv * float2(7.0, 10.0) + float2(gSlice * 0.37, 0));
                        float on = step(0.93 - 0.12 * gBurst, Hash(blk + gSlice));
                        float3 bc = Hue(Hash(blk * 1.7 + gSlice));
                        c.rgb = lerp(c.rgb, bc * (0.6 + 0.6 * lum) + c.rgb * 0.3, on * amt * 0.6);
                        c.rgb += frame * amt * Hue(uv.y * 3.0 + _Time.y * 2.0) * 0.35 * (0.5 + gBurst);
                        c.rgb *= 1.0 - amt * 0.15 * (0.5 + 0.5 * sin(uv.y * _Size.y * 140.0 + _Time.y * 8.0));
                        c.rgb += (Hash(floor(uv * float2(120.0, 168.0)) + frac(_Time.y * 7.3) * 100.0) - 0.5) * 0.1 * amt;
                    }
                    else if (id == 9) // glass: see-through, bright edges, a streak of light that moves with the tilt
                    {
                        float fres = exp(-max(0.0, -d) * 16.0);
                        float3 tint = float3(0.85, 0.95, 1.0);
                        float across = dot(uv - 0.5, normalize(float2(1.0, 0.7)));
                        float at = t.x * 0.6 + t.y * 0.4;
                        float streak = exp(-pow((across - at) * 6.0, 2.0)) + 0.5 * exp(-pow((across - at + 0.3) * 14.0, 2.0));
                        c.rgb = lerp(c.rgb, c.rgb * 0.6 + tint * 0.25, amt * 0.6);
                        c.rgb += tint * (fres * 0.22 + streak * 0.12) * amt;
                        glass = lerp(1.0, saturate(0.1 + fres * 0.45 + streak * 0.12 + frame * 0.5 + lum * 0.6), amt);
                    }
                    else if (id == 12) // infinity mirror: dark glass with the tunnel of lights in it
                    {
                        // (on the body, the deeper rings stay faint behind the title and rules text, so it reads)
                        float textArea = _Border > 0 ? smoothstep(0.47, 0.42, uv.y) * smoothstep(0.47, 0.40, abs(uv.x - 0.5)) * 0.7 : 0.0;
                        float3 rings = Infinity(uv, t, _FxColor.rgb, textArea);
                        c.rgb = lerp(c.rgb, c.rgb * 0.35, amt * inside * 0.6);
                        c.rgb += rings * amt * inside;
                    }
                    else if (id == 10 || id == 11) // borealis / rainbow: an animated fill in place of the card's own
                    {
                        float3 fill = id == 10 ? Borealis(uv) : Rainbow(uv);
                        c.rgb = lerp(c.rgb, fill, amt * inside);
                    }
                }

                // shine sweep: a bright diagonal band passing across
                float along = dot(uv - 0.5, normalize(float2(1.0, 0.65))) + 0.5;
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
                float2 dl = uv - light;
                float streak = exp(-pow(dot(dl, normalize(float2(1.0, -0.55))) * 4.5, 2.0));
                float spot = exp(-dot(dl * float2(1.0, 0.75), dl * float2(1.0, 0.75)) * 7.0);
                c.rgb += (streak * 0.22 + spot * 0.28) * _Glare * (1.0 - spaceMask * 0.8);

                // sparkles: tiny glints in a grid of cells, each twinkling at its own moment
                float2 grid = uv * float2(46.0, 64.0);
                float2 cell = floor(grid);
                float h = Hash(cell);
                float twinkle = pow(saturate(sin(h * 60.0 + t.x * 11.0 + t.y * 8.0 + _Time.y * 1.6)), 24.0);
                float2 f = frac(grid) - 0.5;
                float star = saturate(1.0 - length(f) * 3.2) + saturate(1.0 - abs(f.x) * 12.0) * saturate(1.0 - abs(f.y) * 2.5) * 0.5;
                c.rgb += step(0.86, h) * twinkle * star * _Sparkle * 1.4;

                c.a *= alpha * _Alpha * glass;
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
