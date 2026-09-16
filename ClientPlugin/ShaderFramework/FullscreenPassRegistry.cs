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
/// <see cref="SetEnabled"/> is the pack checkbox — independent of two-Replace
/// fail-closed. A live Replace is the only compose drawn that frame; a
/// pack-disabled Replace does not clear Isolated siblings. <see cref="SetScale"/>
/// / json <c>passes[].scale</c> size the isolated RT (1, 0.5, 0.25). Replace
/// ignores scale. AfterFullscreen C# that draws its own scaled RT must call
/// <see cref="DrawFullscreen"/> with that RT's size — Keen
/// <c>DrawFullscreenQuad()</c> with no viewport calls <c>SetScreenViewport()</c>.
/// <see cref="RequestLitMips"/> asks for catalog <c>litMips</c>.
/// <see cref="TryGetProgramStatus"/> is the pack Status dialog bind.
/// </summary>
public static class FullscreenPassRegistry
{
    public const string IsolatedCatalog = "fullscreenIsolated";
    public const int UniformFloats = 64;
    public const int UniformBytes = 256;
    public const int PackSrvBase = 7;
    public const int PackSrvLast = 9;
    public const int PointLightsSlot = 10;
    public const int TileIndicesSlot = 11;

    const string VsFile = "Fullscreen.hlsl";
    const string MergeFile = "FullscreenMerge.hlsl";
    const int ExtrasBytes = 304;

    static readonly object Gate = new();
    static readonly HashSet<string> LoggedWarnings = new();
    static readonly List<Program> Programs = new();
    static readonly Dictionary<string, UniformCb> Uniforms =
        new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, List<SrvBind>> ExtraSrvs =
        new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, bool> PackEnabledById =
        new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, float> PackScaleById =
        new(StringComparer.OrdinalIgnoreCase);
    static readonly PublishedBuffer IsolatedPublished = new();

    static VertexShader vertexShader;
    static PixelShader mergeCopy;
    static PixelShader mergeAdd;
    static PixelShader mergeOver;
    static PixelShader mergeSub;
    static BlendState isolatedSubBlend;
    static IConstantBuffer extrasCb;
    static IConstantBuffer uniformCb;
    static IUavTexture pingHdr;
    static IUavTexture pongHdr;
    static IUavTexture pingOut;
    static IUavTexture pongOut;
    static int wHdr;
    static int hHdr;
    static int wOut;
    static int hOut;
    static bool scratchOutput;
    static int scratchW;
    static int scratchH;
    static bool helpersReady;
    static bool loggedError;
    static bool warnedMergeDest;
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
        public Vector2 SceneSize;
        public Vector2 InvSceneSize;
        public Vector3 SunColor;
        public float SunDiffuse;
        public Vector3 SunToward;
        public float SkyLuma;
        public Vector3 SkyAmbient;
        public float SkyAmbientPad;
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
    /// 256 B). Extras on b6 are 304 B. <c>AnomalyPassUniform0–7</c> stay
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
    /// two-Replace fail-closed (<see cref="Program.Enabled"/>). Survives pack
    /// reload. Draw runs only when both flags are true. A live Replace still
    /// skips other compose on that slot that frame without clearing their
    /// <see cref="Program.Enabled"/> flag.
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

            ApplyConflictsUnlocked();
            statusLine = FormatStatusUnlocked();
            return true;
        }
    }

    /// <summary>
    /// Isolated RT scale for <paramref name="programId"/>: 1, 0.5, or 0.25.
    /// Snaps to those three. Replace compose stays dest-sized. Survives pack
    /// reload. GBuffer / depth / velocity stay full-res — sample them by UV
    /// via <c>AnomalyScenePixel</c>, not <c>SV_Position</c>.
    /// </summary>
    public static bool SetScale(string programId, float scale)
    {
        if (string.IsNullOrWhiteSpace(programId))
            return false;
        lock (Gate)
        {
            var id = programId.Trim();
            var snapped = SnapScale(scale);
            PackScaleById[id] = snapped;
            for (var i = 0; i < Programs.Count; i++)
            {
                if (!string.Equals(Programs[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (Programs[i].Scale == snapped)
                    continue;
                DisposeScaled(Programs[i]);
                Programs[i].Scale = snapped;
            }

            statusLine = FormatStatusUnlocked();
            return true;
        }
    }

    /// <summary>
    /// Fullscreen triangle-strip through Keen's copy VS, viewport = the RT.
    /// Keen <c>MyScreenPass.DrawFullscreenQuad()</c> with no viewport calls
    /// <c>SetScreenViewport()</c>. A half/quarter RT then stores only the
    /// top-left of UV 0–1, so screen-space lighting stretches with quality.
    /// </summary>
    public static void DrawFullscreen(MyRenderContext rc, int width, int height)
    {
        if (rc == null || width <= 0 || height <= 0)
            return;
        MyScreenPass.DrawFullscreenQuad(rc, new MyViewport(width, height));
    }

    /// <summary>
    /// Pass-sized isolated output for <paramref name="programId"/> (after
    /// <see cref="SetScale"/> / json scale). Replace reports dest / scene size.
    /// </summary>
    public static bool TryGetOutputSize(string programId, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(programId))
            return false;
        lock (Gate)
        {
            for (var i = 0; i < Programs.Count; i++)
            {
                if (!string.Equals(Programs[i].Id, programId.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;
                var scene = SceneSize(Programs[i].Slot);
                var pass = IsolatedSize(Programs[i], scene);
                width = pass.X;
                height = pass.Y;
                return width > 0 && height > 0;
            }
        }

        return false;
    }

    /// <summary>
    /// One-line health for pack Status dialogs. Unknown id returns false.
    /// Compile failures keep the FXC first line so a pack page can show it
    /// without scraping <c>Anomaly.debug.log</c>.
    /// </summary>
    public static bool TryGetProgramStatus(string programId, out string status)
    {
        status = null;
        if (string.IsNullOrWhiteSpace(programId))
            return false;
        lock (Gate)
        {
            for (var i = 0; i < Programs.Count; i++)
            {
                if (!string.Equals(Programs[i].Id, programId.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;
                status = DescribeUnlocked(Programs[i]);
                return true;
            }
        }

        return false;
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

    /// <summary>
    /// Request catalog <c>pointShadowAtlas</c>. <paramref name="maxLights"/>
    /// <c>&lt;= 0</c> skips cubes. Default 4, max 64.
    /// </summary>
    public static void RequestPointShadows(int maxLights = 4, int faceResolution = 64)
    {
        PointShadowPass.RequestPointShadows(maxLights, faceResolution);
    }

    /// <summary>
    /// Request catalog <c>occupancy</c>. Pass false to clear.
    /// </summary>
    public static void RequestOccupancy(bool enabled = true)
    {
        PointShadowPass.RequestOccupancy(enabled);
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
            var liveReplace = 0;
            string replaceId = null;
            for (var i = 0; i < Programs.Count; i++)
            {
                if (!IsLiveUnlocked(Programs[i]) || Programs[i].Slot != slot)
                    continue;
                if (Programs[i].Compose == FullscreenCompose.Replace)
                {
                    liveReplace++;
                    if (replaceId == null)
                        replaceId = Programs[i].Id;
                }
            }

            if (liveReplace > 0)
            {
                for (var i = 0; i < Programs.Count; i++)
                {
                    if (!IsLiveUnlocked(Programs[i]) || Programs[i].Slot != slot)
                        continue;
                    if (Programs[i].Compose == FullscreenCompose.Replace)
                        continue;
                    Programs[i].LastSkip = "live-replace:" + (replaceId ?? "Replace");
                }
            }

            var n = 0;
            for (var i = 0; i < Programs.Count; i++)
            {
                if (!IsLiveUnlocked(Programs[i]) || Programs[i].Slot != slot)
                    continue;
                if (liveReplace > 0 && Programs[i].Compose != FullscreenCompose.Replace)
                    continue;
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
                if (liveReplace > 0 && Programs[i].Compose != FullscreenCompose.Replace)
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
            MergeToDest(rc, previous, target, 0, outputRes);

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
            UnbindCompute(rc);
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
            DisposeScaledUnlocked();
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
            PackEnabled = !PackEnabledById.TryGetValue(spec.Id.Trim(), out var packOn) || packOn,
            Scale = PackScaleById.TryGetValue(spec.Id.Trim(), out var packScale)
                ? packScale
                : SnapScale(spec.Scale)
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
            var replaceCount = 0;
            for (var i = 0; i < Programs.Count; i++)
            {
                if (!IsLiveUnlocked(Programs[i]) || Programs[i].Slot != slot)
                    continue;
                if (Programs[i].Compose != FullscreenCompose.Replace)
                    continue;
                replaceCount++;
            }

            // Two live Replaces fail closed. One live Replace is exclusive at
            // draw time (Run skips other compose) and must not clear Isolated
            // siblings — a pack-disabled debug Replace used to kill IsolatedSub.
            if (replaceCount <= 1)
                continue;

            Warn("Replace claimed twice on " + slot + " — fail closed, those programs disabled");
            for (var i = 0; i < Programs.Count; i++)
            {
                if (Programs[i].Slot == slot && Programs[i].Compose == FullscreenCompose.Replace)
                    Programs[i].Enabled = false;
            }
        }
    }

    static void DrawOne(MyRenderContext rc, Program prog, IRtvBindable dest, ISrvBindable scene,
        ref ISrvBindable previous, ref Program lastChain, bool outputRes)
    {
        if (!EnsureHelpers(rc))
        {
            NoteDraw(prog, "helpers-missing");
            return;
        }

        if (!EnsureProgram(prog))
        {
            NoteDraw(prog, string.IsNullOrEmpty(prog.LastError) ? "program-not-ready" : "compile-failed");
            return;
        }

        var t0 = prog.Compose == FullscreenCompose.Chain ? (previous ?? scene) : scene;
        // Display Replace after DLSS writes the dest it also samples as t0.
        // In-place is an RTV+SRV (and UAV+SRV) hazard — keep scratch+copy there
        // so SE-DLSS __result stays the graded image. Otherwise draw to dest.
        if (prog.Compose == FullscreenCompose.Replace && dest != null && !Aliases(dest, t0))
        {
            var size = dest.Size;
            if (size.X <= 0 || size.Y <= 0)
                return;
            scratchW = size.X;
            scratchH = size.Y;
            WriteExtras(rc, outputRes, size);
            WriteUniforms(rc, prog.Id);
            if (!TryDispatchCompute(rc, prog, dest, t0, size))
                DrawReplacePixel(rc, prog, dest, t0, size);
            PublishReplaceDest(dest, prog);
            previous = dest as ISrvBindable ?? dest as IRtvTexture;
            lastChain = null;
            NoteDraw(prog, null, "replace-dest", dest);
            return;
        }

        var sceneSize = SceneSize(outputRes);
        var pass = IsolatedSize(prog, sceneSize);
        IRtvTexture isolated;
        if (UsesScaledScratch(prog, pass, sceneSize))
        {
            isolated = EnsureScaledScratch(prog, pass);
            scratchW = pass.X;
            scratchH = pass.Y;
        }
        else
        {
            if (!EnsureScratch(outputRes))
                return;
            isolated = NextScratch();
            pass = new Vector2I(scratchW, scratchH);
        }

        if (isolated == null)
        {
            NoteDraw(prog, "scratch-null");
            return;
        }

        WriteExtras(rc, outputRes, pass, sceneSize);
        WriteUniforms(rc, prog.Id);

        // dest aliases t0 (DLSS in-place): grade into UAV scratch, then copy
        // so __result stays the graded dest. Compute when the scratch is a UAV.
        if (prog.Compose == FullscreenCompose.Replace && dest != null)
        {
            var size = new Vector2I(scratchW, scratchH);
            if (TryDispatchCompute(rc, prog, isolated, t0, size))
            {
                MergeToDest(rc, isolated, dest, 0, outputRes);
                PublishIsolated(isolated);
                PublishNamed(prog, isolated);
                previous = isolated;
                lastChain = null;
                return;
            }
        }

        // Transparent ConsumeWork replays this deferred list onto the
        // immediate context after lighting left LBuffer (and sometimes
        // GBuffer1/2) as RTVs. Unbind SRVs first, then force a single RTV
        // before BindBus samples LBuffer / GBuffer / velocity. BindBus-first
        // or Keen SetRtv-only is an RTV+SRV hazard (DEVICE_REMOVED at Present).
        // AfterLighting dest aliases t0 (LBuffer). IsolatedAdd/Mix that
        // sample dest blit a copy before the pack PS (Blit binds mergeCopy).
        // IsolatedSub writes occupancy and blends onto dest — skip that blit.
        var t0Draw = t0;
        if (dest != null && t0 != null && Aliases(dest, t0) &&
            prog.Compose != FullscreenCompose.Replace &&
            prog.Compose != FullscreenCompose.IsolatedSub)
        {
            var copy = OtherScratch(isolated);
            if (copy != null)
            {
                Blit(rc, t0, copy);
                t0Draw = copy;
            }
        }

        rc.SetRasterizerState(MyRasterizerStateManager.NocullRasterizerState);
        rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
        rc.SetInputLayout(null);
        rc.SetPrimitiveTopology(PrimitiveTopology.TriangleList);
        rc.SetVertexBuffer(0, null);
        rc.GeometryShader.Set(null);
        rc.VertexShader.Set(vertexShader);
        rc.PixelShader.Set(prog.Shader);
        rc.SetViewport(0f, 0f, scratchW, scratchH, 0f, 1f);

        UnbindBus(rc);
        if (prog.Compose != FullscreenCompose.Replace)
            rc.ClearRtv(isolated, default(RawColor4));
        rc.SetBlendState(MyBlendStateManager.BlendReplace);
        BindSingleRtv(rc, isolated);
        BindBus(rc, t0Draw, prog);

        if (prog.Compose == FullscreenCompose.Replace && dest != null)
        {
            DrawIsolated(rc, prog, isolated);
            MergeToDest(rc, isolated, dest, 0, outputRes);
            PublishIsolated(isolated);
            PublishNamed(prog, isolated);
            previous = isolated;
            lastChain = null;
            return;
        }

        if (prog.Compose == FullscreenCompose.DirectAdd && dest != null)
        {
            DrawIsolated(rc, prog, isolated);
            MergeToDest(rc, isolated, dest, 1, outputRes);
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
        if (prog.Compose == FullscreenCompose.PublishOnly)
        {
            NoteDraw(prog, null, "publish-only");
            return;
        }

        if (dest == null)
        {
            NoteDraw(prog, "dest-null");
            return;
        }

        if (prog.Compose == FullscreenCompose.IsolatedMix)
        {
            MergeToDest(rc, isolated, dest, 2, outputRes);
            NoteDraw(prog, null, "over", dest);
        }
        else if (prog.Compose == FullscreenCompose.IsolatedSub)
        {
            if (!MergeIsolatedSub(rc, isolated, dest))
                NoteDraw(prog, isolatedSubBlend == null ? "merge-blend-missing" : "merge-blend-failed",
                    null, dest);
            else
                NoteDraw(prog, null, "blend dest*(1-src)", dest);
        }
        else
        {
            MergeToDest(rc, isolated, dest, 1, outputRes);
            NoteDraw(prog, null, "add", dest);
        }
    }

    static void DrawIsolated(MyRenderContext rc, Program prog, IRtvTexture isolated)
    {
        rc.PixelShader.Set(prog.Shader);
        rc.Draw(3, 0);
        UnbindBus(rc);
        rc.SetRtvNull();
        if ((prog.Policy & TemporalPolicy.Reactive) != 0 &&
            prog.Compose != FullscreenCompose.Replace)
            TemporalParticipation.StampFromIsolated(rc, isolated,
                prog.Compose == FullscreenCompose.IsolatedSub);
        if ((prog.Policy & TemporalPolicy.ContributeVelocity) != 0 &&
            prog.Compose != FullscreenCompose.Replace)
            TemporalParticipation.ContributeFromIsolated(rc, isolated);
    }

    static bool Aliases(IRtvBindable dest, ISrvBindable scene)
    {
        if (dest == null || scene == null)
            return false;
        if (ReferenceEquals(dest, scene))
            return true;
        try
        {
            var a = dest.Resource;
            var b = scene.Resource;
            return a != null && b != null && ReferenceEquals(a, b);
        }
        catch
        {
            return true;
        }
    }

    static IUavBindable AsUav(IRtvBindable dest)
    {
        if (dest is IUavBindable uav)
            return uav;
        if (dest is IBorrowedCustomTexture borrowed && borrowed.Linear is IUavBindable borrowedUav)
            return borrowedUav;
        if (dest is ICustomTexture custom && custom.Linear is IUavBindable customUav)
            return customUav;
        return null;
    }

    static void DrawReplacePixel(MyRenderContext rc, Program prog, IRtvBindable dest, ISrvBindable t0,
        Vector2I size)
    {
        rc.SetRasterizerState(MyRasterizerStateManager.NocullRasterizerState);
        rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
        rc.SetInputLayout(null);
        rc.SetPrimitiveTopology(PrimitiveTopology.TriangleList);
        rc.SetVertexBuffer(0, null);
        rc.GeometryShader.Set(null);
        rc.VertexShader.Set(vertexShader);
        rc.PixelShader.Set(prog.Shader);
        rc.SetViewport(0f, 0f, size.X, size.Y, 0f, 1f);
        UnbindBus(rc);
        rc.SetBlendState(MyBlendStateManager.BlendReplace);
        BindSingleRtv(rc, dest);
        BindBus(rc, t0, prog);
        rc.Draw(3, 0);
        UnbindBus(rc);
        rc.SetRtvNull();
    }

    static bool TryDispatchCompute(MyRenderContext rc, Program prog, IRtvBindable dest, ISrvBindable t0,
        Vector2I size)
    {
        if (prog.Compute == null)
            return false;
        var uav = AsUav(dest);
        if (uav == null)
            return false;

        UnbindBus(rc);
        rc.ResetTargets();
        if (rc.DeviceContext != null)
            rc.DeviceContext.OutputMerger.SetTargets((RenderTargetView)null);
        rc.SetRtvNull();
        BindComputeBus(rc, t0, prog, uav);
        rc.ComputeShader.Set(prog.Compute);
        rc.Dispatch((size.X + 7) / 8, (size.Y + 7) / 8, 1);
        UnbindCompute(rc);
        return true;
    }

    static void PublishReplaceDest(IRtvBindable dest, Program prog)
    {
        var tex = dest as IRtvTexture;
        if (tex == null && dest is ICustomTexture custom)
            tex = custom.Linear as IRtvTexture ?? custom.SRgb as IRtvTexture;
        if (tex == null)
            return;
        PublishIsolated(tex);
        PublishNamed(prog, tex);
    }

    static void BindComputeBus(MyRenderContext rc, ISrvBindable scene, Program prog, IUavBindable uav)
    {
        rc.ComputeShader.SetConstantBuffer(6, extrasCb);
        rc.ComputeShader.SetConstantBuffer(7, uniformCb);
        rc.ComputeShader.SetSampler(0, MySamplerStateManager.Point);
        rc.ComputeShader.SetSampler(1, MySamplerStateManager.Linear);
        rc.ComputeShader.SetSampler(2, MySamplerStateManager.CloudSampler);
        BindComputeSrvs(rc, scene, prog);
        rc.ComputeShader.SetUav(0, uav);
    }

    static void BindComputeSrvs(MyRenderContext rc, ISrvBindable scene, Program prog)
    {
        rc.ComputeShader.SetSrv(0, scene);
        var depth = BufferCatalog.Active(BufferCatalog.LinearDepth);
        rc.ComputeShader.SetSrv(1, depth != null && depth.IsAvailable ? depth.Srv as ISrvBindable : null);
        var vel = BufferCatalog.Active(BufferCatalog.Velocity);
        rc.ComputeShader.SetSrv(2, vel != null && vel.IsAvailable ? vel.Srv as ISrvBindable : null);
        var react = BufferCatalog.Active(BufferCatalog.ReactiveMask);
        rc.ComputeShader.SetSrv(3, react != null && react.IsAvailable ? react.Srv as ISrvBindable : null);
        if (IsHdrGBufferSlot(prog.Slot))
        {
            var gb = MyGBuffer.Main;
            rc.ComputeShader.SetSrv(4, gb?.GBuffer0);
            rc.ComputeShader.SetSrv(5, gb?.GBuffer1);
            rc.ComputeShader.SetSrv(6, gb?.GBuffer2);
        }
        else
        {
            var avgLum = BufferCatalog.Active(BufferCatalog.AvgLuminance);
            rc.ComputeShader.SetSrv(4, avgLum != null && avgLum.IsAvailable ? avgLum.Srv as ISrvBindable : null);
            var bloom = BufferCatalog.Active(BufferCatalog.Bloom);
            rc.ComputeShader.SetSrv(5, bloom != null && bloom.IsAvailable ? bloom.Srv as ISrvBindable : null);
            var dirt = BufferCatalog.Active(BufferCatalog.Dirt);
            rc.ComputeShader.SetSrv(6, dirt != null && dirt.IsAvailable ? dirt.Srv as ISrvBindable : null);
        }

        BindPackSrvs(rc, prog.Id, compute: true);
        BindPointLights(rc, compute: true, IsHdrGBufferSlot(prog.Slot));
    }

    static void UnbindCompute(MyRenderContext rc)
    {
        try
        {
            rc.ComputeShader.SetUav(0, null);
            rc.ComputeShader.SetSrv(0, null);
            rc.ComputeShader.SetSrv(1, null);
            rc.ComputeShader.SetSrv(2, null);
            rc.ComputeShader.SetSrv(3, null);
            rc.ComputeShader.SetSrv(4, null);
            rc.ComputeShader.SetSrv(5, null);
            rc.ComputeShader.SetSrv(6, null);
            rc.ComputeShader.SetSrv(7, null);
            rc.ComputeShader.SetSrv(8, null);
            rc.ComputeShader.SetSrv(9, null);
            rc.ComputeShader.SetSrv(10, null);
            rc.ComputeShader.SetSrv(11, null);
            rc.ComputeShader.SetSampler(2, null);
            rc.ComputeShader.SetConstantBuffer(0, null);
            rc.ComputeShader.Set(null);
        }
        catch
        {
            // Compute stage may be unset on a fresh deferred list.
        }
    }

    static void MergeToDest(MyRenderContext rc, ISrvBindable isolated, IRtvBindable dest, int mode,
        bool outputRes, ISrvBindable destHistoryOverride = null)
    {
        var shader = mode == 1 ? mergeAdd : mode == 2 ? mergeOver : mode == 3 ? mergeSub : mergeCopy;
        if (shader == null || dest == null || isolated == null)
            return;
        var destSize = dest.Size;
        if (destSize.X <= 0 || destSize.Y <= 0)
            return;
        ISrvBindable destHistory = null;
        if (mode != 0)
        {
            if (destHistoryOverride != null && !Aliases(dest, destHistoryOverride))
            {
                destHistory = destHistoryOverride;
            }
            else
            {
                if (!EnsureScratch(outputRes))
                    return;
                var historyRt = scratchOutput ? pingOut : pingHdr;
                if (ReferenceEquals(isolated, historyRt))
                    historyRt = scratchOutput ? pongOut : pongHdr;
                if (historyRt != null && dest is ISrvBindable destSrv)
                {
                    Blit(rc, destSrv, historyRt);
                    destHistory = historyRt;
                }
                else
                {
                    if (!warnedMergeDest)
                    {
                        warnedMergeDest = true;
                        Warn("merge skipped (dest is not ISrvBindable) mode=" + mode);
                    }
                    return;
                }
            }
        }

        rc.SetViewport(0f, 0f, destSize.X, destSize.Y, 0f, 1f);
        rc.SetBlendState(MyBlendStateManager.BlendReplace);
        UnbindBus(rc);
        BindSingleRtv(rc, dest);
        rc.VertexShader.Set(vertexShader);
        rc.PixelShader.Set(shader);
        rc.PixelShader.SetSampler(0, MySamplerStateManager.Linear);
        rc.PixelShader.SetSrv(0, isolated);
        rc.PixelShader.SetSrv(1, destHistory);
        rc.Draw(3, 0);
        rc.PixelShader.SetSrv(0, null);
        rc.PixelShader.SetSrv(1, null);
        rc.SetRtvNull();
    }

    /// <summary>
    /// IsolatedSub dest-write: draw occupancy onto dest with
    /// <c>dest.rgb * (1 - src.rgb)</c> blend. Same dest RTV as Replace.
    /// destHistory Dest[p] merge never reached Present.
    /// </summary>
    static bool MergeIsolatedSub(MyRenderContext rc, ISrvBindable isolated, IRtvBindable dest)
    {
        if (mergeCopy == null || dest == null || isolated == null)
            return false;
        if (isolatedSubBlend == null)
            isolatedSubBlend = CreateIsolatedSubBlend();
        if (isolatedSubBlend == null)
            return false;
        var destSize = dest.Size;
        if (destSize.X <= 0 || destSize.Y <= 0)
            return false;
        rc.SetViewport(0f, 0f, destSize.X, destSize.Y, 0f, 1f);
        UnbindBus(rc);
        BindSingleRtv(rc, dest);
        rc.VertexShader.Set(vertexShader);
        rc.PixelShader.Set(mergeCopy);
        rc.PixelShader.SetSampler(0, MySamplerStateManager.Linear);
        rc.PixelShader.SetSrv(0, isolated);
        rc.PixelShader.SetSrv(1, null);
        var ctx = rc.DeviceContext;
        if (ctx == null)
            return false;
        var prevBlend = ctx.OutputMerger.BlendState;
        ctx.OutputMerger.SetBlendState(isolatedSubBlend);
        rc.Draw(3, 0);
        ctx.OutputMerger.SetBlendState(prevBlend);
        rc.SetBlendState(MyBlendStateManager.BlendReplace);
        rc.PixelShader.SetSrv(0, null);
        rc.SetRtvNull();
        return true;
    }

    static BlendState CreateIsolatedSubBlend()
    {
        var device = MyRender11.DeviceInstance;
        if (device == null)
            return null;
        var desc = new BlendStateDescription
        {
            AlphaToCoverageEnable = false,
            IndependentBlendEnable = false
        };
        desc.RenderTarget[0] = new RenderTargetBlendDescription
        {
            IsBlendEnabled = true,
            SourceBlend = BlendOption.Zero,
            DestinationBlend = BlendOption.InverseSourceColor,
            BlendOperation = BlendOperation.Add,
            SourceAlphaBlend = BlendOption.Zero,
            DestinationAlphaBlend = BlendOption.One,
            AlphaBlendOperation = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteMaskFlags.All
        };
        return new BlendState(device, desc) { DebugName = "Anomaly.Fullscreen.IsolatedSub" };
    }

    static void NoteDraw(Program prog, string skip, string merge = null, IRtvBindable dest = null)
    {
        if (prog == null)
            return;
        prog.LastSkip = skip ?? "";
        if (merge != null)
            prog.LastMerge = merge;
        if (dest != null)
            prog.LastDest = DescribeRt(dest);
        prog.LastDrawTick = Environment.TickCount;
    }

    static string DescribeRt(IRtvBindable dest)
    {
        if (dest == null)
            return "null";
        try
        {
            var size = dest.Size;
            var name = dest.GetType().Name;
            return name + " " + size.X + "x" + size.Y;
        }
        catch
        {
            return dest.GetType().Name;
        }
    }

    static void Blit(MyRenderContext rc, ISrvBindable src, IRtvBindable dest)
    {
        if (mergeCopy == null || src == null || dest == null)
            return;
        var destSize = dest.Size;
        if (destSize.X <= 0 || destSize.Y <= 0)
            return;
        rc.SetViewport(0f, 0f, destSize.X, destSize.Y, 0f, 1f);
        rc.SetBlendState(MyBlendStateManager.BlendReplace);
        UnbindBus(rc);
        BindSingleRtv(rc, dest);
        rc.VertexShader.Set(vertexShader);
        rc.PixelShader.Set(mergeCopy);
        rc.PixelShader.SetSampler(0, MySamplerStateManager.Linear);
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
        var hdr = IsHdrGBufferSlot(prog.Slot);
        if (hdr)
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

        BindPackSrvs(rc, prog.Id, compute: false);
        BindPointLights(rc, compute: false, hdr);
    }

    static void BindPackSrvs(MyRenderContext rc, string programId, bool compute)
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
            var srv = buf != null && buf.IsAvailable ? buf.Srv as ISrvBindable : null;
            if (compute)
                rc.ComputeShader.SetSrv(bind.Slot, srv);
            else
                rc.PixelShader.SetSrv(bind.Slot, srv);
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
        rc.PixelShader.SetSrv(10, null);
        rc.PixelShader.SetSrv(11, null);
        rc.PixelShader.SetSampler(2, null);
        rc.AllShaderStages.SetConstantBuffer(0, null);
        rc.AllShaderStages.SetConstantBuffer(6, null);
        rc.AllShaderStages.SetConstantBuffer(7, null);
        UnbindCompute(rc);
    }

    static void BindPointLights(MyRenderContext rc, bool compute, bool hdr)
    {
        if (!hdr)
            return;
        var lights = PointLightCatalog.SrvOrDummy(BufferCatalog.PointLights);
        var tiles = PointLightCatalog.SrvOrDummy(BufferCatalog.TileIndices);
        var frame = MyCommon.FrameConstants;
        if (compute)
        {
            rc.ComputeShader.SetSrv(PointLightsSlot, lights);
            rc.ComputeShader.SetSrv(TileIndicesSlot, tiles);
            if (frame != null)
                rc.ComputeShader.SetConstantBuffer(0, frame);
            return;
        }

        rc.PixelShader.SetSrv(PointLightsSlot, lights);
        rc.PixelShader.SetSrv(TileIndicesSlot, tiles);
        if (frame != null)
            rc.AllShaderStages.SetConstantBuffer(0, frame);
        else
            Warn("FrameConstants null on HDR fullscreen — tiled lights will read tiles_x=0");
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

    static void WriteExtras(MyRenderContext rc, bool outputRes, Vector2I? passSize = null,
        Vector2I? sceneSize = null)
    {
        if (extrasCb == null)
            return;
        FrameTemporal.EnsureSnapshot();
        var scene = sceneSize ?? SceneSize(outputRes);
        var pass = passSize ?? scene;
        var w = pass.X > 0 ? pass.X : 1;
        var h = pass.Y > 0 ? pass.Y : 1;
        var sw = scene.X > 0 ? scene.X : 1;
        var sh = scene.Y > 0 ? scene.Y : 1;
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
            CameraToWorldPad = Vector2.Zero,
            SceneSize = new Vector2(sw, sh),
            InvSceneSize = new Vector2(1f / sw, 1f / sh),
            SunColor = FrameTemporal.SunColor,
            SunDiffuse = FrameTemporal.SunDiffuse,
            SunToward = FrameTemporal.SunToward,
            SkyLuma = FrameTemporal.SkyLuma,
            SkyAmbient = FrameTemporal.SkyAmbient,
            SkyAmbientPad = 0f
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
                    ShaderWarmup.Add(jobs, mergePath, MyShaderProfile.ps_5_0, "Anomaly.Fullscreen.Merge3",
                        new ShaderMacro("MERGE_MODE", "3"));
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
                if (prog.Compose == FullscreenCompose.Replace)
                    ShaderWarmup.Add(jobs, prog.File, MyShaderProfile.cs_5_0,
                        "Anomaly.Fullscreen.CS." + prog.Id,
                        new ShaderMacro("ANOMALY_FULLSCREEN", "1"),
                        new ShaderMacro("ANOMALY_FULLSCREEN_SLOT_" + prog.Slot.ToString().ToUpperInvariant(), "1"),
                        new ShaderMacro("ANOMALY_FULLSCREEN_COMPUTE", "1"));
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
        var subBc = CompileMerge(mergePath, 3);
        if (vsBc == null || vsBc.Length == 0 || copyBc == null || copyBc.Length == 0 ||
            addBc == null || addBc.Length == 0 || overBc == null || overBc.Length == 0 ||
            subBc == null || subBc.Length == 0)
        {
            Fail("fullscreen helper compile failed");
            return false;
        }

        var device = MyRender11.DeviceInstance;
        vertexShader = new VertexShader(device, vsBc) { DebugName = "Anomaly.Fullscreen.VS" };
        mergeCopy = new PixelShader(device, copyBc) { DebugName = "Anomaly.Fullscreen.MergeCopy" };
        mergeAdd = new PixelShader(device, addBc) { DebugName = "Anomaly.Fullscreen.MergeAdd" };
        mergeOver = new PixelShader(device, overBc) { DebugName = "Anomaly.Fullscreen.MergeOver" };
        mergeSub = new PixelShader(device, subBc) { DebugName = "Anomaly.Fullscreen.MergeSub" };
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
            MyShaderProfile.ps_5_0, "Anomaly.Fullscreen.Merge" + mode, invalidateCache: true);
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
                MarkCompileFailed(prog, "empty bytecode");
                return false;
            }

            prog.Shader = new PixelShader(MyRender11.DeviceInstance, bc)
            {
                DebugName = "Anomaly.Fullscreen." + prog.Id
            };
            if (prog.Compose == FullscreenCompose.Replace)
                TryCompileCompute(prog, macros);
            return true;
        }
        catch (Exception e)
        {
            MarkCompileFailed(prog, e.Message);
            return false;
        }
    }

    static void TryCompileCompute(Program prog, ShaderMacro[] psMacros)
    {
        if (prog.ComputeTried)
            return;
        prog.ComputeTried = true;
        var macros = new ShaderMacro[psMacros.Length + 1];
        Array.Copy(psMacros, macros, psMacros.Length);
        macros[psMacros.Length] = new ShaderMacro("ANOMALY_FULLSCREEN_COMPUTE", "1");
        try
        {
            var bc = MyShaderCompiler.Compile(prog.File, macros, MyShaderProfile.cs_5_0,
                "Anomaly.Fullscreen.CS." + prog.Id, invalidateCache: false);
            if (bc == null || bc.Length == 0)
                return;
            prog.Compute = new ComputeShader(MyRender11.DeviceInstance, bc)
            {
                DebugName = "Anomaly.Fullscreen.CS." + prog.Id
            };
        }
        catch (Exception e)
        {
            Warn("compute compile skipped id=" + prog.Id + ": " + e.Message);
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

    static float SnapScale(float scale)
    {
        if (scale <= 0.375f)
            return 0.25f;
        if (scale <= 0.75f)
            return 0.5f;
        return 1f;
    }

    static Vector2I SceneSize(bool outputRes)
    {
        return outputRes ? MyRender11.ViewportResolution : MyRender11.ResolutionI;
    }

    static Vector2I SceneSize(OwnedPassSlot slot)
    {
        return SceneSize(slot == OwnedPassSlot.AfterUpscale);
    }

    static Vector2I ScaledSize(Vector2I scene, float scale)
    {
        var snapped = SnapScale(scale);
        var div = snapped <= 0.25f ? 4 : snapped <= 0.5f ? 2 : 1;
        return new Vector2I(Math.Max(1, scene.X / div), Math.Max(1, scene.Y / div));
    }

    static bool UsesScale(Program prog)
    {
        return prog != null && prog.Compose != FullscreenCompose.Replace && SnapScale(prog.Scale) < 1f;
    }

    static Vector2I IsolatedSize(Program prog, Vector2I scene)
    {
        return UsesScale(prog) ? ScaledSize(scene, prog.Scale) : scene;
    }

    static bool UsesScaledScratch(Program prog, Vector2I pass, Vector2I scene)
    {
        return UsesScale(prog) && (pass.X != scene.X || pass.Y != scene.Y);
    }

    static IRtvTexture EnsureScaledScratch(Program prog, Vector2I size)
    {
        if (prog.ScaledRt != null && prog.ScaledW == size.X && prog.ScaledH == size.Y)
            return prog.ScaledRt;
        DisposeScaled(prog);
        prog.ScaledRt = CreateScratch("Anomaly.Fullscreen.Scale." + prog.Id, size.X, size.Y);
        prog.ScaledW = size.X;
        prog.ScaledH = size.Y;
        return prog.ScaledRt;
    }

    static void DisposeScaled(Program prog)
    {
        if (prog?.ScaledRt == null)
            return;
        var tex = prog.ScaledRt;
        MyManagers.RwTextures.DisposeTex(ref tex);
        prog.ScaledRt = null;
        prog.ScaledW = 0;
        prog.ScaledH = 0;
    }

    static void DisposeScaledUnlocked()
    {
        for (var i = 0; i < Programs.Count; i++)
            DisposeScaled(Programs[i]);
    }

    static IUavTexture CreateScratch(string name, int w, int h)
    {
        return MyManagers.RwTextures.CreateUav(name, w, h, Format.R16G16B16A16_Float);
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

    static void DisposePair(ref IUavTexture a, ref IUavTexture b)
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
        mergeSub?.Dispose();
        isolatedSubBlend?.Dispose();
        vertexShader = null;
        mergeCopy = null;
        mergeAdd = null;
        mergeOver = null;
        mergeSub = null;
        isolatedSubBlend = null;
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
            Programs[i].Compute?.Dispose();
            Programs[i].Compute = null;
            Programs[i].ComputeTried = false;
            DisposeScaled(Programs[i]);
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
            var prog = Programs[i];
            if (!string.IsNullOrEmpty(prog.LastError))
            {
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(prog.Id).Append("!compile");
                continue;
            }

            if (!prog.Enabled)
                continue;
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(prog.Slot).Append('/').Append(prog.Compose).Append(':')
                .Append(prog.Id);
            if (UsesScale(prog))
                sb.Append('@').Append(SnapScale(prog.Scale) <= 0.25f ? "1/4" : "1/2");
            if (!prog.PackEnabled)
                sb.Append('-');
        }

        return sb.Length == 0 ? "none" : sb.ToString();
    }

    static string DescribeUnlocked(Program prog)
    {
        if (!string.IsNullOrEmpty(prog.LastError))
            return "compile-failed: " + prog.LastError;
        if (prog.Shader == null)
            return "pending";
        var scale = UsesScale(prog)
            ? (SnapScale(prog.Scale) <= 0.25f ? " @1/4" : " @1/2")
            : "";
        var slot = prog.Slot + "/" + prog.Compose + scale;
        if (!prog.PackEnabled)
            return "off (pack) " + slot + LastDrawSuffix(prog);
        if (!prog.Enabled)
            return "off (conflict) " + slot + LastDrawSuffix(prog);
        return "live " + slot + LastDrawSuffix(prog);
    }

    static string LastDrawSuffix(Program prog)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(prog.LastMerge))
            sb.Append(" merge=").Append(prog.LastMerge);
        if (!string.IsNullOrEmpty(prog.LastDest))
            sb.Append(" dest=").Append(prog.LastDest);
        if (!string.IsNullOrEmpty(prog.LastSkip))
            sb.Append(" skip=").Append(prog.LastSkip);
        return sb.ToString();
    }

    static void MarkCompileFailed(Program prog, string message)
    {
        prog.LastError = FirstLine(message);
        prog.Enabled = false;
        Warn("compile failed pack=" + prog.PackId + " id=" + prog.Id + ": " + prog.LastError);
        lock (Gate)
            statusLine = FormatStatusUnlocked();
    }

    static string FirstLine(string message)
    {
        if (string.IsNullOrEmpty(message))
            return "unknown";
        var n = message.IndexOf('\r');
        if (n < 0)
            n = message.IndexOf('\n');
        var line = n < 0 ? message : message.Substring(0, n);
        line = line.Trim();
        return line.Length <= 220 ? line : line.Substring(0, 217) + "...";
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
        public string LastError;
        public string LastSkip;
        public string LastMerge;
        public string LastDest;
        public int LastDrawTick;
        public bool WarnedMotion;
        public PixelShader Shader;
        public ComputeShader Compute;
        public bool ComputeTried;
        public PublishedBuffer Published;
        public float Scale = 1f;
        public IUavTexture ScaledRt;
        public int ScaledW;
        public int ScaledH;
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
    public float Scale = 1f;
}

internal sealed class SrvBind
{
    public string CatalogName;
    public int Slot = -1;
}
