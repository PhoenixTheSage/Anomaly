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
using SharpDX.Mathematics.Interop;
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
/// <see cref="SetEnabled"/> is the pack checkbox — independent of Replace
/// fail-closed. <see cref="RequestLitMips"/> asks for catalog <c>litMips</c>.
/// </summary>
public static class FullscreenPassRegistry
{
    public const string IsolatedCatalog = "fullscreenIsolated";
    public const int UniformFloats = 64;
    public const int UniformBytes = 256;
    public const int PackSrvBase = 7;
    public const int PackSrvLast = 9;

    const string VsFile = "Fullscreen.hlsl";
    const string MergeFile = "FullscreenMerge.hlsl";
    const int ExtrasBytes = 256;

    static readonly object Gate = new();
    static readonly HashSet<string> LoggedWarnings = new();
    static readonly List<Program> Programs = new();
    static readonly Dictionary<string, UniformCb> Uniforms =
        new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, List<SrvBind>> ExtraSrvs =
        new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, bool> PackEnabledById =
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
        public float SafetyScale;
        public float SafetyPad;
        public Matrix UnjitteredViewProj;
        public Matrix PrevViewProj;
        public Vector4 CameraToWorldR0;
        public Vector4 CameraToWorldR1;
        public Vector4 CameraToWorldR2;
        public Vector2 ProjScale;
        public Vector2 CameraToWorldPad;
    }

    [StructLayout(LayoutKind.Sequential, Size = UniformBytes)]
    struct UniformCb
    {
        public Vector4 V0;
        public Vector4 V1;
        public Vector4 V2;
        public Vector4 V3;
        public Vector4 V4;
        public Vector4 V5;
        public Vector4 V6;
        public Vector4 V7;
        public Vector4 V8;
        public Vector4 V9;
        public Vector4 V10;
        public Vector4 V11;
        public Vector4 V12;
        public Vector4 V13;
        public Vector4 V14;
        public Vector4 V15;
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
    /// At most <see cref="UniformFloats"/> floats on b7 (16×float4,
    /// 256 B, same size as extras). <c>AnomalyPassUniform0–7</c> stay
    /// the first 128 bytes. Longer arrays fail closed; Anomaly logs
    /// once per program id.
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

            var key = id.Trim();
            if (values.Length > UniformFloats)
            {
                Warn($"SetUniforms '{key}': {values.Length} floats exceeds {UniformFloats} (b7). Fail closed.");
                return false;
            }

            Uniforms[key] = PackUniforms(values);
            return true;
        }
    }

    /// <summary>
    /// Bind catalog texture <paramref name="catalogName"/> on fullscreen
    /// program <paramref name="programId"/> at t7–t9. Slot <c>-1</c> assigns
    /// the next free reserved SRV. Fail closed if the bank is exhausted or
    /// the slot is already owned by a different catalog name.
    /// </summary>
    public static bool RequestSrv(string programId, string catalogName, int slot = -1)
    {
        if (string.IsNullOrWhiteSpace(programId) || string.IsNullOrWhiteSpace(catalogName))
            return false;
        lock (Gate)
        {
            try
            {
                return QueueSrvUnlocked(programId.Trim(), catalogName.Trim(), slot);
            }
            catch (Exception e)
            {
                Warn("RequestSrv(" + programId + "," + catalogName + "): " + e.Message);
                return false;
            }
        }
    }

    /// <summary>
    /// Pack checkbox for program <paramref name="programId"/>. Independent of
    /// Replace fail-closed (<see cref="Program.Enabled"/>). Survives pack
    /// reload. Draw runs only when both flags are true.
    /// </summary>
    public static bool SetEnabled(string programId, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(programId))
            return false;
        lock (Gate)
        {
            var id = programId.Trim();
            PackEnabledById[id] = enabled;
            for (var i = 0; i < Programs.Count; i++)
            {
                if (!string.Equals(Programs[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    continue;
                Programs[i].PackEnabled = enabled;
            }

            statusLine = FormatStatusUnlocked();
            return true;
        }
    }

    /// <summary>
    /// Ask Anomaly for catalog <c>litMips</c> this session. <paramref name="mipLevels"/>
    /// <c>&lt;= 0</c> clears the explicit request; json / <see cref="RequestSrv"/>
    /// binds still generate when that program is live.
    /// </summary>
    public static void RequestLitMips(int mipLevels = 5)
    {
        OwnedBuffersPass.RequestLitMips(mipLevels);
    }

    internal static bool HasSlot(OwnedPassSlot slot)
    {
        lock (Gate)
        {
            for (var i = 0; i < Programs.Count; i++)
            {
                if (IsLiveUnlocked(Programs[i]) && Programs[i].Slot == slot)
                    return true;
            }
        }

        return false;
    }

    internal static bool HasPolicy(OwnedPassSlot slot, TemporalPolicy policy)
    {
        if (policy == TemporalPolicy.None)
            return false;
        lock (Gate)
        {
            for (var i = 0; i < Programs.Count; i++)
            {
                if (IsLiveUnlocked(Programs[i]) && Programs[i].Slot == slot &&
                    (Programs[i].Policy & policy) != 0)
                    return true;
            }
        }

        return false;
    }

    internal static bool WantsCatalog(string catalogName)
    {
        if (string.IsNullOrWhiteSpace(catalogName))
            return false;
        lock (Gate)
        {
            foreach (var kv in ExtraSrvs)
            {
                if (!IsLiveIdUnlocked(kv.Key) || kv.Value == null)
                    continue;
                for (var i = 0; i < kv.Value.Count; i++)
                {
                    if (string.Equals(kv.Value[i].CatalogName, catalogName,
                            StringComparison.OrdinalIgnoreCase))
                        return true;
                }
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

    internal static void Run(OwnedPassSlot slot, MyRenderContext rc, object dest, bool outputRes,
        object scene = null)
    {
        if (rc == null || !rc.IsInitialized)
            return;
        Program[] snapshot;
        lock (Gate)
        {
            var n = 0;
            for (var i = 0; i < Programs.Count; i++)
            {
                if (IsLiveUnlocked(Programs[i]) && Programs[i].Slot == slot)
                    n++;
            }

            if (n == 0)
                return;
            snapshot = new Program[n];
            var w = 0;
            for (var i = 0; i < Programs.Count; i++)
            {
                if (!IsLiveUnlocked(Programs[i]) || Programs[i].Slot != slot)
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
        ISrvBindable sceneSrv = scene as ISrvBindable;
        if (sceneSrv == null)
            sceneSrv = dest as ISrvBindable ?? target as ISrvBindable;
        if (sceneSrv == null)
        {
            var up = BufferCatalog.Active(BufferCatalog.UpscaledColor);
            if (up != null && up.IsAvailable)
                sceneSrv = up.Srv as ISrvBindable;
        }

        if (sceneSrv == null)
            sceneSrv = MyGBuffer.Main?.LBuffer as ISrvBindable;

        ISrvBindable previous = sceneSrv;
        Program lastChain = null;
        for (var i = 0; i < snapshot.Length; i++)
        {
            var prog = snapshot[i];
            RenderTrace.Begin(prog.Id);
            try
            {
                if ((prog.Policy & TemporalPolicy.InColor) != 0 &&
                    (prog.Policy & (TemporalPolicy.Reactive | TemporalPolicy.ContributeVelocity)) == 0)
                    MotionOut(prog);
                if ((prog.Policy & TemporalPolicy.Reactive) != 0)
                    TemporalParticipation.EnsureReactive(rc);
                DrawOne(rc, prog, target, sceneSrv, ref previous, ref lastChain, outputRes);
            }
            catch (Exception e)
            {
                RenderTrace.DumpIfLost(prog.Id, e);
                if (RenderTrace.IsLostDevice(e))
                    throw;
                Warn("program '" + prog.Id + "' threw " + e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                RenderTrace.End(prog.Id);
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
            rc.PixelShader.SetSrv(4, null);
            rc.PixelShader.SetSrv(5, null);
            rc.PixelShader.SetSrv(6, null);
            rc.PixelShader.SetSrv(7, null);
            rc.PixelShader.SetSrv(8, null);
            rc.PixelShader.SetSrv(9, null);
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
            ExtraSrvs.Clear();
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
            Enabled = true,
            PackEnabled = !PackEnabledById.TryGetValue(spec.Id.Trim(), out var packOn) || packOn
        });
        if (spec.Binds != null)
        {
            for (var b = 0; b < spec.Binds.Length; b++)
            {
                var bind = spec.Binds[b];
                if (bind == null || string.IsNullOrWhiteSpace(bind.CatalogName))
                    continue;
                QueueSrvUnlocked(spec.Id.Trim(), bind.CatalogName.Trim(), bind.Slot);
            }
        }

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
        rc.SetRasterizerState(MyRasterizerStateManager.NocullRasterizerState);
        rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
        rc.SetInputLayout(null);
        rc.SetPrimitiveTopology(PrimitiveTopology.TriangleList);
        rc.SetVertexBuffer(0, null);
        rc.GeometryShader.Set(null);
        rc.VertexShader.Set(vertexShader);
        rc.PixelShader.Set(prog.Shader);

        rc.SetViewport(0f, 0f, scratchW, scratchH, 0f, 1f);

        // Transparent ConsumeWork replays this deferred list onto the
        // immediate context after lighting left LBuffer (and sometimes
        // GBuffer1/2) as RTVs. Unbind SRVs first, then force a single RTV
        // before BindBus samples LBuffer / GBuffer / velocity. BindBus-first
        // or Keen SetRtv-only is an RTV+SRV hazard (DEVICE_REMOVED at Present).
        UnbindBus(rc);
        rc.ClearRtv(isolated, default(RawColor4));
        rc.SetBlendState(MyBlendStateManager.BlendReplace);
        BindSingleRtv(rc, isolated);
        BindBus(rc, t0, prog);

        if (prog.Compose == FullscreenCompose.Replace && dest != null)
        {
            DrawIsolated(rc, prog, isolated);
            MergeToDest(rc, isolated, dest, 0);
            PublishIsolated(isolated);
            PublishNamed(prog, isolated);
            previous = isolated;
            lastChain = null;
            return;
        }

        if (prog.Compose == FullscreenCompose.DirectAdd && dest != null)
        {
            DrawIsolated(rc, prog, isolated);
            MergeToDest(rc, isolated, dest, 1);
            PublishIsolated(isolated);
            PublishNamed(prog, isolated);
            previous = isolated;
            lastChain = null;
            return;
        }

        DrawIsolated(rc, prog, isolated);
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

    static void DrawIsolated(MyRenderContext rc, Program prog, IRtvTexture isolated)
    {
        rc.Draw(3, 0);
        UnbindBus(rc);
        rc.SetRtvNull();
        if ((prog.Policy & TemporalPolicy.Reactive) != 0 &&
            prog.Compose != FullscreenCompose.Replace)
            TemporalParticipation.StampFromIsolated(rc, isolated);
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
                return;
        }

        rc.SetBlendState(MyBlendStateManager.BlendReplace);
        UnbindBus(rc);
        BindSingleRtv(rc, dest);
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
        UnbindBus(rc);
        BindSingleRtv(rc, dest);
        rc.VertexShader.Set(vertexShader);
        rc.PixelShader.Set(mergeCopy);
        rc.PixelShader.SetSampler(0, MySamplerStateManager.Point);
        rc.PixelShader.SetSrv(0, src);
        rc.PixelShader.SetSrv(1, null);
        rc.Draw(3, 0);
        rc.PixelShader.SetSrv(0, null);
        rc.SetRtvNull();
    }

    /// <summary>
    /// Keen <c>SetRtvNull</c> is <c>SetTarget(null)</c> and skips the native
    /// call when the deferred tracker already has count=0 (fresh
    /// <c>AcquireRC</c>). That omits OMSetRenderTargets from the command list,
    /// so ConsumeWork replays BindBus onto lighting's leftover LBuffer RTV.
    /// Always record a one-RTV native bind on this context.
    /// </summary>
    static void BindSingleRtv(MyRenderContext rc, IRtvBindable rtv)
    {
        rc.ResetTargets();
        if (rtv?.Rtv == null)
            return;
        if (rc.DeviceContext != null)
            rc.DeviceContext.OutputMerger.SetTargets(null, 1, new[] { rtv.Rtv });
        rc.SetRtv(rtv);
    }

    static void BindBus(MyRenderContext rc, ISrvBindable scene, Program prog)
    {
        rc.AllShaderStages.SetConstantBuffer(6, extrasCb);
        rc.AllShaderStages.SetConstantBuffer(7, uniformCb);
        rc.PixelShader.SetSampler(0, MySamplerStateManager.Point);
        rc.PixelShader.SetSampler(1, MySamplerStateManager.Linear);
        rc.PixelShader.SetSampler(2, MySamplerStateManager.CloudSampler);
        rc.PixelShader.SetSrv(0, scene);
        var depth = BufferCatalog.Active(BufferCatalog.LinearDepth);
        rc.PixelShader.SetSrv(1, depth != null && depth.IsAvailable ? depth.Srv as ISrvBindable : null);
        var vel = BufferCatalog.Active(BufferCatalog.Velocity);
        rc.PixelShader.SetSrv(2, vel != null && vel.IsAvailable ? vel.Srv as ISrvBindable : null);
        var react = BufferCatalog.Active(BufferCatalog.ReactiveMask);
        rc.PixelShader.SetSrv(3, react != null && react.IsAvailable ? react.Srv as ISrvBindable : null);
        if (IsHdrGBufferSlot(prog.Slot))
        {
            var gb = MyGBuffer.Main;
            rc.PixelShader.SetSrv(4, gb?.GBuffer0);
            rc.PixelShader.SetSrv(5, gb?.GBuffer1);
            rc.PixelShader.SetSrv(6, gb?.GBuffer2);
        }
        else
        {
            var avgLum = BufferCatalog.Active(BufferCatalog.AvgLuminance);
            rc.PixelShader.SetSrv(4, avgLum != null && avgLum.IsAvailable ? avgLum.Srv as ISrvBindable : null);
            var bloom = BufferCatalog.Active(BufferCatalog.Bloom);
            rc.PixelShader.SetSrv(5, bloom != null && bloom.IsAvailable ? bloom.Srv as ISrvBindable : null);
            var dirt = BufferCatalog.Active(BufferCatalog.Dirt);
            rc.PixelShader.SetSrv(6, dirt != null && dirt.IsAvailable ? dirt.Srv as ISrvBindable : null);
        }

        BindPackSrvs(rc, prog.Id);
    }

    static void BindPackSrvs(MyRenderContext rc, string programId)
    {
        List<SrvBind> binds;
        lock (Gate)
        {
            if (!ExtraSrvs.TryGetValue(programId, out binds) || binds == null || binds.Count == 0)
                return;
            binds = new List<SrvBind>(binds);
        }

        for (var i = 0; i < binds.Count; i++)
        {
            var bind = binds[i];
            if (bind.Slot < PackSrvBase || bind.Slot > PackSrvLast)
                continue;
            var buf = BufferCatalog.Active(bind.CatalogName);
            rc.PixelShader.SetSrv(bind.Slot,
                buf != null && buf.IsAvailable ? buf.Srv as ISrvBindable : null);
        }
    }

    static void UnbindBus(MyRenderContext rc)
    {
        rc.PixelShader.SetSrv(0, null);
        rc.PixelShader.SetSrv(1, null);
        rc.PixelShader.SetSrv(2, null);
        rc.PixelShader.SetSrv(3, null);
        rc.PixelShader.SetSrv(4, null);
        rc.PixelShader.SetSrv(5, null);
        rc.PixelShader.SetSrv(6, null);
        rc.PixelShader.SetSrv(7, null);
        rc.PixelShader.SetSrv(8, null);
        rc.PixelShader.SetSrv(9, null);
        rc.PixelShader.SetSampler(2, null);
        rc.AllShaderStages.SetConstantBuffer(6, null);
        rc.AllShaderStages.SetConstantBuffer(7, null);
    }

    static bool QueueSrvUnlocked(string programId, string catalogName, int slot)
    {
        if (!ExtraSrvs.TryGetValue(programId, out var list) || list == null)
        {
            list = new List<SrvBind>();
            ExtraSrvs[programId] = list;
        }

        if (slot < 0)
            slot = NextFreePackSlotUnlocked(list);
        if (slot < PackSrvBase || slot > PackSrvLast)
        {
            Warn("RequestSrv slot t" + slot + " for '" + catalogName + "' on '" + programId +
                 "' is outside t" + PackSrvBase + "–t" + PackSrvLast);
            return false;
        }

        for (var i = 0; i < list.Count; i++)
        {
            var existing = list[i];
            if (string.Equals(existing.CatalogName, catalogName, StringComparison.OrdinalIgnoreCase))
            {
                existing.Slot = slot;
                return true;
            }

            if (existing.Slot == slot &&
                !string.Equals(existing.CatalogName, catalogName, StringComparison.OrdinalIgnoreCase))
            {
                Warn("RequestSrv slot t" + slot + " on '" + programId + "' already '" +
                     existing.CatalogName + "', skipped '" + catalogName + "'");
                return false;
            }
        }

        list.Add(new SrvBind { CatalogName = catalogName, Slot = slot });
        return true;
    }

    static int NextFreePackSlotUnlocked(List<SrvBind> list)
    {
        var used = new bool[PackSrvLast + 1];
        if (list != null)
        {
            for (var i = 0; i < list.Count; i++)
            {
                var s = list[i].Slot;
                if (s >= PackSrvBase && s <= PackSrvLast)
                    used[s] = true;
            }
        }

        for (var slot = PackSrvBase; slot <= PackSrvLast; slot++)
        {
            if (!used[slot])
                return slot;
        }

        return -1;
    }

    static bool IsHdrGBufferSlot(OwnedPassSlot slot)
    {
        return CanMergeToLBuffer(slot);
    }

    static void WriteExtras(MyRenderContext rc, bool outputRes)
    {
        if (extrasCb == null)
            return;
        var size = outputRes ? MyRender11.ViewportResolution : MyRender11.ResolutionI;
        var w = size.X > 0 ? size.X : 1;
        var h = size.Y > 0 ? size.Y : 1;
        var vel = BufferCatalog.Active(BufferCatalog.Velocity);
        var hist = VelocityRegistry.Active;
        var cb = new ExtrasCb
        {
            RenderSize = new Vector2(w, h),
            InvRenderSize = new Vector2(1f / w, 1f / h),
            HasVelocity = vel != null && vel.IsAvailable ? 1u : 0u,
            HistoryValid = hist != null && hist.HistoryValid ? 1u : 0u,
            AttachCount = 0,
            FrameIndex = FrameTemporal.FrameIndex,
            JitterOffset = new Vector2(FrameTemporal.JitterX, FrameTemporal.JitterY),
            SafetyScale = FrameTemporal.SafetyScale,
            SafetyPad = 0f,
            UnjitteredViewProj = FrameTemporal.UnjitteredViewProj,
            PrevViewProj = FrameTemporal.PrevViewProj,
            CameraToWorldR0 = FrameTemporal.CameraToWorldRow(0),
            CameraToWorldR1 = FrameTemporal.CameraToWorldRow(1),
            CameraToWorldR2 = FrameTemporal.CameraToWorldRow(2),
            ProjScale = FrameTemporal.ProjScale,
            CameraToWorldPad = Vector2.Zero
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

    internal static void CollectWarmupJobs(List<ShaderWarmup.Job> jobs)
    {
        lock (Gate)
        {
            if (!helpersReady)
            {
                var vsPath = FindHlsl(VsFile);
                var mergePath = FindHlsl(MergeFile);
                ShaderWarmup.Add(jobs, vsPath, MyShaderProfile.vs_5_0, "Anomaly.Fullscreen.VS");
                if (!string.IsNullOrEmpty(mergePath))
                {
                    ShaderWarmup.Add(jobs, mergePath, MyShaderProfile.ps_5_0, "Anomaly.Fullscreen.Merge0",
                        new ShaderMacro("MERGE_MODE", "0"));
                    ShaderWarmup.Add(jobs, mergePath, MyShaderProfile.ps_5_0, "Anomaly.Fullscreen.Merge1",
                        new ShaderMacro("MERGE_MODE", "1"));
                    ShaderWarmup.Add(jobs, mergePath, MyShaderProfile.ps_5_0, "Anomaly.Fullscreen.Merge2",
                        new ShaderMacro("MERGE_MODE", "2"));
                }
            }

            for (var i = 0; i < Programs.Count; i++)
            {
                var prog = Programs[i];
                if (prog.Shader != null || !prog.Enabled)
                    continue;
                ShaderWarmup.Add(jobs, prog.File, MyShaderProfile.ps_5_0, "Anomaly.Fullscreen." + prog.Id,
                    new ShaderMacro("ANOMALY_FULLSCREEN", "1"),
                    new ShaderMacro("ANOMALY_FULLSCREEN_SLOT_" + prog.Slot.ToString().ToUpperInvariant(), "1"));
            }
        }
    }

    internal static void Prewarm()
    {
        EnsureHelpers(null);
        Program[] snapshot;
        lock (Gate)
            snapshot = Programs.ToArray();
        for (var i = 0; i < snapshot.Length; i++)
            EnsureProgram(snapshot[i]);
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
        if (!prog.Enabled)
            return false;
        var macros = new[]
        {
            new ShaderMacro("ANOMALY_FULLSCREEN", "1"),
            new ShaderMacro("ANOMALY_FULLSCREEN_SLOT_" + prog.Slot.ToString().ToUpperInvariant(), "1")
        };
        try
        {
            FullscreenHlslLint.WarnIfUnbounded(prog.Id, prog.File);
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
        catch (Exception e)
        {
            Warn("compile failed pack=" + prog.PackId + " id=" + prog.Id + ": " + e.Message);
            prog.Enabled = false;
            return false;
        }
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

    static UniformCb PackUniforms(float[] values)
    {
        return new UniformCb
        {
            V0 = Read4(values, 0),
            V1 = Read4(values, 4),
            V2 = Read4(values, 8),
            V3 = Read4(values, 12),
            V4 = Read4(values, 16),
            V5 = Read4(values, 20),
            V6 = Read4(values, 24),
            V7 = Read4(values, 28),
            V8 = Read4(values, 32),
            V9 = Read4(values, 36),
            V10 = Read4(values, 40),
            V11 = Read4(values, 44),
            V12 = Read4(values, 48),
            V13 = Read4(values, 52),
            V14 = Read4(values, 56),
            V15 = Read4(values, 60),
        };
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

    static bool IsLiveUnlocked(Program prog)
    {
        return prog != null && prog.Enabled && prog.PackEnabled;
    }

    static bool IsLiveIdUnlocked(string id)
    {
        for (var i = 0; i < Programs.Count; i++)
        {
            if (!string.Equals(Programs[i].Id, id, StringComparison.OrdinalIgnoreCase))
                continue;
            return IsLiveUnlocked(Programs[i]);
        }

        return false;
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
            if (!Programs[i].PackEnabled)
                sb.Append('-');
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
        RenderTrace.DumpIfLost("fullscreen", null);
        if (loggedError)
            return;
        loggedError = true;
        Warn(message);
    }

    static void Warn(string message)
    {
        lock (Gate)
        {
            if (!LoggedWarnings.Add(message))
                return;
        }

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
        public bool PackEnabled = true;
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
    public SrvBind[] Binds;
}

internal sealed class SrvBind
{
    public string CatalogName;
    public int Slot = -1;
}
