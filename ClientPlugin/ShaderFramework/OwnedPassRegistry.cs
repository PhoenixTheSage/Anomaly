using System;
using System.Collections.Generic;
using System.Text;
using ClientPlugin.Buffers;
using ClientPlugin.ShaderFramework;
using VRage.Render11.RenderContext;
using VRage.Utils;
using VRageRender;

namespace ClientPlugin.Shaders;

/// <summary>
/// Well-known type for visual plugins. Resolve by name:
/// <c>ClientPlugin.Shaders.OwnedPassRegistry</c> — do not take a
/// compile-time reference. Register a draw at a named
/// <see cref="OwnedPassSlot"/>; Anomaly owns the Harmony prefixes and the
/// unbind (Rich HUD). Data-driven <see cref="FullscreenPassRegistry"/>
/// programs run first, then C# callbacks. The unique upscale consumer
/// calls <see cref="ClaimUpscale"/> then <see cref="NotifyUpscaleComplete"/>
/// with the dest so AfterUpscale reads catalog <c>upscaledColor</c>.
/// </summary>
public static class OwnedPassRegistry
{
    static readonly object Gate = new();
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
    static string colorStatusLine = "upscale=none upscaledColor=none display=no";

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
                return string.IsNullOrEmpty(colorStatusLine) ? "upscale=none upscaledColor=none display=no" : colorStatusLine;
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
    /// an <see cref="OwnedPassContext"/> boxed as object.
    /// </summary>
    public static void Register(string id, string slot, int priority, int temporalPolicy, Action<object> draw)
    {
        if (draw == null)
            return;
        if (!TryParseSlot(slot, out var parsed))
        {
            Warn("Register ignored unknown slot '" + slot + "' for '" + id + "'");
            return;
        }

        Register(id, parsed, priority, (TemporalPolicy)temporalPolicy, ctx => draw(ctx));
    }

    public static void Register(string id, OwnedPassSlot slot, int priority, TemporalPolicy policy,
        Action<OwnedPassContext> draw)
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
                Draw = draw
            });
            Passes.Sort(Compare);
            RefreshStatusUnlocked();
        }

        DebugLog.Write("OwnedPassRegistry register " + id + " " + slot + " pri=" + priority +
                       " policy=" + policy);
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

    internal static void Run(OwnedPassSlot slot, MyRenderContext rc, bool? outputResolution = null,
        object dest = null)
    {
        if (rc == null || !rc.IsInitialized)
            return;

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
                return;
            snapshot = new Registration[n];
            var w = 0;
            for (var i = 0; i < Passes.Count; i++)
            {
                if (Passes[i].Slot != slot)
                    continue;
                snapshot[w++] = Passes[i];
            }
        }

        FrameTemporal.EnsureSnapshot();
        var output = outputResolution ?? (slot == OwnedPassSlot.AfterUpscale);
        try
        {
            FullscreenPassRegistry.Run(slot, rc, dest, output);
        }
        catch (Exception e)
        {
            Warn("fullscreen at " + slot + " threw " + e.GetType().Name + ": " + e.Message);
        }

        for (var i = 0; i < snapshot.Length; i++)
        {
            var reg = snapshot[i];
            try
            {
                if ((reg.Policy & TemporalPolicy.Reactive) != 0)
                    TemporalParticipation.EnsureReactive();
                var ctx = new OwnedPassContext(slot, rc, reg.Policy, output);
                reg.Draw(ctx);
            }
            catch (Exception e)
            {
                Warn("pass '" + reg.Id + "' at " + slot + " threw " + e.GetType().Name + ": " + e.Message);
            }
        }

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
        return sb.ToString();
    }

    static void Warn(string message)
    {
        MyLog.Default.WriteLine("Anomaly owned pass: " + message);
        DebugLog.Write("OwnedPassRegistry WARN " + message);
    }

    sealed class Registration
    {
        public string Id;
        public OwnedPassSlot Slot;
        public int Priority;
        public TemporalPolicy Policy;
        public Action<OwnedPassContext> Draw;
    }
}
