using System;

namespace ClientPlugin.Buffers;

/// <summary>
/// Named GPU buffer for consumers that are not velocity-specific.
/// Resolve by well-known type name; do not take a compile-time reference.
/// </summary>
public interface ISharedBuffer
{
    bool IsAvailable { get; }

    /// <summary>
    /// Keen <c>ISrvBindable</c>, or null when unavailable.
    /// </summary>
    object Srv { get; }

    /// <summary>ID3D11Resource pointer, or <see cref="IntPtr.Zero"/> when unavailable.</summary>
    IntPtr NativeResource { get; }

    int Width { get; }

    int Height { get; }

    /// <summary>
    /// Texture3D depth, or 1 for 2D / structured. 0 is treated as 1.
    /// Slice AO: brick atlases need this; Width/Height alone is not enough.
    /// </summary>
    int Depth { get; }

    /// <summary>DXGI format enum as int, or 0 when unknown.</summary>
    int Format { get; }
}
