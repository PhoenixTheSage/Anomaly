using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using VRage.Render11.Common;
using VRage.Render11.Resources;
using VRageRender;

namespace ClientPlugin.Buffers;

/// <summary>
/// DX11 image allocation for the catalog. Packs call the same method names
/// on the core assembly; the core forwards here when the renderer is DX11.
/// </summary>
public static class CatalogImages
{
    sealed class ColorTarget
    {
        public IRtvTexture Texture;
        public int Width;
        public int Height;
        public int Format;
    }

    static readonly Dictionary<string, ColorTarget> Targets = new Dictionary<string, ColorTarget>(StringComparer.Ordinal);

    public static void PublishRgba8(PublishedBuffer target, byte[] rgba, int width, int height)
    {
        if (target == null || rgba == null || width <= 0 || height <= 0)
            return;
        var texture = CreateTexture2D(width, height, Format.R8G8B8A8_UNorm, rgba, width * 4);
        var srv = new ShaderResourceView(MyRender11.DeviceInstance, texture);
        target.Publish(srv, texture.NativePointer, width, height);
    }

    public static void PublishVolumeRgba8(PublishedBuffer target, byte[] rgba, int width, int height, int depth)
    {
        if (target == null || rgba == null || width <= 0 || height <= 0 || depth <= 0)
            return;
        var texture = CreateTexture3D(width, height, depth, new[] { rgba });
        var srv = new ShaderResourceView(MyRender11.DeviceInstance, texture);
        target.Publish3D(srv, texture.NativePointer, width, height, depth);
    }

    public static void PublishVolumeMips(PublishedBuffer target, byte[][] mips, int dim)
    {
        if (target == null || mips == null || mips.Length == 0 || dim <= 0)
            return;
        var texture = CreateTexture3D(dim, dim, dim, mips);
        var srv = new ShaderResourceView(MyRender11.DeviceInstance, texture);
        target.Publish3D(srv, texture.NativePointer, dim, dim, dim);
    }

    public static void PublishStructuredFloats(PublishedBuffer target, float[] values, int stride)
    {
        if (target == null || values == null || values.Length == 0 || stride <= 0)
            return;
        int bytes = values.Length * sizeof(float);
        var desc = new BufferDescription
        {
            SizeInBytes = bytes,
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            OptionFlags = ResourceOptionFlags.BufferStructured,
            StructureByteStride = stride,
        };
        var buffer = new SharpDX.Direct3D11.Buffer(MyRender11.DeviceInstance, desc);
        var handle = GCHandle.Alloc(values, GCHandleType.Pinned);
        try
        {
            var box = new DataBox(handle.AddrOfPinnedObject(), bytes, 0);
            MyRender11.DeviceInstance.ImmediateContext.UpdateSubresource(box, buffer, 0);
        }
        finally
        {
            handle.Free();
        }

        var srv = new ShaderResourceView(MyRender11.DeviceInstance, buffer);
        target.PublishStructured(srv, buffer.NativePointer, values.Length, stride);
    }

    public static bool EnsureColorTarget(string packId, string name, int width, int height, int format)
    {
        if (string.IsNullOrWhiteSpace(name) || width <= 0 || height <= 0)
            return false;
        var dxgi = format == 1 ? Format.R16G16B16A16_Float : Format.R8G8B8A8_UNorm;
        if (!Targets.TryGetValue(name, out var target) || target.Texture == null || target.Width != width || target.Height != height || target.Format != format)
        {
            target = new ColorTarget
            {
                Texture = MyManagers.RwTextures.CreateRtv("Anomaly.Catalog." + name, width, height, dxgi),
                Width = width,
                Height = height,
                Format = format,
            };
            Targets[name] = target;
        }

        var published = new PublishedBuffer();
        var resource = target.Texture.Resource;
        published.Publish(target.Texture, resource != null ? resource.NativePointer : IntPtr.Zero, width, height);
        BufferCatalog.Publish(packId ?? "anomaly", name, published);
        return true;
    }

    public static void ClearColorTarget(string name, float r, float g, float b, float a)
    {
        if (name == null || !Targets.TryGetValue(name, out var target) || target.Texture == null)
            return;
        MyRender11.RC.ClearRtv(target.Texture, new RawColor4(r, g, b, a));
    }

    static Texture2D CreateTexture2D(int width, int height, Format format, byte[] rgba, int rowPitch)
    {
        var desc = new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.ShaderResource,
        };
        var handle = GCHandle.Alloc(rgba, GCHandleType.Pinned);
        try
        {
            var data = new DataRectangle(handle.AddrOfPinnedObject(), rowPitch);
            return new Texture2D(MyRender11.DeviceInstance, desc, new[] { data });
        }
        finally
        {
            handle.Free();
        }
    }

    static Texture3D CreateTexture3D(int width, int height, int depth, byte[][] mips)
    {
        var desc = new Texture3DDescription
        {
            Width = width,
            Height = height,
            Depth = depth,
            MipLevels = mips.Length,
            Format = Format.R8G8B8A8_UNorm,
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.ShaderResource,
        };
        var handles = new GCHandle[mips.Length];
        var boxes = new DataBox[mips.Length];
        int sizeW = width;
        int sizeH = height;
        int sizeD = depth;
        try
        {
            for (int i = 0; i < mips.Length; i++)
            {
                handles[i] = GCHandle.Alloc(mips[i], GCHandleType.Pinned);
                boxes[i] = new DataBox(handles[i].AddrOfPinnedObject(), sizeW * 4, sizeW * sizeH * 4);
                sizeW = Math.Max(sizeW / 2, 1);
                sizeH = Math.Max(sizeH / 2, 1);
                sizeD = Math.Max(sizeD / 2, 1);
            }

            return new Texture3D(MyRender11.DeviceInstance, desc, boxes);
        }
        finally
        {
            for (int i = 0; i < handles.Length; i++)
                if (handles[i].IsAllocated)
                    handles[i].Free();
        }
    }
}
