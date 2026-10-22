using System;
using System.Collections.Generic;
using ClientPlugin.Buffers;

namespace ClientPlugin.Backend;

// Descriptions may be requested before a device exists. Only Flush, on the
// renderer callback, creates resources or submits clears.
static class GuestCatalogTargets
{
    sealed class Target
    {
        internal string Owner;
        internal int Width, Height, Format, AppliedWidth, AppliedHeight, AppliedFormat;
        internal ulong Image;
        internal long Epoch;
        internal float[] Clear;
    }
    static readonly object Gate = new object();
    static readonly Dictionary<string, Target> Targets = new Dictionary<string, Target>(StringComparer.Ordinal);
    static readonly List<Tuple<long, ulong>> Releases = new List<Tuple<long, ulong>>();

    internal static bool Ensure(string owner, string name, int width, int height, int dxgiFormat)
    {
        int format;
        switch (dxgiFormat)
        {
            case 2: format = 6; break; // R32G32B32A32_FLOAT
            case 10: format = 1; break; // R16G16B16A16_FLOAT
            case 28: format = 0; break; // R8G8B8A8_UNORM
            case 34: format = 3; break; // R16G16_FLOAT
            case 41: format = 2; break; // R32_FLOAT
            case 54: format = 8; break; // R16_FLOAT
            default: return false;
        }
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(name) || width < 1 || height < 1) return false;
        lock (Gate)
        {
            if (!Targets.TryGetValue(name, out var target)) Targets.Add(name, target = new Target { Owner = owner });
            if (!string.Equals(target.Owner, owner, StringComparison.OrdinalIgnoreCase)) return false;
            target.Width = width; target.Height = height; target.Format = format;
            return true;
        }
    }

    internal static void Clear(string name, float r, float g, float b, float a)
    {
        foreach (float value in new[] { r, g, b, a })
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
        lock (Gate)
            if (Targets.TryGetValue(name, out var target)) target.Clear = new[] { r, g, b, a };
    }

    internal static void Release(string owner, string name = null)
    {
        lock (Gate)
        {
            var removed = new List<string>();
            foreach (var pair in Targets)
                if ((name == null || pair.Key == name) && string.Equals(pair.Value.Owner, owner, StringComparison.OrdinalIgnoreCase))
                {
                    if (pair.Value.Image != 0) Releases.Add(Tuple.Create(pair.Value.Epoch, pair.Value.Image));
                    BufferCatalog.Set(pair.Key, null); removed.Add(pair.Key);
                }
            foreach (string key in removed) Targets.Remove(key);
        }
    }

    internal static void Flush()
    {
        long epoch = GuestBridgeClient.ResourceEpoch;
        if (epoch == 0) return;
        lock (Gate)
        {
            foreach (var image in Releases) if (image.Item1 == epoch) GuestBridgeClient.ResourceCommand(1, image.Item2);
            Releases.Clear();
            foreach (var pair in Targets)
            {
                var target = pair.Value;
                bool fresh = target.Epoch != epoch || target.Image == 0;
                if (fresh) target.Image = 0;
                if (fresh || target.Width != target.AppliedWidth || target.Height != target.AppliedHeight || target.Format != target.AppliedFormat)
                {
                    if (!GuestBridgeClient.SupportsPacketFlags(GuestBridgeClient.ResourceCommands))
                        throw new NotSupportedException("Update SE-Vulkan native library: catalog targets require guest feature bit13.");
                    if (target.Image == 0) target.Image = GuestBridgeClient.AllocateImage(target.Width, target.Height, 1, null, target.Format);
                    else GuestBridgeClient.CreateImage(target.Image, target.Width, target.Height, 1, target.Format);
                    target.AppliedWidth = target.Width; target.AppliedHeight = target.Height; target.AppliedFormat = target.Format; target.Epoch = epoch;
                    if (target.Clear == null) target.Clear = new float[4];
                }
                if (target.Clear != null)
                {
                    if (!GuestBridgeClient.ResourceCommand(3, target.Image, GuestBridgeClient.SupportsPacketFlags(GuestStageBindings.Feature)?0:1, target.Clear))
                        throw new InvalidOperationException("SE-Vulkan refused catalog target clear: " + pair.Key);
                    target.Clear = null;
                }
                BufferCatalog.Set(pair.Key, new GuestRendererImage(target.Image, target.Width, target.Height, 1, false));
            }
        }
    }
}
