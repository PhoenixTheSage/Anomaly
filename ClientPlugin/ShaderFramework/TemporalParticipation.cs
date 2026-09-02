using System;
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
/// Packs write the mask / call <see cref="OwnedPassContext.ContributeVelocity"/>.
/// </summary>
public static class TemporalParticipation
{
    const string VsFile = "Fullscreen.hlsl";
    const string ClearPsFile = "ReactiveClear.hlsl";
    const string ContributePsFile = "VelocityContribute.hlsl";

    static readonly object Gate = new();
    static readonly CatalogTexture ReactivePublished = new();

    static VertexShader vertexShader;
    static PixelShader clearShader;
    static PixelShader contributeShader;
    static IRtvTexture reactiveTarget;
    static IRtvTexture contributeTarget;
    static int width;
    static int height;
    static bool shadersReady;
    static bool clearedThisFrame;
    static bool loggedError;

    public static object ReactiveRtv
    {
        get
        {
            lock (Gate)
            {
                EnsureReactiveUnlocked();
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
            clearedThisFrame = false;
    }

    internal static void EnsureReactive()
    {
        lock (Gate)
            EnsureReactiveUnlocked();
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

    static void EnsureReactiveUnlocked()
    {
        var size = MyRender11.ResolutionI;
        if (size.X <= 0 || size.Y <= 0)
            return;
        EnsureShadersUnlocked();
        if (reactiveTarget == null || width != size.X || height != size.Y)
        {
            DisposeTargets();
            reactiveTarget = MyManagers.RwTextures.CreateRtv("Anomaly.ReactiveMask", size.X, size.Y,
                Format.R8_UNorm);
            width = size.X;
            height = size.Y;
            clearedThisFrame = false;
        }

        if (reactiveTarget == null)
            return;

        if (!clearedThisFrame)
        {
            var rc = MyRender11.RC;
            if (rc != null && rc.IsInitialized && shadersReady && clearShader != null && vertexShader != null)
                ClearReactiveUnlocked(rc);
            clearedThisFrame = true;
        }

        var native = reactiveTarget.Resource != null
            ? reactiveTarget.Resource.NativePointer
            : IntPtr.Zero;
        ReactivePublished.Publish(reactiveTarget, native, width, height);
        BufferCatalog.Set(BufferCatalog.ReactiveMask, ReactivePublished);
    }

    static void ClearReactiveUnlocked(MyRenderContext rc)
    {
        rc.SetScreenViewport();
        rc.SetRasterizerState(MyRasterizerStateManager.NocullRasterizerState);
        rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
        rc.SetBlendState(MyBlendStateManager.BlendReplace);
        rc.SetRtv(reactiveTarget);
        rc.SetInputLayout(null);
        rc.SetPrimitiveTopology(PrimitiveTopology.TriangleList);
        rc.SetVertexBuffer(0, null);
        rc.GeometryShader.Set(null);
        rc.VertexShader.Set(vertexShader);
        rc.PixelShader.Set(clearShader);
        rc.Draw(3, 0);
        rc.SetRtvNull();
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
        rc.SetRtv(contributeTarget);
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

    static void DisposeTargets()
    {
        if (reactiveTarget != null)
            MyManagers.RwTextures.DisposeTex(ref reactiveTarget);
        if (contributeTarget != null)
            MyManagers.RwTextures.DisposeTex(ref contributeTarget);
        reactiveTarget = null;
        contributeTarget = null;
        width = 0;
        height = 0;
        clearedThisFrame = false;
    }

    static void DisposeShaders()
    {
        vertexShader?.Dispose();
        clearShader?.Dispose();
        contributeShader?.Dispose();
        vertexShader = null;
        clearShader = null;
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
