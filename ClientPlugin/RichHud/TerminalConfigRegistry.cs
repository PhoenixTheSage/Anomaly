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
    static readonly List<Action<float, float>> ResizeHandlers = new();
    static TerminalPageCategory frameworkGroup;
    static bool mounted;
    static bool mountFailed;
    static Vector2 windowSize;
    static int columns = TerminalWindowLayout.ColumnsFromWidth(TerminalWindowLayout.MinWindowWidth);

    public static string LastError { get; private set; }

    /// <summary>
    /// Last sampled Master terminal window size in HUD pixels. Zero until
    /// the resize monitor sees <c>HandleInput</c>. Packs that need wrapping
    /// beyond tile columns can read this from
    /// <see cref="ITerminalConfigPage.OnResized"/> /
    /// <see cref="RegisterWindowResized"/>.
    /// </summary>
    public static Vector2 WindowSize
    {
        get { lock (Gate) return windowSize; }
    }

    /// <summary>
    /// Default internal columns for the current <see cref="WindowSize"/>.
    /// Each section is one full-width tile; controls wrap inside it unless
    /// the section calls <see cref="ITerminalConfigPage.Columns"/> or
    /// <see cref="ITerminalConfigPage.SeparateAt"/>. One column before the
    /// first sample.
    /// </summary>
    public static int Columns
    {
        get { lock (Gate) return columns; }
    }

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
                var size = windowSize.X > 1f
                    ? " window=" + (int)windowSize.X + "x" + (int)windowSize.Y
                      + " tileW=" + (int)TerminalWindowLayout.ContentWidth(windowSize.X)
                      + " cols=" + columns
                    : " cols=" + columns;
                return "pages=" + Order.Count + " (" + string.Join(", ", names) + ")" + size;
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
        return RequestPageCore((IReadOnlyList<string>)null, title, reserved: false);
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

        return RequestPageCore(new[] { folderTitle.Trim() }, pageTitle, reserved: false);
    }

    /// <summary>
    /// Page under an arbitrary folder path beside <see cref="FrameworkTitle"/>.
    /// Empty / reserved folder names fail closed. Page title
    /// <see cref="SettingsTitle"/> is allowed. Same path + page is
    /// idempotent. Not pack-specific. Each path segment is a nested
    /// <c>TerminalPageCategory</c> (leaf pages mount on the last folder).
    /// </summary>
    public static ITerminalConfigPage RequestFolderPage(string pageTitle, IReadOnlyList<string> folders)
    {
        if (folders == null || folders.Count == 0)
        {
            LastError = "empty folder path";
            return null;
        }

        return RequestPageCore(folders, pageTitle, reserved: false);
    }

    internal static ITerminalConfigPage RequestReservedPage(string title)
    {
        return RequestPageCore((IReadOnlyList<string>)null, title, reserved: true);
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
            if (mounted || mountFailed)
                return;

            try
            {
                TerminalCategoryNest.EnsurePatched();
                TerminalWindowMonitor.EnsurePatched();
                TerminalTileCompactor.EnsurePatched();
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
                mountFailed = true;
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
            mountFailed = false;
            TerminalWindowMonitor.Reset();
        }
    }

    /// <summary>
    /// Optional pack callback after a completed terminal resize (mouse-up).
    /// HUD input thread. Do not serialize or write a <c>.cfg</c>. Same
    /// handler is idempotent. Empty handler fails closed.
    /// </summary>
    public static bool RegisterWindowResized(Action<float, float> handler)
    {
        if (handler == null)
        {
            LastError = "missing resize handler";
            return false;
        }

        lock (Gate)
        {
            if (!ResizeHandlers.Contains(handler))
                ResizeHandlers.Add(handler);
            LastError = null;
            return true;
        }
    }

    public static bool UnregisterWindowResized(Action<float, float> handler)
    {
        if (handler == null)
            return false;
        lock (Gate)
            return ResizeHandlers.Remove(handler);
    }

    internal static void NotifyWindowResized(Vector2 size)
    {
        ApplyWindowSize(size, completed: true);
    }

    internal static void ApplyWindowSize(Vector2 size, bool completed)
    {
        List<Action<float, float>> handlers = null;
        List<Page> pages;
        lock (Gate)
        {
            windowSize = size;
            columns = TerminalWindowLayout.ColumnsFromWidth(size.X);
            pages = new List<Page>(Order);
            if (completed)
                handlers = ResizeHandlers.Count == 0 ? null : new List<Action<float, float>>(ResizeHandlers);
        }

        if (mounted)
        {
            foreach (var page in pages)
            {
                try
                {
                    page.PresentSize();
                }
                catch (Exception e)
                {
                    DebugLog.Write("terminal reflow " + page.DisplayPath + ": " + e);
                }
            }
        }

        if (!completed)
            return;

        InvokeResizeHandlers(handlers, size);
        foreach (var page in pages)
            page.NotifyResized(size);
    }

    static void InvokeResizeHandlers(List<Action<float, float>> handlers, Vector2 size)
    {
        if (handlers == null)
            return;
        foreach (var handler in handlers)
        {
            try
            {
                handler(size.X, size.Y);
            }
            catch (Exception e)
            {
                DebugLog.Write("terminal WindowResized: " + e);
            }
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
        IReadOnlyList<string> folders = string.IsNullOrWhiteSpace(folderTitle)
            ? null
            : new[] { folderTitle.Trim() };
        return RequestPageCore(folders, pageTitle, reserved);
    }

    static ITerminalConfigPage RequestPageCore(IReadOnlyList<string> folders, string pageTitle, bool reserved)
    {
        if (string.IsNullOrWhiteSpace(pageTitle))
        {
            LastError = "empty title";
            return null;
        }

        pageTitle = pageTitle.Trim();
        if (folders != null && folders.Count > 0)
        {
            if (IsReservedRootTitle(pageTitle))
            {
                LastError = "reserved title: " + pageTitle;
                return null;
            }
        }
        else
        {
            folders = null;
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
            Folder folder = null;
            if (folders != null)
            {
                folder = EnsureFolderPath(folders);
                if (folder == null)
                    return null;
            }

            var key = PageKey(folder, pageTitle);
            if (Pages.TryGetValue(key, out var existing))
                return existing;

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

    static Folder EnsureFolderPath(IReadOnlyList<string> folders)
    {
        Folder parent = null;
        var path = "";
        for (int i = 0; i < folders.Count; i++)
        {
            var seg = folders[i];
            if (string.IsNullOrWhiteSpace(seg))
            {
                LastError = "empty folder title";
                return null;
            }

            seg = seg.Trim();
            if (IsReservedTitle(seg))
            {
                LastError = "reserved folder title: " + seg;
                return null;
            }

            path = path.Length == 0 ? seg : path + "/" + seg;
            if (!Folders.TryGetValue(path, out var folder))
            {
                folder = new Folder(seg, path, parent);
                Folders.Add(path, folder);
            }

            parent = folder;
        }

        return parent;
    }

    static string PageKey(Folder folder, string pageTitle)
    {
        return folder == null ? pageTitle : folder.Path + "/" + pageTitle;
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
        public Folder(string title, string path, Folder parent)
        {
            Title = title;
            Path = path;
            Parent = parent;
        }

        public string Title { get; }

        public string Path { get; }

        public Folder Parent { get; }

        public TerminalPageCategory Mounted;

        public void EnsureMounted()
        {
            if (Mounted != null || !RichHudClient.Registered)
                return;

            Parent?.EnsureMounted();
            var group = new TerminalPageCategory
            {
                Name = Title,
                Enabled = true,
            };
            if (Parent == null)
                RichHudTerminal.Root.Add(group);
            else if (Parent.Mounted == null || !TerminalCategoryNest.TryAttach(Parent.Mounted, group))
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
        readonly List<Action<float, float>> resized = new();
        CategorySpec current;
        ControlPage mountedPage;
        int lastWrap = -1;

        public Page(string title, Folder folder)
        {
            Title = title;
            this.folder = folder;
            persistHostConfig = folder == null && IsReservedPage(title);
        }

        public string Title { get; }

        public string DisplayPath => folder == null ? Title : folder.Path + "/" + Title;

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

        public ITerminalConfigPage Columns(int count)
        {
            if (current == null)
                Category(Title);
            if (count < 0)
                count = 0;
            if (count > TerminalWindowLayout.MaxColumns)
                count = TerminalWindowLayout.MaxColumns;
            current.Columns = count;
            return this;
        }

        public ITerminalConfigPage SeparateAt(float width)
        {
            if (current == null)
                Category(Title);
            current.SeparateAt = width > 1f ? width : 0f;
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
                Weight = TileWeight.Standard,
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

        public ITerminalConfigPage OnResized(Action<float, float> handler)
        {
            if (handler == null)
                return this;
            if (!resized.Contains(handler))
                resized.Add(handler);
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
            lastWrap = TilesPerRow(null);
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
            lastWrap = -1;
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

        public void PresentSize()
        {
            if (mountedPage == null)
                return;
            var rebuild = false;
            foreach (var spec in categories)
            {
                if (ColumnsFor(spec) != spec.MountedColumns)
                    rebuild = true;
            }

            if (rebuild)
            {
                Reflow();
                return;
            }

            foreach (var spec in categories)
                FitCategory(spec);
        }

        public void Reflow()
        {
            if (mountedPage == null)
                return;
            lastWrap = TilesPerRow(null);
            foreach (var spec in categories)
            {
                if (spec.MountedRows.Count > 0)
                {
                    var old = spec.MountedRows.ToArray();
                    foreach (var row in old)
                    {
                        try
                        {
                            row.Enabled = false;
                        }
                        catch
                        {
                            // Master already tore the row down.
                        }
                    }
                }

                foreach (var row in BuildCategoryRows(spec))
                    mountedPage.Add(row);
            }

            PullMounted();
        }

        public void NotifyResized(Vector2 size)
        {
            if (resized.Count == 0)
                return;
            foreach (var handler in resized.ToArray())
            {
                try
                {
                    handler(size.X, size.Y);
                }
                catch (Exception e)
                {
                    DebugLog.Write("terminal OnResized " + DisplayPath + ": " + e);
                }
            }
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

            // One Master tile per section — append into it and re-fit
            // internal columns from the live window.
            var last = current.MountedRows[current.MountedRows.Count - 1];
            last.Tiles[0].Add(spec.Build());
            FitCategory(current);
        }

        List<ControlCategory> BuildCategoryRows(CategorySpec spec)
        {
            spec.MountedRows.Clear();
            spec.MountedCategory = null;
            if (spec.Controls.Count == 0)
                return spec.MountedRows;

            var cols = ColumnsFor(spec);
            var row = NewCategoryRow(spec, first: true);
            var tile = new ControlTile { Enabled = true };
            row.Add(tile);
            foreach (var control in spec.Controls)
                tile.Add(control.Build());

            spec.MountedRows.Add(row);
            spec.MountedCategory = row;
            spec.MountedColumns = cols;
            FitCategory(spec);
            return spec.MountedRows;
        }

        static void FitCategory(CategorySpec spec)
        {
            if (spec.MountedRows.Count == 0)
                return;

            var cols = ColumnsFor(spec);
            var tileWidth = TerminalWindowLayout.ContentWidth(WindowWidth());
            // Height matches the LTR grid when cols>1; ChainLayoutPostfix
            // places that grid after Master’s vertical stack each Layout.
            var tileHeight = InternalTileHeight(spec.Controls, cols);
            var row = spec.MountedRows[0];
            var tiles = row.Tiles;
            if (tiles.Count > 0)
                TerminalTileCompactor.ApplyTile(tiles[0], tileWidth, tileHeight, cols);

            TerminalTileCompactor.ApplyRow(
                row,
                tileHeight,
                header: true,
                subheader: !string.IsNullOrEmpty(spec.Subheader));
            spec.MountedColumns = cols;
        }

        static float ControlHeight(ControlSpec control)
        {
            var weight = control.Weight;
            return weight == TileWeight.Wide
                ? TerminalWindowLayout.WideControlHeight
                : weight == TileWeight.Standard
                    ? TerminalWindowLayout.StandardControlHeight
                    : TerminalWindowLayout.CompactControlHeight;
        }

        /// <summary>
        /// Content-hugging height for one tile whose controls wrap into
        /// <paramref name="columns"/> internal columns (row-major). Wide
        /// controls take a full row.
        /// </summary>
        static float InternalTileHeight(List<ControlSpec> controls, int columns)
        {
            if (controls == null || controls.Count == 0)
                return TerminalWindowLayout.TilePadding.Y + TerminalWindowLayout.CompactControlHeight;
            if (columns < 1)
                columns = 1;

            var stack = 0f;
            var i = 0;
            while (i < controls.Count)
            {
                if (controls[i].Weight == TileWeight.Wide)
                {
                    if (stack > 0f)
                        stack += TerminalWindowLayout.ControlSpacing;
                    stack += ControlHeight(controls[i]);
                    i++;
                    continue;
                }

                var rowCount = Math.Min(columns, controls.Count - i);
                for (var c = 0; c < rowCount; c++)
                {
                    if (controls[i + c].Weight == TileWeight.Wide)
                    {
                        rowCount = c;
                        break;
                    }
                }

                if (rowCount < 1)
                    rowCount = 1;

                var rowH = 0f;
                for (var c = 0; c < rowCount; c++)
                {
                    var h = ControlHeight(controls[i + c]);
                    if (h > rowH)
                        rowH = h;
                }

                if (stack > 0f)
                    stack += TerminalWindowLayout.ControlSpacing;
                stack += rowH;
                i += rowCount;
            }

            return TerminalWindowLayout.TilePadding.Y + stack;
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
        /// Internal column count for one section. Null uses the page-wide
        /// wrap so <see cref="Reflow"/> can detect a window-driven change.
        /// </summary>
        static int TilesPerRow(CategorySpec spec)
        {
            return ColumnsFor(spec);
        }

        static float WindowWidth()
        {
            return windowSize.X > 1f ? windowSize.X : TerminalWindowLayout.MinWindowWidth;
        }

        static int ColumnsFor(CategorySpec spec)
        {
            return TerminalWindowLayout.ColumnsFor(
                WindowWidth(),
                spec == null ? 0 : spec.Columns,
                spec == null ? 0f : spec.SeparateAt);
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
            // Open-list height is tightened in TerminalTileCompactor.FitDropdownList.
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
            public int Columns;
            public float SeparateAt;
            public int MountedColumns;
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
