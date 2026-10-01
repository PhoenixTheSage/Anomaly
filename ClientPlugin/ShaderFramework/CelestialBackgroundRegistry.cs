using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ClientPlugin.ShaderFramework;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using VRage.Render11.Common;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Shaders;

/// <summary>
/// Exclusive, depth-masked celestial background before atmosphere, and the
/// matching probe background. Resolve by name; packs never own draw calls.
/// The provider file implements AnomalyEvaluateCelestial and includes
/// AnomalyCelestial.hlsli / AnomalyCelestialEntry.hlsli. Both views must compile.
/// Public setters copy CPU data; GPU work happens only on a supplied render context.
/// </summary>
public static class CelestialBackgroundRegistry
{
    const int MaxDataFloats = 4 * 1048576;
    static readonly object Gate = new();
    static readonly Dictionary<string, Provider> Providers = new(StringComparer.OrdinalIgnoreCase);
    static readonly Queue<ArtBindable> RetiredArt = new();
    static Provider compiled;
    static PixelShader mainShader, probeShader;
    static VertexShader vertexShader;
    static IConstantBuffer viewCb, uniformCb;
    static ISrvBuffer dataBuffer;
    static float[] uploadedData;
    internal static bool ProbeHookAvailable;
    static Provider mainDrawnProvider;
    static uint mainDrawnFrame;

    /// <summary>Suppress only the game's solar flare after successful main-view replacement.</summary>
    public static void SetNativeSunGlare(string id, bool enabled)
    { lock (Gate) if (Providers.TryGetValue(id, out var p)) p.NativeSunGlare = enabled; }

    internal static bool SuppressNativeSunGlare
    {
        get
        {
            lock (Gate)
                return mainDrawnProvider != null && mainDrawnFrame == FrameTemporal.FrameIndex &&
                    Providers.Count == 1 && Providers.TryGetValue(mainDrawnProvider.Id, out var p) &&
                    ReferenceEquals(p, mainDrawnProvider) && p.Enabled && !p.Failed && !p.NativeSunGlare;
        }
    }
    static string status = "no provider";

    public static string StatusLine { get { lock (Gate) return status; } }

    public static bool Register(string id, string shaderFile)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(shaderFile)) return false;
        try
        {
            var file = Path.GetFullPath(shaderFile);
            if (!File.Exists(file) || !file.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase)) return false;
            lock (Gate)
            {
                if (Providers.TryGetValue(id, out var old))
                {
                    if (string.Equals(old.File, file, StringComparison.OrdinalIgnoreCase)) return true;
                    old.File = file;
                    old.Failed = false;
                    old.Revision++;
                }
                else Providers.Add(id, new Provider { Id = id, File = file });
                status = Providers.Count == 1 ? "pending compile: " + id : "conflict: multiple celestial providers";
            }
            return true;
        }
        catch (Exception e) { MyLog.Default.WriteLine("Anomaly celestial registration: " + e.Message); return false; }
    }

    public static void Unregister(string id)
    {
        if (id == null) return;
        lock (Gate)
        {
            if (!Providers.TryGetValue(id, out var p)) return;
            p.ArtState.Clear();
            Providers.Remove(id);
            status = "provider removed";
        }
    }
    public static void SetEnabled(string id, bool enabled) { lock (Gate) if (Providers.TryGetValue(id, out var p)) p.Enabled = enabled; }
    public static void Retry(string id) { lock (Gate) if (Providers.TryGetValue(id, out var p)) { p.Failed = false; p.Revision++; } }

    public static void SetUniforms(string id, float[] values)
    {
        Validate(values, 64, false);
        var copy = new float[64];
        Array.Copy(values, copy, values.Length);
        lock (Gate) if (Providers.TryGetValue(id, out var p)) p.Uniforms = copy;
    }

    /// <summary>Optional float4 structured records at t1. Maximum 16 MiB. Empty is valid.</summary>
    public static void SetData(string id, float[] values)
    {
        Validate(values, MaxDataFloats, true);
        var copy = (float[])values.Clone();
        lock (Gate) if (Providers.TryGetValue(id, out var p)) p.Data = copy;
    }

    /// <summary>
    /// Optional art Texture2DArray at t2 with Linear s0. Pass null rgba to clear.
    /// Anomaly owns the GPU texture; packs supply tightly packed RGBA8 slices
    /// (width*height*4*slices bytes). Fail closed when unbound.
    /// </summary>
    public static void SetArt(string id, int width, int height, int slices, byte[] rgba)
    {
        lock (Gate)
        {
            if (!Providers.TryGetValue(id, out var p)) return;
            p.ArtState.Set(width, height, slices, rgba);
        }
    }

    /// <summary>Clear optional art. Equivalent to SetArt(id, 0, 0, 0, null).</summary>
    public static void SetArt(string id, byte[] rgba)
    {
        if (rgba != null) throw new ArgumentException("Use SetArt(id, width, height, slices, rgba) to upload");
        SetArt(id, 0, 0, 0, null);
    }

    static void Validate(float[] values, int max, bool records)
    {
        if (values == null || values.Length > max || (records && values.Length % 4 != 0))
            throw new ArgumentException("Celestial data size is invalid.", nameof(values));
        foreach (var value in values)
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("Celestial values must be finite.", nameof(values));
    }

    internal static void Install() => OwnedPassRegistry.Register("Anomaly.Celestial", OwnedPassSlot.AfterLighting,
        int.MinValue, TemporalPolicy.InColor, ctx => DrawMain(ctx.Rc), OwnedPassPhase.BeforeFullscreen);

    static void DrawMain(MyRenderContext rc)
    {
        lock (Gate) { DrainRetiredArt(); mainDrawnProvider = null; }
        var gbuffer = MyGBuffer.Main;
        if (gbuffer?.ResolvedDepthStencil?.SrvDepth == null) return;
        Draw(rc, gbuffer.LBuffer, gbuffer.ResolvedDepthStencil.SrvDepth, false, Matrix.Identity, Matrix.Identity);
    }

    internal static void DrawProbe(MyRenderContext rc, IRtvBindable target, ISrvBindable depth,
        Matrix view, Matrix projection) => Draw(rc, target, depth, true, view, projection);

    static void Draw(MyRenderContext rc, IRtvBindable target, ISrvBindable depth, bool probe, Matrix view, Matrix projection)
    {
        if (rc == null || !rc.IsInitialized || target == null || depth == null) return;
        lock (Gate)
        {
            DrainRetiredArt();
            if (Providers.Count != 1) { status = Providers.Count == 0 ? "no provider" : "conflict: multiple celestial providers"; return; }
            Provider p = null;
            foreach (var candidate in Providers.Values) p = candidate;
            if (!p.Enabled) { status = "disabled: " + p.Id; return; }
            if (!ProbeHookAvailable) { status = "vanilla fallback: probe hook unavailable"; return; }
            if (ShaderPackRegistry.TryResolveOverlay("Lighting/LightDir.hlsl", out _) ||
                ShaderPackRegistry.TryResolveOverlay("EnvProbe/ForwardPostprocess.hlsl", out _))
            { status = "vanilla fallback: conflicting lighting/probe overlay"; return; }
            // A single-sample pass must not overwrite mixed foreground/background MSAA pixels.
            if (MyRender11.MultisamplingEnabled) { status = "vanilla fallback: MSAA is not supported"; return; }
            if (p.Failed) return;
            try
            {
                if (!EnsureShaders(p)) return;
                p.ArtState.Apply(CreateArt);
                var count = Math.Max(1, p.Data.Length / 4);
                if (dataBuffer == null || !ReferenceEquals(uploadedData, p.Data))
                {
                    if (dataBuffer != null) MyManagers.Buffers.Dispose(dataBuffer);
                    dataBuffer = null;
                    var initial = p.Data.Length == 0 ? new float[4] : p.Data;
                    var pinned = GCHandle.Alloc(initial, GCHandleType.Pinned);
                    try { dataBuffer = MyManagers.Buffers.CreateSrv("Anomaly.CelestialData", count, 16, pinned.AddrOfPinnedObject(), ResourceUsage.Immutable); }
                    finally { pinned.Free(); }
                    uploadedData = p.Data;
                }
                var mapping = MyMapping.MapDiscard(rc, uniformCb);
                try
                {
                    for (var i = 0; i < 16; i++)
                    {
                        var v = new Vector4(p.Uniforms[i * 4], p.Uniforms[i * 4 + 1], p.Uniforms[i * 4 + 2], p.Uniforms[i * 4 + 3]);
                        mapping.WriteAndPosition(ref v);
                    }
                }
                finally { mapping.Unmap(); }
                var size = target.Size;
                var rows = new[] {
                    new Vector4(view.M11, view.M12, view.M13, 0),
                    new Vector4(view.M21, view.M22, view.M23, 0),
                    new Vector4(view.M31, view.M32, view.M33, 0),
                    new Vector4(size.X, size.Y, projection.M11, projection.M22),
                    new Vector4(p.Data.Length / 4, 0, 0, 0) };
                mapping = MyMapping.MapDiscard(rc, viewCb);
                try { for (var i = 0; i < rows.Length; i++) mapping.WriteAndPosition(ref rows[i]); }
                finally { mapping.Unmap(); }

                // Record a real OM bind even on a fresh deferred context. No destination SRV is read.
                rc.AllShaderStages.SetSrv(0, null);
                rc.AllShaderStages.SetSrv(1, null);
                rc.AllShaderStages.SetSrv(2, null);
                rc.ResetTargets();
                rc.DeviceContext.OutputMerger.SetTargets(null, 1, new[] { target.Rtv });
                rc.SetRtv(target);
                rc.SetRasterizerState(MyRasterizerStateManager.NocullRasterizerState);
                rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
                rc.SetBlendState(MyBlendStateManager.BlendReplace);
                rc.SetInputLayout(null);
                rc.SetPrimitiveTopology(PrimitiveTopology.TriangleList);
                rc.SetVertexBuffer(0, null);
                rc.GeometryShader.Set(null);
                rc.VertexShader.Set(vertexShader);
                rc.PixelShader.Set(probe ? probeShader : mainShader);
                rc.PixelShader.SetConstantBuffer(0, MyCommon.FrameConstants);
                rc.PixelShader.SetConstantBuffer(6, viewCb);
                rc.PixelShader.SetConstantBuffer(7, uniformCb);
                rc.PixelShader.SetSrv(0, depth);
                rc.PixelShader.SetSrv(1, dataBuffer);
                rc.PixelShader.SetSrv(2, p.Art);
                rc.PixelShader.SetSampler(0, MySamplerStateManager.Linear);
                rc.SetViewport(0, 0, size.X, size.Y);
                rc.Draw(3, 0);
                if (!probe) { mainDrawnProvider = p; mainDrawnFrame = FrameTemporal.FrameIndex; }
                status = "active: " + p.Id + " (main + probes)";
            }
            catch (Exception e)
            {
                p.Failed = true;
                status = "vanilla fallback: " + e.Message;
                MyLog.Default.WriteLine("Anomaly celestial: " + status);
                if (RenderTrace.IsLostDevice(e)) throw;
            }
            finally
            {
                rc.PixelShader.SetSrv(0, null);
                rc.PixelShader.SetSrv(1, null);
                rc.PixelShader.SetSrv(2, null);
                rc.PixelShader.SetSampler(0, null);
                rc.PixelShader.SetConstantBuffer(6, null);
                rc.PixelShader.SetConstantBuffer(7, null);
                rc.SetRtvNull();
            }
        }
    }

    static bool EnsureShaders(Provider p)
    {
        if (ReferenceEquals(compiled, p) && compiledRevision == p.Revision && mainShader != null) return true;
        ReleaseGpu();
        PixelShader main = null, probe = null;
        VertexShader vertex = null;
        try
        {
            var vs = MyShaderCompiler.Compile(Path.Combine(ShaderCompileIntercept.IncludeDirectory, "Fullscreen.hlsl"),
                Array.Empty<ShaderMacro>(), MyShaderProfile.vs_5_0, "Anomaly.Celestial.VS", false);
            var a = Compile(p.File, false);
            var b = Compile(p.File, true);
            if (vs == null || vs.Length == 0 || a == null || a.Length == 0 || b == null || b.Length == 0)
                throw new InvalidOperationException("celestial main/probe compilation failed; both views retained vanilla");
            vertex = new VertexShader(MyRender11.DeviceInstance, vs);
            main = new PixelShader(MyRender11.DeviceInstance, a);
            probe = new PixelShader(MyRender11.DeviceInstance, b);
            viewCb = MyManagers.Buffers.CreateConstantBuffer("Anomaly.CelestialView", 80, usage: ResourceUsage.Dynamic);
            uniformCb = MyManagers.Buffers.CreateConstantBuffer("Anomaly.CelestialUniforms", 256, usage: ResourceUsage.Dynamic);
            vertexShader = vertex; mainShader = main; probeShader = probe;
            compiled = p; compiledRevision = p.Revision;
            return true;
        }
        catch
        {
            main?.Dispose(); probe?.Dispose(); vertex?.Dispose();
            ReleaseGpu();
            throw;
        }
    }

    static ArtBindable CreateArt(QueuedArtResource<ArtBindable>.Upload upload)
    {
        var handle = GCHandle.Alloc(upload.Rgba, GCHandleType.Pinned);
        Texture2D texture = null;
        ShaderResourceView srv = null;
        try
        {
            var boxes = new DataBox[upload.Slices];
            int sliceBytes = upload.Width * upload.Height * 4;
            for (int s = 0; s < upload.Slices; s++)
                boxes[s] = new DataBox(IntPtr.Add(handle.AddrOfPinnedObject(), s * sliceBytes), upload.Width * 4, 0);
            texture = new Texture2D(MyRender11.DeviceInstance, new Texture2DDescription
            {
                Width = upload.Width,
                Height = upload.Height,
                MipLevels = 1,
                ArraySize = upload.Slices,
                Format = Format.R8G8B8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Immutable,
                BindFlags = BindFlags.ShaderResource,
            }, boxes);
            srv = new ShaderResourceView(MyRender11.DeviceInstance, texture, new ShaderResourceViewDescription
            {
                Format = Format.R8G8B8A8_UNorm,
                Dimension = ShaderResourceViewDimension.Texture2DArray,
                Texture2DArray =
                {
                    MipLevels = 1,
                    FirstArraySlice = 0,
                    ArraySize = upload.Slices,
                    MostDetailedMip = 0,
                }
            });
            return new ArtBindable(texture, srv, upload.Width, upload.Height);
        }
        catch { srv?.Dispose(); texture?.Dispose(); throw; }
        finally { handle.Free(); }
    }

    // Called under Gate from the render path, including when no provider remains.
    static void DrainRetiredArt()
    {
        while (RetiredArt.Count > 0) RetiredArt.Dequeue().Dispose();
        if (compiled != null && (!Providers.TryGetValue(compiled.Id, out var live) || !ReferenceEquals(live, compiled)))
            ReleaseGpu();
    }

    static int compiledRevision;
    static byte[] Compile(string file, bool probe) => MyShaderCompiler.Compile(file,
        new[] { new ShaderMacro("ANOMALY_CELESTIAL_PROBE", probe ? "1" : "0") },
        MyShaderProfile.ps_5_0, "Anomaly.Celestial." + (probe ? "Probe" : "Main"), invalidateCache: true);

    internal static void Release()
    {
        lock (Gate)
        {
            foreach (var p in Providers.Values)
            {
                p.ArtState.Clear();
                p.Failed = false;
            }
            DrainRetiredArt();
            ReleaseGpu();
            status = "pending device resources";
        }
    }

    static void ReleaseGpu()
    {
        mainDrawnProvider = null;
        mainShader?.Dispose(); probeShader?.Dispose(); vertexShader?.Dispose();
        mainShader = null; probeShader = null; vertexShader = null;
        if (viewCb != null) MyManagers.Buffers.Dispose(viewCb);
        if (uniformCb != null) MyManagers.Buffers.Dispose(uniformCb);
        if (dataBuffer != null) MyManagers.Buffers.Dispose(dataBuffer);
        viewCb = null; uniformCb = null; dataBuffer = null; uploadedData = null; compiled = null;
    }

    sealed class ArtBindable : ISrvBindable, IDisposable
    {
        readonly Texture2D texture;
        public string Name => "Anomaly.CelestialArt";
        public SharpDX.Direct3D11.Resource Resource => texture;
        public ShaderResourceView Srv { get; }
        public Vector2I Size { get; }
        public Vector3I Size3 => new Vector3I(Size.X, Size.Y, 1);
        public ArtBindable(Texture2D tex, ShaderResourceView view, int w, int h)
        {
            texture = tex;
            Srv = view;
            Size = new Vector2I(w, h);
        }
        public void Dispose()
        {
            Srv?.Dispose();
            texture?.Dispose();
        }
    }

    sealed class Provider
    {
        public string Id, File;
        public bool Enabled = true, Failed, NativeSunGlare = true;
        public int Revision;
        public float[] Uniforms = new float[64], Data = Array.Empty<float>();
        public readonly QueuedArtResource<ArtBindable> ArtState = new(art => RetiredArt.Enqueue(art));
        public ISrvBindable Art => ArtState.Resource;
    }
}
