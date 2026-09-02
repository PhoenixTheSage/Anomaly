namespace ClientPlugin;

/// <summary>
/// Fullscreen overlay of a catalog texture after <c>DrawGameScene</c>.
/// </summary>
public enum DebugBuffer
{
    Off,
    Velocity,
    LinearDepth,
    HistoryColor,
    HiZ,
    ReactiveMask,
    FullscreenIsolated
}
