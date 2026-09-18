using System;
using ClientPlugin.Shaders;
using SharpDX.DXGI;
using SharpDX.Direct3D11;
using VRage.Render.Scene;
using VRage.Render11.Common;
using VRage.Render11.Culling;
using VRage.Render11.Culling.Frustum;
using VRage.Render11.Scene;
using VRage.Render11.Resources;
using VRageMath;
using VRageRender;

namespace ClientPlugin.ShaderFramework;

/// <summary>
/// Anomaly-owned maps, rendered serially BEFORE Keen schedules its views. The
/// projection view IDs are scratch indices in this isolated batch, never extra
/// entries appended to Keen's fixed 19-view arrays. Uses both engine mesh paths
/// (including their depth materials), not receiver-screen masks or box proxies.
/// </summary>
internal static class DirectionalVolumeShadows
{
    // The shared renderer must explicitly enable this after all shaders and
    // composition/temporal products are ready. Registration alone does not run it.
    internal static bool Requested;
    internal const int Resolution = 2048;
    static readonly VolumetricGpuTimer Timer = new();
    internal static double GpuMilliseconds => Timer.LastMilliseconds;
    internal static readonly double[] ReceiverRadii = {128, 1000, 8000};
    internal static readonly MatrixD[] WorldToShadow = new MatrixD[3];
    internal static readonly IDepthTexture[] Maps = new IDepthTexture[3];
    static readonly MyCullQueries Queries = new();
    static uint validFrame;
    static bool valid;
    internal static string Status { get; private set; } = "Not requested";
    internal static bool IsValid(uint frame) => valid && validFrame == frame;

    internal static void RenderBeforeScheduler()
    {
        Timer.Poll(MyRender11.RC.DeviceContext);
        valid = false;
        if (!Requested || VolumetricMediumRegistry.Capture(out _).Length == 0) return;
        if (MyRender11.MultisamplingEnabled) { Status = "Unsupported MSAA"; return; }
        FrameTemporal.EnsureSnapshot();
        var sun = (Vector3D)FrameTemporal.SunToward;
        if (sun.LengthSquared() < 0.5) { Status = "Sun direction unavailable"; return; }
        sun.Normalize();
        var camera = MyRender11.Environment.Matrices.CameraPosition;
        bool culler = false, oldStarted = false, newStarted = false;
        try
        {
            Timer.Begin(MyRender11.RC.DeviceContext);
            for (int i = 0; i < 3; i++)
            {
                if (Maps[i] == null) Maps[i] = MyManagers.RwTextures.CreateDepth(
                    "Anomaly.VolumeSun." + i, Resolution, Resolution,
                    Format.R32_Typeless, Format.R32_Float, Format.D32_Float);
                // Include off-screen casters towards the light. Planet-body
                // occultation is evaluated analytically in the volume shader.
                var radius = ReceiverRadii[i];
                var up = Math.Abs(Vector3D.Dot(sun, Vector3D.Up)) > .95 ? Vector3D.Right : Vector3D.Up;
                var right = Vector3D.Normalize(Vector3D.Cross(up, sun));
                up = Vector3D.Normalize(Vector3D.Cross(sun, right));
                double texel = 2 * radius / Resolution;
                var center = camera;
                center += right * (Math.Round(Vector3D.Dot(center, right) / texel) * texel - Vector3D.Dot(center, right));
                center += up * (Math.Round(Vector3D.Dot(center, up) / texel) * texel - Vector3D.Dot(center, up));
                double reach = 32000 + radius;
                var eye = center + sun * reach;
                var view = MatrixD.CreateLookAt(eye, center, up);
                var projection = Matrix.CreateOrthographic((float)(radius*2), (float)(radius*2), 1, (float)(reach+radius));
                var world = view * (MatrixD)projection;
                WorldToShadow[i] = world;
                var local = MatrixD.CreateTranslation(camera) * world;
                Queries.AddDepthPass(new MyShadowmapQuery {
                    DepthBuffer = Maps[i], DepthBufferRo = Maps[i].DsvRo,
                    Viewport = new MyViewport(Resolution, Resolution),
                    ViewType = MyViewType.ShadowProjection, ViewIndex = i,
                    ProjectionInfo = new MyProjectionInfo {
                        WorldCameraOffsetPosition = camera, WorldToProjection = world,
                        LocalToProjection = local, LocalToProjectionExtruded = local,
                        Projection = projection, ViewOrigin = eye
                    }
                });
                MyRender11.RC.ClearDsv(Maps[i], DepthStencilClearFlags.Depth, 1, 0);
            }
            MyFrustumCuller.Init(Queries, MyScene11.DynamicRenderablesDBVH,
                MyScene11.DynamicRenderablesFarDBVH, MyScene11.ManualCullTree);
            culler = true;
            var old = MyManagers.GeometryRendererOld;
            var mesh = MyManagers.GeometryRenderer;
            old.InitFrame(); oldStarted = true;
            newStarted = true; mesh.InitFrame(Queries);
            for (int i = 0; i < Queries.Size; i++)
            {
                var query = Queries.CullQueries[i];
                MyFrustumCullingWork.ExecuteCulling(query);
                mesh.UpdateMatrices(query);
                mesh.UpdateLods(query);
                // Preprocess calls the singleton; invoke our exact batch's
                // preparation explicitly before the regular frame is initialized.
                mesh.Prepare(query);
                old.ProcessCullProxies(query);
                old.Prepare(query);
                mesh.Render(query);
                while (old.HasWork(query)) old.Render(query);
            }
            old.DoneFrame(); oldStarted = false;
            mesh.DoneFrame(); newStarted = false;
            validFrame = FrameTemporal.FrameIndex;
            valid = true;
            Status = "Three current-frame geometry maps (visual acceptance pending)";
        }
        catch (Exception e)
        {
            Status = "Unavailable: " + e.Message;
            VRage.Utils.MyLog.Default.WriteLine("Anomaly volume shadows: " + Status);
            if (RenderTrace.IsLostDevice(e)) throw;
        }
        finally
        {
            // These pools must be returned before Keen initializes its batch.
            if (oldStarted) MyManagers.GeometryRendererOld.DoneFrame();
            if (newStarted) MyManagers.GeometryRenderer.DoneFrame();
            if (culler) MyFrustumCuller.Done(Queries);
            Queries.Reset();
            Timer.End(MyRender11.RC.DeviceContext);
            MyRender11.RC.ClearState();
        }
    }

    internal static void Release()
    {
        valid = false;
        Requested = false;
        Timer.Dispose();
        for (int i = 0; i < Maps.Length; i++) MyManagers.RwTextures.DisposeTex(ref Maps[i]);
        Status = "Resources released";
    }
}
