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
/// programs run first, then C# callbacks. SE-DLSS (or any upscaler) calls
/// <see cref="NotifyUpscaleComplete"/> after evaluate.
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
    static string statusLine = "none";

    public static string StatusLine
    {
        get
        {
            lock (Gate)
                return string.IsNullOrEmpty(statusLine) ? "none" : statusLine;
        }
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
            statusLine = FormatStatusUnlocked();
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

            statusLine = FormatStatusUnlocked();
        }
    }

    /// <summary>
    /// Upscale consumer (SE-DLSS) calls this after evaluate, at output
    /// resolution. Runs <see cref="OwnedPassSlot.AfterUpscale"/> once per frame.
    /// Safe to call when no passes are registered.
    /// </summary>
    public static void NotifyUpscaleComplete()
    {
        NotifyUpscaleComplete(MyRender11.RC);
    }

    public static void NotifyUpscaleComplete(object renderContext)
    {
        var rc = renderContext as MyRenderContext ?? MyRender11.RC;
        lock (Gate)
        {
            if (upscaleNotified)
                return;
            upscaleNotified = true;
        }

        Run(OwnedPassSlot.AfterUpscale, rc, outputResolution: true);
    }

    internal static void BeginFrame()
    {
        lock (Gate)
            upscaleNotified = false;
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
        BufferCatalogLifetime.NotifyResolutionChanged();
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
            statusLine = FormatStatusUnlocked();
        }
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
            }
        }

        return sb.Length == 0 ? "none" : sb.ToString();
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
