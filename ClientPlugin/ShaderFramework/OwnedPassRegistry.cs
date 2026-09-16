using System;
using System.Collections.Generic;
using System.Text;
using ClientPlugin.Buffers;
using ClientPlugin.ShaderFramework;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageRender;

namespace ClientPlugin.Shaders;

/// <summary>
/// Well-known type for visual plugins. Resolve by name:
/// <c>ClientPlugin.Shaders.OwnedPassRegistry</c> — do not take a
/// compile-time reference. Register a draw at a named
/// <see cref="OwnedPassSlot"/>; Anomaly owns the Harmony prefixes and the
/// unbind (Rich HUD). C# callbacks with <see cref="OwnedPassPhase.BeforeFullscreen"/>
/// run first, then data-driven <see cref="FullscreenPassRegistry"/> programs,
/// then AfterFullscreen C# (the default). The unique upscale consumer
/// calls <see cref="ClaimUpscale"/> then <see cref="NotifyUpscaleComplete"/>
/// with the dest so AfterUpscale reads catalog <c>upscaledColor</c>.
/// </summary>
public static class OwnedPassRegistry
{
    static readonly object Gate = new();
    static readonly HashSet<string> LoggedWarnings = new();
    static readonly List<Registration> Passes = new();
    static readonly OwnedPassSlot[] SlotOrder =
    {
        OwnedPassSlot.AfterLighting,
        OwnedPassSlot.AfterAtmosphere,
        OwnedPassSlot.AfterTransparent,
        OwnedPassSlot.BeforeTonemap,
        OwnedPassSlot.AfterTonemap,
        OwnedPassSlot.AfterUpscale
    };

    static bool upscaleNotified;
    static string upscaleConsumerId;
    static object notifiedColor;
    static string statusLine = "none";
    static string colorStatusLine = "upscale=none upscaledColor=none display=no dest=none";

    public static string StatusLine
    {
        get
        {
            lock (Gate)
                return string.IsNullOrEmpty(statusLine) ? "none" : statusLine;
        }
    }

    /// <summary>
    /// Unique upscale consumer, published dest, and whether an AfterUpscale
    /// <see cref="TemporalPolicy.Display"/> tenant is registered.
    /// </summary>
    public static string ColorStatusLine
    {
        get
        {
            lock (Gate)
                return string.IsNullOrEmpty(colorStatusLine)
                    ? "upscale=none upscaledColor=none display=no dest=none"
                    : colorStatusLine;
        }
    }

    /// <summary>
    /// True when an AfterUpscale C# pass or fullscreen program set
    /// <see cref="TemporalPolicy.Display"/>. Upscalers should evaluate
    /// pre-tonemap HDR and skip Keen SDR tonemap.
    /// </summary>
    public static bool HasDisplayTenant
    {
        get
        {
            lock (Gate)
            {
                for (var i = 0; i < Passes.Count; i++)
                {
                    if (Passes[i].Slot == OwnedPassSlot.AfterUpscale &&
                        (Passes[i].Policy & TemporalPolicy.Display) != 0)
                        return true;
                }
            }

            return FullscreenPassRegistry.HasPolicy(OwnedPassSlot.AfterUpscale, TemporalPolicy.Display);
        }
    }

    /// <summary>True after <see cref="ClaimUpscale"/>; HdrRender-class tenants skip stealing <c>MyToneMapping.Run</c>.</summary>
    public static bool HasUpscaleConsumer
    {
        get
        {
            lock (Gate)
                return !string.IsNullOrEmpty(upscaleConsumerId);
        }
    }

    public static string UpscaleConsumerId
    {
        get
        {
            lock (Gate)
                return upscaleConsumerId ?? "";
        }
    }

    /// <summary>Catalog <c>upscaledColor</c> is live this frame (notify passed a dest).</summary>
    public static bool HasUpscaledColor
    {
        get
        {
            var buf = BufferCatalog.Active(BufferCatalog.UpscaledColor);
            return buf != null && buf.IsAvailable;
        }
    }

    /// <summary>True after the unique consumer notified (or the DrawGameScene fallback ran).</summary>
    public static bool WasUpscaleNotified
    {
        get
        {
            lock (Gate)
                return upscaleNotified;
        }
    }

    /// <summary>Raw dest from notify this frame, or null. Keen <c>ISrvBindable</c>.</summary>
    internal static object NotifiedColor
    {
        get
        {
            lock (Gate)
                return notifiedColor;
        }
    }

    /// <summary>
    /// Unique upscale consumer (SE-DLSS) claims the slot at init. A second
    /// different id fails closed. Display tenants query
    /// <see cref="HasUpscaleConsumer"/> and yield <c>MyToneMapping.Run</c>.
    /// </summary>
    public static bool ClaimUpscale(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;
        id = id.Trim();
        lock (Gate)
        {
            if (!string.IsNullOrEmpty(upscaleConsumerId) &&
                !string.Equals(upscaleConsumerId, id, StringComparison.OrdinalIgnoreCase))
            {
                Warn("ClaimUpscale ignored '" + id + "' — '" + upscaleConsumerId + "' already claimed");
                return false;
            }

            upscaleConsumerId = id;
            RefreshStatusUnlocked();
        }

        DebugLog.Write("OwnedPassRegistry ClaimUpscale " + id);
        return true;
    }

    public static void ReleaseUpscale(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;
        lock (Gate)
        {
            if (!string.Equals(upscaleConsumerId, id, StringComparison.OrdinalIgnoreCase))
                return;
            upscaleConsumerId = null;
            RefreshStatusUnlocked();
        }

        DebugLog.Write("OwnedPassRegistry ReleaseUpscale " + id);
    }

    /// <summary>
    /// Reflection-friendly: any C# or fullscreen tenant on <paramref name="slot"/>.
    /// </summary>
    public static bool HasSlot(string slot)
    {
        if (!TryParseSlot(slot, out var parsed))
            return false;
        if (FullscreenPassRegistry.HasSlot(parsed))
            return true;
        lock (Gate)
        {
            for (var i = 0; i < Passes.Count; i++)
            {
                if (Passes[i].Slot == parsed)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reflection-friendly register. <paramref name="slot"/> is an
    /// <see cref="OwnedPassSlot"/> name. <paramref name="temporalPolicy"/> is
    /// <see cref="TemporalPolicy"/> flags. <paramref name="draw"/> receives
    /// an <see cref="OwnedPassContext"/> boxed as object. Runs after
    /// data-driven fullscreen programs.
    /// </summary>
    public static void Register(string id, string slot, int priority, int temporalPolicy, Action<object> draw)
    {
        Register(id, slot, priority, temporalPolicy, draw, (int)OwnedPassPhase.AfterFullscreen);
    }

    /// <summary>
    /// Reflection-friendly register with <see cref="OwnedPassPhase"/>.
    /// <paramref name="phase"/> is <see cref="OwnedPassPhase"/> as int
    /// (<c>0</c> = BeforeFullscreen, <c>1</c> = AfterFullscreen).
    /// </summary>
    public static void Register(string id, string slot, int priority, int temporalPolicy, Action<object> draw,
        int phase)
    {
        if (draw == null)
            return;
        if (!TryParseSlot(slot, out var parsed))
        {
            Warn("Register ignored unknown slot '" + slot + "' for '" + id + "'");
            return;
        }

        var parsedPhase = OwnedPassPhase.AfterFullscreen;
        if (Enum.IsDefined(typeof(OwnedPassPhase), phase))
            parsedPhase = (OwnedPassPhase)phase;
        else
            Warn("Register ignored unknown phase " + phase + " for '" + id + "' — AfterFullscreen");

        Register(id, parsed, priority, (TemporalPolicy)temporalPolicy, ctx => draw(ctx), parsedPhase);
    }

    public static void Register(string id, OwnedPassSlot slot, int priority, TemporalPolicy policy,
        Action<OwnedPassContext> draw)
    {
        Register(id, slot, priority, policy, draw, OwnedPassPhase.AfterFullscreen);
    }

    public static void Register(string id, OwnedPassSlot slot, int priority, TemporalPolicy policy,
        Action<OwnedPassContext> draw, OwnedPassPhase phase)
    {
        if (string.IsNullOrWhiteSpace(id) || draw == null)
            return;
        lock (Gate)
        {
            for (var i = Passes.Count - 1; i >= 0; i--)
            {
                if (string.Equals(Passes[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    Passes.RemoveAt(i);
            }

            Passes.Add(new Registration
            {
                Id = id.Trim(),
                Slot = slot,
                Priority = priority,
                Policy = policy,
                Phase = phase,
                Draw = draw
            });
            Passes.Sort(Compare);
            RefreshStatusUnlocked();
        }

        DebugLog.Write("OwnedPassRegistry register " + id + " " + slot + " pri=" + priority +
                       " policy=" + policy + " phase=" + phase);
    }

    public static void Unregister(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;
        lock (Gate)
        {
            for (var i = Passes.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(Passes[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    continue;
                Passes.RemoveAt(i);
            }

            RefreshStatusUnlocked();
        }
    }

    /// <summary>
    /// Unique upscale consumer calls this after evaluate, at output
    /// resolution. Runs <see cref="OwnedPassSlot.AfterUpscale"/> once per frame.
    /// Safe to call when no passes are registered. Prefer the two-argument
    /// overload so AfterUpscale reads the dest, not internal <c>LBuffer</c>.
    /// </summary>
    public static void NotifyUpscaleComplete()
    {
        NotifyUpscaleComplete(MyRender11.RC, null);
    }

    /// <summary>
    /// <paramref name="renderContextOrColor"/> is a <c>MyRenderContext</c> or
    /// the upscaled dest (Keen <c>ISrvBindable</c> / <c>ICustomTexture</c>).
    /// </summary>
    public static void NotifyUpscaleComplete(object renderContextOrColor)
    {
        var rc = renderContextOrColor as MyRenderContext;
        if (rc != null)
            NotifyUpscaleComplete(rc, null);
        else
            NotifyUpscaleComplete(MyRender11.RC, renderContextOrColor);
    }

    /// <summary>
    /// Publishes <paramref name="color"/> as catalog <c>upscaledColor</c>,
    /// binds it as fullscreen t0 / <see cref="OwnedPassContext.SceneColor"/>,
    /// then runs AfterUpscale at <c>ViewportResolution</c>.
    /// </summary>
    public static void NotifyUpscaleComplete(object renderContext, object color)
    {
        var rc = renderContext as MyRenderContext ?? MyRender11.RC;
        lock (Gate)
        {
            if (upscaleNotified)
                return;
            upscaleNotified = true;
            notifiedColor = color;
        }

        var size = MyRender11.ViewportResolution;
        BufferCatalog.PublishUpscaledColor(color, size.X, size.Y);
        lock (Gate)
            RefreshStatusUnlocked();
        RenderTrace.Note("NotifyUpscale");
        Run(OwnedPassSlot.AfterUpscale, rc, dest: color, outputResolution: true);
    }

    internal static void BeginFrame()
    {
        lock (Gate)
        {
            upscaleNotified = false;
            notifiedColor = null;
        }

        BufferCatalog.ClearUpscaledColor();
        TonemapInputs.BeginFrame();
        OwnedBuffersPass.BeginFrame();
        lock (Gate)
            RefreshStatusUnlocked();
        FrameTemporal.BeginFrame();
        TemporalParticipation.BeginFrame();
    }

    internal static void RunFallbackAfterUpscale()
    {
        bool run;
        lock (Gate)
        {
            run = !upscaleNotified;
            upscaleNotified = true;
        }

        if (run)
            Run(OwnedPassSlot.AfterUpscale, MyRender11.RC, outputResolution: false);
    }

    /// <summary>
    /// <c>DrawGameScene</c> NREs on <c>borrowedCustomTexture.Linear</c> /
    /// <c>.SRgb</c> when <c>MyToneMapping.Run</c> returns null. Unique
    /// upscalers skip Keen after <see cref="NotifyUpscaleComplete"/> and
    /// often omit <c>__result</c>. Prefer the notified dest (already graded
    /// by AfterUpscale); otherwise borrow the same HDR wrap Display-without-
    /// upscale uses.
    /// </summary>
    internal static IBorrowedCustomTexture EnsureDrawSceneDest(IBorrowedCustomTexture dest)
    {
        if (dest != null)
            return DisplayDest.EnsureHdr(DisplayDest.TonemappedName, dest);

        var adopted = DisplayDest.Adopt(NotifiedColor);
        if (adopted != null)
        {
            RenderTrace.Note("AdoptDisplayDest notify");
            return adopted;
        }

        if (!HasDisplayTenant)
            return null;

        try
        {
            dest = DisplayDest.BorrowTonemapped();
            if (dest != null)
                RenderTrace.Note("AdoptDisplayDest borrow");
            return dest;
        }
        catch (Exception e)
        {
            Warn("display dest: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// Display tenant, no unique upscaler. Keen <c>Run</c> was skipped, so
    /// grade <c>LBuffer</c> into the borrowed dest Keen's <c>DrawGameScene</c>
    /// still copies (fp16 when the Keen dest is 8-bit UNORM). Marks notified
    /// so the DrawGameScene postfix does not run AfterUpscale a second time
    /// into <c>LBuffer</c>.
    /// </summary>
    internal static void CompleteDisplayWithoutUpscale(object dest)
    {
        if (dest == null)
            return;

        bool run;
        lock (Gate)
        {
            run = !upscaleNotified;
            if (run)
            {
                upscaleNotified = true;
                notifiedColor = dest;
            }
        }

        if (!run)
            return;

        // ResolutionI (internal). ViewportResolution is only for Notify after
        // an upscaler; grading native LBuffer at output size is wasted ALU.
        Run(OwnedPassSlot.AfterUpscale, MyRender11.RC, dest: dest, outputResolution: false,
            scene: MyGBuffer.Main?.LBuffer);
    }

    internal static void Run(OwnedPassSlot slot, MyRenderContext rc, bool? outputResolution = null,
        object dest = null, object scene = null)
    {
        if (rc == null || !rc.IsInitialized)
            return;
        if (IsHdrSlot(slot) && MainViewGate.IsOffscreen())
            return;

        var label = SlotLabel(slot);
        RenderTrace.Begin(label);
        try
        {
            using (DeferredContextGuard.Push(slot, rc))
                RunUnlocked(slot, rc, outputResolution, dest, scene);
        }
        catch (Exception e)
        {
            RenderTrace.DumpIfLost(label, e);
            if (RenderTrace.IsLostDevice(e))
                throw;
            Warn("slot " + label + " threw " + e.GetType().Name + ": " + e.Message);
        }
        finally
        {
            RenderTrace.End(label);
        }
    }

    static void RunUnlocked(OwnedPassSlot slot, MyRenderContext rc, bool? outputResolution, object dest,
        object scene = null)
    {
        var hasFullscreen = FullscreenPassRegistry.HasSlot(slot);
        Registration[] snapshot;
        lock (Gate)
        {
            var n = 0;
            for (var i = 0; i < Passes.Count; i++)
            {
                if (Passes[i].Slot == slot)
                    n++;
            }

            if (n == 0 && !hasFullscreen)
                snapshot = null;
            else
            {
                snapshot = new Registration[n];
                var w = 0;
                for (var i = 0; i < Passes.Count; i++)
                {
                    if (Passes[i].Slot != slot)
                        continue;
                    snapshot[w++] = Passes[i];
                }
            }
        }

        if (snapshot == null)
        {
            if (IsHdrSlot(slot))
            {
                OwnedBuffersPass.ExecuteLitMips(rc);
                if (slot == OwnedPassSlot.AfterLighting)
                    PointShadowPass.Execute(rc);
            }
            return;
        }

        FrameTemporal.EnsureSnapshot();
        var output = outputResolution ?? (slot == OwnedPassSlot.AfterUpscale);
        InvokeSnapshot(snapshot, OwnedPassPhase.BeforeFullscreen, slot, rc, output);
        if (IsHdrSlot(slot))
        {
            OwnedBuffersPass.ExecuteLitMips(rc);
            if (slot == OwnedPassSlot.AfterLighting)
                PointShadowPass.Execute(rc);
        }
        try
        {
            FullscreenPassRegistry.Run(slot, rc, dest, output, scene);
        }
        catch (Exception e)
        {
            RenderTrace.DumpIfLost(SlotLabel(slot), e);
            if (RenderTrace.IsLostDevice(e))
                throw;
            Warn("fullscreen at " + slot + " threw " + e.GetType().Name + ": " + e.Message);
        }

        InvokeSnapshot(snapshot, OwnedPassPhase.AfterFullscreen, slot, rc, output);

        try
        {
            rc.PixelShader.SetSrv(0, null);
            rc.SetRtvNull();
        }
        catch
        {
            // Best-effort unbind so a broken tenant cannot leak into Rich HUD.
        }
    }

    static string SlotLabel(OwnedPassSlot slot)
    {
        switch (slot)
        {
            case OwnedPassSlot.AfterLighting:
                return "AfterLighting";
            case OwnedPassSlot.AfterAtmosphere:
                return "AfterAtmosphere";
            case OwnedPassSlot.AfterTransparent:
                return "AfterTransparent";
            case OwnedPassSlot.BeforeTonemap:
                return "BeforeTonemap";
            case OwnedPassSlot.AfterTonemap:
                return "AfterTonemap";
            case OwnedPassSlot.AfterUpscale:
                return "AfterUpscale";
            default:
                return "OwnedPass";
        }
    }

    static bool IsHdrSlot(OwnedPassSlot slot)
    {
        return slot == OwnedPassSlot.AfterLighting ||
               slot == OwnedPassSlot.AfterAtmosphere ||
               slot == OwnedPassSlot.AfterTransparent ||
               slot == OwnedPassSlot.BeforeTonemap;
    }

    internal static void OnResolutionChanged()
    {
        TemporalParticipation.OnResolutionChanged();
        FullscreenPassRegistry.OnResolutionChanged();
        BufferCatalog.ClearUpscaledColor();
        BufferCatalogLifetime.NotifyResolutionChanged();
        lock (Gate)
            RefreshStatusUnlocked();
    }

    internal static void Release()
    {
        TemporalParticipation.Release();
        FrameTemporal.Release();
        FullscreenPassRegistry.Release();
        BufferCatalogLifetime.NotifyDeviceEnd();
        lock (Gate)
        {
            upscaleNotified = false;
            notifiedColor = null;
            RefreshStatusUnlocked();
        }

        BufferCatalog.ClearUpscaledColor();
    }

    internal static bool TryParseSlot(string name, out OwnedPassSlot slot)
    {
        slot = default;
        if (string.IsNullOrWhiteSpace(name))
            return false;
        return Enum.TryParse(name.Trim(), ignoreCase: true, out slot);
    }

    static int Compare(Registration a, Registration b)
    {
        var p = a.Priority.CompareTo(b.Priority);
        if (p != 0)
            return p;
        return string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
    }

    static void RefreshStatusUnlocked()
    {
        statusLine = FormatStatusUnlocked();
        colorStatusLine = FormatColorStatusUnlocked();
    }

    static string FormatStatusUnlocked()
    {
        if (Passes.Count == 0)
            return "none";
        var sb = new StringBuilder();
        for (var s = 0; s < SlotOrder.Length; s++)
        {
            var slot = SlotOrder[s];
            var first = true;
            for (var i = 0; i < Passes.Count; i++)
            {
                if (Passes[i].Slot != slot)
                    continue;
                if (sb.Length > 0 && first)
                    sb.Append(' ');
                if (first)
                {
                    sb.Append(slot).Append(':');
                    first = false;
                }
                else
                    sb.Append(',');
                sb.Append(Passes[i].Id);
                if (Passes[i].Phase == OwnedPassPhase.BeforeFullscreen)
                    sb.Append('<');
                if ((Passes[i].Policy & TemporalPolicy.Display) != 0)
                    sb.Append('*');
            }
        }

        return sb.Length == 0 ? "none" : sb.ToString();
    }

    static string FormatColorStatusUnlocked()
    {
        var sb = new StringBuilder();
        sb.Append("upscale=");
        sb.Append(string.IsNullOrEmpty(upscaleConsumerId) ? "none" : upscaleConsumerId);
        sb.Append(" upscaledColor=");
        var buf = BufferCatalog.Active(BufferCatalog.UpscaledColor);
        if (buf != null && buf.IsAvailable)
            sb.Append(buf.Width).Append('x').Append(buf.Height);
        else
            sb.Append("none");
        sb.Append(" display=");
        var display = false;
        for (var i = 0; i < Passes.Count; i++)
        {
            if (Passes[i].Slot == OwnedPassSlot.AfterUpscale &&
                (Passes[i].Policy & TemporalPolicy.Display) != 0)
            {
                display = true;
                break;
            }
        }

        if (!display)
            display = FullscreenPassRegistry.HasPolicy(OwnedPassSlot.AfterUpscale, TemporalPolicy.Display);
        sb.Append(display ? "yes" : "no");
        sb.Append(" dest=");
        sb.Append(DisplayDest.StatusLine);
        return sb.ToString();
    }

    static void InvokeSnapshot(Registration[] snapshot, OwnedPassPhase phase, OwnedPassSlot slot,
        MyRenderContext rc, bool output)
    {
        if (snapshot == null)
            return;
        for (var i = 0; i < snapshot.Length; i++)
        {
            var reg = snapshot[i];
            if (reg.Phase != phase)
                continue;
            RenderTrace.Begin(reg.Id);
            try
            {
                if ((reg.Policy & TemporalPolicy.Reactive) != 0)
                    TemporalParticipation.EnsureReactive(rc);
                var ctx = new OwnedPassContext(slot, rc, reg.Policy, output);
                reg.Draw(ctx);
            }
            catch (Exception e)
            {
                RenderTrace.DumpIfLost(reg.Id, e);
                if (RenderTrace.IsLostDevice(e))
                    throw;
                Warn("pass '" + reg.Id + "' at " + slot + " threw " + e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                RenderTrace.End(reg.Id);
            }
        }
    }

    static void Warn(string message)
    {
        lock (Gate)
        {
            if (!LoggedWarnings.Add(message))
                return;
        }

        MyLog.Default.WriteLine("Anomaly owned pass: " + message);
        DebugLog.Write("OwnedPassRegistry WARN " + message);
    }

    sealed class Registration
    {
        public string Id;
        public OwnedPassSlot Slot;
        public int Priority;
        public TemporalPolicy Policy;
        public OwnedPassPhase Phase;
        public Action<OwnedPassContext> Draw;
    }
}
