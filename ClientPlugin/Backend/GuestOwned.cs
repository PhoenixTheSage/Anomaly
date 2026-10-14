using System.IO;

namespace ClientPlugin.Backend;

/// <summary>
/// Owned fullscreen sources the Vulkan guest compiles. DX11 still publishes
/// the same catalog names from <c>OwnedBuffersPass</c>.
/// </summary>
static class GuestOwned
{
    static readonly string[] Files =
    {
        "CameraVelocity.hlsl",
        "LinearDepth.hlsl",
        "HiZDownsample.hlsl",
        "HistoryCopy.hlsl",
        "ReactiveClear.hlsl",
    };

    public static void Register(string assetFolder)
    {
        if (string.IsNullOrEmpty(assetFolder))
            return;
        string shaders = Path.Combine(assetFolder, "Shaders");
        if (!Directory.Exists(shaders))
            shaders = assetFolder;
        foreach (var file in Files)
        {
            string path = Path.Combine(shaders, file);
            if (!File.Exists(path))
                continue;
            string name = Path.GetFileNameWithoutExtension(file);
            GuestPassList.AddProgram("anomaly.owned." + name, "AfterLighting", path, "PublishOnly");
            int format = name == "HistoryCopy" ? 1 : name == "CameraVelocity" ? 3 : name == "ReactiveClear" ? 0 : 2;
            GuestPassList.SetFlags("anomaly.owned." + name, 0, format);
        }
    }
}
