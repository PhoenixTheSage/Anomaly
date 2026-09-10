using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ClientPlugin.Shaders;
using ClientPlugin.Velocity;
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
/// Fullscreen camera-from-depth into an <c>RG16F</c> RT at <see cref="MyRender11.ResolutionI"/>.
/// Runs after <c>MyRenderScheduler.Done</c> (GBuffer + resolve finished, before post).
/// When GBuffer velocity is live, a second PS (<c>ANOMALY_COMPOSITE</c>) keeps
/// GBuffer MVs that were actually written and camera-fills clear-zero pixels
/// (sky / particles / foliage) from depth. Writes alternate ping-pong RTs so
/// DLSS / catalog can keep sampling last frame while this frame draws.
/// </summary>
public static class CameraVelocityPass
{
    public const float CameraCutMeters = 80f;

    static readonly object Gate = new();
    static readonly float CutDistanceSq = CameraCutMeters * CameraCutMeters;
    const int ConstantBufferBytes = 256;
    const string VsFile = "Fullscreen.hlsl";
    const string PsFile = "CameraVelocity.hlsl";

    public static bool Enabled { get; set; } = true;
    public static string LastError { get; private set; }
    public static bool ShadersReady { get; private set; }

    internal static bool TryGetMotionFrame(out Matrix unjittered, out Matrix prev, out Vector3D prevCam,
        out bool historyValid, out Vector2I size)
    {
        unjittered = default;
        prev = default;
        prevCam = prevCameraPos;
        historyValid = false;
        size = MyRender11.ResolutionI;
        var env = MyRender11.Environment?.Matrices;
        if (env == null)
            return false;

        unjittered = UnjitteredViewProjection(env);
        var cut = hasPrev && Vector3D.DistanceSquared(env.CameraPosition, prevCameraPos) > CutDistanceSq;
        historyValid = hasPrev && !cut && !justResized;
        prev = historyValid ? prevViewProj : unjittered;
        return true;
    }

    internal static bool TryGetPrevCamera(out Vector3D prevCam)
    {
        prevCam = prevCameraPos;
        return hasPrev;
    }

    internal static void AdvanceHistory()
    {
        var env = MyRender11.Environment?.Matrices;
        if (env == null)
            return;
        prevViewProj = UnjitteredViewProjection(env);
        prevCameraPos = env.CameraPosition;
        hasPrev = true;
        justResized = false;
    }

    static VertexShader vertexShader;
    static PixelShader pixelShader;
    static PixelShader compositeShader;
    static IConstantBuffer constants;
    static readonly IRtvTexture[] targets = new IRtvTexture[2];
    static int writeIndex;
    static int targetWidth;
    static int targetHeight;
    static bool hasPrev;
    static bool justResized;
    static Matrix prevViewProj;
    static Vector3D prevCameraPos;
    static bool loggedError;

    [StructLayout(LayoutKind.Sequential, Size = ConstantBufferBytes)]
    struct Constants
    {
        public Matrix InvViewProj;
        public Matrix UnjitteredViewProj;
        public Matrix PrevViewProj;
        public Vector2 RenderSize;
        public Vector2 InvRenderSize;
    }

    public static void Execute()
    {
        if (!Enabled)
            return;

        lock (Gate)
        {
            if (!Enabled)
                return;
            try
            {
                RenderTrace.Begin("CameraVelocity");
                ExecuteCore();
            }
            catch (Exception e)
            {
                Fail("execute: " + e.GetType().Name + ": " + e.Message, e);
                VelocityRegistry.SetActive(UnavailableVelocityBuffer.Instance);
            }
            finally
            {
                RenderTrace.End("CameraVelocity");
            }
        }
    }

    public static void OnResolutionChanged()
    {
        if (!Enabled)
            return;
        GBufferVelocity.OnResolutionChanged();
        lock (Gate)
        {
            // Keen recreates screen resources at the same size (DLSS SetDRS
            // / CreateScreenResources). Wiping history here painted the
            // overlay magenta. EnsureTarget invalidates only on a real resize.
            try
            {
                EnsureTarget();
            }
            catch (Exception e)
            {
                Fail("resize: " + e.Message, e);
            }
        }
    }

    public static void Release()
    {
        GBufferVelocity.Release();
        lock (Gate)
        {
            CameraVelocityBuffer.Instance.Clear();
            VelocityRegistry.SetActive(UnavailableVelocityBuffer.Instance);
            DisposeTarget();
            DisposeShadersAndCb();
            hasPrev = false;
            justResized = true;
            ShadersReady = false;
        }
    }

    static void ExecuteCore()
    {
        if (GBufferVelocity.ShouldPublish)
        {
            var src = GBufferVelocity.PrepareCompositeSource();
            if (src != null && ExecuteDraw(composite: true, src))
                return;
        }

        ExecuteDraw(composite: false, null);
    }

    static bool ExecuteDraw(bool composite, ISrvBindable gbufferVelocity)
    {
        var gbuffer = MyGBuffer.Main;
        var depth = gbuffer?.ResolvedDepthStencil?.SrvDepth;
        var rc = MyRender11.RC;
        if (gbuffer == null || depth == null || rc == null || !rc.IsInitialized)
            return false;

        EnsureShaders();
        EnsureTarget();
        EnsureConstants();
        if (!ShadersReady || targets[0] == null || targets[1] == null || constants == null)
            return false;
        if (composite && (compositeShader == null || gbufferVelocity == null))
            return false;

        var env = MyRender11.Environment?.Matrices;
        if (env == null)
            return false;

        var unjittered = UnjitteredViewProjection(env);
        var cut = hasPrev && Vector3D.DistanceSquared(env.CameraPosition, prevCameraPos) > CutDistanceSq;
        var historyValid = hasPrev && !cut && !justResized;
        var prev = historyValid ? prevViewProj : unjittered;

        var size = MyRender11.ResolutionI;
        var cb = new Constants
        {
            InvViewProj = env.InvViewProjectionAt0,
            UnjitteredViewProj = unjittered,
            // Reconstructed depth positions are relative to the current camera.
            PrevViewProj = historyValid
                ? Matrix.CreateTranslation((Vector3)(env.CameraPosition - prevCameraPos)) * prev
                : prev,
            RenderSize = new Vector2(size.X, size.Y),
            InvRenderSize = new Vector2(1f / size.X, 1f / size.Y)
        };

        var mapping = MyMapping.MapDiscard(rc, constants);
        mapping.WriteAndPosition(ref cb);
        mapping.Unmap();

        var dest = NextWriteTarget();
        if (dest == null)
            return false;

        rc.SetScreenViewport();
        rc.SetRasterizerState(MyRasterizerStateManager.NocullRasterizerState);
        rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
        rc.SetBlendState(MyBlendStateManager.BlendReplace);
        // Lighting/HBAO bind catalog velocity as t5 and may leave leftover OM
        // RTVs. Unbind before this draw so dest is never SRV+RTV, and so DLSS
        // can keep reading last frame's ping while we write the other.
        rc.SetRtvNull();
        rc.PixelShader.SetSrv(0, null);
        rc.PixelShader.SetSrv(1, null);
        rc.AllShaderStages.SetSrv(ShaderBindRegistry.LightingVelocitySlot, null);
        rc.SetRtv(dest);
        rc.SetInputLayout(null);
        rc.SetPrimitiveTopology(PrimitiveTopology.TriangleList);
        rc.SetVertexBuffer(0, null);
        rc.GeometryShader.Set(null);
        rc.VertexShader.Set(vertexShader);
        rc.PixelShader.Set(composite ? compositeShader : pixelShader);
        rc.PixelShader.SetConstantBuffer(0, constants);
        rc.PixelShader.SetSampler(0, MySamplerStateManager.Point);
        rc.PixelShader.SetSrv(0, depth);
        rc.PixelShader.SetSrv(1, composite ? gbufferVelocity : null);
        rc.Draw(3, 0);
        rc.ClearState();

        var native = dest.Resource != null ? dest.Resource.NativePointer : IntPtr.Zero;
        CameraVelocityBuffer.Instance.Publish(dest, native, size.X, size.Y, historyValid);
        VelocityRegistry.SetActive(CameraVelocityBuffer.Instance);

        prevViewProj = unjittered;
        prevCameraPos = env.CameraPosition;
        hasPrev = true;
        justResized = false;
        LastError = null;
        loggedError = false;
        return true;
    }

    static Matrix UnjitteredViewProjection(MyEnvironmentMatrices env)
    {
        var proj = env.Projection;
        proj.M31 = 0f;
        proj.M32 = 0f;
        return env.ViewAt0 * proj;
    }

    internal static void CollectWarmupJobs(List<ShaderWarmup.Job> jobs)
    {
        if (vertexShader != null && pixelShader != null && compositeShader != null)
            return;
        var psPath = FindHlsl(PsFile);
        ShaderWarmup.Add(jobs, FindHlsl(VsFile), MyShaderProfile.vs_5_0, "Anomaly.Fullscreen");
        ShaderWarmup.Add(jobs, psPath, MyShaderProfile.ps_5_0, "Anomaly.CameraVelocity");
        ShaderWarmup.Add(jobs, psPath, MyShaderProfile.ps_5_0, "Anomaly.CameraVelocity.Composite",
            new ShaderMacro("ANOMALY_COMPOSITE", "1"));
    }

    internal static void Prewarm()
    {
        EnsureShaders();
    }

    static void EnsureShaders()
    {
        if (vertexShader != null && pixelShader != null && compositeShader != null)
        {
            ShadersReady = true;
            return;
        }

        var vsPath = FindHlsl(VsFile);
        var psPath = FindHlsl(PsFile);
        if (vsPath == null || psPath == null)
        {
            Fail("HLSL not found (Fullscreen.hlsl / CameraVelocity.hlsl) under " +
                 (ShaderCompileIntercept.IncludeDirectory ?? "(no include dir)"), null);
            return;
        }

        var vsBc = MyShaderCompiler.Compile(vsPath, Array.Empty<ShaderMacro>(), MyShaderProfile.vs_5_0,
            "Anomaly.Fullscreen", invalidateCache: false);
        var psBc = MyShaderCompiler.Compile(psPath, Array.Empty<ShaderMacro>(), MyShaderProfile.ps_5_0,
            "Anomaly.CameraVelocity", invalidateCache: false);
        var compositeBc = MyShaderCompiler.Compile(psPath,
            new[] { new ShaderMacro("ANOMALY_COMPOSITE", "1") },
            MyShaderProfile.ps_5_0, "Anomaly.CameraVelocity.Composite", invalidateCache: false);
        if (vsBc == null || vsBc.Length == 0 || psBc == null || psBc.Length == 0 ||
            compositeBc == null || compositeBc.Length == 0)
        {
            Fail("shader compile returned empty bytecode", null);
            return;
        }

        var device = MyRender11.DeviceInstance;
        vertexShader = new VertexShader(device, vsBc) { DebugName = "Anomaly.Fullscreen" };
        pixelShader = new PixelShader(device, psBc) { DebugName = "Anomaly.CameraVelocity" };
        compositeShader = new PixelShader(device, compositeBc) { DebugName = "Anomaly.CameraVelocity.Composite" };
        ShadersReady = true;
        MyLog.Default.WriteLine("Anomaly camera velocity shaders compiled.");
        DebugLog.Write("CameraVelocityPass shaders ok vs=" + vsPath + " ps=" + psPath);
    }

    static IRtvTexture NextWriteTarget()
    {
        writeIndex ^= 1;
        return targets[writeIndex];
    }

    static void EnsureTarget()
    {
        var size = MyRender11.ResolutionI;
        if (size.X <= 0 || size.Y <= 0)
            return;
        if (targets[0] != null && targets[1] != null && targetWidth == size.X && targetHeight == size.Y)
            return;

        DisposeTarget();
        targets[0] = MyManagers.RwTextures.CreateRtv("Anomaly.CameraVelocity.A", size.X, size.Y, Format.R16G16_Float);
        targets[1] = MyManagers.RwTextures.CreateRtv("Anomaly.CameraVelocity.B", size.X, size.Y, Format.R16G16_Float);
        writeIndex = 1;
        targetWidth = size.X;
        targetHeight = size.Y;
        hasPrev = false;
        justResized = true;
        CameraVelocityBuffer.Instance.Clear();
        DebugLog.Write("CameraVelocityPass RT " + size.X + "x" + size.Y + " ping-pong");
    }

    static void EnsureConstants()
    {
        if (constants != null)
            return;
        constants = MyManagers.Buffers.CreateConstantBuffer("Anomaly.CameraVelocityCB", ConstantBufferBytes,
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

    static void DisposeTarget()
    {
        for (var i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null)
                continue;
            MyManagers.RwTextures.DisposeTex(ref targets[i]);
            targets[i] = null;
        }

        writeIndex = 1;
        targetWidth = 0;
        targetHeight = 0;
        CameraVelocityBuffer.Instance.Clear();
    }

    static void DisposeShadersAndCb()
    {
        if (constants != null)
        {
            MyManagers.Buffers.Dispose(new[] { constants });
            constants = null;
        }

        vertexShader?.Dispose();
        pixelShader?.Dispose();
        compositeShader?.Dispose();
        vertexShader = null;
        pixelShader = null;
        compositeShader = null;
        ShadersReady = false;
    }

    static void Fail(string message, Exception e)
    {
        LastError = message;
        RenderTrace.DumpIfLost("CameraVelocity", e);
        if (loggedError)
            return;
        loggedError = true;
        MyLog.Default.WriteLine("Anomaly camera velocity: " + message);
        DebugLog.Write("CameraVelocityPass " + message + (e != null ? "\n" + e : ""));
    }
}
