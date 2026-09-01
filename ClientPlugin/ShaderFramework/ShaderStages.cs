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
    public const string AnomalyLinearDepth = "Anomaly.LinearDepth";
    public const string AnomalyHistoryColor = "Anomaly.HistoryColor";
    public const string Shadows = "Shadows";
    public const string Atmosphere = "Atmosphere";
    public const string Decals = "Decals";
    public const string GpuParticles = "GPUParticles";
    public const string EnvProbe = "EnvProbe";
    public const string Foliage = "Foliage";
    /// <summary>
    /// Inject-only bucket (slice P wraps <c>Lighting/Light.hlsli</c>).
    /// Not an overlay stage name.
    /// </summary>
    public const string Lighting = "Lighting";

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
        }),
        new Stage(AnomalyLinearDepth, new[]
        {
            "LinearDepth.hlsl",
            "HiZDownsample.hlsl",
            "Fullscreen.hlsl"
        }),
        new Stage(AnomalyHistoryColor, new[]
        {
            "HistoryCopy.hlsl",
            "Fullscreen.hlsl"
        }),
        new Stage(Shadows, new[]
        {
            "Shadows/Shadows.hlsl",
            "Shadows/Csm.hlsli",
            "Shadows/Shape.hlsl",
            "Shadows/ShadowStats.hlsl"
        }),
        new Stage(Atmosphere, Under("Transparent/Atmosphere",
            "AtmosphereCommon.hlsli", "AtmospherePrecompute.hlsl",
            "AtmosphereGBuffer.hlsl", "AtmosphereEnv.hlsl", "AtmosphereVS.hlsl")),
        new Stage(Decals, new[]
        {
            "Decals/Decals.hlsl",
            "Decals/DecalsCommon.hlsli"
        }),
        new Stage(GpuParticles, Under("Transparent/GPUParticles",
            "Render.hlsl", "Emit.hlsl", "EmitSkipFix.hlsl", "Simulation.hlsl",
            "Simulation.hlsli", "SimulationArgs.hlsl", "InitDeadList.hlsl",
            "Reset.hlsl", "Globals.hlsli")),
        new Stage(EnvProbe, Under("EnvProbe",
            "EnvProbe.hlsl", "EnvProbeCopy.hlsl", "EnvProbeBlend.hlsl",
            "EnvPrefiltering.hlsl", "EnvPrefiltering.hlsli", "ForwardPostprocess.hlsl")),
        new Stage(Foliage, Under("Foliage",
            "Foliage.hlsl", "Foliage.hlsli", "GrassFoliage.hlsli",
            "RockFoliage.hlsli", "FoliageStreaming.hlsl"))
    };

    static readonly string[] OwnedGBuffer =
    {
        "Geometry/Passes/GBuffer/VertexStage.hlsli",
        "Geometry/Passes/GBuffer/PixelStage.hlsli",
        "GBuffer/GBufferWrite.hlsli"
    };

    static readonly string[] OwnedGBufferRead =
    {
        "GBuffer/GBuffer.hlsli",
        "GBuffer/Surface.hlsli"
    };

    const string OwnedLightingWrap = "Lighting/Light.hlsli";

    static readonly Dictionary<string, Stage> ByName =
        new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string> KeyToStage =
        new(StringComparer.OrdinalIgnoreCase);
    static readonly string[] NamesLongestFirst;
    static readonly string[] InjectNamesLongestFirst;

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
        var inject = new List<string>(names.Count + 1);
        inject.AddRange(names);
        if (!inject.Exists(n => string.Equals(n, Lighting, StringComparison.OrdinalIgnoreCase)))
            inject.Add(Lighting);
        inject.Sort((a, b) => b.Length.CompareTo(a.Length));
        InjectNamesLongestFirst = inject.ToArray();
        FillSentinels();
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

    public static bool IsForbiddenInjectStage(string stageName)
    {
        return string.Equals(stageName, Depth, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Map an <c>Inject/</c> relative path to a named stage.
    /// Unscoped files (<c>Inject/foo.hlsli</c>) go to GBuffer (v1 packs).
    /// Unknown folders fail closed.
    /// </summary>
    public static bool TryMapInjectPath(string injectRelative, out string stageName)
    {
        stageName = null;
        var n = NormalizeSlashes(injectRelative);
        if (n.Length == 0)
            return false;

        for (var i = 0; i < InjectNamesLongestFirst.Length; i++)
        {
            var name = InjectNamesLongestFirst[i];
            if (n.Length == name.Length)
            {
                if (!string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                    continue;
                stageName = CanonicalInjectName(name);
                return true;
            }

            if (n.Length < name.Length + 1)
                continue;
            if (!n.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                continue;
            var next = n[name.Length];
            if (next == '/')
            {
                stageName = CanonicalInjectName(name);
                return true;
            }

            if (next == '.' && n.IndexOf('/', name.Length) < 0)
            {
                var ext = n.Substring(name.Length);
                if (IsShaderExtension(ext))
                {
                    stageName = CanonicalInjectName(name);
                    return true;
                }
            }
        }

        if (n.IndexOf('/') < 0)
        {
            stageName = GBuffer;
            return true;
        }

        return false;
    }

    static string CanonicalInjectName(string name)
    {
        if (string.Equals(name, Lighting, StringComparison.OrdinalIgnoreCase))
            return Lighting;
        for (var i = 0; i < NamesLongestFirst.Length; i++)
        {
            if (string.Equals(NamesLongestFirst[i], name, StringComparison.OrdinalIgnoreCase))
                return NamesLongestFirst[i];
        }

        return name;
    }

    static bool IsShaderExtension(string ext)
    {
        return string.Equals(ext, ".hlsl", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".hlsli", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".h", StringComparison.OrdinalIgnoreCase);
    }

    public static string ExtrasIncludePath(string stageName)
    {
        return "Anomaly/Extras/" + CanonicalInjectName(stageName ?? GBuffer) + ".hlsli";
    }

    public static bool IsGeometryStage(string stageName)
    {
        return string.Equals(stageName, GBuffer, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(stageName, Depth, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(stageName, Forward, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(stageName, Highlight, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(stageName, Transparent, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(stageName, TransparentForDecals, StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryGetSentinels(string stageName, out StageCompileProbe[] probes)
    {
        probes = null;
        if (string.IsNullOrEmpty(stageName))
            return false;
        return Sentinels.TryGetValue(stageName, out probes) && probes != null && probes.Length > 0;
    }

    static readonly Dictionary<string, StageCompileProbe[]> Sentinels =
        new(StringComparer.OrdinalIgnoreCase);

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
    /// Deferred-read wraps. Overlay needs <c>exclusive: ["GBuffer"]</c> or
    /// <c>["Lighting"]</c> so a lighting pack does not kill velocity writes.
    /// </summary>
    public static bool IsAnomalyOwnedGBufferRead(string keenKey)
    {
        var key = NormalizeSlashes(keenKey);
        for (var i = 0; i < OwnedGBufferRead.Length; i++)
        {
            if (string.Equals(key, OwnedGBufferRead[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// <c>Lighting/Light.hlsli</c> wrap. Overlay needs <c>exclusive: ["Lighting"]</c>
    /// (not Lighting.Dir / .Point / .Spot).
    /// </summary>
    public static bool IsAnomalyOwnedLightingWrap(string keenKey)
    {
        return string.Equals(NormalizeSlashes(keenKey), OwnedLightingWrap,
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLightingFamily(string stageName)
    {
        return string.Equals(stageName, Lighting, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(stageName, LightingDir, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(stageName, LightingPoint, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(stageName, LightingSpot, StringComparison.OrdinalIgnoreCase);
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

    static void FillSentinels()
    {
        Add(GBuffer, Geometry("0", depthOnly: false));
        Add(Depth, Geometry("1", depthOnly: true));
        Add(Forward, Geometry("2", depthOnly: false));
        Add(Highlight, Geometry("3", depthOnly: false));
        Add(Transparent, Concat(Geometry("5", depthOnly: false), new[]
        {
            Pixel("Transparent/OIT/Resolve.hlsl", Transparent)
        }));
        Add(TransparentForDecals, Geometry("6", depthOnly: false));
        Add(LightingDir, new[] { Pixel("Lighting/LightDir.hlsl", LightingDir) });
        Add(LightingPoint, new[] { Pixel("Lighting/LightPoint.hlsl", LightingPoint) });
        Add(LightingSpot, new[] { Pixel("Lighting/LightSpot.hlsl", LightingSpot) });
        Add(PostTonemap, new[]
        {
            Compute("Postprocess/Tonemapping/Main.hlsl", PostTonemap, "NUMTHREADS=8")
        });
        Add(PostHbao, new[] { Pixel("Postprocess/HBAO/LinearizeDepth.hlsl", PostHbao) });
        Add(PostSsao, new[] { Pixel("Postprocess/SSAO/Ssao.hlsl", PostSsao) });
        Add(PostBloom, new[]
        {
            Compute("Postprocess/Bloom/Init.hlsl", PostBloom, "NUMTHREADS=8")
        });
        Add(PostFxaa, new[] { Pixel("Postprocess/Fxaa.hlsl", PostFxaa) });
        Add(PostEyeAdaptation, new[]
        {
            Pixel("Postprocess/EyeAdaptation/ConstantExposure.hlsl", PostEyeAdaptation)
        });
        Add(PostLuminance, new[]
        {
            Compute("Postprocess/LuminanceReduction/Skip.hlsl", PostLuminance, "")
        });
        Add(PostChromaticAberration, new[]
        {
            Compute("Postprocess/ChromaticAberration/ChromaticAberration.hlsl",
                PostChromaticAberration, "NUMTHREADS=8")
        });
        Add(AnomalyCameraVelocity, new[]
        {
            new StageCompileProbe("Fullscreen.hlsl", true, "vs_5_0", "",
                "Anomaly.StageProbe." + AnomalyCameraVelocity + ".VS"),
            new StageCompileProbe("CameraVelocity.hlsl", true, "ps_5_0", "",
                "Anomaly.StageProbe." + AnomalyCameraVelocity + ".PS")
        });
        Add(AnomalyLinearDepth, new[]
        {
            new StageCompileProbe("Fullscreen.hlsl", true, "vs_5_0", "",
                "Anomaly.StageProbe." + AnomalyLinearDepth + ".VS"),
            new StageCompileProbe("LinearDepth.hlsl", true, "ps_5_0", "",
                "Anomaly.StageProbe." + AnomalyLinearDepth + ".PS")
        });
        Add(AnomalyHistoryColor, new[]
        {
            new StageCompileProbe("Fullscreen.hlsl", true, "vs_5_0", "",
                "Anomaly.StageProbe." + AnomalyHistoryColor + ".VS"),
            new StageCompileProbe("HistoryCopy.hlsl", true, "ps_5_0", "",
                "Anomaly.StageProbe." + AnomalyHistoryColor + ".PS")
        });
        Add(Shadows, new[] { Compute("Shadows/Shadows.hlsl", Shadows, "") });
        Add(Atmosphere, new[]
        {
            Pixel("Transparent/Atmosphere/AtmosphereGBuffer.hlsl", Atmosphere, "LQ=1")
        });
        Add(Decals, new[]
        {
            Vertex("Decals/Decals.hlsl", Decals),
            Pixel("Decals/Decals.hlsl", Decals)
        });
        Add(GpuParticles, new[]
        {
            Pixel("Transparent/GPUParticles/Render.hlsl", GpuParticles, "STREAKS=0;LIT_PARTICLE=0")
        });
        Add(EnvProbe, new[] { Pixel("EnvProbe/EnvProbeBlend.hlsl", EnvProbe) });
        Add(Foliage, new[] { Pixel("Foliage/Foliage.hlsl", Foliage) });
    }

    static void Add(string stage, StageCompileProbe[] probes)
    {
        Sentinels[stage] = probes;
    }

    static StageCompileProbe[] Geometry(string pass, bool depthOnly)
    {
        var macros = depthOnly
            ? "DEPTH_ONLY=1;RENDERING_PASS=" + pass
            : "RENDERING_PASS=" + pass;
        var stage = depthOnly ? Depth :
            pass == "0" ? GBuffer :
            pass == "2" ? Forward :
            pass == "3" ? Highlight :
            pass == "5" ? Transparent :
            pass == "6" ? TransparentForDecals : GBuffer;
        return new[]
        {
            new StageCompileProbe("Geometry/Materials/Standard/Pixel.hlsl", false, "ps_5_0", macros,
                "Anomaly.StageProbe." + stage + ".Pixel"),
            new StageCompileProbe("Geometry/Materials/Standard/Vertex.hlsl", false, "vs_5_0", macros,
                "Anomaly.StageProbe." + stage + ".Vertex")
        };
    }

    static StageCompileProbe Pixel(string path, string stage, string macros = "")
    {
        return new StageCompileProbe(path, false, "ps_5_0", macros, "Anomaly.StageProbe." + stage);
    }

    static StageCompileProbe Vertex(string path, string stage, string macros = "")
    {
        return new StageCompileProbe(path, false, "vs_5_0", macros, "Anomaly.StageProbe." + stage + ".VS");
    }

    static StageCompileProbe Compute(string path, string stage, string macros)
    {
        return new StageCompileProbe(path, false, "cs_5_0", macros, "Anomaly.StageProbe." + stage);
    }

    static StageCompileProbe[] Concat(StageCompileProbe[] a, StageCompileProbe[] b)
    {
        var n = new StageCompileProbe[a.Length + b.Length];
        Array.Copy(a, n, a.Length);
        Array.Copy(b, 0, n, a.Length, b.Length);
        return n;
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

/// <summary>
/// One compile used to prove a named stage still builds after overlays apply.
/// </summary>
public sealed class StageCompileProbe
{
    public readonly string RelativePath;
    public readonly bool FromAnomalyInclude;
    public readonly string Profile;
    public readonly string Macros;
    public readonly string Descriptor;

    public StageCompileProbe(string relativePath, bool fromAnomalyInclude, string profile, string macros,
        string descriptor)
    {
        RelativePath = relativePath;
        FromAnomalyInclude = fromAnomalyInclude;
        Profile = profile;
        Macros = macros ?? "";
        Descriptor = descriptor;
    }
}
