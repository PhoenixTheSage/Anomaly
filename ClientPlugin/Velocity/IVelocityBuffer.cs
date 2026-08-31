using System;

namespace ClientPlugin.Velocity;

/// <summary>
/// Shared velocity texture for this frame. Consumers bind <see cref="Srv"/> or
/// <see cref="NativeResource"/> before the first draw that needs motion vectors.
/// </summary>
public interface IVelocityBuffer
{
    bool IsAvailable { get; }

    /// <summary>
    /// Keen <c>ISrvBindable</c> for the velocity RT, or null when unavailable.
    /// Typed as object so Pulsar from-source builds do not expose game internals on this contract.
    /// </summary>
    object Srv { get; }

    /// <summary>ID3D11Resource pointer, or <see cref="IntPtr.Zero"/> when unavailable.</summary>
    IntPtr NativeResource { get; }

    int Width { get; }

    int Height { get; }

    VelocityConvention Convention { get; }

    /// <summary>False on the first frame, after a camera cut, or when actor history is empty.</summary>
    bool HistoryValid { get; }
}
