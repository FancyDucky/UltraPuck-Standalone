// The Underwater rink's floor: warm sand with slow lighter and darker patches, a fine grain, and little grey pebbles
// scattered about. The moving light on it (caustics) is drawn over the top by Caustics.shader, so it plays over the
// players too.
Shader "UltraPuck/Sand"
{
    Properties
    {
        _Sand ("Sand", Color) = (0.73, 0.645, 0.415, 1)
        _Pebble ("Pebbles", Color) = (0.6, 0.6, 0.6, 1)
        _PebbleCell ("Pebble spacing (world units)", Float) = 1.6
        _PebbleChance ("Share of spots with a pebble", Float) = 0.22
        _PebbleAmount ("How much the pebbles show (0..1)", Float) = 0.45
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

            float4 _Sand, _Tint, _Pebble;
            float _TintAmount, _PebbleCell, _PebbleChance, _PebbleAmount;

            // how much of p a pebble covers (0..1, softened edge): a flat grey shape, a little squashed and turned
            float Pebble(float2 p)
            {
                float cover = 0.0;
                float2 c = floor(p / _PebbleCell);
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 cell = c + float2(x, y);
                    if (Hash(cell + 17.3) > _PebbleChance) continue;
                    float2 at = (cell + 0.2 + 0.6 * Hash2(cell)) * _PebbleCell;
                    float r = lerp(0.06, 0.14, Hash(cell + 4.1));
                    float ang = Hash(cell + 9.7) * 6.2831;
                    float2 d = p - at;
                    float2 q = float2(cos(ang) * d.x + sin(ang) * d.y, -sin(ang) * d.x + cos(ang) * d.y);
                    q.y *= lerp(1.1, 1.6, Hash(cell + 2.3));
                    cover = max(cover, 1.0 - smoothstep(r * 0.8, r, length(q)));
                }
                return cover;
            }

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
                // pebbles: a flat grey, only partly over the sand so they stay quiet (no shading, no shadow)
                sand = lerp(sand, _Pebble.rgb, Pebble(p) * _PebbleAmount);
                return fixed4(lerp(sand * 0.95, _Tint.rgb, _TintAmount), 1.0);
            }
            ENDCG
        }
    }
}
