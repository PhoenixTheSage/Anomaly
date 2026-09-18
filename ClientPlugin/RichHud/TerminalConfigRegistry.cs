using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ClientPlugin.Settings;
using RichHudFramework;
using RichHudFramework.Client;
using RichHudFramework.UI;
using RichHudFramework.UI.Client;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.RichHud;

/// <summary>
/// Well-known type for pack and consumer plugins. Resolve by name:
/// <c>ClientPlugin.RichHud.TerminalConfigRegistry</c> — do not take a
/// compile-time reference to Anomaly. Call <see cref="RequestPage"/> or
/// <see cref="RequestFolderPage"/> from <c>LoadAssets</c> or <c>Init</c>.
/// Pages appear under the Rich HUD root <see cref="RootName"/> when Master
/// is in the world. Anomaly's Pulsar mirrors live under
/// <see cref="FrameworkTitle"/> as <c>Settings</c> and <c>Velocity Debug</c>.
/// Those titles plus <see cref="FrameworkTitle"/> fail closed on
/// <see cref="RequestPage"/>. <see cref="RequestFolderPage"/> may use
/// <c>Settings</c> as the page name inside a consumer folder.
/// </summary>
public static class TerminalConfigRegistry
{
    public const string RootName = "Anomaly Shaders";
    public const string FrameworkTitle = "Anomaly";
    public const string SettingsTitle = "Settings";
    public const string VelocityDebugTitle = "Velocity Debug";

    static readonly object Gate = new();
    static readonly Dictionary<string, Page> Pages = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, Folder> Folders = new(StringComparer.OrdinalIgnoreCase);
    static readonly List<Page> Order = new();
    static TerminalPageCategory frameworkGroup;
    static bool mounted;

    public static string LastError { get; private set; }

    public static int PageCount
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
                    return "pages=0";
                var names = new List<string>(Order.Count);
                foreach (var page in Order)
                    names.Add(page.DisplayPath);
                return "pages=" + Order.Count + " (" + string.Join(", ", names) + ")";
            }
        }
    }

    /// <summary>
    /// Returns an existing page or creates one named <paramref name="title"/>
    /// as a sibling of the <see cref="FrameworkTitle"/> folder.
    /// Empty and reserved titles fail closed (null). Same title is idempotent.
    /// </summary>
    public static ITerminalConfigPage RequestPage(string title)
    {
        return RequestPageCore(null, title, reserved: false);
    }

    /// <summary>
    /// Returns an existing page or creates <paramref name="pageTitle"/> under
    /// folder <paramref name="folderTitle"/> (sibling of
    /// <see cref="FrameworkTitle"/>). Folder titles cannot be reserved.
    /// Page title <see cref="SettingsTitle"/> is allowed here so consumers can
    /// nest <c>Anomaly Shaders → Folder → Settings</c>.
    /// </summary>
    public static ITerminalConfigPage RequestFolderPage(string folderTitle, string pageTitle)
    {
        if (string.IsNullOrWhiteSpace(folderTitle))
        {
            LastError = "empty folder title";
            return null;
        }

        return RequestPageCore(folderTitle, pageTitle, reserved: false);
    }

    internal static ITerminalConfigPage RequestReservedPage(string title)
    {
        return RequestPageCore(null, title, reserved: true);
    }

    public static bool UnregisterPage(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        lock (Gate)
        {
            if (!Pages.TryGetValue(title.Trim(), out var page))
                return false;
            if (page.DisplayPath == page.Title && IsReservedPage(page.Title))
            {
                LastError = "reserved title: " + page.Title;
                return false;
            }

            page.Hide();
            Pages.Remove(title.Trim());
            Order.Remove(page);
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
                RichHudTerminal.Root.Enabled = true;
                RichHudTerminal.Root.Name = RootName;
                EnsureFrameworkGroup();
                foreach (var page in Order)
                    page.Mount();
                mounted = true;
                LastError = null;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                MyLog.Default.WriteLine("Anomaly Rich HUD terminal mount failed: " + e.Message);
                DebugLog.Write("TerminalConfigRegistry mount failed: " + e);
            }
        }
    }

    internal static void Unmount()
    {
        lock (Gate)
        {
            foreach (var page in Order)
                page.Unmount();
            foreach (var folder in Folders.Values)
                folder.Unmount();
            try
            {
                if (frameworkGroup != null)
                    frameworkGroup.Enabled = false;
            }
            catch
            {
                // Master already tore the group down.
            }
            frameworkGroup = null;
            mounted = false;
        }
    }

    static void EnsureFrameworkGroup()
    {
        if (frameworkGroup != null || !RichHudClient.Registered)
            return;

        var group = new TerminalPageCategory
        {
            Name = FrameworkTitle,
            Enabled = true,
        };
        RichHudTerminal.Root.Add(group);
        frameworkGroup = group;
    }

    static ITerminalConfigPage RequestPageCore(string folderTitle, string pageTitle, bool reserved)
    {
        if (string.IsNullOrWhiteSpace(pageTitle))
        {
            LastError = "empty title";
            return null;
        }

        pageTitle = pageTitle.Trim();
        if (!string.IsNullOrWhiteSpace(folderTitle))
        {
            folderTitle = folderTitle.Trim();
            if (IsReservedTitle(folderTitle))
            {
                LastError = "reserved folder title: " + folderTitle;
                return null;
            }

            if (IsReservedRootTitle(pageTitle))
            {
                LastError = "reserved title: " + pageTitle;
                return null;
            }
        }
        else
        {
            folderTitle = null;
            if (reserved ? !IsReservedPage(pageTitle) : IsReservedTitle(pageTitle))
            {
                LastError = reserved
                    ? "not a reserved title: " + pageTitle
                    : "reserved title: " + pageTitle;
                return null;
            }
        }

        lock (Gate)
        {
            var key = PageKey(folderTitle, pageTitle);
            if (Pages.TryGetValue(key, out var existing))
                return existing;

            Folder folder = null;
            if (folderTitle != null)
            {
                if (!Folders.TryGetValue(folderTitle, out folder))
                {
                    folder = new Folder(folderTitle);
                    Folders.Add(folderTitle, folder);
                }
            }

            var page = new Page(pageTitle, folder);
            Pages.Add(key, page);
            Order.Add(page);
            if (mounted && RichHudClient.Registered)
            {
                try
                {
                    page.Mount();
                }
                catch (Exception e)
                {
                    LastError = e.Message;
                    DebugLog.Write("TerminalConfigRegistry late mount failed: " + e);
                }
            }

            LastError = null;
            return page;
        }
    }

    static string PageKey(string folderTitle, string pageTitle)
    {
        return folderTitle == null ? pageTitle : folderTitle + "/" + pageTitle;
    }

    static bool IsReservedPage(string title)
    {
        return string.Equals(title, SettingsTitle, StringComparison.OrdinalIgnoreCase)
            || string.Equals(title, VelocityDebugTitle, StringComparison.OrdinalIgnoreCase);
    }

    static bool IsReservedTitle(string title)
    {
        return IsReservedPage(title)
            || IsReservedRootTitle(title);
    }

    static bool IsReservedRootTitle(string title)
    {
        return string.Equals(title, FrameworkTitle, StringComparison.OrdinalIgnoreCase)
            || string.Equals(title, RootName, StringComparison.OrdinalIgnoreCase);
    }

    sealed class Folder
    {
        public Folder(string title)
        {
            Title = title;
        }

        public string Title { get; }

        public TerminalPageCategory Mounted;

        public void EnsureMounted()
        {
            if (Mounted != null || !RichHudClient.Registered)
                return;

            var group = new TerminalPageCategory
            {
                Name = Title,
                Enabled = true,
            };
            RichHudTerminal.Root.Add(group);
            Mounted = group;
        }

        public void Unmount()
        {
            try
            {
                if (Mounted != null)
                    Mounted.Enabled = false;
            }
            catch
            {
                // Master already tore the group down.
            }

            Mounted = null;
        }
    }

    enum TileWeight
    {
        Compact = 1,
        Standard = 2,
        Wide = 3,
    }

    sealed class Page : ITerminalConfigPage
    {
        readonly Folder folder;
        readonly bool persistHostConfig;
        readonly List<CategorySpec> categories = new();
        CategorySpec current;
        ControlPage mountedPage;

        public Page(string title, Folder folder)
        {
            Title = title;
            this.folder = folder;
            persistHostConfig = folder == null && IsReservedPage(title);
        }

        public string Title { get; }

        public string DisplayPath => folder == null ? Title : folder.Title + "/" + Title;

        public ITerminalConfigPage Category(string header)
        {
            return Category(header, null);
        }

        public ITerminalConfigPage Category(string header, string subheader)
        {
            current = new CategorySpec
            {
                Header = string.IsNullOrWhiteSpace(header) ? Title : header.Trim(),
                Subheader = string.IsNullOrWhiteSpace(subheader) ? "" : subheader.Trim(),
            };
            categories.Add(current);
            return this;
        }

        public ITerminalConfigPage Label(string text)
        {
            return Label(text, null);
        }

        public ITerminalConfigPage Label(string text, Func<string> get)
        {
            var spec = new ControlSpec { Weight = TileWeight.Compact };
            TerminalLabel label = null;
            spec.Build = () =>
            {
                var value = get != null ? get() : text;
                if (string.IsNullOrEmpty(value))
                    value = text ?? "";
                label = new TerminalLabel
                {
                    Name = value,
                };
                return label;
            };
            // Pull on Refresh() only. Do not set CustomValueGetter — Master
            // would assign it every HandleInput tick.
            if (get != null)
            {
                spec.Pull = () =>
                {
                    if (label == null)
                        return;
                    var value = get();
                    if (string.IsNullOrEmpty(value))
                        value = text ?? "";
                    label.Name = value;
                };
            }

            AddControl(spec);
            return this;
        }

        public ITerminalConfigPage Checkbox(string label, Func<bool> get, Action<bool> set, string description)
        {
            return Checkbox(label, get, set, description, null);
        }

        public ITerminalConfigPage Checkbox(string label, Func<bool> get, Action<bool> set, string description, Func<bool> enabled)
        {
            if (get == null || set == null)
                return this;
            var spec = new ControlSpec { Weight = TileWeight.Compact };
            spec.Build = () => BuildCheckbox(spec, label, get, set, description, enabled);
            AddControl(spec);
            return this;
        }

        public ITerminalConfigPage Slider(string label, float min, float max, Func<float> get, Action<float> set, string description)
        {
            return Slider(label, min, max, get, set, description, 1f);
        }

        public ITerminalConfigPage Slider(string label, float min, float max, Func<float> get, Action<float> set, string description, float step)
        {
            if (get == null || set == null)
                return this;
            var spec = new ControlSpec { Weight = TileWeight.Standard };
            spec.Build = () => BuildSlider(spec, label, min, max, step, false, () => get(), v => set(v), description);
            AddControl(spec);
            return this;
        }

        public ITerminalConfigPage IntSlider(string label, int min, int max, Func<int> get, Action<int> set, string description)
        {
            return IntSlider(label, min, max, get, set, description, 1);
        }

        public ITerminalConfigPage IntSlider(string label, int min, int max, Func<int> get, Action<int> set, string description, int step)
        {
            if (get == null || set == null)
                return this;
            var spec = new ControlSpec { Weight = TileWeight.Standard };
            spec.Build = () => BuildSlider(spec, label, min, max, step < 1 ? 1 : step, true, () => get(), v => set((int)Math.Round(v)), description);
            AddControl(spec);
            return this;
        }

        public ITerminalConfigPage Dropdown(string label, Type enumType, Func<object> get, Action<object> set, string description)
        {
            if (enumType == null || !enumType.IsEnum || get == null || set == null)
                return this;
            var spec = new ControlSpec { Weight = TileWeight.Standard };
            spec.Build = () => BuildDropdown(spec, label, enumType, get, set, description);
            AddControl(spec);
            return this;
        }

        public ITerminalConfigPage Dropdown<T>(string label, Func<T> get, Action<T> set, string description)
        {
            if (get == null || set == null)
                return this;
            return Dropdown(label, typeof(T), () => get(), v => set((T)v), description);
        }

        public ITerminalConfigPage Button(string label, Action click, string description)
        {
            if (click == null)
                return this;
            AddControl(new ControlSpec
            {
                Build = () =>
                {
                    var button = new TerminalButton
                    {
                        Name = label,
                        ControlChangedHandler = (_, __) => click(),
                    };
                    ApplyTip(button, description);
                    return button;
                },
                Weight = TileWeight.Compact,
            });
            return this;
        }

        public ITerminalConfigPage Color(string label, Func<Color> get, Action<Color> set, string description)
        {
            if (get == null || set == null)
                return this;
            var spec = new ControlSpec { Weight = TileWeight.Wide };
            spec.Build = () => BuildColor(spec, label, get, set, description);
            AddControl(spec);
            return this;
        }

        public ITerminalConfigPage Refresh()
        {
            PullMounted();
            return this;
        }

        public void Mount()
        {
            if (mountedPage != null || !RichHudClient.Registered)
                return;

            var page = new ControlPage
            {
                Name = Title,
                Enabled = true,
            };

            foreach (var spec in categories)
            {
                foreach (var row in BuildCategoryRows(spec))
                    page.Add(row);
            }

            if (folder != null)
            {
                folder.EnsureMounted();
                if (folder.Mounted == null)
                    return;
                folder.Mounted.Add(page);
            }
            else if (IsReservedPage(Title))
            {
                EnsureFrameworkGroup();
                if (frameworkGroup == null)
                    return;
                frameworkGroup.Add(page);
            }
            else
            {
                RichHudTerminal.Root.Add(page);
            }

            mountedPage = page;
        }

        public void Unmount()
        {
            try
            {
                if (mountedPage != null)
                    mountedPage.Enabled = false;
            }
            catch
            {
                // Master already tore the page down.
            }
            mountedPage = null;
            foreach (var spec in categories)
            {
                spec.MountedCategory = null;
                spec.MountedRows.Clear();
            }
        }

        public void Hide()
        {
            if (mountedPage != null)
                mountedPage.Enabled = false;
        }

        void AddControl(ControlSpec spec)
        {
            if (current == null)
                Category(Title);
            current.Controls.Add(spec);
            if (mountedPage == null)
                return;
            if (current.MountedRows.Count == 0)
            {
                foreach (var row in BuildCategoryRows(current))
                    mountedPage.Add(row);
                return;
            }

            var packed = PackTiles(current.Controls);
            var lastGroup = packed[packed.Count - 1];
            var startedNewTile = lastGroup.Count == 1 && ReferenceEquals(lastGroup[0], spec);
            var last = current.MountedRows[current.MountedRows.Count - 1];
            if (!startedNewTile)
            {
                last.Tiles[last.Tiles.Count - 1].Add(spec.Build());
                return;
            }

            if (last.Tiles.Count >= TilesPerRow())
            {
                last = NewCategoryRow(current, first: false);
                current.MountedRows.Add(last);
                mountedPage.Add(last);
            }

            var tile = new ControlTile { Enabled = true };
            last.Add(tile);
            tile.Add(spec.Build());
            current.MountedCategory = last;
        }

        List<ControlCategory> BuildCategoryRows(CategorySpec spec)
        {
            spec.MountedRows.Clear();
            spec.MountedCategory = null;
            var packed = PackTiles(spec.Controls);
            if (packed.Count == 0)
                return spec.MountedRows;

            var perRow = TilesPerRow();
            for (var i = 0; i < packed.Count; i += perRow)
            {
                var row = NewCategoryRow(spec, first: i == 0);
                var count = Math.Min(perRow, packed.Count - i);
                for (var t = 0; t < count; t++)
                {
                    var tile = new ControlTile { Enabled = true };
                    row.Add(tile);
                    foreach (var control in packed[i + t])
                        tile.Add(control.Build());
                }

                spec.MountedRows.Add(row);
            }

            spec.MountedCategory = spec.MountedRows[spec.MountedRows.Count - 1];
            return spec.MountedRows;
        }

        static ControlCategory NewCategoryRow(CategorySpec spec, bool first)
        {
            var category = new ControlCategory
            {
                HeaderText = first ? spec.Header : "\u00A0",
                Enabled = true,
            };
            if (first && !string.IsNullOrEmpty(spec.Subheader))
                category.SubheaderText = spec.Subheader;
            else
                category.SubheaderText = "\u00A0";
            return category;
        }

        /// <summary>
        /// Master’s <c>ControlCategory</c> is a fixed-height horizontal scroller
        /// (300×250 tiles, 12px gap). The terminal’s minimum width (1044) fits
        /// two tiles after the mod list. Extra tiles go on new category rows.
        /// Column count follows screen width so a stretched terminal on a wide
        /// display picks up more columns without assuming a maximized window
        /// on 1080p.
        /// </summary>
        static int TilesPerRow()
        {
            const float tile = 300f;
            const float gap = 12f;
            const float chrome = 400f;
            const float minWindow = 1044f;
            var screen = SafeScreenWidth();
            var window = Math.Max(minWindow, screen - 80f);
            window = Math.Min(window, Math.Max(minWindow, screen * 0.55f));
            var content = Math.Max(tile, window - chrome);
            var n = (int)((content + gap) / (tile + gap));
            if (n < 1)
                n = 1;
            if (n > 6)
                n = 6;
            return n;
        }

        static float SafeScreenWidth()
        {
            try
            {
                if (!RichHudClient.Registered)
                    return 1920f;
                HudMain.Init();
                var width = HudMain.ScreenDimHighDPI.X;
                return width > 1f ? width : 1920f;
            }
            catch
            {
                return 1920f;
            }
        }

        static List<List<ControlSpec>> PackTiles(List<ControlSpec> controls)
        {
            var tiles = new List<List<ControlSpec>>();
            List<ControlSpec> current = null;
            var kind = TileWeight.Compact;
            foreach (var control in controls)
            {
                if (current == null || !CanFit(current.Count, kind, control.Weight))
                {
                    current = new List<ControlSpec>();
                    tiles.Add(current);
                    kind = control.Weight;
                }
                else if ((int)control.Weight > (int)kind)
                {
                    kind = control.Weight;
                }

                current.Add(control);
            }

            return tiles;
        }

        static bool CanFit(int count, TileWeight tileKind, TileWeight incoming)
        {
            if (count == 0)
                return true;
            if (tileKind == TileWeight.Wide || incoming == TileWeight.Wide)
                return false;
            var limit = tileKind == TileWeight.Standard || incoming == TileWeight.Standard ? 2 : 3;
            return count < limit;
        }

        TerminalSlider BuildSlider(ControlSpec spec, string label, float min, float max, float step, bool integer, Func<float> get, Action<float> set, string description)
        {
            var applied = get();
            var lastText = FormatSlider(applied, integer);
            var pulling = false;
            var slider = new TerminalSlider
            {
                Name = label,
                Min = min,
                Max = max,
                Value = applied,
                ValueText = lastText,
            };
            // Do not set CustomValueGetter. Master's TerminalValue.Update assigns
            // getter() every HandleInput tick. Min/max percent round-trip makes
            // Value != lastValue after mouse-up, so ControlChanged (and ValueText
            // SetText) keeps firing on the HUD thread until Present TDRs.
            slider.ControlChangedHandler = (sender, _) =>
            {
                if (pulling)
                    return;
                var control = sender as TerminalSlider;
                if (control == null)
                    return;
                float value;
                if (!TryRead(() => control.Value, "slider Value", out value))
                    return;
                if (step > 0f)
                    value = (float)Math.Round(value / step) * step;
                if (value == applied)
                    return;
                applied = value;
                if (!TryRun(() => set(value), "slider set"))
                    return;
                var text = FormatSlider(value, integer);
                if (text != lastText)
                {
                    lastText = text;
                    TryRun(() => { control.ValueText = text; }, "slider ValueText");
                }

                if (persistHostConfig)
                    ConfigStorage.Save(Config.Current);
            };
            spec.Pull = () =>
            {
                var value = get();
                if (step > 0f)
                    value = (float)Math.Round(value / step) * step;
                pulling = true;
                try
                {
                    applied = value;
                    slider.Value = value;
                    var text = FormatSlider(value, integer);
                    lastText = text;
                    slider.ValueText = text;
                }
                finally
                {
                    pulling = false;
                }
            };
            ApplyTip(slider, description);
            return slider;
        }

        TerminalCheckbox BuildCheckbox(ControlSpec spec, string label, Func<bool> get, Action<bool> set, string description, Func<bool> enabled)
        {
            var pulling = false;
            var box = new TerminalCheckbox
            {
                Name = label,
                Value = get(),
                Enabled = enabled == null || enabled(),
            };
            // Do not set CustomValueGetter. Master invokes it every HandleInput
            // tick while the page is visible. On Pulsar .NET 10 that cross-ALC
            // call (and the first-frame ControlChanged it can retrigger) is
            // what closes the terminal after opening Anomaly settings.
            box.ControlChangedHandler = (sender, _) =>
            {
                if (pulling)
                    return;
                var control = sender as TerminalCheckbox;
                if (control == null)
                    return;
                bool value;
                if (!TryRead(() => control.Value, "checkbox Value", out value))
                    return;
                if (enabled != null && !enabled())
                {
                    TryRun(() => { control.Value = get(); }, "checkbox revert");
                    return;
                }

                if (value == get())
                    return;
                if (!TryRun(() => set(value), "checkbox set"))
                    return;
                PersistHostIfNeeded();
            };
            spec.Pull = () =>
            {
                pulling = true;
                try
                {
                    box.Value = get();
                    box.Enabled = enabled == null || enabled();
                }
                finally
                {
                    pulling = false;
                }
            };
            ApplyTip(box, description);
            return box;
        }

        TerminalColorPicker BuildColor(ControlSpec spec, string label, Func<Color> get, Action<Color> set, string description)
        {
            var pulling = false;
            var picker = new TerminalColorPicker
            {
                Name = label,
                Value = get(),
            };
            picker.ControlChangedHandler = (sender, _) =>
            {
                if (pulling)
                    return;
                var control = sender as TerminalColorPicker;
                if (control == null)
                    return;
                Color value;
                if (!TryRead(() => control.Value, "color Value", out value))
                    return;
                if (value == get())
                    return;
                if (!TryRun(() => set(value), "color set"))
                    return;
                PersistHostIfNeeded();
            };
            spec.Pull = () =>
            {
                pulling = true;
                try
                {
                    picker.Value = get();
                }
                finally
                {
                    pulling = false;
                }
            };
            ApplyTip(picker, description);
            return picker;
        }

        TerminalDropdown<object> BuildDropdown(ControlSpec spec, string label, Type enumType, Func<object> get, Action<object> set, string description)
        {
            var pulling = false;
            var dropdown = new TerminalDropdown<object>
            {
                Name = label,
            };

            foreach (var value in Enum.GetValues(enumType))
                dropdown.List.Add(UnCamelCase(value.ToString()), value);

            dropdown.List.SetSelection(get());
            // Do not set CustomValueGetter. Master TValue is ListBoxEntry<object>;
            // this client returns EntryData<object>. If net10 binds the Func,
            // Update assigns it every tick, InvalidCastException, ExceptionHandler
            // reloads, and the terminal closes. Selection is push-only.
            dropdown.ControlChangedHandler = (sender, _) =>
            {
                if (pulling)
                    return;
                var control = sender as TerminalDropdown<object>;
                object selected = null;
                if (!TryRun(() => { selected = control?.Value?.AssocObject; }, "dropdown Value"))
                    return;
                if (selected == null || Equals(selected, get()))
                    return;
                if (!TryRun(() => set(selected), "dropdown set"))
                    return;
                if (persistHostConfig)
                    ConfigStorage.Save(Config.Current);
                PullMounted();
            };
            spec.Pull = () =>
            {
                pulling = true;
                try
                {
                    dropdown.List.SetSelection(get());
                }
                finally
                {
                    pulling = false;
                }
            };
            ApplyTip(dropdown, description);
            return dropdown;
        }

        void PullMounted()
        {
            foreach (var cat in categories)
            {
                foreach (var spec in cat.Controls)
                    spec.Pull?.Invoke();
            }
        }

        static bool TryRun(Action action, string what)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception e)
            {
                DebugLog.Write("terminal " + what + ": " + e);
                try
                {
                    MyLog.Default.WriteLine("Anomaly Rich HUD " + what + ": " + e.Message);
                }
                catch
                {
                    // ignored
                }

                return false;
            }
        }

        static bool TryRead<T>(Func<T> read, string what, out T value)
        {
            try
            {
                value = read();
                return true;
            }
            catch (Exception e)
            {
                value = default;
                DebugLog.Write("terminal " + what + ": " + e);
                try
                {
                    MyLog.Default.WriteLine("Anomaly Rich HUD " + what + ": " + e.Message);
                }
                catch
                {
                    // ignored
                }

                return false;
            }
        }

        static void ApplyTip(TerminalControlBase control, string description)
        {
            if (!string.IsNullOrEmpty(description))
                control.ToolTip = description;
        }

        static string FormatSlider(float value, bool integer)
        {
            return integer ? ((int)Math.Round(value)).ToString() : value.ToString("0.##");
        }

        static string UnCamelCase(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            return Regex.Replace(
                Regex.Replace(value, @"(\P{Ll})(\P{Ll}\p{Ll})", "$1 $2"),
                @"(\p{Ll})(\P{Ll})",
                "$1 $2");
        }

        void PersistHostIfNeeded()
        {
            if (persistHostConfig)
                ConfigStorage.Save(Config.Current);
        }

        sealed class CategorySpec
        {
            public string Header;
            public string Subheader;
            public readonly List<ControlSpec> Controls = new();
            public readonly List<ControlCategory> MountedRows = new();
            public ControlCategory MountedCategory;
        }

        sealed class ControlSpec
        {
            public Func<TerminalControlBase> Build;
            public Action Pull;
            public TileWeight Weight = TileWeight.Standard;
        }
    }
}
