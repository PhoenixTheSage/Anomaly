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
}
