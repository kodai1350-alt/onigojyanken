// 壁の裏では隠れる（通常の深度テスト）、頂点カラー対応のワールド空間テキスト用シェーダー。
//
// TextMeshはメッシュの頂点カラーにTextMesh.colorを焼き込むが、標準のURP Unlitは
// 頂点カラーを一切参照しない。そのため優位/劣位/互角の色分け（TextMesh.colorをRGBAで変える）が
// 画面に反映されず、フォントテクスチャそのものの色（黒）で出てしまっていた
// （文字が常に黒く表示される不具合の原因）。フォントのテクスチャはアルファチャンネルだけが
// グリフの形を持つ（RGBは無視してよい）ため、テクスチャのアルファ×頂点カラーで出力する
// （旧来の"GUI/Text Shader"と同じ合成方法。壁を貫通させたい表示用の
// XRayMarker.shaderとは異なり、こちらはZTestを通常のLEqualのままにしている）。
Shader "MagicHand/WorldTextVertexColor"
{
    Properties
    {
        _MainTex ("Font Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "WorldTextVertexColor"

            ZTest LEqual
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;
                return half4(IN.color.rgb, IN.color.a * alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
