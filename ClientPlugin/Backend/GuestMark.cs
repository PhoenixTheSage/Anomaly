namespace ClientPlugin.Backend;

/// <summary>
/// Vulkan guest insertion marks. Values match <c>guest_replay</c> in SE-Vulkan.
/// Empty packets do not record GPU work.
/// </summary>
public static class GuestMark
{
    public const int AfterLighting = 1;
    public const int AfterAtmosphere = 2;
    public const int AfterTransparent = 3;
    public const int BeforeTonemap = 4;
    public const int AfterTonemap = 5;
    public const int AfterUpscale = 6;

    public static int FromSlot(string slot)
    {
        if (string.IsNullOrEmpty(slot))
            return 0;
        switch (slot.Trim().ToLowerInvariant())
        {
            case "afterlighting": return AfterLighting;
            case "afteratmosphere": return AfterAtmosphere;
            case "aftertransparent": return AfterTransparent;
            case "beforetonemap": return BeforeTonemap;
            case "aftertonemap": return AfterTonemap;
            case "afterupscale": return AfterUpscale;
            default: return 0;
        }
    }
}
