namespace ClientPlugin;

/// <summary>
/// Developer probes for the GBuffer velocity path. These deliberately replace
/// real motion vectors while enabled.
/// </summary>
public enum VelocityProbe
{
    Off = 0,
    /// <summary>Known +X clear value; geometry should overwrite it if Target3 is live.</summary>
    TargetClear = 1,
    /// <summary>
    /// Clears to -X, then emits +X at the active GBuffer pixel output. Cyan means
    /// no Target3 write, gray means an explicit zero, and pink proves the MRT path.
    /// </summary>
    MrtWrite = 2,
    /// <summary>+X for a previous-world hit; +Y for a miss.</summary>
    HistoryCoverage = 3,
    /// <summary>
    /// Known +X clear issued on the immediate context after the render
    /// scheduler has executed every deferred geometry command list.
    /// </summary>
    PassEndClear = 4
}
