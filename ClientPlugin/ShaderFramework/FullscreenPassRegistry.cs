using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ClientPlugin.Buffers;
using ClientPlugin.ShaderFramework;
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

namespace ClientPlugin.Shaders;

/// <summary>
/// Well-known type. Resolve by name:
/// <c>ClientPlugin.Shaders.FullscreenPassRegistry</c>. Anomaly compiles and
/// draws pack <c>Fullscreen/</c> programs. Packs do not call Draw or create
/// RTs. <see cref="SetUniforms"/> writes the b7 blob for a program id.
/// </summary>
public static class FullscreenPassRegistry
{
    public const string IsolatedCatalog = "fullscreenIsolated";
    public const int UniformBytes = 64;

    const string VsFile = "Fullscreen.hlsl";
    const string MergeFile = "FullscreenMerge.hlsl";
    const int ExtrasBytes = 256;

    static readonly object Gate = new();
    static readonly List<Program> Programs = new();
    static readonly Dictionary<string, UniformCb> Uniforms =
        new(StringComparer.OrdinalIgnoreCase);
    static readonly PublishedBuffer IsolatedPublished = new();

    static VertexShader vertexShader;
    static PixelShader mergeCopy;
    static PixelShader mergeAdd;
    static PixelShader mergeOver;
    static IConstantBuffer extrasCb;
    static IConstantBuffer uniformCb;
    static IRtvTexture pingHdr;
    static IRtvTexture pongHdr;
    static IRtvTexture pingOut;
    static IRtvTexture pongOut;
    static int wHdr;
    static int hHdr;
    static int wOut;
    static int hOut;
    static bool scratchOutput;
    static int scratchW;
    static int scratchH;
    static bool helpersReady;
    static bool loggedError;
    static int scratchToggle;
    static string statusLine = "none";

    [StructLayout(LayoutKind.Sequential, Size = ExtrasBytes)]
    struct ExtrasCb
    {
        public Vector2 RenderSize;
        public Vector2 InvRenderSize;
        public uint HasVelocity;
        public uint HistoryValid;
        public uint AttachCount;
        public uint FrameIndex;
        public Vector2 JitterOffset;
        public Vector2 Pad1;
        public Matrix UnjitteredViewProj;
        public Matrix PrevViewProj;
    }

    [StructLayout(LayoutKind.Sequential, Size = UniformBytes)]
    struct UniformCb
    {
        public Vector4 V0;
        public Vector4 V1;
        public Vector4 V2;
        public Vector4 V3;
    }

    public static string StatusLine
    {
        get
        {
            lock (Gate)
                return string.IsNullOrEmpty(statusLine) ? "none" : statusLine;
        }
    }

    /// <summary>
    /// Pack-owned scalars for the next draw of <paramref name="id"/>.
    /// At most 16 floats (b7, <see cref="UniformBytes"/>). Longer arrays
    /// fail closed.
    /// </summary>
    public static bool SetUniforms(string id, float[] values)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;
        lock (Gate)
        {
            if (values == null || values.Length == 0)
            {
                Uniforms.Remove(id);
                return true;
            }

            if (values.Length > 16)
                return false;
            var u = default(UniformCb);
            u.V0 = Read4(values, 0);
            u.V1 = Read4(values, 4);
            u.V2 = Read4(values, 8);
            u.V3 = Read4(values, 12);
            Uniforms[id.Trim()] = u;
            return true;
        }
    }

    internal static bool HasSlot(OwnedPassSlot slot)
    {
        lock (Gate)
        {
            for (var i = 0; i < Programs.Count; i++)
            {
                if (Programs[i].Enabled && Programs[i].Slot == slot)
                    return true;
            }
        }

        return false;
    }

    internal static void ReplaceAll(IReadOnlyList<FullscreenProgramSpec> specs)
    {
        lock (Gate)
        {
            DisposeProgramsUnlocked();
            Programs.Clear();
            if (specs != null)
            {
                for (var i = 0; i < specs.Count; i++)
                    TryAddUnlocked(specs[i]);
            }

            ApplyConflictsUnlocked();
            statusLine = FormatStatusUnlocked();
        }
    }

    internal static void Run(OwnedPassSlot slot, MyRenderContext rc, object dest, bool outputRes)
    {
        if (rc == null || !rc.IsInitialized)
            return;
        Program[] snapshot;
        lock (Gate)
        {
            var n = 0;
            for (var i = 0; i < Programs.Count; i++)
            {
                if (Programs[i].Enabled && Programs[i].Slot == slot)
                    n++;
            }

            if (n == 0)
                return;
            snapshot = new Program[n];
            var w = 0;
            for (var i = 0; i < Programs.Count; i++)
            {
                if (!Programs[i].Enabled || Programs[i].Slot != slot)
                    continue;
                snapshot[w++] = Programs[i];
            }
        }

        FrameTemporal.EnsureSnapshot();
        scratchToggle = 0;
        IRtvBindable target = dest as IRtvBindable;
        if (target == null && dest is ICustomTexture custom)
            target = custom.Linear ?? custom.SRgb;
        if (target == null && CanMergeToLBuffer(slot))
            target = MyGBuffer.Main?.LBuffer;
        ISrvBindable sceneSrv = dest as ISrvBindable ?? target as ISrvBindable;
        if (sceneSrv == null)
            sceneSrv = MyGBuffer.Main?.LBuffer as ISrvBindable;

        ISrvBindable previous = sceneSrv;
        Program lastChain = null;
        for (var i = 0; i < snapshot.Length; i++)
        {
            var prog = snapshot[i];
            try
            {
                if ((prog.Policy & TemporalPolicy.InColor) != 0 &&
                    (prog.Policy & (TemporalPolicy.Reactive | TemporalPolicy.ContributeVelocity)) == 0)
                    MotionOut(prog);
                if ((prog.Policy & TemporalPolicy.Reactive) != 0)
                    TemporalParticipation.EnsureReactive();
                DrawOne(rc, prog, target, sceneSrv, ref previous, ref lastChain, outputRes);
            }
            catch (Exception e)
            {
                Warn("program '" + prog.Id + "' threw " + e.GetType().Name + ": " + e.Message);
            }
        }

        if (lastChain != null && lastChain.Compose == FullscreenCompose.Chain && previous != null &&
            target != null)
            MergeToDest(rc, previous, target, 0);

        try
        {
            rc.PixelShader.SetSrv(0, null);
            rc.PixelShader.SetSrv(1, null);
            rc.PixelShader.SetSrv(2, null);
            rc.PixelShader.SetSrv(3, null);
            rc.SetRtvNull();
        }
        catch
        {
            // Rich HUD
        }
    }

    internal static void OnResolutionChanged()
    {
        lock (Gate)
        {
            DisposeTargets();
            IsolatedPublished.Clear();
            BufferCatalog.Set(IsolatedCatalog, null);
        }
    }

    internal static void Release()
    {
        lock (Gate)
        {
            DisposeTargets();
            DisposeHelpers();
            DisposeProgramsUnlocked();
            IsolatedPublished.Clear();
            BufferCatalog.Set(IsolatedCatalog, null);
            Uniforms.Clear();
            helpersReady = false;
            statusLine = FormatStatusUnlocked();
        }
    }

    static void TryAddUnlocked(FullscreenProgramSpec spec)
    {
        if (spec == null || string.IsNullOrWhiteSpace(spec.Id) || string.IsNullOrWhiteSpace(spec.File) ||
            !File.Exists(spec.File))
            return;
        for (var i = 0; i < Programs.Count; i++)
        {
            if (!string.Equals(Programs[i].Id, spec.Id, StringComparison.OrdinalIgnoreCase))
                continue;
            Warn("duplicate fullscreen id '" + spec.Id + "' — fail closed, skipped");
            return;
        }

        Programs.Add(new Program
        {
            Id = spec.Id.Trim(),
            PackId = spec.PackId,
            Slot = spec.Slot,
            Compose = spec.Compose,
            Priority = spec.Priority,
            Policy = spec.Policy,
            File = spec.File,
            OutputName = string.IsNullOrWhiteSpace(spec.OutputName)
                ? "pass." + spec.Id.Trim()
                : spec.OutputName.Trim(),
            Enabled = true
        });
        Programs.Sort(Compare);
    }

    static void ApplyConflictsUnlocked()
    {
        foreach (OwnedPassSlot slot in Enum.GetValues(typeof(OwnedPassSlot)))
        {
            Program replace = null;
            var replaceCount = 0;
            for (var i = 0; i < Programs.Count; i++)
            {
                if (!Programs[i].Enabled || Programs[i].Slot != slot)
                    continue;
                if (Programs[i].Compose != FullscreenCompose.Replace)
                    continue;
                replaceCount++;
                replace = Programs[i];
            }

            if (replaceCount > 1)
            {
                Warn("Replace claimed twice on " + slot + " — fail closed, those programs disabled");
                for (var i = 0; i < Programs.Count; i++)
                {
                    if (Programs[i].Slot == slot && Programs[i].Compose == FullscreenCompose.Replace)
                        Programs[i].Enabled = false;
                }

                continue;
            }

            if (replaceCount == 1 && replace != null)
            {
                for (var i = 0; i < Programs.Count; i++)
                {
                    if (Programs[i].Slot != slot || ReferenceEquals(Programs[i], replace))
                        continue;
                    if (!Programs[i].Enabled)
                        continue;
                    Programs[i].Enabled = false;
                    Warn("Replace '" + replace.Id + "' on " + slot + " disables '" + Programs[i].Id +
                         "' — fail closed");
                }
            }
        }
    }

    static void DrawOne(MyRenderContext rc, Program prog, IRtvBindable dest, ISrvBindable scene,
        ref ISrvBindable previous, ref Program lastChain, bool outputRes)
    {
        if (!EnsureHelpers(rc) || !EnsureProgram(prog))
            return;
        if (!EnsureScratch(outputRes))
            return;
        var isolated = NextScratch();
        if (isolated == null)
            return;

        var t0 = prog.Compose == FullscreenCompose.Chain ? (previous ?? scene) : scene;
        WriteExtras(rc, outputRes);
        WriteUniforms(rc, prog.Id);
        BindBus(rc, t0);
        rc.SetRasterizerState(MyRasterizerStateManager.NocullRasterizerState);
        rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
        rc.SetInputLayout(null);
        rc.SetPrimitiveTopology(PrimitiveTopology.TriangleList);
        rc.SetVertexBuffer(0, null);
        rc.GeometryShader.Set(null);
        rc.VertexShader.Set(vertexShader);
        rc.PixelShader.Set(prog.Shader);

        rc.SetViewport(0f, 0f, scratchW, scratchH, 0f, 1f);

        if (prog.Compose == FullscreenCompose.Replace && dest != null)
        {
            DrawIsolated(rc, isolated);
            MergeToDest(rc, isolated, dest, 0);
            PublishIsolated(isolated);
            PublishNamed(prog, isolated);
            previous = isolated;
            lastChain = null;
            return;
        }

        if (prog.Compose == FullscreenCompose.DirectAdd && dest != null)
        {
            DrawIsolated(rc, isolated);
            MergeToDest(rc, isolated, dest, 1);
            PublishIsolated(isolated);
            PublishNamed(prog, isolated);
            previous = isolated;
            lastChain = null;
            return;
        }

        DrawIsolated(rc, isolated);
        PublishIsolated(isolated);
        PublishNamed(prog, isolated);
        previous = isolated;
        if (prog.Compose == FullscreenCompose.Chain)
        {
            lastChain = prog;
            return;
        }

        lastChain = null;
        if (prog.Compose == FullscreenCompose.PublishOnly || dest == null)
            return;
        if (prog.Compose == FullscreenCompose.IsolatedMix)
            MergeToDest(rc, isolated, dest, 2);
        else
            MergeToDest(rc, isolated, dest, 1);
    }

    static void DrawIsolated(MyRenderContext rc, IRtvTexture isolated)
    {
        rc.SetBlendState(MyBlendStateManager.BlendReplace);
        rc.SetRtv(isolated);
        rc.Draw(3, 0);
        UnbindBus(rc);
        rc.SetRtvNull();
    }

    static void MergeToDest(MyRenderContext rc, ISrvBindable isolated, IRtvBindable dest, int mode)
    {
        var shader = mode == 1 ? mergeAdd : mode == 2 ? mergeOver : mergeCopy;
        if (shader == null || dest == null || isolated == null)
            return;
        ISrvBindable destHistory = null;
        if (mode != 0)
        {
            var historyRt = isolated as IRtvTexture;
            historyRt = historyRt != null ? OtherScratch(historyRt) : null;
            if (historyRt != null && dest is ISrvBindable destSrv)
            {
                Blit(rc, destSrv, historyRt);
                destHistory = historyRt;
            }
            else
                destHistory = dest as ISrvBindable;
        }

        rc.SetBlendState(MyBlendStateManager.BlendReplace);
        rc.SetRtv(dest);
        rc.VertexShader.Set(vertexShader);
        rc.PixelShader.Set(shader);
        rc.PixelShader.SetSampler(0, MySamplerStateManager.Point);
        rc.PixelShader.SetSrv(0, isolated);
        rc.PixelShader.SetSrv(1, destHistory);
        rc.Draw(3, 0);
        rc.PixelShader.SetSrv(0, null);
        rc.PixelShader.SetSrv(1, null);
        rc.SetRtvNull();
    }

    static void Blit(MyRenderContext rc, ISrvBindable src, IRtvBindable dest)
    {
        if (mergeCopy == null || src == null || dest == null)
            return;
        rc.SetBlendState(MyBlendStateManager.BlendReplace);
        rc.SetRtv(dest);
        rc.VertexShader.Set(vertexShader);
        rc.PixelShader.Set(mergeCopy);
        rc.PixelShader.SetSampler(0, MySamplerStateManager.Point);
        rc.PixelShader.SetSrv(0, src);
        rc.PixelShader.SetSrv(1, null);
        rc.Draw(3, 0);
        rc.PixelShader.SetSrv(0, null);
        rc.SetRtvNull();
    }

    static void BindBus(MyRenderContext rc, ISrvBindable scene)
    {
        rc.AllShaderStages.SetConstantBuffer(6, extrasCb);
        rc.AllShaderStages.SetConstantBuffer(7, uniformCb);
        rc.PixelShader.SetSampler(0, MySamplerStateManager.Point);
        rc.PixelShader.SetSampler(1, MySamplerStateManager.Linear);
        rc.PixelShader.SetSrv(0, scene);
        var depth = BufferCatalog.Active(BufferCatalog.LinearDepth);
        rc.PixelShader.SetSrv(1, depth != null && depth.IsAvailable ? depth.Srv as ISrvBindable : null);
        var vel = BufferCatalog.Active(BufferCatalog.Velocity);
        rc.PixelShader.SetSrv(2, vel != null && vel.IsAvailable ? vel.Srv as ISrvBindable : null);
        var react = BufferCatalog.Active(BufferCatalog.ReactiveMask);
        rc.PixelShader.SetSrv(3, react != null && react.IsAvailable ? react.Srv as ISrvBindable : null);
    }

    static void UnbindBus(MyRenderContext rc)
    {
        rc.PixelShader.SetSrv(0, null);
        rc.PixelShader.SetSrv(1, null);
        rc.PixelShader.SetSrv(2, null);
        rc.PixelShader.SetSrv(3, null);
        rc.AllShaderStages.SetConstantBuffer(6, null);
        rc.AllShaderStages.SetConstantBuffer(7, null);
    }

    static void WriteExtras(MyRenderContext rc, bool outputRes)
    {
        if (extrasCb == null)
            return;
        var size = outputRes ? MyRender11.ViewportResolution : MyRender11.ResolutionI;
        var vel = BufferCatalog.Active(BufferCatalog.Velocity);
        var hist = VelocityRegistry.Active;
        var w = size.X > 0 ? size.X : 1;
        var h = size.Y > 0 ? size.Y : 1;
        var cb = new ExtrasCb
        {
            RenderSize = new Vector2(w, h),
            InvRenderSize = new Vector2(1f / w, 1f / h),
            HasVelocity = vel != null && vel.IsAvailable ? 1u : 0u,
            HistoryValid = hist != null && hist.HistoryValid ? 1u : 0u,
            AttachCount = 0,
            FrameIndex = FrameTemporal.FrameIndex,
            JitterOffset = new Vector2(FrameTemporal.JitterX, FrameTemporal.JitterY),
            Pad1 = Vector2.Zero,
            UnjitteredViewProj = FrameTemporal.UnjitteredViewProj,
            PrevViewProj = FrameTemporal.PrevViewProj
        };
        var mapping = MyMapping.MapDiscard(rc, extrasCb);
        mapping.WriteAndPosition(ref cb);
        mapping.Unmap();
    }

    static void WriteUniforms(MyRenderContext rc, string id)
    {
        if (uniformCb == null)
            return;
        UniformCb u;
        lock (Gate)
            Uniforms.TryGetValue(id, out u);
        var mapping = MyMapping.MapDiscard(rc, uniformCb);
        mapping.WriteAndPosition(ref u);
        mapping.Unmap();
    }

    static bool EnsureHelpers(MyRenderContext rc)
    {
        if (helpersReady)
            return true;
        var vsPath = FindHlsl(VsFile);
        var mergePath = FindHlsl(MergeFile);
        if (vsPath == null || mergePath == null)
        {
            Fail("Fullscreen.hlsl / FullscreenMerge.hlsl missing");
            return false;
        }

        var vsBc = MyShaderCompiler.Compile(vsPath, Array.Empty<ShaderMacro>(), MyShaderProfile.vs_5_0,
            "Anomaly.Fullscreen.VS", invalidateCache: false);
        var copyBc = CompileMerge(mergePath, 0);
        var addBc = CompileMerge(mergePath, 1);
        var overBc = CompileMerge(mergePath, 2);
        if (vsBc == null || vsBc.Length == 0 || copyBc == null || addBc == null || overBc == null)
        {
            Fail("fullscreen helper compile failed");
            return false;
        }

        var device = MyRender11.DeviceInstance;
        vertexShader = new VertexShader(device, vsBc) { DebugName = "Anomaly.Fullscreen.VS" };
        mergeCopy = new PixelShader(device, copyBc) { DebugName = "Anomaly.Fullscreen.MergeCopy" };
        mergeAdd = new PixelShader(device, addBc) { DebugName = "Anomaly.Fullscreen.MergeAdd" };
        mergeOver = new PixelShader(device, overBc) { DebugName = "Anomaly.Fullscreen.MergeOver" };
        extrasCb = MyManagers.Buffers.CreateConstantBuffer("Anomaly.FullscreenExtrasCB", ExtrasBytes,
            usage: ResourceUsage.Dynamic);
        uniformCb = MyManagers.Buffers.CreateConstantBuffer("Anomaly.FullscreenUniformsCB", UniformBytes,
            usage: ResourceUsage.Dynamic);
        helpersReady = true;
        return true;
    }

    static byte[] CompileMerge(string path, int mode)
    {
        return MyShaderCompiler.Compile(path, new[] { new ShaderMacro("MERGE_MODE", mode.ToString()) },
            MyShaderProfile.ps_5_0, "Anomaly.Fullscreen.Merge" + mode, invalidateCache: false);
    }

    static bool EnsureProgram(Program prog)
    {
        if (prog.Shader != null)
            return true;
        var macros = new[]
        {
            new ShaderMacro("ANOMALY_FULLSCREEN", "1"),
            new ShaderMacro("ANOMALY_FULLSCREEN_SLOT_" + prog.Slot.ToString().ToUpperInvariant(), "1")
        };
        var bc = MyShaderCompiler.Compile(prog.File, macros, MyShaderProfile.ps_5_0,
            "Anomaly.Fullscreen." + prog.Id, invalidateCache: false);
        if (bc == null || bc.Length == 0)
        {
            Warn("compile failed pack=" + prog.PackId + " id=" + prog.Id);
            prog.Enabled = false;
            return false;
        }

        prog.Shader = new PixelShader(MyRender11.DeviceInstance, bc)
        {
            DebugName = "Anomaly.Fullscreen." + prog.Id
        };
        return true;
    }

    static bool EnsureScratch(bool outputRes)
    {
        var size = outputRes ? MyRender11.ViewportResolution : MyRender11.ResolutionI;
        if (size.X <= 0 || size.Y <= 0)
            return false;
        scratchOutput = outputRes;
        if (outputRes)
        {
            if (pingOut == null || wOut != size.X || hOut != size.Y)
            {
                DisposePair(ref pingOut, ref pongOut);
                pingOut = CreateScratch("Anomaly.Fullscreen.OutPing", size.X, size.Y);
                pongOut = CreateScratch("Anomaly.Fullscreen.OutPong", size.X, size.Y);
                wOut = size.X;
                hOut = size.Y;
            }

            scratchW = wOut;
            scratchH = hOut;
            return pingOut != null && pongOut != null;
        }

        if (pingHdr == null || wHdr != size.X || hHdr != size.Y)
        {
            DisposePair(ref pingHdr, ref pongHdr);
            pingHdr = CreateScratch("Anomaly.Fullscreen.Ping", size.X, size.Y);
            pongHdr = CreateScratch("Anomaly.Fullscreen.Pong", size.X, size.Y);
            wHdr = size.X;
            hHdr = size.Y;
        }

        scratchW = wHdr;
        scratchH = hHdr;
        return pingHdr != null && pongHdr != null;
    }

    static IRtvTexture CreateScratch(string name, int w, int h)
    {
        return MyManagers.RwTextures.CreateRtv(name, w, h, Format.R16G16B16A16_Float);
    }

    static IRtvTexture NextScratch()
    {
        scratchToggle++;
        if (scratchOutput)
            return (scratchToggle & 1) == 0 ? pingOut : pongOut;
        return (scratchToggle & 1) == 0 ? pingHdr : pongHdr;
    }

    static IRtvTexture OtherScratch(IRtvTexture current)
    {
        if (current == pingHdr)
            return pongHdr;
        if (current == pongHdr)
            return pingHdr;
        if (current == pingOut)
            return pongOut;
        if (current == pongOut)
            return pingOut;
        return null;
    }

    static Vector4 Read4(float[] values, int offset)
    {
        return new Vector4(
            offset < values.Length ? values[offset] : 0,
            offset + 1 < values.Length ? values[offset + 1] : 0,
            offset + 2 < values.Length ? values[offset + 2] : 0,
            offset + 3 < values.Length ? values[offset + 3] : 0);
    }

    static void PublishIsolated(IRtvTexture isolated)
    {
        if (isolated == null)
            return;
        var native = isolated.Resource != null ? isolated.Resource.NativePointer : IntPtr.Zero;
        IsolatedPublished.Publish(isolated, native, scratchW, scratchH);
        BufferCatalog.Set(IsolatedCatalog, IsolatedPublished);
    }

    static void PublishNamed(Program prog, IRtvTexture isolated)
    {
        if (isolated == null || string.IsNullOrEmpty(prog.OutputName))
            return;
        if (BufferCatalog.IsReservedName(prog.OutputName))
            return;
        var native = isolated.Resource != null ? isolated.Resource.NativePointer : IntPtr.Zero;
        var published = prog.Published ?? new PublishedBuffer();
        published.Publish(isolated, native, scratchW, scratchH);
        prog.Published = published;
        if (!string.IsNullOrEmpty(prog.PackId))
            BufferCatalog.Publish(prog.PackId, prog.OutputName, published);
        else
            BufferCatalog.Set(prog.OutputName, published);
    }

    static bool CanMergeToLBuffer(OwnedPassSlot slot)
    {
        return slot == OwnedPassSlot.AfterLighting ||
               slot == OwnedPassSlot.AfterAtmosphere ||
               slot == OwnedPassSlot.AfterTransparent ||
               slot == OwnedPassSlot.BeforeTonemap;
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

        return null;
    }

    static void DisposeTargets()
    {
        DisposePair(ref pingHdr, ref pongHdr);
        DisposePair(ref pingOut, ref pongOut);
        wHdr = hHdr = wOut = hOut = scratchW = scratchH = 0;
    }

    static void DisposePair(ref IRtvTexture a, ref IRtvTexture b)
    {
        if (a != null)
            MyManagers.RwTextures.DisposeTex(ref a);
        if (b != null)
            MyManagers.RwTextures.DisposeTex(ref b);
        a = null;
        b = null;
    }

    static void DisposeHelpers()
    {
        vertexShader?.Dispose();
        mergeCopy?.Dispose();
        mergeAdd?.Dispose();
        mergeOver?.Dispose();
        vertexShader = null;
        mergeCopy = null;
        mergeAdd = null;
        mergeOver = null;
        if (extrasCb != null)
        {
            MyManagers.Buffers.Dispose(new[] { extrasCb });
            extrasCb = null;
        }

        if (uniformCb != null)
        {
            MyManagers.Buffers.Dispose(new[] { uniformCb });
            uniformCb = null;
        }
    }

    static void DisposeProgramsUnlocked()
    {
        for (var i = 0; i < Programs.Count; i++)
        {
            Programs[i].Shader?.Dispose();
            Programs[i].Shader = null;
            if (string.IsNullOrEmpty(Programs[i].OutputName))
                continue;
            if (!string.IsNullOrEmpty(Programs[i].PackId))
                BufferCatalog.Unpublish(Programs[i].PackId, Programs[i].OutputName);
            else
                BufferCatalog.Set(Programs[i].OutputName, null);
        }
    }

    static int Compare(Program a, Program b)
    {
        var p = a.Priority.CompareTo(b.Priority);
        return p != 0 ? p : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
    }

    static string FormatStatusUnlocked()
    {
        if (Programs.Count == 0)
            return "none";
        var sb = new StringBuilder();
        for (var i = 0; i < Programs.Count; i++)
        {
            if (!Programs[i].Enabled)
                continue;
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(Programs[i].Slot).Append('/').Append(Programs[i].Compose).Append(':')
                .Append(Programs[i].Id);
        }

        return sb.Length == 0 ? "none" : sb.ToString();
    }

    static void MotionOut(Program prog)
    {
        if (prog.WarnedMotion)
            return;
        prog.WarnedMotion = true;
        Warn("id=" + prog.Id + " InColor after Scheduler.Done without Reactive/ContributeVelocity (motion-out)");
    }

    static void Fail(string message)
    {
        if (loggedError)
            return;
        loggedError = true;
        Warn(message);
    }

    static void Warn(string message)
    {
        MyLog.Default.WriteLine("Anomaly fullscreen: " + message);
        DebugLog.Write("FullscreenPassRegistry WARN " + message);
    }

    sealed class Program
    {
        public string Id;
        public string PackId;
        public OwnedPassSlot Slot;
        public FullscreenCompose Compose;
        public int Priority;
        public TemporalPolicy Policy;
        public string File;
        public string OutputName;
        public bool Enabled;
        public bool WarnedMotion;
        public PixelShader Shader;
        public PublishedBuffer Published;
    }
}

internal sealed class FullscreenProgramSpec
{
    public string Id;
    public string PackId;
    public OwnedPassSlot Slot;
    public FullscreenCompose Compose;
    public int Priority;
    public TemporalPolicy Policy;
    public string File;
    public string OutputName;
}
