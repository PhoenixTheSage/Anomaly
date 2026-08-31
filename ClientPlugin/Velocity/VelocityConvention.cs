using System;

namespace ClientPlugin.Velocity;

/// <summary>
/// How <see cref="IVelocityBuffer"/> stores motion. Combine flags; the built-in producer
/// always sets all three once a buffer exists.
/// </summary>
/// <remarks>
/// Units are <b>pixel delta</b> at internal (DRS) resolution. Y is down in D3D (top of
/// the render target is v = 0). The buffer is unjittered: Halton / TAA offsets are a
/// consumer problem, not part of this texture.
/// </remarks>
[Flags]
public enum VelocityConvention
{
    None = 0,

    /// <summary>Encoded without temporal jitter in the current or previous view-projection.</summary>
    Unjittered = 1,

    /// <summary>XY is a pixel-space delta, not NDC or UV.</summary>
    PixelSpace = 2,

    /// <summary>Width/height match Keen internal render resolution (<c>ResolutionI</c>), not the swapchain.</summary>
    MatchesRenderResolution = 4
}
