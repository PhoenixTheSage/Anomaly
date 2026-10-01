using System;
using RichHudFramework.UI;
using VRageMath;

namespace ClientPlugin.RichHud;

/// <summary>
/// <see cref="WindowBase"/> subclass that publishes resize feedback. Master
/// does not fire a resize event; it polls <c>resizeDir</c> in
/// <see cref="WindowBase.HandleInput"/>. Anomaly-owned HUD windows should
/// subclass this instead of <see cref="WindowBase"/>. The shared terminal
/// window is Master’s — <see cref="TerminalWindowMonitor"/> Harmony-patches
/// that <c>HandleInput</c> and feeds <see cref="TerminalConfigRegistry"/>.
/// </summary>
public abstract class ResizableWindow : WindowBase
{
    /// <summary>
    /// Size changed while the user is dragging an edge. HUD input thread.
    /// Do not serialize or write a <c>.cfg</c>.
    /// </summary>
    public event Action<Vector2> Resizing;

    /// <summary>
    /// Drag ended after the size changed (mouse-up). HUD input thread.
    /// Do not serialize or write a <c>.cfg</c>.
    /// </summary>
    public event Action<Vector2> Resized;

    Vector2 lastSize;
    bool wasResizing;
    bool sizeChangedDuringDrag;

    protected ResizableWindow(HudParentBase parent) : base(parent)
    {
        lastSize = Size;
    }

    protected override void HandleInput(Vector2 cursorPos)
    {
        base.HandleInput(cursorPos);

        var size = Size;
        var resizing = resizeDir != Vector2.Zero;
        if (!TerminalWindowLayout.NearlyEqual(size, lastSize))
        {
            lastSize = size;
            sizeChangedDuringDrag = true;
            TryInvoke(Resizing, size);
        }

        if (wasResizing && !resizing && sizeChangedDuringDrag)
        {
            sizeChangedDuringDrag = false;
            TryInvoke(Resized, size);
        }

        wasResizing = resizing;
    }

    static void TryInvoke(Action<Vector2> handler, Vector2 size)
    {
        if (handler == null)
            return;
        try
        {
            handler(size);
        }
        catch (Exception e)
        {
            DebugLog.Write("ResizableWindow resize handler: " + e);
        }
    }
}
