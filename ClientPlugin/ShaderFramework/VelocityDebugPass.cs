using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ClientPlugin.Buffers;
using ClientPlugin.Shaders;
using ClientPlugin.Velocity;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using VRage.Render11.Common;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageRender;

namespace ClientPlugin.ShaderFramework;

/// <summary>
/// Fullscreen false-color of a catalog texture onto the backbuffer after
/// <c>DrawGameScene</c> (after the scene copy, so it covers the presented
/// image). Velocity mode binds GBuffer depth so complementary-zero sky
/// is dark grey (silhouettes) instead of rest-gray or saturated camera MVs.
/// Off by default. Unbinds RT/SRV before return.
/// </summary>
public static class VelocityDebugPass
{
    const int ConstantBufferBytes = 256;
    const string VsFile = "Fullscreen.hlsl";
    const string PsFile = "CatalogDebug.hlsl";

    static readonly object Gate = new();

    static VertexShader vertexShader;
    static PixelShader pixelShader;
    static IConstantBuffer constants;
    static bool shadersReady;
    static bool loggedError;
    static string lastError;
    static IRtvTexture persistentA, persistentB;
    static bool persistentValid;
    static DebugBuffer persistentMode;
    static long persistenceTick;
    public static string PersistenceStatus { get; private set; } = "LIVE";

    public static string LastError => lastError;

    [StructLayout(LayoutKind.Sequential, Size = ConstantBufferBytes)]
    struct Constants
    {
        public float Mode;
        public float Scale;
        public float HistoryValid;
        public float HasDepth;
        public float PersistenceDecay;
        public float PersistenceValid;
    }

    public static void Draw(IRtvBindable dest)
    {
        if (!OverlayOn() || dest == null)
        {
            // This patch runs every frame. Keep the default Off path lock-free;
            // there is cleanup work only after persistence allocated textures.
            if (persistentA != null || persistentB != null)
                lock (Gate) ResetPersistence();
            return;
        }

        lock (Gate)
        {
            if (!OverlayOn())
                return;
            try
            {
                DrawUnlocked(dest);
            }
            catch (Exception e)
            {
                Fail("draw: " + e.GetType().Name + ": " + e.Message, e);
            }
        }
    }

    public static void Release()
    {
        lock (Gate)
        {
            ResetPersistence();
            if (constants != null)
            {
                MyManagers.Buffers.Dispose(new[] { constants });
                constants = null;
            }

            vertexShader?.Dispose();
            pixelShader?.Dispose();
            vertexShader = null;
            pixelShader = null;
            shadersReady = false;
            loggedError = false;
            lastError = null;
        }
    }

    static bool OverlayOn()
    {
        var cfg = Config.Current;
        if (cfg == null)
            return false;
        return cfg.DebugBuffer != DebugBuffer.Off;
    }

    static DebugBuffer EffectiveBuffer()
    {
        var cfg = Config.Current;
        if (cfg == null)
            return DebugBuffer.Off;
        return cfg.DebugBuffer;
    }

    static void DrawUnlocked(IRtvBindable dest)
    {
        var mode = EffectiveBuffer();
        if (mode == DebugBuffer.Off || dest == null)
            return;

        ISharedBuffer buf = null;
        ISrvBindable srv = null;
        float shaderMode;
        float historyValid = 1f;
        ISrvBindable depthSrv = null;
        ISrvBindable auditSrv = null;
        ISrvBindable gbuffer0Srv = null;
        switch (mode)
        {
            case DebugBuffer.GBufferVelocityRaw:
                srv = GBufferVelocity.PrepareDebugSource();
                shaderMode = 0f;
                var rawVel = VelocityRegistry.Active;
                historyValid = rawVel != null && rawVel.HistoryValid ? 1f : 0f;
                depthSrv = MyGBuffer.Main?.ResolvedDepthStencil?.SrvDepth;
                break;
            case DebugBuffer.VelocityPipelineAudit:
                srv = GBufferVelocity.PrepareDebugSource();
                auditSrv = GBufferVelocity.PrepareAuditProofSource();
                shaderMode = 4f;
                depthSrv = MyGBuffer.Main?.ResolvedDepthStencil?.SrvDepth;
                gbuffer0Srv = MyGBuffer.Main?.GBuffer0;
                break;
            case DebugBuffer.LinearDepth:
                buf = BufferCatalog.Active(BufferCatalog.LinearDepth);
                shaderMode = 1f;
                break;
            case DebugBuffer.HiZ:
                buf = BufferCatalog.Active(BufferCatalog.HiZ);
                shaderMode = 1f;
                break;
            case DebugBuffer.HistoryColor:
                buf = BufferCatalog.Active(BufferCatalog.HistoryColor);
                shaderMode = 2f;
                break;
            case DebugBuffer.ReactiveMask:
                buf = BufferCatalog.Active(BufferCatalog.ReactiveMask);
                shaderMode = 3f;
                break;
            case DebugBuffer.FullscreenIsolated:
                buf = BufferCatalog.Active(BufferCatalog.FullscreenIsolated);
                shaderMode = 2f;
                break;
            default:
                buf = BufferCatalog.Active(BufferCatalog.Velocity);
                shaderMode = 0f;
                var vel = VelocityRegistry.Active;
                historyValid = vel != null && vel.HistoryValid ? 1f : 0f;
                depthSrv = MyGBuffer.Main?.ResolvedDepthStencil?.SrvDepth;
                break;
        }

        srv ??= buf?.Srv as ISrvBindable;
        var rc = MyRender11.RC;
        if ((buf != null && !buf.IsAvailable) || srv == null ||
            (mode == DebugBuffer.VelocityPipelineAudit && auditSrv == null) ||
            (mode == DebugBuffer.VelocityPipelineAudit && gbuffer0Srv == null) ||
            dest == null || rc == null || !rc.IsInitialized)
            return;

        EnsureShaders();
        EnsureConstants();
        if (!shadersReady || vertexShader == null || pixelShader == null || constants == null)
            return;

        var scalePx = Config.Current.DebugVelocityScale;
        var cb = new Constants
        {
            Mode = shaderMode,
            Scale = 1f / scalePx,
            HistoryValid = historyValid,
            HasDepth = depthSrv != null ? 1f : 0f
        };
        bool persist = Config.Current.DebugMotionPersistence && Config.Current.VelocityProbe == VelocityProbe.Off &&
            Config.Current.Target3Checkpoint == Target3Checkpoint.Live && shaderMode == 0f;
        if (persist)
        {
            var size = srv.Size;
            if (persistentA == null || persistentA.Size != size || persistentMode != mode)
            {
                ResetPersistence();
                persistentA = MyManagers.RwTextures.CreateRtv("Anomaly.DebugMotion.A", size.X, size.Y, SharpDX.DXGI.Format.R16G16B16A16_Float);
                persistentB = MyManagers.RwTextures.CreateRtv("Anomaly.DebugMotion.B", size.X, size.Y, SharpDX.DXGI.Format.R16G16B16A16_Float);
                persistentMode = mode;
            }
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            var elapsed = persistenceTick == 0 ? 0 : (now - persistenceTick) / (double)System.Diagnostics.Stopwatch.Frequency;
            persistenceTick = now;
            cb.Scale *= Config.Current.DebugMotionGain;
            bool paused = Sandbox.MySandboxGame.IsPaused;
            // A real camera cut must not carry old evidence into a new view.
            // Pause can invalidate live history; preserve the debug copy then.
            if (!paused && historyValid < 0.5) persistentValid = false;
            if (!paused || !persistentValid)
            {
                cb.Mode = 5;
                cb.PersistenceDecay = (float)Math.Pow(0.5, elapsed / Config.Current.DebugMotionHalfLife);
                cb.PersistenceValid = persistentValid ? 1 : 0;
                Render(rc, persistentB, cb, srv, depthSrv, null, null, persistentValid ? persistentA : null);
                var swap = persistentA; persistentA = persistentB; persistentB = swap;
                persistentValid = true;
            }
            PersistenceStatus = paused ? "PERSISTENT / PAUSED (retained evidence)" : "PERSISTENT (screen-space trails)";
            cb.Mode = 2.5f;
            Render(rc, dest, cb, persistentA, null, null, null, null);
        }
        else
        {
            ResetPersistence();
            Render(rc, dest, cb, srv, depthSrv, auditSrv, gbuffer0Srv, null);
        }
        lastError = null;
        loggedError = false;
    }

    static void ResetPersistence()
    {
        if (persistentA != null) MyManagers.RwTextures.DisposeTex(ref persistentA);
        if (persistentB != null) MyManagers.RwTextures.DisposeTex(ref persistentB);
        persistentValid = false;
        persistenceTick = 0;
        PersistenceStatus = "LIVE";
    }

    static void Render(MyRenderContext rc, IRtvBindable dest, Constants cb, ISrvBindable srv,
        ISrvBindable depthSrv, ISrvBindable auditSrv, ISrvBindable gbuffer0Srv, ISrvBindable previous)
    {
        var mapping = MyMapping.MapDiscard(rc, constants);
        mapping.WriteAndPosition(ref cb);
        mapping.Unmap();

        SetOverlayViewport(rc, dest);
        rc.SetRasterizerState(MyRasterizerStateManager.NocullRasterizerState);
        rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
        rc.SetBlendState(MyBlendStateManager.BlendReplace);
        rc.SetRtv(dest);
        rc.SetInputLayout(null);
        rc.SetPrimitiveTopology(PrimitiveTopology.TriangleList);
        rc.SetVertexBuffer(0, null);
        rc.GeometryShader.Set(null);
        rc.VertexShader.Set(vertexShader);
        rc.PixelShader.Set(pixelShader);
        rc.PixelShader.SetConstantBuffer(0, constants);
        rc.PixelShader.SetSampler(0, MySamplerStateManager.Point);
        rc.PixelShader.SetSrv(0, srv);
        rc.PixelShader.SetSrv(1, depthSrv);
        rc.PixelShader.SetSrv(2, auditSrv);
        rc.PixelShader.SetSrv(3, gbuffer0Srv);
        rc.PixelShader.SetSrv(4, previous);
        rc.Draw(3, 0);
        rc.ClearState();
        lastError = null;
        loggedError = false;
    }

    /// <summary>
    /// Presented size. After SE-DLSS <c>SetDRS</c>, backbuffer
    /// <see cref="IResource.Size"/> follows internal
    /// <see cref="MyRender11.ResolutionI"/> while the DXGI swapchain and
    /// <see cref="MyRender11.ViewportResolution"/> are output. Using
    /// <c>dest.Size</c> left the overlay as an internal-res rectangle.
    /// </summary>
    static void SetOverlayViewport(MyRenderContext rc, IRtvBindable dest)
    {
        var output = MyRender11.ViewportResolution;
        float w;
        float h;
        if (ReferenceEquals(dest, MyRender11.Backbuffer) && output.X > 0 && output.Y > 0)
        {
            w = output.X;
            h = output.Y;
        }
        else
        {
            var size = dest.Size;
            w = size.X;
            h = size.Y;
        }

        rc.SetViewport(0f, 0f, w, h, 0f, 1f);
    }

    static void EnsureShaders()
    {
        if (shadersReady && vertexShader != null && pixelShader != null)
            return;

        var vsPath = FindHlsl(VsFile);
        var psPath = FindHlsl(PsFile);
        if (vsPath == null || psPath == null)
        {
            Fail("HLSL not found (Fullscreen.hlsl / CatalogDebug.hlsl) under " +
                 (ShaderCompileIntercept.IncludeDirectory ?? "(no include dir)"), null);
            return;
        }

        var vsBc = MyShaderCompiler.Compile(vsPath, Array.Empty<ShaderMacro>(), MyShaderProfile.vs_5_0,
            "Anomaly.Fullscreen", invalidateCache: false);
        var psBc = MyShaderCompiler.Compile(psPath, Array.Empty<ShaderMacro>(), MyShaderProfile.ps_5_0,
            "Anomaly.CatalogDebug", invalidateCache: true);
        if (vsBc == null || vsBc.Length == 0 || psBc == null || psBc.Length == 0)
        {
            Fail("shader compile returned empty bytecode", null);
            return;
        }

        var device = MyRender11.DeviceInstance;
        vertexShader?.Dispose();
        pixelShader?.Dispose();
        vertexShader = new VertexShader(device, vsBc) { DebugName = "Anomaly.CatalogDebug.VS" };
        pixelShader = new PixelShader(device, psBc) { DebugName = "Anomaly.CatalogDebug.PS" };
        shadersReady = true;
        MyLog.Default.WriteLine("Anomaly catalog debug shaders compiled.");
        DebugLog.Write("VelocityDebugPass shaders ok vs=" + vsPath + " ps=" + psPath);
    }

    static void EnsureConstants()
    {
        if (constants != null)
            return;
        constants = MyManagers.Buffers.CreateConstantBuffer("Anomaly.CatalogDebugCB", ConstantBufferBytes,
            usage: ResourceUsage.Dynamic);
    }

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

    static void Fail(string message, Exception e)
    {
        lastError = message;
        if (loggedError)
            return;
        loggedError = true;
        MyLog.Default.WriteLine("Anomaly catalog debug: " + message);
        DebugLog.Write("VelocityDebugPass " + message + (e != null ? "\n" + e : ""));
    }
}
