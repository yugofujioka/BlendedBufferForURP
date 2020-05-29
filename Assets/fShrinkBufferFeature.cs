using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Shrink Buffer for URP
/// </summary>
public class fShrinkBufferFeature : ScriptableRendererFeature {
    [System.Serializable]
    public class Settings {
        public string passTag = "fShrinkBuffer";
        public RenderPassEvent Event = RenderPassEvent.AfterRenderingSkybox;
        public Vector2Int resolution = new Vector2Int(320, 180);
        public LayerMask LayerMask = 0;
        public Material blitMaterial = null;
    }

    public Settings settings = new Settings();
    private ShrinkBufferPass shrinkBufferPass = null;

    class ShrinkBufferPass : ScriptableRenderPass {

        private const string PASS_NAME = "ShrinkBuffer";
        private ProfilingSampler profilingSampler = new ProfilingSampler(PASS_NAME);
        private ShaderTagId SHADER_TAG_ID = new ShaderTagId("SRPDefaultUnlit");

        private Settings settings = null;
        private Material copyDepth = null;
        RenderTargetHandle tempBufferHandle;


        public ShrinkBufferPass(Settings settings) {
            this.settings = settings;
            this.renderPassEvent = this.settings.Event;
            this.tempBufferHandle.Init("_ShrinkBufferColor");

            var copyDepthShader = Shader.Find("Hidden/Universal Render Pipeline/CopyDepth");
			if (copyDepthShader != null)
	            this.copyDepth = new Material(copyDepthShader);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
            var cmd = CommandBufferPool.Get(PASS_NAME);

            using (new ProfilingScope(cmd, this.profilingSampler)) {
                // Create ShrinkBuffer
                cmd.Clear();
				if (UniversalRenderPipeline.asset.supportsHDR)
	                cmd.GetTemporaryRT(this.tempBufferHandle.id, this.settings.resolution.x, this.settings.resolution.y, 32, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
				else
	                cmd.GetTemporaryRT(this.tempBufferHandle.id, this.settings.resolution.x, this.settings.resolution.y, 32, FilterMode.Bilinear, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                cmd.SetRenderTarget(new RenderTargetIdentifier(this.tempBufferHandle.id));
                cmd.ClearRenderTarget(false, true, Color.black);
                cmd.Blit(this.depthAttachment, this.tempBufferHandle.id, this.copyDepth);
                context.ExecuteCommandBuffer(cmd);

                // Rendering by LayerMask(e.g. any VFX)
                var drawingSettings = CreateDrawingSettings(SHADER_TAG_ID, ref renderingData, SortingCriteria.CommonTransparent);
                var filteringSettings = new FilteringSettings(RenderQueueRange.transparent, this.settings.LayerMask);
                var renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings, ref renderStateBlock);

				// Combine ShrinkBuffer to ColorAttachment
                cmd.Clear();
                cmd.Blit(this.tempBufferHandle.id, this.colorAttachment, this.settings.blitMaterial);
                cmd.ReleaseTemporaryRT(this.tempBufferHandle.id);
                cmd.SetRenderTarget(this.colorAttachment, this.depthAttachment);
                context.ExecuteCommandBuffer(cmd);
            }

            CommandBufferPool.Release(cmd);
        }
    }

    public override void Create() {
        this.shrinkBufferPass = new ShrinkBufferPass(this.settings);

#if UNITY_EDITOR
		if (!UniversalRenderPipeline.asset.supportsCameraDepthTexture) {
			Debug.LogWarning("ShrinkBufferFeature require DepthTexture.");
			UniversalRenderPipeline.asset.supportsCameraDepthTexture = true;
		}
#endif
		}

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        this.shrinkBufferPass.ConfigureTarget(renderer.cameraColorTarget, renderer.cameraDepth);
        renderer.EnqueuePass(this.shrinkBufferPass);
    }
}

