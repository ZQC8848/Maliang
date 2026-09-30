// Draws one brush / seal stamp into a RenderTexture via GL immediate mode (see InkCanvas).
// Output is premultiplied alpha, blended One OneMinusSrcAlpha.
//   _UseTexColor = 0: colour comes from _Color, shape from the texture alpha (brush textures are black + alpha)
//   _UseTexColor = 1: colour comes from the texture tinted by _Color (seal textures)
Shader "Hidden/Maliang/BrushStamp"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (0,0,0,1)
        _UseTexColor ("Use Texture Color", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float _UseTexColor;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);
                fixed3 rgb = lerp(_Color.rgb, tex.rgb * _Color.rgb, _UseTexColor);
                fixed a = tex.a * _Color.a;
                return fixed4(rgb * a, a);
            }
            ENDCG
        }
    }
}
