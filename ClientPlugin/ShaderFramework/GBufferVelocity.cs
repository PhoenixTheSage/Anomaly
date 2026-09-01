using System;
using System.Runtime.InteropServices;
using ClientPlugin.Shaders;
using ClientPlugin.Velocity;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using VRage.Render.Scene;
using VRage.Render11.Common;
using VRage.Render11.GeometryStage2.Instancing;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRage.Render11.Scene.Components;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace ClientPlugin.ShaderFramework;

/// <summary>
/// Extra GBuffer color target + Stage 2 previous-world SRV + old-pipeline prev bones.
/// Bound only from GBuffer begin (old <c>MyGBufferPass</c> and Stage 2
/// <c>MyGBufferRenderPass</c>). Depth stays on Keen's 3-RT SetRtvs.
/// </summary>
public static class GBufferVelocity
{
    public const int ConstantSlot = 6;
    public const int PrevWorldSlot = 15;
    public const int PrevBoneSlot = 16;
    const int ConstantBufferBytes = 256;
    const int PrevStride = 64;
    const int BoneStride = 64;

    static readonly object Gate = new();
    static RenderTargetView[] rtvScratch = new RenderTargetView[4];

    public static bool Enabled { get; set; } = true;
    public static bool IsLive { get; private set; }
    public static string LastError { get; private set; }

    static IRtvTexture target;
    static IRtvTexture resolved;
    static int targetWidth;
    static int targetHeight;
    static int targetSamples;
    static IConstantBuffer constants;
    static ISrvBuffer prevWorld;
    static ISrvBuffer prevBones;
    static int prevCapacity;
    static int prevCount;
    static PrevInstance[] cpuPrev = Array.Empty<PrevInstance>();
    static readonly Matrix[] BoneScratch = new Matrix[BoneHistory.MaxBones];
    static bool loggedError;
    static bool historyValidThisFrame;
    static Matrix lastUnjittered;
    static Matrix lastPrevViewProj;
    static Vector2I lastSize;

    [StructLayout(LayoutKind.Sequential, Size = PrevStride)]
    struct PrevInstance
    {
        public Vector4 Col0;
        public Vector4 Col1;
        public Vector4 Col2;
        public Vector4 Flags;
    }

    [StructLayout(LayoutKind.Sequential, Size = ConstantBufferBytes)]
    struct Constants
    {
        public Matrix UnjitteredViewProj;
        public Matrix PrevViewProj;
        public Vector2 RenderSize;
        public Vector2 InvRenderSize;
        public uint PrevCount;
        public uint HasHistory;
        public uint HasPrevWorld;
        public uint BoneCount;
        public Vector4 PrevRow0;
        public Vector4 PrevRow1;
        public Vector4 PrevRow2;
    }

    static bool InjectionWanted =>
        Enabled && (Config.Current?.VelocitySource ?? VelocitySource.GBuffer) == VelocitySource.GBuffer;

    public static bool ShouldPublish =>
        InjectionWanted && target != null && IsLive;

    public static void OnInitInstanceElements(int elementsCount)
    {
        if (!InjectionWanted)
            return;
        lock (Gate)
        {
            prevCount = Math.Max(elementsCount, 0);
            if (cpuPrev.Length < Math.Max(prevCount, 1))
                cpuPrev = new PrevInstance[Math.Max(prevCount, 1)];
            Array.Clear(cpuPrev, 0, cpuPrev.Length);
        }
    }

    public static void OnAddInstance(int bufferOffset, MyInstance instance)
    {
        if (!InjectionWanted || instance == null)
            return;
        lock (Gate)
        {
            if (bufferOffset < 0)
                return;
            if (cpuPrev.Length <= bufferOffset)
            {
                var next = new PrevInstance[bufferOffset + 1];
                Array.Copy(cpuPrev, next, cpuPrev.Length);
                cpuPrev = next;
            }

            if (bufferOffset >= prevCount)
                prevCount = bufferOffset + 1;

            if (IsClipmapActor(instance.Owner?.Owner) ||
                !CameraVelocityPass.TryGetPrevCamera(out var prevCam) ||
                ActorHistory.Instance.WasTeleported(instance.ActorID) ||
                !ActorHistory.Instance.TryGetPrevious(instance.ActorID, out var prevWorldAbs))
            {
                cpuPrev[bufferOffset] = default;
                return;
            }

            cpuPrev[bufferOffset] = Pack(prevWorldAbs, prevCam);
        }
    }

    public static void OnWriteInstanceData()
    {
        if (!InjectionWanted)
            return;
        lock (Gate)
        {
            try
            {
                UploadPrevWorldUnlocked();
            }
            catch (Exception e)
            {
                Fail("prev-world upload: " + e.Message, e);
            }
        }
    }

    public static void Bind(MyRenderContext rc, MyGBuffer gbuffer)
    {
        if (rc == null || gbuffer == null || !rc.IsInitialized)
            return;
        if (!InjectionWanted && !GBufferAttachments.HasColorTargets)
            return;

        lock (Gate)
        {
            if (!InjectionWanted && !GBufferAttachments.HasColorTargets)
                return;
            try
            {
                BindUnlocked(rc, gbuffer);
            }
            catch (Exception e)
            {
                Fail("bind: " + e.Message, e);
            }
        }
    }

    public static void Unbind(MyRenderContext rc)
    {
        if (rc == null || !rc.IsInitialized)
            return;
        try
        {
            rc.AllShaderStages.SetSrv(PrevWorldSlot, null);
            rc.AllShaderStages.SetSrv(PrevBoneSlot, null);
        }
        catch (Exception e)
        {
            DebugLog.Write("GBufferVelocity unbind: " + e.Message);
        }
    }

    public static void ClearTarget(MyRenderContext rc)
    {
        if (rc == null)
            return;
        lock (Gate)
        {
            if (target != null)
                rc.ClearRtv(target, default(RawColor4));
            GBufferAttachments.ClearTargets(rc);
        }
    }

    public static ISrvBindable PrepareCompositeSource()
    {
        lock (Gate)
        {
            try
            {
                return PrepareCompositeSourceUnlocked();
            }
            catch (Exception e)
            {
                Fail("composite source: " + e.Message, e);
                return null;
            }
        }
    }

    public static void OnProxyDraw(MyRenderContext rc, MyRenderableProxy proxy)
    {
        if (!InjectionWanted || rc == null || proxy == null || !rc.IsInitialized)
            return;
        lock (Gate)
        {
            if (!InjectionWanted)
                return;
            try
            {
                OnProxyDrawUnlocked(rc, proxy);
            }
            catch (Exception e)
            {
                Fail("proxy draw: " + e.Message, e);
            }
        }
    }

    public static void PublishAndAdvanceHistory()
    {
        lock (Gate)
        {
            try
            {
                PublishUnlocked();
            }
            catch (Exception e)
            {
                Fail("publish: " + e.Message, e);
                VelocityRegistry.SetActive(UnavailableVelocityBuffer.Instance);
            }
        }
    }

    public static void OnResolutionChanged()
    {
        lock (Gate)
        {
            GBufferAttachments.OnResolutionChanged();
            if (!InjectionWanted && !GBufferAttachments.HasColorTargets)
                return;
            try
            {
                EnsureTargetUnlocked();
            }
            catch (Exception e)
            {
                Fail("resize: " + e.Message, e);
            }
        }
    }

    public static void Release()
    {
        lock (Gate)
        {
            IsLive = false;
            CameraVelocityBuffer.Instance.Clear();
            DisposeTarget();
            DisposePrevAndCb();
            GBufferAttachments.Release();
        }
    }

    static void BindUnlocked(MyRenderContext rc, MyGBuffer gbuffer)
    {
        EnsureTargetUnlocked();
        EnsureConstantsUnlocked();
        EnsurePrevBufferUnlocked(Math.Max(prevCount, 1));
        EnsureBoneBufferUnlocked();
        if (target == null || constants == null || prevWorld == null)
            return;
        if (gbuffer.GbufferRtvs == null || gbuffer.GbufferRtvs.Length < 3 || gbuffer.DepthStencil?.Dsv == null)
            return;

        var extraMax = GBufferAttachments.MaxBoundTarget;
        var gbufferSize = MyRender11.ResolutionI;
        var samples = Math.Max(gbuffer.SamplesCount, 1);
        var quality = gbuffer.SamplesQuality;
        GBufferAttachments.EnsureTargets(gbufferSize.X, gbufferSize.Y, samples, quality);

        if (!CameraVelocityPass.TryGetMotionFrame(out var unjittered, out var prevVp, out _, out var historyValid, out var size))
            return;

        historyValidThisFrame = historyValid;
        lastUnjittered = unjittered;
        lastPrevViewProj = prevVp;
        lastSize = size;
        WriteConstants(rc, unjittered, prevVp, size, historyValid, (uint)Math.Max(prevCount, 1),
            hasPrevWorld: false, default, default, default, boneCount: 0);

        var n = Math.Max(4, extraMax + 1);
        if (rtvScratch == null || rtvScratch.Length != n)
            rtvScratch = new RenderTargetView[n];
        Array.Clear(rtvScratch, 0, rtvScratch.Length);
        rtvScratch[0] = gbuffer.GbufferRtvs[0];
        rtvScratch[1] = gbuffer.GbufferRtvs[1];
        rtvScratch[2] = gbuffer.GbufferRtvs[2];
        rtvScratch[3] = target.Rtv;
        GBufferAttachments.CopyRtvs(rtvScratch);
        rc.SetRtvs(gbuffer.DepthStencil.Dsv, rtvScratch);
        rc.VertexShader.SetConstantBuffer(ConstantSlot, constants);
        rc.PixelShader.SetConstantBuffer(ConstantSlot, constants);
        rc.AllShaderStages.SetSrv(PrevWorldSlot, prevWorld);
        if (prevBones != null)
            rc.AllShaderStages.SetSrv(PrevBoneSlot, prevBones);

        IsLive = ShaderCompileIntercept.GBufferOverlayPresent;
        LastError = ShaderCompileIntercept.GBufferOverlayPresent
            ? null
            : "GBuffer overlay HLSL missing";
        loggedError = false;
    }

    static void PublishUnlocked()
    {
        if (target == null)
            return;

        var size = MyRender11.ResolutionI;
        IRtvTexture publish = target;
        if (targetSamples > 1)
        {
            EnsureResolvedUnlocked(size.X, size.Y);
            var rc = MyRender11.RC;
            if (resolved == null || rc?.DeviceContext == null)
                return;
            rc.DeviceContext.ResolveSubresource(target.Resource, 0, resolved.Resource, 0, Format.R16G16_Float);
            publish = resolved;
        }

        var native = publish.Resource != null ? publish.Resource.NativePointer : IntPtr.Zero;
        CameraVelocityBuffer.Instance.Publish(publish, native, size.X, size.Y, historyValidThisFrame);
        VelocityRegistry.SetActive(CameraVelocityBuffer.Instance);
        CameraVelocityPass.AdvanceHistory();
        LastError = null;
    }

    static void UploadPrevWorldUnlocked()
    {
        var count = Math.Max(prevCount, 1);
        EnsurePrevBufferUnlocked(count);
        if (prevWorld == null)
            return;
        var rc = MyRender11.RC;
        if (rc == null || !rc.IsInitialized)
            return;
        if (cpuPrev.Length < count)
        {
            var next = new PrevInstance[count];
            Array.Copy(cpuPrev, next, cpuPrev.Length);
            cpuPrev = next;
        }

        var mapping = MyMapping.MapDiscard(rc, prevWorld);
        mapping.WriteAndPosition(cpuPrev, count, 0);
        mapping.Unmap();
    }

    static void EnsureTargetUnlocked()
    {
        var size = MyRender11.ResolutionI;
        if (size.X <= 0 || size.Y <= 0)
            return;

        var gbuffer = MyGBuffer.Main;
        var samples = Math.Max(gbuffer?.SamplesCount ?? 1, 1);
        var quality = gbuffer?.SamplesQuality ?? 0;
        if (target != null && targetWidth == size.X && targetHeight == size.Y && targetSamples == samples)
            return;

        DisposeTarget();
        target = MyManagers.RwTextures.CreateRtv("Anomaly.GBufferVelocity", size.X, size.Y, Format.R16G16_Float,
            samples, quality);
        targetWidth = size.X;
        targetHeight = size.Y;
        targetSamples = samples;
        if (samples <= 1)
        {
            if (resolved != null)
                MyManagers.RwTextures.DisposeTex(ref resolved);
            resolved = null;
        }

        DebugLog.Write("GBufferVelocity RT " + size.X + "x" + size.Y + " samples=" + samples);
    }

    static void EnsureResolvedUnlocked(int width, int height)
    {
        if (resolved != null && resolved.Size.X == width && resolved.Size.Y == height)
            return;
        if (resolved != null)
            MyManagers.RwTextures.DisposeTex(ref resolved);
        resolved = MyManagers.RwTextures.CreateRtv("Anomaly.GBufferVelocity.Resolved", width, height, Format.R16G16_Float);
    }

    static void EnsureConstantsUnlocked()
    {
        if (constants != null)
            return;
        constants = MyManagers.Buffers.CreateConstantBuffer("Anomaly.GBufferVelocityCB", ConstantBufferBytes,
            usage: ResourceUsage.Dynamic);
    }

    static void EnsurePrevBufferUnlocked(int elements)
    {
        elements = Math.Max(elements, 1);
        if (prevWorld != null && prevCapacity >= elements)
            return;
        if (prevWorld == null)
        {
            prevWorld = MyManagers.Buffers.CreateSrv("Anomaly.PrevWorld", elements, PrevStride,
                usage: ResourceUsage.Dynamic);
            prevCapacity = elements;
            return;
        }

        MyManagers.Buffers.Resize(prevWorld, elements, PrevStride, null);
        prevCapacity = elements;
    }

    static void EnsureBoneBufferUnlocked()
    {
        if (prevBones != null)
            return;
        prevBones = MyManagers.Buffers.CreateSrv("Anomaly.PrevBones", BoneHistory.MaxBones, BoneStride,
            usage: ResourceUsage.Dynamic);
    }

    static ISrvBindable PrepareCompositeSourceUnlocked()
    {
        if (target == null)
            return null;
        var size = MyRender11.ResolutionI;
        if (targetSamples > 1)
        {
            EnsureResolvedUnlocked(size.X, size.Y);
            var rc = MyRender11.RC;
            if (resolved == null || rc?.DeviceContext == null)
                return null;
            rc.DeviceContext.ResolveSubresource(target.Resource, 0, resolved.Resource, 0, Format.R16G16_Float);
            return resolved;
        }

        return target;
    }

    static void OnProxyDrawUnlocked(MyRenderContext rc, MyRenderableProxy proxy)
    {
        if (constants == null)
            return;

        EnsureBoneBufferUnlocked();
        var actor = proxy.Parent?.Owner;
        var actorId = actor != null ? actor.ID : 0;
        var clipmap = proxy.VoxelCommonObjectData.IsValid || IsClipmapActor(actor);
        var packed = default(PrevInstance);
        var hasPrevWorld = false;
        if (!clipmap &&
            actorId != 0 &&
            CameraVelocityPass.TryGetPrevCamera(out var prevCam) &&
            !ActorHistory.Instance.WasTeleported(actorId) &&
            ActorHistory.Instance.TryGetPrevious(actorId, out var prevWorldAbs))
        {
            packed = Pack(prevWorldAbs, prevCam);
            hasPrevWorld = true;
        }

        var boneCount = PackPrevBones(rc, actorId, proxy.SkinningMatrices, proxy.DrawSubmesh.BonesMapping);
        WriteConstants(rc, lastUnjittered, lastPrevViewProj, lastSize, historyValidThisFrame,
            (uint)Math.Max(prevCount, 1), hasPrevWorld, packed.Col0, packed.Col1, packed.Col2, (uint)boneCount);
        if (prevBones != null)
            rc.AllShaderStages.SetSrv(PrevBoneSlot, prevBones);
    }

    static int PackPrevBones(MyRenderContext rc, uint actorId, Matrix[] current, int[] mapping)
    {
        EnsureBoneBufferUnlocked();
        Array.Clear(BoneScratch, 0, BoneScratch.Length);
        var count = 0;
        if (current != null &&
            actorId != 0 &&
            BoneHistory.Instance.TryGetPrevious(actorId, current.Length, out var previous))
        {
            if (mapping == null)
            {
                count = Math.Min(BoneHistory.MaxBones, previous.Length);
                Array.Copy(previous, BoneScratch, count);
            }
            else
            {
                count = Math.Min(BoneHistory.MaxBones, mapping.Length);
                for (var i = 0; i < count; i++)
                {
                    var idx = mapping[i];
                    if (idx >= 0 && idx < previous.Length)
                        BoneScratch[i] = previous[idx];
                }
            }
        }

        if (prevBones == null)
            return 0;
        var mappingGpu = MyMapping.MapDiscard(rc, prevBones);
        mappingGpu.WriteAndPosition(BoneScratch, BoneHistory.MaxBones, 0);
        mappingGpu.Unmap();
        return count;
    }

    static void WriteConstants(MyRenderContext rc, Matrix unjittered, Matrix prevVp, Vector2I size,
        bool historyValid, uint packedCount, bool hasPrevWorld, Vector4 row0, Vector4 row1, Vector4 row2, uint boneCount)
    {
        if (constants == null || size.X <= 0 || size.Y <= 0)
            return;
        var cb = new Constants
        {
            UnjitteredViewProj = unjittered,
            PrevViewProj = prevVp,
            RenderSize = new Vector2(size.X, size.Y),
            InvRenderSize = new Vector2(1f / size.X, 1f / size.Y),
            PrevCount = packedCount,
            HasHistory = historyValid ? 1u : 0u,
            HasPrevWorld = hasPrevWorld ? 1u : 0u,
            BoneCount = boneCount,
            PrevRow0 = row0,
            PrevRow1 = row1,
            PrevRow2 = row2
        };
        var mapping = MyMapping.MapDiscard(rc, constants);
        mapping.WriteAndPosition(ref cb);
        mapping.Unmap();
    }

    static bool IsClipmapActor(IMyActor actor)
    {
        return actor != null && actor.GetComponent<MyVoxelCellComponent>() != null;
    }

    static PrevInstance Pack(MatrixD world, Vector3D camera)
    {
        var t = world.Translation - camera;
        return new PrevInstance
        {
            Col0 = new Vector4((float)world.M11, (float)world.M21, (float)world.M31, (float)t.X),
            Col1 = new Vector4((float)world.M12, (float)world.M22, (float)world.M32, (float)t.Y),
            Col2 = new Vector4((float)world.M13, (float)world.M23, (float)world.M33, (float)t.Z),
            Flags = new Vector4(1f, 0f, 0f, 0f)
        };
    }

    static void DisposeTarget()
    {
        if (target != null)
            MyManagers.RwTextures.DisposeTex(ref target);
        if (resolved != null)
            MyManagers.RwTextures.DisposeTex(ref resolved);
        target = null;
        resolved = null;
        targetWidth = 0;
        targetHeight = 0;
        targetSamples = 0;
    }

    static void DisposePrevAndCb()
    {
        if (constants != null)
        {
            MyManagers.Buffers.Dispose(new[] { constants });
            constants = null;
        }

        if (prevWorld != null)
        {
            MyManagers.Buffers.Dispose(new ISrvBindable[] { prevWorld });
            prevWorld = null;
        }

        if (prevBones != null)
        {
            MyManagers.Buffers.Dispose(new ISrvBindable[] { prevBones });
            prevBones = null;
        }

        prevCapacity = 0;
        prevCount = 0;
    }

    static void Fail(string message, Exception e)
    {
        LastError = message;
        IsLive = false;
        if (loggedError)
            return;
        loggedError = true;
        MyLog.Default.WriteLine("Anomaly GBuffer velocity: " + message);
        DebugLog.Write("GBufferVelocity " + message + (e != null ? "\n" + e : ""));
    }
}
