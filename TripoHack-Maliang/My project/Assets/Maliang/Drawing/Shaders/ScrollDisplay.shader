// Scroll surface: paper × ink (multiply), then the seal layer on top (premultiplied alpha).
// Double-sided so the scroll reads from either side once it levitates.
Shader "Maliang/ScrollDisplay"
{
    Properties
    {
        _PaperTex ("Paper", 2D) = "white" {}
        _PaperTint ("Paper Tint", Color) = (0.96, 0.92, 0.84, 1)
        _InkTex ("Ink (RT)", 2D) = "white" {}
        _SealTex ("Seal (RT)", 2D) = "black" {}
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

            TEXTURE2D(_PaperTex); SAMPLER(sampler_PaperTex);
            TEXTURE2D(_InkTex);   SAMPLER(sampler_InkTex);
            TEXTURE2D(_SealTex);  SAMPLER(sampler_SealTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _PaperTex_ST;
                half4 _PaperTint;
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

            half4 frag(Varyings i) : SV_Target
            {
                half3 paper = SAMPLE_TEXTURE2D(_PaperTex, sampler_PaperTex, i.uv * _PaperTex_ST.xy + _PaperTex_ST.zw).rgb * _PaperTint.rgb;
                half3 ink = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, i.uv).rgb;
                half4 seal = SAMPLE_TEXTURE2D(_SealTex, sampler_SealTex, i.uv);
                half3 col = paper * ink;
                col = col * (1 - seal.a) + seal.rgb;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
