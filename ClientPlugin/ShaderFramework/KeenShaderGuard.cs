using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using VRage.Utils;
using VRageRender;

namespace ClientPlugin.ShaderFramework;

/// <summary>
/// Applies Anomaly's GBuffer delta to Keen's live <c>Content/Shaders</c> files.
/// The repo ships the transform, not Keen's sources. A hash mismatch disables
/// GBuffer injection instead of patching unknown content.
/// </summary>
static class KeenShaderGuard
{
    static readonly object Gate = new();
    static readonly Dictionary<string, byte[]> Patched =
        new(StringComparer.OrdinalIgnoreCase);

    public static bool GBufferPatchesReady { get; private set; }
    public static string LastError { get; private set; }
    public static string StatusLine { get; private set; } = "not prepared";

    public static void Prepare()
    {
        lock (Gate)
        {
            Patched.Clear();
            GBufferPatchesReady = false;
            LastError = null;
            StatusLine = "not prepared";

            string shadersRoot;
            try
            {
                shadersRoot = Path.GetFullPath(MyShaderCompiler.ShadersPath);
            }
            catch (Exception e)
            {
                Fail("Keen ShadersPath unavailable: " + e.Message);
                return;
            }

            if (string.IsNullOrEmpty(shadersRoot) || !Directory.Exists(shadersRoot))
            {
                Fail("Keen shader directory missing: " + shadersRoot);
                return;
            }

            try
            {
                PatchFile(shadersRoot, "Geometry/Passes/GBuffer/VertexStage.hlsli",
                    "2B0AF7CD0F1A65F0C813060F1BB77B81016181951E6AFE419F31C4D472EA15C4",
                    PatchVertexStage);
                PatchFile(shadersRoot, "Geometry/Passes/GBuffer/PixelStage.hlsli",
                    "7D9953942B84D44CFBDD0682D465B6017F1059DFE253F632095B33059CCCC133",
                    PatchPixelStage);
                PatchFile(shadersRoot, "GBuffer/GBufferWrite.hlsli",
                    "600067F3562DB23F20BC6D4D3EC012BD8C3C7046951E107DA6D87C21346D5F51",
                    PatchGBufferWrite);
                PatchFile(shadersRoot, "GBuffer/GBuffer.hlsli",
                    "275317C25DD1D5D45ED5BA3E43AEB6C3CE4AEF06D95AF31FDDD603CB7C275FEE",
                    PatchGBufferRead);
            }
            catch (Exception e)
            {
                Patched.Clear();
                Fail(e.Message);
                return;
            }

            GBufferPatchesReady = true;
            StatusLine = "patched " + Patched.Count + " Keen files";
            MyLog.Default.WriteLine("Anomaly: " + StatusLine);
            DebugLog.Write("KeenShaderGuard " + StatusLine);
        }
    }

    public static bool TryOpen(string relativeKey, out Stream stream)
    {
        stream = null;
        if (string.IsNullOrEmpty(relativeKey))
            return false;
        var key = NormalizeKey(relativeKey);
        lock (Gate)
        {
            if (!Patched.TryGetValue(key, out var bytes))
                return false;
            stream = new KeenPatchedIncludeStream(key, bytes);
            return true;
        }
    }

    static void PatchFile(string shadersRoot, string relative, string expectedSha256,
        Func<string, string> apply)
    {
        var diskPath = Path.GetFullPath(Path.Combine(shadersRoot,
            relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(diskPath))
            throw new InvalidOperationException("Keen file missing: " + relative);

        var raw = File.ReadAllBytes(diskPath);
        var actual = Sha256Hex(raw);
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Keen shader changed; GBuffer velocity disabled. File " + relative +
                " hash " + actual + " expected " + expectedSha256);
        }

        var text = Encoding.UTF8.GetString(raw).Replace("\r\n", "\n").Replace('\r', '\n');
        var patched = apply(text);
        Patched[NormalizeKey(relative)] = Encoding.UTF8.GetBytes(patched);
    }

    static string PatchVertexStage(string src)
    {
        src = ReplaceOnce(src,
            "#ifdef USE_SIMPLE_INSTANCING_COLORING // all other key colors and and emissivities are deprecated for simple instancing\n" +
            "	float4 instance_key_color_dithering : TEXCOORD10;\n" +
            "	float4 instance_color_mult_emissivity : TEXCOORD11;\n" +
            "#endif\n" +
            "};",
            "#ifdef USE_SIMPLE_INSTANCING_COLORING // all other key colors and and emissivities are deprecated for simple instancing\n" +
            "	float4 instance_key_color_dithering : TEXCOORD10;\n" +
            "	float4 instance_color_mult_emissivity : TEXCOORD11;\n" +
            "#endif\n" +
            "\n" +
            "#ifdef ANOMALY_VELOCITY\n" +
            "	float2 anomaly_velocity : TEXCOORD12;\n" +
            "#endif\n" +
            "};\n" +
            "\n" +
            "#include <Anomaly.hlsli>",
            "VertexStage interpolant");

        src = ReplaceOnce(src,
            "void __vertex_shader(__VertexInput input, out VertexStageOutput output, uint sv_vertex_id : SV_VertexID)\n" +
            "{\n" +
            "	VertexShaderInterface vertex = __prepare_interface(input, sv_vertex_id);",
            "void __vertex_shader(__VertexInput input, out VertexStageOutput output, uint sv_vertex_id : SV_VertexID, uint sv_instance_id : SV_InstanceID)\n" +
            "{\n" +
            "	VertexShaderInterface vertex = __prepare_interface(input, sv_vertex_id, sv_instance_id);",
            "VertexStage instance id");

        src = ReplaceOnce(src,
            "#if defined(BUILD_TANGENT_IN_PIXEL) || defined(WANTS_POSITION_WS)\n" +
            "	output.position_ws = vertex.position_local.xyz;\n" +
            "#endif\n" +
            "}",
            "#if defined(BUILD_TANGENT_IN_PIXEL) || defined(WANTS_POSITION_WS)\n" +
            "	output.position_ws = vertex.position_local.xyz;\n" +
            "#endif\n" +
            "\n" +
            "#ifdef ANOMALY_VELOCITY\n" +
            "#ifdef USE_SKINNING\n" +
            "	output.anomaly_velocity = AnomalyComputeVelocity(vertex.position_local.xyz, vertex._local_matrix, sv_instance_id, input.blend_indices, input.blend_weights);\n" +
            "#else\n" +
            "	output.anomaly_velocity = AnomalyComputeVelocity(vertex.position_local.xyz, vertex._local_matrix, sv_instance_id);\n" +
            "#endif\n" +
            "#endif\n" +
            "}",
            "VertexStage velocity");
        return src;
    }

    static string PatchPixelStage(string src)
    {
        src = ReplaceOnce(src,
            "	#ifdef USE_SIMPLE_INSTANCING_COLORING\n" +
            "		float4 instance_key_color_dithering : TEXCOORD10;\n" +
            "		float4 instance_color_mult_emissivity : TEXCOORD11;\n" +
            "	#endif\n" +
            "};\n" +
            "\n" +
            "#include <GBuffer/GBufferWrite.hlsli>",
            "	#ifdef USE_SIMPLE_INSTANCING_COLORING\n" +
            "		float4 instance_key_color_dithering : TEXCOORD10;\n" +
            "		float4 instance_color_mult_emissivity : TEXCOORD11;\n" +
            "	#endif\n" +
            "\n" +
            "#ifdef ANOMALY_VELOCITY\n" +
            "	float2 anomaly_velocity : TEXCOORD12;\n" +
            "#endif\n" +
            "};\n" +
            "\n" +
            "#define ANOMALY_PIXEL_STAGE\n" +
            "#include <Anomaly.hlsli>\n" +
            "\n" +
            "#ifdef ANOMALY_PACK_GBUFFER1A\n" +
            "#ifndef ANOMALY_GBUFFER1A_OVERRIDE\n" +
            "float AnomalyGBuffer1A(PixelInterface pixel, MaterialOutputInterface material_output)\n" +
            "{\n" +
            "	return 0;\n" +
            "}\n" +
            "#endif\n" +
            "#endif\n" +
            "\n" +
            "#include <GBuffer/GBufferWrite.hlsli>",
            "PixelStage interpolant");

        src = ReplaceOnce(src,
            "    ApplyMultipliers(material_output);\n" +
            "\n" +
            "	#ifdef STATIC_DECAL",
            "    ApplyMultipliers(material_output);\n" +
            "\n" +
            "#ifdef ANOMALY_VELOCITY\n" +
            "	float2 anomaly_velocity = input.anomaly_velocity;\n" +
            "#endif\n" +
            "\n" +
            "	#ifdef STATIC_DECAL",
            "PixelStage velocity read");

        src = ReplaceOnce(src,
            "            GbufferWriteBlend(output, material_output.base_color, material_output.metalness, material_output.normal, material_output.gloss, ao,\n" +
            "                material_output.emissive, decalAlpha, normalAlpha, 1, depth);",
            "            #ifdef ANOMALY_VELOCITY\n" +
            "            GbufferWriteBlend(output, material_output.base_color, material_output.metalness, material_output.normal, material_output.gloss, ao,\n" +
            "                material_output.emissive, decalAlpha, normalAlpha, 1, anomaly_velocity, depth);\n" +
            "            #else\n" +
            "            GbufferWriteBlend(output, material_output.base_color, material_output.metalness, material_output.normal, material_output.gloss, ao,\n" +
            "                material_output.emissive, decalAlpha, normalAlpha, 1, depth);\n" +
            "            #endif",
            "PixelStage blend custom depth");

        src = ReplaceOnce(src,
            "            GbufferWriteBlend(output, material_output.base_color, material_output.metalness, material_output.normal, material_output.gloss, ao,\n" +
            "                material_output.emissive, decalAlpha, normalAlpha, 1);",
            "            #ifdef ANOMALY_VELOCITY\n" +
            "            GbufferWriteBlend(output, material_output.base_color, material_output.metalness, material_output.normal, material_output.gloss, ao,\n" +
            "                material_output.emissive, decalAlpha, normalAlpha, 1, anomaly_velocity);\n" +
            "            #else\n" +
            "            GbufferWriteBlend(output, material_output.base_color, material_output.metalness, material_output.normal, material_output.gloss, ao,\n" +
            "                material_output.emissive, decalAlpha, normalAlpha, 1);\n" +
            "            #endif",
            "PixelStage blend");

        src = ReplaceOnce(src,
            "        GbufferWrite(output, material_output.base_color, material_output.metalness, material_output.gloss, material_output.normal, material_output.ao, material_output.emissive, material_output.coverage, material_output.LOD, depth);",
            "        #ifdef ANOMALY_VELOCITY\n" +
            "        GbufferWrite(output, material_output.base_color, material_output.metalness, material_output.gloss, material_output.normal, material_output.ao, material_output.emissive, material_output.coverage, material_output.LOD, anomaly_velocity, depth);\n" +
            "        #else\n" +
            "        GbufferWrite(output, material_output.base_color, material_output.metalness, material_output.gloss, material_output.normal, material_output.ao, material_output.emissive, material_output.coverage, material_output.LOD, depth);\n" +
            "        #endif",
            "PixelStage write custom depth");

        src = ReplaceOnce(src,
            "        GbufferWrite(output, material_output.base_color, material_output.metalness, material_output.gloss, material_output.normal, material_output.ao, material_output.emissive, material_output.coverage, material_output.LOD);\n" +
            "	#endif\n" +
            "}",
            "        #ifdef ANOMALY_VELOCITY\n" +
            "        GbufferWrite(output, material_output.base_color, material_output.metalness, material_output.gloss, material_output.normal, material_output.ao, material_output.emissive, material_output.coverage, material_output.LOD, anomaly_velocity);\n" +
            "        #else\n" +
            "        GbufferWrite(output, material_output.base_color, material_output.metalness, material_output.gloss, material_output.normal, material_output.ao, material_output.emissive, material_output.coverage, material_output.LOD);\n" +
            "        #endif\n" +
            "	#endif\n" +
            "\n" +
            "#ifndef STATIC_DECAL\n" +
            "#ifdef ANOMALY_PACK_GBUFFER1A\n" +
            "	output.gbuffer1.a = AnomalyGBuffer1A(pixel, material_output);\n" +
            "#endif\n" +
            "#endif\n" +
            "}",
            "PixelStage gbuffer1a");
        return src;
    }

    static string PatchGBufferWrite(string src)
    {
        src = ReplaceOnce(src,
            "    float4 gbuffer2 : SV_Target2;\n" +
            "#ifdef CUSTOM_DEPTH\n" +
            "	float depth : SV_Depth;\n" +
            "#endif\n" +
            "};",
            "    float4 gbuffer2 : SV_Target2;\n" +
            "#ifdef ANOMALY_VELOCITY\n" +
            "    float2 velocity : SV_Target3;\n" +
            "    float4 velocityAudit : SV_Target7;\n" +
            "#endif\n" +
            "#include <Anomaly/Extras/GBufferAttachmentFields.hlsli>\n" +
            "#ifdef CUSTOM_DEPTH\n" +
            "	float depth : SV_Depth;\n" +
            "#endif\n" +
            "};\n" +
            "\n" +
            "#include <Anomaly/Extras/GBufferAttachmentInit.hlsli>",
            "GBufferWrite targets");

        src = ReplaceOnce(src,
            "void GbufferWrite(out GbufferOutput output,\n" +
            "    float3 color, float metal, float gloss, float3 N, float ao, float emissive, uint coverage, uint lod\n" +
            "#ifdef CUSTOM_DEPTH\n" +
            "	, float depth\n" +
            "#endif\n" +
            "	)\n" +
            "{\n" +
            "    float3 nview = normalize(world_to_view(N));",
            "void GbufferWrite(out GbufferOutput output,\n" +
            "    float3 color, float metal, float gloss, float3 N, float ao, float emissive, uint coverage, uint lod\n" +
            "#ifdef ANOMALY_VELOCITY\n" +
            "    , float2 velocity\n" +
            "#endif\n" +
            "#ifdef CUSTOM_DEPTH\n" +
            "	, float depth\n" +
            "#endif\n" +
            "	)\n" +
            "{\n" +
            "    output = (GbufferOutput)0;\n" +
            "    AnomalyInitAttachments(output);\n" +
            "    float3 nview = normalize(world_to_view(N));",
            "GBufferWrite args");

        src = ReplaceOnce(src,
            "    output.gbuffer2 = float4(metal, gloss, emissive, coverage / 255.f);\n" +
            "\n" +
            "#ifdef CUSTOM_DEPTH\n" +
            "	output.depth = depth;\n" +
            "#endif\n" +
            "}",
            "    output.gbuffer2 = float4(metal, gloss, emissive, coverage / 255.f);\n" +
            "\n" +
            "#ifdef ANOMALY_VELOCITY\n" +
            "    output.velocity = AnomalyPixelProbe.z > 0.5 ? AnomalyPixelProbe.xy : velocity;\n" +
            "    output.velocityAudit = float4(1, AnomalyPixelProbe.z, velocity);\n" +
            "    if (AnomalyPixelProbe.z > 0.5)\n" +
            "        output.gbuffer0.rgb = float3(1, 0, 1);\n" +
            "#endif\n" +
            "\n" +
            "#ifdef CUSTOM_DEPTH\n" +
            "	output.depth = depth;\n" +
            "#endif\n" +
            "}",
            "GBufferWrite velocity");

        src = ReplaceOnce(src,
            "void GbufferWriteBlend(out GbufferOutput output,\n" +
            "    float3 color, float metal, float3 normal, float gloss, float ao, float emissive, float alpha, float alphaN, float fadeAlpha\n" +
            "#ifdef CUSTOM_DEPTH\n" +
            "    , float depth\n" +
            "#endif\n" +
            "    )\n" +
            "{\n" +
            "    output.gbuffer0 = float4(color, alpha) * fadeAlpha;",
            "void GbufferWriteBlend(out GbufferOutput output,\n" +
            "    float3 color, float metal, float3 normal, float gloss, float ao, float emissive, float alpha, float alphaN, float fadeAlpha\n" +
            "#ifdef ANOMALY_VELOCITY\n" +
            "    , float2 velocity\n" +
            "#endif\n" +
            "#ifdef CUSTOM_DEPTH\n" +
            "    , float depth\n" +
            "#endif\n" +
            "    )\n" +
            "{\n" +
            "    output = (GbufferOutput)0;\n" +
            "    AnomalyInitAttachments(output);\n" +
            "    output.gbuffer0 = float4(color, alpha) * fadeAlpha;",
            "GBufferWriteBlend args");

        src = ReplaceOnce(src,
            "    output.gbuffer2 = float4(metal, gloss, emissive, alpha) * fadeAlpha;\n" +
            "\n" +
            "#ifdef CUSTOM_DEPTH\n" +
            "    output.depth = depth;\n" +
            "#endif\n" +
            "}",
            "    output.gbuffer2 = float4(metal, gloss, emissive, alpha) * fadeAlpha;\n" +
            "\n" +
            "#ifdef ANOMALY_VELOCITY\n" +
            "    output.velocity = AnomalyPixelProbe.z > 0.5 ? AnomalyPixelProbe.xy : velocity;\n" +
            "    output.velocityAudit = float4(1, AnomalyPixelProbe.z, velocity);\n" +
            "    if (AnomalyPixelProbe.z > 0.5)\n" +
            "        output.gbuffer0.rgb = float3(1, 0, 1);\n" +
            "#endif\n" +
            "\n" +
            "#ifdef CUSTOM_DEPTH\n" +
            "    output.depth = depth;\n" +
            "#endif\n" +
            "}",
            "GBufferWriteBlend velocity");
        return src;
    }

    static string PatchGBufferRead(string src)
    {
        src = ReplaceOnce(src,
            "Texture2D<float> AOTexture : register( t12 );\n" +
            "\n" +
            "// data read from indexed buffer instead of gbuffer",
            "Texture2D<float> AOTexture : register( t12 );\n" +
            "\n" +
            "#include <Anomaly/LightingSlots.hlsli>\n" +
            "#include <Anomaly/Extras/GBufferReadSrvs.hlsli>\n" +
            "#include <Anomaly/Extras/GBufferAttachmentDefs.hlsli>\n" +
            "\n" +
            "// data read from indexed buffer instead of gbuffer",
            "GBuffer extras includes");

        src = ReplaceOnce(src,
            "	gbuffer.f0 = SurfaceF0(gbuffer.base_color, gbuffer.metalness);\n" +
            "\n" +
            "	return gbuffer;\n" +
            "}",
            "	gbuffer.f0 = SurfaceF0(gbuffer.base_color, gbuffer.metalness);\n" +
            "\n" +
            "	return gbuffer;\n" +
            "}\n" +
            "\n" +
            "#ifdef ANOMALY_VELOCITY\n" +
            "float2 AnomalyReadVelocity(uint2 screencoord)\n" +
            "{\n" +
            "	return AnomalyVelocityBuffer[screencoord].xy;\n" +
            "}\n" +
            "#endif",
            "GBuffer AnomalyReadVelocity");
        return src;
    }

    static string ReplaceOnce(string source, string old, string replacement, string label)
    {
        var index = source.IndexOf(old, StringComparison.Ordinal);
        if (index < 0)
            throw new InvalidOperationException("Keen patch failed (" + label + "): anchor not found");
        if (source.IndexOf(old, index + old.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("Keen patch failed (" + label + "): anchor is not unique");
        return source.Substring(0, index) + replacement + source.Substring(index + old.Length);
    }

    static void Fail(string message)
    {
        LastError = message;
        StatusLine = "disabled: " + message;
        GBufferPatchesReady = false;
        MyLog.Default.WriteLine("Anomaly: " + StatusLine);
        DebugLog.Write("KeenShaderGuard " + StatusLine);
    }

    static string NormalizeKey(string relative) =>
        relative.Replace('\\', '/').TrimStart('/');

    static string Sha256Hex(byte[] data)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(data);
        var sb = new StringBuilder(hash.Length * 2);
        for (var i = 0; i < hash.Length; i++)
            sb.Append(hash[i].ToString("X2"));
        return sb.ToString();
    }
}

sealed class KeenPatchedIncludeStream : MemoryStream
{
    public string VirtualPath { get; }

    public KeenPatchedIncludeStream(string virtualPath, byte[] bytes)
        : base(bytes, writable: false)
    {
        VirtualPath = virtualPath;
    }
}
