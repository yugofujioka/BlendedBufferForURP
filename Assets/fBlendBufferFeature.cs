using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;

namespace FBlendedBuffer
{
    /// <summary>
    /// Blended Buffer for URP
    /// </summary>
    public class fBlendedBufferFeature : ScriptableRendererFeature
    {
        const string RENDER_TARGET_NAME = "_BlendedTarget";
        
        public enum DOWN_SAMPLING
        {
            X2 = 2,
            X4 = 4,
            X8 = 8,
            X16 = 16,
        }

        [System.Serializable]
        public class Settings
        {
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
            public DOWN_SAMPLING downSampling = DOWN_SAMPLING.X2; // to divide resolution
            public LayerMask layerMask = 0; // layer for VFX 
        }

        [SerializeField] Settings settings = new Settings();
        BlendedBufferPass blendedBufferPass;
        RTHandle colorRTHandle, depthRTHandle;

        class BlendedBufferPass : ScriptableRenderPass
        {
            static readonly List<ShaderTagId> SHADER_TAG_ID = new List<ShaderTagId>
            {
                new ShaderTagId("SRPDefaultUnlit"),
                new ShaderTagId("UniversalForward"),
            };

            RTHandle colorRT, depthRT;
            Material copyDepth, blitMaterial;
            readonly FilteringSettings filteringSettings;

            public BlendedBufferPass(Settings settings)
            {
                this.profilingSampler = new ProfilingSampler(nameof(BlendedBufferPass));
                this.renderPassEvent = settings.renderPassEvent;
                this.filteringSettings = new FilteringSettings(RenderQueueRange.transparent, settings.layerMask);
                
                var copyDepthShader = Shader.Find("Hidden/Universal Render Pipeline/CopyDepth");
                if (copyDepthShader != null)
                    this.copyDepth = new Material(copyDepthShader);
                
                var blitShader = Shader.Find("Custom/Blit");
                if (blitShader != null)
                    this.blitMaterial = new Material(blitShader);
            }

            // Called at SetupRenderPasses
            public void Setup(RTHandle colorRT, RTHandle depthRT)
            {
                this.colorRT = colorRT;
                this.depthRT = depthRT;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var isGameCamera = renderingData.cameraData.cameraType == CameraType.Game;
                var cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, this.profilingSampler))
                {
                    if (isGameCamera)
                    {
                        cmd.SetRenderTarget(this.colorRT, this.depthRT);
                        cmd.ClearRenderTarget(true, true, Color.clear);

                        // Blit DepthBuffer -> BlendedBuffer
                        if (this.depthRT.rt.graphicsFormat == GraphicsFormat.None)
                            cmd.EnableShaderKeyword("_OUTPUT_DEPTH");
                        else
                            cmd.DisableShaderKeyword("_OUTPUT_DEPTH");

                        var targetDepthRTHandle = renderingData.cameraData.renderer.cameraDepthTargetHandle;
                        var scaleBias = new Vector4(1f, 1f, 0f, 0f);
                        Blitter.BlitTexture(cmd, targetDepthRTHandle, scaleBias, this.copyDepth, 0);
                    }
                    else
                    {
                        // other cameras do not need to use BlendedBuffer(SceneView, Preview, etc...)
                        cmd.SetRenderTarget(renderingData.cameraData.renderer.cameraColorTargetHandle,
                                            renderingData.cameraData.renderer.cameraDepthTargetHandle);
                    }
                    
                    // Rendering by LayerMask(e.g. any VFX)
                    var drawSettings =
                        CreateDrawingSettings(SHADER_TAG_ID, ref renderingData, SortingCriteria.CommonTransparent);
                    drawSettings.perObjectData = PerObjectData.None;

                    // NOTE:
                    // 近景・遠景のVFXでLayerを分けて遠景の画面占有率が低いVFXに関しては直接バッファに書き込むアプローチもあるらしい
                    // https://game.watch.impress.co.jp/docs/20081203/3dmg4.htm
#if true
                    var param = new RendererListParams(renderingData.cullResults, drawSettings, this.filteringSettings);
                    var rl = context.CreateRendererList(ref param);
                    cmd.DrawRendererList(rl);
#else
                    // NOTE: DrawRenderers式なら先にExecuteCommandBufferしないといけない
                    context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filteringSettings);
#endif

                    if (isGameCamera)
                    {
                        // Blit BlendedBuffer -> CameraColorAttachment
                        var cameraColorTargetRTHandle = renderingData.cameraData.renderer.cameraColorTargetHandle;
                        Blitter.BlitCameraTexture(cmd,
                                                  this.colorRT, cameraColorTargetRTHandle,
                                                  RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store,
                                                  this.blitMaterial, 0);
                    }
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

            public void Dispose()
            {
                DestroyImmediate(this.copyDepth);
                DestroyImmediate(this.blitMaterial);
                this.copyDepth = this.blitMaterial = null;
            }
        }

        public override void Create()
        {
            this.name = "fBlendedBuffer";
            this.blendedBufferPass = new BlendedBufferPass(this.settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(this.blendedBufferPass);
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            // Create RTHandle with RenderingUtils.ReAllocateIfNeeded
            var downSampling = (int)this.settings.downSampling;
            desc.width = desc.width / downSampling;
            desc.height = desc.height / downSampling;
            desc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            
            var depthDesc = desc;
            depthDesc.msaaSamples = 1;// Depth-Only pass don't use MSAA
            depthDesc.graphicsFormat = GraphicsFormat.None; // DepthBufferとしてColorBufferにBindさせるにはR32ではダメ
            RenderingUtils.ReAllocateIfNeeded(ref this.depthRTHandle, depthDesc, FilterMode.Point,
                                              TextureWrapMode.Clamp, false, 1, 0, RENDER_TARGET_NAME);
            // must set 0 to use as DepthBuffer
            // automatically set stencilFormat when set depthBufferBits
            desc.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref this.colorRTHandle, desc, FilterMode.Point, // BilinearだとDepthとのEdgeは綺麗だが全体的にボケが強い
                                              TextureWrapMode.Clamp, false, 1, 0, RENDER_TARGET_NAME);

            this.blendedBufferPass.Setup(this.colorRTHandle, this.depthRTHandle);
        }

        protected override void Dispose(bool disposing)
        {
            this.blendedBufferPass.Dispose();
            this.colorRTHandle?.Release();
            this.depthRTHandle?.Release();
        }
    }
}
