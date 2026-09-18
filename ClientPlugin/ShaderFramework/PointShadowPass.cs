using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ClientPlugin.Buffers;
using ClientPlugin.Shaders;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using VRage.Library.Collections;
using VRage.Render.Scene;
using VRage.Render11.Common;
using VRage.Render11.Culling;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRage.Render11.Scene;
using VRage.Render11.Scene.Components;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Import;

namespace ClientPlugin.ShaderFramework;

/// <summary>
/// Request-driven occupancy clipmap and capped light-view depth atlas.
/// Catalog <c>occupancy</c> / <c>pointShadowAtlas</c>. Optional AABB boxes.
/// Local character mesh uses MeshDepth VS (VertexTemplateBase skin +
/// interpolated world, no Depth z-clamp). Default cube cap is 4; max is 64.
/// History depth lives on the linear ping-pong (<c>historyDepth</c>), not here.
/// </summary>
public static class PointShadowPass
{
    public const int DefaultLightCap = 4;
    public const int MaxLightCap = 64;
    public const int DefaultFaceRes = 128;
    public const int MinFaceRes = 32;
    public const int MaxFaceRes = 1024;
    public const int OccupancyDim = 64;
    public const float OccupancyVoxel = 2f;
    const int MaxBoxesPerLight = 48;
    const int MaxStampBoxes = 256;
    const int ConstantBytes = 256;
    const int BoxStride = 32;
    const float FarClear = 1e5f;
    const float MaxBoxExtent = 80f;
    const float CubeNear = 0.02f;
    const string SplatFile = "OccupancySplat.hlsl";
    const string StampFile = "OccupancyStamp.hlsl";
    const string BoxFile = "BoxDepth.hlsl";
    const string MeshFile = "MeshDepth.hlsl";

    static readonly object Gate = new();
    static readonly CatalogTexture OccupancyPublished = new();
    static readonly CatalogTexture AtlasPublished = new();
    static readonly CatalogTexture PlayerDepthPublished = new();
    static readonly List<CapturedLight> Captured = new();
    static readonly List<CapturedLight> ExecuteLights = new();
    static readonly HashSet<uint> PreviousLights = new();
    static readonly List<BoxGpu> StampBoxes = new();
    static readonly List<BoxGpu> LightBoxes = new();
    static readonly List<object> OverlapScratch = new();
    static readonly HashSet<uint> SeenActors = new();
    static readonly Dictionary<int, VertexShader> MeshVsByBundle = new();
    static readonly HashSet<int> MeshVsFailed = new();
    static readonly List<MyRenderableProxy> MeshProxyScratch = new();
    static readonly ShaderMacro[] MeshPsMacros = { new ShaderMacro("MESH_DEPTH_PS", null) };
    static readonly float[] HeaderScratch = new float[(MaxLightCap + 2) * 4];

    static int requestedCap;
    static int effectiveCap;
    static int requestedFace = DefaultFaceRes;
    static bool occupancyRequested;
    static bool worldBoxesRequested;
    static bool loggedError;
    static bool loggedMeshError;
    static bool loggedMeshDraw;
    static int lastMeshProxies;
    static int lastMeshDraws;
    static int lastPlayerDepthDraws;
    static string playerDepthState = "not run";
    static string meshState = "not run";

    public static string Status
    {
        get
        {
            lock (Gate)
                return "lights=" + ExecuteLights.Count + "/" + Captured.Count +
                    " selected=" + string.Join(",", ExecuteLights.ConvertAll(light => light.ActorId.ToString())) +
                    " cap=" + effectiveCap + "/" + requestedCap + " face=" + requestedFace +
                    " proxies=" + lastMeshProxies + " draws=" + lastMeshDraws +
                    " playerDepthDraws=" + lastPlayerDepthDraws + " mesh=" + meshState +
                    " playerDepth=" + playerDepthState;
        }
    }

    static ComputeShader splatCs;
    static ComputeShader stampCs;
    static VertexShader boxVs;
    static PixelShader boxPs;
    static PixelShader meshPs;
    static string meshPath;
    static IConstantBuffer occupancyCb;
    static IConstantBuffer faceCb;
    static IConstantBuffer meshProjectionCb;
    static ISrvBuffer boxBuffer;
    static IUavTexture occupancy;
    static IRtvTexture atlas;
    static IRtvTexture playerDepth;
    static BlendState minBlend;
    static int atlasW;
    static int atlasH;
    static int allocatedCap;
    static int allocatedFace;
    static bool shadersReady;

    [StructLayout(LayoutKind.Sequential, Size = ConstantBytes)]
    struct OccupancyConstants
    {
        public Vector2 InvSize;
        public Vector2 ProjScale;
        public Vector2 Jitter;
        public uint Dim;
        public uint HistoryValid;
        public Vector3 Origin;
        public float VoxelSize;
        public uint BoxCount;
        public uint Pad0;
        public uint Pad1;
        public uint Pad2;
        public Vector4 CameraToWorldR0;
        public Vector4 CameraToWorldR1;
        public Vector4 CameraToWorldR2;
        public Matrix PrevViewProj;
    }

    [StructLayout(LayoutKind.Sequential, Size = ConstantBytes)]
    struct FaceConstants
    {
        public Matrix ViewProj;
        public Matrix InvViewProj;
        public Vector3 LightPos;
        public float Range;
        public uint BoxCount;
        public float ViewportX;
        public float ViewportY;
        public float FaceRes;
    }

    [StructLayout(LayoutKind.Sequential, Size = BoxStride)]
    struct BoxGpu
    {
        public Vector3 Center;
        public float Pad0;
        public Vector3 Half;
        public float Pad1;
    }

    struct CapturedLight
    {
        public Vector3 ViewPos;
        public float Range;
        public Vector3D WorldPos;
        public uint ActorId;
        public MyScene Scene;
    }

    /// <summary>
    /// Sticky request for catalog <c>pointShadowAtlas</c>.
    /// <paramref name="maxLights"/> <c>&lt;= 0</c> skips the cube pass.
    /// VRAM grows with the requested cap, not <see cref="MaxLightCap"/>.
    /// </summary>
    public static void RequestPointShadows(int maxLights = DefaultLightCap, int faceResolution = DefaultFaceRes)
    {
        lock (Gate)
        {
            if (maxLights <= 0)
            {
                requestedCap = 0;
                return;
            }

            requestedCap = Math.Min(Math.Max(maxLights, 1), MaxLightCap);
            var face = faceResolution <= 0 ? DefaultFaceRes : faceResolution;
            requestedFace = Math.Min(Math.Max(face, MinFaceRes), MaxFaceRes);
        }
    }

    /// <summary>
    /// Sticky request for catalog <c>occupancy</c>. Pass false to clear.
    /// </summary>
    public static void RequestOccupancy(bool enabled = true)
    {
        lock (Gate)
            occupancyRequested = enabled;
    }

    /// <summary>
    /// Stamp actor AABB boxes into <c>pointShadowAtlas</c> in addition to
    /// the local character mesh. Off = mesh only (grates stay open). The
    /// local player AABB is never stamped.
    /// </summary>
    public static void RequestWorldBoxes(bool enabled = true)
    {
        lock (Gate)
            worldBoxesRequested = enabled;
    }

    internal static void CaptureLights(MyList<MyLightComponent> visibleLights)
    {
        lock (Gate)
        {
            Captured.Clear();
            if (visibleLights == null || !MyRender11.DebugOverrides.PointLights)
                return;
            var env = MyRender11.Environment?.Matrices;
            if (env == null)
                return;

            foreach (var light in visibleLights)
            {
                if (light == null)
                    continue;
                var data = light.Data;
                if (!data.PointLightOn || data.PointLight.Range < 0.05f)
                    continue;
                var owner = light.Owner;
                if (owner == null || owner.IsDestroyed)
                    continue;
                // Match MyLightComponent.WritePointlightConstants, including parent transforms.
                var world = owner.WorldMatrix.Translation + data.PointOffset * owner.WorldMatrix.Forward;
                var view = Vector3.Transform(world - env.CameraPosition, ref env.ViewAt0);
                Captured.Add(new CapturedLight
                {
                    ViewPos = view,
                    Range = data.PointLight.Range,
                    WorldPos = world,
                    ActorId = owner.ID,
                    Scene = owner.Scene
                });
            }
        }
    }

    internal static void Execute(MyRenderContext rc)
    {
        int cap;
        int face;
        bool wantOcc;
        lock (Gate)
        {
            cap = requestedCap;
            face = requestedFace;
            wantOcc = occupancyRequested;
        }

        if (rc == null || !rc.IsInitialized || (cap <= 0 && !wantOcc))
        {
            ClearCatalogs();
            return;
        }

        lock (Gate)
        {
            try
            {
                RenderTrace.Begin("PointShadows");
                ExecuteUnlocked(rc, cap, face, wantOcc);
            }
            catch (Exception e)
            {
                Fail("execute: " + e.GetType().Name + ": " + e.Message, e);
                ClearCatalogsUnlocked();
            }
            finally
            {
                RenderTrace.End("PointShadows");
            }
        }
    }

    internal static void Release()
    {
        lock (Gate)
        {
            ClearCatalogsUnlocked();
            DisposeTargets();
            DisposeShaders();
            occupancyRequested = false;
            worldBoxesRequested = false;
            requestedCap = 0;
        }
    }

    internal static void CollectWarmupJobs(List<ShaderWarmup.Job> jobs)
    {
        lock (Gate)
        {
            if (splatCs != null && stampCs != null && boxVs != null && boxPs != null && meshPs != null)
                return;
            ShaderWarmup.Add(jobs, FindHlsl(SplatFile), MyShaderProfile.cs_5_0, "Anomaly.OccupancySplat");
            ShaderWarmup.Add(jobs, FindHlsl(StampFile), MyShaderProfile.cs_5_0, "Anomaly.OccupancyStamp");
            ShaderWarmup.Add(jobs, FindHlsl(BoxFile), MyShaderProfile.vs_5_0, "Anomaly.BoxDepth.VS");
            ShaderWarmup.Add(jobs, FindHlsl(BoxFile), MyShaderProfile.ps_5_0, "Anomaly.BoxDepth.PS");
            ShaderWarmup.Add(jobs, FindHlsl(MeshFile), MyShaderProfile.ps_5_0, "Anomaly.MeshDepth.PS",
                MeshPsMacros);
        }
    }

    internal static void Prewarm()
    {
        lock (Gate)
            EnsureShaders();
    }

    static void ExecuteUnlocked(MyRenderContext rc, int cap, int face, bool wantOcc)
    {
        EnsureShaders();
        if (!shadersReady)
            return;

        PreviousLights.Clear();
        foreach (var previous in ExecuteLights) PreviousLights.Add(previous.ActorId);
        ExecuteLights.Clear();
        // D3D11 texture dimensions are limited to 16384. Reserve header row;
        // also keep this RGBA32F atlas within roughly 96 MiB.
        cap = Math.Min(cap, Math.Min(16383 / face, (96 * 1024 * 1024) / (6 * face * face * 16)));
        effectiveCap = cap;
        SelectLightsUnlocked(cap, ExecuteLights);
        StampBoxes.Clear();
        var skipActor = LocalCharacter.Copy().ActorId;
        for (var i = 0; i < ExecuteLights.Count; i++)
            CollectBoxes(ExecuteLights[i], StampBoxes, MaxStampBoxes, skipActor);

        if (wantOcc)
            FillOccupancy(rc, StampBoxes);
        else
        {
            OccupancyPublished.Clear();
            BufferCatalog.Set(BufferCatalog.Occupancy, null);
        }

        if (cap > 0)
        {
            FillAtlas(rc, face, ExecuteLights);
            FillPlayerDepth(rc);
        }
        else
        {
            AtlasPublished.Clear();
            PlayerDepthPublished.Clear();
            BufferCatalog.Set(BufferCatalog.PlayerDepth, null);
            BufferCatalog.Set(BufferCatalog.PointShadowAtlas, null);
        }
    }

    static void SelectLightsUnlocked(int cap, List<CapturedLight> dest)
    {
        dest.Clear();
        if (cap <= 0 || Captured.Count == 0)
            return;
        var cam = MyRender11.Environment?.Matrices?.CameraPosition ?? Vector3D.Zero;
        var character = MyIDTracker<MyActor>.FindByID(LocalCharacter.Copy().ActorId);
        var anchor = character != null ? character.WorldAabb.Center : cam;
        var order = new List<int>(Captured.Count);
        for (var i = 0; i < Captured.Count; i++)
            order.Add(i);
        order.Sort((a, b) =>
        {
            var da = Vector3D.DistanceSquared(Captured[a].WorldPos, anchor);
            var db = Vector3D.DistanceSquared(Captured[b].WorldPos, anchor);
            // Prefer lights that reach the caster. Retain an incumbent until
            // another is materially closer, independent of camera orbit.
            if (da > Captured[a].Range * Captured[a].Range) da += 1e12;
            if (db > Captured[b].Range * Captured[b].Range) db += 1e12;
            if (PreviousLights.Contains(Captured[a].ActorId)) da *= 0.81;
            if (PreviousLights.Contains(Captured[b].ActorId)) db *= 0.81;
            var comparison = da.CompareTo(db);
            return comparison != 0 ? comparison : Captured[a].ActorId.CompareTo(Captured[b].ActorId);
        });
        var n = Math.Min(cap, order.Count);
        for (var i = 0; i < n; i++)
            dest.Add(Captured[order[i]]);
    }

    static void CollectBoxes(CapturedLight light, List<BoxGpu> dest, int maxTotal, uint skipActor, Vector3D? origin = null)
    {
        if (dest.Count >= maxTotal || light.Scene == null)
            return;
        var sphere = new BoundingSphereD(light.WorldPos, light.Range);
        SeenActors.Clear();
        OverlapTree(light.Scene.DynamicRenderablesDBVH, ref sphere, light, dest, maxTotal, skipActor, origin);
        OverlapTree(light.Scene.DynamicRenderablesFarDBVH, ref sphere, light, dest, maxTotal, skipActor, origin);
        OverlapTree(light.Scene.ManualCullTree, ref sphere, light, dest, maxTotal, skipActor, origin);
    }

    static void OverlapTree(MyDynamicAABBTreeD tree, ref BoundingSphereD sphere, CapturedLight light,
        List<BoxGpu> dest, int maxTotal, uint skipActor, Vector3D? origin)
    {
        if (tree == null || dest.Count >= maxTotal)
            return;
        OverlapScratch.Clear();
        try
        {
            tree.OverlapAllBoundingSphere(ref sphere, OverlapScratch);
        }
        catch
        {
            return;
        }

        var cam = origin ?? MyRender11.Environment?.Matrices?.CameraPosition ?? Vector3D.Zero;
        var added = 0;
        for (var i = 0; i < OverlapScratch.Count && dest.Count < maxTotal && added < MaxBoxesPerLight; i++)
        {
            if (!TryGetActor(OverlapScratch[i], out var actor) || actor == null)
                continue;
            if (actor.ID == light.ActorId || actor.ID == skipActor || actor.IsDestroyed ||
                SeenActors.Contains(actor.ID))
                continue;
            if (actor.VolumeExtent > MaxBoxExtent)
                continue;
            var aabb = actor.WorldAabb;
            var half = (Vector3)(aabb.Max - aabb.Min) * 0.5f;
            if (half.Length() > MaxBoxExtent || half.LengthSquared() < 1e-4f)
                continue;
            SeenActors.Add(actor.ID);
            dest.Add(new BoxGpu
            {
                Center = (Vector3)(aabb.Center - cam),
                Half = half
            });
            added++;
        }
    }

    static bool TryGetActor(object item, out IMyActor actor)
    {
        actor = item as IMyActor;
        if (actor != null)
            return true;
        if (item is MyCullProxy proxy)
        {
            actor = proxy.Parent?.Owner;
            return actor != null;
        }

        if (item is MyRenderableComponent renderable)
        {
            actor = renderable.Owner;
            return actor != null;
        }

        var prop = item?.GetType().GetProperty("Owner", BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance);
        actor = prop?.GetValue(item) as IMyActor;
        return actor != null;
    }

    static void FillOccupancy(MyRenderContext rc, List<BoxGpu> boxes)
    {
        EnsureOccupancy();
        if (occupancy == null || splatCs == null)
            return;
        var linear = BufferCatalog.Active(BufferCatalog.LinearDepth);
        var depth = linear != null && linear.IsAvailable ? linear.Srv as ISrvBindable : null;
        if (depth == null)
        {
            OccupancyPublished.Clear();
            BufferCatalog.Set(BufferCatalog.Occupancy, null);
            return;
        }

        var w = Math.Max(linear.Width, 1);
        var h = Math.Max(linear.Height, 1);
        rc.ClearRtv(occupancy, default(RawColor4));
        var origin = OccupancyOrigin();
        var cb = OccupancyConstantsFrom(w, h, origin, (uint)boxes.Count);
        WriteCb(rc, occupancyCb, ref cb);

        rc.ComputeShader.Set(splatCs);
        rc.ComputeShader.SetConstantBuffer(0, occupancyCb);
        rc.ComputeShader.SetSrv(0, depth);
        rc.ComputeShader.SetSrv(1, null);
        rc.ComputeShader.SetUav(0, occupancy);
        rc.Dispatch((w + 7) / 8, (h + 7) / 8, 1);

        if (boxes.Count > 0 && stampCs != null && boxBuffer != null)
        {
            UploadBoxes(rc, boxes);
            rc.ComputeShader.Set(stampCs);
            rc.ComputeShader.SetSrv(0, boxBuffer);
            rc.Dispatch((boxes.Count + 63) / 64, 1, 1);
        }

        rc.ComputeShader.SetUav(0, null);
        rc.ComputeShader.SetSrv(0, null);
        rc.ComputeShader.Set(null);

        var native = occupancy.Resource != null ? occupancy.Resource.NativePointer : IntPtr.Zero;
        OccupancyPublished.Publish(occupancy, native, OccupancyDim * 8, OccupancyDim * 8);
        BufferCatalog.Set(BufferCatalog.Occupancy, OccupancyPublished);
    }

    static void FillAtlas(MyRenderContext rc, int face, List<CapturedLight> lights)
    {
        lastMeshProxies = lastMeshDraws = 0;
        meshState = lights.Count == 0 ? "no selected lights" : "not drawn";
        var cap = Math.Max(lights.Count, 1);
        EnsureAtlas(face, cap);
        if (atlas == null || boxVs == null || boxPs == null)
            return;

        rc.ClearState();
        rc.SetRtv(atlas);
        rc.ClearRtv(atlas, new RawColor4(FarClear, FarClear, FarClear, FarClear));

        if (minBlend == null)
            minBlend = CreateMinBlend();
        var ctx = rc.DeviceContext;
        var prevBlend = ctx?.OutputMerger.BlendState;
        if (ctx != null && minBlend != null)
            ctx.OutputMerger.SetBlendState(minBlend);

        rc.SetRasterizerState(MyRasterizerStateManager.NocullRasterizerState);
        rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
        rc.SetPrimitiveTopology(PrimitiveTopology.TriangleList);
        rc.GeometryShader.Set(null);

        var wantBoxes = worldBoxesRequested;
        var skipActor = LocalCharacter.Copy().ActorId;
        for (var i = 0; i < lights.Count; i++)
        {
            LightBoxes.Clear();
            if (wantBoxes)
                CollectBoxes(lights[i], LightBoxes, MaxBoxesPerLight, skipActor, lights[i].WorldPos);
            if (LightBoxes.Count > 0)
                UploadBoxes(rc, LightBoxes);

            for (var f = 0; f < 6; f++)
            {
                // Light-relative geometry and world-axis faces have no dependency
                // on the viewer. Subtract the light origin in double precision.
                var viewProj = CubeFaceViewProj(Vector3.Zero, f, lights[i].Range);
                Matrix.Invert(ref viewProj, out var invViewProj);
                var vx = f * allocatedFace;
                var vy = 1 + i * allocatedFace;
                var faceCbData = new FaceConstants
                {
                    ViewProj = viewProj,
                    InvViewProj = invViewProj,
                    LightPos = Vector3.Zero,
                    Range = lights[i].Range,
                    BoxCount = (uint)LightBoxes.Count,
                    ViewportX = vx,
                    ViewportY = vy,
                    FaceRes = allocatedFace
                };
                WriteCb(rc, faceCb, ref faceCbData);
                WriteMeshProjection(rc, viewProj);
                rc.SetViewport(vx, vy, allocatedFace, allocatedFace, 0f, 1f);
                if (LightBoxes.Count > 0)
                {
                    BindBoxPipeline(rc);
                    rc.Draw(36 * LightBoxes.Count, 0);
                }

                DrawLocalCharacterMesh(rc, lights[i].WorldPos);
            }
        }

        if (ctx != null)
            ctx.OutputMerger.SetBlendState(prevBlend);
        rc.SetRtvNull();
        WriteHeader(rc, lights);
        rc.ClearState();

        var native = atlas.Resource != null ? atlas.Resource.NativePointer : IntPtr.Zero;
        AtlasPublished.Publish(atlas, native, atlasW, atlasH);
        BufferCatalog.Set(BufferCatalog.PointShadowAtlas, AtlasPublished);
    }

    static void FillPlayerDepth(MyRenderContext rc)
    {
        var size = MyRender11.ResolutionI;
        var env = MyRender11.Environment?.Matrices;
        if (env == null || size.X <= 0 || size.Y <= 0)
        {
            PlayerDepthPublished.Clear();
            BufferCatalog.Set(BufferCatalog.PlayerDepth, null);
            return;
        }
        if (playerDepth == null || playerDepth.Size.X != size.X || playerDepth.Size.Y != size.Y)
        {
            if (playerDepth != null) MyManagers.RwTextures.DisposeTex(ref playerDepth);
            playerDepth = MyManagers.RwTextures.CreateRtv("Anomaly.PlayerDepth", size.X, size.Y, Format.R32_Float);
        }
        rc.ClearState();
        rc.SetRtv(playerDepth);
        rc.ClearRtv(playerDepth, new RawColor4(FarClear, FarClear, FarClear, FarClear));
        var constants = new FaceConstants { ViewProj = env.ViewProjectionAt0, LightPos = Vector3.Zero };
        WriteCb(rc, faceCb, ref constants);
        WriteMeshProjection(rc, env.ViewProjectionAt0);
        rc.SetViewport(0, 0, size.X, size.Y, 0, 1);
        rc.SetPrimitiveTopology(PrimitiveTopology.TriangleList);
        var previousBlend = rc.DeviceContext.OutputMerger.BlendState;
        rc.DeviceContext.OutputMerger.SetBlendState(minBlend);
        // Same skinned mesh, raster projection and frame as the main depth.
        // Store camera distance; contact compares it with scene depth before
        // excluding a sample, so walls in front of the player remain casters.
        var draws = lastMeshDraws;
        var atlasState = meshState;
        var atlasProxies = lastMeshProxies;
        DrawLocalCharacterMesh(rc, env.CameraPosition, true);
        lastPlayerDepthDraws = lastMeshDraws - draws;
        playerDepthState = meshState;
        meshState = atlasState;
        lastMeshProxies = atlasProxies;
        lastMeshDraws = draws;
        rc.DeviceContext.OutputMerger.SetBlendState(previousBlend);
        rc.SetRtvNull();
        rc.ClearState();
        PlayerDepthPublished.Publish(playerDepth, playerDepth.Resource.NativePointer, size.X, size.Y);
        BufferCatalog.Set(BufferCatalog.PlayerDepth, PlayerDepthPublished);
    }

    static void WriteMeshProjection(MyRenderContext rc, Matrix viewProj)
    {
        if (meshProjectionCb == null)
            return;
        var transposed = Matrix.Transpose(viewProj);
        WriteCb(rc, meshProjectionCb, ref transposed);
    }

    static void BindMeshProjection(MyRenderContext rc)
    {
        rc.VertexShader.SetConstantBuffer(MyCommon.FRAME_SLOT, MyCommon.FrameConstants);
        rc.VertexShader.SetConstantBuffer(MyCommon.PROJECTION_SLOT, meshProjectionCb);
        rc.PixelShader.SetConstantBuffer(0, faceCb);
    }

    static void BindBoxPipeline(MyRenderContext rc)
    {
        rc.SetInputLayout(null);
        rc.SetVertexBuffer(0, null);
        rc.SetIndexBuffer(null);
        rc.VertexShader.Set(boxVs);
        rc.PixelShader.Set(boxPs);
        rc.VertexShader.SetSrv(0, boxBuffer);
        rc.PixelShader.SetSrv(0, boxBuffer);
        rc.VertexShader.SetConstantBuffer(0, faceCb);
        rc.PixelShader.SetConstantBuffer(0, faceCb);
    }

    static void DrawLocalCharacterMesh(MyRenderContext rc, Vector3D origin, bool cameraMask = false)
    {
        if (meshPs == null || meshProjectionCb == null)
        {
            meshState = "shader/projection unavailable";
            return;
        }
        var snap = LocalCharacter.Copy();
        if (!snap.IsValid || snap.ActorId == 0)
        {
            meshState = "no controlled character";
            return;
        }

        try
        {
            var actor = MyIDTracker<MyActor>.FindByID(snap.ActorId);
            var renderable = actor?.GetRenderable();
            if (renderable == null)
            {
                meshState = "render actor unavailable";
                return;
            }
            CollectCharacterProxies(renderable, cameraMask);
            lastMeshProxies = MeshProxyScratch.Count;
            if (MeshProxyScratch.Count == 0)
            {
                meshState = "no drawable opaque proxies";
                return;
            }
            meshState = snap.IsFirstPerson ? "first person" : "third person";

            rc.VertexShader.SetSrv(0, null);
            rc.PixelShader.SetSrv(0, null);
            BindMeshProjection(rc);
            if (!loggedMeshDraw)
            {
                loggedMeshDraw = true;
                DebugLog.Write("PointShadowPass mesh stamp actor=" + snap.ActorId +
                    " fp=" + snap.IsFirstPerson + " proxies=" + MeshProxyScratch.Count);
            }

            // Inactive LOD proxies retain old camera-relative matrices. Never
            // use their cached transform for an independent shadow camera.
            var local = CharacterWorldRelativeTo(actor.WorldMatrix, origin);
            for (var p = 0; p < MeshProxyScratch.Count; p++)
                DrawCharacterProxy(rc, MeshProxyScratch[p], local);
        }
        catch (Exception e)
        {
            meshState = "draw failed: " + e.GetType().Name;
            if (!loggedMeshError)
            {
                loggedMeshError = true;
                Fail("mesh map: " + e.GetType().Name + ": " + e.Message, e);
            }
        }
    }

    static void CollectCharacterProxies(MyRenderableComponent renderable, bool cameraMask)
    {
        MeshProxyScratch.Clear();
        var lods = renderable.Lods;
        if (lods == null || lods.Length == 0)
            return;

        // One stable shadow LOD, independent of main-view LOD transitions.
        // All LOD proxies share the live skinning matrices. Fall back only
        // when an entire higher-detail LOD has no drawable opaque parts.
        var first = cameraMask ? Math.Min(Math.Max(renderable.CurrentLod, 0), lods.Length - 1) : 0;
        var end = cameraMask ? first + 1 : lods.Length;
        for (var i = first; i < end; i++)
        {
            var proxies = lods[i]?.RenderableProxies;
            if (proxies == null)
                continue;
            for (var p = 0; p < proxies.Length; p++)
            {
                var proxy = proxies[p];
                if (!AcceptCharacterProxy(proxy))
                    continue;
                // Reference identity avoids collisions between submesh keys.
                if (!MeshProxyScratch.Contains(proxy))
                    MeshProxyScratch.Add(proxy);
            }
            if (MeshProxyScratch.Count > 0)
                break;
        }
    }

    static bool AcceptCharacterProxy(MyRenderableProxy proxy)
    {
        if (proxy == null || proxy.DrawSubmesh.IndexCount <= 0)
            return false;
        if (proxy.DepthShaders == MyMaterialShadersBundleId.NULL)
            return false;
        if (proxy.Mesh == LodMeshId.NULL || proxy.ObjectBufferSize <= 0)
            return false;
        if (proxy.TransparentTechnique)
            return false;
        switch (proxy.Technique)
        {
            case MyMeshDrawTechnique.DECAL:
            case MyMeshDrawTechnique.DECAL_CUTOUT:
            case MyMeshDrawTechnique.DECAL_NOPREMULT:
            case MyMeshDrawTechnique.GLASS:
            case MyMeshDrawTechnique.HOLO:
            case MyMeshDrawTechnique.SHIELD:
            case MyMeshDrawTechnique.SHIELD_LIT:
            case MyMeshDrawTechnique.ATMOSPHERE:
            case MyMeshDrawTechnique.CLOUD_LAYER:
            case MyMeshDrawTechnique.FOLIAGE:
                return false;
        }

        // A shadow view is not the main camera. Include opaque head/body
        // parts hidden by first-person material flags without mutating them.
        return proxy.Mesh.Buffers.VB0 != null && proxy.Mesh.Buffers.IB != null;
    }

    static void DrawCharacterProxy(MyRenderContext rc, MyRenderableProxy proxy, Matrix local)
    {
        var buffers = proxy.Mesh.Buffers;
        if (buffers.VB0 == null || buffers.IB == null)
            return;
        var meshVs = GetMeshVs(proxy.DepthShaders);
        if (meshVs == null)
        {
            meshState = "vertex shader unavailable";
            return;
        }

        rc.SetInputLayout(proxy.DepthShaders.IL);
        rc.VertexShader.Set(meshVs);
        rc.PixelShader.Set(meshPs);
        rc.GeometryShader.Set(null);
        rc.SetRasterizerState(MyRasterizerStateManager.NocullRasterizerState);
        rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
        var objectCb = WriteCharacterObjectBuffer(rc, proxy, local);
        rc.VertexShader.SetConstantBuffer(MyCommon.OBJECT_SLOT, objectCb);
        rc.PixelShader.SetConstantBuffer(MyCommon.OBJECT_SLOT, objectCb);
        BindMeshProjection(rc);

        var instVb = proxy.InstancingEnabled ? proxy.Instancing.VB : null;
        rc.SetVertexBuffersFast(0, buffers.VB0, buffers.VB1, instVb);
        rc.SetIndexBuffer(buffers.IB);
        if (proxy.InstanceCount > 0)
        {
            rc.DrawIndexedInstanced(proxy.DrawSubmesh.IndexCount, proxy.InstanceCount,
                proxy.DrawSubmesh.StartIndex, proxy.DrawSubmesh.BaseVertex, proxy.StartInstance);
        }
        else
        {
            rc.DrawIndexed(proxy.DrawSubmesh.IndexCount, proxy.DrawSubmesh.StartIndex,
                proxy.DrawSubmesh.BaseVertex);
        }
        lastMeshDraws++;
    }

    static Matrix CharacterWorldRelativeTo(MatrixD world, Vector3D origin)
    {
        var local = new Matrix(world);
        local.Translation = (Vector3)(world.Translation - origin);
        return local;
    }

    static IConstantBuffer WriteCharacterObjectBuffer(MyRenderContext rc, MyRenderableProxy proxy, Matrix local)
    {
        // Mirror Keen's object layout and bone remapping, but patch a COPY of
        // the common data. Main-view proxies and their culling cache stay intact.
        var cb = proxy.GetObjectBuffer(rc);
        var mapping = MyMapping.MapDiscard(rc, cb);
        if (proxy.NonVoxelObjectData.IsValid)
            mapping.WriteAndPosition(ref proxy.NonVoxelObjectData);
        else if (proxy.VoxelCommonObjectData.IsValid)
            mapping.WriteAndPosition(ref proxy.VoxelCommonObjectData);
        var common = proxy.CommonObjectData;
        common.LocalMatrix = local;
        mapping.WriteAndPosition(ref common);
        if (proxy.SkinningMatrices != null)
        {
            var remap = proxy.DrawSubmesh.BonesMapping;
            if (remap == null)
                mapping.WriteAndPosition(proxy.SkinningMatrices, Math.Min(60, proxy.SkinningMatrices.Length));
            else
                for (var i = 0; i < remap.Length; i++)
                    mapping.WriteAndPosition(ref proxy.SkinningMatrices[remap[i]]);
        }
        mapping.Unmap();
        return cb;
    }

    static VertexShader GetMeshVs(MyMaterialShadersBundleId bundle)
    {
        var index = bundle.Index;
        if (index < 0)
            return null;
        VertexShader existing;
        if (MeshVsByBundle.TryGetValue(index, out existing))
            return existing;
        if (MeshVsFailed.Contains(index))
            return null;
        if (string.IsNullOrEmpty(meshPath) || meshPs == null)
            return null;

        try
        {
            var info = MyMaterialShaders.BundleInfo.Data[index];
            var macros = new List<ShaderMacro>
            {
                MyMaterialShaders.GetRenderingPassMacro(info.Pass.String)
            };
            MyMaterialShaders.AddMaterialShaderFlagMacrosTo(macros, info.Flags, info.TextureTypes);
            if (info.Layout.Index >= 0)
            {
                var layoutMacros = info.Layout.Info.Macros;
                if (layoutMacros != null && layoutMacros.Length != 0)
                    macros.AddRange(layoutMacros);
            }

            var bc = MyShaderCompiler.Compile(meshPath, macros.ToArray(), MyShaderProfile.vs_5_0,
                "Anomaly.MeshDepth.VS." + index, invalidateCache: false);
            if (Empty(bc))
            {
                MeshVsFailed.Add(index);
                DebugLog.Write("PointShadowPass MeshDepth VS empty bundle=" + index);
                return null;
            }

            var vs = new VertexShader(MyRender11.DeviceInstance, bc)
            {
                DebugName = "Anomaly.MeshDepth.VS." + index
            };
            MeshVsByBundle[index] = vs;
            return vs;
        }
        catch (Exception e)
        {
            MeshVsFailed.Add(index);
            if (!loggedMeshError)
            {
                loggedMeshError = true;
                Fail("mesh VS bundle=" + index + ": " + e.GetType().Name + ": " + e.Message, e);
            }

            return null;
        }
    }

    static void WriteHeader(MyRenderContext rc, List<CapturedLight> lights)
    {
        if (atlas?.Resource == null || rc.DeviceContext == null)
            return;
        Array.Clear(HeaderScratch, 0, HeaderScratch.Length);
        for (var i = 0; i < lights.Count && i < MaxLightCap; i++)
        {
            HeaderScratch[i * 4 + 0] = lights[i].ViewPos.X;
            HeaderScratch[i * 4 + 1] = lights[i].ViewPos.Y;
            HeaderScratch[i * 4 + 2] = lights[i].ViewPos.Z;
            HeaderScratch[i * 4 + 3] = lights[i].Range;
        }

        // Caster identity bounds are independent of light coverage. Contact
        // must never fall back to a second, partial player silhouette.
        var actor = MyIDTracker<MyActor>.FindByID(LocalCharacter.Copy().ActorId);
        if (actor != null)
        {
            var cam = MyRender11.Environment.Matrices.CameraPosition;
            var center = (Vector3)(actor.WorldAabb.Center - cam);
            var half = (Vector3)(actor.WorldAabb.Max - actor.WorldAabb.Min) * 0.5f;
            HeaderScratch[256] = center.X; HeaderScratch[257] = center.Y;
            HeaderScratch[258] = center.Z; HeaderScratch[259] = 1;
            HeaderScratch[260] = half.X; HeaderScratch[261] = half.Y;
            HeaderScratch[262] = half.Z;
            HeaderScratch[263] = lastMeshDraws >= lights.Count * 6 && lastMeshDraws > 0 &&
                (meshState == "first person" || meshState == "third person") ? 1 : 0;
        }
        var handle = GCHandle.Alloc(HeaderScratch, GCHandleType.Pinned);
        try
        {
            var box = new DataBox(handle.AddrOfPinnedObject(), (MaxLightCap + 2) * 16, 0);
            var region = new ResourceRegion(0, 0, 0, MaxLightCap + 2, 1, 1);
            rc.DeviceContext.UpdateSubresource(box, atlas.Resource, 0, region);
        }
        finally
        {
            handle.Free();
        }
    }

    static void UploadBoxes(MyRenderContext rc, List<BoxGpu> boxes)
    {
        EnsureBoxBuffer();
        if (boxBuffer == null)
            return;
        var mapping = MyMapping.MapDiscard(rc, boxBuffer);
        var empty = default(BoxGpu);
        var count = Math.Max(boxBuffer.ElementCount, 1);
        for (var i = 0; i < count; i++)
        {
            if (i < boxes.Count)
            {
                var box = boxes[i];
                mapping.WriteAndPosition(ref box);
            }
            else
                mapping.WriteAndPosition(ref empty);
        }

        mapping.Unmap();
    }

    static OccupancyConstants OccupancyConstantsFrom(int w, int h, Vector3 origin, uint boxCount)
    {
        return new OccupancyConstants
        {
            InvSize = new Vector2(1f / w, 1f / h),
            ProjScale = FrameTemporal.ProjScale,
            Jitter = new Vector2(FrameTemporal.JitterX, FrameTemporal.JitterY),
            Dim = OccupancyDim,
            HistoryValid = FrameTemporal.HistoryValid ? 1u : 0u,
            Origin = origin,
            VoxelSize = OccupancyVoxel,
            BoxCount = boxCount,
            CameraToWorldR0 = FrameTemporal.CameraToWorldRow(0),
            CameraToWorldR1 = FrameTemporal.CameraToWorldRow(1),
            CameraToWorldR2 = FrameTemporal.CameraToWorldRow(2),
            PrevViewProj = FrameTemporal.PrevViewProj
        };
    }

    static Vector3 OccupancyOrigin()
    {
        var half = OccupancyDim * OccupancyVoxel * 0.5f;
        return new Vector3(-half, -half, -half);
    }

    static Matrix CubeFaceViewProj(Vector3 light, int face, float range)
    {
        Vector3 fwd;
        Vector3 up;
        switch (face)
        {
            case 0:
                fwd = Vector3.Right;
                up = Vector3.Down;
                break;
            case 1:
                fwd = Vector3.Left;
                up = Vector3.Down;
                break;
            case 2:
                fwd = Vector3.Up;
                up = Vector3.Backward;
                break;
            case 3:
                fwd = Vector3.Down;
                up = Vector3.Forward;
                break;
            case 4:
                fwd = Vector3.Backward;
                up = Vector3.Down;
                break;
            default:
                fwd = Vector3.Forward;
                up = Vector3.Down;
                break;
        }

        var view = Matrix.CreateLookAt(light, light + fwd, up);
        var far = Math.Max(range, 0.25f);
        var proj = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2, 1f, CubeNear, far);
        return view * proj;
    }

    static BlendState CreateMinBlend()
    {
        var device = MyRender11.DeviceInstance;
        if (device == null)
            return null;
        var desc = new BlendStateDescription
        {
            AlphaToCoverageEnable = false,
            IndependentBlendEnable = false
        };
        desc.RenderTarget[0] = new RenderTargetBlendDescription
        {
            IsBlendEnabled = true,
            SourceBlend = BlendOption.One,
            DestinationBlend = BlendOption.One,
            BlendOperation = BlendOperation.Minimum,
            SourceAlphaBlend = BlendOption.One,
            DestinationAlphaBlend = BlendOption.One,
            AlphaBlendOperation = BlendOperation.Minimum,
            RenderTargetWriteMask = ColorWriteMaskFlags.All
        };
        return new BlendState(device, desc) { DebugName = "Anomaly.PointShadow.Min" };
    }

    static void WriteCb<T>(MyRenderContext rc, IConstantBuffer cb, ref T data) where T : struct
    {
        if (cb == null)
            return;
        var mapping = MyMapping.MapDiscard(rc, cb);
        mapping.WriteAndPosition(ref data);
        mapping.Unmap();
    }

    static void EnsureShaders()
    {
        if (shadersReady)
            return;
        var splatPath = FindHlsl(SplatFile);
        var stampPath = FindHlsl(StampFile);
        var boxPath = FindHlsl(BoxFile);
        meshPath = FindHlsl(MeshFile);
        if (splatPath == null || stampPath == null || boxPath == null)
        {
            Fail("HLSL not found (OccupancySplat / OccupancyStamp / BoxDepth)", null);
            return;
        }

        var splatBc = MyShaderCompiler.Compile(splatPath, Array.Empty<ShaderMacro>(), MyShaderProfile.cs_5_0,
            "Anomaly.OccupancySplat", invalidateCache: false);
        var stampBc = MyShaderCompiler.Compile(stampPath, Array.Empty<ShaderMacro>(), MyShaderProfile.cs_5_0,
            "Anomaly.OccupancyStamp", invalidateCache: false);
        var vsBc = MyShaderCompiler.Compile(boxPath, Array.Empty<ShaderMacro>(), MyShaderProfile.vs_5_0,
            "Anomaly.BoxDepth.VS", invalidateCache: false);
        var psBc = MyShaderCompiler.Compile(boxPath, Array.Empty<ShaderMacro>(), MyShaderProfile.ps_5_0,
            "Anomaly.BoxDepth.PS", invalidateCache: false);
        byte[] meshBc = null;
        if (meshPath != null)
        {
            meshBc = MyShaderCompiler.Compile(meshPath, MeshPsMacros, MyShaderProfile.ps_5_0,
                "Anomaly.MeshDepth.PS", invalidateCache: false);
        }

        if (Empty(splatBc) || Empty(stampBc) || Empty(vsBc) || Empty(psBc))
        {
            Fail("point-shadow shader compile returned empty bytecode", null);
            return;
        }

        var device = MyRender11.DeviceInstance;
        var compiledMeshPath = meshPath;
        DisposeShadersOnly();
        meshPath = compiledMeshPath;
        splatCs = new ComputeShader(device, splatBc) { DebugName = "Anomaly.OccupancySplat" };
        stampCs = new ComputeShader(device, stampBc) { DebugName = "Anomaly.OccupancyStamp" };
        boxVs = new VertexShader(device, vsBc) { DebugName = "Anomaly.BoxDepth.VS" };
        boxPs = new PixelShader(device, psBc) { DebugName = "Anomaly.BoxDepth.PS" };
        if (!Empty(meshBc))
            meshPs = new PixelShader(device, meshBc) { DebugName = "Anomaly.MeshDepth.PS" };
        else
            DebugLog.Write("PointShadowPass MeshDepth.hlsl missing or empty — character mesh maps off");
        occupancyCb ??= MyManagers.Buffers.CreateConstantBuffer("Anomaly.OccupancyCB", ConstantBytes,
            usage: ResourceUsage.Dynamic);
        faceCb ??= MyManagers.Buffers.CreateConstantBuffer("Anomaly.PointShadowFaceCB", ConstantBytes,
            usage: ResourceUsage.Dynamic);
        // Never map Keen's shared projection buffer: transparency/occlusion
        // workers also record writes to it while this immediate pass runs.
        meshProjectionCb ??= MyManagers.Buffers.CreateConstantBuffer("Anomaly.PointShadowProjectionCB", 64,
            usage: ResourceUsage.Dynamic);
        shadersReady = true;
        MyLog.Default.WriteLine("Anomaly point-shadow shaders compiled.");
    }

    static void EnsureOccupancy()
    {
        if (occupancy != null)
            return;
        occupancy = MyManagers.RwTextures.CreateUav("Anomaly.Occupancy", OccupancyDim * 8, OccupancyDim * 8,
            Format.R8_UNorm);
    }

    static void EnsureAtlas(int face, int cap)
    {
        var w = face * 6;
        var h = 1 + face * cap;
        if (atlas != null && allocatedFace == face && allocatedCap >= cap && atlasW == w && atlasH >= h)
            return;
        if (atlas != null)
            MyManagers.RwTextures.DisposeTex(ref atlas);
        atlas = MyManagers.RwTextures.CreateRtv("Anomaly.PointShadowAtlas", w, h, Format.R32G32B32A32_Float);
        atlasW = w;
        atlasH = h;
        allocatedFace = face;
        allocatedCap = cap;
    }

    static void EnsureBoxBuffer()
    {
        if (boxBuffer != null)
            return;
        boxBuffer = MyManagers.Buffers.CreateSrv("Anomaly.PointShadowBoxes", MaxStampBoxes, BoxStride, null,
            ResourceUsage.Dynamic, isGlobal: true);
    }

    static void ClearCatalogs()
    {
        lock (Gate)
            ClearCatalogsUnlocked();
    }

    static void ClearCatalogsUnlocked()
    {
        OccupancyPublished.Clear();
        AtlasPublished.Clear();
        PlayerDepthPublished.Clear();
        BufferCatalog.Set(BufferCatalog.PlayerDepth, null);
        BufferCatalog.Set(BufferCatalog.Occupancy, null);
        BufferCatalog.Set(BufferCatalog.PointShadowAtlas, null);
    }

    static void DisposeTargets()
    {
        PlayerDepthPublished.Clear();
        BufferCatalog.Set(BufferCatalog.PlayerDepth, null);
        if (playerDepth != null) MyManagers.RwTextures.DisposeTex(ref playerDepth);
        if (occupancy != null)
            MyManagers.RwTextures.DisposeTex(ref occupancy);
        occupancy = null;
        if (atlas != null)
            MyManagers.RwTextures.DisposeTex(ref atlas);
        atlas = null;
        atlasW = atlasH = allocatedCap = allocatedFace = 0;
        if (boxBuffer != null)
        {
            MyManagers.Buffers.Dispose(boxBuffer);
            boxBuffer = null;
        }
    }

    static void DisposeShaders()
    {
        DisposeShadersOnly();
        if (occupancyCb != null)
        {
            MyManagers.Buffers.Dispose(new[] { occupancyCb });
            occupancyCb = null;
        }

        if (faceCb != null)
        {
            MyManagers.Buffers.Dispose(new[] { faceCb });
            faceCb = null;
        }

        if (meshProjectionCb != null)
        {
            MyManagers.Buffers.Dispose(new[] { meshProjectionCb });
            meshProjectionCb = null;
        }

        minBlend?.Dispose();
        minBlend = null;
        meshPath = null;
        shadersReady = false;
        loggedMeshDraw = false;
    }

    static void DisposeShadersOnly()
    {
        splatCs?.Dispose();
        stampCs?.Dispose();
        boxVs?.Dispose();
        boxPs?.Dispose();
        meshPs?.Dispose();
        foreach (var vs in MeshVsByBundle.Values)
            vs?.Dispose();
        MeshVsByBundle.Clear();
        MeshVsFailed.Clear();
        splatCs = null;
        stampCs = null;
        boxVs = null;
        boxPs = null;
        meshPs = null;
    }

    static void Fail(string message, Exception e)
    {
        RenderTrace.DumpIfLost("PointShadows", e);
        if (loggedError)
            return;
        loggedError = true;
        MyLog.Default.WriteLine("Anomaly point shadows: " + message);
        DebugLog.Write("PointShadowPass " + message + (e != null ? "\n" + e : ""));
    }

    static bool Empty(byte[] bc) => bc == null || bc.Length == 0;

    static string FindHlsl(string fileName)
    {
        if (ShaderPackRegistry.TryResolveOverlay(fileName, out var overlay) && File.Exists(overlay))
            return overlay;
        if (!string.IsNullOrEmpty(ShaderCompileIntercept.IncludeDirectory))
        {
            var path = Path.Combine(ShaderCompileIntercept.IncludeDirectory, fileName);
            if (File.Exists(path))
                return Path.GetFullPath(path);
        }

        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrEmpty(asmDir))
        {
            var path = Path.Combine(asmDir, "Shaders", fileName);
            if (File.Exists(path))
                return Path.GetFullPath(path);
        }

        return null;
    }
}
