namespace ClientPlugin.Backend;

/// <summary>
/// Early read for every Anomaly pack. <see cref="Active"/> is the operational
/// renderer: 0 pending, 1 DX11, 2 Vulkan. Packs do not read SE-Vulkan's
/// requested backend. Device calls go through <see cref="RefuseDx11Device"/>
/// and <see cref="RefuseGuest"/> so a DX11 call cannot land on Vulkan, or the
/// reverse.
/// </summary>
public static class RendererRoute
{
    public const int Pending = RendererPaths.Pending;
    public const int DirectX11 = RendererPaths.DirectX11;
    public const int Vulkan = RendererPaths.Vulkan;
    public const string Shared = RendererPaths.Shared;
    public const string Dx11Device = RendererPaths.Dx11Device;
    public const string Guest = RendererPaths.Guest;

    static string loggedRefusal = "";

    public static int Active => !RenderHost.Decided ? Pending : RenderHost.IsVulkan ? Vulkan : DirectX11;
    public static string ActiveName => RendererPaths.Name(Active);
    public static bool Decided => Active != Pending;
    public static bool UseSharedBus => RendererPaths.UseSharedBus(Active);
    public static bool UseDx11Device => RendererPaths.UseDx11Device(Active);
    public static bool UseGuestImages => RendererPaths.UseGuestImages(Active);
    public static string LastRefusal { get; private set; } = "";

    public static bool IsPathOpen(string path) => RendererPaths.IsPathOpen(Active, path);

    internal static bool RefuseDx11Device(string call)
    {
        if (UseDx11Device)
            return false;
        Note(call + " blocked; the DX11 device path is closed while the renderer is " + ActiveName);
        return true;
    }

    internal static bool RefuseGuest(string call)
    {
        if (UseGuestImages)
            return false;
        Note(call + " blocked; the Vulkan guest path is closed while the renderer is " + ActiveName);
        return true;
    }

    static void Note(string refusal)
    {
        LastRefusal = refusal;
        if (loggedRefusal == refusal)
            return;
        loggedRefusal = refusal;
        try { VRage.Utils.MyLog.Default.WriteLine("Anomaly: " + refusal); }
        catch { /* LastRefusal stays readable when the game log is not up. */ }
    }
}
