using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ClientPlugin.Buffers;
using ClientPlugin.Shaders;
using SharpDX.D3DCompiler;
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
/// Owned linear view-depth, half-res min Hi-Z, and previous-frame HDR color.
/// Linear/Hi-Z run after <c>MyRenderScheduler.Done</c>. History is copied after
/// post at <c>DrawGameScene</c> postfix so TAA still sees last frame during post.
/// </summary>
public static class OwnedBuffersPass
{
    static readonly object Gate = new();
    const int ConstantBufferBytes = 256;
    const string VsFile = "Fullscreen.hlsl";
    const string LinearPsFile = "LinearDepth.hlsl";
    const string HiZPsFile = "HiZDownsample.hlsl";
    const string HistoryPsFile = "HistoryCopy.hlsl";

    public static bool Enabled { get; set; } = true;
    public static string LastError { get; private set; }
    public static bool ShadersReady { get; private set; }

    static readonly CatalogTexture LinearPublished = new();
    static readonly CatalogTexture HiZPublished = new();
    static readonly CatalogTexture HistoryPublished = new();

    static VertexShader vertexShader;
    static PixelShader linearShader;
    static PixelShader hiZShader;
    static PixelShader historyShader;
    static IConstantBuffer linearCb;
    static IRtvTexture linearTarget;
    static IRtvTexture hiZTarget;
    static IRtvTexture historyTarget;
    static IRtvTexture msaaScratch;
    static int linearWidth;
    static int linearHeight;
    static int hiZWidth;
    static int hiZHeight;
    static int historyWidth;
    static int historyHeight;
    static int scratchWidth;
    static int scratchHeight;
    static Format scratchFormat;
    static bool loggedError;

    [StructLayout(LayoutKind.Sequential, Size = ConstantBufferBytes)]
    struct LinearConstants
    {
        public float Proj33;
        public float Proj43;
        public float Pad0;
        public float Pad1;
    }

    public static string StatusLine
    {
        get
        {
            lock (Gate)
            {
                if (!Enabled)
                    return "disabled";
                if (!string.IsNullOrEmpty(LastError))
                    return "error (" + LastError + ")";
                if (!ShadersReady)
                    return "shaders not ready";
                return "linearDepth " + FormatTex(LinearPublished)
                    + "; hiZ " + FormatTex(HiZPublished)
                    + "; historyColor " + FormatTex(HistoryPublished);
            }
        }
    }

    public static void Execute()
    {
        if (!Enabled)
            return;

        lock (Gate)
        {
            if (!Enabled)
                return;
            if (!WantDepthBuffers())
            {
                ClearDepthCatalog();
                return;
            }
            try
            {
                ExecuteDepthUnlocked();
            }
            catch (Exception e)
            {
                Fail("execute: " + e.GetType().Name + ": " + e.Message, e);
                ClearDepthCatalog();
            }
        }
    }

    public static void CaptureHistory()
    {
        if (!Enabled)
            return;

        lock (Gate)
        {
            if (!Enabled)
                return;
            if (!WantHistoryBuffer())
            {
                ClearHistoryCatalog();
                return;
            }
            try
            {
                CaptureHistoryUnlocked();
            }
            catch (Exception e)
            {
                Fail("history: " + e.GetType().Name + ": " + e.Message, e);
                ClearHistoryCatalog();
            }
        }
    }

    public static void OnResolutionChanged()
    {
        if (!Enabled)
            return;
        lock (Gate)
        {
            DisposeTargets();
            ClearDepthCatalog();
            ClearHistoryCatalog();
        }
    }

    public static void Release()
    {
        lock (Gate)
        {
            ClearDepthCatalog();
            ClearHistoryCatalog();
            DisposeTargets();
            DisposeShadersAndCb();
            ShadersReady = false;
        }
    }

    static bool WantDepthBuffers()
    {
        if (ShaderPackRegistry.LivePackCount > 0)
            return true;
        var dbg = Config.Current?.DebugBuffer ?? DebugBuffer.Off;
        return dbg == DebugBuffer.LinearDepth || dbg == DebugBuffer.HiZ;
    }

    static bool WantHistoryBuffer()
    {
        if (ShaderPackRegistry.LivePackCount > 0)
            return true;
        return (Config.Current?.DebugBuffer ?? DebugBuffer.Off) == DebugBuffer.HistoryColor;
    }

    static bool WantHiZ()
    {
        if (ShaderPackRegistry.LivePackCount > 0)
            return true;
        return (Config.Current?.DebugBuffer ?? DebugBuffer.Off) == DebugBuffer.HiZ;
    }

    static void ExecuteDepthUnlocked()
    {
        var gbuffer = MyGBuffer.Main;
        var depth = gbuffer?.ResolvedDepthStencil?.SrvDepth;
        var rc = MyRender11.RC;
        if (gbuffer == null || depth == null || rc == null || !rc.IsInitialized)
            return;

        var env = MyRender11.Environment?.Matrices;
        if (env == null)
            return;

        EnsureShaders();
        EnsureDepthTargets();
        EnsureLinearCb();
        if (!ShadersReady || linearTarget == null || linearCb == null ||
            vertexShader == null || linearShader == null)
            return;
        if (WantHiZ() && (hiZTarget == null || hiZShader == null))
            return;

        var cb = new LinearConstants
        {
            Proj33 = env.Projection.M33,
            Proj43 = env.Projection.M43
        };
        var mapping = MyMapping.MapDiscard(rc, linearCb);
        mapping.WriteAndPosition(ref cb);
        mapping.Unmap();

        BindFullscreen(rc, linearShader);
        rc.SetScreenViewport();
        rc.SetRtv(linearTarget);
        rc.PixelShader.SetConstantBuffer(0, linearCb);
        rc.PixelShader.SetSampler(0, MySamplerStateManager.Point);
        rc.PixelShader.SetSrv(0, depth);
        rc.Draw(3, 0);

        if (WantHiZ() && hiZTarget != null && hiZShader != null)
        {
            BindFullscreen(rc, hiZShader);
            rc.SetViewport(0f, 0f, hiZWidth, hiZHeight, 0f, 1f);
            rc.SetRtv(hiZTarget);
            rc.PixelShader.SetConstantBuffer(0, null);
            rc.PixelShader.SetSampler(0, null);
            rc.PixelShader.SetSrv(0, linearTarget);
            rc.Draw(3, 0);
            Publish(HiZPublished, BufferCatalog.HiZ, hiZTarget, hiZWidth, hiZHeight);
        }
        else
        {
            HiZPublished.Clear();
            BufferCatalog.Set(BufferCatalog.HiZ, null);
        }

        rc.ClearState();

        Publish(LinearPublished, BufferCatalog.LinearDepth, linearTarget, linearWidth, linearHeight);
        LastError = null;
        loggedError = false;
    }

    static void CaptureHistoryUnlocked()
    {
        var gbuffer = MyGBuffer.Main;
        var lbuffer = gbuffer?.LBuffer;
        var rc = MyRender11.RC;
        if (gbuffer == null || lbuffer == null || rc == null || !rc.IsInitialized)
            return;

        EnsureShaders();
        EnsureHistoryTarget();
        if (!ShadersReady || historyTarget == null || historyShader == null || vertexShader == null)
            return;

        ISrvBindable src = lbuffer;
        var samples = Math.Max(gbuffer.SamplesCount, 1);
        if (samples > 1)
        {
            var format = lbuffer.Format;
            EnsureMsaaScratch(format);
            if (msaaScratch == null || rc.DeviceContext == null)
                return;
            rc.DeviceContext.ResolveSubresource(lbuffer.Resource, 0, msaaScratch.Resource, 0, format);
            src = msaaScratch;
        }

        BindFullscreen(rc, historyShader);
        rc.SetScreenViewport();
        rc.SetRtv(historyTarget);
        rc.PixelShader.SetSampler(0, MySamplerStateManager.Point);
        rc.PixelShader.SetSrv(0, src);
        rc.Draw(3, 0);
        rc.ClearState();

        Publish(HistoryPublished, BufferCatalog.HistoryColor, historyTarget, historyWidth, historyHeight);
        LastError = null;
        loggedError = false;
    }

    static void BindFullscreen(MyRenderContext rc, PixelShader ps)
    {
        rc.SetRasterizerState(MyRasterizerStateManager.NocullRasterizerState);
        rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
        rc.SetBlendState(MyBlendStateManager.BlendReplace);
        rc.SetInputLayout(null);
        rc.SetPrimitiveTopology(PrimitiveTopology.TriangleList);
        rc.SetVertexBuffer(0, null);
        rc.GeometryShader.Set(null);
        rc.VertexShader.Set(vertexShader);
        rc.PixelShader.Set(ps);
    }

    static void EnsureShaders()
    {
        if (vertexShader != null && linearShader != null && hiZShader != null && historyShader != null)
        {
            ShadersReady = true;
            return;
        }

        var vsPath = FindHlsl(VsFile);
        var linearPath = FindHlsl(LinearPsFile);
        var hiZPath = FindHlsl(HiZPsFile);
        var historyPath = FindHlsl(HistoryPsFile);
        if (vsPath == null || linearPath == null || hiZPath == null || historyPath == null)
        {
            Fail("HLSL not found (Fullscreen / LinearDepth / HiZDownsample / HistoryCopy) under " +
                 (ShaderCompileIntercept.IncludeDirectory ?? "(no include dir)"), null);
            return;
        }

        var vsBc = MyShaderCompiler.Compile(vsPath, Array.Empty<ShaderMacro>(), MyShaderProfile.vs_5_0,
            "Anomaly.Fullscreen", invalidateCache: false);
        var linearBc = MyShaderCompiler.Compile(linearPath, Array.Empty<ShaderMacro>(), MyShaderProfile.ps_5_0,
            "Anomaly.LinearDepth", invalidateCache: false);
        var hiZBc = MyShaderCompiler.Compile(hiZPath, Array.Empty<ShaderMacro>(), MyShaderProfile.ps_5_0,
            "Anomaly.HiZ", invalidateCache: false);
        var historyBc = MyShaderCompiler.Compile(historyPath, Array.Empty<ShaderMacro>(), MyShaderProfile.ps_5_0,
            "Anomaly.HistoryColor", invalidateCache: false);
        if (Empty(vsBc) || Empty(linearBc) || Empty(hiZBc) || Empty(historyBc))
        {
            Fail("shader compile returned empty bytecode", null);
            return;
        }

        var device = MyRender11.DeviceInstance;
        vertexShader?.Dispose();
        linearShader?.Dispose();
        hiZShader?.Dispose();
        historyShader?.Dispose();
        vertexShader = new VertexShader(device, vsBc) { DebugName = "Anomaly.OwnedBuffers.VS" };
        linearShader = new PixelShader(device, linearBc) { DebugName = "Anomaly.LinearDepth" };
        hiZShader = new PixelShader(device, hiZBc) { DebugName = "Anomaly.HiZ" };
        historyShader = new PixelShader(device, historyBc) { DebugName = "Anomaly.HistoryColor" };
        ShadersReady = true;
        MyLog.Default.WriteLine("Anomaly owned-buffer shaders compiled.");
        DebugLog.Write("OwnedBuffersPass shaders ok");
    }

    static void EnsureDepthTargets()
    {
        var size = MyRender11.ResolutionI;
        if (size.X <= 0 || size.Y <= 0)
            return;

        var halfX = Math.Max(1, size.X / 2);
        var halfY = Math.Max(1, size.Y / 2);
        if (linearTarget != null && linearWidth == size.X && linearHeight == size.Y &&
            hiZTarget != null && hiZWidth == halfX && hiZHeight == halfY)
            return;

        DisposeDepthTargets();
        linearTarget = MyManagers.RwTextures.CreateRtv("Anomaly.LinearDepth", size.X, size.Y, Format.R32_Float);
        hiZTarget = MyManagers.RwTextures.CreateRtv("Anomaly.HiZ", halfX, halfY, Format.R32_Float);
        linearWidth = size.X;
        linearHeight = size.Y;
        hiZWidth = halfX;
        hiZHeight = halfY;
        ClearDepthCatalog();
        DebugLog.Write("OwnedBuffersPass depth RT " + size.X + "x" + size.Y + " hiZ " + halfX + "x" + halfY);
    }

    static void EnsureHistoryTarget()
    {
        var size = MyRender11.ResolutionI;
        if (size.X <= 0 || size.Y <= 0)
            return;
        if (historyTarget != null && historyWidth == size.X && historyHeight == size.Y)
            return;

        DisposeHistoryTargets();
        historyTarget = MyManagers.RwTextures.CreateRtv("Anomaly.HistoryColor", size.X, size.Y,
            Format.R16G16B16A16_Float);
        historyWidth = size.X;
        historyHeight = size.Y;
        ClearHistoryCatalog();
        DebugLog.Write("OwnedBuffersPass history RT " + size.X + "x" + size.Y);
    }

    static void EnsureMsaaScratch(Format format)
    {
        var size = MyRender11.ResolutionI;
        if (size.X <= 0 || size.Y <= 0 || format == Format.Unknown)
            return;
        if (msaaScratch != null && scratchWidth == size.X && scratchHeight == size.Y && scratchFormat == format)
            return;

        DisposeScratch();
        msaaScratch = MyManagers.RwTextures.CreateRtv("Anomaly.HistoryColor.Resolve", size.X, size.Y, format);
        scratchWidth = size.X;
        scratchHeight = size.Y;
        scratchFormat = format;
    }

    static void EnsureLinearCb()
    {
        if (linearCb != null)
            return;
        linearCb = MyManagers.Buffers.CreateConstantBuffer("Anomaly.LinearDepthCB", ConstantBufferBytes,
            usage: ResourceUsage.Dynamic);
    }

    static void Publish(CatalogTexture tex, string name, IRtvTexture target, int w, int h)
    {
        var native = target?.Resource != null ? target.Resource.NativePointer : IntPtr.Zero;
        tex.Publish(target, native, w, h);
        BufferCatalog.Set(name, tex);
    }

    static void ClearDepthCatalog()
    {
        LinearPublished.Clear();
        HiZPublished.Clear();
        BufferCatalog.Set(BufferCatalog.LinearDepth, null);
        BufferCatalog.Set(BufferCatalog.HiZ, null);
    }

    static void ClearHistoryCatalog()
    {
        HistoryPublished.Clear();
        BufferCatalog.Set(BufferCatalog.HistoryColor, null);
    }

    static string FormatTex(CatalogTexture tex)
    {
        if (tex == null || !tex.IsAvailable)
            return "—";
        return tex.Width + "x" + tex.Height + " live";
    }

    static bool Empty(byte[] bc) => bc == null || bc.Length == 0;

    static string FindHlsl(string fileName)
    {
        if (ShaderPackRegistry.TryResolveOverlay(fileName, out var overlay) && File.Exists(overlay))
            return overlay;
        foreach (var dir in ShaderDirs())
        {
            var path = Path.Combine(dir, fileName);
            if (File.Exists(path))
                return Path.GetFullPath(path);
        }

        return null;
    }

    static System.Collections.Generic.IEnumerable<string> ShaderDirs()
    {
        if (!string.IsNullOrEmpty(ShaderCompileIntercept.IncludeDirectory))
            yield return ShaderCompileIntercept.IncludeDirectory;
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrEmpty(asmDir))
            yield return Path.Combine(asmDir, "Shaders");
    }

    static void DisposeTargets()
    {
        DisposeDepthTargets();
        DisposeHistoryTargets();
    }

    static void DisposeDepthTargets()
    {
        if (linearTarget != null)
            MyManagers.RwTextures.DisposeTex(ref linearTarget);
        if (hiZTarget != null)
            MyManagers.RwTextures.DisposeTex(ref hiZTarget);
        linearTarget = null;
        hiZTarget = null;
        linearWidth = 0;
        linearHeight = 0;
        hiZWidth = 0;
        hiZHeight = 0;
    }

    static void DisposeHistoryTargets()
    {
        if (historyTarget != null)
            MyManagers.RwTextures.DisposeTex(ref historyTarget);
        historyTarget = null;
        historyWidth = 0;
        historyHeight = 0;
        DisposeScratch();
    }

    static void DisposeScratch()
    {
        if (msaaScratch != null)
            MyManagers.RwTextures.DisposeTex(ref msaaScratch);
        msaaScratch = null;
        scratchWidth = 0;
        scratchHeight = 0;
        scratchFormat = Format.Unknown;
    }

    static void DisposeShadersAndCb()
    {
        if (linearCb != null)
        {
            MyManagers.Buffers.Dispose(new[] { linearCb });
            linearCb = null;
        }

        vertexShader?.Dispose();
        linearShader?.Dispose();
        hiZShader?.Dispose();
        historyShader?.Dispose();
        vertexShader = null;
        linearShader = null;
        hiZShader = null;
        historyShader = null;
        ShadersReady = false;
    }

    static void Fail(string message, Exception e)
    {
        LastError = message;
        if (loggedError)
            return;
        loggedError = true;
        MyLog.Default.WriteLine("Anomaly owned buffers: " + message);
        DebugLog.Write("OwnedBuffersPass " + message + (e != null ? "\n" + e : ""));
    }
}
