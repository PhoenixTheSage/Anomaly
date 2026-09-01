using System;
using System.Collections.Generic;

namespace ClientPlugin.Shaders;

/// <summary>
/// Well-known type for pack plugins. Resolve by name:
/// <c>ClientPlugin.Shaders.ShaderStages</c>. Named overlay folders
/// (<c>Overlay/GBuffer/PixelStage.hlsli</c>) map to Keen (or Anomaly)
/// relative paths. Raw Keen-relative overlay stays the escape hatch.
/// </summary>
public static class ShaderStages
{
    public const string GBuffer = "GBuffer";
    public const string Depth = "Depth";
    public const string Forward = "Forward";
    public const string Highlight = "Highlight";
    public const string Transparent = "Transparent";
    public const string TransparentForDecals = "TransparentForDecals";
    public const string LightingDir = "Lighting.Dir";
    public const string LightingPoint = "Lighting.Point";
    public const string LightingSpot = "Lighting.Spot";
    public const string PostTonemap = "Post.Tonemap";
    public const string PostHbao = "Post.HBAO";
    public const string PostSsao = "Post.SSAO";
    public const string PostBloom = "Post.Bloom";
    public const string PostFxaa = "Post.FXAA";
    public const string PostEyeAdaptation = "Post.EyeAdaptation";
    public const string PostLuminance = "Post.Luminance";
    public const string PostChromaticAberration = "Post.ChromaticAberration";
    public const string AnomalyCameraVelocity = "Anomaly.CameraVelocity";

    static readonly Stage[] Table =
    {
        new Stage(GBuffer, new[]
        {
            "Geometry/Passes/GBuffer/VertexStage.hlsli",
            "Geometry/Passes/GBuffer/PixelStage.hlsli",
            "GBuffer/GBufferWrite.hlsli",
            "GBuffer/GBuffer.hlsli",
            "GBuffer/Surface.hlsli"
        }),
        new Stage(Depth, new[]
        {
            "Geometry/Passes/Depth/VertexStage.hlsli",
            "Geometry/Passes/Depth/PixelStage.hlsli"
        }),
        new Stage(Forward, new[]
        {
            "Geometry/Passes/Forward/Declarations.hlsli",
            "Geometry/Passes/Forward/VertexStage.hlsli",
            "Geometry/Passes/Forward/PixelStage.hlsli"
        }),
        new Stage(Highlight, new[]
        {
            "Geometry/Passes/Highlight/VertexStage.hlsli",
            "Geometry/Passes/Highlight/PixelStage.hlsli"
        }),
        new Stage(Transparent, new[]
        {
            "Geometry/Passes/Transparent/Declarations.hlsli",
            "Geometry/Passes/Transparent/VertexStage.hlsli",
            "Geometry/Passes/Transparent/PixelStage.hlsli",
            "Transparent/OIT/Globals.hlsli",
            "Transparent/OIT/Resolve.hlsl"
        }),
        new Stage(TransparentForDecals, new[]
        {
            "Geometry/Passes/TransparentForDecals/Declarations.hlsli",
            "Geometry/Passes/TransparentForDecals/VertexStage.hlsli",
            "Geometry/Passes/TransparentForDecals/PixelStage.hlsli"
        }),
        new Stage(LightingDir, new[] { "Lighting/LightDir.hlsl" }),
        new Stage(LightingPoint, new[] { "Lighting/LightPoint.hlsl" }),
        new Stage(LightingSpot, new[] { "Lighting/LightSpot.hlsl" }),
        new Stage(PostTonemap, new[]
        {
            "Postprocess/Tonemapping/Main.hlsl",
            "Postprocess/Tonemapping/Filters.hlsli",
            "Postprocess/Tonemapping/Defines.hlsli"
        }),
        new Stage(PostHbao, Under("Postprocess/HBAO",
            "BlurX.hlsl", "BlurY.hlsl", "Blur_Common.hlsli", "CoarseAO.hlsl",
            "ConstantBuffers.hlsli", "Copy.hlsl", "DeinterleaveDepth.hlsl",
            "DrawNormals.hlsl", "FetchNormal_Common.hlsli",
            "FullScreenTriangle_Common.hlsli", "LinearizeDepth.hlsl",
            "ReconstructNormal.hlsl", "ReconstructNormal_Common.hlsli",
            "ReinterleaveAO.hlsl", "SharedDefines.hlsli")),
        new Stage(PostSsao, new[] { "Postprocess/SSAO/Ssao.hlsl" }),
        new Stage(PostBloom, Under("Postprocess/Bloom",
            "Init.hlsl", "PreFilter.hlsl", "Downscale2.hlsl", "Downscale4.hlsl",
            "DownsampleBlur.hlsl", "Blur.hlsl", "UpsampleBlur.hlsl", "Defines.hlsli")),
        new Stage(PostFxaa, new[]
        {
            "Postprocess/Fxaa.hlsl",
            "Postprocess/Fxaa3_11.hlsli"
        }),
        new Stage(PostEyeAdaptation, Under("Postprocess/EyeAdaptation",
            "Defines.hlsli", "ConstantExposure.hlsl", "UpdateHistogram.hlsl",
            "DownSample.hlsl", "DebugHistogram.hlsl", "EyeAdaptation.hlsl",
            "EyeAdaptation.hlsli")),
        new Stage(PostLuminance, Under("Postprocess/LuminanceReduction",
            "Init.hlsl", "Sum.hlsl", "Skip.hlsl", "Defines.hlsli")),
        new Stage(PostChromaticAberration, new[]
        {
            "Postprocess/ChromaticAberration/ChromaticAberration.hlsl"
        }),
        new Stage(AnomalyCameraVelocity, new[]
        {
            "CameraVelocity.hlsl",
            "Fullscreen.hlsl"
        })
    };

    static readonly string[] OwnedGBuffer =
    {
        "Geometry/Passes/GBuffer/VertexStage.hlsli",
        "Geometry/Passes/GBuffer/PixelStage.hlsli",
        "GBuffer/GBufferWrite.hlsli"
    };

    static readonly Dictionary<string, Stage> ByName =
        new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string> KeyToStage =
        new(StringComparer.OrdinalIgnoreCase);
    static readonly string[] NamesLongestFirst;

    static ShaderStages()
    {
        var names = new List<string>(Table.Length);
        for (var i = 0; i < Table.Length; i++)
        {
            var stage = Table[i];
            ByName[stage.Name] = stage;
            names.Add(stage.Name);
            for (var f = 0; f < stage.Files.Length; f++)
            {
                var key = NormalizeSlashes(stage.Files[f]);
                stage.Files[f] = key;
                if (!KeyToStage.ContainsKey(key))
                    KeyToStage[key] = stage.Name;
            }
        }

        names.Sort((a, b) => b.Length.CompareTo(a.Length));
        NamesLongestFirst = names.ToArray();
    }

    public static IReadOnlyList<string> Names => NamesLongestFirst;

    public static bool TryGetFiles(string stageName, out IReadOnlyList<string> files)
    {
        files = null;
        if (string.IsNullOrEmpty(stageName) || !ByName.TryGetValue(stageName, out var stage))
            return false;
        files = stage.Files;
        return true;
    }

    public static bool TryGetStageForKey(string keenKey, out string stageName)
    {
        stageName = null;
        var key = NormalizeSlashes(keenKey);
        if (key.Length == 0)
            return false;
        return KeyToStage.TryGetValue(key, out stageName);
    }

    public static bool IsAnomalyOwnedGBuffer(string keenKey)
    {
        var key = NormalizeSlashes(keenKey);
        for (var i = 0; i < OwnedGBuffer.Length; i++)
        {
            if (string.Equals(key, OwnedGBuffer[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Map an <c>Overlay/</c> relative path to a Keen (or Anomaly) compile key.
    /// <paramref name="stageName"/> is set when the first path segment is a
    /// named stage. Unknown files under a stage name fail closed.
    /// </summary>
    public static bool TryMapOverlayPath(string overlayRelative, out string keenKey, out string stageName)
    {
        keenKey = null;
        stageName = null;
        var n = NormalizeSlashes(overlayRelative);
        if (n.Length == 0)
            return false;

        for (var i = 0; i < NamesLongestFirst.Length; i++)
        {
            var name = NamesLongestFirst[i];
            if (n.Length <= name.Length)
                continue;
            if (n[name.Length] != '/')
                continue;
            if (!n.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!ByName.TryGetValue(name, out var stage))
                continue;
            var rest = n.Substring(name.Length + 1);
            if (!TryMapStageFile(stage, rest, out keenKey))
                return false;
            stageName = stage.Name;
            return true;
        }

        keenKey = n;
        return true;
    }

    static bool TryMapStageFile(Stage stage, string rest, out string keenKey)
    {
        keenKey = null;
        rest = NormalizeSlashes(rest);
        if (rest.Length == 0)
            return false;

        string exact = null;
        string suffix = null;
        string basename = null;
        var suffixCount = 0;
        var baseCount = 0;
        var fileOnly = rest.IndexOf('/') < 0;

        for (var i = 0; i < stage.Files.Length; i++)
        {
            var mapped = stage.Files[i];
            if (string.Equals(mapped, rest, StringComparison.OrdinalIgnoreCase))
            {
                exact = mapped;
                break;
            }

            if (mapped.Length > rest.Length &&
                mapped[mapped.Length - rest.Length - 1] == '/' &&
                mapped.EndsWith(rest, StringComparison.OrdinalIgnoreCase))
            {
                suffix = mapped;
                suffixCount++;
            }

            if (fileOnly &&
                string.Equals(FileName(mapped), rest, StringComparison.OrdinalIgnoreCase))
            {
                basename = mapped;
                baseCount++;
            }
        }

        if (exact != null)
        {
            keenKey = exact;
            return true;
        }

        if (suffixCount == 1)
        {
            keenKey = suffix;
            return true;
        }

        if (baseCount == 1)
        {
            keenKey = basename;
            return true;
        }

        return false;
    }

    static string[] Under(string folder, params string[] names)
    {
        var files = new string[names.Length];
        folder = NormalizeSlashes(folder);
        for (var i = 0; i < names.Length; i++)
            files[i] = folder + "/" + names[i];
        return files;
    }

    static string FileName(string path)
    {
        var i = path.LastIndexOf('/');
        return i < 0 ? path : path.Substring(i + 1);
    }

    internal static string NormalizeSlashes(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        return path.Replace('\\', '/').Trim().Trim('/');
    }

    sealed class Stage
    {
        public readonly string Name;
        public readonly string[] Files;

        public Stage(string name, string[] files)
        {
            Name = name;
            Files = files;
        }
    }
}
