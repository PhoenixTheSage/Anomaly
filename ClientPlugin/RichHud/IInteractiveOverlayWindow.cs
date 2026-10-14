using System;

namespace ClientPlugin.RichHud;

/// <summary>Version 1 pack boundary. Only CLR primitives, arrays and delegates cross the plugin boundary.
/// Coordinates are viewport-local HUD pixels, centered with Y up. Lines contain x1,y1,x2,y2,r,g,b,a;
/// triangles contain x1,y1,x2,y2,x3,y3,r,g,b,a. Counts are primitive counts, not float counts.
/// Submit copies caller buffers. Input events: 0=click, 1=orbit delta, 2=pan delta, 3=wheel steps.
/// Callbacks run on the HUD thread: enqueue commands; never read entities or write config there.</summary>
public interface IInteractiveOverlayWindow
{
    bool Available { get; }
    bool Visible { get; }
    void SetVisible(bool visible);
    void SetBinding(int key, bool ctrl, bool alt, bool shift);
    void SetHandlers(Action<int, float, float, float> input, Action<float, float> resize,
        Action<long> select, Action<bool> visibility);
    void Button(string text, Action click);
    void Checkbox(string text, Func<bool> get, Action<bool> set, Func<bool> enabled);
    void Slider(string text, float min, float max, Func<float> get, Action<float> set);
    void Refresh();
    void SubmitGeometry(float[] lines, int lineCount, float[] triangles, int triangleCount,
        string[] labels, float[] labelPositions);
    void SubmitRows(long[] ids, string[] rows, string details, string status);
}
