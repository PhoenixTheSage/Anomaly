using System;
using VRage.Render11.Common;
using VRage.Render11.Resources;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ClientPlugin.Shaders;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using VRage.Render11.RenderContext;

using VRageMath;
using VRageRender;

namespace ClientPlugin.ShaderFramework;

// The depth binding transpilers call this explicitly, so JIT inlining of
// MyPixelStage.Set cannot bypass the isolated-pass clip contract.
internal static class FarSunClip
{
    sealed class Variant { internal Task<byte[]> Code; internal PixelShader Shader; internal string Error; }
    static readonly Dictionary<PixelShader, Variant> Variants = new();
    static IConstantBuffer constants;
    static PixelShader opaque;
    static Task<byte[]> opaqueCode;
    static int active, bindings, missing;
    internal static bool HooksReady;
    internal static bool Complete => Volatile.Read(ref bindings) > 0 && Volatile.Read(ref missing) == 0 && HooksReady;
    internal static string Status { get; private set; } = "Clip variants pending";
    [StructLayout(LayoutKind.Sequential)] struct ClipConstants { public Matrix Inverse; public Vector4 Parameters; }

    internal static bool Prepare(MatrixD local)
    {
        if (!HooksReady) { Status="Depth binding hooks unavailable"; return false; }
        if (string.IsNullOrEmpty(ShaderCompileIntercept.IncludeDirectory)) { Status="Shader include directory unavailable"; return false; }
        foreach(var key in Variants.Keys.Where(shader=>shader.IsDisposed).ToArray())
        { var stale=Variants[key]; stale.Shader?.Dispose(); Observe(stale.Code); Variants.Remove(key); }
        opaqueCode ??= Task.Run(() => RayScene.CompileShader(Path.Combine(ShaderCompileIntercept.IncludeDirectory, "FarSunDepth.hlsl"), (int)MyShaderProfile.ps_5_0));
        // Snapshot registry under the same lock as Keen's shader publication.
        lock (MyPixelShaders.m_keyToId)
        {
            foreach (var id in MyPixelShaders.m_keyToId.Values)
            {
                var item = MyPixelShaders.m_shaders.Data[id.Index];
                if (item.Shader == null || Variants.ContainsKey(item.Shader)) continue;
                var info = MyShaders.GetCompilationInfo(item.InfoId);
                if (!ShaderCompileIntercept.IsDepthPermutation(info.Macros)) continue;
                Queue(item.Shader, Path.Combine(MyShaderCompiler.ShadersPath, info.File.ToString()), info.Macros);
            }
        }
        MyMaterialShadersBundleId[] legacy;
        lock (MyMaterialShaders.m_hashIndex) legacy = MyMaterialShaders.m_hashIndex.Values.ToArray();
        foreach (var id in legacy)
        {
            var bundle = MyMaterialShaders.Bundles[id.Index];
            // Do not wait for a legacy material compilation on the render thread.
            if (bundle == null || !Monitor.TryEnter(bundle)) continue;
            try
            {
                var info = MyMaterialShaders.BundleInfo.Data[id.Index];
                if (bundle.PS == null || info.Pass != MyMaterialShaders.DEPTH_PASS_ID || Variants.ContainsKey(bundle.PS)) continue;
                var macros = new List<ShaderMacro> { MyMaterialShaders.GetRenderingPassMacro(info.Pass.String) };
                MyMaterialShaders.AddMaterialShaderFlagMacrosTo(macros, info.Flags, info.TextureTypes);
                macros.AddRange(info.Layout.Info.Macros);
                Queue(bundle.PS, Path.Combine(MyShaderCompiler.ShadersPath, MyMaterialShaders.MaterialsFolder, info.Material.String, "Pixel.hlsl"), macros.ToArray());
            }
            finally { Monitor.Exit(bundle); }
        }
        foreach (var variant in Variants.Values)
        {
            if (variant.Shader != null || variant.Error != null || !variant.Code.IsCompleted) continue;
            try { variant.Shader = CreateVerified(variant.Code.GetAwaiter().GetResult()); }
            catch (Exception e) { variant.Error = e.Message; Status = "Depth clip unavailable: " + e.Message; }
        }
        if (opaque == null && opaqueCode.IsCompleted)
        {
            try { opaque = CreateVerified(opaqueCode.GetAwaiter().GetResult()); }
            catch (Exception e) { Status = "Opaque clip unavailable: " + e.Message; return false; }
        }
        if (opaque == null) { Status="Depth clip compilation pending"; return false; }
        constants ??= MyManagers.Buffers.CreateConstantBuffer("Anomaly.FarSun.Clip", 80, usage: ResourceUsage.Dynamic);
        var data = new ClipConstants { Inverse = (Matrix)MatrixD.Invert(local), Parameters = new Vector4(2048, 256, 0, 0) };
        var mapping = MyMapping.MapDiscard(MyRender11.RC, constants); mapping.WriteAndPosition(ref data); mapping.Unmap();
        bindings = missing = 0;
        return true;
    }
    static readonly SemaphoreSlim CompilerSlots = new(2);
    static void Queue(PixelShader original, string path, ShaderMacro[] nativeMacros)
    {
        var macros = (nativeMacros ?? Array.Empty<ShaderMacro>()).Concat(new[] { new ShaderMacro("ANOMALY_FAR_SUN", "1") }).ToArray();
        Variants.Add(original, new Variant { Code = Task.Run(async () => {
            await CompilerSlots.WaitAsync().ConfigureAwait(false);
            try {
                var code = MyShaderCompiler.Compile(path, macros, MyShaderProfile.ps_5_0, "Anomaly.FarSun", true, false,
                    out _, out string log, out _, false, false);
                if (code == null || code.Length == 0) throw new InvalidOperationException(log);
                return code;
            } finally { CompilerSlots.Release(); }
        }) });
    }
    static PixelShader CreateVerified(byte[] code)
    {
        using var reflection = new SharpDX.D3DCompiler.ShaderReflection(code);
        if (reflection.GetConstantBuffer("AnomalyFarSunClip").Description.Size != 80 ||
            reflection.GetResourceBindingDescription("AnomalyFarSunClip").BindPoint != 13)
            throw new InvalidOperationException("Far-sun b13/80-byte contract mismatch");
        return new PixelShader(MyRender11.DeviceInstance, code) { DebugName = "Anomaly.FarSun.ClippedDepth" };
    }
    internal static void Begin() => Volatile.Write(ref active, 1);
    internal static void End() => Volatile.Write(ref active, 0);
    public static void Set(MyPixelStage stage, PixelShader shader)
    {
        if (Volatile.Read(ref active) != 0)
        {
            Interlocked.Increment(ref bindings);
            if (shader == null) shader = opaque;
            else if (Variants.TryGetValue(shader, out var variant) && variant.Shader != null) shader = variant.Shader;
            else Interlocked.Increment(ref missing);
            stage.SetConstantBuffer(13, constants);
        }
        stage.Set(shader);
    }
    public static void Bind(VRage.Render11.RenderContext.MyRenderContext context, MyMaterialShadersBundleId id)
    {
        context.SetInputLayout(id.IL); context.VertexShader.Set(id.VS); Set(context.PixelShader,id.PS);
    }
    internal static void Release()
    {
        End(); opaque?.Dispose(); opaque = null;
        // Observe faults without waiting for compiler jobs on device teardown.
        Observe(opaqueCode); opaqueCode = null;
        foreach (var v in Variants.Values) { v.Shader?.Dispose(); Observe(v.Code); }
        Variants.Clear();
        if (constants != null) { MyManagers.Buffers.Dispose(new[] { constants }); constants = null; }
        Status = "Clip resources released";
    }
    static void Observe(Task<byte[]> task) { if (task != null) _ = task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); }
}
