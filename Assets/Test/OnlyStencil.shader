Shader "BlendedBuffer/OnlyStencil"
{
    SubShader
    {
        Tags {
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "LightMode" = "SRPDefaultUnlit"
        }

        Pass
        {
            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }

            ColorMask 0
            ZTest LEqual
            ZWrite On
            
            
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
                float4 positionCS : SV_POSITION;
            };
            Varyings vert (Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }
            void frag() {  }
            ENDHLSL
        }
    }
}
