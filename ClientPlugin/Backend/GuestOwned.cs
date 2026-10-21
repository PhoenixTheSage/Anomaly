using System.IO;

namespace ClientPlugin.Backend;

/// <summary>
/// Owned fullscreen sources the Vulkan guest compiles. DX11 still publishes
/// the same catalog names from <c>OwnedBuffersPass</c>.
/// </summary>
static class GuestOwned
{
    static int explicitMipLevels;
    public static bool DebugLitMips;
    static readonly object RequestGate = new object();
    public static int ExplicitLitMipLevels { get { lock (RequestGate) return explicitMipLevels; } }

    public static void RequestLitMips(int levels)
    {
        lock (RequestGate)
            explicitMipLevels = levels <= 0 ? 0 : System.Math.Max(explicitMipLevels, System.Math.Min(12, System.Math.Max(2, levels)));
    }

    public static int LitMipLevels(System.Collections.Generic.IReadOnlyList<GuestProgram> programs, int width, int height)
    {
        int levels = System.Math.Max(ExplicitLitMipLevels, DebugLitMips ? 5 : 0);
        foreach (var p in programs)
            if (p != null && p.Enabled && p.Pipeline != 0 && p.Srvs != null)
                foreach (var bind in p.Srvs)
                    if (string.Equals(bind.Name, "litMips", System.StringComparison.OrdinalIgnoreCase))
                        levels = System.Math.Max(5, levels);
        if (levels == 0) return 0;
        int available = 1;
        for (int size = System.Math.Max(width, height); size > 1; size >>= 1) available++;
        return System.Math.Min(levels, available);
    }

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
        string merge = Path.Combine(shaders,"FullscreenMerge.hlsl");
        if(File.Exists(merge))
            for(int mode=0;mode<4;mode++)
            {
                string generated=Path.Combine(Path.GetTempPath(),"anomaly-guest-sources","anomaly-merge-"+mode+".hlsl");
                Directory.CreateDirectory(Path.GetDirectoryName(generated));
                File.WriteAllText(generated,"#define MERGE_MODE "+mode+"\n"+File.ReadAllText(merge).Replace("register(s0)","register(s1)"));
                string id="anomaly.helper.Merge"+mode;
                GuestPassList.AddProgram(id,"AfterLighting",generated,"PublishOnly");
                foreach(var program in GuestPassList.Snapshot())if(program.Id==id)program.Helper=true;
            }
        foreach (var file in Files)
        {
            string path = Path.Combine(shaders, file);
            if (!File.Exists(path))
                continue;
            string name = Path.GetFileNameWithoutExtension(file);
            if (name == "HiZDownsample") name = "HiZ";
            GuestPassList.AddProgram("anomaly.owned." + name, name == "HistoryCopy" ? "AfterUpscale" : "AfterLighting", path, "PublishOnly");
            int format = name == "HistoryCopy" ? 1 : name == "CameraVelocity" ? 3 : name == "ReactiveClear" ? 0 : 2;
            GuestPassList.SetFlags("anomaly.owned." + name, 0, format);
            if (name == "HiZ") GuestPassList.SetScale("anomaly.owned." + name, 0.5f);
            if (name == "LinearDepth") GuestPassList.SetHistory("anomaly.owned." + name, "historyDepth");
        }
        // Native owns the mip-chain copy/blits; reuse the copy shader for the
        // pipeline readiness handshake without introducing a second pack host.
        const string lit = "anomaly.owned.LitMips";
        GuestPassList.AddProgram(lit, "AfterLighting", Path.Combine(shaders, "HistoryCopy.hlsl"), "PublishOnly");
        GuestPassList.SetFlags(lit, GuestBridgeClient.GenerateMips, 1);
        GuestOccupancy.Register(shaders);
        GuestPointShadows.Register(shaders);
    }
}
