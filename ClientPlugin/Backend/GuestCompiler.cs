using System;
using System.IO;
using System.Reflection;

namespace ClientPlugin.Backend;

/// <summary>
/// Compiles pack fullscreen HLSL with SE-Vulkan's compiler. The fixed bus
/// (t0–t13, s0–s2, b0 owned or stock frame, b6 extras, b7 uniforms) gets a pipeline.
/// Native pipeline errors remain visible; a renderer that is not ready retries.
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
    static string loggedError;
    public static string LastError { get; private set; }

    public static bool HasPending
    {
        get
        {
            foreach (var program in GuestPassList.Snapshot())
                if (program.Pipeline == 0 && program.Status != "compile-failed" && program.Status != "pipeline-failed")
                    return true;
            foreach (var attachment in GuestPassList.SnapshotAttachments())
                if (attachment.Status != "pipeline" && attachment.Status != "compile-failed" && attachment.Status != "inject-failed")
                    return true;
            return false;
        }
    }

    static void Report(string message)
    {
        LastError = message;
        if (loggedError == message) return;
        loggedError = message;
        try { VRage.Utils.MyLog.Default.WriteLine("Anomaly guest compiler: " + message); } catch { }
    }

    public static void CompilePending(string assetFolder)
    {
        var compiler = Find("ClientPlugin.Rendering.Shaders.VulkanShaderCompiler");
        if (compiler == null)
        {
            Report("waiting for SE-Vulkan shader compiler");
            return;
        }
        string cache = Path.Combine(Path.GetTempPath(), "anomaly-guest-spirv");
        Directory.CreateDirectory(cache);
        object bundled;
        try { bundled = compiler.GetMethod("CreateBundled", BindingFlags.Public | BindingFlags.Static)
            ?.Invoke(null, new object[] { cache }); }
        catch (Exception error) { Report(error.GetBaseException().Message); return; }
        if (bundled == null)
            return;
        LastError = null;
        try
        {
            if (vertexSpirv == null)
                vertexSpirv = Compile(bundled, WriteTemp("anomaly-guest-vs.hlsl", VertexSource), "main", "vs_5_0", assetFolder);
            PrepareVolume(assetFolder);
            string celestialIncludes = WriteCelestialIncludes();
            foreach (var program in GuestPassList.Snapshot())
            {
                if (program.Pipeline != 0 || program.Status == "compile-failed" || program.Status == "pipeline-failed")
                    continue;
                if (program.File == null || !File.Exists(program.File) || vertexSpirv == null)
                {
                    program.Status = "compile-skipped";
                    continue;
                }

                bool compute = (program.Flags & GuestBridgeClient.Compute) != 0;
                string firstInclude = (program.Flags & GuestBridgeClient.DepthTest) != 0 ? celestialIncludes : null;
                if (compute) program.VertexSpirv = new byte[] { 1, 0, 0, 0 };
                else if (string.IsNullOrEmpty(program.VertexFile)) program.VertexSpirv = vertexSpirv;
                else if (program.VertexSpirv == null)
                    program.VertexSpirv = Compile(bundled, program.VertexFile, program.VertexEntry, "vs_5_0", assetFolder);
                if (program.VertexSpirv == null) { program.Status = "compile-failed"; program.Error = LastError; continue; }
                if (program.PixelSpirv == null)
                {
                    if (compute)
                    {
                        program.PixelSpirv = Compile(bundled, program.File, "main", "cs_5_0", assetFolder, firstInclude, false, program.IncludeRoot ?? Path.GetDirectoryName(program.SourceFile ?? program.File), program.Slot);
                        program.PixelEntry = "main";
                        if (program.PixelSpirv == null)
                        {
                            program.PixelSpirv = Compile(bundled, program.File, "__compute_shader", "cs_5_0", assetFolder, firstInclude, true, program.IncludeRoot ?? Path.GetDirectoryName(program.SourceFile ?? program.File), program.Slot);
                            program.PixelEntry = "__compute_shader";
                        }
                    }
                    else
                    {
                        program.PixelSpirv = Compile(bundled, program.File, "main", "ps_5_0", assetFolder, firstInclude, false, program.IncludeRoot ?? Path.GetDirectoryName(program.SourceFile ?? program.File), program.Slot);
                        program.PixelEntry = "main";
                        if (program.PixelSpirv == null)
                        {
                            program.PixelSpirv = Compile(bundled, program.File, "__pixel_shader", "ps_5_0", assetFolder, firstInclude, true, program.IncludeRoot ?? Path.GetDirectoryName(program.SourceFile ?? program.File), program.Slot);
                            program.PixelEntry = "__pixel_shader";
                        }
                    }
                    if (program.PixelSpirv == null)
                    {
                        program.Status = "compile-failed";
                        program.Error = LastError;
                        continue;
                    }
                }

                string vsEntry = compute ? "compute" : program.VertexEntry;
                try
                {
                    if (program.ResourceContract != 0 && !GuestBridgeClient.SupportsPacketFlags(512))
                    {
                        program.Status = "capability-missing";
                        program.Error = "shared volume resource transport requires updated SE-Vulkan native library";
                        continue;
                    }
                    program.Pipeline = program.ResourceContract == 0
                        ? GuestBridgeClient.CreatePipeline(program.VertexSpirv, program.PixelSpirv, vsEntry, program.PixelEntry)
                        : GuestBridgeClient.CreateExtendedPipeline(program.VertexSpirv, program.PixelSpirv, vsEntry, program.PixelEntry, program.ResourceContract);
                    program.Status = program.Pipeline != 0 ? "pipeline" : "renderer-pending";
                    if (program.Pipeline != 0) program.ResourceEpoch = GuestBridgeClient.ResourceEpoch;
                    if (program.Pipeline == 0 && GuestBridgeClient.TryGetFrame(new byte[344]))
                    {
                        program.Status = "pipeline-failed";
                        program.Error = "native pipeline rejected the shader descriptor layout";
                        Report(program.Id + ": " + program.Error);
                    }
                }
                catch (Exception error)
                {
                    program.Error = error.GetBaseException().Message;
                    program.Status = "pipeline-failed";
                    Report(program.Id + ": " + program.Error);
                }
            }
            foreach (var attachment in GuestPassList.SnapshotAttachments())
            {
                if (attachment.Slot < 0)
                    continue;
                GuestBridgeClient.PublishAttachment(attachment.Name, attachment.Slot);
                if (attachment.Status == "pipeline" || attachment.Status == "compile-failed" || attachment.Status == "inject-failed"
                    || string.IsNullOrEmpty(attachment.File) || !File.Exists(attachment.File))
                    continue;
                if (attachment.PixelSpirv == null)
                    attachment.PixelSpirv = Compile(bundled, attachment.File, "__pixel_shader", "ps_5_0", assetFolder);
                if (attachment.PixelSpirv == null)
                {
                    attachment.Status = "compile-failed";
                    continue;
                }
                attachment.Status = GuestBridgeClient.InitGBufferAttachment(attachment.Slot, attachment.NativeFormat, attachment.PixelSpirv) ? "pipeline" : "inject-failed";
            }
        }
        finally { (bundled as IDisposable)?.Dispose(); }
    }

    static byte[] Compile(object compiler, string source, string entry, string profile, string assetFolder, string firstInclude = null, bool reportFailure = true, string packRoot = null, string slot = null)
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
            ["ANOMALY_SPIRV"] = "1",
        };
        Set(request, "Defines", defines);
        if (!string.IsNullOrEmpty(slot)) defines["ANOMALY_FULLSCREEN_SLOT_" + slot.ToUpperInvariant()] = "1";
        Set(request, "NativeAbiVersion", (uint)1);
        var includes = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(firstInclude))
            includes.Add(firstInclude);
        string sourceDir = Path.GetDirectoryName(source);
        if (!string.IsNullOrEmpty(sourceDir))
            includes.Add(sourceDir);
        if (!string.IsNullOrEmpty(packRoot) && Directory.Exists(packRoot)) includes.Add(packRoot);
        if (!string.IsNullOrEmpty(packRoot) && Directory.Exists(Path.Combine(packRoot, "Inject"))) includes.Add(Path.Combine(packRoot, "Inject"));
        var fileSystem = Find("VRage.FileSystem.MyFileSystem");
        string content = fileSystem?.GetProperty("ContentPath", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
        if (!string.IsNullOrEmpty(content) && Directory.Exists(Path.Combine(content, "Shaders")))
            includes.Add(Path.Combine(content, "Shaders"));
        if (!string.IsNullOrEmpty(assetFolder) && Directory.Exists(assetFolder))
        {
            includes.Add(assetFolder);
            string shaders = Path.Combine(assetFolder, "Shaders");
            if (Directory.Exists(shaders)) includes.Add(shaders);
        }
        Set(request, "IncludeDirectories", includes);
        var compile = compiler.GetType().GetMethod("Compile", BindingFlags.Public | BindingFlags.Instance);
        object result;
        try { result = compile?.Invoke(compiler, new[] { request }); }
        catch (Exception error)
        {
            if (reportFailure) Report(source + " [" + entry + "]: " + error.GetBaseException().Message);
            return null;
        }
        var path = result?.GetType().GetField("SpirvPath")?.GetValue(result) as string
            ?? result?.GetType().GetProperty("SpirvPath")?.GetValue(result) as string;
        if (path == null || !File.Exists(path)) return null;
        // DXC's -fspv-reflect metadata is for offline reflection, not the
        // driver's guest allowlist. Use SE-Vulkan's stock runtime preparation.
        var prepare = Find("ClientPlugin.Rendering.Shaders.SpirvRuntimeModule")
            ?.GetMethod("Prepare", BindingFlags.Public | BindingFlags.Static);
        if (prepare == null) { Report("SE-Vulkan runtime SPIR-V preparation is unavailable"); return null; }
        return prepare.Invoke(null, new object[] { File.ReadAllBytes(path) }) as byte[];
    }

    static string WriteTemp(string name, string source)
    {
        string dir = Path.Combine(Path.GetTempPath(), "anomaly-guest-sources");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
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
            string generated = Path.Combine(Path.GetTempPath(), "anomaly-guest-sources", "anomaly-volume-" + Safe(program.Id) + ".hlsl");
            if (!string.Equals(program.File, generated, StringComparison.OrdinalIgnoreCase))
            {
                WriteTemp(Path.GetFileName(generated), VolumeCompute(source, assetFolder));
                GuestPassList.SetFile(program.Id, generated);
            }
            string present = program.Id + ".present";
            if (!GuestPassList.Contains(present))
                GuestPassList.AddProgram(present, "AfterAtmosphere", WriteTemp("anomaly-volume-present.hlsl", VolumePresent), "IsolatedAdd");
            GuestPassList.SetEnabled(present,program.Enabled);
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
            "[[vk::image_format(\"rgba16f\")]] RWTexture2D<float4> VolumeScatter : register(u0);\n" +
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
