using System.Collections.Generic;
using ClientPlugin.Buffers;

namespace ClientPlugin.Backend;

/// <summary>
/// Vulkan color handoff. Bilinear compose stays the default until a pack
/// claims the upscale slot and notifies completion. Vendor SDKs stay in
/// those packs. <c>pointShadowAtlas</c> is not published: the renderer has
/// no local-character cube to alias.
/// </summary>
static class GuestColor
{
    static string upscaleId = "";
    static bool notified;
    static readonly Dictionary<string, bool> Glare = new Dictionary<string, bool>();

    public static bool Open => RendererRoute.UseGuestImages;
    public static string UpscaleId => upscaleId ?? "";
    public static bool HasUpscale => !string.IsNullOrEmpty(upscaleId);
    public static bool Notified => notified;
    public static bool HasDisplay => true;

    public static string Status => HasUpscale ? "upscale=" + upscaleId : "upscale=bilinear";

    public static string ColorStatus =>
        "upscale=" + (HasUpscale ? upscaleId : "bilinear") +
        " upscaledColor=renderer display=renderer dest=" + (notified ? upscaleId : "bilinear");

    public static bool Claim(string id)
    {
        if (string.IsNullOrEmpty(id))
            return false;
        if (HasUpscale && !string.Equals(upscaleId, id, System.StringComparison.OrdinalIgnoreCase))
            return false;
        upscaleId = id;
        notified = false;
        GuestBridgeClient.SetUpscaleOwner(id);
        return true;
    }

    public static void Release(string id)
    {
        if (string.Equals(upscaleId, id, System.StringComparison.OrdinalIgnoreCase))
        {
            upscaleId = "";
            notified = false;
            GuestBridgeClient.SetUpscaleOwner("");
        }
    }

    public static void Notify() => notified = HasUpscale;

    public static void SetGlare(string id, bool enabled)
    {
        if (!string.IsNullOrEmpty(id))
            Glare[id] = enabled;
    }

    public static bool NativeGlare(string id) => string.IsNullOrEmpty(id) || !Glare.TryGetValue(id, out var enabled) || enabled;

    public static void Publish()
    {
        int width = SceneView.Width > 0 ? SceneView.Width : 1;
        int height = SceneView.Height > 0 ? SceneView.Height : 1;
        BufferCatalog.Set(BufferCatalog.HdrColor, new GuestRendererImage(GuestBridgeClient.Hdr, width, height, 1));
        BufferCatalog.Set(BufferCatalog.UpscaledColor, new GuestRendererImage(GuestBridgeClient.Upscale, width, height, 1));
        BufferCatalog.Set("displayColor", new GuestRendererImage(GuestBridgeClient.Display, width, height, 1));
    }

    public static void SetCelestialData(string id, float[] values)
    {
        if (string.IsNullOrEmpty(id) || values == null || values.Length == 0)
            return;
        var bytes = new byte[values.Length * 4];
        System.Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        ulong image = GuestBridgeClient.AllocateImage(bytes.Length, 1, 1, bytes, 4);
        if (image != 0)
            Celestial.Data[id] = image;
    }

    public static void SetCelestialArt(string id, int width, int height, int slices, byte[] rgba)
    {
        if (string.IsNullOrEmpty(id) || rgba == null || width <= 0 || height <= 0)
            return;
        int layers = slices < 1 ? 1 : slices;
        ulong image = GuestBridgeClient.AllocateImage(width, height, layers, rgba, 5);
        if (image != 0)
            Celestial.Art[id] = image;
    }

    public static class Celestial
    {
        public static readonly Dictionary<string, ulong> Data = new Dictionary<string, ulong>();
        public static readonly Dictionary<string, ulong> Art = new Dictionary<string, ulong>();
    }
}
