using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URPで縮小バッファ v1
/// </summary>
public class ShrinkBufferFeature : ScriptableRendererFeature {
    [System.Serializable]
    public class Settings {
        public string passTag = "ShrinkBuffer";
        public RenderPassEvent Event = RenderPassEvent.AfterRenderingSkybox;
        public Vector2Int resolution = new Vector2Int(320, 180);
        public LayerMask LayerMask = 0;
        public Material blitMaterial = null;
    }

    public Settings settings = new Settings();
    private CustomRenderPass scriptablePass;

    class CustomRenderPass : ScriptableRenderPass {

        private const string PASS_NAME = "ShrinkBuffer";
        private ProfilingSampler profilingSampler = new ProfilingSampler(PASS_NAME);
        private ShaderTagId SHADER_TAG_ID = new ShaderTagId("SRPDefaultUnlit");

        public Settings settings = null;
        private Material copyDepth = null;
        RenderTargetHandle tempBufferHandle, depthBufferHandle;


        public CustomRenderPass(Settings settings) {
            this.settings = settings;
            this.renderPassEvent = this.settings.Event;

            this.tempBufferHandle.Init("_ShrinkBufferColor");
            this.depthBufferHandle.Init("_ShrinkBufferDepth");

            Shader copyDepthShader = Shader.Find("Hidden/Universal Render Pipeline/CopyDepth");
            this.copyDepth = new Material(copyDepthShader);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
            CommandBuffer cmd = CommandBufferPool.Get(PASS_NAME);

            using (new ProfilingScope(cmd, this.profilingSampler)) {
                // 縮小バッファ生成
                cmd.Clear();
                cmd.GetTemporaryRT(this.tempBufferHandle.id, this.settings.resolution.x, this.settings.resolution.y, 0, FilterMode.Bilinear, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                // note: ColorBufferとDepthBufferのサイズを合わせないといけないのでコピーする
                //       直接DepthBufferを参照しているはずだがDepthTextureを有効（_CameraDepthTextureを用意）しないとダメ…？
                cmd.GetTemporaryRT(this.depthBufferHandle.id, this.settings.resolution.x, this.settings.resolution.y, 32, FilterMode.Point, RenderTextureFormat.Depth);
                cmd.Blit(this.depthAttachment, this.depthBufferHandle.id, this.copyDepth);
                cmd.SetRenderTarget(new RenderTargetIdentifier(this.tempBufferHandle.id), new RenderTargetIdentifier(this.depthBufferHandle.id));
                cmd.ClearRenderTarget(false, true, Color.black);

                // RenderTarget切り換えの為一度実行
                context.ExecuteCommandBuffer(cmd);

                // LayerMaskで描画
                var drawingSettings = CreateDrawingSettings(SHADER_TAG_ID, ref renderingData, SortingCriteria.CommonTransparent);
                var filteringSettings = new FilteringSettings(RenderQueueRange.transparent, this.settings.LayerMask);
                var renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings, ref renderStateBlock);

                // 合成
                cmd.Clear();
                cmd.Blit(this.tempBufferHandle.id, this.colorAttachment, this.settings.blitMaterial);
                //cmd.Blit(DEPTH_COPY, this.colorAttachment); // Depthバッファ確認
                cmd.ReleaseTemporaryRT(this.tempBufferHandle.id);
                cmd.ReleaseTemporaryRT(this.depthBufferHandle.id);
                cmd.SetRenderTarget(this.colorAttachment, this.depthAttachment);
                context.ExecuteCommandBuffer(cmd);
            }

            CommandBufferPool.Release(cmd);
        }
    }

    public override void Create() {
        this.scriptablePass = new CustomRenderPass(this.settings);

#if UNITY_EDITOR
		if (!UniversalRenderPipeline.asset.supportsCameraDepthTexture) {
			Debug.LogWarning("ShrinkBufferFeature require DepthTexture.");
			UniversalRenderPipeline.asset.supportsCameraDepthTexture = true;
		}
#endif
		}

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        this.scriptablePass.ConfigureTarget(renderer.cameraColorTarget, renderer.cameraDepth);
        renderer.EnqueuePass(this.scriptablePass);
    }
}

