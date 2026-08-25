// 壁の向こうにあっても必ず手前に描かれる、単色のマーカー用シェーダー。
//
// URP の Unlit は _ZTest を公開しておらず、マテリアル設定だけでは
// 「遮蔽物を無視して描く」ことができないため、最小構成で自前に用意している。
Shader "MagicHand/XRayMarker"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 0.3, 0.3, 0.9)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Overlay"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "XRayMarker"

            // 深度テストを常に通し、深度は書かない＝壁に隠れず、他の描画も邪魔しない
            ZTest Always
            ZWrite Off
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return _BaseColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
