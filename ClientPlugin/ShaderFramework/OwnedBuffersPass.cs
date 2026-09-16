using System;
using System.Collections.Generic;
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
/// Owned linear view-depth, half-res min Hi-Z, previous-frame HDR color,
/// and request-driven this-frame <c>litMips</c> (GenerateMips of LBuffer).
/// Linear/Hi-Z run after <c>MyRenderScheduler.Done</c>. History is copied after
/// post at <c>DrawGameScene</c> postfix so TAA still sees last frame during post.
/// <c>litMips</c> is filled at AfterLighting (before fullscreen programs).
/// </summary>
public static class OwnedBuffersPass
{
    static readonly object Gate = new();
    const int ConstantBufferBytes = 256;
    const int DefaultLitMips = 5;
    const int MinLitMips = 2;
    const int MaxLitMips = 12;
    const string VsFile = "Fullscreen.hlsl";
    const string LinearPsFile = "LinearDepth.hlsl";
    const string HiZPsFile = "HiZDownsample.hlsl";
    const string HistoryPsFile = "HistoryCopy.hlsl";

    public static bool Enabled { get; set; } = true;
    public static string LastError { get; private set; }
    public static bool ShadersReady { get; private set; }

    static readonly CatalogTexture LinearPublished = new();
    static readonly CatalogTexture HistoryDepthPublished = new();
    static readonly CatalogTexture HiZPublished = new();
    static readonly CatalogTexture HistoryPublished = new();
    static readonly CatalogTexture LitMipsPublished = new();

    static VertexShader vertexShader;
    static PixelShader linearShader;
    static PixelShader hiZShader;
    static PixelShader historyShader;
    static IConstantBuffer linearCb;
    static readonly IRtvTexture[] linearTargets = new IRtvTexture[2];
    static int linearWriteIndex = 1;
    static bool linearHistoryReady;
    static IRtvTexture hiZTarget;
    static IRtvTexture historyTarget;
    static IRtvTexture litMipsTarget;
    static IRtvTexture msaaScratch;
    static int linearWidth;
    static int linearHeight;
    static int hiZWidth;
    static int hiZHeight;
    static int historyWidth;
    static int historyHeight;
    static int litMipsWidth;
    static int litMipsHeight;
    static int litMipsLevels;
    static int scratchWidth;
    static int scratchHeight;
    static Format scratchFormat;
    static int explicitMipLevels;
    static bool litMipsDoneThisFrame;
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
                    + "; historyDepth " + FormatTex(HistoryDepthPublished)
                    + "; hiZ " + FormatTex(HiZPublished)
                    + "; historyColor " + FormatTex(HistoryPublished)
                    + "; litMips " + FormatTex(LitMipsPublished);
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
                RenderTrace.Begin("OwnedBuffers");
                ExecuteDepthUnlocked();
            }
            catch (Exception e)
            {
                Fail("execute: " + e.GetType().Name + ": " + e.Message, e);
                ClearDepthCatalog();
            }
            finally
            {
                RenderTrace.End("OwnedBuffers");
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
                RenderTrace.Begin("historyColor");
                CaptureHistoryUnlocked();
            }
            catch (Exception e)
            {
                Fail("history: " + e.GetType().Name + ": " + e.Message, e);
                ClearHistoryCatalog();
            }
            finally
            {
                RenderTrace.End("historyColor");
            }
        }
    }

    public static void BeginFrame()
    {
        lock (Gate)
            litMipsDoneThisFrame = false;
    }

    /// <summary>
    /// Sticky request for catalog <c>litMips</c>. <paramref name="mipLevels"/>
    /// <c>&lt;= 0</c> clears the explicit request.
    /// </summary>
    public static void RequestLitMips(int mipLevels = DefaultLitMips)
    {
        lock (Gate)
        {
            if (mipLevels <= 0)
            {
                explicitMipLevels = 0;
                return;
            }

            var clamped = mipLevels;
            if (clamped < MinLitMips)
                clamped = MinLitMips;
            if (clamped > MaxLitMips)
                clamped = MaxLitMips;
            if (clamped > explicitMipLevels)
                explicitMipLevels = clamped;
        }
    }

    public static void ExecuteLitMips(MyRenderContext rc)
    {
        if (!Enabled || rc == null || !rc.IsInitialized)
            return;

        var want = WantLitMips();
        lock (Gate)
        {
            if (!Enabled || litMipsDoneThisFrame)
                return;
            litMipsDoneThisFrame = true;
            if (!want)
            {
                ClearLitMipsCatalog();
                return;
            }

            try
            {
                RenderTrace.Begin("litMips");
                ExecuteLitMipsUnlocked(rc);
            }
            catch (Exception e)
            {
                Fail("litMips: " + e.GetType().Name + ": " + e.Message, e);
                ClearLitMipsCatalog();
            }
            finally
            {
                RenderTrace.End("litMips");
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
            ClearLitMipsCatalog();
        }
    }

    public static void Release()
    {
        lock (Gate)
        {
            ClearDepthCatalog();
            ClearHistoryCatalog();
            ClearLitMipsCatalog();
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

    static bool WantLitMips()
    {
        if (explicitMipLevels > 0)
            return true;
        if ((Config.Current?.DebugBuffer ?? DebugBuffer.Off) == DebugBuffer.LitMips)
            return true;
        return FullscreenPassRegistry.WantsCatalog(BufferCatalog.LitMips);
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
        if (!ShadersReady || linearTargets[0] == null || linearTargets[1] == null ||
            linearCb == null || vertexShader == null || linearShader == null)
            return;
        if (WantHiZ() && (hiZTarget == null || hiZShader == null))
            return;

        var dest = NextLinearWrite();
        if (dest == null)
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
        // AfterAtmosphere IsolatedAdd may still be sampling last frame's
        // published ping. Write the other. Lighting's leftover LBuffer MRT
        // must not stay bound when we sample depth / later LBuffer.
        rc.PixelShader.SetSrv(0, null);
        rc.PixelShader.SetSrv(1, null);
        BindOwnedRtv(rc, dest);
        rc.PixelShader.SetConstantBuffer(0, linearCb);
        rc.PixelShader.SetSampler(0, MySamplerStateManager.Point);
        rc.PixelShader.SetSrv(0, depth);
        rc.Draw(3, 0);

        if (WantHiZ() && hiZTarget != null && hiZShader != null)
        {
            BindFullscreen(rc, hiZShader);
            rc.SetViewport(0f, 0f, hiZWidth, hiZHeight, 0f, 1f);
            rc.PixelShader.SetSrv(0, null);
            BindOwnedRtv(rc, hiZTarget);
            rc.PixelShader.SetConstantBuffer(0, null);
            rc.PixelShader.SetSampler(0, null);
            rc.PixelShader.SetSrv(0, dest);
            rc.Draw(3, 0);
            Publish(HiZPublished, BufferCatalog.HiZ, hiZTarget, hiZWidth, hiZHeight);
        }
        else
        {
            HiZPublished.Clear();
            BufferCatalog.Set(BufferCatalog.HiZ, null);
        }

        rc.ClearState();

        Publish(LinearPublished, BufferCatalog.LinearDepth, dest, linearWidth, linearHeight);
        var hist = linearTargets[linearWriteIndex ^ 1];
        if (linearHistoryReady && hist != null)
            Publish(HistoryDepthPublished, BufferCatalog.HistoryDepth, hist, linearWidth, linearHeight);
        else
        {
            HistoryDepthPublished.Clear();
            BufferCatalog.Set(BufferCatalog.HistoryDepth, null);
        }

        linearHistoryReady = true;
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
        BindOwnedRtv(rc, historyTarget);
        rc.PixelShader.SetSampler(0, MySamplerStateManager.Point);
        rc.PixelShader.SetSrv(0, src);
        rc.Draw(3, 0);
        rc.ClearState();

        Publish(HistoryPublished, BufferCatalog.HistoryColor, historyTarget, historyWidth, historyHeight);
        LastError = null;
        loggedError = false;
    }

    static void ExecuteLitMipsUnlocked(MyRenderContext rc)
    {
        var gbuffer = MyGBuffer.Main;
        var lbuffer = gbuffer?.LBuffer;
        if (gbuffer == null || lbuffer == null)
            return;

        EnsureShaders();
        var levels = explicitMipLevels > 0 ? explicitMipLevels : DefaultLitMips;
        EnsureLitMipsTarget(levels);
        if (!ShadersReady || litMipsTarget == null || historyShader == null || vertexShader == null)
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
        BindOwnedRtv(rc, litMipsTarget);
        rc.PixelShader.SetSampler(0, MySamplerStateManager.Linear);
        rc.PixelShader.SetSrv(0, src);
        rc.Draw(3, 0);
        rc.PixelShader.SetSrv(0, null);
        rc.SetRtvNull();
        rc.GenerateMips(litMipsTarget);
        rc.ClearState();

        Publish(LitMipsPublished, BufferCatalog.LitMips, litMipsTarget, litMipsWidth, litMipsHeight);
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

    /// <summary>
    /// Keen <c>SetRtv</c> on lighting's deferred list leaves LBuffer / GBuffer
    /// MRT slots live. Sampling those as SRVs is the AfterLighting TDR.
    /// </summary>
    static void BindOwnedRtv(MyRenderContext rc, IRtvBindable rtv)
    {
        rc.ResetTargets();
        if (rtv?.Rtv == null)
            return;
        if (rc.DeviceContext != null)
            rc.DeviceContext.OutputMerger.SetTargets(null, 1, new[] { rtv.Rtv });
        rc.SetRtv(rtv);
    }

    internal static void CollectWarmupJobs(List<ShaderWarmup.Job> jobs)
    {
        lock (Gate)
        {
            if (vertexShader != null && linearShader != null && hiZShader != null && historyShader != null)
                return;
            ShaderWarmup.Add(jobs, FindHlsl(VsFile), MyShaderProfile.vs_5_0, "Anomaly.Fullscreen");
            ShaderWarmup.Add(jobs, FindHlsl(LinearPsFile), MyShaderProfile.ps_5_0, "Anomaly.LinearDepth");
            ShaderWarmup.Add(jobs, FindHlsl(HiZPsFile), MyShaderProfile.ps_5_0, "Anomaly.HiZ");
            ShaderWarmup.Add(jobs, FindHlsl(HistoryPsFile), MyShaderProfile.ps_5_0, "Anomaly.HistoryColor");
        }
    }

    internal static void Prewarm()
    {
        lock (Gate)
            EnsureShaders();
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
        if (linearTargets[0] != null && linearTargets[1] != null &&
            linearWidth == size.X && linearHeight == size.Y &&
            hiZTarget != null && hiZWidth == halfX && hiZHeight == halfY)
            return;

        DisposeDepthTargets();
        linearTargets[0] = MyManagers.RwTextures.CreateRtv("Anomaly.LinearDepth.A", size.X, size.Y,
            Format.R32_Float);
        linearTargets[1] = MyManagers.RwTextures.CreateRtv("Anomaly.LinearDepth.B", size.X, size.Y,
            Format.R32_Float);
        hiZTarget = MyManagers.RwTextures.CreateRtv("Anomaly.HiZ", halfX, halfY, Format.R32_Float);
        linearWriteIndex = 1;
        linearWidth = size.X;
        linearHeight = size.Y;
        hiZWidth = halfX;
        hiZHeight = halfY;
        ClearDepthCatalog();
        DebugLog.Write("OwnedBuffersPass depth RT " + size.X + "x" + size.Y +
            " ping-pong hiZ " + halfX + "x" + halfY);
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

    static void EnsureLitMipsTarget(int mipLevels)
    {
        var size = MyRender11.ResolutionI;
        if (size.X <= 0 || size.Y <= 0)
            return;
        if (mipLevels < MinLitMips)
            mipLevels = MinLitMips;
        if (mipLevels > MaxLitMips)
            mipLevels = MaxLitMips;
        if (litMipsTarget != null && litMipsWidth == size.X && litMipsHeight == size.Y &&
            litMipsLevels == mipLevels)
            return;

        DisposeLitMipsTargets();
        litMipsTarget = MyManagers.RwTextures.CreateRtv("Anomaly.LitMips", size.X, size.Y,
            Format.R16G16B16A16_Float, mipLevels: mipLevels,
            optionFlags: ResourceOptionFlags.GenerateMipMaps);
        litMipsWidth = size.X;
        litMipsHeight = size.Y;
        litMipsLevels = mipLevels;
        ClearLitMipsCatalog();
        DebugLog.Write("OwnedBuffersPass litMips RT " + size.X + "x" + size.Y + " mips " + mipLevels);
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
        HistoryDepthPublished.Clear();
        HiZPublished.Clear();
        BufferCatalog.Set(BufferCatalog.LinearDepth, null);
        BufferCatalog.Set(BufferCatalog.HistoryDepth, null);
        BufferCatalog.Set(BufferCatalog.HiZ, null);
    }

    static void ClearHistoryCatalog()
    {
        HistoryPublished.Clear();
        BufferCatalog.Set(BufferCatalog.HistoryColor, null);
    }

    static void ClearLitMipsCatalog()
    {
        LitMipsPublished.Clear();
        BufferCatalog.Set(BufferCatalog.LitMips, null);
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
        DisposeLitMipsTargets();
    }

    static IRtvTexture NextLinearWrite()
    {
        linearWriteIndex ^= 1;
        return linearTargets[linearWriteIndex];
    }

    static void DisposeDepthTargets()
    {
        for (var i = 0; i < linearTargets.Length; i++)
        {
            if (linearTargets[i] == null)
                continue;
            MyManagers.RwTextures.DisposeTex(ref linearTargets[i]);
            linearTargets[i] = null;
        }

        if (hiZTarget != null)
            MyManagers.RwTextures.DisposeTex(ref hiZTarget);
        hiZTarget = null;
        linearWriteIndex = 1;
        linearHistoryReady = false;
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

    static void DisposeLitMipsTargets()
    {
        if (litMipsTarget != null)
            MyManagers.RwTextures.DisposeTex(ref litMipsTarget);
        litMipsTarget = null;
        litMipsWidth = 0;
        litMipsHeight = 0;
        litMipsLevels = 0;
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
        RenderTrace.DumpIfLost("OwnedBuffers", e);
        if (loggedError)
            return;
        loggedError = true;
        MyLog.Default.WriteLine("Anomaly owned buffers: " + message);
        DebugLog.Write("OwnedBuffersPass " + message + (e != null ? "\n" + e : ""));
    }
}
