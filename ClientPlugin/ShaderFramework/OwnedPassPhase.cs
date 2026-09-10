namespace ClientPlugin.Shaders;

/// <summary>
/// When a C# owned pass runs relative to data-driven
/// <see cref="FullscreenPassRegistry"/> programs on the same slot.
/// Resolve by name: <c>ClientPlugin.Shaders.OwnedPassPhase</c>.
/// </summary>
public enum OwnedPassPhase
{
    /// <summary>
    /// Runs before Anomaly draws <c>Fullscreen/</c> programs. Use to
    /// publish catalog textures the PS will sample (for example LBuffer mips).
    /// </summary>
    BeforeFullscreen = 0,

    /// <summary>
    /// Default. Runs after data-driven programs (existing Register behavior).
    /// </summary>
    AfterFullscreen = 1
}
