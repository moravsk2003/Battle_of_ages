using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace BattleOfAges.Player
{
    /// <summary>
    /// Draws the first-person arms (the Viewmodel layer) inside the main camera's own frame instead of with a second
    /// camera: after the world is drawn the depth is cleared, the projection is set to the arms' own field of view and
    /// the layer is drawn, then the projection is put back for what follows. A second camera in the stack cost close to
    /// a millisecond of every frame whatever it showed: its own culling of the whole scene, its own pass over every LOD
    /// group, its own shadow map and its own render graph. Drawn this way the arms are also shaded by the world's
    /// shadows (the overlay camera's shadow map held nothing but the arms).
    /// The renderers leave the Viewmodel layer out of their own passes (Opaque / Transparent Layer Mask), so any other
    /// camera that looks at the layer (captures) has it drawn here too, plainly, with its own projection.
    /// </summary>
    public sealed class ViewmodelPass : ScriptableRenderPass
    {
        /// <summary>The camera whose frame holds the arms, and the projection they are drawn with there.</summary>
        public static Camera Owner;
        public static float FieldOfView = 60f, Near = 0.01f, Far = 3f;

        private static ViewmodelPass instance;
        private static int layer = -1;
        private static readonly int ProjectionParams = Shader.PropertyToID("_ProjectionParams");
        private static readonly ShaderTagId[] Tags = { new ShaderTagId("UniversalForward"), new ShaderTagId("UniversalForwardOnly"), new ShaderTagId("SRPDefaultUnlit"), new ShaderTagId("LightweightForward") };
        private static readonly System.Collections.Generic.List<ShaderTagId> TagList = new System.Collections.Generic.List<ShaderTagId>(Tags);

        private sealed class PassData
        {
            public RendererListHandle List;
            public TextureHandle Color;
            public Matrix4x4 View, Projection, OwnProjection;
            public Vector2 Planes, OwnPlanes; // near, far
            public bool Own;
        }

        private ViewmodelPass()
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            profilingSampler = new ProfilingSampler("Viewmodel");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Owner = null; Ensure(); }

        /// <summary>The pass is offered to every camera that looks at the Viewmodel layer.</summary>
        public static void Ensure()
        {
            layer = LayerMask.NameToLayer(FpsViewmodelSetup.ViewmodelLayerName);
            RenderPipelineManager.beginCameraRendering -= Offer;
            RenderPipelineManager.beginCameraRendering += Offer;
        }

        private static void Offer(ScriptableRenderContext context, Camera camera)
        {
            if (layer < 0 || (camera.cullingMask & (1 << layer)) == 0) return;
            if (camera.cameraType != CameraType.Game) return;
            if (!camera.TryGetComponent(out UniversalAdditionalCameraData data) || data.scriptableRenderer == null) return;
            if (instance == null) instance = new ViewmodelPass();
            data.scriptableRenderer.EnqueuePass(instance);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();

            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(passName, out PassData data, profilingSampler))
            {
                DrawingSettings drawing = RenderingUtils.CreateDrawingSettings(TagList, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
                var filtering = new FilteringSettings(RenderQueueRange.all, 1 << layer);
                data.List = renderGraph.CreateRendererList(new RendererListParams(renderingData.cullResults, drawing, filtering));
                data.Color = resources.activeColorTexture;
                data.Own = Owner != null && cameraData.camera == Owner && !cameraData.camera.orthographic;
                data.View = cameraData.GetViewMatrix();
                data.Projection = cameraData.GetProjectionMatrix();
                data.OwnProjection = Matrix4x4.Perspective(FieldOfView, cameraData.camera.aspect, Near, Far);
                data.Planes = new Vector2(cameraData.camera.nearClipPlane, cameraData.camera.farClipPlane);
                data.OwnPlanes = new Vector2(Near, Far);

                builder.UseRendererList(data.List);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Write);
                if (resources.mainShadowsTexture.IsValid()) builder.UseTexture(resources.mainShadowsTexture, AccessFlags.Read);
                if (resources.additionalShadowsTexture.IsValid()) builder.UseTexture(resources.additionalShadowsTexture, AccessFlags.Read);
                if (resources.ssaoTexture.IsValid()) builder.UseTexture(resources.ssaoTexture, AccessFlags.Read);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                {
                    if (!d.Own) { context.cmd.DrawRendererList(d.List); return; }
                    bool flipped = context.GetTextureUVOrigin(d.Color) == TextureUVOrigin.BottomLeft;
                    context.cmd.ClearRenderTarget(true, false, Color.clear); // the arms never sink into a wall
                    // (the planes go with the projection: the fog reads the distance back from them, and with the world's
                    // planes the arms came out at the far end of it, all fog)
                    RenderingUtils.SetViewAndProjectionMatrices(context.cmd, d.View, GL.GetGPUProjectionMatrix(d.OwnProjection, flipped), false);
                    context.cmd.SetGlobalVector(ProjectionParams, new Vector4(flipped ? -1f : 1f, d.OwnPlanes.x, d.OwnPlanes.y, 1f / d.OwnPlanes.y));
                    context.cmd.DrawRendererList(d.List);
                    RenderingUtils.SetViewAndProjectionMatrices(context.cmd, d.View, GL.GetGPUProjectionMatrix(d.Projection, flipped), false);
                    context.cmd.SetGlobalVector(ProjectionParams, new Vector4(flipped ? -1f : 1f, d.Planes.x, d.Planes.y, 1f / d.Planes.y));
                });
            }
        }
    }
}
