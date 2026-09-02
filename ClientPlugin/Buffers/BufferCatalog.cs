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
/// <c>reactiveMask</c>, <c>fullscreenIsolated</c>, <c>objectId</c>). <c>velocity</c> aliases
/// <see cref="VelocityRegistry.Active"/>. Packs publish extras with
/// <see cref="Publish"/>; reserved names fail closed.
/// </summary>
public static class BufferCatalog
{
    public const string Velocity = "velocity";
    public const string LinearDepth = "linearDepth";
    public const string ObjectId = "objectId";
    public const string HistoryColor = "historyColor";
    public const string HiZ = "hiZ";
    public const string ReactiveMask = "reactiveMask";
    public const string FullscreenIsolated = "fullscreenIsolated";

    static readonly object Gate = new();
    static readonly Dictionary<string, ISharedBuffer> ByName =
        new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string> Owners =
        new(StringComparer.OrdinalIgnoreCase);
    static readonly string[] Reserved =
    {
        Velocity, LinearDepth, HiZ, HistoryColor, ReactiveMask, FullscreenIsolated
    };

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
    /// Anomaly internals use this; packs should prefer <see cref="Publish"/>.
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

    /// <summary>
    /// Pack-owned catalog entry. Reserved names
    /// (<c>velocity</c>, <c>linearDepth</c>, <c>hiZ</c>, <c>historyColor</c>,
    /// <c>reactiveMask</c>, <c>fullscreenIsolated</c>) fail closed. Same name
    /// from two pack ids fails closed.
    /// </summary>
    public static bool Publish(string packId, string name, ISharedBuffer buffer)
    {
        if (string.IsNullOrWhiteSpace(packId) || string.IsNullOrWhiteSpace(name) || buffer == null)
            return false;
        packId = packId.Trim();
        name = name.Trim();
        if (IsReservedName(name))
            return false;
        lock (Gate)
        {
            if (Owners.TryGetValue(name, out var owner) &&
                !string.Equals(owner, packId, StringComparison.OrdinalIgnoreCase))
                return false;
            Owners[name] = packId;
            ByName[name] = buffer;
            return true;
        }
    }

    public static void Unpublish(string packId, string name)
    {
        if (string.IsNullOrWhiteSpace(packId) || string.IsNullOrWhiteSpace(name))
            return;
        lock (Gate)
        {
            if (!Owners.TryGetValue(name, out var owner) ||
                !string.Equals(owner, packId, StringComparison.OrdinalIgnoreCase))
                return;
            Owners.Remove(name);
            ByName.Remove(name);
        }
    }

    public static void UnpublishAll(string packId)
    {
        if (string.IsNullOrWhiteSpace(packId))
            return;
        lock (Gate)
        {
            var drop = new List<string>();
            foreach (var kv in Owners)
            {
                if (string.Equals(kv.Value, packId, StringComparison.OrdinalIgnoreCase))
                    drop.Add(kv.Key);
            }

            for (var i = 0; i < drop.Count; i++)
            {
                Owners.Remove(drop[i]);
                ByName.Remove(drop[i]);
            }
        }
    }

    /// <summary>
    /// DRS / device-end callbacks so a pack can drop its own
    /// <c>OnDeviceReset</c> Harmony patch.
    /// </summary>
    public static void RegisterLifetime(string packId, Action onResolutionChanged, Action onDeviceEnd)
    {
        BufferCatalogLifetime.Register(packId, onResolutionChanged, onDeviceEnd);
    }

    public static void UnregisterLifetime(string packId)
    {
        BufferCatalogLifetime.Unregister(packId);
    }

    public static bool IsReservedName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;
        for (var i = 0; i < Reserved.Length; i++)
        {
            if (string.Equals(name, Reserved[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    internal static void PublishVelocity(IVelocityBuffer buffer)
    {
        Set(Velocity, VelocitySharedBuffer.Wrap(buffer));
    }
}

/// <summary>
/// Pack-owned catalog texture. Pass to <see cref="BufferCatalog.Publish"/>.
/// Resolve by name: <c>ClientPlugin.Buffers.PublishedBuffer</c>.
/// </summary>
public sealed class PublishedBuffer : ISharedBuffer
{
    public bool IsAvailable => Srv != null && NativeResource != IntPtr.Zero && Width > 0 && Height > 0;
    public object Srv { get; private set; }
    public IntPtr NativeResource { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public PublishedBuffer()
    {
    }

    public PublishedBuffer(object srv, IntPtr nativeResource, int width, int height)
    {
        Publish(srv, nativeResource, width, height);
    }

    public void Publish(object srv, IntPtr nativeResource, int width, int height)
    {
        Srv = srv;
        NativeResource = nativeResource;
        Width = width;
        Height = height;
    }

    public void Clear()
    {
        Srv = null;
        NativeResource = IntPtr.Zero;
        Width = 0;
        Height = 0;
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
