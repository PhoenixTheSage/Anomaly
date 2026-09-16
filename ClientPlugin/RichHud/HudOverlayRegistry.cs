using System;
using System.Collections.Generic;
using System.Text;
using RichHudFramework.Client;
using RichHudFramework.UI;
using RichHudFramework.UI.Client;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.RichHud;

/// <summary>
/// Well-known type for pack and consumer plugins. Resolve by name:
/// <c>ClientPlugin.RichHud.HudOverlayRegistry</c> — do not take a
/// compile-time reference to Anomaly. Call <see cref="Register"/> from
/// <c>LoadAssets</c> or <c>Init</c>. Anomaly draws a stacked corner label
/// when Rich HUD Master is registered. Getters run on the HUD draw thread:
/// return a cached string. Do not serialize or write a file from the getter.
/// </summary>
public static class HudOverlayRegistry
{
    static readonly object Gate = new();
    static readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);
    static readonly List<Entry> Order = new();
    static readonly StringBuilder Scratch = new();
    static OverlayPanel host;
    static bool mounted;

    public static string LastError { get; private set; }

    public static int Count
    {
        get { lock (Gate) return Order.Count; }
    }

    public static string StatusLine
    {
        get
        {
            lock (Gate)
            {
                if (Order.Count == 0)
                    return "overlays=0";
                var names = new List<string>(Order.Count);
                foreach (var entry in Order)
                    names.Add(entry.Id);
                return "overlays=" + Order.Count + " (" + string.Join(", ", names) + ")";
            }
        }
    }

    /// <summary>
    /// Registers or replaces the overlay line for <paramref name="id"/>.
    /// Empty ids fail closed. Same id is idempotent (updates the getter).
    /// A null or empty getter result hides that line for the frame.
    /// The host panel stays in the layout tree so a later non-empty getter can show again.
    /// </summary>
    public static bool Register(string id, Func<string> get)
    {
        if (string.IsNullOrWhiteSpace(id) || get == null)
        {
            LastError = get == null ? "missing getter" : "empty overlay id";
            return false;
        }

        id = id.Trim();
        lock (Gate)
        {
            if (Entries.TryGetValue(id, out var existing))
            {
                existing.Get = get;
                LastError = null;
                return true;
            }

            var entry = new Entry { Id = id, Get = get };
            Entries.Add(id, entry);
            Order.Add(entry);
            LastError = null;
            return true;
        }
    }

    public static bool Unregister(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        lock (Gate)
        {
            if (!Entries.TryGetValue(id.Trim(), out var entry))
                return false;
            Entries.Remove(entry.Id);
            Order.Remove(entry);
            return true;
        }
    }

    internal static void Mount()
    {
        if (!RichHudClient.Registered)
            return;

        lock (Gate)
        {
            if (mounted)
                return;

            try
            {
                HudMain.Init();
                host = new OverlayPanel();
                host.Register(HudMain.HighDpiRoot);
                mounted = true;
                LastError = null;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                host = null;
                mounted = false;
                MyLog.Default.WriteLine("Anomaly Rich HUD overlay mount failed: " + e.Message);
                DebugLog.Write("HudOverlayRegistry mount failed: " + e);
            }
        }
    }

    internal static void Unmount()
    {
        lock (Gate)
        {
            try
            {
                if (host != null)
                {
                    host.Visible = false;
                    host.Unregister();
                }
            }
            catch
            {
                // Master already tore the HUD tree down.
            }

            host = null;
            mounted = false;
        }
    }

    static string ComposeUnlocked()
    {
        Scratch.Clear();
        foreach (var entry in Order)
        {
            string line;
            try
            {
                line = entry.Get?.Invoke();
            }
            catch (Exception e)
            {
                DebugLog.Write("HudOverlayRegistry " + entry.Id + ": " + e.GetType().Name);
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (Scratch.Length > 0)
                Scratch.Append('\n');
            Scratch.Append(line.Trim());
        }

        return Scratch.ToString();
    }

    sealed class Entry
    {
        public string Id;
        public Func<string> Get;
    }

    sealed class OverlayPanel : HudElementBase
    {
        static readonly GlyphFormat Format = new GlyphFormat(
            new Color(230, 236, 242, 230),
            TextAlignment.Right,
            0.85f);

        readonly Label label;

        public OverlayPanel() : base(null)
        {
            ParentAlignment = ParentAlignments.InnerTopRight;
            Offset = new Vector2(-18f, -18f);
            ZOffset = 8;
            UseCursor = false;
            ShareCursor = false;
            label = new Label(this)
            {
                ParentAlignment = ParentAlignments.InnerTopRight,
                AutoResize = true,
                BuilderMode = TextBuilderModes.Lined,
                Format = Format,
                UseCursor = false,
                ShareCursor = false,
            };
        }

        protected override void Layout()
        {
            string text;
            lock (Gate)
                text = ComposeUnlocked();

            var show = !string.IsNullOrEmpty(text);
            Visible = true;
            label.Visible = show;
            if (show)
                label.Text = text;
            else
                label.Text = "";
            base.Layout();
        }
    }
}
