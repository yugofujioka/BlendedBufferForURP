Shader "BlendedBuffer/ParticleShader"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        _Cutoff("AlphaCutout", Range(0.0, 1.0)) = 0.5
        
        [Toggle(_ALPHATEST_ON)] _AlphaTest("Alpha Test", Float) = 0.0
        
        [Enum(UnityEngine.Rendering.BlendMode)]
        _SrcFactor("Src Factor", Int) = 5     // SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)]
        _DstFactor("Dst Factor", Int) = 10    // OneMinusSrcAlpha
    }
    SubShader
    {
        Tags {
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "LightMode" = "SRPDefaultUnlit"
        }
        LOD 100
        
        Blend [_SrcFactor][_DstFactor]
        Cull Back
        ZTest LEqual
        ZWrite Off

        Pass
        {
            Name "Blended Particle"
            
//            Stencil {  
//                Ref 1
//                Comp Always  
//                Pass Replace  
//            }
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            //#pragma multi_compile_fog
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            //#pragma shader_feature_local_fragment _ALPHAMODULATE_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float4 positionCS : SV_POSITION;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            
CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Cutoff;
CBUFFER_END

            Varyings vert (Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
                return output;
            }

            half4 frag (Varyings i) : SV_Target
            {
                // sample the texture
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half4 finalColor = 0;
                half alpha = texColor.a * _BaseColor.a;
                finalColor.a = AlphaDiscard(alpha, _Cutoff);
                half3 color = texColor.rgb * _BaseColor.rgb;
                finalColor.rgb = color;//AlphaModulate(color, alpha);
                
                return finalColor;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
