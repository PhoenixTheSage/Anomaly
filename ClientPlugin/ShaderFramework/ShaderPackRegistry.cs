using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ClientPlugin.ShaderFramework;
using SharpDX.Direct3D;
using VRage.Utils;
using VRageRender;

namespace ClientPlugin.Shaders;

/// <summary>
/// Well-known type for pack plugins. Resolve by name:
/// <c>ClientPlugin.Shaders.ShaderPackRegistry</c> — do not take a
/// compile-time reference to Anomaly. Call <see cref="Register"/> from
/// the pack's <c>LoadAssets</c>. Instantiate order is not dependency-sorted.
/// Slice L: sentinel compile per live named stage; roll back the pack that
/// fails it. Overlay of Anomaly GBuffer <em>write</em> stages needs
/// <c>exclusive: ["GBuffer"]</c>. Read wraps need <c>GBuffer</c> or
/// <c>Lighting</c>; <c>Lighting/Light.hlsli</c> needs <c>Lighting</c>;
/// <c>Transparent/Atmosphere/AtmosphereCommon.hlsli</c> needs <c>Atmosphere</c>.
/// Slice J: <see cref="ShaderStages"/> maps named overlay folders to Keen paths.
/// Slices M–T plus owned-pass / temporal / catalog publish: stage-scoped inject,
/// pack defines, <see cref="GBufferAttachments"/>, lighting and atmosphere wraps,
/// <see cref="ShaderBindRegistry"/>, <see cref="OwnedPassRegistry"/>,
/// <see cref="FullscreenPassRegistry"/> (<c>Fullscreen/&lt;Slot&gt;</c> +
/// <c>passes[]</c>), buffer catalog.
/// </summary>
public static class ShaderPackRegistry
{
    public const string ManifestName = "anomaly.json";
    public const string OverlayFolder = "Overlay";
    public const string InjectFolder = "Inject";
    public const string FullscreenFolder = "Fullscreen";
    public const string GeneratedExtrasPath = "Anomaly/GBufferExtras.hlsli";
    public const string GeneratedExtrasStagePath = "Anomaly/Extras/GBuffer.hlsli";
    public const string GeneratedFingerprintPath = "Anomaly/PackFingerprint.hlsli";
    public const string ExclusiveGBuffer = ShaderStages.GBuffer;
    public const string ExclusiveLighting = ShaderStages.Lighting;
    public const string ExclusiveAtmosphere = ShaderStages.Atmosphere;

    static readonly object Gate = new();
    static readonly Dictionary<string, PendingPack> Pending = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string> OverlayFiles = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string> OverlayOwners = new(StringComparer.OrdinalIgnoreCase);
    static readonly List<string> PackIncludeDirs = new();
    static readonly UTF8Encoding Utf8 = new(false);

    static byte[] extrasBytes;
    static byte[] fingerprintBytes;
    static readonly Dictionary<string, byte[]> GeneratedFiles = new(StringComparer.OrdinalIgnoreCase);
    static ShaderMacro[] liveDefineMacros = Array.Empty<ShaderMacro>();
    static string liveDefinesStatus = "";
    static bool localScanned;
    static string extractRoot;
    static bool stageProbePending;
    static bool stageProbeInProgress;

    public static string Fingerprint { get; private set; } = "0";
    public static int LivePackCount { get; private set; }
    public static int ConflictCount { get; private set; }
    public static int RolledBackCount { get; private set; }
    public static string LiveStages { get; private set; } = "";
    public static string LastError { get; private set; }
    public static string LiveDefines => liveDefinesStatus;

    public static ShaderMacro[] LiveDefineMacros
    {
        get
        {
            lock (Gate)
                return liveDefineMacros;
        }
    }

    internal static bool StageProbePending
    {
        get { lock (Gate) return stageProbePending; }
    }

    internal static bool StageProbeInProgress
    {
        get { lock (Gate) return stageProbeInProgress; }
    }

    internal static bool DepthProbePending => StageProbePending;

    internal static bool DepthProbeInProgress => StageProbeInProgress;

    public static string StatusLine
    {
        get
        {
            lock (Gate)
            {
                if (LivePackCount == 0 && ConflictCount == 0 && RolledBackCount == 0)
                    return "none";
                var fp = Fingerprint;
                if (fp.Length > 8)
                    fp = fp.Substring(0, 8);
                var line = LivePackCount + " live  conflicts=" + ConflictCount + "  fp=" + fp;
                if (RolledBackCount > 0)
                    line += "  rolled-back=" + RolledBackCount;
                if (!string.IsNullOrEmpty(LiveStages))
                    line += "  stages=" + LiveStages;
                if (!string.IsNullOrEmpty(liveDefinesStatus) && liveDefinesStatus != "none")
                    line += "  defines=" + liveDefinesStatus;
                var attach = GBufferAttachments.StatusLine;
                if (!string.IsNullOrEmpty(attach) && attach != "none")
                    line += "  attach=" + attach;
                return line;
            }
        }
    }

    /// <summary>
    /// Pack plugins call this with their Pulsar asset path
    /// (<c>assets["AnomalyPack"]</c>). <paramref name="root"/> is a folder
    /// or a <c>.zip</c>.
    /// </summary>
    public static void Register(string id, string root)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(root))
            return;
        var applied = false;
        lock (Gate)
        {
            try
            {
                var resolved = ResolveRoot(root, id);
                if (resolved == null)
                    return;
                if (!TryReadManifest(Path.Combine(resolved, ManifestName), out var manifest))
                {
                    Warn("pack '" + id + "' missing or invalid " + ManifestName + " at " + resolved);
                    return;
                }

                var pack = ScanPack(id, resolved, manifest, local: id.StartsWith("local:", StringComparison.OrdinalIgnoreCase));
                Pending[pack.ManifestId] = pack;
                if (extrasBytes != null)
                {
                    ApplyUnlocked();
                    applied = true;
                }
            }
            catch (Exception e)
            {
                LastError = e.GetType().Name + ": " + e.Message;
                Warn("Register(" + id + "): " + LastError);
            }
        }

        if (applied)
            ValidateStages();
    }

    internal static void ScanLocalDrop(Func<string, string, string> getConfigPath)
    {
        if (getConfigPath == null)
            return;
        lock (Gate)
        {
            if (localScanned)
                return;
            localScanned = true;
            try
            {
                var anomalyDir = getConfigPath("Anomaly", null);
                if (string.IsNullOrEmpty(anomalyDir))
                    return;
                var packsDir = Path.Combine(anomalyDir, "Packs");
                Directory.CreateDirectory(packsDir);
                extractRoot = Path.Combine(anomalyDir, "Generated", "LocalZips");
                Directory.CreateDirectory(extractRoot);
                Log("local pack drop (developer-only): " + packsDir);

                foreach (var dir in Directory.GetDirectories(packsDir))
                    RegisterUnlocked("local:" + Path.GetFileName(dir), dir);
                foreach (var zip in Directory.GetFiles(packsDir, "*.zip"))
                    RegisterUnlocked("local:" + Path.GetFileNameWithoutExtension(zip), zip);
            }
            catch (Exception e)
            {
                LastError = "local drop: " + e.Message;
                Warn(LastError);
            }
        }
    }

    internal static void Apply()
    {
        lock (Gate)
            ApplyUnlocked();
        ValidateStages();
    }

    /// <summary>
    /// Compile a sentinel for each live named stage (and Standard Depth/GBuffer
    /// when an escape-hatch overlay can touch geometry). Safe after
    /// <c>MyShaderCompiler.Compile</c> returns or from <c>Init</c> — not from
    /// inside an in-flight compile.
    /// </summary>
    internal static void ValidateStages()
    {
        lock (Gate)
            ValidateStagesUnlocked();
    }

    internal static void ValidateDepth()
    {
        ValidateStages();
    }

    internal static string DescribeCompileOwners(string filepath)
    {
        lock (Gate)
        {
            if (TryMatchOverlayOwner(filepath, out var owner) && !string.IsNullOrEmpty(owner))
                return owner;
            return "none";
        }
    }

    internal static string DescribeLivePackIds()
    {
        lock (Gate)
            return LivePackIdsUnlocked();
    }

    /// <summary>
    /// Compile failed in-game. Roll back the overlay owner, or packs on the
    /// inferred named stage. Does not compile (Keen's static macro list).
    /// </summary>
    internal static void OnCompileFailed(string filepath, ShaderMacro[] macros)
    {
        lock (Gate)
        {
            if (stageProbeInProgress)
                return;

            if (TryMatchOverlayOwner(filepath, out var ownerId) &&
                TryGetPack(ownerId, out var owner) && !owner.RolledBack)
            {
                owner.RolledBack = true;
                ApplyUnlocked();
                Warn("rolled back pack '" + owner.ManifestId + "' after compile failure of " +
                     (filepath ?? "(unknown)"));
                return;
            }

            var stage = InferFailedStage(filepath, macros);
            var suspects = stage != null
                ? CollectStageSuspects(stage)
                : CollectOverlaySuspects(filepath);
            if (suspects.Count == 0)
            {
                Warn("compile failed with no overlay owner: " + (filepath ?? "(unknown)") +
                     (stage != null ? " stage=" + stage : ""));
                return;
            }

            foreach (var pack in suspects)
                pack.RolledBack = true;
            ApplyUnlocked();
            Warn("rolled back " + suspects.Count + " overlay pack(s) after compile failure: " +
                 JoinPackIds(suspects) + " file=" + (filepath ?? "(unknown)") +
                 (stage != null ? " stage=" + stage : ""));
        }
    }

    internal static void OnDepthCompileFailed(string filepath)
    {
        OnCompileFailed(filepath, null);
    }

    internal static bool TryOpenGenerated(string relativeKey, out Stream stream)
    {
        stream = null;
        if (GBufferAttachments.TryOpenGenerated(relativeKey, out stream))
            return true;
        lock (Gate)
        {
            EnsureStubsUnlocked();
            var key = NormalizeKeyOrEmpty(relativeKey);
            if (GeneratedFiles.TryGetValue(key, out var bytes) && bytes != null)
            {
                stream = new MemoryStream(bytes, writable: false);
                return true;
            }

            if (KeysEqual(relativeKey, GeneratedFingerprintPath))
                bytes = fingerprintBytes;
            else if (KeysEqual(relativeKey, GeneratedExtrasPath) ||
                     KeysEqual(relativeKey, GeneratedExtrasStagePath))
                bytes = extrasBytes;
            else
                return false;
            if (bytes == null)
                return false;
            stream = new MemoryStream(bytes, writable: false);
            return true;
        }
    }

    internal static bool TryResolveOverlay(string relativeKey, out string fullPath)
    {
        fullPath = null;
        if (string.IsNullOrEmpty(relativeKey))
            return false;
        lock (Gate)
            return OverlayFiles.TryGetValue(NormalizeKeyOrEmpty(relativeKey), out fullPath);
    }

    internal static bool TryRemapCompilePath(ref string filepath)
    {
        if (string.IsNullOrEmpty(filepath))
            return false;

        string full;
        try
        {
            full = Path.GetFullPath(filepath);
        }
        catch
        {
            return false;
        }

        string rel = null;
        try
        {
            var shadersRoot = Path.GetFullPath(MyShaderCompiler.ShadersPath);
            TryRelativize(shadersRoot, full, out rel);
        }
        catch
        {
            // ShadersPath not ready.
        }

        if (string.IsNullOrEmpty(rel))
        {
            var include = ShaderCompileIntercept.IncludeDirectory;
            if (string.IsNullOrEmpty(include))
                return false;
            try
            {
                if (!TryRelativize(Path.GetFullPath(include), full, out rel))
                    return false;
            }
            catch
            {
                return false;
            }
        }

        if (!TryResolveOverlay(rel, out var overlay) || string.IsNullOrEmpty(overlay))
            return false;
        filepath = overlay;
        return true;
    }

    internal static IReadOnlyList<string> IncludeDirectories
    {
        get
        {
            lock (Gate)
                return PackIncludeDirs.ToArray();
        }
    }

    static void RegisterUnlocked(string id, string root)
    {
        try
        {
            var resolved = ResolveRoot(root, id);
            if (resolved == null)
                return;
            if (!TryReadManifest(Path.Combine(resolved, ManifestName), out var manifest))
            {
                Warn("pack '" + id + "' missing or invalid " + ManifestName);
                return;
            }

            var pack = ScanPack(id, resolved, manifest, local: true);
            Pending[pack.ManifestId] = pack;
        }
        catch (Exception e)
        {
            Warn("local pack '" + id + "': " + e.Message);
        }
    }

    static void ApplyUnlocked()
    {
        EnsureStubsUnlocked();
        OverlayFiles.Clear();
        OverlayOwners.Clear();
        PackIncludeDirs.Clear();
        ConflictCount = 0;

        var packs = new List<PendingPack>(Pending.Values);
        packs.Sort(ComparePacks);

        var exclusiveOwners = new Dictionary<string, List<PendingPack>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packs)
        {
            pack.Disabled = pack.RolledBack;
            if (pack.Disabled || pack.Exclusive == null)
                continue;
            for (var i = 0; i < pack.Exclusive.Length; i++)
            {
                var stage = pack.Exclusive[i];
                if (string.IsNullOrWhiteSpace(stage))
                    continue;
                if (!exclusiveOwners.TryGetValue(stage, out var list))
                {
                    list = new List<PendingPack>();
                    exclusiveOwners[stage] = list;
                }

                list.Add(pack);
            }
        }

        foreach (var kv in exclusiveOwners)
        {
            if (kv.Value.Count < 2)
                continue;
            ConflictCount++;
            var names = new string[kv.Value.Count];
            for (var i = 0; i < kv.Value.Count; i++)
            {
                names[i] = kv.Value[i].ManifestId;
                kv.Value[i].Disabled = true;
            }

            Warn("exclusive stage '" + kv.Key + "' claimed by " + string.Join(", ", names) +
                 " — fail closed, those packs disabled");
        }

        var claims = new Dictionary<string, List<PendingPack>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packs)
        {
            if (pack.Disabled)
                continue;
            foreach (var overlay in pack.Overlays)
            {
                if (!claims.TryGetValue(overlay.Key, out var list))
                {
                    list = new List<PendingPack>();
                    claims[overlay.Key] = list;
                }

                list.Add(pack);
            }
        }

        foreach (var kv in claims)
        {
            if (kv.Value.Count != 1)
            {
                ConflictCount++;
                var names = new string[kv.Value.Count];
                for (var i = 0; i < kv.Value.Count; i++)
                    names[i] = kv.Value[i].ManifestId;
                Warn("overlay '" + kv.Key + "' claimed by " + string.Join(", ", names) +
                     " — fail closed, Keen/Anomaly default kept");
                continue;
            }

            var owner = kv.Value[0];
            if (!CanOverlayOwned(kv.Key, owner.Exclusive, owner.ManifestId))
                continue;

            string path = null;
            foreach (var overlay in owner.Overlays)
            {
                if (string.Equals(overlay.Key, kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    path = overlay.FullPath;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(path))
            {
                OverlayFiles[kv.Key] = path;
                OverlayOwners[kv.Key] = owner.ManifestId;
            }
        }

        var extrasByStage = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
        EnsureStageExtrasHeader(extrasByStage, ShaderStages.GBuffer);
        EnsureStageExtrasHeader(extrasByStage, ShaderStages.Lighting);
        var live = 0;
        var defineNames = new List<string>();
        var packAttachments = new List<GBufferAttachments.QueuedRequest>();
        using (var fingerprintStream = new MemoryStream())
        {
            foreach (var pack in packs)
            {
                if (pack.Disabled)
                    continue;
                live++;
                WriteUtf8(fingerprintStream, pack.ManifestId);
                WriteUtf8(fingerprintStream, pack.Root);
                MergePackDefines(pack, defineNames);
                CollectPackAttachments(pack, packAttachments);
                if (Directory.Exists(Path.Combine(pack.Root, InjectFolder)))
                    PackIncludeDirs.Add(Path.GetFullPath(Path.Combine(pack.Root, InjectFolder)));

                foreach (var overlay in pack.Overlays)
                {
                    if (!OverlayFiles.ContainsKey(overlay.Key))
                        continue;
                    WriteUtf8(fingerprintStream, overlay.Key);
                    WriteFile(fingerprintStream, overlay.FullPath);
                }

                foreach (var inject in pack.InjectFiles)
                {
                    WriteUtf8(fingerprintStream, inject.Stage);
                    WriteFile(fingerprintStream, inject.FullPath);
                    var stageExtras = EnsureStageExtrasHeader(extrasByStage, inject.Stage);
                    stageExtras.Append("// pack ").Append(pack.ManifestId).Append(" ")
                        .AppendLine(Path.GetFileName(inject.FullPath));
                    try
                    {
                        stageExtras.AppendLine(File.ReadAllText(inject.FullPath));
                    }
                    catch (Exception e)
                    {
                        stageExtras.Append("// failed to read: ").AppendLine(e.Message);
                    }
                }

                if (pack.FullscreenPrograms != null)
                {
                    foreach (var fs in pack.FullscreenPrograms)
                    {
                        WriteUtf8(fingerprintStream, "fullscreen:" + fs.Id);
                        WriteFile(fingerprintStream, fs.File);
                    }
                }
            }

            defineNames.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var name in defineNames)
                WriteUtf8(fingerprintStream, name);

            GBufferAttachments.SetPackRequests(packAttachments);
            GBufferAttachments.Assign();
            ConflictCount += GBufferAttachments.ConflictCount;
            GBufferAttachments.WriteFingerprint(fingerprintStream);

            CloseStageExtras(extrasByStage);
            WriteGeneratedExtras(extrasByStage);
            extrasBytes = GeneratedFiles.ContainsKey(NormalizeKeyOrEmpty(GeneratedExtrasStagePath))
                ? GeneratedFiles[NormalizeKeyOrEmpty(GeneratedExtrasStagePath)]
                : Utf8.GetBytes(EmptyStageExtras(ShaderStages.GBuffer));

            liveDefineMacros = ToDefineMacros(defineNames);
            liveDefinesStatus = defineNames.Count == 0 ? "none" : string.Join(",", defineNames);

            fingerprintStream.Position = 0;
            using (var sha = SHA256.Create())
                Fingerprint = ToHex(sha.ComputeHash(fingerprintStream));
        }

        LivePackCount = live;
        RolledBackCount = CountRolledBack(packs);
        LiveStages = CollectLiveStages();
        fingerprintBytes = Utf8.GetBytes(
            "#ifndef ANOMALY_PACK_FINGERPRINT_HLSLI\n" +
            "#define ANOMALY_PACK_FINGERPRINT_HLSLI\n" +
            "// anomaly-packs " + Fingerprint + "\n" +
            "#endif\n");
        GeneratedFiles[NormalizeKeyOrEmpty(GeneratedFingerprintPath)] = fingerprintBytes;

        ShaderCompileIntercept.SetPackIncludeDirectories(PackIncludeDirs);
        PushFullscreenUnlocked(packs);
        Log("packs applied live=" + LivePackCount + " overlays=" + OverlayFiles.Count +
            " conflicts=" + ConflictCount + " rolled-back=" + RolledBackCount +
            (string.IsNullOrEmpty(LiveStages) ? "" : " stages=" + LiveStages) +
            (liveDefinesStatus != "none" ? " defines=" + liveDefinesStatus : "") +
            (GBufferAttachments.StatusLine != "none" ? " attach=" + GBufferAttachments.StatusLine : "") +
            " fp=" + Fingerprint);
    }

    static void EnsureStubsUnlocked()
    {
        if (fingerprintBytes != null && extrasBytes != null && GeneratedFiles.Count > 0)
            return;
        extrasBytes = Utf8.GetBytes(EmptyStageExtras(ShaderStages.GBuffer));
        fingerprintBytes = Utf8.GetBytes(
            "#ifndef ANOMALY_PACK_FINGERPRINT_HLSLI\n#define ANOMALY_PACK_FINGERPRINT_HLSLI\n// anomaly-packs none\n#endif\n");
        GeneratedFiles[NormalizeKeyOrEmpty(GeneratedExtrasStagePath)] = extrasBytes;
        GeneratedFiles[NormalizeKeyOrEmpty(ShaderStages.ExtrasIncludePath(ShaderStages.Lighting))] =
            Utf8.GetBytes(EmptyStageExtras(ShaderStages.Lighting));
        GeneratedFiles[NormalizeKeyOrEmpty(GeneratedExtrasPath)] = Utf8.GetBytes(GBufferExtrasAlias());
        GeneratedFiles[NormalizeKeyOrEmpty(GeneratedFingerprintPath)] = fingerprintBytes;
    }

    static StringBuilder EnsureStageExtrasHeader(Dictionary<string, StringBuilder> map, string stage)
    {
        if (!map.TryGetValue(stage, out var sb))
        {
            sb = new StringBuilder();
            var guard = "ANOMALY_EXTRAS_" + stage.Replace(".", "_").ToUpperInvariant() + "_HLSLI";
            sb.AppendLine("#ifndef " + guard);
            sb.AppendLine("#define " + guard);
            map[stage] = sb;
        }

        return sb;
    }

    static void CloseStageExtras(Dictionary<string, StringBuilder> map)
    {
        foreach (var kv in map)
            kv.Value.AppendLine("#endif");
    }

    static void WriteGeneratedExtras(Dictionary<string, StringBuilder> map)
    {
        GeneratedFiles.Clear();
        foreach (var kv in map)
        {
            var path = ShaderStages.ExtrasIncludePath(kv.Key);
            GeneratedFiles[NormalizeKeyOrEmpty(path)] = Utf8.GetBytes(kv.Value.ToString());
        }

        if (!GeneratedFiles.ContainsKey(NormalizeKeyOrEmpty(GeneratedExtrasStagePath)))
            GeneratedFiles[NormalizeKeyOrEmpty(GeneratedExtrasStagePath)] =
                Utf8.GetBytes(EmptyStageExtras(ShaderStages.GBuffer));
        var lightingExtras = NormalizeKeyOrEmpty(ShaderStages.ExtrasIncludePath(ShaderStages.Lighting));
        if (!GeneratedFiles.ContainsKey(lightingExtras))
            GeneratedFiles[lightingExtras] = Utf8.GetBytes(EmptyStageExtras(ShaderStages.Lighting));
        var atmosphereExtras = NormalizeKeyOrEmpty(ShaderStages.ExtrasIncludePath(ShaderStages.Atmosphere));
        if (!GeneratedFiles.ContainsKey(atmosphereExtras))
            GeneratedFiles[atmosphereExtras] = Utf8.GetBytes(EmptyStageExtras(ShaderStages.Atmosphere));
        GeneratedFiles[NormalizeKeyOrEmpty(GeneratedExtrasPath)] = Utf8.GetBytes(GBufferExtrasAlias());
    }

    static string EmptyStageExtras(string stage)
    {
        var guard = "ANOMALY_EXTRAS_" + stage.Replace(".", "_").ToUpperInvariant() + "_HLSLI";
        return "#ifndef " + guard + "\n#define " + guard + "\n#endif\n";
    }

    static string GBufferExtrasAlias()
    {
        return "#ifndef ANOMALY_GBUFFER_EXTRAS_HLSLI\n" +
               "#define ANOMALY_GBUFFER_EXTRAS_HLSLI\n" +
               "#include <Anomaly/Extras/GBuffer.hlsli>\n" +
               "#include <Anomaly/Extras/GBufferAttachmentDefs.hlsli>\n" +
               "#endif\n";
    }

    static void MergePackDefines(PendingPack pack, List<string> dest)
    {
        if (pack.Defines == null)
            return;
        for (var i = 0; i < pack.Defines.Length; i++)
        {
            var name = pack.Defines[i];
            if (string.IsNullOrWhiteSpace(name))
                continue;
            name = name.Trim();
            var eq = name.IndexOf('=');
            if (eq > 0)
                name = name.Substring(0, eq).Trim();
            if (!GBufferAttachments.IsHlslIdent(name))
            {
                Warn("pack " + pack.ManifestId + " skipped invalid define '" + pack.Defines[i] + "'");
                continue;
            }

            if (GBufferAttachments.IsReservedDefine(name))
            {
                Warn("pack " + pack.ManifestId + " skipped reserved define '" + name + "'");
                continue;
            }

            var found = false;
            for (var d = 0; d < dest.Count; d++)
            {
                if (!string.Equals(dest[d], name, StringComparison.OrdinalIgnoreCase))
                    continue;
                found = true;
                break;
            }

            if (!found)
                dest.Add(name);
        }
    }

    static void CollectPackAttachments(PendingPack pack, List<GBufferAttachments.QueuedRequest> dest)
    {
        if (pack.Attachments == null)
            return;
        for (var i = 0; i < pack.Attachments.Length; i++)
        {
            var spec = pack.Attachments[i];
            if (spec == null)
                continue;
            if (!GBufferAttachments.TryNormalizeRequest(spec.Name, spec.Format, spec.Stage,
                    pack.ManifestId, out var queued, out var error))
            {
                Warn("pack " + pack.ManifestId + " " + error);
                continue;
            }

            dest.Add(queued);
        }
    }

    static ShaderMacro[] ToDefineMacros(List<string> names)
    {
        if (names == null || names.Count == 0)
            return Array.Empty<ShaderMacro>();
        var macros = new ShaderMacro[names.Count];
        for (var i = 0; i < names.Count; i++)
            macros[i] = new ShaderMacro(names[i], "1");
        return macros;
    }

    internal static void RefreshFingerprint()
    {
        lock (Gate)
        {
            if (extrasBytes == null)
                return;
            using (var fingerprintStream = new MemoryStream())
            {
                WriteUtf8(fingerprintStream, Fingerprint);
                GBufferAttachments.WriteFingerprint(fingerprintStream);
                foreach (var m in liveDefineMacros)
                    WriteUtf8(fingerprintStream, m.Name);
                fingerprintStream.Position = 0;
                using (var sha = SHA256.Create())
                    Fingerprint = ToHex(sha.ComputeHash(fingerprintStream));
            }

            fingerprintBytes = Utf8.GetBytes(
                "#ifndef ANOMALY_PACK_FINGERPRINT_HLSLI\n" +
                "#define ANOMALY_PACK_FINGERPRINT_HLSLI\n" +
                "// anomaly-packs " + Fingerprint + "\n" +
                "#endif\n");
            GeneratedFiles[NormalizeKeyOrEmpty(GeneratedFingerprintPath)] = fingerprintBytes;
        }
    }

    static void PushFullscreenUnlocked(List<PendingPack> packs)
    {
        var specs = new List<FullscreenProgramSpec>();
        for (var i = 0; i < packs.Count; i++)
        {
            var pack = packs[i];
            if (pack.Disabled || pack.FullscreenPrograms == null)
                continue;
            for (var p = 0; p < pack.FullscreenPrograms.Count; p++)
                specs.Add(pack.FullscreenPrograms[p]);
        }

        FullscreenPassRegistry.ReplaceAll(specs);
    }

    static void ScanFullscreenFolder(PendingPack pack)
    {
        var root = Path.Combine(pack.Root, FullscreenFolder);
        if (!Directory.Exists(root))
            return;
        root = Path.GetFullPath(root);
        foreach (var file in Directory.EnumerateFiles(root, "*.hlsl", SearchOption.AllDirectories))
        {
            var full = Path.GetFullPath(file);
            if (!IsUnderRoot(root, full))
                continue;
            if (!string.Equals(Path.GetExtension(full), ".hlsl", StringComparison.OrdinalIgnoreCase))
                continue;
            var rel = file.Substring(root.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!TryNormalizeKey(rel, out var key))
            {
                Warn("pack " + pack.ManifestId + " skipped fullscreen path: " + rel);
                continue;
            }

            var parts = key.Split('/');
            if (parts.Length != 2)
            {
                Warn("pack " + pack.ManifestId +
                     " skipped fullscreen (expected Fullscreen/<Slot>/<file>.hlsl): " + key);
                continue;
            }

            if (!OwnedPassRegistry.TryParseSlot(parts[0], out var slot))
            {
                Warn("pack " + pack.ManifestId + " skipped fullscreen unknown slot '" + parts[0] +
                     "'");
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(parts[1]);
            if (string.IsNullOrWhiteSpace(name))
                continue;
            var id = pack.ManifestId + "." + name;
            for (var i = 0; i < pack.FullscreenPrograms.Count; i++)
            {
                if (!string.Equals(pack.FullscreenPrograms[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    continue;
                id = pack.ManifestId + "." + slot + "." + name;
                break;
            }

            pack.FullscreenPrograms.Add(new FullscreenProgramSpec
            {
                Id = id,
                PackId = pack.ManifestId,
                Slot = slot,
                Compose = FullscreenCompose.IsolatedAdd,
                Priority = pack.Priority,
                Policy = TemporalPolicy.InColor,
                File = full,
                OutputName = "pass." + id
            });
        }
    }

    static void ApplyPassManifest(PendingPack pack, PassSpec[] passes)
    {
        if (passes == null || pack.FullscreenPrograms == null)
            return;
        for (var i = 0; i < passes.Length; i++)
        {
            var spec = passes[i];
            if (spec == null || string.IsNullOrWhiteSpace(spec.File))
            {
                Warn("pack " + pack.ManifestId + " skipped pass with no file");
                continue;
            }

            if (!TryNormalizeKey(spec.File, out var fileKey))
            {
                Warn("pack " + pack.ManifestId + " skipped pass path: " + spec.File);
                continue;
            }

            var full = CombineRelative(pack.Root, fileKey);
            if (!File.Exists(full) ||
                !string.Equals(Path.GetExtension(full), ".hlsl", StringComparison.OrdinalIgnoreCase))
            {
                Warn("pack " + pack.ManifestId + " skipped pass missing hlsl: " + fileKey);
                continue;
            }

            if (!OwnedPassRegistry.TryParseSlot(spec.Slot, out var slot))
            {
                Warn("pack " + pack.ManifestId + " skipped pass unknown slot '" + spec.Slot + "'");
                continue;
            }

            var compose = FullscreenCompose.IsolatedAdd;
            if (!string.IsNullOrWhiteSpace(spec.Compose) &&
                !Enum.TryParse(spec.Compose, true, out compose))
            {
                Warn("pack " + pack.ManifestId + " skipped pass unknown compose '" + spec.Compose +
                     "'");
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(full);
            var id = string.IsNullOrWhiteSpace(spec.Id)
                ? pack.ManifestId + "." + name
                : spec.Id.Trim();
            var output = string.IsNullOrWhiteSpace(spec.Output) ? "pass." + id : spec.Output.Trim();
            var priority = spec.Priority ?? pack.Priority;
            var policy = ParseTemporal(pack.ManifestId, spec.Temporal);
            FullscreenProgramSpec existing = null;
            for (var p = 0; p < pack.FullscreenPrograms.Count; p++)
            {
                var cur = pack.FullscreenPrograms[p];
                if (string.Equals(cur.File, full, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(cur.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    existing = cur;
                    break;
                }
            }

            if (existing == null)
            {
                existing = new FullscreenProgramSpec();
                pack.FullscreenPrograms.Add(existing);
            }

            existing.Id = id;
            existing.PackId = pack.ManifestId;
            existing.Slot = slot;
            existing.Compose = compose;
            existing.Priority = priority;
            existing.Policy = policy;
            existing.File = full;
            existing.OutputName = output;
        }
    }

    static TemporalPolicy ParseTemporal(string packId, string[] names)
    {
        if (names == null || names.Length == 0)
            return TemporalPolicy.InColor;
        TemporalPolicy policy = 0;
        for (var i = 0; i < names.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(names[i]))
                continue;
            if (!Enum.TryParse(names[i].Trim(), true, out TemporalPolicy flag))
            {
                Warn("pack " + packId + " skipped unknown temporal '" + names[i] + "'");
                continue;
            }

            policy |= flag;
        }

        return policy == 0 ? TemporalPolicy.InColor : policy;
    }

    static PassSpec[] ReadJsonPasses(string json)
    {
        var m = Regex.Match(json, "\"passes\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
        if (!m.Success)
            return null;
        var inner = m.Groups[1].Value;
        var objects = Regex.Matches(inner, "\\{[^}]*\\}");
        if (objects.Count == 0)
            return Array.Empty<PassSpec>();
        var list = new List<PassSpec>(objects.Count);
        for (var i = 0; i < objects.Count; i++)
        {
            var obj = objects[i].Value;
            var file = ReadJsonString(obj, "file");
            var slot = ReadJsonString(obj, "slot");
            if (string.IsNullOrWhiteSpace(file) || string.IsNullOrWhiteSpace(slot))
                continue;
            int? priority = null;
            var pri = ReadJsonNumber(obj, "priority");
            if (pri != null && int.TryParse(pri, out var parsed))
                priority = parsed;
            list.Add(new PassSpec
            {
                Id = ReadJsonString(obj, "id"),
                Slot = slot,
                File = file,
                Compose = ReadJsonString(obj, "compose"),
                Priority = priority,
                Temporal = ReadJsonStringArray(obj, "temporal"),
                Output = ReadJsonString(obj, "output")
            });
        }

        return list.ToArray();
    }

    static PendingPack ScanPack(string registerId, string root, Manifest manifest, bool local)
    {
        var pack = new PendingPack
        {
            RegisterId = registerId,
            ManifestId = string.IsNullOrWhiteSpace(manifest.Id) ? registerId : manifest.Id,
            Name = manifest.Name ?? registerId,
            Priority = manifest.Priority,
            Exclusive = manifest.Exclusive,
            Defines = manifest.Defines,
            Attachments = manifest.Attachments,
            Root = root,
            Local = local,
            Overlays = new List<OverlayFile>(),
            InjectFiles = new List<InjectFile>(),
            FullscreenPrograms = new List<FullscreenProgramSpec>()
        };
        ScanFullscreenFolder(pack);
        ApplyPassManifest(pack, manifest.Passes);

        var overlayRoot = Path.Combine(root, OverlayFolder);
        if (Directory.Exists(overlayRoot))
        {
            overlayRoot = Path.GetFullPath(overlayRoot);
            foreach (var file in Directory.EnumerateFiles(overlayRoot, "*", SearchOption.AllDirectories))
            {
                if (!IsShaderFile(file))
                    continue;
                var rel = file.Substring(overlayRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!TryNormalizeKey(rel, out var overlayRel))
                {
                    Warn("pack " + pack.ManifestId + " skipped overlay path: " + rel);
                    continue;
                }

                if (!ShaderStages.TryMapOverlayPath(overlayRel, out var key, out var stageName))
                {
                    Warn("pack " + pack.ManifestId + " skipped overlay (unknown named-stage file): " +
                         overlayRel);
                    continue;
                }

                if (!string.IsNullOrEmpty(stageName) &&
                    !string.Equals(overlayRel, key, StringComparison.OrdinalIgnoreCase))
                    Log("pack " + pack.ManifestId + " stage " + stageName + " '" + overlayRel + "' → " + key);

                if (!CanOverlayOwned(key, pack.Exclusive, pack.ManifestId))
                    continue;

                var full = Path.GetFullPath(file);
                if (!IsUnderRoot(overlayRoot, full))
                    continue;
                if (IsForbiddenDepthMrt(key, full))
                {
                    Warn("pack " + pack.ManifestId + " Depth overlay must not add SV_Target3: " + key);
                    continue;
                }

                pack.Overlays.Add(new OverlayFile { Key = key, FullPath = full });
            }
        }

        var injectRoot = Path.Combine(root, InjectFolder);
        if (Directory.Exists(injectRoot))
        {
            injectRoot = Path.GetFullPath(injectRoot);
            foreach (var file in Directory.EnumerateFiles(injectRoot, "*", SearchOption.AllDirectories))
            {
                if (!IsShaderFile(file))
                    continue;
                var full = Path.GetFullPath(file);
                if (!IsUnderRoot(injectRoot, full))
                    continue;
                var rel = file.Substring(injectRoot.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!TryNormalizeKey(rel, out var injectRel))
                {
                    Warn("pack " + pack.ManifestId + " skipped inject path: " + rel);
                    continue;
                }

                if (!ShaderStages.TryMapInjectPath(injectRel, out var injectStage))
                {
                    Warn("pack " + pack.ManifestId + " skipped inject (unknown named-stage folder): " +
                         injectRel);
                    continue;
                }

                if (ShaderStages.IsForbiddenInjectStage(injectStage))
                {
                    Warn("pack " + pack.ManifestId + " skipped inject into Depth: " + injectRel);
                    continue;
                }

                pack.InjectFiles.Add(new InjectFile { Stage = injectStage, FullPath = full });
            }

            pack.InjectFiles.Sort((a, b) =>
            {
                var c = string.Compare(a.Stage, b.Stage, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase);
            });
        }

        return pack;
    }

    static string ResolveRoot(string root, string id)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root))
            return root;
        if (!File.Exists(root) ||
            !root.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            Warn("pack '" + id + "' root not found: " + root);
            return null;
        }

        var baseDir = extractRoot ?? Path.Combine(Path.GetTempPath(), "AnomalyPacks");
        var extract = Path.Combine(baseDir, HashFileName(root));
        if (!Directory.Exists(extract) || Directory.GetFileSystemEntries(extract).Length == 0)
        {
            if (Directory.Exists(extract))
                Directory.Delete(extract, true);
            Directory.CreateDirectory(extract);
            ZipFile.ExtractToDirectory(root, extract);
        }

        var nested = FindPackRoot(extract);
        return nested ?? extract;
    }

    static string FindPackRoot(string extract)
    {
        if (File.Exists(Path.Combine(extract, ManifestName)))
            return extract;
        var dirs = Directory.GetDirectories(extract);
        if (dirs.Length == 1 && File.Exists(Path.Combine(dirs[0], ManifestName)))
            return dirs[0];
        foreach (var dir in dirs)
        {
            if (File.Exists(Path.Combine(dir, ManifestName)))
                return dir;
        }

        return null;
    }

    static void ValidateStagesUnlocked()
    {
        if (stageProbeInProgress)
            return;
        if (!HasAnythingToProbeUnlocked())
        {
            stageProbePending = false;
            return;
        }

        if (!ShaderCompileIntercept.IsLive)
        {
            stageProbePending = true;
            return;
        }

        stageProbeInProgress = true;
        try
        {
            for (var round = 0; round < 8; round++)
            {
                if (!TryBuildRuntimeProbes(out var probes, out var notReady))
                {
                    if (notReady)
                    {
                        stageProbePending = true;
                        return;
                    }

                    stageProbePending = false;
                    return;
                }

                string failedStage = null;
                for (var i = 0; i < probes.Count; i++)
                {
                    if (CompileProbe(probes[i]))
                        continue;
                    failedStage = probes[i].Stage;
                    break;
                }

                if (failedStage == null)
                {
                    stageProbePending = false;
                    Log("stage probes ok (" + DescribeProbeStages(probes) + ")");
                    return;
                }

                var stageProbes = FilterProbes(probes, failedStage);
                if (!IsolateStageFailureUnlocked(failedStage, stageProbes))
                    return;
            }

            Warn("stage probes stopped after too many rollback rounds");
            stageProbePending = false;
        }
        finally
        {
            stageProbeInProgress = false;
        }
    }

    static bool IsolateStageFailureUnlocked(string stage, List<RuntimeProbe> stageProbes)
    {
        var candidates = CollectStageSuspects(stage);
        if (candidates.Count == 0)
            candidates = CollectLiveOverlayPacks();
        if (candidates.Count == 0)
        {
            Warn(stage + " probe failed with no live overlay packs — Keen/Anomaly baseline");
            return false;
        }

        candidates.Sort(ComparePacks);
        for (var i = candidates.Count - 1; i >= 0; i--)
        {
            var pack = candidates[i];
            pack.RolledBack = true;
            ApplyUnlocked();
            if (CompileProbeList(stageProbes))
            {
                Warn("rolled back pack '" + pack.ManifestId + "' after " + stage + " probe failure");
                return true;
            }

            pack.RolledBack = false;
        }

        foreach (var pack in candidates)
            pack.RolledBack = true;
        ApplyUnlocked();
        if (CompileProbeList(stageProbes))
        {
            Warn("rolled back all overlay packs after " + stage + " probe failure: " +
                 JoinPackIds(candidates));
            return true;
        }

        foreach (var pack in candidates)
            pack.RolledBack = false;
        ApplyUnlocked();
        Warn(stage + " probe failed with overlays disabled — Keen/Anomaly baseline");
        return false;
    }

    static bool CompileProbeList(List<RuntimeProbe> probes)
    {
        for (var i = 0; i < probes.Count; i++)
        {
            if (!CompileProbe(probes[i]))
                return false;
        }

        return true;
    }

    static bool CompileProbe(RuntimeProbe probe)
    {
        try
        {
            if (string.IsNullOrEmpty(probe.FullPath) || !File.Exists(probe.FullPath))
                return false;
            var bc = MyShaderCompiler.Compile(probe.FullPath, probe.Macros, probe.Profile,
                probe.Descriptor, invalidateCache: false);
            return bc != null && bc.Length != 0;
        }
        catch (Exception e)
        {
            Warn(probe.Stage + " probe exception: " + e.GetType().Name + ": " + e.Message);
            return false;
        }
    }

    static bool HasAnythingToProbeUnlocked()
    {
        if (OverlayFiles.Count > 0)
            return true;
        if (GBufferAttachments.HasColorTargets)
            return true;
        foreach (var pack in Pending.Values)
        {
            if (pack.Disabled)
                continue;
            if (pack.InjectFiles != null && pack.InjectFiles.Count > 0)
                return true;
            if (pack.Defines != null && pack.Defines.Length > 0)
                return true;
        }

        return false;
    }

    static bool TryBuildRuntimeProbes(out List<RuntimeProbe> probes, out bool notReady)
    {
        probes = new List<RuntimeProbe>();
        notReady = false;
        var stages = CollectStagesToProbe();
        if (stages.Count == 0)
            return false;

        string shadersRoot = null;
        try
        {
            shadersRoot = MyShaderCompiler.ShadersPath;
        }
        catch
        {
            shadersRoot = null;
        }

        var include = ShaderCompileIntercept.IncludeDirectory;
        for (var s = 0; s < stages.Count; s++)
        {
            if (!ShaderStages.TryGetSentinels(stages[s], out var specs))
                continue;
            for (var i = 0; i < specs.Length; i++)
            {
                var spec = specs[i];
                var root = spec.FromAnomalyInclude ? include : shadersRoot;
                if (string.IsNullOrEmpty(root))
                {
                    notReady = true;
                    return false;
                }

                var full = CombineRelative(root, spec.RelativePath);
                if (!File.Exists(full))
                {
                    notReady = true;
                    return false;
                }

                if (!TryParseProfile(spec.Profile, out var profile))
                    continue;
                probes.Add(new RuntimeProbe
                {
                    Stage = stages[s],
                    FullPath = full,
                    Profile = profile,
                    Macros = ParseMacros(spec.Macros),
                    Descriptor = spec.Descriptor
                });
            }
        }

        return probes.Count > 0;
    }

    static List<string> CollectStagesToProbe()
    {
        var names = new List<string>();
        var hasEscape = false;
        foreach (var key in OverlayFiles.Keys)
        {
            if (ShaderStages.IsAnomalyOwnedGBufferRead(key) ||
                ShaderStages.IsAnomalyOwnedLightingWrap(key))
            {
                AddLightingFamily(names);
                continue;
            }

            if (ShaderStages.IsAnomalyOwnedAtmosphereWrap(key))
            {
                AddUnique(names, ShaderStages.Atmosphere);
                continue;
            }

            if (ShaderStages.TryGetStageForKey(key, out var stage) && !string.IsNullOrEmpty(stage))
            {
                AddUnique(names, stage);
                continue;
            }

            hasEscape = true;
        }

        foreach (var pack in Pending.Values)
        {
            if (pack.Disabled)
                continue;
            foreach (var inject in pack.InjectFiles)
                AddStageOrLightingFamily(names, inject.Stage);
        }

        if (hasEscape)
        {
            AddUnique(names, ShaderStages.GBuffer);
            AddUnique(names, ShaderStages.Depth);
            AddUnique(names, ShaderStages.Forward);
            AddUnique(names, ShaderStages.Highlight);
            AddUnique(names, ShaderStages.Transparent);
            AddUnique(names, ShaderStages.TransparentForDecals);
        }

        if (names.Count == 0 && GBufferAttachments.HasColorTargets)
            AddUnique(names, ShaderStages.GBuffer);

        return names;
    }

    static void AddUnique(List<string> names, string stage)
    {
        for (var i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], stage, StringComparison.OrdinalIgnoreCase))
                return;
        }

        names.Add(stage);
    }

    static void AddStageOrLightingFamily(List<string> names, string stage)
    {
        if (string.Equals(stage, ShaderStages.Lighting, StringComparison.OrdinalIgnoreCase))
        {
            AddLightingFamily(names);
            return;
        }

        AddUnique(names, stage);
    }

    static void AddLightingFamily(List<string> names)
    {
        AddUnique(names, ShaderStages.LightingDir);
        AddUnique(names, ShaderStages.LightingPoint);
        AddUnique(names, ShaderStages.LightingSpot);
    }

    static List<RuntimeProbe> FilterProbes(List<RuntimeProbe> probes, string stage)
    {
        var list = new List<RuntimeProbe>();
        for (var i = 0; i < probes.Count; i++)
        {
            if (string.Equals(probes[i].Stage, stage, StringComparison.OrdinalIgnoreCase))
                list.Add(probes[i]);
        }

        return list;
    }

    static string DescribeProbeStages(List<RuntimeProbe> probes)
    {
        var names = new List<string>();
        for (var i = 0; i < probes.Count; i++)
            AddUnique(names, probes[i].Stage);
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join(",", names);
    }

    static string CombineRelative(string root, string relative)
    {
        var parts = relative.Replace('/', Path.DirectorySeparatorChar)
            .Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        var path = root;
        for (var i = 0; i < parts.Length; i++)
            path = Path.Combine(path, parts[i]);
        return Path.GetFullPath(path);
    }

    static bool TryParseProfile(string name, out MyShaderProfile profile)
    {
        profile = MyShaderProfile.ps_5_0;
        if (string.Equals(name, "vs_5_0", StringComparison.OrdinalIgnoreCase))
        {
            profile = MyShaderProfile.vs_5_0;
            return true;
        }

        if (string.Equals(name, "ps_5_0", StringComparison.OrdinalIgnoreCase))
        {
            profile = MyShaderProfile.ps_5_0;
            return true;
        }

        if (string.Equals(name, "gs_5_0", StringComparison.OrdinalIgnoreCase))
        {
            profile = MyShaderProfile.gs_5_0;
            return true;
        }

        if (string.Equals(name, "cs_5_0", StringComparison.OrdinalIgnoreCase))
        {
            profile = MyShaderProfile.cs_5_0;
            return true;
        }

        return false;
    }

    static ShaderMacro[] ParseMacros(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<ShaderMacro>();
        var parts = text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        var macros = new ShaderMacro[parts.Length];
        var n = 0;
        for (var i = 0; i < parts.Length; i++)
        {
            var p = parts[i].Trim();
            if (p.Length == 0)
                continue;
            var eq = p.IndexOf('=');
            if (eq <= 0)
            {
                macros[n++] = new ShaderMacro(p, "1");
                continue;
            }

            macros[n++] = new ShaderMacro(p.Substring(0, eq).Trim(), p.Substring(eq + 1).Trim());
        }

        if (n == macros.Length)
            return macros;
        var trimmed = new ShaderMacro[n];
        Array.Copy(macros, trimmed, n);
        return trimmed;
    }

    static string InferFailedStage(string filepath, ShaderMacro[] macros)
    {
        if (!string.IsNullOrEmpty(filepath))
        {
            try
            {
                var full = Path.GetFullPath(filepath);
                try
                {
                    var shadersRoot = Path.GetFullPath(MyShaderCompiler.ShadersPath);
                    if (TryRelativize(shadersRoot, full, out var rel) &&
                        ShaderStages.TryGetStageForKey(rel, out var fromKeen))
                        return fromKeen;
                }
                catch
                {
                    // ShadersPath not ready.
                }

                var include = ShaderCompileIntercept.IncludeDirectory;
                if (!string.IsNullOrEmpty(include) &&
                    TryRelativize(Path.GetFullPath(include), full, out var inc) &&
                    ShaderStages.TryGetStageForKey(inc, out var fromAnomaly))
                    return fromAnomaly;

                var file = Path.GetFileName(full);
                if (string.Equals(file, "CameraVelocity.hlsl", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(file, "Fullscreen.hlsl", StringComparison.OrdinalIgnoreCase))
                    return ShaderStages.AnomalyCameraVelocity;
                if (string.Equals(file, "LinearDepth.hlsl", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(file, "HiZDownsample.hlsl", StringComparison.OrdinalIgnoreCase))
                    return ShaderStages.AnomalyLinearDepth;
                if (string.Equals(file, "HistoryCopy.hlsl", StringComparison.OrdinalIgnoreCase))
                    return ShaderStages.AnomalyHistoryColor;
            }
            catch
            {
                // ignore
            }
        }

        return InferStageFromMacros(macros);
    }

    static string InferStageFromMacros(ShaderMacro[] macros)
    {
        if (macros == null)
            return null;
        var depthOnly = false;
        string pass = null;
        for (var i = 0; i < macros.Length; i++)
        {
            if (string.Equals(macros[i].Name, "DEPTH_ONLY", StringComparison.Ordinal))
                depthOnly = true;
            if (string.Equals(macros[i].Name, ShaderCompileIntercept.RenderingPassMacro,
                    StringComparison.Ordinal))
                pass = macros[i].Definition;
        }

        if (depthOnly || pass == "1")
            return ShaderStages.Depth;
        if (pass == "0")
            return ShaderStages.GBuffer;
        if (pass == "2")
            return ShaderStages.Forward;
        if (pass == "3")
            return ShaderStages.Highlight;
        if (pass == "5")
            return ShaderStages.Transparent;
        if (pass == "6")
            return ShaderStages.TransparentForDecals;
        return null;
    }

    sealed class RuntimeProbe
    {
        public string Stage;
        public string FullPath;
        public MyShaderProfile Profile;
        public ShaderMacro[] Macros;
        public string Descriptor;
    }

    static bool TryMatchOverlayOwner(string filepath, out string id)
    {
        id = null;
        if (string.IsNullOrEmpty(filepath))
            return false;

        string full;
        try
        {
            full = Path.GetFullPath(filepath);
        }
        catch
        {
            return false;
        }

        foreach (var kv in OverlayFiles)
        {
            try
            {
                if (!string.Equals(Path.GetFullPath(kv.Value), full, StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            catch
            {
                continue;
            }

            return OverlayOwners.TryGetValue(kv.Key, out id) && !string.IsNullOrEmpty(id);
        }

        try
        {
            var shadersRoot = Path.GetFullPath(MyShaderCompiler.ShadersPath);
            if (TryRelativize(shadersRoot, full, out var rel) &&
                OverlayOwners.TryGetValue(NormalizeKeyOrEmpty(rel), out id) &&
                !string.IsNullOrEmpty(id))
                return true;
        }
        catch
        {
            // ShadersPath not ready.
        }

        return false;
    }

    static bool TryGetPack(string manifestId, out PendingPack pack)
    {
        pack = null;
        return !string.IsNullOrEmpty(manifestId) && Pending.TryGetValue(manifestId, out pack);
    }

    static List<PendingPack> CollectOverlaySuspects(string filepath)
    {
        var suspects = new List<PendingPack>();
        if (TryMatchOverlayOwner(filepath, out var ownerId) && TryGetPack(ownerId, out var owner) &&
            !owner.Disabled)
        {
            suspects.Add(owner);
            return suspects;
        }

        foreach (var pack in Pending.Values)
        {
            if (pack.Disabled)
                continue;
            if (!PackTouchesDepth(pack))
                continue;
            suspects.Add(pack);
        }

        if (suspects.Count > 0)
            return suspects;

        return CollectLiveOverlayPacks();
    }

    static List<PendingPack> CollectStageSuspects(string stage)
    {
        var list = new List<PendingPack>();
        if (string.IsNullOrEmpty(stage))
            return list;
        foreach (var pack in Pending.Values)
        {
            if (pack.Disabled)
                continue;
            if (!PackTouchesStage(pack, stage))
                continue;
            list.Add(pack);
        }

        if (list.Count > 0)
            return list;
        if (!ShaderStages.IsGeometryStage(stage))
            return list;
        foreach (var pack in Pending.Values)
        {
            if (pack.Disabled)
                continue;
            if (!PackTouchesGeometryShared(pack))
                continue;
            list.Add(pack);
        }

        return list;
    }

    static bool PackTouchesStage(PendingPack pack, string stage)
    {
        foreach (var overlay in pack.Overlays)
        {
            if (!OverlayFiles.ContainsKey(overlay.Key))
                continue;
            if (ShaderStages.IsLightingFamily(stage) &&
                (ShaderStages.IsAnomalyOwnedGBufferRead(overlay.Key) ||
                 ShaderStages.IsAnomalyOwnedLightingWrap(overlay.Key)))
                return true;
            if (string.Equals(stage, ShaderStages.Atmosphere, StringComparison.OrdinalIgnoreCase) &&
                ShaderStages.IsAnomalyOwnedAtmosphereWrap(overlay.Key))
                return true;
            if (ShaderStages.TryGetStageForKey(overlay.Key, out var name) &&
                string.Equals(name, stage, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        foreach (var inject in pack.InjectFiles)
        {
            if (string.Equals(inject.Stage, stage, StringComparison.OrdinalIgnoreCase))
                return true;
            if (ShaderStages.IsLightingFamily(stage) &&
                string.Equals(inject.Stage, ShaderStages.Lighting, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static bool PackTouchesGeometryShared(PendingPack pack)
    {
        foreach (var overlay in pack.Overlays)
        {
            if (!OverlayFiles.ContainsKey(overlay.Key))
                continue;
            if (IsDepthRelatedKey(overlay.Key) &&
                !ShaderStages.TryGetStageForKey(overlay.Key, out _))
                return true;
        }

        return false;
    }

    static List<PendingPack> CollectLiveOverlayPacks()
    {
        var list = new List<PendingPack>();
        foreach (var pack in Pending.Values)
        {
            if (pack.Disabled)
                continue;
            var claimed = false;
            foreach (var overlay in pack.Overlays)
            {
                if (!OverlayFiles.ContainsKey(overlay.Key))
                    continue;
                claimed = true;
                break;
            }

            if (claimed)
                list.Add(pack);
        }

        return list;
    }

    static bool PackTouchesDepth(PendingPack pack)
    {
        foreach (var overlay in pack.Overlays)
        {
            if (!OverlayFiles.ContainsKey(overlay.Key))
                continue;
            if (IsDepthRelatedKey(overlay.Key))
                return true;
        }

        return false;
    }

    static bool IsDepthRelatedKey(string key)
    {
        if (ShaderStages.TryGetStageForKey(key, out var stage) &&
            string.Equals(stage, ShaderStages.Depth, StringComparison.OrdinalIgnoreCase))
            return true;
        var n = (key ?? "").Replace('\\', '/');
        return n.IndexOf("/Depth", StringComparison.OrdinalIgnoreCase) >= 0 ||
               n.StartsWith("Depth", StringComparison.OrdinalIgnoreCase) ||
               n.IndexOf("Geometry/Materials/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               n.IndexOf("Geometry/Passes/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               n.IndexOf("PixelTemplate", StringComparison.OrdinalIgnoreCase) >= 0 ||
               n.IndexOf("VertexTemplate", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool CanOverlayOwned(string key, string[] exclusive, string packId)
    {
        if (ShaderStages.IsAnomalyOwnedGBuffer(key))
        {
            if (HasExclusiveStage(exclusive, ExclusiveGBuffer))
                return true;
            Warn("pack " + packId +
                 " overlay of Anomaly GBuffer stage requires exclusive: [\"" +
                 ExclusiveGBuffer + "\"]: " + key);
            return false;
        }

        if (ShaderStages.IsAnomalyOwnedGBufferRead(key))
        {
            if (HasExclusiveStage(exclusive, ExclusiveGBuffer) ||
                HasExclusiveStage(exclusive, ExclusiveLighting))
                return true;
            Warn("pack " + packId +
                 " overlay of Anomaly GBuffer read wrap requires exclusive: [\"" +
                 ExclusiveGBuffer + "\"] or [\"" + ExclusiveLighting + "\"]: " + key);
            return false;
        }

        if (ShaderStages.IsAnomalyOwnedLightingWrap(key))
        {
            if (HasExclusiveStage(exclusive, ExclusiveLighting))
                return true;
            Warn("pack " + packId +
                 " overlay of Anomaly Lighting wrap requires exclusive: [\"" +
                 ExclusiveLighting + "\"]: " + key);
            return false;
        }

        if (ShaderStages.IsAnomalyOwnedAtmosphereWrap(key))
        {
            if (HasExclusiveStage(exclusive, ExclusiveAtmosphere))
                return true;
            Warn("pack " + packId +
                 " overlay of Anomaly Atmosphere wrap requires exclusive: [\"" +
                 ExclusiveAtmosphere + "\"]: " + key);
            return false;
        }

        return true;
    }

    static bool HasExclusiveStage(string[] exclusive, string stage)
    {
        if (exclusive == null || string.IsNullOrEmpty(stage))
            return false;
        for (var i = 0; i < exclusive.Length; i++)
        {
            if (string.Equals(exclusive[i], stage, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static string CollectLiveStages()
    {
        var names = new List<string>();
        foreach (var key in OverlayFiles.Keys)
        {
            if (ShaderStages.IsAnomalyOwnedLightingWrap(key) ||
                ShaderStages.IsAnomalyOwnedGBufferRead(key))
                AddUnique(names, ShaderStages.Lighting);
            if (ShaderStages.IsAnomalyOwnedAtmosphereWrap(key))
                AddUnique(names, ShaderStages.Atmosphere);
            if (!ShaderStages.TryGetStageForKey(key, out var stage) || string.IsNullOrEmpty(stage))
                continue;
            AddUnique(names, stage);
        }

        foreach (var pack in Pending.Values)
        {
            if (pack.Disabled)
                continue;
            if (pack.InjectFiles != null)
            {
                foreach (var inject in pack.InjectFiles)
                    AddUnique(names, inject.Stage);
            }

            if (pack.FullscreenPrograms != null && pack.FullscreenPrograms.Count > 0)
                AddUnique(names, "Fullscreen");
        }

        if (names.Count == 0)
            return "";
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join(",", names);
    }

    static int CountRolledBack(List<PendingPack> packs)
    {
        var n = 0;
        for (var i = 0; i < packs.Count; i++)
        {
            if (packs[i].RolledBack)
                n++;
        }

        return n;
    }

    static string LivePackIdsUnlocked()
    {
        var ids = new List<string>();
        foreach (var pack in Pending.Values)
        {
            if (pack.Disabled)
                continue;
            ids.Add(pack.ManifestId);
        }

        if (ids.Count == 0)
            return "none";
        ids.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join(",", ids);
    }

    static string JoinPackIds(List<PendingPack> packs)
    {
        var ids = new string[packs.Count];
        for (var i = 0; i < packs.Count; i++)
            ids[i] = packs[i].ManifestId;
        Array.Sort(ids, StringComparer.OrdinalIgnoreCase);
        return string.Join(",", ids);
    }

    static bool IsForbiddenDepthMrt(string key, string fullPath)
    {
        var n = key.Replace('\\', '/');
        var depth = ShaderStages.TryGetStageForKey(n, out var stage) &&
                    string.Equals(stage, ShaderStages.Depth, StringComparison.OrdinalIgnoreCase);
        if (!depth)
            depth = n.IndexOf("/Depth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.StartsWith("Depth", StringComparison.OrdinalIgnoreCase);
        if (!depth)
            return false;
        try
        {
            var text = File.ReadAllText(fullPath);
            return text.IndexOf("SV_Target3", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch
        {
            return true;
        }
    }

    static bool TryReadManifest(string path, out Manifest manifest)
    {
        manifest = null;
        if (!File.Exists(path))
            return false;
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch
        {
            return false;
        }

        var id = ReadJsonString(json, "id");
        if (string.IsNullOrWhiteSpace(id))
            return false;
        int.TryParse(ReadJsonNumber(json, "priority") ?? "0", out var priority);
        manifest = new Manifest
        {
            Id = id,
            Name = ReadJsonString(json, "name"),
            Priority = priority,
            Exclusive = ReadJsonStringArray(json, "exclusive"),
            Defines = ReadJsonStringArray(json, "defines"),
            Attachments = ReadJsonAttachments(json),
            Passes = ReadJsonPasses(json)
        };
        return true;
    }

    static string ReadJsonString(string json, string key)
    {
        var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"");
        return m.Success ? Regex.Unescape(m.Groups[1].Value) : null;
    }

    static string ReadJsonNumber(string json, string key)
    {
        var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?\\d+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    static string[] ReadJsonStringArray(string json, string key)
    {
        var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\[([^\\]]*)\\]");
        if (!m.Success)
            return null;
        var inner = m.Groups[1].Value;
        var matches = Regex.Matches(inner, "\"((?:\\\\.|[^\"])*)\"");
        if (matches.Count == 0)
            return Array.Empty<string>();
        var result = new string[matches.Count];
        for (var i = 0; i < matches.Count; i++)
            result[i] = Regex.Unescape(matches[i].Groups[1].Value);
        return result;
    }

    static AttachmentSpec[] ReadJsonAttachments(string json)
    {
        var m = Regex.Match(json, "\"attachments\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
        if (!m.Success)
            return null;
        var inner = m.Groups[1].Value;
        var objects = Regex.Matches(inner, "\\{[^}]*\\}");
        if (objects.Count == 0)
            return Array.Empty<AttachmentSpec>();
        var list = new List<AttachmentSpec>(objects.Count);
        for (var i = 0; i < objects.Count; i++)
        {
            var obj = objects[i].Value;
            var name = ReadJsonString(obj, "name");
            var format = ReadJsonString(obj, "format");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(format))
                continue;
            list.Add(new AttachmentSpec
            {
                Name = name,
                Format = format,
                Stage = ReadJsonString(obj, "stage")
            });
        }

        return list.ToArray();
    }

    static bool TryNormalizeKey(string relative, out string key)
    {
        key = null;
        if (string.IsNullOrWhiteSpace(relative))
            return false;
        var n = relative.Replace('\\', '/').Trim();
        if (n.StartsWith("/", StringComparison.Ordinal) || n.Contains(":"))
            return false;
        var parts = n.Split('/');
        var kept = new List<string>();
        foreach (var part in parts)
        {
            if (part.Length == 0 || part == ".")
                continue;
            if (part == "..")
                return false;
            kept.Add(part);
        }

        if (kept.Count == 0)
            return false;
        key = string.Join("/", kept);
        return true;
    }

    static string NormalizeKeyOrEmpty(string relative)
    {
        return TryNormalizeKey(relative, out var key) ? key : "";
    }

    static bool KeysEqual(string a, string b)
    {
        return string.Equals(NormalizeKeyOrEmpty(a), NormalizeKeyOrEmpty(b), StringComparison.OrdinalIgnoreCase);
    }

    static bool IsShaderFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".hlsl", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".hlsli", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".h", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsUnderRoot(string root, string fullPath)
    {
        var prefix = root;
        if (!prefix.EndsWith(Path.DirectorySeparatorChar.ToString()) &&
            !prefix.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            prefix += Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    static bool TryRelativize(string root, string fullPath, out string relative)
    {
        relative = null;
        if (!IsUnderRoot(root, fullPath) &&
            !string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            return false;
        relative = fullPath.Substring(root.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return relative.Length > 0;
    }

    static int ComparePacks(PendingPack a, PendingPack b)
    {
        var c = a.Priority.CompareTo(b.Priority);
        return c != 0 ? c : string.Compare(a.ManifestId, b.ManifestId, StringComparison.OrdinalIgnoreCase);
    }

    static string HashFileName(string path)
    {
        using (var sha = SHA256.Create())
        using (var fs = File.OpenRead(path))
            return ToHex(sha.ComputeHash(fs));
    }

    static void WriteUtf8(Stream stream, string text)
    {
        var bytes = Utf8.GetBytes(text ?? "");
        stream.Write(bytes, 0, bytes.Length);
        stream.WriteByte(0);
    }

    static void WriteFile(Stream stream, string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            stream.Write(bytes, 0, bytes.Length);
        }
        catch
        {
            WriteUtf8(stream, path);
        }
    }

    static string ToHex(byte[] hash)
    {
        var sb = new StringBuilder(hash.Length * 2);
        for (var i = 0; i < hash.Length; i++)
            sb.Append(hash[i].ToString("x2"));
        return sb.ToString();
    }

    static void Log(string message)
    {
        MyLog.Default.WriteLine("Anomaly shader packs: " + message);
        DebugLog.Write("ShaderPackRegistry " + message);
    }

    static void Warn(string message)
    {
        MyLog.Default.WriteLine("Anomaly shader packs: " + message);
        DebugLog.Write("ShaderPackRegistry WARN " + message);
    }

    sealed class PendingPack
    {
        public string RegisterId;
        public string ManifestId;
        public string Name;
        public int Priority;
        public string[] Exclusive;
        public string[] Defines;
        public AttachmentSpec[] Attachments;
        public string Root;
        public bool Local;
        public bool Disabled;
        public bool RolledBack;
        public List<OverlayFile> Overlays;
        public List<InjectFile> InjectFiles;
        public List<FullscreenProgramSpec> FullscreenPrograms;
    }

    struct OverlayFile
    {
        public string Key;
        public string FullPath;
    }

    struct InjectFile
    {
        public string Stage;
        public string FullPath;
    }

    sealed class AttachmentSpec
    {
        public string Name;
        public string Format;
        public string Stage;
    }

    sealed class Manifest
    {
        public string Id;
        public string Name;
        public int Priority;
        public string[] Exclusive;
        public string[] Defines;
        public AttachmentSpec[] Attachments;
        public PassSpec[] Passes;
    }

    sealed class PassSpec
    {
        public string Id;
        public string Slot;
        public string File;
        public string Compose;
        public int? Priority;
        public string[] Temporal;
        public string Output;
    }
}
