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
internal static class FarSunShadows
{
    // The shared renderer must explicitly enable this after all shaders and
    // composition/temporal products are ready. Registration alone does not run it.
    internal static bool Requested => RayScene.HasEnabledFrames;
    internal const int Resolution = 2048;
    static readonly VolumetricGpuTimer Timer = new();
    internal static double GpuMilliseconds => Requested ? Timer.LastMilliseconds : 0;
    internal static object[] Product(uint frame, Vector3D camera) => new object[] { 1, frame, IsValid(frame) ? Maps[0].Srv : null,
        RayScene.MatrixArray((Matrix)(MatrixD.CreateTranslation(camera) * WorldToShadow[0])), GpuMilliseconds, Timer.Measurements, Status };
    internal static readonly double[] ReceiverRadii = {256};
    internal static readonly MatrixD[] WorldToShadow = new MatrixD[1];
    internal static readonly IDepthTexture[] Maps = new IDepthTexture[1];
    static readonly MyCullQueries Queries = new();
    static uint validFrame;
    static bool valid;
    internal static string Status { get; private set; } = "Not requested";
    internal static bool IsValid(uint frame) => valid && validFrame == frame;

    internal static void RenderBeforeScheduler()
    {
        Timer.Poll(MyRender11.RC.DeviceContext);
        valid = false;
        if (!Requested) { for(int i=0;i<Maps.Length;i++)MyManagers.RwTextures.DisposeTex(ref Maps[i]); Status="Not requested"; return; }
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
            for (int i = 0; i < 1; i++)
            {
                if (Maps[i] == null) Maps[i] = MyManagers.RwTextures.CreateDepth(
                    "Anomaly.FarSun." + i, Resolution, Resolution,
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
                if (!FarSunClip.Prepare(local)) { Status = FarSunClip.Status; return; }
                FarSunClip.Begin();
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
            valid = FarSunClip.Complete;
            Status = valid ? "Far-only geometry coverage" : "Depth clip variants pending or unavailable";
        }
        catch (Exception e)
        {
            Status = "Unavailable: " + e.Message;
            VRage.Utils.MyLog.Default.WriteLine("Anomaly far sun: " + Status);
            if (RenderTrace.IsLostDevice(e)) throw;
        }
        finally
        {
            // These pools must be returned before Keen initializes its batch.
            if (oldStarted) MyManagers.GeometryRendererOld.DoneFrame();
            if (newStarted) MyManagers.GeometryRenderer.DoneFrame();
            if (culler) MyFrustumCuller.Done(Queries);
            FarSunClip.End();
            Queries.Reset();
            Timer.End(MyRender11.RC.DeviceContext);
            MyRender11.RC.ClearState();
        }
    }

    internal static void Release()
    {
        valid = false;
        FarSunClip.Release();
        Timer.Dispose();
        for (int i = 0; i < Maps.Length; i++) MyManagers.RwTextures.DisposeTex(ref Maps[i]);
        Status = "Resources released";
    }
}

