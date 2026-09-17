using System;
using VRageMath;

namespace ClientPlugin.RichHud;

/// <summary>
/// Fluent builder for one page under the Rich HUD root <c>Anomaly Shaders</c>
/// (sibling of the <c>Anomaly</c> folder, or a child of a folder from
/// <see cref="TerminalConfigRegistry.RequestFolderPage"/>).
/// Resolve <see cref="TerminalConfigRegistry"/> by well-known type name; do not
/// take a compile-time reference to Anomaly or RichHudFramework.
/// <para>
/// Optional parameters are explicit overloads so pack reflection can match
/// arity without default-argument padding. Tiles pack horizontally: color
/// pickers occupy a tile alone, sliders and dropdowns pair, checkboxes and
/// buttons stack up to three. Master’s category is one horizontal scroller;
/// Anomaly wraps extra tiles onto new category rows from the terminal width.
/// </para>
/// </summary>
public interface ITerminalConfigPage
{
    string Title { get; }

    ITerminalConfigPage Category(string header);

    ITerminalConfigPage Category(string header, string subheader);

    ITerminalConfigPage Label(string text);

    /// <summary>
    /// Status line. <paramref name="get"/> is applied at mount and again on
    /// <see cref="Refresh"/>. Do not use <c>CustomValueGetter</c>.
    /// </summary>
    ITerminalConfigPage Label(string text, Func<string> get);

    ITerminalConfigPage Checkbox(string label, Func<bool> get, Action<bool> set, string description);

    /// <param name="enabled">When provided and false, the checkbox stays visible
    /// but the setter is ignored (and <c>Enabled</c> is false when Rich HUD greys it out).</param>
    ITerminalConfigPage Checkbox(string label, Func<bool> get, Action<bool> set, string description, Func<bool> enabled);

    ITerminalConfigPage Slider(string label, float min, float max, Func<float> get, Action<float> set, string description);

    ITerminalConfigPage Slider(string label, float min, float max, Func<float> get, Action<float> set, string description, float step);

    ITerminalConfigPage IntSlider(string label, int min, int max, Func<int> get, Action<int> set, string description);

    ITerminalConfigPage IntSlider(string label, int min, int max, Func<int> get, Action<int> set, string description, int step);

    ITerminalConfigPage Dropdown(string label, Type enumType, Func<object> get, Action<object> set, string description);

    ITerminalConfigPage Dropdown<T>(string label, Func<T> get, Action<T> set, string description);

    ITerminalConfigPage Button(string label, Action click, string description);

    ITerminalConfigPage Color(string label, Func<Color> get, Action<Color> set, string description);

    /// <summary>
    /// Pushes current getter values into mounted controls. Host pages never
    /// use <c>CustomValueGetter</c> (Master assigns it every HandleInput tick;
    /// dropdown getters return client <c>EntryData</c> that cannot be Master's
    /// <c>ListBoxEntry</c>). Call after a dropdown or button writes several
    /// fields. Dropdown setters already pull sibling controls on this page,
    /// including other dropdowns.
    /// </summary>
    ITerminalConfigPage Refresh();
}
