using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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
/// Fullscreen false-color of <see cref="IVelocityBuffer"/> onto the backbuffer
/// after <c>DrawGameScene</c>. Off by default. Unbinds RT/SRV before return.
/// </summary>
public static class VelocityDebugPass
{
    const int ConstantBufferBytes = 256;
    const string VsFile = "Fullscreen.hlsl";
    const string PsFile = "VelocityDebug.hlsl";

    static readonly object Gate = new();

    static VertexShader vertexShader;
    static PixelShader pixelShader;
    static IConstantBuffer constants;
    static bool shadersReady;
    static bool loggedError;
    static string lastError;

    public static string LastError => lastError;

    [StructLayout(LayoutKind.Sequential, Size = ConstantBufferBytes)]
    struct Constants
    {
        public float Scale;
        public float HistoryValid;
        public float Pad0;
        public float Pad1;
    }

    public static void Draw(IRtvBindable dest)
    {
        if (Config.Current == null || !Config.Current.DebugVelocity || dest == null)
            return;

        lock (Gate)
        {
            if (Config.Current == null || !Config.Current.DebugVelocity)
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

    static void DrawUnlocked(IRtvBindable dest)
    {
        var buf = VelocityRegistry.Active;
        var srv = buf?.Srv as ISrvBindable;
        var rc = MyRender11.RC;
        if (buf == null || !buf.IsAvailable || srv == null || dest == null || rc == null || !rc.IsInitialized)
            return;

        EnsureShaders();
        EnsureConstants();
        if (!shadersReady || vertexShader == null || pixelShader == null || constants == null)
            return;

        var scalePx = Config.Current.DebugVelocityScale;
        var cb = new Constants
        {
            Scale = 1f / scalePx,
            HistoryValid = buf.HistoryValid ? 1f : 0f
        };
        var mapping = MyMapping.MapDiscard(rc, constants);
        mapping.WriteAndPosition(ref cb);
        mapping.Unmap();

        rc.SetViewport(0f, 0f, dest.Size.X, dest.Size.Y, 0f, 1f);
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
        rc.Draw(3, 0);
        rc.ClearState();
        lastError = null;
        loggedError = false;
    }

    static void EnsureShaders()
    {
        if (shadersReady && vertexShader != null && pixelShader != null)
            return;

        var vsPath = FindHlsl(VsFile);
        var psPath = FindHlsl(PsFile);
        if (vsPath == null || psPath == null)
        {
            Fail("HLSL not found (Fullscreen.hlsl / VelocityDebug.hlsl) under " +
                 (ShaderCompileIntercept.IncludeDirectory ?? "(no include dir)"), null);
            return;
        }

        var vsBc = MyShaderCompiler.Compile(vsPath, Array.Empty<ShaderMacro>(), MyShaderProfile.vs_5_0,
            "Anomaly.Fullscreen", invalidateCache: false);
        var psBc = MyShaderCompiler.Compile(psPath, Array.Empty<ShaderMacro>(), MyShaderProfile.ps_5_0,
            "Anomaly.VelocityDebug", invalidateCache: false);
        if (vsBc == null || vsBc.Length == 0 || psBc == null || psBc.Length == 0)
        {
            Fail("shader compile returned empty bytecode", null);
            return;
        }

        var device = MyRender11.DeviceInstance;
        vertexShader?.Dispose();
        pixelShader?.Dispose();
        vertexShader = new VertexShader(device, vsBc) { DebugName = "Anomaly.VelocityDebug.VS" };
        pixelShader = new PixelShader(device, psBc) { DebugName = "Anomaly.VelocityDebug.PS" };
        shadersReady = true;
        MyLog.Default.WriteLine("Anomaly velocity debug shaders compiled.");
        DebugLog.Write("VelocityDebugPass shaders ok vs=" + vsPath + " ps=" + psPath);
    }

    static void EnsureConstants()
    {
        if (constants != null)
            return;
        constants = MyManagers.Buffers.CreateConstantBuffer("Anomaly.VelocityDebugCB", ConstantBufferBytes,
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
        MyLog.Default.WriteLine("Anomaly velocity debug: " + message);
        DebugLog.Write("VelocityDebugPass " + message + (e != null ? "\n" + e : ""));
    }
}
