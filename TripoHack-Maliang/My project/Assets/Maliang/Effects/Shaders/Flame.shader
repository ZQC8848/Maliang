// Additive particle shader for the candle flame. No texture: the shape is computed from the particle UVs,
// either a teardrop (tongue of flame, wide round base tapering to a point) or a soft round blob (glow, embers).
// Colour and alpha come from the particle vertex colour; _Intensity pushes the core past 1 for a hot look.
Shader "Maliang/Flame"
{
    Properties
    {
        [Enum(Teardrop,0,Round,1)] _Shape ("Shape", Float) = 0
        _Intensity ("Intensity", Range(0, 4)) = 1.5
        _Softness ("Edge Softness", Range(0.5, 4)) = 1.6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Flame"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Shape;
                half _Intensity;
                half _Softness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 2 - 1; // -1..1, +y up
                half d;
                if (_Shape < 0.5)
                {
                    // Teardrop: full width at the lower third, pinching to a point at the top.
                    float t = saturate(p.y * 0.5 + 0.5);
                    float w = lerp(0.95, 0.12, pow(t, 1.3));
                    float y = p.y < -0.3 ? (p.y + 0.3) / 0.7 : (p.y + 0.3) / 1.3;
                    d = length(float2(p.x / w, y));
                }
                else
                {
                    d = length(p);
                }
                half a = pow(saturate(1 - d), _Softness);
                half k = a * i.color.a * _Intensity;
                return half4(i.color.rgb * k, 0);
            }
            ENDHLSL
        }
    }
}
