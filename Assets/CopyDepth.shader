Shader "BlendedBuffer/CopyDepth"
{
    SubShader
    {
        Tags {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "CopyDepth"
            ZTest Always
            ZWrite On
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            //DEPTH_TEXTURE(_BlitTexture);
            SAMPLER(sampler_BlitTexture);
            //#define DEPTH_TEXTURE(name) TEXTURE2D_FLOAT(name)
            #define SAMPLE(uv) SAMPLE_DEPTH_TEXTURE(_BlitTexture, sampler_BlitTexture, uv)

            float frag(Varyings input) : SV_Depth
            {
                return SAMPLE(input.texcoord);
            }
            ENDHLSL
        }
    }
}
