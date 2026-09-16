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
using VRage.Render11.Scene.Components;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace ClientPlugin.ShaderFramework;

/// <summary>
/// Request-driven occupancy clipmap and capped light-view AABB depth atlas.
/// Catalog <c>occupancy</c> / <c>pointShadowAtlas</c>. Default cube cap is 4;
/// max is 64. History depth lives on the linear ping-pong
/// (<c>historyDepth</c>), not here.
/// </summary>
public static class PointShadowPass
{
    public const int DefaultLightCap = 4;
    public const int MaxLightCap = 64;
    public const int DefaultFaceRes = 64;
    public const int MinFaceRes = 32;
    public const int MaxFaceRes = 128;
    public const int OccupancyDim = 64;
    public const float OccupancyVoxel = 2f;
    const int MaxBoxesPerLight = 48;
    const int MaxStampBoxes = 256;
    const int ConstantBytes = 256;
    const int BoxStride = 32;
    const float FarClear = 1e5f;
    const float MaxBoxExtent = 80f;
    const string SplatFile = "OccupancySplat.hlsl";
    const string StampFile = "OccupancyStamp.hlsl";
    const string BoxFile = "BoxDepth.hlsl";

    static readonly object Gate = new();
    static readonly CatalogTexture OccupancyPublished = new();
    static readonly CatalogTexture AtlasPublished = new();
    static readonly List<CapturedLight> Captured = new();
    static readonly List<CapturedLight> ExecuteLights = new();
    static readonly List<BoxGpu> StampBoxes = new();
    static readonly List<BoxGpu> LightBoxes = new();
    static readonly List<object> OverlapScratch = new();
    static readonly HashSet<uint> SeenActors = new();
    static readonly float[] HeaderScratch = new float[MaxLightCap * 4];

    static int requestedCap;
    static int requestedFace = DefaultFaceRes;
    static bool occupancyRequested;
    static bool loggedError;

    static ComputeShader splatCs;
    static ComputeShader stampCs;
    static VertexShader boxVs;
    static PixelShader boxPs;
    static IConstantBuffer occupancyCb;
    static IConstantBuffer faceCb;
    static ISrvBuffer boxBuffer;
    static IUavTexture occupancy;
    static IRtvTexture atlas;
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
        public Vector3 LightPos;
        public float Range;
        public uint BoxCount;
        public uint Pad0;
        public uint Pad1;
        public uint Pad2;
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
                var world = data.Position + data.PointOffset * owner.WorldMatrix.Forward;
                var view = Vector3.Transform((Vector3)(world - env.CameraPosition), ref env.ViewAt0);
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
            requestedCap = 0;
        }
    }

    internal static void CollectWarmupJobs(List<ShaderWarmup.Job> jobs)
    {
        lock (Gate)
        {
            if (splatCs != null && stampCs != null && boxVs != null && boxPs != null)
                return;
            ShaderWarmup.Add(jobs, FindHlsl(SplatFile), MyShaderProfile.cs_5_0, "Anomaly.OccupancySplat");
            ShaderWarmup.Add(jobs, FindHlsl(StampFile), MyShaderProfile.cs_5_0, "Anomaly.OccupancyStamp");
            ShaderWarmup.Add(jobs, FindHlsl(BoxFile), MyShaderProfile.vs_5_0, "Anomaly.BoxDepth.VS");
            ShaderWarmup.Add(jobs, FindHlsl(BoxFile), MyShaderProfile.ps_5_0, "Anomaly.BoxDepth.PS");
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

        ExecuteLights.Clear();
        SelectLightsUnlocked(cap, ExecuteLights);
        StampBoxes.Clear();
        for (var i = 0; i < ExecuteLights.Count; i++)
            CollectBoxes(ExecuteLights[i], StampBoxes, MaxStampBoxes);

        if (wantOcc)
            FillOccupancy(rc, StampBoxes);
        else
        {
            OccupancyPublished.Clear();
            BufferCatalog.Set(BufferCatalog.Occupancy, null);
        }

        if (cap > 0)
            FillAtlas(rc, face, ExecuteLights);
        else
        {
            AtlasPublished.Clear();
            BufferCatalog.Set(BufferCatalog.PointShadowAtlas, null);
        }
    }

    static void SelectLightsUnlocked(int cap, List<CapturedLight> dest)
    {
        dest.Clear();
        if (cap <= 0 || Captured.Count == 0)
            return;
        var cam = MyRender11.Environment?.Matrices?.CameraPosition ?? Vector3D.Zero;
        var order = new List<int>(Captured.Count);
        for (var i = 0; i < Captured.Count; i++)
            order.Add(i);
        order.Sort((a, b) =>
        {
            var da = Vector3D.DistanceSquared(Captured[a].WorldPos, cam);
            var db = Vector3D.DistanceSquared(Captured[b].WorldPos, cam);
            return da.CompareTo(db);
        });
        var n = Math.Min(cap, order.Count);
        for (var i = 0; i < n; i++)
            dest.Add(Captured[order[i]]);
    }

    static void CollectBoxes(CapturedLight light, List<BoxGpu> dest, int maxTotal)
    {
        if (dest.Count >= maxTotal || light.Scene == null)
            return;
        var sphere = new BoundingSphereD(light.WorldPos, light.Range);
        SeenActors.Clear();
        OverlapTree(light.Scene.DynamicRenderablesDBVH, ref sphere, light, dest, maxTotal);
        OverlapTree(light.Scene.DynamicRenderablesFarDBVH, ref sphere, light, dest, maxTotal);
        OverlapTree(light.Scene.ManualCullTree, ref sphere, light, dest, maxTotal);
    }

    static void OverlapTree(MyDynamicAABBTreeD tree, ref BoundingSphereD sphere, CapturedLight light,
        List<BoxGpu> dest, int maxTotal)
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

        var cam = MyRender11.Environment?.Matrices?.CameraPosition ?? Vector3D.Zero;
        var added = 0;
        for (var i = 0; i < OverlapScratch.Count && dest.Count < maxTotal && added < MaxBoxesPerLight; i++)
        {
            if (!TryGetActor(OverlapScratch[i], out var actor) || actor == null)
                continue;
            if (actor.ID == light.ActorId || actor.IsDestroyed || SeenActors.Contains(actor.ID))
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
        var cap = Math.Max(lights.Count, 1);
        EnsureAtlas(face, cap);
        if (atlas == null || boxVs == null || boxPs == null)
            return;

        rc.SetRtv(atlas);
        rc.ClearRtv(atlas, new RawColor4(FarClear, FarClear, FarClear, FarClear));
        WriteHeader(rc, lights);

        if (minBlend == null)
            minBlend = CreateMinBlend();
        var ctx = rc.DeviceContext;
        var prevBlend = ctx?.OutputMerger.BlendState;
        if (ctx != null && minBlend != null)
            ctx.OutputMerger.SetBlendState(minBlend);

        rc.SetRasterizerState(MyRasterizerStateManager.NocullRasterizerState);
        rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
        rc.SetInputLayout(null);
        rc.SetPrimitiveTopology(PrimitiveTopology.TriangleList);
        rc.SetVertexBuffer(0, null);
        rc.GeometryShader.Set(null);
        rc.VertexShader.Set(boxVs);
        rc.PixelShader.Set(boxPs);
        rc.VertexShader.SetSrv(0, boxBuffer);
        rc.PixelShader.SetSrv(0, boxBuffer);

        for (var i = 0; i < lights.Count; i++)
        {
            LightBoxes.Clear();
            CollectBoxes(lights[i], LightBoxes, MaxBoxesPerLight);
            if (LightBoxes.Count == 0)
                continue;
            UploadBoxes(rc, LightBoxes);
            for (var f = 0; f < 6; f++)
            {
                var viewProj = CubeFaceViewProj(lights[i].ViewPos, f, lights[i].Range);
                var faceCbData = new FaceConstants
                {
                    ViewProj = viewProj,
                    LightPos = lights[i].ViewPos,
                    Range = lights[i].Range,
                    BoxCount = (uint)LightBoxes.Count
                };
                WriteCb(rc, faceCb, ref faceCbData);
                rc.VertexShader.SetConstantBuffer(0, faceCb);
                rc.PixelShader.SetConstantBuffer(0, faceCb);
                rc.SetViewport(f * allocatedFace, 1 + i * allocatedFace, allocatedFace, allocatedFace, 0f, 1f);
                rc.Draw(36 * LightBoxes.Count, 0);
            }
        }

        if (ctx != null)
            ctx.OutputMerger.SetBlendState(prevBlend);
        rc.SetRtvNull();
        rc.ClearState();

        var native = atlas.Resource != null ? atlas.Resource.NativePointer : IntPtr.Zero;
        AtlasPublished.Publish(atlas, native, atlasW, atlasH);
        BufferCatalog.Set(BufferCatalog.PointShadowAtlas, AtlasPublished);
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

        var handle = GCHandle.Alloc(HeaderScratch, GCHandleType.Pinned);
        try
        {
            var box = new DataBox(handle.AddrOfPinnedObject(), MaxLightCap * 16, 0);
            var region = new ResourceRegion(0, 0, 0, Math.Max(lights.Count, 1), 1, 1);
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
        var proj = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2, 1f, 0.08f, far);
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
        if (Empty(splatBc) || Empty(stampBc) || Empty(vsBc) || Empty(psBc))
        {
            Fail("point-shadow shader compile returned empty bytecode", null);
            return;
        }

        var device = MyRender11.DeviceInstance;
        DisposeShadersOnly();
        splatCs = new ComputeShader(device, splatBc) { DebugName = "Anomaly.OccupancySplat" };
        stampCs = new ComputeShader(device, stampBc) { DebugName = "Anomaly.OccupancyStamp" };
        boxVs = new VertexShader(device, vsBc) { DebugName = "Anomaly.BoxDepth.VS" };
        boxPs = new PixelShader(device, psBc) { DebugName = "Anomaly.BoxDepth.PS" };
        occupancyCb ??= MyManagers.Buffers.CreateConstantBuffer("Anomaly.OccupancyCB", ConstantBytes,
            usage: ResourceUsage.Dynamic);
        faceCb ??= MyManagers.Buffers.CreateConstantBuffer("Anomaly.PointShadowFaceCB", ConstantBytes,
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
        BufferCatalog.Set(BufferCatalog.Occupancy, null);
        BufferCatalog.Set(BufferCatalog.PointShadowAtlas, null);
    }

    static void DisposeTargets()
    {
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

        minBlend?.Dispose();
        minBlend = null;
        shadersReady = false;
    }

    static void DisposeShadersOnly()
    {
        splatCs?.Dispose();
        stampCs?.Dispose();
        boxVs?.Dispose();
        boxPs?.Dispose();
        splatCs = null;
        stampCs = null;
        boxVs = null;
        boxPs = null;
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
