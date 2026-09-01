Shader "Guilty/LightShaftAdditive"
{
    // Facho de luz volumetrico falso.
    // O EEVEE resolvia isso com Volume Scatter no World; URP nao tem volumetria,
    // entao o cone de luz e geometria (FX_LightShaft, vindo do FBX) com este shader aditivo.
    //
    // Tres fades combinados evitam que o cone leia como "cone de plastico":
    //   1. angular  - some no silhueta (esconde a borda dura da malha)
    //   2. vertical - forte perto da lampada, fraco perto da mesa
    //   3. de profundidade - some onde o cone atravessa mesa/chao (sem linha de interseccao)

    Properties
    {
        _Color        ("Cor", Color)                    = (1.0, 0.86, 0.66, 1.0)
        _Intensity    ("Intensidade", Range(0, 4))      = 0.55
        _EdgeSoftness ("Suavidade da borda", Range(0.5, 8)) = 2.6
        _BottomFade   ("Fade inferior", Range(0.01, 1)) = 0.55
        _TopBoost     ("Ganho no topo", Range(0, 1))    = 0.65
        _DepthFade    ("Fade de profundidade (m)", Range(0.01, 3)) = 0.55
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent+100"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ShaftAdditive"

            Blend One One          // aditivo: so soma luz, nunca escurece
            ZWrite Off
            ZTest LEqual
            Cull Off               // o cone e uma casca aberta, precisa das duas faces

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 viewWS     : TEXCOORD2;
                float4 screenPos  : TEXCOORD3;
                float  eyeDepth   : TEXCOORD4;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Intensity;
                float  _EdgeSoftness;
                float  _BottomFade;
                float  _TopBoost;
                float  _DepthFade;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = pos.positionCS;
                OUT.uv         = IN.uv;
                OUT.normalWS   = nrm.normalWS;
                OUT.viewWS     = GetWorldSpaceViewDir(pos.positionWS);
                OUT.screenPos  = ComputeScreenPos(pos.positionCS);
                OUT.eyeDepth   = -TransformWorldToView(pos.positionWS).z;

                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(IN.viewWS);

                // 1. FADE ANGULAR — abs() porque Cull Off deixa ver as duas faces do cone.
                //    Faces de frente para a camera brilham; a silhueta some, escondendo a borda da malha.
                float facing = saturate(abs(dot(N, V)));
                float angular = pow(facing, _EdgeSoftness);

                // 2. FADE VERTICAL — a UV do cone foi gerada no Blender com V = altura (0 = mesa, 1 = lampada)
                float v = saturate(IN.uv.y);
                float vertical = smoothstep(0.0, _BottomFade, v);
                vertical *= lerp(1.0 - _TopBoost, 1.0, v);

                // 3. FADE DE PROFUNDIDADE — mata a linha dura onde o cone corta a mesa e o chao
                float2 screenUV = IN.screenPos.xy / max(IN.screenPos.w, 1e-4);
                float  rawDepth = SampleSceneDepth(screenUV);
                float  sceneEye = LinearEyeDepth(rawDepth, _ZBufferParams);
                float  depthFade = saturate((sceneEye - IN.eyeDepth) / max(_DepthFade, 1e-4));

                float a = angular * vertical * depthFade * _Intensity;

                return half4(_Color.rgb * a, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
