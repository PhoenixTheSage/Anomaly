using System.Collections.Generic;
using ClientPlugin.Buffers;

namespace ClientPlugin.Backend;

/// <summary>
/// Vulkan color handoff. Bilinear compose stays the default until a pack
/// claims the upscale slot and notifies completion. Vendor SDKs stay in
/// those packs. Character atlas/player-depth products are owned separately by
/// GuestPointShadows and use the same catalog as the DX11 host.
/// </summary>
static class GuestColor
{
    static string upscaleId = "";
    static bool notified;
    static readonly Dictionary<string, bool> Glare = new Dictionary<string, bool>();
    sealed class Payload
    {
        internal byte[] Bytes;
        internal int Width,Height,Depth,Kind;
        internal ulong Upload(ulong image=0)
        {
            if(image==0)return GuestBridgeClient.AllocateImage(Width,Height,Depth,Bytes,Kind);
            GuestBridgeClient.CreateImage(image,Width,Height,Depth,Kind);GuestBridgeClient.UpdateImage(image,Bytes);return image;
        }
    }
    static readonly Dictionary<string,Payload> dataPayloads=new Dictionary<string,Payload>();
    static readonly Dictionary<string,Payload> artPayloads=new Dictionary<string,Payload>();

    internal static void ResetDevice(){Celestial.Data.Clear();Celestial.Art.Clear();notified=false;}
    internal static void RestoreDevice()
    {
        Celestial.Data.Clear();Celestial.Art.Clear();notified=false;
        foreach(var pair in dataPayloads)Celestial.Data[pair.Key]=pair.Value.Upload();
        foreach(var pair in artPayloads)Celestial.Art[pair.Key]=pair.Value.Upload();
        GuestBridgeClient.SetUpscaleOwner(upscaleId);
    }

    public static bool Open => RendererRoute.UseGuestImages;
    public static string UpscaleId => upscaleId ?? "";
    public static bool HasUpscale => !string.IsNullOrEmpty(upscaleId);
    public static bool Notified => notified;
    /// <summary>True while a Display-policy AfterUpscale program is registered and enabled, as DX11 derives the tenant.</summary>
    public static bool HasDisplay => displayTenant;
    static bool displayTenant;
    const string DisplayOwner = "anomaly.display-tenant";

    /// <summary>
    /// Derives display tenancy from active registrations and requests the renderer's tenant service. The renderer
    /// grades only on an HDR swapchain once the program's pipeline is ready (guest frame service bit 32).
    /// </summary>
    internal static void SyncDisplay()
    {
        bool tenant = false;
        foreach (var program in GuestPassList.Snapshot())
            if (program.Mark == GuestMark.AfterUpscale && (program.Policy & 8) != 0 && program.Enabled && !program.ConflictDisabled) { tenant = true; break; }
        if (tenant == displayTenant) return;
        displayTenant = tenant;
        if (tenant) ClientPlugin.Shaders.DisplayServices.RequestFor(DisplayOwner, ClientPlugin.Shaders.DisplayServices.DisplayTenant);
        else ClientPlugin.Shaders.DisplayServices.ReleaseFor(DisplayOwner, ClientPlugin.Shaders.DisplayServices.DisplayTenant);
    }

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
        int outputWidth=GuestFrame.Open?System.Math.Max(1,(int)GuestFrame.F32(332)):width;
        int outputHeight=GuestFrame.Open?System.Math.Max(1,(int)GuestFrame.F32(336)):height;
        BufferCatalog.Set(BufferCatalog.HdrColor, new GuestRendererImage(GuestBridgeClient.Hdr, width, height, 1));
        BufferCatalog.Set(BufferCatalog.UpscaledColor, new GuestRendererImage(GuestBridgeClient.Upscale, outputWidth, outputHeight, 1));
        BufferCatalog.Set("displayColor", new GuestRendererImage(GuestBridgeClient.Display, outputWidth, outputHeight, 1));
        // Keen tonemap inputs for post-tonemap readers (t4-t6), as DX11 TonemapInputs publishes them.
        var tonemap = GuestBridgeClient.TonemapInputImages();
        if (tonemap != null && tonemap.Length == 3)
        {
            BufferCatalog.Set(ClientPlugin.Shaders.TonemapInputs.AvgLuminance, new GuestRendererImage(tonemap[0], 1, 1, 1));
            BufferCatalog.Set(ClientPlugin.Shaders.TonemapInputs.Bloom, new GuestRendererImage(tonemap[1], width, height, 1));
            BufferCatalog.Set(ClientPlugin.Shaders.TonemapInputs.Dirt, new GuestRendererImage(tonemap[2], 1, 1, 1));
        }
    }

    public static void SetCelestialData(string id, float[] values)
    {
        if (string.IsNullOrEmpty(id) || values == null || values.Length == 0)
            return;
        var bytes = new byte[values.Length * 4];
        System.Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        var payload=new Payload{Bytes=bytes,Width=bytes.Length,Height=1,Depth=1,Kind=4};
        dataPayloads[id]=payload;
        Celestial.Data.TryGetValue(id,out var previous);
        ulong image = GuestBridgeClient.ResourceEpoch==0?0:payload.Upload(previous);
        if (image != 0)
            Celestial.Data[id] = image;
    }

    public static void SetCelestialArt(string id, int width, int height, int slices, byte[] rgba)
    {
        if (string.IsNullOrEmpty(id) || rgba == null || width <= 0 || height <= 0)
            return;
        int layers = slices < 1 ? 1 : slices;
        var payload=new Payload{Bytes=(byte[])rgba.Clone(),Width=width,Height=height,Depth=layers,Kind=5};
        artPayloads[id]=payload;
        Celestial.Art.TryGetValue(id,out var previous);
        ulong image = GuestBridgeClient.ResourceEpoch==0?0:payload.Upload(previous);
        if (image != 0)
            Celestial.Art[id] = image;
    }

    public static class Celestial
    {
        public static readonly Dictionary<string, ulong> Data = new Dictionary<string, ulong>();
        public static readonly Dictionary<string, ulong> Art = new Dictionary<string, ulong>();
    }
}
