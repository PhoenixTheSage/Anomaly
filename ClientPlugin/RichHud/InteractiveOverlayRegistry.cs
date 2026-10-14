using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using RichHudFramework;
using RichHudFramework.UI;
using RichHudFramework.UI.Client;
using RichHudFramework.UI.Rendering;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using Sandbox.Graphics.GUI;
using VRage.Input;
using VRageMath;

namespace ClientPlugin.RichHud;

/// <summary>Optional interactive RHUD windows. Idempotent requests; lifecycle owned by Anomaly.
/// This is separate from the corner-text HudOverlayRegistry. Packs resolve it by type name.</summary>
public static class InteractiveOverlayRegistry
{
    public const int ApiVersion = 1;
    static readonly Dictionary<string, Entry> Entries = new(StringComparer.OrdinalIgnoreCase);
    static Controller controller;
    static readonly ConcurrentQueue<MyGuiScreenBase> ClosingGuards = new();
    /// <summary>Game-thread ownership of a focus-only GUI screen. RHUD still draws the window;
    /// the guard prevents raw Escape/Alt look and other non-blacklistable input reaching gameplay.</summary>
    internal static void Update()
    {
        while (ClosingGuards.TryDequeue(out var closing)) closing.CloseScreenNow();
        lock (Entries) foreach (var entry in Entries.Values)
        {
            if (entry.Visible && entry.Guard == null)
            {
                entry.Guard = new InputGuard(entry);
                MyGuiSandbox.AddScreen(entry.Guard);
            }
            else if (!entry.Visible && entry.Guard != null)
            {
                entry.Guard.CloseScreenNow(); entry.Guard = null;
            }
        }
    }
    public static IInteractiveOverlayWindow RequestWindow(string id, string title)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title)) return null;
        lock (Entries)
        {
            if (!Entries.TryGetValue(id, out var entry)) Entries.Add(id, entry = new Entry(title));
            return entry;
        }
    }
    public static void Unregister(string id)
    {
        lock (Entries)
            if (id != null && Entries.TryGetValue(id, out var entry)) { entry.Unmount(); Entries.Remove(id); }
    }
    internal static void Mount()
    {
        if (controller != null) return;
        controller = new Controller();
        controller.Register(HudMain.HighDpiRoot);
    }
    internal static void Unmount()
    {
        lock (Entries) foreach (var entry in Entries.Values) entry.Unmount();
        controller?.Unregister(); controller = null;
    }
    static void Safe(Action action)
    {
        try { action?.Invoke(); } catch (Exception e) { DebugLog.Write("Interactive overlay: " + e); }
    }
    sealed class Controller : HudElementBase
    {
        public Controller() : base(null) { UseCursor = false; ShareCursor = true; }
        protected override void HandleInput(Vector2 cursorPos)
        {
            lock (Entries) foreach (var entry in Entries.Values) Safe(entry.Tick);
        }
    }
    sealed class Control
    {
        public string Text;
        public Action Click;
        public Func<bool> Get, Enabled;
        public Action<bool> Set;
        public Func<float> GetFloat;
        public Action<float> SetFloat;
        public float Min, Max;
    }
    sealed class Entry : IInteractiveOverlayWindow
    {
        internal readonly object Gate = new();
        internal readonly List<Control> Controls = new();
        internal readonly string Title;
        internal Window Host;
        internal InputGuard Guard;
        internal float[] Lines = Array.Empty<float>(), Triangles = Array.Empty<float>(), LabelPositions = Array.Empty<float>();
        internal int LineCount, TriangleCount;
        internal string[] Labels = Array.Empty<string>(), Rows = Array.Empty<string>();
        internal long[] Ids = Array.Empty<long>();
        internal string Details = "Select an object", Status = "";
        internal Action<int, float, float, float> Input;
        internal Action<float, float> Resize;
        internal Action<long> Select;
        internal Action<bool> Visibility;
        bool desired, refresh = true, pendingOpen;
        int pendingSince;
        MyKeys key;
        bool ctrl, alt, shift;
        public Entry(string title) { Title = title; }
        public bool Available => RichHudSupport.Available && controller != null;
        public bool Visible { get; private set; }
        public void SetVisible(bool visible)
        {
            lock (Gate)
            {
                if (visible) { pendingOpen = true; pendingSince = Environment.TickCount; }
                else { desired = pendingOpen = false; }
            }
        }
        public void SetBinding(int value, bool control, bool alternate, bool shifted)
        { lock (Gate) { key = (MyKeys)value; ctrl = control; alt = alternate; shift = shifted; } }
        public void SetHandlers(Action<int, float, float, float> input, Action<float, float> resize, Action<long> select, Action<bool> visibility)
        { Input = input; Resize = resize; Select = select; Visibility = visibility; }
        public void Button(string text, Action click) { Controls.Add(new Control { Text = text, Click = click }); }
        public void Checkbox(string text, Func<bool> get, Action<bool> set, Func<bool> enabled)
        { Controls.Add(new Control { Text = text, Get = get, Set = set, Enabled = enabled }); }
        public void Slider(string text, float min, float max, Func<float> get, Action<float> set)
        { Controls.Add(new Control { Text = text, Min = min, Max = max, GetFloat = get, SetFloat = set }); }
        public void Refresh() { lock (Gate) refresh = true; }
        static void Copy<T>(T[] source, ref T[] destination, int count)
        {
            if (destination.Length < count) Array.Resize(ref destination, Math.Max(count, destination.Length * 2));
            Array.Copy(source, destination, count);
        }
        static void ValidateFinite(float[] buffer, int count)
        {
            for (int i = 0; i < count; i++)
                if (float.IsNaN(buffer[i]) || float.IsInfinity(buffer[i])) throw new ArgumentException("Overlay geometry must be finite.");
        }
        public void SubmitGeometry(float[] lines, int lineCount, float[] triangles, int triangleCount, string[] labels, float[] labelPositions)
        {
            if (lineCount < 0 || lineCount > 4096 || triangleCount < 0 || triangleCount > 1024 ||
                lines == null || triangles == null || lines.Length < lineCount * 8 || triangles.Length < triangleCount * 10 ||
                labels == null || labels.Length > 128 || labelPositions == null || labelPositions.Length < labels.Length * 2)
                throw new ArgumentException("Invalid overlay geometry or primitive budget.");
            ValidateFinite(lines, lineCount * 8); ValidateFinite(triangles, triangleCount * 10); ValidateFinite(labelPositions, labels.Length * 2);
            lock (Gate)
            {
                Copy(lines, ref Lines, lineCount * 8); Copy(triangles, ref Triangles, triangleCount * 10);
                Copy(labelPositions, ref LabelPositions, labels.Length * 2);
                Labels = (string[])labels.Clone(); LineCount = lineCount; TriangleCount = triangleCount;
            }
        }
        public void SubmitRows(long[] ids, string[] rows, string details, string status)
        {
            if (ids == null || rows == null || ids.Length != rows.Length) throw new ArgumentException("Row ids and text must match.");
            lock (Gate) { Ids = (long[])ids.Clone(); Rows = (string[])rows.Clone(); Details = details ?? ""; Status = status ?? ""; }
        }
        internal void Tick()
        {
            var input = MyInput.Static;
            var focus = MyScreenManager.GetScreenWithFocus();
            bool gameplay = InteractiveOverlayInputPolicy.CanUse(MySession.Static?.LocalHumanPlayer != null,
                focus is MyGuiScreenGamePlay, Guard != null && ReferenceEquals(focus, Guard),
                MyAPIGateway.Gui?.ChatEntryVisible ?? false, RichHudTerminal.Open, HudMain.InputMode == HudInputMode.Full, Visible);
            lock (Gate)
            {
                if (pendingOpen)
                {
                    if (RichHudTerminal.Open) RichHudTerminal.CloseMenu();
                    if (gameplay) { desired = true; pendingOpen = false; }
                    else if (unchecked(Environment.TickCount - pendingSince) > 10000) pendingOpen = false;
                }
                if (!gameplay) desired = false;
                else if (key != MyKeys.None && input.IsNewKeyPressed(key) && input.IsAnyCtrlKeyPressed() == ctrl &&
                    input.IsAnyAltKeyPressed() == alt && input.IsAnyShiftKeyPressed() == shift) desired = !desired;
                if (Visible && input.IsNewKeyPressed(MyKeys.Escape)) { desired = false; input.NegateEscapePress(); }
                if (desired && Host == null) { Host = new Window(this); Host.Register(HudMain.HighDpiRoot); }
                if (Visible != desired)
                {
                    Visible = desired;
                    // Never show a previous session's permissions or contact names on the first reopen frame.
                    LineCount = TriangleCount = 0; Labels = Array.Empty<string>(); Rows = Array.Empty<string>(); Ids = Array.Empty<long>();
                    Details = "Waiting for map snapshot"; Status = "";
                    if (Host != null) { Host.Visible = desired; if (desired) Host.GetWindowFocus(); }
                    Safe(() => Visibility?.Invoke(desired));
                }
                if (Visible)
                {
                    HudMain.EnableCursorTemp();
                    BindManager.RequestTempBlacklist(SeBlacklistModes.AllKeys | SeBlacklistModes.CameraRot);
                    if (refresh) { Host.RefreshControls(); refresh = false; }
                }
            }
        }
        internal void Unmount()
        {
            lock (Gate)
            {
                desired = Visible = pendingOpen = false;
                if (Guard != null) { ClosingGuards.Enqueue(Guard); Guard = null; }
                if (Host != null) { Host.Visible = false; Host.Unregister(); Host = null; }
                LineCount = TriangleCount = 0; Rows = Array.Empty<string>(); Ids = Array.Empty<long>(); Labels = Array.Empty<string>();
                refresh = true;
                Safe(() => Visibility?.Invoke(false));
            }
        }
    }
    sealed class InputGuard : MyGuiScreenBase
    {
        readonly Entry entry;
        public InputGuard(Entry entry) : base(Vector2.Zero, null, null)
        {
            this.entry = entry; CanHideOthers = false; CanBeHidden = false;
            CanHaveFocus = true; DrawMouseCursor = false; EnabledBackgroundFade = false;
        }
        public override string GetFriendlyName() => "AnomalyInteractiveOverlayInputGuard";
        public override bool Draw() => true;
        public override void HandleInput(bool receivedFocusInThisUpdate)
        {
            if (receivedFocusInThisUpdate && MyInput.Static.IsNewKeyPressed(MyKeys.Escape))
            {
                entry.SetVisible(false); MyInput.Static.NegateEscapePress();
            }
        }
    }
    sealed class Window : ResizableWindow
    {
        readonly Entry entry;
        readonly Viewport viewport;
        readonly HudElementBase panel;
        readonly MouseInputElement panelInput;
        readonly List<HudElementBase> controls = new();
        readonly List<Action> refreshers = new();
        readonly Label details, status;
        readonly List<LabelBoxButton> rows = new();
        readonly List<long> rowIds = new();
        float scroll;
        bool refreshing;
        public Window(Entry entry) : base(null)
        {
            this.entry = entry;
            Size = HudMain.ScreenDimHighDPI * .8f;
            MinimumSize = new Vector2(700, 460);
            HeaderText = entry.Title; BodyColor = new Color(12, 18, 26, 245); BorderColor = new Color(40, 65, 85, 255);
            Visible = false;
            var close = MakeButton(header, "Close", () => entry.SetVisible(false));
            close.Size = new Vector2(80, 28); close.ParentAlignment = ParentAlignments.InnerRight;
            viewport = new Viewport(body, entry);
            panel = new EmptyHudElement(body) { IsMasking = true, UseCursor = true };
            panelInput = new MouseInputElement(panel) { DimAlignment = DimAlignments.Size };
            foreach (var def in entry.Controls)
            {
                if (def.Click != null) controls.Add(MakeButton(panel, def.Text, def.Click));
                else if (def.Get != null)
                {
                    var check = new NamedCheckBox(panel) { Name = def.Text, AutoResize = false };
                    check.ValueChanged += (_, _) => { if (!refreshing && (def.Enabled?.Invoke() ?? true)) Safe(() => def.Set(check.Value)); };
                    controls.Add(check);
                    refreshers.Add(() => { check.Value = def.Get(); check.InputEnabled = def.Enabled?.Invoke() ?? true; });
                }
                else
                {
                    var slider = new NamedSliderBox(panel) { Name = def.Text, Min = def.Min, Max = def.Max };
                    slider.ValueChanged += (_, _) =>
                    {
                        slider.ValueText = slider.Value.ToString("0.##");
                        if (!refreshing) Safe(() => def.SetFloat(slider.Value));
                    };
                    controls.Add(slider); refreshers.Add(() => { slider.Value = def.GetFloat(); slider.ValueText = slider.Value.ToString("0.##"); });
                }
            }
            details = new Label(panel) { AutoResize = false, BuilderMode = TextBuilderModes.Wrapped, Format = GlyphFormat.White.WithSize(.8f) };
            status = new Label(panel) { AutoResize = false, BuilderMode = TextBuilderModes.Wrapped, Format = GlyphFormat.White.WithSize(.75f) };
            Resizing += _ => NotifyResize(); Resized += _ => NotifyResize();
        }
        void EnsureRowSlots(int count)
        {
            while (rows.Count < count)
            {
                int slot = rows.Count;
                var row = MakeButton(panel, "", () => { });
                row.MouseInput.LeftClicked += (_, _) => Safe(() => entry.Select?.Invoke(rowIds[slot]));
                rows.Add(row); rowIds.Add(0);
            }
        }
        internal void RefreshControls()
        {
            refreshing = true;
            try { foreach (var refresh in refreshers) Safe(refresh); } finally { refreshing = false; }
        }
        static LabelBoxButton MakeButton(HudParentBase parent, string text, Action click)
        {
            var button = new LabelBoxButton(parent) { Text = text, AutoResize = false, Format = GlyphFormat.White.WithSize(.8f), Color = new Color(28, 44, 60), HighlightColor = new Color(48, 72, 94) };
            button.MouseInput.LeftClicked += (_, _) => Safe(click);
            return button;
        }
        void NotifyResize() { Safe(() => entry.Resize?.Invoke(viewport.Width, viewport.Height)); }
        Vector2 lastViewport;
        protected override void Layout()
        {
            base.Layout();
            panel.Size = new Vector2(300, body.Height - 16);
            panel.Offset = new Vector2((body.Width - panel.Width) * .5f - 8, 0);
            viewport.Size = new Vector2(body.Width - panel.Width - 32, body.Height - 16);
            viewport.Offset = new Vector2(-panel.Width * .5f - 8, 0);
            if (viewport.Size != lastViewport) { lastViewport = viewport.Size; NotifyResize(); }
            EnsureRowSlots((int)Math.Ceiling(panel.Height / 28) + 2);
            lock (entry.Gate)
            {
                float total = controls.Count * 34 + 170 + entry.Rows.Length * 28;
                scroll = MathHelper.Clamp(scroll, 0, Math.Max(0, total - panel.Height));
                float y = panel.Height * .5f - 18 + scroll;
                foreach (var control in controls) { control.Size = new Vector2(panel.Width - 8, 30); control.Offset = new Vector2(0, y); y -= 34; }
                if (details.Text.ToString() != entry.Details) details.Text = entry.Details;
                details.Size = new Vector2(panel.Width - 8, 100); details.Offset = new Vector2(0, y - 38); y -= 104;
                if (status.Text.ToString() != entry.Status) status.Text = entry.Status;
                status.Size = new Vector2(panel.Width - 8, 60); status.Offset = new Vector2(0, y - 20); y -= 66;
                int first = Math.Max(0, (int)Math.Floor((y - panel.Height * .5f) / 28));
                for (int i = 0; i < rows.Count; i++)
                {
                    int index = first + i;
                    var row = rows[i]; row.Visible = index < entry.Rows.Length && y - index * 28 > -panel.Height * .5f - 28;
                    if (!row.Visible) continue;
                    rowIds[i] = entry.Ids[index];
                    if (row.Text.ToString() != entry.Rows[index]) row.Text = entry.Rows[index];
                    row.Size = new Vector2(panel.Width - 8, 26); row.Offset = new Vector2(0, y - index * 28);
                }
            }
        }
        protected override void HandleInput(Vector2 cursorPos)
        {
            base.HandleInput(cursorPos);
            if (panelInput.IsMousedOver) scroll -= MyInput.Static.DeltaMouseScrollWheelValue() / 120f * 60;
        }
    }
    sealed class Viewport : HudElementBase
    {
        readonly Entry entry;
        readonly MouseInputElement mouse;
        readonly Label[] labels = new Label[128];
        Vector2 lastCursor;
        bool orbit, pan;
        public Viewport(HudParentBase parent, Entry entry) : base(parent)
        {
            this.entry = entry; IsMasking = true; UseCursor = true;
            mouse = new MouseInputElement(this) { DimAlignment = DimAlignments.Size };
            for (int i = 0; i < labels.Length; i++) labels[i] = new Label(this) { Visible = false, AutoResize = true, Format = GlyphFormat.White.WithSize(.75f), UseCursor = false };
        }
        protected override void HandleInput(Vector2 cursorPos)
        {
            var input = MyInput.Static;
            Vector2 local = cursorPos - Position, delta = cursorPos - lastCursor; lastCursor = cursorPos;
            if (mouse.IsMousedOver)
            {
                if (mouse.IsNewLeftClicked) Safe(() => entry.Input?.Invoke(0, local.X, local.Y, 0));
                if (input.IsNewRightMousePressed()) orbit = true;
                if (input.IsNewMiddleMousePressed()) pan = true;
                float wheel = input.DeltaMouseScrollWheelValue() / 120f;
                if (wheel != 0) Safe(() => entry.Input?.Invoke(3, local.X, local.Y, wheel));
            }
            if (!input.IsRightMousePressed()) orbit = false;
            if (!input.IsMiddleMousePressed()) pan = false;
            if ((orbit || pan) && delta != Vector2.Zero) Safe(() => entry.Input?.Invoke(orbit ? 1 : 2, delta.X, delta.Y, 0));
        }
        protected override void Layout()
        {
            lock (entry.Gate) for (int i = 0; i < labels.Length; i++)
            {
                labels[i].Visible = i < entry.Labels.Length;
                if (!labels[i].Visible) continue;
                if (labels[i].Text.ToString() != entry.Labels[i]) labels[i].Text = entry.Labels[i];
                labels[i].Offset = new Vector2(entry.LabelPositions[i * 2], entry.LabelPositions[i * 2 + 1]);
            }
        }
        readonly List<VRage.MyTuple<VRageRender.MyBillboard.BlendTypeEnum, Vector2I, VRage.Utils.MyStringId,
            VRage.MyTuple<Vector4, BoundingBox2?>, VRage.MyTuple<Vector2, Vector2, Vector2>, VRage.MyTuple<Vector2, Vector2, Vector2>>> batch = new();
        void Triangle(Vector2 a, Vector2 b, Vector2 c, Vector4 color)
        {
            batch.Add(new(VRageRender.MyBillboard.BlendTypeEnum.PostPP, Vector2I.Zero, Material.Default.TextureID,
                new(color, MaskingBox), new(Vector2.Zero, Vector2.UnitY, Vector2.UnitX), new(a + Position, b + Position, c + Position)));
        }
        protected override void Draw()
        {
            batch.Clear();
            lock (entry.Gate)
            {
                for (int i = 0; i < entry.TriangleCount; i++)
                {
                    int p = i * 10; var data = entry.Triangles;
                    Triangle(new(data[p], data[p + 1]), new(data[p + 2], data[p + 3]), new(data[p + 4], data[p + 5]),
                        new Color(data[p + 6], data[p + 7], data[p + 8], data[p + 9]).GetBbColor());
                }
                for (int i = 0; i < entry.LineCount; i++)
                {
                    int p = i * 8; var data = entry.Lines;
                    var a = new Vector2(data[p], data[p + 1]); var b = new Vector2(data[p + 2], data[p + 3]);
                    var normal = b - a; if (normal.LengthSquared() < .0001f) continue;
                    normal = Vector2.Normalize(new Vector2(-normal.Y, normal.X)) * .6f;
                    var color = new Color(data[p + 4], data[p + 5], data[p + 6], data[p + 7]).GetBbColor();
                    Triangle(a - normal, a + normal, b + normal, color); Triangle(a - normal, b + normal, b - normal, color);
                }
            }
            BillBoardUtils.AddTriangleData(batch, HudSpace.PlaneToWorldRef);
        }
    }
}
