using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// Shrink Buffer for URP
/// </summary>
public class fShrinkBufferFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public string renderTargetName = "_CustomTargetTexture";
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        public Vector2Int resolution = new Vector2Int(640, 360);
        public LayerMask layerMask = 0;
        public Material blitMaterial = null;
    }

    [SerializeField] Settings settings = new Settings();
    CopyDepthPass copyDepthPass;
    ShrinkBufferPass renderPass;
    RTHandle colorRTHandle, depthRTHandle; // RTHandle for custom render target


    class CopyDepthPass : ScriptableRenderPass
    {
        readonly Material copyDepth;
        RTHandle depthRT;

        public CopyDepthPass()
        {
            this.profilingSampler = new ProfilingSampler(nameof(CopyDepthPass));
            var copyDepthShader = Shader.Find("Hidden/Universal Render Pipeline/CopyDepth");
            if (copyDepthShader != null)
                this.copyDepth = new Material(copyDepthShader);
        }

        public void Setup(RTHandle depthRT)
        {
            this.depthRT = depthRT;
        }

        //public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
#if UNITY_EDITOR
            // This is a temporary workaround for Editor as not setting any depth here
            // would lead to overwriting depth in certain scenarios (reproducable while running DX11 tests)
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11)
                ConfigureTarget(this.depthRT, this.depthRT);
            else
#endif
                ConfigureTarget(this.depthRT);
            
            ConfigureClear(ClearFlag.All, Color.black);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, this.profilingSampler))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                
                if (this.depthRT.rt.graphicsFormat == GraphicsFormat.None)
                    cmd.EnableShaderKeyword("_OUTPUT_DEPTH");
                else
                    cmd.DisableShaderKeyword("_OUTPUT_DEPTH");
                
                //renderingData.commandBuffer.SetGlobalTexture("_CameraDepthAttachment", source.nameID);
                var targetDepthRTHandle = renderingData.cameraData.renderer.cameraDepthTargetHandle;
                var scaleBias = new Vector4(1f, 1f, 0f, 0f);
                Blitter.BlitTexture(cmd, targetDepthRTHandle, scaleBias, this.copyDepth, 0);
            }
            context.ExecuteCommandBuffer(cmd);

            CommandBufferPool.Release(cmd);
        }
        
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new System.ArgumentNullException("cmd");
            this.depthRT = null;
        }
    }

    class ShrinkBufferPass : ScriptableRenderPass
    {
        static readonly List<ShaderTagId> SHADER_TAG_ID = new List<ShaderTagId> {
            new ShaderTagId("SRPDefaultUnlit"),
            new ShaderTagId("UniversalForward"),
        };

        RTHandle colorRT, depthRT;
        readonly int layerMask;

        public ShrinkBufferPass(Settings settings)
        {
            this.profilingSampler = new ProfilingSampler(nameof(ShrinkBufferPass));
            this.renderPassEvent = settings.renderPassEvent;
            this.layerMask = settings.layerMask;
        }

        public void Setup(RTHandle colorRT, RTHandle depthRT)
        {
            this.colorRT = colorRT;
            this.depthRT = depthRT;
        }

        //public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType == CameraType.Game)
            {
                ConfigureTarget(this.colorRT, this.depthRT);
                ConfigureClear(ClearFlag.Color, Color.clear);
            }
            else
            {
                ConfigureTarget(renderingData.cameraData.renderer.cameraColorTargetHandle, renderingData.cameraData.renderer.cameraDepthTargetHandle);
            }

            // SetGlobalTexture to use in shader
            // This is required because the name is set automatically with appended information in ReAllocateIfNeeded
            //cmd.SetGlobalTexture(this.settings.renderTargetName, this.customRTHandle);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, this.profilingSampler))
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                // Rendering by LayerMask(e.g. any VFX)
                var drawSettings = CreateDrawingSettings(SHADER_TAG_ID, ref renderingData, SortingCriteria.CommonTransparent);
                drawSettings.perObjectData = PerObjectData.None;
                var filteringSettings = new FilteringSettings(RenderQueueRange.transparent, this.layerMask);
                
#if UNITY_2023_1_OR_NEWER
                var param = new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings);
                var rl = context.CreateRendererList(ref param);
                cmd.DrawRendererList(rl);
#else
                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filteringSettings);
#endif

                // Combine ShrinkBuffer to ColorAttachment
                // Blit tempRT -> cameraColor
                //var cameraColorTargetRTHandle = renderingData.cameraData.renderer.cameraColorTargetHandle;
                //Blitter.BlitCameraTexture(cmd, this.colorRT, cameraColorTargetRTHandle);
            }
            context.ExecuteCommandBuffer(cmd);

            CommandBufferPool.Release(cmd);
        }
        
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new System.ArgumentNullException("cmd");
            this.colorRT = null;
            this.depthRT = null;
        }
    }

    public override void Create()
    {
        this.copyDepthPass = new CopyDepthPass();
        this.renderPass = new ShrinkBufferPass(this.settings);

// #if UNITY_EDITOR
//         if (!UniversalRenderPipeline.asset.supportsCameraDepthTexture)
//         {
//             Debug.LogWarning("ShrinkBufferFeature require DepthTexture.");
//             UniversalRenderPipeline.asset.supportsCameraDepthTexture = true;
//         }
// #endif
    }
    
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType == CameraType.Game)
            renderer.EnqueuePass(this.copyDepthPass);
        renderer.EnqueuePass(this.renderPass);
    }
    
#if UNITY_SWITCH || UNITY_ANDROID
    const GraphicsFormat k_DepthStencilFormat = GraphicsFormat.D24_UNorm_S8_UInt;
    const int k_DepthBufferBits = 24;
#else
    const GraphicsFormat k_DepthStencilFormat = GraphicsFormat.D32_SFloat_S8_UInt;
    const int k_DepthBufferBits = 32;
#endif
    
    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        var descriptor = renderingData.cameraData.cameraTargetDescriptor;
        // Create RTHandle with RenderingUtils.ReAllocateIfNeeded
        descriptor.width = this.settings.resolution.x;
        descriptor.height = this.settings.resolution.y;
        var depthDesc = descriptor;
        //desc.msaaSamples = 1;// Depth-Only pass don't use MSAA
        
        // TODO fujioka: DepthPrepassの場合は_CameraDepthTextureを利用してコピーしない
        // //if ((requiresDepthPrepass && this.renderingModeActual != RenderingMode.Deferred) ||
        //if (!RenderingUtils.SupportsGraphicsFormat(GraphicsFormat.R32_SFloat, FormatUsage.Render))
        {
            depthDesc.graphicsFormat = GraphicsFormat.None;
            depthDesc.depthBufferBits = k_DepthBufferBits;
            depthDesc.depthStencilFormat = k_DepthStencilFormat;
        }
        // else
        // {
        //     depthDesc.graphicsFormat = GraphicsFormat.R32_SFloat;
        //     depthDesc.depthStencilFormat = GraphicsFormat.None;
        //     depthDesc.depthBufferBits = 0;
        // }
        RenderingUtils.ReAllocateIfNeeded(ref this.depthRTHandle, depthDesc, FilterMode.Point, TextureWrapMode.Clamp, false, 1, 0, this.settings.renderTargetName);
        descriptor.depthBufferBits = 0;
        RenderingUtils.ReAllocateIfNeeded(ref this.colorRTHandle, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, false, 1, 0, this.settings.renderTargetName);
        
        this.copyDepthPass.Setup(this.depthRTHandle);
        this.renderPass.Setup(this.colorRTHandle, this.depthRTHandle);
        //this.renderPass.Setup(this.colorRTHandle, renderingData.cameraData.renderer.cameraDepthTargetHandle);
    }

    protected override void Dispose(bool disposing)
    {
        // Use Dispose for cleanup

        // Release RTHandle
        this.colorRTHandle?.Release();
        this.depthRTHandle?.Release();
    }
}

