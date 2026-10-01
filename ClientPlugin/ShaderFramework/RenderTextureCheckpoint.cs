using System;
using SharpDX.Direct3D11;

namespace ClientPlugin.ShaderFramework;

/// <summary>Private GPU copy used to undo a failed render transaction. Never maps or reads back.</summary>
internal sealed class RenderTextureCheckpoint : IDisposable
{
    Texture2D copy, target;
    internal bool Captured => target != null;

    internal void Capture(Device device, DeviceContext context, Texture2D texture)
    {
        target = null;
        if (texture == null) return;
        var desc = texture.Description;
        if (copy == null || copy.Description.Width != desc.Width || copy.Description.Height != desc.Height ||
            copy.Description.Format != desc.Format || copy.Description.SampleDescription.Count != desc.SampleDescription.Count ||
            copy.Description.SampleDescription.Quality != desc.SampleDescription.Quality ||
            copy.Description.MipLevels != desc.MipLevels || copy.Description.ArraySize != desc.ArraySize)
        {
            copy?.Dispose(); copy = null;
            desc.Usage = ResourceUsage.Default; desc.CpuAccessFlags = CpuAccessFlags.None;
            desc.BindFlags = BindFlags.None; desc.OptionFlags = ResourceOptionFlags.None;
            copy = new Texture2D(device, desc);
        }
        context.CopyResource(texture, copy);
        target = texture;
    }

    internal void Restore(DeviceContext context)
    {
        if (target != null) context.CopyResource(copy, target);
    }

    internal void Reset() => target = null;
    public void Dispose() { target = null; copy?.Dispose(); copy = null; }
}
