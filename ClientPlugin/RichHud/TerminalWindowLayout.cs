using System;
using VRageMath;

namespace ClientPlugin.RichHud;

/// <summary>
/// Tile size for <see cref="TerminalConfigRegistry"/> from the live terminal
/// window. Each section is one full-width Master tile. Controls flow
/// left-to-right into <em>internal</em> columns sized around
/// <see cref="PreferredTileWidth"/> (or <c>SeparateAt</c> /
/// <c>Columns</c>). Sections stack downward, and Master’s page scroll
/// moves them when the window runs out of height.
/// </summary>
public static class TerminalWindowLayout
{
    public const float MinTileWidth = 280f;

    /// <summary>
    /// Default internal control-column width. A section wraps left-to-right
    /// into as many of these as fit the tile (~300px) unless it calls
    /// <c>Columns</c> or <c>SeparateAt</c>. At the default minimum window
    /// this yields at least two columns.
    /// </summary>
    public const float PreferredTileWidth = 300f;

    public const float TileGap = 12f;

    /// <summary>
    /// Master terminal chrome left of the page: mod list 270, divider 26,
    /// two 12px chain gaps, and the page chain’s 40px horizontal padding.
    /// </summary>
    public const float ListChrome = 360f;

    /// <summary>
    /// Room for the page’s vertical scrollbar (Master ScrollBar is 13px)
    /// plus padding so a full-width tile does not force a horizontal scroll
    /// track beside the vertical bar.
    /// </summary>
    public const float PageScrollGutter = 52f;

    /// <summary>
    /// Header, page padding, and a category header above the first tile.
    /// </summary>
    public const float VerticalChrome = 180f;

    public const float MinWindowWidth = 1044f;
    public const float MinWindowHeight = 500f;
    public const int MinColumns = 1;
    public const int MaxColumns = 16;

    /// <summary>
    /// Inset split across both sides (14px horizontal, 18px vertical) so
    /// checkboxes, dropdowns, and buttons stay inside the tile chrome.
    /// </summary>
    public static readonly Vector2 TilePadding = new Vector2(28f, 36f);

    /// <summary>Gap between stacked controls inside a tile.</summary>
    public const float ControlSpacing = 18f;

    /// <summary>Labels and checkboxes (Master NamedCheckBox + caption).</summary>
    public const float CompactControlHeight = 56f;

    /// <summary>Sliders, dropdowns, and buttons (label band + control).</summary>
    public const float StandardControlHeight = 92f;

    /// <summary>Color pickers (swatch + RGB).</summary>
    public const float WideControlHeight = 168f;
    public const float HeaderHeight = 24f;
    public const float SubheaderHeight = 20f;

    /// <summary>Room under the tile for Master’s horizontal scroll track.</summary>
    public const float ScrollSlack = 16f;

    public static float ContentWidth(float windowWidth)
    {
        var width = windowWidth > 1f ? windowWidth : MinWindowWidth;
        var content = width - ListChrome - PageScrollGutter;
        if (content < MinTileWidth)
            content = MinTileWidth;
        return content;
    }

    public static float ContentHeight(float windowHeight)
    {
        var height = windowHeight > 1f ? windowHeight : MinWindowHeight;
        var content = height - VerticalChrome;
        if (content < StandardControlHeight * 2f)
            content = StandardControlHeight * 2f;
        return content;
    }

    /// <summary>
    /// Default wraps at <see cref="PreferredTileWidth"/> so controls sit in
    /// readable internal columns inside one tile. <paramref name="requested"/>
    /// of 1 forces one full-width column. 2 or more forces that count.
    /// <paramref name="separateAt"/> above zero replaces the preferred
    /// wrap width. Counts are limited by how many
    /// <see cref="MinTileWidth"/> columns fit the content width.
    /// </summary>
    public static int ColumnsFor(float windowWidth, int requested, float separateAt)
    {
        if (requested == 1)
            return 1;
        if (requested >= 2)
            return ClampColumns(windowWidth, requested);

        var limit = separateAt > 1f ? separateAt : PreferredTileWidth;
        var content = ContentWidth(windowWidth);
        var wrapped = (int)((content + TileGap) / (limit + TileGap));
        return ClampColumns(windowWidth, wrapped);
    }

    public static int ColumnsFromWidth(float windowWidth)
    {
        return ColumnsFor(windowWidth, 0, 0f);
    }

    /// <summary>
    /// Width of one internal control column for the given count. The Master
    /// tile itself spans <see cref="ContentWidth"/>.
    /// </summary>
    public static float TileWidthFor(float windowWidth, int columns)
    {
        if (columns < 1)
            columns = 1;
        var content = ContentWidth(windowWidth);
        var width = (content - TileGap * (columns - 1)) / columns;
        if (width < MinTileWidth)
            width = MinTileWidth;
        return width;
    }

    static int ClampColumns(float windowWidth, int requested)
    {
        var content = ContentWidth(windowWidth);
        var cap = (int)((content + TileGap) / (MinTileWidth + TileGap));
        if (cap < MinColumns)
            cap = MinColumns;
        if (cap > MaxColumns)
            cap = MaxColumns;
        if (requested < MinColumns)
            requested = MinColumns;
        if (requested > cap)
            requested = cap;
        return requested;
    }

    public static float RowHeight(float tileHeight, bool header, bool subheader)
    {
        var chrome = ScrollSlack;
        if (header)
            chrome += HeaderHeight;
        if (subheader)
            chrome += SubheaderHeight;
        return tileHeight + chrome;
    }

    public static bool NearlyEqual(Vector2 a, Vector2 b)
    {
        return Math.Abs(a.X - b.X) < 0.5f && Math.Abs(a.Y - b.Y) < 0.5f;
    }
}
