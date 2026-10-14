using System;
using System.IO;
using System.Reflection;

namespace ClientPlugin.Backend;

/// <summary>
/// Compiles pack fullscreen HLSL with SE-Vulkan's compiler. The fixed bus
/// (t0–t13, s0–s2, b0 owned constants, b6 extras, b7 uniforms) gets a pipeline.
/// A binding outside that set stays descriptor-deferred.
/// </summary>
static class GuestCompiler
{
    const string VertexSource =
        "struct VsOut { float4 pos : SV_Position; float2 uv : TEXCOORD0; };\n" +
        "VsOut main(uint id : SV_VertexID) {\n" +
        "  float2 uv = float2((id << 1) & 2, id & 2);\n" +
        "  VsOut o;\n" +
        "  o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);\n" +
        "  o.uv = float2(uv.x, 1 - uv.y);\n" +
        "  return o;\n" +
        "}\n";

    static byte[] vertexSpirv;

    public static void CompilePending(string assetFolder)
    {
        var compiler = Find("ClientPlugin.Rendering.Shaders.VulkanShaderCompiler");
        if (compiler == null)
            return;
        string cache = Path.Combine(Path.GetTempPath(), "anomaly-guest-spirv");
        Directory.CreateDirectory(cache);
        object bundled = compiler.GetMethod("CreateBundled", BindingFlags.Public | BindingFlags.Static)
            ?.Invoke(null, new object[] { cache });
        if (bundled == null)
            return;
        if (vertexSpirv == null)
            vertexSpirv = Compile(bundled, WriteTemp("anomaly-guest-vs.hlsl", VertexSource), "main", "vs_5_0", assetFolder);
        PrepareVolume(assetFolder);
        string celestialIncludes = WriteCelestialIncludes();
        foreach (var program in GuestPassList.Snapshot())
        {
            if (program.Pipeline != 0 || program.Status == "compile-failed" || program.Status == "descriptor-deferred" || program.Status == "pipeline")
                continue;
            if (program.File == null || !File.Exists(program.File) || vertexSpirv == null)
            {
                program.Status = "compile-skipped";
                continue;
            }

            bool compute = (program.Flags & GuestBridgeClient.Compute) != 0;
            string firstInclude = (program.Flags & GuestBridgeClient.DepthTest) != 0 ? celestialIncludes : null;
            program.VertexSpirv = compute ? new byte[] { 1, 0, 0, 0 } : vertexSpirv;
            if (compute)
            {
                program.PixelSpirv = Compile(bundled, program.File, "main", "cs_5_0", assetFolder, firstInclude);
                program.PixelEntry = "main";
                if (program.PixelSpirv == null)
                {
                    program.PixelSpirv = Compile(bundled, program.File, "__compute_shader", "cs_5_0", assetFolder, firstInclude);
                    program.PixelEntry = "__compute_shader";
                }
            }
            else
            {
                program.PixelSpirv = Compile(bundled, program.File, "main", "ps_5_0", assetFolder, firstInclude);
                program.PixelEntry = "main";
                if (program.PixelSpirv == null)
                {
                    program.PixelSpirv = Compile(bundled, program.File, "__pixel_shader", "ps_5_0", assetFolder, firstInclude);
                    program.PixelEntry = "__pixel_shader";
                }
            }
            if (program.PixelSpirv == null)
            {
                program.Status = "compile-failed";
                continue;
            }

            string vsEntry = compute ? "compute" : "main";
            program.Pipeline = GuestBridgeClient.CreatePipeline(program.VertexSpirv, program.PixelSpirv, vsEntry, program.PixelEntry);
            program.Status = program.Pipeline != 0 ? "pipeline" : "descriptor-deferred";
        }
        foreach (var attachment in GuestPassList.SnapshotAttachments())
        {
            if (attachment.Slot < 0)
                continue;
            GuestBridgeClient.PublishAttachment(attachment.Name, attachment.Slot);
            if (attachment.PixelSpirv != null || string.IsNullOrEmpty(attachment.File) || !File.Exists(attachment.File))
                continue;
            attachment.PixelSpirv = Compile(bundled, attachment.File, "__pixel_shader", "ps_5_0", assetFolder);
            if (attachment.PixelSpirv == null)
            {
                attachment.Status = "compile-failed";
                continue;
            }
            attachment.Status = GuestBridgeClient.InitGBufferAttachment(attachment.Slot, attachment.NativeFormat, attachment.PixelSpirv) ? "pipeline" : "inject-failed";
        }
    }

    static byte[] Compile(object compiler, string source, string entry, string profile, string assetFolder, string firstInclude = null)
    {
        var requestType = Find("ClientPlugin.Rendering.Shaders.VulkanShaderRequest");
        if (requestType == null)
            return null;
        var request = Activator.CreateInstance(requestType);
        Set(request, "SourcePath", source);
        Set(request, "EntryPoint", entry);
        Set(request, "Profile", profile);
        var defines = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ANOMALY"] = "1",
            ["ANOMALY_COMPOSITE"] = "1",
        };
        Set(request, "Defines", defines);
        Set(request, "NativeAbiVersion", (uint)1);
        var includes = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(firstInclude))
            includes.Add(firstInclude);
        string sourceDir = Path.GetDirectoryName(source);
        if (!string.IsNullOrEmpty(sourceDir))
            includes.Add(sourceDir);
        if (!string.IsNullOrEmpty(assetFolder))
        {
            includes.Add(assetFolder);
            includes.Add(Path.Combine(assetFolder, "Shaders"));
        }
        Set(request, "IncludeDirectories", includes);
        var compile = compiler.GetType().GetMethod("Compile", BindingFlags.Public | BindingFlags.Instance);
        object result;
        try { result = compile?.Invoke(compiler, new[] { request }); }
        catch { return null; }
        var path = result?.GetType().GetField("SpirvPath")?.GetValue(result) as string
            ?? result?.GetType().GetProperty("SpirvPath")?.GetValue(result) as string;
        return path != null && File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    static string WriteTemp(string name, string source)
    {
        string path = Path.Combine(Path.GetTempPath(), name);
        File.WriteAllText(path, source);
        return path;
    }

    static void Set(object target, string name, object value)
    {
        var type = target.GetType();
        var field = type.GetField(name);
        if (field != null) { field.SetValue(target, value); return; }
        type.GetProperty(name)?.SetValue(target, value);
    }

    static Type Find(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type;
            try { type = assembly.GetType(name, false); }
            catch { continue; }
            if (type != null)
                return type;
        }

        return null;
    }

    static void PrepareVolume(string assetFolder)
    {
        foreach (var program in GuestPassList.Snapshot())
        {
            if (program == null || (program.Flags & GuestBridgeClient.Volume) == 0)
                continue;
            string source = program.SourceFile ?? program.File;
            if (string.IsNullOrEmpty(source) || !source.EndsWith(".hlsli", StringComparison.OrdinalIgnoreCase))
                continue;
            string generated = Path.Combine(Path.GetTempPath(), "anomaly-volume-" + Safe(program.Id) + ".hlsl");
            if (!string.Equals(program.File, generated, StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(generated, VolumeCompute(source, assetFolder));
                GuestPassList.SetFile(program.Id, generated);
            }
            string present = program.Id + ".present";
            if (!GuestPassList.Contains(present))
                GuestPassList.AddProgram(present, "AfterAtmosphere", WriteTemp("anomaly-volume-present.hlsl", VolumePresent), "IsolatedAdd");
        }
    }

    static string VolumeCompute(string provider, string assetFolder)
    {
        string medium = string.IsNullOrEmpty(assetFolder) ? "AnomalyMedium.hlsli" : Path.Combine(assetFolder, "Shaders", "AnomalyMedium.hlsli");
        string extras = string.IsNullOrEmpty(assetFolder) ? "PassExtrasCb.hlsli" : Path.Combine(assetFolder, "Shaders", "Anomaly", "PassExtrasCb.hlsli");
        return
            "#define MERGE(a,b) a##b\n" +
            "#include \"" + medium.Replace('\\', '/') + "\"\n" +
            "#include \"" + extras.Replace('\\', '/') + "\"\n" +
            "cbuffer VolumeOrigin : register(b0) { float3 MediumOrigin; float _originPad; float3 MediumMotion; float _motionPad; };\n" +
            "cbuffer VolumeProviders : register(b7) { float4 VolumeUniforms[16]; };\n" +
            "#define EvaluateMedium VolumeProvider0\n" +
            "#define MediumUniforms VolumeUniforms\n" +
            "#define MediumCameraPosition (-MediumOrigin)\n" +
            "#define MediumVelocity MediumMotion\n" +
            "#define MediumRegister0 t11\n#define MediumRegister1 t12\n#define MediumRegister2 t13\n" +
            "#define MediumTexture0 VolumeTex0\n#define MediumTexture1 VolumeTex1\n#define MediumTexture2 VolumeTex2\n" +
            "Texture3D VolumeTex0 : register(t11);\nTexture3D VolumeTex1 : register(t12);\nTexture3D VolumeTex2 : register(t13);\n" +
            "#include \"" + provider.Replace('\\', '/') + "\"\n" +
            "#undef EvaluateMedium\n" +
            "RWTexture2D<float4> VolumeScatter : register(u0);\n" +
            "[numthreads(8,8,1)] void __compute_shader(uint3 id : SV_DispatchThreadID) {\n" +
            "  uint w, h; VolumeScatter.GetDimensions(w, h); if (id.x >= w || id.y >= h) return;\n" +
            "  float2 uv = (id.xy + 0.5) * AnomalyInvSceneSize;\n" +
            "  float3 view = float3((uv * float2(2, -2) + float2(-1, 1)) / max(AnomalyProjScale, float2(1e-4, 1e-4)), -1);\n" +
            "  float3 ray = normalize(mul(float4(view, 0), AnomalyCameraToWorld).xyz);\n" +
            "  float3 camera = float3(AnomalyCameraToWorld[0][3], AnomalyCameraToWorld[1][3], AnomalyCameraToWorld[2][3]);\n" +
            "  float3 sun = normalize(AnomalySunToward); float3 sunLight = max(AnomalySunColor * AnomalySunDiffuse, 0);\n" +
            "  float3 accum = 0; float transmit = 1; float stepMeters = 250;\n" +
            "  [loop] for (int i = 0; i < 12 && transmit > 0.02; i++) {\n" +
            "    float3 local = camera + ray * (stepMeters * (i + 0.5)) - MediumOrigin;\n" +
            "    AnomalyMediumSample sample = VolumeProvider0(local);\n" +
            "    float sigma = max(sample.extinction, 0);\n" +
            "    if (!isfinite(sigma)) continue;\n" +
            "    float phase = AnomalyHenyeyGreenstein(dot(ray, sun), sample.anisotropy);\n" +
            "    float3 light = max(sample.emission, 0) + clamp(sample.scattering, 0, sigma) * sunLight * phase;\n" +
            "    accum += transmit * light * stepMeters;\n" +
            "    transmit *= exp(-sigma * stepMeters);\n" +
            "  }\n" +
            "  VolumeScatter[id.xy] = float4(max(accum, 0), 1 - transmit);\n" +
            "}\n";
    }

    const string VolumePresent =
        "Texture2D<float4> VolumeScatter : register(t7);\n" +
        "float4 __pixel_shader(float4 position : SV_Position) : SV_Target0 {\n" +
        "  return float4(VolumeScatter.Load(int3(uint2(position.xy), 0)).rgb, 0);\n" +
        "}\n";

    static string WriteCelestialIncludes()
    {
        string dir = Path.Combine(Path.GetTempPath(), "anomaly-guest-celestial");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Frame.hlsli"),
            "struct AnomalyGuestLight { float3 directionalLightVec; float SunDiscIntensity; float3 SunDiscColor; float skyboxBrightness; float3 directionalLightColor; float envSkyboxBrightness; };\n" +
            "struct AnomalyGuestFog { float3 color; float sky; };\n" +
            "struct AnomalyGuestEnv { row_major float4x4 inv_view_matrix; };\n" +
            "struct AnomalyGuestFrame { AnomalyGuestLight Light; AnomalyGuestFog Fog; float frameTime; float _pad0; float _pad1; float _pad2; AnomalyGuestEnv Environment; };\n" +
            "cbuffer AnomalyGuestFrameCB : register(b0) { AnomalyGuestFrame frame_; };\n");
        File.WriteAllText(Path.Combine(dir, "VertexTransformations.hlsli"), "\n");
        File.WriteAllText(Path.Combine(dir, "PixelUtils.hlsli"), "\n");
        File.WriteAllText(Path.Combine(dir, "AnomalyCelestial.hlsli"),
            "#ifndef ANOMALY_CELESTIAL_HLSLI\n#define ANOMALY_CELESTIAL_HLSLI\n#include \"Frame.hlsli\"\n" +
            "Texture2D<float> AnomalyCelestialDepth : register(t0);\n" +
            "StructuredBuffer<float4> AnomalyCelestialData : register(t1);\n" +
            "Texture2DArray<float4> AnomalyCelestialArt : register(t2);\n" +
            "SamplerState AnomalyCelestialArtSampler : register(s0);\n" +
            "cbuffer AnomalyCelestialView : register(b6) { float4 CelestialViewR0, CelestialViewR1, CelestialViewR2; float4 CelestialView; float4 CelestialDataInfo; };\n" +
            "cbuffer AnomalyCelestialUniforms : register(b7) { float4 CelestialUniform[16]; };\n" +
            "struct AnomalyCelestialInput { float3 direction, directionDx, directionDy, sunDirection, sunRadiance; float angularPixel; uint isProbe, dataCount; };\n" +
            "float AnomalyCelestialFootprint(float3 ray) { return max(max(length(ddx(ray)), length(ddy(ray))), 1e-8); }\n" +
            "float AnomalyCelestialDisc(float3 ray, float3 center, float radius, float pixelWidth) { float chord = length(ray - center); float edge = 2 * sin(radius * 0.5); return 1 - smoothstep(max(0, edge - pixelWidth * 0.5), edge + pixelWidth * 0.5, chord); }\n" +
            "#endif\n");
        File.WriteAllText(Path.Combine(dir, "AnomalyCelestialEntry.hlsli"),
            "float4 __pixel_shader(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target0 {\n" +
            "  AnomalyCelestialInput input;\n" +
            "  float2 ndc = uv * float2(2, -2) + float2(-1, 1);\n" +
            "  float3 localRay = float3(ndc / max(CelestialView.zw, float2(1e-4, 1e-4)), -1);\n" +
            "  input.direction = normalize(float3(dot(localRay, CelestialViewR0.xyz), dot(localRay, CelestialViewR1.xyz), dot(localRay, CelestialViewR2.xyz)));\n" +
            "  input.isProbe = 0;\n" +
            "  input.angularPixel = AnomalyCelestialFootprint(input.direction);\n" +
            "  input.directionDx = ddx(input.direction); input.directionDy = ddy(input.direction);\n" +
            "  input.sunDirection = normalize(-frame_.Light.directionalLightVec);\n" +
            "  input.sunRadiance = frame_.Light.SunDiscColor * frame_.Light.directionalLightColor * frame_.Light.SunDiscIntensity;\n" +
            "  input.dataCount = (uint)CelestialDataInfo.x;\n" +
            "  if (AnomalyCelestialDepth.Load(int3(uint2(position.xy), 0)) > 0) discard;\n" +
            "  float3 color = AnomalyEvaluateCelestial(input);\n" +
            "  if (!all(isfinite(color))) discard;\n" +
            "  color = lerp(max(color, 0), frame_.Fog.color, frame_.Fog.sky);\n" +
            "  return float4(max(color, 0), 1);\n" +
            "}\n");
        return dir;
    }

    static string Safe(string id)
    {
        if (string.IsNullOrEmpty(id))
            return "volume";
        var chars = id.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (!char.IsLetterOrDigit(chars[i]))
                chars[i] = '-';
        return new string(chars);
    }
}
