using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RichHudFramework.UI;
using RichHudFramework.UI.Client;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.RichHud;

/// <summary>
/// Master paints every settings tile at 300×250. Anomaly pages overwrite
/// that from <see cref="TerminalConfigRegistry.WindowSize"/> and arrange
/// controls in internal columns inside the tile. Master’s HUD types live in
/// Master’s assembly — never cast them to Anomaly’s
/// <see cref="HudElementBase"/>.
/// </summary>
static class TerminalTileCompactor
{
    static readonly Harmony Harmony = new("Anomaly.TerminalTileCompactor");
    static readonly ConditionalWeakTable<object, SizeBox> WantedTiles = new();
    static readonly ConditionalWeakTable<object, SizeBox> WantedChains = new();
    static readonly HashSet<MethodInfo> PatchedChainLayouts = new();
    static bool patched;
    static bool beginLayoutPatched;
    static bool slidersPatched;
    static bool loggedMiss;
    static bool loggedApply;
    static bool loggedChainApply;
    static bool loggedArrangeMiss;

    sealed class SizeBox
    {
        public Vector2 Size;
        public int Columns = 1;
    }

    public static void EnsurePatched()
    {
        if (patched)
            return;

        var ours = typeof(HudElementBase).Assembly;
        var found = 0;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (ReferenceEquals(assembly, ours))
                continue;

            Type tileType;
            try
            {
                tileType = assembly.GetType("RichHudFramework.UI.Server.ControlTile", false);
            }
            catch
            {
                continue;
            }

            if (tileType == null)
                continue;

            Type elementType = null;
            foreach (var nested in tileType.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (nested.Name == "TileElement")
                {
                    elementType = nested;
                    break;
                }
            }

            if (elementType == null)
                continue;

            var layout = elementType.GetMethod("Layout", BindingFlags.Instance | BindingFlags.NonPublic);
            if (layout == null)
                continue;

            Harmony.Patch(layout, postfix: new HarmonyMethod(typeof(TerminalTileCompactor), nameof(TileLayoutPostfix)));
            found++;
            // Chain Layout is patched from the live tile chain in ApplyTile —
            // Master uses HudChain<TerminalControlBase>, not non-generic HudChain.
        }

        PatchBeginLayout();
        PatchSliderLayout();

        if (found == 0)
            return;

        patched = true;
        MyLog.Default.WriteLine("Anomaly: settings tile size lock is live");
    }

    /// <summary>
    /// BeginLayout runs Layout then UpdateChildAlignment. A postfix here can
    /// re-grid and re-align in the same frame even when the generic Layout
    /// patch does not fire (that was the cols=2 / still-vertical failure).
    /// </summary>
    static void PatchBeginLayout()
    {
        if (beginLayoutPatched)
            return;

        var ours = typeof(HudElementBase).Assembly;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (ReferenceEquals(assembly, ours))
                continue;

            Type elementBase;
            try
            {
                elementBase = assembly.GetType("RichHudFramework.UI.HudElementBase", false);
            }
            catch
            {
                continue;
            }

            if (elementBase == null)
                continue;

            var begin = elementBase.GetMethod(
                "BeginLayout",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (begin == null)
                continue;

            Harmony.Patch(
                begin,
                postfix: new HarmonyMethod(typeof(TerminalTileCompactor), nameof(BeginLayoutPostfix)));
            beginLayoutPatched = true;
            MyLog.Default.WriteLine("Anomaly: settings BeginLayout grid postfix is live");
            return;
        }
    }

    static void BeginLayoutPostfix(object __instance)
    {
        if (__instance == null || !WantedChains.TryGetValue(__instance, out var box))
            return;
        if (box.Columns < 2)
            return;
        if (!ArrangeInternalColumns(__instance, box.Columns))
            return;

        // Layout already finished and UpdateChildAlignment used Master’s
        // vertical Offsets — push Positions again from the LTR Offsets.
        try
        {
            for (var t = __instance.GetType(); t != null; t = t.BaseType)
            {
                var align = t.GetMethod(
                    "UpdateChildAlignment",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (align == null)
                    continue;
                align.Invoke(__instance, null);
                return;
            }
        }
        catch
        {
            // Next frame’s BeginLayout will pick up Offsets.
        }
    }

    /// <summary>
    /// Patch Layout on the live chain’s closed generic. Postfix overwrites
    /// Master’s vertical AlignMembersCenter stack with an LTR grid — Prefix
    /// skip was a no-op in practice (never applied / never logged).
    /// </summary>
    static void EnsureChainLayoutPatched(object chain)
    {
        if (chain == null)
            return;

        try
        {
            MethodInfo layout = null;
            for (var t = chain.GetType(); t != null && layout == null; t = t.BaseType)
                layout = t.GetMethod("Layout", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            if (layout == null || !PatchedChainLayouts.Add(layout))
                return;

            Harmony.Patch(
                layout,
                postfix: new HarmonyMethod(typeof(TerminalTileCompactor), nameof(ChainLayoutPostfix)));
            MyLog.Default.WriteLine(
                "Anomaly: settings internal-column Layout patched on " + layout.DeclaringType);
        }
        catch (Exception e)
        {
            MyLog.Default.WriteLine("Anomaly: settings live chain layout patch skipped: " + e.Message);
        }
    }

    static void PatchSliderLayout()
    {
        if (slidersPatched)
            return;

        var ours = typeof(HudElementBase).Assembly;
        var found = 0;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (ReferenceEquals(assembly, ours))
                continue;

            Type slider;
            try
            {
                slider = assembly.GetType("RichHudFramework.UI.NamedSliderBox", false);
            }
            catch
            {
                continue;
            }

            if (slider == null)
                continue;

            var layout = slider.GetMethod("Layout", BindingFlags.Instance | BindingFlags.NonPublic);
            if (layout == null)
                continue;

            Harmony.Patch(layout, postfix: new HarmonyMethod(typeof(TerminalTileCompactor), nameof(SliderLayoutPostfix)));
            found++;
        }

        if (found > 0)
            slidersPatched = true;
    }

    static void TileLayoutPostfix(object __instance)
    {
        if (__instance == null || !WantedTiles.TryGetValue(__instance, out var box))
            return;

        SetPadding(__instance, TerminalWindowLayout.TilePadding);
        // Always re-apply — NearlyEqual skips blocked live resize.
        SetSize(__instance, box.Size);

        var chain = FindField(__instance, "controls");
        if (chain == null)
            return;

        EnsureChainLayoutPatched(chain);
        if (!WantedChains.TryGetValue(chain, out _))
            WantedChains.Add(chain, box);

        SetFloat(chain, "Spacing", TerminalWindowLayout.ControlSpacing);
        SetProperty(
            chain,
            "SizingMode",
            box.Columns > 1
                ? HudChainSizingModes.None
                : HudChainSizingModes.FitMembersOffAxis | HudChainSizingModes.AlignMembersStart);

        // Chain.Layout runs in the child’s BeginLayout (often after this
        // postfix). Still arrange here so the first frame isn’t empty if the
        // chain patch misses; ChainLayoutPostfix re-applies after Master.
        if (box.Columns > 1)
            ArrangeInternalColumns(chain, box.Columns);
    }

    /// <summary>
    /// After Master stacks vertically, place controls left-to-right in columns.
    /// Runs inside BeginLayout before UpdateChildAlignment, so Offsets stick.
    /// </summary>
    static void ChainLayoutPostfix(object __instance)
    {
        if (__instance == null)
            return;

        if (!TryGetChainBox(__instance, out var box))
        {
            LogArrangeMiss("WantedChains miss on " + __instance.GetType().Name);
            return;
        }

        if (box.Columns < 2)
            return;

        ArrangeInternalColumns(__instance, box.Columns);
    }

    static bool TryGetChainBox(object chain, out SizeBox box)
    {
        if (WantedChains.TryGetValue(chain, out box))
            return true;

        // Recover via parent TileElement — CWT key miss should be rare, but
        // the Layout postfix must not silently fall back to Master’s stack.
        var parent = GetProperty(chain, "Parent");
        if (parent != null && WantedTiles.TryGetValue(parent, out box))
        {
            WantedChains.Add(chain, box);
            return true;
        }

        box = null;
        return false;
    }

    /// <returns>True when the grid was applied.</returns>
    static bool ArrangeInternalColumns(object chain, int columns)
    {
        var members = VisibleElements(chain);
        if (members.Count == 0)
        {
            LogArrangeMiss("no visible members on " + chain.GetType().Name);
            return false;
        }

        var inner = Unpadded(chain);
        if (inner.X < 1f || inner.Y < 1f)
        {
            var parent = GetProperty(chain, "Parent");
            if (parent != null)
                inner = Unpadded(parent);
        }

        if (inner.X < 1f || inner.Y < 1f)
        {
            // Fall back to the size we locked on the tile.
            if (WantedChains.TryGetValue(chain, out var box) && box.Size.X > 1f)
            {
                inner = new Vector2(
                    Math.Max(1f, box.Size.X - TerminalWindowLayout.TilePadding.X),
                    Math.Max(1f, box.Size.Y - TerminalWindowLayout.TilePadding.Y));
            }
        }

        if (inner.X < 1f || inner.Y < 1f)
        {
            LogArrangeMiss("inner size 0");
            return false;
        }

        var gap = TerminalWindowLayout.ControlSpacing;
        var colW = (inner.X - gap * (columns - 1)) / columns;
        if (colW < 80f)
            colW = inner.X;

        var heights = new float[members.Count];
        for (var i = 0; i < members.Count; i++)
        {
            // DimAlignment.UnpaddedWidth (NamedDropdown / TerminalButton) can
            // leave a bogus full-tile CachedSize until we clear it in PlaceMember.
            heights[i] = SizeOf(members[i]).Y;
            if (heights[i] < 1f || heights[i] > TerminalWindowLayout.WideControlHeight)
                heights[i] = TerminalWindowLayout.StandardControlHeight;
        }

        var top = 0.5f * inner.Y;
        var iMember = 0;
        // Color pickers only — dropdowns are Standard height, not full-row Wide.
        var wideFloor = TerminalWindowLayout.WideControlHeight * 0.85f;
        var align = ParentAlignments.Inner | ParentAlignments.UsePadding;
        while (iMember < members.Count)
        {
            if (heights[iMember] >= wideFloor)
            {
                PlaceMember(members[iMember], inner.X, new Vector2(0f, top - heights[iMember] * 0.5f), align);
                top -= heights[iMember] + gap;
                iMember++;
                continue;
            }

            var rowCount = Math.Min(columns, members.Count - iMember);
            for (var c = 0; c < rowCount; c++)
            {
                if (heights[iMember + c] >= wideFloor)
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
                if (heights[iMember + c] > rowH)
                    rowH = heights[iMember + c];
            }

            for (var c = 0; c < rowCount; c++)
            {
                var x = -0.5f * inner.X + colW * 0.5f + c * (colW + gap);
                PlaceMember(members[iMember + c], colW, new Vector2(x, top - rowH * 0.5f), align);
            }

            top -= rowH + gap;
            iMember += rowCount;
        }

        if (!loggedChainApply)
        {
            loggedChainApply = true;
            MyLog.Default.WriteLine(
                "Anomaly: settings internal columns applied members="
                + members.Count + " cols=" + columns
                + " inner=" + (int)inner.X + "x" + (int)inner.Y);
        }

        return true;
    }

    static void PlaceMember(object el, float width, Vector2 offset, ParentAlignments align)
    {
        // Master NamedDropdown / TerminalButton use DimAlignment.UnpaddedWidth
        // so the chain’s UpdateChildAlignment stretches them to the full tile.
        // That undoes column Width and shifts the open list over the sidebar
        // (center at column offset, half-width = full tile).
        SetProperty(el, "DimAlignment", DimAlignments.None);

        var size = SizeOf(el);
        var height = size.Y;
        if (height < 1f || height > TerminalWindowLayout.WideControlHeight)
            height = TerminalWindowLayout.StandardControlHeight;

        // NamedDropdown defaults to 66px; keep that band so the tile height
        // budget (StandardControlHeight) still has padding left.
        if (IsDropdownHost(el))
            height = Math.Min(height, TerminalWindowLayout.StandardControlHeight);

        SetUnpadded(el, new Vector2(width, height));
        SetFloat(el, "Width", width);
        SetFloat(el, "Height", height);
        SetProperty(el, "ParentAlignment", align);
        SetProperty(el, "Offset", offset);

        FitDropdownList(el, width);
    }

    static bool IsDropdownHost(object el)
    {
        var name = el.GetType().Name;
        return name.IndexOf("Dropdown", StringComparison.Ordinal) >= 0;
    }

    /// <summary>
    /// Open list is a child with DimAlignment.Width — keep it on the column
    /// and shrink tall Master defaults (172px) for short enums.
    /// </summary>
    static void FitDropdownList(object host, float width)
    {
        if (!IsDropdownHost(host))
            return;

        var dropdown = FindField(host, "dropdown")
            ?? GetProperty(host, "List");
        if (dropdown == null)
        {
            // Walk children: NamedDropdown → HudChain → Dropdown.
            var children = FindField(host, "children") as IList;
            if (children != null)
            {
                foreach (var child in children)
                {
                    if (child == null)
                        continue;
                    if (child.GetType().Name.IndexOf("Dropdown", StringComparison.Ordinal) >= 0
                        && child.GetType().Name.IndexOf("Named", StringComparison.Ordinal) < 0)
                    {
                        dropdown = child;
                        break;
                    }

                    var nested = FindField(child, "dropdown")
                        ?? FindField(child, "hudCollectionList");
                    if (nested is IList nestList)
                    {
                        foreach (var entry in nestList)
                        {
                            var el = GetProperty(entry, "Element") ?? entry;
                            if (el != null
                                && el.GetType().Name.IndexOf("Dropdown", StringComparison.Ordinal) >= 0)
                            {
                                dropdown = el;
                                break;
                            }
                        }
                    }

                    if (dropdown != null)
                        break;
                }
            }
        }

        if (dropdown == null)
            return;

        SetFloat(dropdown, "Width", width);
        var openH = GetProperty(dropdown, "DropdownHeight");
        if (!(openH is float h) || h <= 120f)
            return;

        var entries = GetProperty(dropdown, "EntryList") as ICollection;
        var count = entries?.Count ?? 0;
        if (count <= 0 || count > 8)
            return;

        var tight = 36f * count + 12f;
        if (tight < h)
            SetProperty(dropdown, "DropdownHeight", tight);
    }

    static List<object> VisibleElements(object chain)
    {
        var list = new List<object>();
        var collection = GetProperty(chain, "Collection") as IEnumerable
            ?? FindField(chain, "hudCollectionList") as IEnumerable;
        if (collection == null)
        {
            LogArrangeMiss("no Collection/hudCollectionList");
            return list;
        }

        foreach (var entry in collection)
        {
            if (entry == null)
                continue;

            // ScrollBoxEntry.Enabled is on the container (TerminalControlBase).
            var entryEnabled = GetProperty(entry, "Enabled");
            if (entryEnabled is bool on && !on)
                continue;

            // HudChain<TerminalControlBase>: entry IS the container; Element is
            // the NamedSliderBox / dropdown host that Master offsets.
            var element = GetProperty(entry, "Element") ?? FindField(entry, "element");
            if (element == null)
                continue;

            var vis = GetProperty(element, "Visible");
            if (vis is bool shown && !shown)
                continue;

            list.Add(element);
        }

        return list;
    }

    static void LogArrangeMiss(string detail)
    {
        if (loggedArrangeMiss)
            return;
        loggedArrangeMiss = true;
        MyLog.Default.WriteLine("Anomaly: settings internal columns skipped (" + detail + ")");
    }

    static void SliderLayoutPostfix(object __instance)
    {
        if (__instance == null)
            return;

        var name = FindField(__instance, "name");
        var value = FindField(__instance, "current");
        var slider = FindField(__instance, "SliderBox");
        if (name == null || value == null || slider == null)
            return;

        var nameText = TextSize(name);
        var valueText = TextSize(value);
        if (nameText.Y <= 0f)
            nameText.Y = 18f;
        if (valueText.Y <= 0f)
            valueText.Y = nameText.Y;

        const float gap = 12f;
        var width = Unpadded(__instance).X;
        var nameWidth = width - valueText.X - gap;
        if (nameWidth < 0f)
            nameWidth = 0f;

        SetUnpadded(name, new Vector2(nameWidth, nameText.Y));
        SetBool(name, "IsMasking", true);
        SetUnpadded(value, valueText);

        var textBand = 18f + Math.Max(nameText.Y, valueText.Y) + 4f;
        var track = Unpadded(__instance).Y - textBand;
        if (track < 16f)
            track = 16f;
        SetFloat(slider, "Height", track);
    }

    public static void ApplyRow(ControlCategory category, float tileHeight, bool header, bool subheader)
    {
        if (category == null)
            return;

        try
        {
            var server = category.ID;
            if (server == null)
                return;

            var element = FindField(server, "categoryElement");
            if (element == null)
            {
                LogMiss("categoryElement");
                return;
            }

            var headerLabel = FindField(element, "header");
            var subheaderLabel = FindField(element, "subheader");
            var scroll = FindField(element, "scrollBox");
            if (headerLabel == null || subheaderLabel == null || scroll == null)
            {
                LogMiss("category chrome");
                return;
            }

            SetBox(headerLabel, header ? TerminalWindowLayout.HeaderHeight : 0f);
            SetBox(subheaderLabel, subheader ? TerminalWindowLayout.SubheaderHeight : 0f);

            var window = TerminalConfigRegistry.WindowSize;
            var width = TerminalWindowLayout.ContentWidth(
                window.X > 1f ? window.X : TerminalWindowLayout.MinWindowWidth);
            SetFloat(element, "Width", width);
            SetFloat(scroll, "Height", tileHeight + TerminalWindowLayout.ScrollSlack);
            SetFloat(element, "Height", TerminalWindowLayout.RowHeight(tileHeight, header, subheader));
        }
        catch (Exception e)
        {
            LogMiss(e.Message);
        }
    }

    public static void ApplyTile(ControlTile tile, float width, float height, int columns)
    {
        if (tile == null)
            return;

        try
        {
            var server = tile.ID;
            if (server == null)
            {
                LogMiss("tile.ID");
                return;
            }

            var element = FindField(server, "tileElement");
            if (element == null)
            {
                LogMiss("tileElement field");
                return;
            }

            if (columns < 1)
                columns = 1;

            var size = new Vector2(width, height);
            SetPadding(element, TerminalWindowLayout.TilePadding);
            SetSize(element, size);

            var chain = FindField(element, "controls");
            if (chain == null)
            {
                LogMiss("controls");
                return;
            }

            EnsureChainLayoutPatched(chain);

            SizeBox box;
            SizeBox chainBox;
            var haveTile = WantedTiles.TryGetValue(element, out box);
            var haveChain = WantedChains.TryGetValue(chain, out chainBox);
            if (!haveTile && haveChain)
            {
                box = chainBox;
                WantedTiles.Add(element, box);
            }
            else if (haveTile && !haveChain)
            {
                WantedChains.Add(chain, box);
            }
            else if (!haveTile && !haveChain)
            {
                box = new SizeBox();
                WantedTiles.Add(element, box);
                WantedChains.Add(chain, box);
            }
            else if (!ReferenceEquals(box, chainBox))
            {
                chainBox.Size = size;
                chainBox.Columns = columns;
            }

            box.Size = size;
            box.Columns = columns;

            SetFloat(chain, "Spacing", TerminalWindowLayout.ControlSpacing);
            // Master defaults to AlignMembersCenter (centered single column).
            // None + our Layout prefix places the LTR grid; single-column
            // keeps off-axis fit but left-aligns. Chain DimAlignment stays
            // UnpaddedSize so it tracks the tile on resize.
            SetProperty(
                chain,
                "SizingMode",
                columns > 1
                    ? HudChainSizingModes.None
                    : HudChainSizingModes.FitMembersOffAxis | HudChainSizingModes.AlignMembersStart);

            EnsurePatched();

            if (!loggedApply)
            {
                loggedApply = true;
                var window = TerminalConfigRegistry.WindowSize;
                MyLog.Default.WriteLine(
                    "Anomaly: settings tile size from window "
                    + (int)window.X + "x" + (int)window.Y
                    + " -> tile " + (int)width + "x" + (int)height
                    + " cols=" + columns
                    + " chain=" + chain.GetType().Name);
            }
        }
        catch (Exception e)
        {
            LogMiss(e.Message);
        }
    }

    static void SetBox(object box, float height)
    {
        SetPadding(box, Vector2.Zero);
        SetFloat(box, "Height", height);
    }

    static void SetSize(object element, Vector2 size)
    {
        SetProperty(element, "Size", size);
    }

    static void SetPadding(object element, Vector2 padding)
    {
        SetProperty(element, "Padding", padding);
    }

    static Vector2 TextSize(object label)
    {
        var board = GetProperty(label, "TextBoard");
        if (board == null)
            return Vector2.Zero;
        var size = GetProperty(board, "TextSize");
        return size is Vector2 v ? v : Vector2.Zero;
    }

    static Vector2 SizeOf(object element)
    {
        var size = GetProperty(element, "Size");
        return size is Vector2 v ? v : Vector2.Zero;
    }

    static Vector2 Unpadded(object element)
    {
        var size = GetProperty(element, "UnpaddedSize");
        return size is Vector2 v ? v : Vector2.Zero;
    }

    static void SetUnpadded(object element, Vector2 size)
    {
        SetProperty(element, "UnpaddedSize", size);
    }

    static void SetBool(object element, string property, bool value)
    {
        SetProperty(element, property, value);
    }

    static void SetFloat(object element, string property, float value)
    {
        SetProperty(element, property, value);
    }

    static object GetProperty(object element, string property)
    {
        return element.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public)?.GetValue(element, null);
    }

    static void SetProperty(object element, string property, object value)
    {
        var prop = element.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public);
        if (prop == null || !prop.CanWrite)
            return;

        // Master’s HUD types live in Master’s assembly. Anomaly’s vendored
        // enums share names/values but are a different Type — coerce.
        if (value != null && prop.PropertyType.IsEnum && value.GetType() != prop.PropertyType)
        {
            try
            {
                value = Enum.ToObject(prop.PropertyType, Convert.ToUInt64(value));
            }
            catch
            {
                return;
            }
        }

        prop.SetValue(element, value, null);
    }

    static object FindField(object instance, string name)
    {
        for (var type = instance.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null)
                return field.GetValue(instance);
        }

        return null;
    }

    static void LogMiss(string detail)
    {
        if (loggedMiss)
            return;
        loggedMiss = true;
        MyLog.Default.WriteLine("Anomaly: settings tile compact skipped (" + detail + ")");
    }
}
