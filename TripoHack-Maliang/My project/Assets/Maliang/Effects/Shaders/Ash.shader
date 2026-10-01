// Alpha-blended particle shader for burning paper: ash flakes and thin smoke. No texture: a soft blob with a
// ragged outline, cut from the particle UVs; colour and alpha from the particle vertex colour.
Shader "Maliang/Ash"
{
    Properties
    {
        _Softness ("Edge Softness", Range(0.05, 1)) = 0.35
        _Ragged ("Ragged Edge", Range(0, 0.5)) = 0.25
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Ash"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Softness;
                half _Ragged;
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
                float2 p = i.uv * 2 - 1;
                float a = atan2(p.y, p.x);
                // A few lobes around the edge make it read as a torn flake rather than a dot.
                float edge = 1 - _Ragged * (0.5 + 0.5 * sin(a * 5 + 1.3) * sin(a * 3 - 0.7));
                float d = length(p) / edge;
                half alpha = saturate((1 - d) / _Softness);
                return half4(i.color.rgb, i.color.a * alpha);
            }
            ENDHLSL
        }
    }
}
