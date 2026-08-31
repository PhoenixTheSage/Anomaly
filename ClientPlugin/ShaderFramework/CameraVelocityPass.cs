using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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
/// Slice B: fullscreen camera-from-depth into an <c>RG16F</c> RT at <see cref="MyRender11.ResolutionI"/>.
/// Runs after <c>MyRenderScheduler.Done</c> (GBuffer + resolve finished, before post).
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
    static IConstantBuffer constants;
    static IRtvTexture target;
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
                ExecuteCore();
            }
            catch (Exception e)
            {
                Fail("execute: " + e.GetType().Name + ": " + e.Message, e);
                VelocityRegistry.SetActive(UnavailableVelocityBuffer.Instance);
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
            justResized = true;
            hasPrev = false;
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
            GBufferVelocity.PublishAndAdvanceHistory();
            return;
        }

        var gbuffer = MyGBuffer.Main;
        var depth = gbuffer?.ResolvedDepthStencil?.SrvDepth;
        var rc = MyRender11.RC;
        if (gbuffer == null || depth == null || rc == null || !rc.IsInitialized)
            return;

        EnsureShaders();
        EnsureTarget();
        EnsureConstants();
        if (!ShadersReady || target == null || constants == null)
            return;

        var env = MyRender11.Environment?.Matrices;
        if (env == null)
            return;

        var unjittered = UnjitteredViewProjection(env);
        var cut = hasPrev && Vector3D.DistanceSquared(env.CameraPosition, prevCameraPos) > CutDistanceSq;
        var historyValid = hasPrev && !cut && !justResized;
        var prev = historyValid ? prevViewProj : unjittered;

        var size = MyRender11.ResolutionI;
        var cb = new Constants
        {
            InvViewProj = env.InvViewProjectionAt0,
            UnjitteredViewProj = unjittered,
            PrevViewProj = prev,
            RenderSize = new Vector2(size.X, size.Y),
            InvRenderSize = new Vector2(1f / size.X, 1f / size.Y)
        };

        var mapping = MyMapping.MapDiscard(rc, constants);
        mapping.WriteAndPosition(ref cb);
        mapping.Unmap();

        rc.SetScreenViewport();
        rc.SetRasterizerState(MyRasterizerStateManager.NocullRasterizerState);
        rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
        rc.SetBlendState(MyBlendStateManager.BlendReplace);
        rc.SetRtv(target);
        rc.SetInputLayout(null);
        rc.SetPrimitiveTopology(PrimitiveTopology.TriangleList);
        rc.SetVertexBuffer(0, null);
        rc.GeometryShader.Set(null);
        rc.VertexShader.Set(vertexShader);
        rc.PixelShader.Set(pixelShader);
        rc.PixelShader.SetConstantBuffer(0, constants);
        rc.PixelShader.SetSampler(0, MySamplerStateManager.Point);
        rc.PixelShader.SetSrv(0, depth);
        rc.Draw(3, 0);
        rc.ClearState();

        var native = target.Resource != null ? target.Resource.NativePointer : IntPtr.Zero;
        CameraVelocityBuffer.Instance.Publish(target, native, size.X, size.Y, historyValid);
        VelocityRegistry.SetActive(CameraVelocityBuffer.Instance);

        prevViewProj = unjittered;
        prevCameraPos = env.CameraPosition;
        hasPrev = true;
        justResized = false;
        LastError = null;
        loggedError = false;
    }

    static Matrix UnjitteredViewProjection(MyEnvironmentMatrices env)
    {
        var proj = env.Projection;
        proj.M31 = 0f;
        proj.M32 = 0f;
        return env.ViewAt0 * proj;
    }

    static void EnsureShaders()
    {
        if (vertexShader != null && pixelShader != null)
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
        if (vsBc == null || vsBc.Length == 0 || psBc == null || psBc.Length == 0)
        {
            Fail("shader compile returned empty bytecode", null);
            return;
        }

        var device = MyRender11.DeviceInstance;
        vertexShader = new VertexShader(device, vsBc) { DebugName = "Anomaly.Fullscreen" };
        pixelShader = new PixelShader(device, psBc) { DebugName = "Anomaly.CameraVelocity" };
        ShadersReady = true;
        MyLog.Default.WriteLine("Anomaly camera velocity shaders compiled.");
        DebugLog.Write("CameraVelocityPass shaders ok vs=" + vsPath + " ps=" + psPath);
    }

    static void EnsureTarget()
    {
        var size = MyRender11.ResolutionI;
        if (size.X <= 0 || size.Y <= 0)
            return;
        if (target != null && targetWidth == size.X && targetHeight == size.Y)
            return;

        DisposeTarget();
        target = MyManagers.RwTextures.CreateRtv("Anomaly.CameraVelocity", size.X, size.Y, Format.R16G16_Float);
        targetWidth = size.X;
        targetHeight = size.Y;
        hasPrev = false;
        justResized = true;
        CameraVelocityBuffer.Instance.Clear();
        DebugLog.Write("CameraVelocityPass RT " + size.X + "x" + size.Y);
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
        if (target == null)
            return;
        MyManagers.RwTextures.DisposeTex(ref target);
        target = null;
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
        vertexShader = null;
        pixelShader = null;
        ShadersReady = false;
    }

    static void Fail(string message, Exception e)
    {
        LastError = message;
        if (loggedError)
            return;
        loggedError = true;
        MyLog.Default.WriteLine("Anomaly camera velocity: " + message);
        DebugLog.Write("CameraVelocityPass " + message + (e != null ? "\n" + e : ""));
    }
}
