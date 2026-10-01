using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ClientPlugin.Buffers;
using ClientPlugin.Shaders;
using ClientPlugin.Velocity;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using VRage.Render11.Common;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace ClientPlugin.ShaderFramework;

/// <summary>
/// Anomaly-owned <c>reactiveMask</c> and velocity overlay composite.
/// IsolatedAdd / IsolatedMix / IsolatedSub / DirectAdd / PublishOnly with
/// <see cref="TemporalPolicy.Reactive"/> stamps dilated isolated luma.
/// IsolatedSub stamps <c>.a</c> (0–1 occlusion) because rgb is the composited
/// dest. IsolatedSub umbra uses Reactive, not ContributeVelocity.
/// The same composes with <see cref="TemporalPolicy.ContributeVelocity"/>
/// reconstruct camera MVs from isolated.a. C# owned passes may still write
/// the mask / call <see cref="OwnedPassContext.ContributeVelocity"/>.
/// </summary>
public static class TemporalParticipation
{
    const string VsFile = "Fullscreen.hlsl";
    const string ClearPsFile = "ReactiveClear.hlsl";
    const string StampPsFile = "ReactiveStamp.hlsl";
    const string ContributePsFile = "VelocityContribute.hlsl";
    const string IsolatedVelocityPsFile = "IsolatedVelocity.hlsl";
    const int IsolatedVelocityCbBytes = 256;

    static readonly object Gate = new();
    static readonly CatalogTexture ReactivePublished = new();
    static readonly IRtvTexture[] ContributeTargets = new IRtvTexture[2];

    static VertexShader vertexShader;
    static PixelShader clearShader;
    static PixelShader stampShader;
    static PixelShader stampAlphaShader;
    static PixelShader contributeShader;
    static PixelShader isolatedVelocityShader;
    static IConstantBuffer isolatedVelocityCb;
    static IRtvTexture reactiveTarget;
    static IRtvTexture stampScratch;
    static int contributeWrite;
    static int width;
    static int height;
    static bool shadersReady;
    static bool clearedThisFrame;
    static bool stampedThisFrame;
    static bool loggedError;
    static readonly RenderTextureCheckpoint ReactiveRecovery = new(), StampRecovery = new();
    static bool recoveryCleared, recoveryStamped;
    static IRtvTexture recoveryReactiveTarget, recoveryStampScratch;

    internal static void CaptureRecovery(MyRenderContext rc)
    {
        lock (Gate)
        {
            EnsureReactiveUnlocked(rc);
            recoveryCleared = clearedThisFrame; recoveryStamped = stampedThisFrame;
            recoveryReactiveTarget = reactiveTarget; recoveryStampScratch = stampScratch;
            ReactiveRecovery.Capture(MyRender11.DeviceInstance, rc.DeviceContext, reactiveTarget?.Resource as Texture2D);
            StampRecovery.Capture(MyRender11.DeviceInstance, rc.DeviceContext, stampScratch?.Resource as Texture2D);
        }
    }

    internal static void RestoreRecovery(MyRenderContext rc)
    {
        lock (Gate)
        {
            ReactiveRecovery.Restore(rc.DeviceContext); StampRecovery.Restore(rc.DeviceContext);
            // Stamping swaps these textures; restore their roles as well as their pixels.
            reactiveTarget = recoveryReactiveTarget; stampScratch = recoveryStampScratch;
            clearedThisFrame = recoveryCleared; stampedThisFrame = recoveryStamped;
            var native = reactiveTarget?.Resource?.NativePointer ?? IntPtr.Zero;
            ReactivePublished.Publish(reactiveTarget, native, width, height);
            BufferCatalog.Set(BufferCatalog.ReactiveMask, ReactivePublished);
        }
    }

    [StructLayout(LayoutKind.Sequential, Size = IsolatedVelocityCbBytes)]
    struct IsolatedVelocityConstants
    {
        public Matrix UnjitteredViewProj;
        public Matrix PrevViewProj;
        public Vector4 CamToWorldR0;
        public Vector4 CamToWorldR1;
        public Vector4 CamToWorldR2;
        public Vector2 RenderSize;
        public Vector2 ProjScale;
        public uint HistoryValid;
        public float DistanceScale;
        public Vector2 Pad1;
    }

    public static object ReactiveRtv
    {
        get
        {
            lock (Gate)
            {
                EnsureReactiveUnlocked(null);
                return reactiveTarget;
            }
        }
    }

    public static object LinearDepthSrv
    {
        get
        {
            var buf = BufferCatalog.Active(BufferCatalog.LinearDepth);
            return buf != null && buf.IsAvailable ? buf.Srv : null;
        }
    }

    internal static void BeginFrame()
    {
        lock (Gate)
        {
            clearedThisFrame = false;
            stampedThisFrame = false;
        }
    }

    /// <summary>
    /// Allocate / clear on <paramref name="rc"/>. AfterLighting and
    /// AfterAtmosphere record on Keen's transparent deferred worker
    /// (<c>AcquireRC("MyTransparentRendering")</c>). Never clear on
    /// <c>MyRender11.RC</c> from that worker — D3D11 immediate contexts
    /// are single-threaded and a later Present reports DEVICE_HUNG.
    /// Pass null to create targets only (no GPU work).
    /// </summary>
    internal static void EnsureReactive(MyRenderContext rc)
    {
        lock (Gate)
            EnsureReactiveUnlocked(rc);
    }

    internal static void ContributeVelocity(MyRenderContext rc, ISrvBindable overlay, ISrvBindable mask)
    {
        if (rc == null || !rc.IsInitialized || overlay == null || mask == null)
            return;
        lock (Gate)
        {
            try
            {
                ContributeUnlocked(rc, overlay, mask);
            }
            catch (Exception e)
            {
                Fail("contribute: " + e.GetType().Name + ": " + e.Message);
            }
        }
    }

    /// <summary>
    /// IsolatedAdd with <see cref="TemporalPolicy.ContributeVelocity"/>:
    /// reconstruct camera MVs from isolated.a * distanceScale (meters along
    /// the unjittered camera ray) and composite over catalog velocity.
    /// </summary>
    internal static void ContributeFromIsolated(MyRenderContext rc, ISrvBindable isolated,
        float distanceScale = 1f)
    {
        if (rc == null || !rc.IsInitialized || isolated == null)
            return;
        lock (Gate)
        {
            try
            {
                ContributeFromIsolatedUnlocked(rc, isolated, distanceScale);
            }
            catch (Exception e)
            {
                Fail("isolated-velocity: " + e.GetType().Name + ": " + e.Message);
            }
        }
    }

    internal static void StampFromIsolated(MyRenderContext rc, ISrvBindable isolated,
        bool occlusionAlpha = false)
    {
        if (rc == null || !rc.IsInitialized || isolated == null)
            return;
        lock (Gate)
        {
            try
            {
                StampUnlocked(rc, isolated, occlusionAlpha);
            }
            catch (Exception e)
            {
                Fail("stamp: " + e.GetType().Name + ": " + e.Message);
            }
        }
    }

    internal static void OnResolutionChanged()
    {
        lock (Gate)
        {
            DisposeTargets();
            ReactivePublished.Clear();
            BufferCatalog.Set(BufferCatalog.ReactiveMask, null);
            FrameTemporal.InvalidateHistory();
        }
    }

    internal static void Release()
    {
        lock (Gate)
        {
            DisposeTargets();
            DisposeShaders();
            ReactivePublished.Clear();
            BufferCatalog.Set(BufferCatalog.ReactiveMask, null);
            shadersReady = false;
        }
    }

    static void EnsureReactiveUnlocked(MyRenderContext rc)
    {
        var size = MyRender11.ResolutionI;
        if (size.X <= 0 || size.Y <= 0)
            return;
        EnsureShadersUnlocked();
        var created = false;
        if (reactiveTarget == null || width != size.X || height != size.Y)
        {
            DisposeTargets();
            reactiveTarget = MyManagers.RwTextures.CreateRtv("Anomaly.ReactiveMask", size.X, size.Y,
                Format.R8_UNorm);
            width = size.X;
            height = size.Y;
            clearedThisFrame = false;
            stampedThisFrame = false;
            created = true;
        }

        if (reactiveTarget == null)
            return;

        EnsureStampScratchUnlocked(size.X, size.Y);

        // Fresh unpublished ping each frame. Do not write the published
        // target — DLSS may still hold last Evaluate's bias texture.
        // GPU clear only on the caller’s rc (transparent deferred worker
        // for AfterAtmosphere). MyRender11.RC from that worker is a hang.
        if (!clearedThisFrame && rc != null && rc.IsInitialized && shadersReady &&
            clearShader != null && vertexShader != null)
        {
            if (created)
                ClearReactiveUnlocked(rc, reactiveTarget);
            if (stampScratch != null)
                ClearReactiveUnlocked(rc, stampScratch);
            else
                ClearReactiveUnlocked(rc, reactiveTarget);
            clearedThisFrame = true;
        }

        var native = reactiveTarget.Resource != null
            ? reactiveTarget.Resource.NativePointer
            : IntPtr.Zero;
        ReactivePublished.Publish(reactiveTarget, native, width, height);
        BufferCatalog.Set(BufferCatalog.ReactiveMask, ReactivePublished);
    }

    static void EnsureStampScratchUnlocked(int w, int h)
    {
        if (w <= 0 || h <= 0)
            return;
        if (stampScratch != null && stampScratch.Size.X == w && stampScratch.Size.Y == h)
            return;
        if (stampScratch != null)
            MyManagers.RwTextures.DisposeTex(ref stampScratch);
        stampScratch = MyManagers.RwTextures.CreateRtv("Anomaly.ReactiveStamp", w, h, Format.R8_UNorm);
    }

    static void ClearReactiveUnlocked(MyRenderContext rc, IRtvTexture dest)
    {
        if (dest == null)
            return;
        rc.SetScreenViewport();
        rc.SetRasterizerState(MyRasterizerStateManager.NocullRasterizerState);
        rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
        rc.SetBlendState(MyBlendStateManager.BlendReplace);
        BindRtv(rc, dest);
        rc.SetInputLayout(null);
        rc.SetPrimitiveTopology(PrimitiveTopology.TriangleList);
        rc.SetVertexBuffer(0, null);
        rc.GeometryShader.Set(null);
        rc.VertexShader.Set(vertexShader);
        rc.PixelShader.Set(clearShader);
        rc.Draw(3, 0);
        rc.SetRtvNull();
    }

    static void StampUnlocked(MyRenderContext rc, ISrvBindable isolated, bool occlusionAlpha)
    {
        EnsureReactiveUnlocked(rc);
        EnsureStampShaderUnlocked();
        var size = MyRender11.ResolutionI;
        var ps = occlusionAlpha ? stampAlphaShader : stampShader;
        if (!shadersReady || vertexShader == null || ps == null || reactiveTarget == null)
            return;
        if (size.X <= 0 || size.Y <= 0)
            return;

        EnsureStampScratchUnlocked(size.X, size.Y);
        if (stampScratch == null)
            return;

        rc.SetScreenViewport();
        rc.SetRasterizerState(MyRasterizerStateManager.NocullRasterizerState);
        rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
        rc.SetBlendState(MyBlendStateManager.BlendReplace);
        BindRtv(rc, stampScratch);
        rc.SetInputLayout(null);
        rc.SetPrimitiveTopology(PrimitiveTopology.TriangleList);
        rc.SetVertexBuffer(0, null);
        rc.GeometryShader.Set(null);
        rc.VertexShader.Set(vertexShader);
        rc.PixelShader.Set(ps);
        rc.PixelShader.SetSampler(0, MySamplerStateManager.Point);
        rc.PixelShader.SetSrv(0, isolated);
        // First IsolatedAdd this frame writes the cleared unpublished ping.
        // Later IsolatedAdds max against the published result (read-only).
        rc.PixelShader.SetSrv(1, stampedThisFrame ? reactiveTarget : null);
        rc.Draw(3, 0);
        rc.PixelShader.SetSrv(0, null);
        rc.PixelShader.SetSrv(1, null);
        rc.SetRtvNull();

        var swap = reactiveTarget;
        reactiveTarget = stampScratch;
        stampScratch = swap;
        stampedThisFrame = true;

        var native = reactiveTarget.Resource != null
            ? reactiveTarget.Resource.NativePointer
            : IntPtr.Zero;
        ReactivePublished.Publish(reactiveTarget, native, width, height);
        BufferCatalog.Set(BufferCatalog.ReactiveMask, ReactivePublished);
    }

    static void ContributeUnlocked(MyRenderContext rc, ISrvBindable overlay, ISrvBindable mask)
    {
        EnsureShadersUnlocked();
        var size = MyRender11.ResolutionI;
        if (!shadersReady || vertexShader == null || contributeShader == null)
            return;
        if (size.X <= 0 || size.Y <= 0)
            return;

        var current = BufferCatalog.Active(BufferCatalog.Velocity);
        var currentSrv = current != null && current.IsAvailable ? current.Srv as ISrvBindable : null;
        if (currentSrv == null)
            return;

        var dest = NextContributeTarget(size.X, size.Y);
        if (dest == null)
            return;

        rc.SetScreenViewport();
        rc.SetRasterizerState(MyRasterizerStateManager.NocullRasterizerState);
        rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
        rc.SetBlendState(MyBlendStateManager.BlendReplace);
        BindRtv(rc, dest);
        rc.SetInputLayout(null);
        rc.SetPrimitiveTopology(PrimitiveTopology.TriangleList);
        rc.SetVertexBuffer(0, null);
        rc.GeometryShader.Set(null);
        rc.VertexShader.Set(vertexShader);
        rc.PixelShader.Set(contributeShader);
        rc.PixelShader.SetSampler(0, MySamplerStateManager.Point);
        rc.PixelShader.SetSrv(0, currentSrv);
        rc.PixelShader.SetSrv(1, overlay);
        rc.PixelShader.SetSrv(2, mask);
        rc.Draw(3, 0);
        rc.PixelShader.SetSrv(0, null);
        rc.PixelShader.SetSrv(1, null);
        rc.PixelShader.SetSrv(2, null);
        rc.SetRtvNull();

        PublishContribute(dest, size.X, size.Y);
    }

    static void ContributeFromIsolatedUnlocked(MyRenderContext rc, ISrvBindable isolated, float distanceScale)
    {
        EnsureShadersUnlocked();
        EnsureIsolatedVelocityUnlocked();
        var size = MyRender11.ResolutionI;
        if (!shadersReady || vertexShader == null || isolatedVelocityShader == null ||
            isolatedVelocityCb == null)
            return;
        if (size.X <= 0 || size.Y <= 0)
            return;

        var current = BufferCatalog.Active(BufferCatalog.Velocity);
        var currentSrv = current != null && current.IsAvailable ? current.Srv as ISrvBindable : null;
        if (currentSrv == null)
            return;

        var dest = NextContributeTarget(size.X, size.Y);
        if (dest == null)
            return;

        FrameTemporal.EnsureSnapshot();
        var prev = FrameTemporal.PrevViewProj;
        if (FrameTemporal.HistoryValid)
            prev = Matrix.CreateTranslation(FrameTemporal.CameraDelta) * prev;
        var cb = new IsolatedVelocityConstants
        {
            UnjitteredViewProj = FrameTemporal.UnjitteredViewProj,
            PrevViewProj = prev,
            CamToWorldR0 = FrameTemporal.CameraToWorldRow(0),
            CamToWorldR1 = FrameTemporal.CameraToWorldRow(1),
            CamToWorldR2 = FrameTemporal.CameraToWorldRow(2),
            RenderSize = new Vector2(size.X, size.Y),
            ProjScale = FrameTemporal.ProjScale,
            HistoryValid = FrameTemporal.HistoryValid ? 1u : 0u,
            DistanceScale = distanceScale
        };
        var mapping = MyMapping.MapDiscard(rc, isolatedVelocityCb);
        mapping.WriteAndPosition(ref cb);
        mapping.Unmap();

        rc.SetScreenViewport();
        rc.SetRasterizerState(MyRasterizerStateManager.NocullRasterizerState);
        rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
        rc.SetBlendState(MyBlendStateManager.BlendReplace);
        BindRtv(rc, dest);
        rc.SetInputLayout(null);
        rc.SetPrimitiveTopology(PrimitiveTopology.TriangleList);
        rc.SetVertexBuffer(0, null);
        rc.GeometryShader.Set(null);
        rc.VertexShader.Set(vertexShader);
        rc.PixelShader.Set(isolatedVelocityShader);
        rc.PixelShader.SetConstantBuffer(0, isolatedVelocityCb);
        rc.PixelShader.SetSampler(0, MySamplerStateManager.Point);
        rc.PixelShader.SetSrv(0, isolated);
        rc.PixelShader.SetSrv(1, currentSrv);
        rc.Draw(3, 0);
        rc.PixelShader.SetSrv(0, null);
        rc.PixelShader.SetSrv(1, null);
        rc.PixelShader.SetConstantBuffer(0, null);
        rc.SetRtvNull();

        PublishContribute(dest, size.X, size.Y);
    }

    static IRtvTexture NextContributeTarget(int w, int h)
    {
        if (w <= 0 || h <= 0)
            return null;
        if (ContributeTargets[0] == null || ContributeTargets[0].Size.X != w ||
            ContributeTargets[0].Size.Y != h)
        {
            DisposeContributeTargets();
            ContributeTargets[0] = MyManagers.RwTextures.CreateRtv("Anomaly.VelocityContribute.A", w, h,
                Format.R16G16_Float);
            ContributeTargets[1] = MyManagers.RwTextures.CreateRtv("Anomaly.VelocityContribute.B", w, h,
                Format.R16G16_Float);
            contributeWrite = 1;
            width = w;
            height = h;
        }

        if (ContributeTargets[0] == null || ContributeTargets[1] == null)
            return null;
        contributeWrite ^= 1;
        return ContributeTargets[contributeWrite];
    }

    static void PublishContribute(IRtvTexture dest, int w, int h)
    {
        if (dest == null)
            return;
        var native = dest.Resource != null ? dest.Resource.NativePointer : IntPtr.Zero;
        CameraVelocityBuffer.Instance.Publish(dest, native, w, h, FrameTemporal.HistoryValid);
        VelocityRegistry.SetActive(CameraVelocityBuffer.Instance);
    }

    internal static void CollectWarmupJobs(List<ShaderWarmup.Job> jobs)
    {
        lock (Gate)
        {
            if (!shadersReady)
            {
                ShaderWarmup.Add(jobs, FindHlsl(VsFile), MyShaderProfile.vs_5_0, "Anomaly.Fullscreen");
                ShaderWarmup.Add(jobs, FindHlsl(ClearPsFile), MyShaderProfile.ps_5_0, "Anomaly.ReactiveClear");
                ShaderWarmup.Add(jobs, FindHlsl(ContributePsFile), MyShaderProfile.ps_5_0,
                    "Anomaly.VelocityContribute");
            }

            if (stampShader == null)
                ShaderWarmup.Add(jobs, FindHlsl(StampPsFile), MyShaderProfile.ps_5_0, "Anomaly.ReactiveStamp");
            if (stampAlphaShader == null)
                ShaderWarmup.Add(jobs, FindHlsl(StampPsFile), MyShaderProfile.ps_5_0,
                    "Anomaly.ReactiveStampAlpha", new ShaderMacro("STAMP_ALPHA", "1"));
            if (isolatedVelocityShader == null)
                ShaderWarmup.Add(jobs, FindHlsl(IsolatedVelocityPsFile), MyShaderProfile.ps_5_0,
                    "Anomaly.IsolatedVelocity");
        }
    }

    internal static void Prewarm()
    {
        lock (Gate)
        {
            EnsureShadersUnlocked();
            EnsureStampShaderUnlocked();
            EnsureIsolatedVelocityUnlocked();
        }
    }

    static void EnsureShadersUnlocked()
    {
        if (shadersReady)
            return;
        var vsPath = FindHlsl(VsFile);
        var clearPath = FindHlsl(ClearPsFile);
        var contribPath = FindHlsl(ContributePsFile);
        if (vsPath == null || clearPath == null || contribPath == null)
        {
            Fail("HLSL not found (Fullscreen / ReactiveClear / VelocityContribute)");
            return;
        }

        var vsBc = MyShaderCompiler.Compile(vsPath, Array.Empty<ShaderMacro>(), MyShaderProfile.vs_5_0,
            "Anomaly.Fullscreen", invalidateCache: false);
        var clearBc = MyShaderCompiler.Compile(clearPath, Array.Empty<ShaderMacro>(), MyShaderProfile.ps_5_0,
            "Anomaly.ReactiveClear", invalidateCache: false);
        var contribBc = MyShaderCompiler.Compile(contribPath, Array.Empty<ShaderMacro>(), MyShaderProfile.ps_5_0,
            "Anomaly.VelocityContribute", invalidateCache: false);
        if (vsBc == null || vsBc.Length == 0 || clearBc == null || clearBc.Length == 0 ||
            contribBc == null || contribBc.Length == 0)
        {
            Fail("shader compile returned empty bytecode");
            return;
        }

        var device = MyRender11.DeviceInstance;
        vertexShader = new VertexShader(device, vsBc) { DebugName = "Anomaly.Temporal.VS" };
        clearShader = new PixelShader(device, clearBc) { DebugName = "Anomaly.ReactiveClear" };
        contributeShader = new PixelShader(device, contribBc) { DebugName = "Anomaly.VelocityContribute" };
        shadersReady = true;
        EnsureStampShaderUnlocked();
        EnsureIsolatedVelocityUnlocked();
    }

    static void EnsureIsolatedVelocityUnlocked()
    {
        if (isolatedVelocityShader != null && isolatedVelocityCb != null)
            return;
        var path = FindHlsl(IsolatedVelocityPsFile);
        if (path == null)
        {
            Fail("HLSL not found (IsolatedVelocity)");
            return;
        }

        var bc = MyShaderCompiler.Compile(path, Array.Empty<ShaderMacro>(), MyShaderProfile.ps_5_0,
            "Anomaly.IsolatedVelocity", invalidateCache: false);
        if (bc == null || bc.Length == 0)
        {
            Fail("IsolatedVelocity compile returned empty bytecode");
            return;
        }

        isolatedVelocityShader = new PixelShader(MyRender11.DeviceInstance, bc)
        {
            DebugName = "Anomaly.IsolatedVelocity"
        };
        if (isolatedVelocityCb == null)
            isolatedVelocityCb = MyManagers.Buffers.CreateConstantBuffer("Anomaly.IsolatedVelocityCB",
                IsolatedVelocityCbBytes, usage: ResourceUsage.Dynamic);
    }

    static void EnsureStampShaderUnlocked()
    {
        var stampPath = FindHlsl(StampPsFile);
        if (stampPath == null)
        {
            Fail("HLSL not found (ReactiveStamp)");
            return;
        }

        if (stampShader == null)
        {
            var stampBc = MyShaderCompiler.Compile(stampPath, Array.Empty<ShaderMacro>(),
                MyShaderProfile.ps_5_0, "Anomaly.ReactiveStamp", invalidateCache: false);
            if (stampBc == null || stampBc.Length == 0)
            {
                Fail("ReactiveStamp compile returned empty bytecode");
                return;
            }

            stampShader = new PixelShader(MyRender11.DeviceInstance, stampBc)
            {
                DebugName = "Anomaly.ReactiveStamp"
            };
        }

        if (stampAlphaShader == null)
        {
            var alphaBc = MyShaderCompiler.Compile(stampPath,
                new[] { new ShaderMacro("STAMP_ALPHA", "1") }, MyShaderProfile.ps_5_0,
                "Anomaly.ReactiveStampAlpha", invalidateCache: false);
            if (alphaBc == null || alphaBc.Length == 0)
            {
                Fail("ReactiveStampAlpha compile returned empty bytecode");
                return;
            }

            stampAlphaShader = new PixelShader(MyRender11.DeviceInstance, alphaBc)
            {
                DebugName = "Anomaly.ReactiveStampAlpha"
            };
        }
    }

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
        if (string.IsNullOrEmpty(asmDir))
            return null;
        var fallback = Path.Combine(asmDir, "Shaders", fileName);
        return File.Exists(fallback) ? Path.GetFullPath(fallback) : null;
    }

    /// <summary>
    /// Keen <c>SetRtvNull</c> skips the native OM call on a fresh deferred
    /// tracker. Force one RTV so ConsumeWork cannot replay leftover LBuffer.
    /// </summary>
    static void BindRtv(MyRenderContext rc, IRtvBindable rtv)
    {
        rc.ResetTargets();
        if (rtv?.Rtv == null)
            return;
        if (rc.DeviceContext != null)
            rc.DeviceContext.OutputMerger.SetTargets(null, 1, new[] { rtv.Rtv });
        rc.SetRtv(rtv);
    }

    static void DisposeTargets()
    {
        ReactiveRecovery.Dispose(); StampRecovery.Dispose();
        recoveryReactiveTarget = recoveryStampScratch = null;
        if (reactiveTarget != null)
            MyManagers.RwTextures.DisposeTex(ref reactiveTarget);
        if (stampScratch != null)
            MyManagers.RwTextures.DisposeTex(ref stampScratch);
        DisposeContributeTargets();
        reactiveTarget = null;
        stampScratch = null;
        width = 0;
        height = 0;
        clearedThisFrame = false;
        stampedThisFrame = false;
    }

    static void DisposeContributeTargets()
    {
        for (var i = 0; i < ContributeTargets.Length; i++)
        {
            if (ContributeTargets[i] == null)
                continue;
            MyManagers.RwTextures.DisposeTex(ref ContributeTargets[i]);
            ContributeTargets[i] = null;
        }

        contributeWrite = 0;
    }

    static void DisposeShaders()
    {
        if (isolatedVelocityCb != null)
        {
            MyManagers.Buffers.Dispose(new[] { isolatedVelocityCb });
            isolatedVelocityCb = null;
        }

        vertexShader?.Dispose();
        clearShader?.Dispose();
        stampShader?.Dispose();
        stampAlphaShader?.Dispose();
        contributeShader?.Dispose();
        isolatedVelocityShader?.Dispose();
        vertexShader = null;
        clearShader = null;
        stampShader = null;
        stampAlphaShader = null;
        contributeShader = null;
        isolatedVelocityShader = null;
    }

    static void Fail(string message)
    {
        if (loggedError)
            return;
        loggedError = true;
        MyLog.Default.WriteLine("Anomaly temporal: " + message);
        DebugLog.Write("TemporalParticipation " + message);
    }
}
