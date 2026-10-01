// Scroll surface: paper × ink (multiply), then the seal layer on top (premultiplied alpha).
// Double-sided so the scroll reads from either side once it levitates.
// _RevealHalf clips the paper to |u - 0.5| <= _RevealHalf: the strip between the rollers while the scroll unrolls.
//
// Burning (Phase 4): up to 16 burn points in canvas metres (xy = position from the centre, z = radius, w = unused).
// The burn field is the distance outside the nearest circle, roughened by noise. Inside: a hole. Just outside: a
// glowing edge, then a charred band, then a brown scorch ring. A point with a negative radius has not caught yet:
// it only shows as a scorch spot that darkens as the radius approaches zero.
Shader "Maliang/ScrollDisplay"
{
    Properties
    {
        _PaperTex ("Paper", 2D) = "white" {}
        _PaperTint ("Paper Tint", Color) = (0.96, 0.92, 0.84, 1)
        _InkTex ("Ink (RT)", 2D) = "white" {}
        _SealTex ("Seal (RT)", 2D) = "black" {}
        _RevealHalf ("Reveal Half Width (UV)", Range(0, 0.5)) = 0.5

        [Header(Burning)]
        _CanvasSize ("Canvas Size (m)", Vector) = (0.72, 0.36, 0, 0)
        _BurnCount ("Burn Point Count", Float) = 0
        _EdgeNoise ("Edge Roughness (m)", Range(0, 0.04)) = 0.02
        _NoiseScale ("Edge Noise Scale (1/m)", Float) = 38
        _GlowWidth ("Glow Width (m)", Range(0, 0.02)) = 0.004
        _CharWidth ("Char Width (m)", Range(0, 0.05)) = 0.012
        _ScorchWidth ("Scorch Width (m)", Range(0, 0.1)) = 0.04
        [HDR] _GlowColor ("Glow Colour", Color) = (3.2, 1.15, 0.28, 1)
        _CharColor ("Char Colour", Color) = (0.07, 0.045, 0.03, 1)
        _ScorchColor ("Scorch Colour", Color) = (0.55, 0.36, 0.18, 1)
        _GlowGain ("Glow Gain (breathing while held)", Range(0, 2)) = 1
        _Cool ("Cooling (fire put out)", Range(0, 1)) = 0
        _Crumble ("Crumble To Ash", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        Pass
        {
            Name "Unlit"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define MAX_BURN_POINTS 16

            TEXTURE2D(_PaperTex); SAMPLER(sampler_PaperTex);
            TEXTURE2D(_InkTex);   SAMPLER(sampler_InkTex);
            TEXTURE2D(_SealTex);  SAMPLER(sampler_SealTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _PaperTex_ST;
                half4 _PaperTint;
                float _RevealHalf;
                float4 _CanvasSize;
                float _BurnCount;
                float _EdgeNoise;
                float _NoiseScale;
                float _GlowWidth;
                float _CharWidth;
                float _ScorchWidth;
                half4 _GlowColor;
                half4 _CharColor;
                half4 _ScorchColor;
                float _GlowGain;
                float _Cool;
                float _Crumble;
                float4 _BurnPoints[MAX_BURN_POINTS];
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            // Integer hash of a lattice point. A float hash breaks here: the compiler may evaluate a corner shared by
            // two cells slightly differently in each, and the hash turns that rounding into visible seams.
            float Hash(int2 c)
            {
                uint2 q = asuint(c) * uint2(1597334673u, 3812015801u);
                uint n = (q.x ^ q.y) * 1597334673u;
                return n * (1.0 / 4294967295.0);
            }

            float ValueNoise(float2 p)
            {
                float2 fl = floor(p);
                int2 i = (int2)fl;
                float2 f = p - fl;
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash(i), b = Hash(i + int2(1, 0)), c = Hash(i + int2(0, 1)), d = Hash(i + int2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // Two octaves, roughly -0.5..0.5
            float EdgeNoise(float2 p)
            {
                return ValueNoise(p) * 0.65 + ValueNoise(p * 2.7 + 17.3) * 0.35 - 0.5;
            }

            half4 frag(Varyings i) : SV_Target
            {
                clip(_RevealHalf - abs(i.uv.x - 0.5));
                half3 paper = SAMPLE_TEXTURE2D(_PaperTex, sampler_PaperTex, i.uv * _PaperTex_ST.xy + _PaperTex_ST.zw).rgb * _PaperTint.rgb;
                half3 ink = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, i.uv).rgb;
                half4 seal = SAMPLE_TEXTURE2D(_SealTex, sampler_SealTex, i.uv);
                half3 col = paper * ink;
                col = col * (1 - seal.a) + seal.rgb;

                float2 pos = (i.uv - 0.5) * _CanvasSize.xy;
                // Crumbling remnant: the paper falls apart into ash everywhere, with grey edges.
                float crumbleEdge = 0;
                if (_Crumble > 0)
                {
                    float n = ValueNoise(pos * 22 + 3.7) * 0.65 + ValueNoise(pos * 64 + 9.1) * 0.35;
                    float k = n + 0.1 - _Crumble * 1.2;
                    clip(k);
                    crumbleEdge = saturate(1 - k / 0.06);
                }

                int count = (int)_BurnCount;
                if (count <= 0) return half4(lerp(col, half3(0.24, 0.23, 0.22), crumbleEdge), 1);

                // Distance outside the nearest burn circle, in metres.
                float field = 1e5;
                for (int k = 0; k < MAX_BURN_POINTS; k++)
                {
                    if (k >= count) break;
                    float4 bp = _BurnPoints[k];
                    field = min(field, distance(pos, bp.xy) - bp.z);
                }
                // Fine ragged detail plus broad wobble, so holes do not grow as circles.
                field += EdgeNoise(pos * _NoiseScale) * _EdgeNoise + EdgeNoise(pos * _NoiseScale * 0.28 + 5.1) * _EdgeNoise * 2.4;
                clip(field);

                float t = _Time.y;
                float charEnd = _GlowWidth + _CharWidth;
                // Scorch: paper browns towards the char band.
                float scorch = saturate(1 - (field - charEnd) / max(_ScorchWidth, 1e-4));
                col = lerp(col, col * _ScorchColor.rgb, scorch * scorch * 0.85);
                // Char: blackened paper (the drawing disappears under it).
                float charK = saturate(1 - (field - _GlowWidth) / max(_CharWidth, 1e-4));
                col = lerp(col, _CharColor.rgb, smoothstep(0, 1, charK));
                // Glowing edge, flickering along its length; a few sparks smoulder in the char.
                // Put out (_Cool): the glow fades orange -> dark red -> nothing, leaving a grey ash rim.
                float flicker = 0.65 + 0.7 * ValueNoise(pos * 60 + float2(t * 2.3, -t * 1.7));
                float glow = saturate(1 - field / max(_GlowWidth, 1e-4));
                float spark = step(0.86, ValueNoise(pos * 420 + t * 0.9)) * charK * (1 - glow);
                half3 glowCol = lerp(_GlowColor.rgb * flicker, half3(0.55, 0.06, 0.02), saturate(_Cool * 1.8));
                float hot = (1 - smoothstep(0.45, 1.0, _Cool)) * _GlowGain;
                col += glowCol * (glow * glow + spark * 0.35 * (1 - _Cool)) * hot;
                col = lerp(col, half3(0.3, 0.29, 0.27), glow * glow * smoothstep(0.5, 1.0, _Cool));
                col = lerp(col, half3(0.24, 0.23, 0.22), crumbleEdge);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
