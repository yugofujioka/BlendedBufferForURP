Shader "BlendedBuffer/Premultiply Blit"
{
//    Properties
//    {
//        [Enum(UnityEngine.Rendering.CompareFunction)]
//        _StencilComp("Stencil Comp", Int) = 0 // Disable
//        [Enum(UnityEngine.Rendering.BlendMode)]
//        _SrcFactor("Src Factor", Int) = 1     // One
//        [Enum(UnityEngine.Rendering.BlendMode)]
//        _DstFactor("Dst Factor", Int) = 10    // OneMinusSrcAlpha
//    }
    
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"}

        Pass
        {
            Name "Blit Shrink"
            //Blend [_SrcFactor] [_DstFactor]
            Blend One OneMinusSrcAlpha
            ZTest Always
            ZWrite Off
            Cull Off
            
//            Stencil {  
//                Ref 1
//                Comp [_StencilComp]
//                Pass Replace  
//            }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Fragment
            //#pragma multi_compile_fragment _ _LINEAR_TO_SRGB_CONVERSION // no need?
            #pragma multi_compile_fragment _ DEBUG_DISPLAY

            // Core.hlsl for XR dependencies
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/DebuggingFullscreen.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            SAMPLER(sampler_BlitTexture);

            half4 Fragment(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv);

                //clip(col.a - 0.001); // TODO: confirm performance

                #ifdef _LINEAR_TO_SRGB_CONVERSION
                col = LinearToSRGB(col);
                #endif

                #if defined(DEBUG_DISPLAY)
                half4 debugColor = 0;

                if(CanDebugOverrideOutputColor(col, uv, debugColor))
                {
                    return debugColor;
                }
                #endif

                return col;
            }
            ENDHLSL
        }
    }
}
