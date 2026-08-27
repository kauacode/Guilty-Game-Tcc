Shader "Guilty/UIBlur"
{
    // Blur leve de tela de fundo para painéis de UI translúcidos (glassmorphism).
    // Requer "Opaque Texture" habilitado no URP Asset — sem isso, a amostra
    // de cor de cena fica preta e o painel aparece só com a cor de tint sólida.
    Properties
    {
        _TintColor ("Tint Color", Color) = (0.04, 0.04, 0.07, 0.55)
        _BlurSize ("Blur Size", Range(0, 10)) = 3
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _TintColor;
                float _BlurSize;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.screenPos = ComputeScreenPos(OUT.positionHCS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float2 texelSize = _BlurSize / _ScreenParams.xy;

                half3 color = 0;
                color += SampleSceneColor(screenUV + texelSize * float2(1, 1));
                color += SampleSceneColor(screenUV + texelSize * float2(-1, 1));
                color += SampleSceneColor(screenUV + texelSize * float2(1, -1));
                color += SampleSceneColor(screenUV + texelSize * float2(-1, -1));
                color += SampleSceneColor(screenUV) * 2;
                color /= 6.0;

                color = lerp(color, _TintColor.rgb, _TintColor.a);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
