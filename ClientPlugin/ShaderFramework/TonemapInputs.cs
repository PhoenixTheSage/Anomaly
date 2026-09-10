using System;
using ClientPlugin.Buffers;
using VRage.Render11.Common;
using VRage.Render11.Resources;
using VRage.Render11.Resources.Textures;
using VRage.Utils;
using VRageRender;

namespace ClientPlugin.Shaders;

/// <summary>
/// Well-known type. Resolve by name:
/// <c>ClientPlugin.Shaders.TonemapInputs</c>. Anomaly captures Keen
/// <c>MyToneMapping.Run</c> SRVs (avg luminance, bloom, dirt) even when a
/// later prefix skips the original, so AfterUpscale Display programs can
/// sample them. Packs do not patch <c>Run</c>.
/// </summary>
public static class TonemapInputs
{
    public const string AvgLuminance = "avgLuminance";
    public const string Bloom = "bloom";
    public const string Dirt = "dirt";

    static readonly object Gate = new();
    static bool enableTonemapping = true;
    static bool needsAlphaLuminance;
    static bool captured;
    static string lastDirtError;

    /// <summary>Keen <c>enableTonemapping</c> for this frame (true when post is on).</summary>
    public static bool EnableTonemapping
    {
        get { lock (Gate) return enableTonemapping; }
    }

    /// <summary>Keen <c>needsAlphaLuminance</c> — FXAA reads dest.a as luma.</summary>
    public static bool NeedsAlphaLuminance
    {
        get { lock (Gate) return needsAlphaLuminance; }
    }

    /// <summary>True after this frame's <c>MyToneMapping.Run</c> prefix captured SRVs.</summary>
    public static bool Captured
    {
        get { lock (Gate) return captured; }
    }

    internal static void BeginFrame()
    {
        lock (Gate)
        {
            enableTonemapping = true;
            needsAlphaLuminance = false;
            captured = false;
        }

        BufferCatalog.Set(AvgLuminance, null);
        BufferCatalog.Set(Bloom, null);
        BufferCatalog.Set(Dirt, null);
    }

    internal static void Capture(object avgLum, object bloom, bool enableTonemap, string dirtTexture,
        bool needsAlpha)
    {
        lock (Gate)
        {
            enableTonemapping = enableTonemap;
            needsAlphaLuminance = needsAlpha;
            captured = true;
        }

        BufferCatalog.SetKeen(AvgLuminance, avgLum);
        BufferCatalog.SetKeen(Bloom, bloom);
        BufferCatalog.SetKeen(Dirt, ResolveDirt(dirtTexture));
    }

    static object ResolveDirt(string dirtTexture)
    {
        if (string.IsNullOrEmpty(dirtTexture))
            return null;
        try
        {
            return MyManagers.Textures.GetTempTexture(dirtTexture,
                new MyTextureStreamingManager.QueryArgs
                {
                    TextureType = MyFileTextureEnum.ALPHAMASK,
                    WaitUntilLoaded = true,
                    SkipQualityReduction = true
                });
        }
        catch (Exception e)
        {
            var message = e.Message;
            lock (Gate)
            {
                if (lastDirtError == message)
                    return null;
                lastDirtError = message;
            }

            MyLog.Default.WriteLine("Anomaly tonemap dirt: " + message);
            return null;
        }
    }
}
