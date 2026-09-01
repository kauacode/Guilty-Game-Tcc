Shader "Guilty/TwoWayMirror"
{
    // Espelho falso (one-way glass) da sala de interrogatório.
    //
    // O URP não tem screen space reflections, e Reflection Probe é cubemap capturado
    // de um ponto — não serve para superfície plana. Aqui a reflexão vem de uma
    // RenderTexture renderizada por uma câmera espelhada (PlanarMirrorReflection.cs),
    // amostrada em espaço de tela.
    //
    // Não é espelho perfeito de propósito: vidro espelhado de sala de interrogatório
    // devolve a imagem escurecida e com um leve tom esverdeado.

    Properties
    {
        _ReflectionTex ("Reflexão (preenchida por script)", 2D) = "black" {}
        _Tint          ("Tom do vidro", Color)            = (0.62, 0.68, 0.64, 1)
        _Strength      ("Força da reflexão", Range(0, 2)) = 0.75
        _BaseColor     ("Cor do vidro", Color)            = (0.012, 0.014, 0.013, 1)
        _Fresnel       ("Ganho rasante", Range(0, 3))     = 0.9
        _Grime         ("Sujeira", Range(0, 1))           = 0.12
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "MirrorForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos  : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 viewWS     : TEXCOORD2;
            };

            TEXTURE2D(_ReflectionTex);
            SAMPLER(sampler_ReflectionTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float4 _BaseColor;
                float  _Strength;
                float  _Fresnel;
                float  _Grime;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS = pos.positionCS;
                OUT.screenPos  = ComputeScreenPos(pos.positionCS);
                OUT.normalWS   = nrm.normalWS;
                OUT.viewWS     = GetWorldSpaceViewDir(pos.positionWS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // A câmera de reflexão usa a mesma projeção da principal, então a
                // amostragem em espaço de tela alinha o reflexo com a geometria.
                float2 uv = IN.screenPos.xy / max(IN.screenPos.w, 1e-4);
                half3 refl = SAMPLE_TEXTURE2D(_ReflectionTex, sampler_ReflectionTex, uv).rgb;

                // Mais reflexo em ângulo rasante, como vidro de verdade.
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(IN.viewWS);
                float  f = pow(1.0 - saturate(dot(N, V)), 4.0);

                half3 col = refl * _Tint.rgb * (_Strength + f * _Fresnel);

                // Poeira/dedadas: levantam um piso escuro e tiram o ar de espelho perfeito.
                col = lerp(col, col * 0.75 + _BaseColor.rgb * 2.0, _Grime);
                col += _BaseColor.rgb;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Lit"
}
