using System;
using System.Collections.Generic;
using ClientPlugin.Backend;

namespace ClientPlugin.Buffers;

public static class CatalogImages
{
    public static bool EnsureColorTarget(string packId, string name, int width, int height, int format)
    {
        if (RendererRoute.UseDx11Device)
            return Dx11Call.Try(Dx, "EnsureColorTarget", new object[] { packId, name, width, height, format }, out var result) && result is true;
        var buffer = new PublishedBuffer();
        buffer.PublishRgba8(new byte[Math.Max(width, 1) * Math.Max(height, 1) * 4], Math.Max(width, 1), Math.Max(height, 1));
        BufferCatalog.Publish(packId ?? "anomaly", name, buffer);
        return RendererRoute.Pending == RendererRoute.Active || buffer.ImageId != 0;
    }

    public static void ClearColorTarget(string name, float r, float g, float b, float a)
    {
        if (RendererRoute.RefuseDx11Device("CatalogImages.ClearColorTarget"))
            return;
        Dx11Call.Call(Dx, "ClearColorTarget", name, r, g, b, a);
    }

    const string Dx = "ClientPlugin.Buffers.CatalogImages";
}

public sealed class GuestRendererImage
{
    public GuestRendererImage(ulong imageId, int width, int height, int depth)
    {
        ImageId = imageId;
        Width = width;
        Height = height;
        Depth = depth < 1 ? 1 : depth;
    }

    public ulong ImageId { get; }
    public bool IsAvailable => ImageId != 0;
    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }
}

public static class BufferCatalog
{
    public const string HdrColor = "hdrColor";
    public const string UpscaledColor = "upscaledColor";
    static readonly Dictionary<string, object> Local = new Dictionary<string, object>(StringComparer.Ordinal);
    static readonly Dictionary<string, Action[]> Lifetimes = new Dictionary<string, Action[]>(StringComparer.OrdinalIgnoreCase);

    public static void RegisterLifetime(string packId, Action onResolutionChanged, Action onDeviceEnd)
    {
        if (string.IsNullOrWhiteSpace(packId)) return;
        lock (Local) Lifetimes[packId] = new[] { onResolutionChanged, onDeviceEnd };
        if (RendererRoute.UseDx11Device)
            Dx11Call.Call(Dx, "RegisterLifetime", packId, onResolutionChanged, onDeviceEnd);
    }

    public static void UnregisterLifetime(string packId)
    {
        if (string.IsNullOrWhiteSpace(packId)) return;
        lock (Local) Lifetimes.Remove(packId);
        if (RendererRoute.UseDx11Device) Dx11Call.Call(Dx, "UnregisterLifetime", packId);
    }

    public static void Set(string name, object buffer)
    {
        if (string.IsNullOrEmpty(name))
            return;
        lock (Local)
        {
            if (buffer == null)
                Local.Remove(name);
            else
                Local[name] = buffer;
        }
    }

    public static object Active(string name)
    {
        if (RendererRoute.UseDx11Device && Dx11Call.Try(Dx, "Active", new object[] { name }, out var result) && result != null)
            return result;
        lock (Local)
            return Local.TryGetValue(name ?? "", out var value) ? value : null;
    }

    public static bool Publish(string packId, string name, object buffer)
    {
        lock (Local)
            Local[name ?? ""] = buffer;
        if (!RendererRoute.UseDx11Device)
            return true;
        object inner = buffer is PublishedBuffer published ? published.Inner ?? published : buffer;
        return Dx11Call.Try(Dx, "Publish", new object[] { packId, name, inner }, out var result) && result is true;
    }

    public static void Unpublish(string packId, string name)
    {
        lock (Local)
            Local.Remove(name ?? "");
        if (RendererRoute.UseDx11Device)
            Dx11Call.Call(Dx, "Unpublish", packId, name);
    }

    public static void UnpublishAll(string packId)
    {
        if (RendererRoute.RefuseDx11Device("BufferCatalog.UnpublishAll"))
            return;
        Dx11Call.Call(Dx, "UnpublishAll", packId);
    }

    public static void RequestLitMips(int mipLevels)
    {
        if (RendererRoute.RefuseDx11Device("BufferCatalog.RequestLitMips"))
            return;
        Dx11Call.Call(Dx, "RequestLitMips", mipLevels);
    }

    public static void RequestPointShadows(int maxLights, int faceResolution)
    {
        if (RendererRoute.RefuseDx11Device("BufferCatalog.RequestPointShadows"))
            return;
        Dx11Call.Call(Dx, "RequestPointShadows", maxLights, faceResolution);
    }

    public static void RequestOccupancy(bool enabled)
    {
        if (RendererRoute.RefuseDx11Device("BufferCatalog.RequestOccupancy"))
            return;
        Dx11Call.Call(Dx, "RequestOccupancy", enabled);
    }

    internal static void ApplyDeferred()
    {
        PublishedBuffer[] pending;
        KeyValuePair<string, Action[]>[] lifetimes;
        lock (Local)
        {
            lifetimes = new KeyValuePair<string, Action[]>[Lifetimes.Count];
            int registration = 0;
            foreach (var pair in Lifetimes) lifetimes[registration++] = pair;
            pending = new PublishedBuffer[Local.Count];
            int index = 0;
            foreach (var value in Local.Values)
            {
                if (value is PublishedBuffer buffer)
                    pending[index++] = buffer;
            }

            if (index != pending.Length)
                Array.Resize(ref pending, index);
        }

        if (RendererRoute.UseDx11Device)
            foreach (var pair in lifetimes)
                Dx11Call.Call(Dx, "RegisterLifetime", pair.Key, pair.Value[0], pair.Value[1]);

        foreach (var buffer in pending)
            buffer.ApplyDeferred();
    }

    const string Dx = "ClientPlugin.Buffers.BufferCatalog";
}

public sealed class PublishedBuffer
{
    public object Srv
    {
        get
        {
            if (RendererRoute.RefuseDx11Device("PublishedBuffer.Srv"))
                return null;
            return Inner == null ? null : Dx11Call.CallInstance(Inner, "get_Srv") ?? Read("Srv");
        }
    }

    public IntPtr NativeResource { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Depth { get; private set; } = 1;
    public byte[] Rgba { get; private set; }
    public ulong ImageId { get; private set; }
    internal object Inner { get; private set; }
    byte[][] mipData;
    float[] structured;
    int structuredStride;
    int uploadKind;
    bool deviceApplied;

    public PublishedBuffer() => EnsureInner();

    public void Publish(object srv, IntPtr native, int width, int height)
    {
        if (RendererRoute.RefuseDx11Device("PublishedBuffer.Publish"))
            return;
        EnsureInner();
        NativeResource = native;
        Width = width;
        Height = height;
        uploadKind = 0;
        if (Inner != null)
            Dx11Call.CallInstance(Inner, "Publish", srv, native, width, height);
    }

    public void Publish3D(object srv, IntPtr native, int width, int height, int depth)
    {
        if (RendererRoute.RefuseDx11Device("PublishedBuffer.Publish3D"))
            return;
        EnsureInner();
        NativeResource = native;
        Width = width;
        Height = height;
        Depth = depth;
        uploadKind = 0;
        if (Inner != null)
            Dx11Call.CallInstance(Inner, "Publish3D", srv, native, width, height, depth);
    }

    public void PublishRgba8(byte[] rgba, int width, int height)
    {
        Rgba = rgba;
        Width = width;
        Height = height;
        Depth = 1;
        uploadKind = 1;
        deviceApplied = false;
        ApplyDeferred();
    }

    public void PublishVolumeRgba8(byte[] rgba, int width, int height, int depth)
    {
        Rgba = rgba;
        Width = width;
        Height = height;
        Depth = depth;
        uploadKind = 2;
        deviceApplied = false;
        ApplyDeferred();
    }

    public void PublishVolumeMips(byte[][] mips, int dim)
    {
        mipData = mips;
        Rgba = mips != null && mips.Length > 0 ? mips[0] : null;
        Width = dim;
        Height = dim;
        Depth = dim;
        uploadKind = 3;
        deviceApplied = false;
        ApplyDeferred();
    }

    public void PublishStructuredFloats(float[] values, int stride)
    {
        structured = values;
        structuredStride = stride;
        Width = values?.Length ?? 0;
        Height = 1;
        uploadKind = 4;
        deviceApplied = false;
        ApplyDeferred();
    }

    public void Clear()
    {
        if (Inner != null)
            Dx11Call.CallInstance(Inner, "Clear");
        Rgba = null;
        mipData = null;
        structured = null;
        Width = 0;
        Height = 0;
        Depth = 1;
        NativeResource = IntPtr.Zero;
        uploadKind = 0;
        deviceApplied = false;
        ImageId = 0;
    }

    internal void ApplyDeferred()
    {
        if (deviceApplied || uploadKind == 0)
            return;
        if (RendererRoute.UseDx11Device)
        {
            EnsureInner();
            if (Inner == null)
                return;
            if (uploadKind == 1)
                Dx11Call.CallInstance(Inner, "PublishRgba8", Rgba, Width, Height);
            else if (uploadKind == 2)
                Dx11Call.CallInstance(Inner, "PublishVolumeRgba8", Rgba, Width, Height, Depth);
            else if (uploadKind == 3)
                Dx11Call.CallInstance(Inner, "PublishVolumeMips", mipData, Width);
            else if (uploadKind == 4)
                Dx11Call.CallInstance(Inner, "PublishStructuredFloats", structured, structuredStride);
            deviceApplied = true;
            return;
        }

        if (!RendererRoute.UseGuestImages || uploadKind == 4)
            return;
        if (uploadKind == 3)
            ImageId = GuestBridgeClient.AllocateVolumeMips(Math.Max(Width, 1), mipData);
        else
        {
            int depth = uploadKind == 1 ? 1 : Depth;
            ImageId = GuestBridgeClient.AllocateImage(Math.Max(Width, 1), Math.Max(Height, 1), Math.Max(depth, 1), Rgba);
        }
        deviceApplied = ImageId != 0;
    }

    void EnsureInner()
    {
        if (Inner != null || !RendererRoute.UseDx11Device || !Dx11Call.Ready)
            return;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(assembly.GetName().Name, "Anomaly.Dx11", StringComparison.Ordinal))
                continue;
            var type = assembly.GetType("ClientPlugin.Buffers.PublishedBuffer");
            if (type != null)
                Inner = Activator.CreateInstance(type);
            return;
        }
    }

    object Read(string property)
    {
        var info = Inner?.GetType().GetProperty(property);
        return info?.GetValue(Inner);
    }
}
