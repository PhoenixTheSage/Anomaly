namespace ClientPlugin.Backend;

/// <summary>
/// Path table for the operational renderer. Pending closes both device paths.
/// The shared bus stays open so packs register there instead of guessing a device.
/// </summary>
public static class RendererPaths
{
    public const int Pending = 0;
    public const int DirectX11 = 1;
    public const int Vulkan = 2;

    public const string Shared = "shared";
    public const string Dx11Device = "dx11-device";
    public const string Guest = "guest";

    public static string Name(int active)
    {
        if (active == DirectX11)
            return "DX11";
        if (active == Vulkan)
            return "Vulkan";
        return "pending";
    }

    public static bool UseSharedBus(int active) => active == Pending || active == DirectX11 || active == Vulkan;

    public static bool UseDx11Device(int active) => active == DirectX11;

    public static bool UseGuestImages(int active) => active == Vulkan;

    public static bool IsPathOpen(int active, string path)
    {
        if (path == Shared)
            return UseSharedBus(active);
        if (path == Dx11Device)
            return UseDx11Device(active);
        if (path == Guest)
            return UseGuestImages(active);
        return false;
    }
}
