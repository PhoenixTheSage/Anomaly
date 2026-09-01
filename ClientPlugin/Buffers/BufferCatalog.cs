using System;
using System.Collections.Generic;
using ClientPlugin.Shaders;
using ClientPlugin.Velocity;

namespace ClientPlugin.Buffers;

/// <summary>
/// Well-known type for consumers. Resolve by name:
/// <c>ClientPlugin.Buffers.BufferCatalog</c> — do not take a compile-time
/// reference to Anomaly. <see cref="Active"/> looks up a named buffer
/// (<c>velocity</c>, <c>linearDepth</c>, <c>hiZ</c>, <c>historyColor</c>,
/// <c>objectId</c>). <c>velocity</c> aliases
/// <see cref="VelocityRegistry.Active"/>.
/// </summary>
public static class BufferCatalog
{
    public const string Velocity = "velocity";
    public const string LinearDepth = "linearDepth";
    public const string ObjectId = "objectId";
    public const string HistoryColor = "historyColor";
    public const string HiZ = "hiZ";

    static readonly object Gate = new();
    static readonly Dictionary<string, ISharedBuffer> ByName =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Current producer for <paramref name="name"/>, or an unavailable
    /// placeholder. Never returns null.
    /// </summary>
    public static ISharedBuffer Active(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return UnavailableSharedBuffer.Instance;

        lock (Gate)
        {
            if (ByName.TryGetValue(name, out var published) && published != null)
                return published;
        }

        if (string.Equals(name, Velocity, StringComparison.OrdinalIgnoreCase))
            return VelocitySharedBuffer.Wrap(VelocityRegistry.Active);

        if (GBufferAttachments.TryGet(name, out var attachment))
            return new AttachmentSharedBuffer(attachment);

        return UnavailableSharedBuffer.Instance;
    }

    /// <summary>
    /// Publish or replace a named buffer. Pass null to clear.
    /// </summary>
    public static void Set(string name, ISharedBuffer buffer)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        lock (Gate)
        {
            if (buffer == null)
                ByName.Remove(name);
            else
                ByName[name] = buffer;
        }
    }

    internal static void PublishVelocity(IVelocityBuffer buffer)
    {
        Set(Velocity, VelocitySharedBuffer.Wrap(buffer));
    }
}

sealed class CatalogTexture : ISharedBuffer
{
    object srv;
    IntPtr nativeResource;
    int width;
    int height;

    public bool IsAvailable => srv != null && nativeResource != IntPtr.Zero && width > 0 && height > 0;
    public object Srv => srv;
    public IntPtr NativeResource => nativeResource;
    public int Width => width;
    public int Height => height;

    public void Clear()
    {
        srv = null;
        nativeResource = IntPtr.Zero;
        width = 0;
        height = 0;
    }

    public void Publish(object srvBindable, IntPtr resource, int w, int h)
    {
        srv = srvBindable;
        nativeResource = resource;
        width = w;
        height = h;
    }
}

sealed class UnavailableSharedBuffer : ISharedBuffer
{
    public static readonly UnavailableSharedBuffer Instance = new();

    UnavailableSharedBuffer()
    {
    }

    public bool IsAvailable => false;
    public object Srv => null;
    public IntPtr NativeResource => IntPtr.Zero;
    public int Width => 0;
    public int Height => 0;
}

sealed class VelocitySharedBuffer : ISharedBuffer
{
    readonly IVelocityBuffer inner;

    VelocitySharedBuffer(IVelocityBuffer inner)
    {
        this.inner = inner;
    }

    public static ISharedBuffer Wrap(IVelocityBuffer buffer)
    {
        if (buffer == null)
            return UnavailableSharedBuffer.Instance;
        return new VelocitySharedBuffer(buffer);
    }

    public bool IsAvailable => inner != null && inner.IsAvailable;
    public object Srv => inner?.Srv;
    public IntPtr NativeResource => inner != null ? inner.NativeResource : IntPtr.Zero;
    public int Width => inner != null ? inner.Width : 0;
    public int Height => inner != null ? inner.Height : 0;
}

sealed class AttachmentSharedBuffer : ISharedBuffer
{
    readonly GBufferAttachment inner;

    public AttachmentSharedBuffer(GBufferAttachment inner)
    {
        this.inner = inner;
    }

    public bool IsAvailable => inner != null && inner.Srv != null && inner.NativeResource != IntPtr.Zero;
    public object Srv => inner?.Srv;
    public IntPtr NativeResource => inner != null ? inner.NativeResource : IntPtr.Zero;
    public int Width => 0;
    public int Height => 0;
}
