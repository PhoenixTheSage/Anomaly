namespace ClientPlugin;

/// <summary>
/// Fullscreen overlay of a catalog texture after <c>DrawGameScene</c>.
/// </summary>
public enum DebugBuffer
{
    Off,
    Velocity,
    /// <summary>Raw SV_Target3 before camera/depth gap fill.</summary>
    GBufferVelocityRaw,
    /// <summary>
    /// Six-panel developer proof of Target3, pixel execution, PS b7, incoming
    /// VS velocity, and a known-good GBuffer0 lane from the same geometry draw.
    /// </summary>
    VelocityPipelineAudit,
    LinearDepth,
    HistoryColor,
    HiZ,
    ReactiveMask,
    FullscreenIsolated,
    /// <summary>Unique upscale dest published by <c>NotifyUpscaleComplete</c>.</summary>
    UpscaledColor
}
