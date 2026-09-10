using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
using VRageRender;

namespace ClientPlugin.ShaderFramework;

/// <summary>
/// Anomaly-owned <c>reactiveMask</c> and velocity overlay composite.
/// IsolatedAdd (and IsolatedMix / DirectAdd / PublishOnly) with
/// <see cref="TemporalPolicy.Reactive"/> stamps dilated isolated luma.
/// C# owned passes may still write the mask / call
/// <see cref="OwnedPassContext.ContributeVelocity"/>.
/// </summary>
public static class TemporalParticipation
{
    const string VsFile = "Fullscreen.hlsl";
    const string ClearPsFile = "ReactiveClear.hlsl";
    const string StampPsFile = "ReactiveStamp.hlsl";
    const string ContributePsFile = "VelocityContribute.hlsl";

    static readonly object Gate = new();
    static readonly CatalogTexture ReactivePublished = new();

    static VertexShader vertexShader;
    static PixelShader clearShader;
    static PixelShader stampShader;
    static PixelShader contributeShader;
    static IRtvTexture reactiveTarget;
    static IRtvTexture stampScratch;
    static IRtvTexture contributeTarget;
    static int width;
    static int height;
    static bool shadersReady;
    static bool clearedThisFrame;
    static bool stampedThisFrame;
    static bool loggedError;

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

    internal static void StampFromIsolated(MyRenderContext rc, ISrvBindable isolated)
    {
        if (rc == null || !rc.IsInitialized || isolated == null)
            return;
        lock (Gate)
        {
            try
            {
                StampUnlocked(rc, isolated);
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

    static void StampUnlocked(MyRenderContext rc, ISrvBindable isolated)
    {
        EnsureReactiveUnlocked(rc);
        EnsureStampShaderUnlocked();
        var size = MyRender11.ResolutionI;
        if (!shadersReady || vertexShader == null || stampShader == null || reactiveTarget == null)
            return;
        if (size.X <= 0 || size.Y <= 0)
            return;

        var isoRt = isolated as IRtvTexture;
        if (isoRt != null && (isoRt.Size.X != size.X || isoRt.Size.Y != size.Y))
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
        rc.PixelShader.Set(stampShader);
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

        if (contributeTarget == null || width != size.X || height != size.Y)
        {
            if (contributeTarget != null)
                MyManagers.RwTextures.DisposeTex(ref contributeTarget);
            contributeTarget = MyManagers.RwTextures.CreateRtv("Anomaly.VelocityContribute", size.X, size.Y,
                Format.R16G16_Float);
            width = size.X;
            height = size.Y;
        }

        if (contributeTarget == null)
            return;

        rc.SetScreenViewport();
        rc.SetRasterizerState(MyRasterizerStateManager.NocullRasterizerState);
        rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
        rc.SetBlendState(MyBlendStateManager.BlendReplace);
        BindRtv(rc, contributeTarget);
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

        var native = contributeTarget.Resource != null
            ? contributeTarget.Resource.NativePointer
            : IntPtr.Zero;
        CameraVelocityBuffer.Instance.Publish(contributeTarget, native, size.X, size.Y,
            FrameTemporal.HistoryValid);
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
        }
    }

    internal static void Prewarm()
    {
        lock (Gate)
        {
            EnsureShadersUnlocked();
            EnsureStampShaderUnlocked();
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
    }

    static void EnsureStampShaderUnlocked()
    {
        if (stampShader != null)
            return;
        var stampPath = FindHlsl(StampPsFile);
        if (stampPath == null)
        {
            Fail("HLSL not found (ReactiveStamp)");
            return;
        }

        var stampBc = MyShaderCompiler.Compile(stampPath, Array.Empty<ShaderMacro>(), MyShaderProfile.ps_5_0,
            "Anomaly.ReactiveStamp", invalidateCache: false);
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
        if (reactiveTarget != null)
            MyManagers.RwTextures.DisposeTex(ref reactiveTarget);
        if (stampScratch != null)
            MyManagers.RwTextures.DisposeTex(ref stampScratch);
        if (contributeTarget != null)
            MyManagers.RwTextures.DisposeTex(ref contributeTarget);
        reactiveTarget = null;
        stampScratch = null;
        contributeTarget = null;
        width = 0;
        height = 0;
        clearedThisFrame = false;
        stampedThisFrame = false;
    }

    static void DisposeShaders()
    {
        vertexShader?.Dispose();
        clearShader?.Dispose();
        stampShader?.Dispose();
        contributeShader?.Dispose();
        vertexShader = null;
        clearShader = null;
        stampShader = null;
        contributeShader = null;
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
