using System;

namespace ClientPlugin.ShaderFramework;

/// <summary>CPU setters queue art changes; the caller applies and retires GPU resources on its render path.</summary>
internal sealed class QueuedArtResource<T> where T : class, IDisposable
{
    readonly Action<T> retire;
    internal T Resource { get; private set; }
    internal Upload Pending { get; private set; }

    internal QueuedArtResource(Action<T> retire) => this.retire = retire;

    internal void Set(int width, int height, int slices, byte[] rgba)
    {
        if (rgba == null || width <= 0 || height <= 0 || slices <= 0)
        {
            Clear();
            return;
        }
        // Validate before changing either the pending request or the live resource.
        if (width > 2048 || height > 2048 || slices > 64)
            throw new ArgumentException("Celestial art dimensions out of range");
        if (rgba.LongLength != (long)width * height * 4 * slices)
            throw new ArgumentException("Celestial art size mismatch", nameof(rgba));
        var next = new Upload(width, height, slices, (byte[])rgba.Clone());
        Clear();
        Pending = next;
    }

    internal void Clear()
    {
        Pending = null;
        var old = Resource;
        Resource = null;
        if (old != null) retire(old);
    }

    internal void Apply(Func<Upload, T> create)
    {
        var upload = Pending;
        if (upload == null) return;
        var resource = create(upload) ?? throw new InvalidOperationException("Celestial art upload returned no resource");
        Resource = resource;
        Pending = null;
    }

    internal sealed class Upload
    {
        internal readonly int Width, Height, Slices;
        internal readonly byte[] Rgba;
        internal Upload(int width, int height, int slices, byte[] rgba)
        { Width = width; Height = height; Slices = slices; Rgba = rgba; }
    }
}
