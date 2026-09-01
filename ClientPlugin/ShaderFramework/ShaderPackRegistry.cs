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
/// Slice I: Standard Depth probe after apply; roll back the pack that
/// fails it. Overlay of Anomaly GBuffer stages needs
/// <c>exclusive: ["GBuffer"]</c>.
/// </summary>
public static class ShaderPackRegistry
{
    public const string ManifestName = "anomaly.json";
    public const string OverlayFolder = "Overlay";
    public const string InjectFolder = "Inject";
    public const string GeneratedExtrasPath = "Anomaly/GBufferExtras.hlsli";
    public const string GeneratedFingerprintPath = "Anomaly/PackFingerprint.hlsli";
    public const string ExclusiveGBuffer = "GBuffer";

    static readonly string[] OwnedGBufferStages =
    {
        "Geometry/Passes/GBuffer/VertexStage.hlsli",
        "Geometry/Passes/GBuffer/PixelStage.hlsli",
        "GBuffer/GBufferWrite.hlsli"
    };

    static readonly string DepthProbePixel =
        Path.Combine("Geometry", "Materials", "Standard", "Pixel.hlsl");
    static readonly string DepthProbeVertex =
        Path.Combine("Geometry", "Materials", "Standard", "Vertex.hlsl");

    static readonly object Gate = new();
    static readonly Dictionary<string, PendingPack> Pending = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string> OverlayFiles = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string> OverlayOwners = new(StringComparer.OrdinalIgnoreCase);
    static readonly List<string> PackIncludeDirs = new();
    static readonly UTF8Encoding Utf8 = new(false);

    static byte[] extrasBytes;
    static byte[] fingerprintBytes;
    static bool localScanned;
    static string extractRoot;
    static bool depthProbePending;
    static bool depthProbeInProgress;

    public static string Fingerprint { get; private set; } = "0";
    public static int LivePackCount { get; private set; }
    public static int ConflictCount { get; private set; }
    public static int RolledBackCount { get; private set; }
    public static string LastError { get; private set; }

    internal static bool DepthProbePending
    {
        get { lock (Gate) return depthProbePending; }
    }

    internal static bool DepthProbeInProgress
    {
        get { lock (Gate) return depthProbeInProgress; }
    }

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
            ValidateDepth();
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
        ValidateDepth();
    }

    /// <summary>
    /// Compile Keen Standard Depth after overlays are live. Safe after
    /// <c>MyShaderCompiler.Compile</c> returns (Harmony postfix) or from
    /// <c>Init</c> before the first frame — not from inside an in-flight compile.
    /// </summary>
    internal static void ValidateDepth()
    {
        lock (Gate)
            ValidateDepthUnlocked();
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
    /// Depth permutation failed in-game. Roll back the overlay owner, or
    /// every live overlay pack if the owner is unknown. Does not compile
    /// (Keen's static macro list is still in use on this stack).
    /// </summary>
    internal static void OnDepthCompileFailed(string filepath)
    {
        lock (Gate)
        {
            if (depthProbeInProgress)
                return;

            if (TryMatchOverlayOwner(filepath, out var ownerId) &&
                TryGetPack(ownerId, out var owner) && !owner.RolledBack)
            {
                owner.RolledBack = true;
                ApplyUnlocked();
                Warn("rolled back pack '" + owner.ManifestId + "' after Depth compile failure of " +
                     (filepath ?? "(unknown)"));
                return;
            }

            var suspects = CollectOverlaySuspects(filepath);
            if (suspects.Count == 0)
            {
                Warn("Depth compile failed with no overlay owner: " + (filepath ?? "(unknown)"));
                return;
            }

            foreach (var pack in suspects)
                pack.RolledBack = true;
            ApplyUnlocked();
            Warn("rolled back " + suspects.Count + " overlay pack(s) after Depth compile failure: " +
                 JoinPackIds(suspects) + " file=" + (filepath ?? "(unknown)"));
        }
    }

    internal static bool TryOpenGenerated(string relativeKey, out Stream stream)
    {
        stream = null;
        lock (Gate)
        {
            EnsureStubsUnlocked();
            byte[] bytes;
            if (KeysEqual(relativeKey, GeneratedFingerprintPath))
                bytes = fingerprintBytes;
            else if (KeysEqual(relativeKey, GeneratedExtrasPath))
                bytes = extrasBytes;
            else
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
        string shadersRoot;
        try
        {
            shadersRoot = Path.GetFullPath(MyShaderCompiler.ShadersPath);
        }
        catch
        {
            return false;
        }

        var full = Path.GetFullPath(filepath);
        if (!TryRelativize(shadersRoot, full, out var rel))
            return false;
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
            if (IsOwnedGBufferStage(kv.Key) && !HasExclusiveStage(owner.Exclusive, ExclusiveGBuffer))
            {
                Warn("pack " + owner.ManifestId +
                     " overlay of Anomaly GBuffer stage requires exclusive: [\"" +
                     ExclusiveGBuffer + "\"]: " + kv.Key);
                continue;
            }

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

        var extras = new StringBuilder();
        extras.AppendLine("#ifndef ANOMALY_GBUFFER_EXTRAS_HLSLI");
        extras.AppendLine("#define ANOMALY_GBUFFER_EXTRAS_HLSLI");
        var live = 0;
        using (var fingerprintStream = new MemoryStream())
        {
            foreach (var pack in packs)
            {
                if (pack.Disabled)
                    continue;
                live++;
                WriteUtf8(fingerprintStream, pack.ManifestId);
                WriteUtf8(fingerprintStream, pack.Root);
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
                    WriteFile(fingerprintStream, inject);
                    extras.Append("// pack ").Append(pack.ManifestId).Append(" ")
                        .AppendLine(Path.GetFileName(inject));
                    try
                    {
                        extras.AppendLine(File.ReadAllText(inject));
                    }
                    catch (Exception e)
                    {
                        extras.Append("// failed to read: ").AppendLine(e.Message);
                    }
                }
            }

            extras.AppendLine("#endif");
            extrasBytes = Utf8.GetBytes(extras.ToString());
            fingerprintStream.Position = 0;
            using (var sha = SHA256.Create())
                Fingerprint = ToHex(sha.ComputeHash(fingerprintStream));
        }

        LivePackCount = live;
        RolledBackCount = CountRolledBack(packs);
        fingerprintBytes = Utf8.GetBytes(
            "#ifndef ANOMALY_PACK_FINGERPRINT_HLSLI\n" +
            "#define ANOMALY_PACK_FINGERPRINT_HLSLI\n" +
            "// anomaly-packs " + Fingerprint + "\n" +
            "#endif\n");

        ShaderCompileIntercept.SetPackIncludeDirectories(PackIncludeDirs);
        Log("packs applied live=" + LivePackCount + " overlays=" + OverlayFiles.Count +
            " conflicts=" + ConflictCount + " rolled-back=" + RolledBackCount + " fp=" + Fingerprint);
    }

    static void EnsureStubsUnlocked()
    {
        if (fingerprintBytes != null && extrasBytes != null)
            return;
        extrasBytes = Utf8.GetBytes(
            "#ifndef ANOMALY_GBUFFER_EXTRAS_HLSLI\n#define ANOMALY_GBUFFER_EXTRAS_HLSLI\n#endif\n");
        fingerprintBytes = Utf8.GetBytes(
            "#ifndef ANOMALY_PACK_FINGERPRINT_HLSLI\n#define ANOMALY_PACK_FINGERPRINT_HLSLI\n// anomaly-packs none\n#endif\n");
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
            Root = root,
            Local = local,
            Overlays = new List<OverlayFile>(),
            InjectFiles = new List<string>()
        };

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
                if (!TryNormalizeKey(rel, out var key))
                {
                    Warn("pack " + pack.ManifestId + " skipped overlay path: " + rel);
                    continue;
                }

                if (IsOwnedGBufferStage(key) && !HasExclusiveStage(pack.Exclusive, ExclusiveGBuffer))
                {
                    Warn("pack " + pack.ManifestId +
                         " overlay of Anomaly GBuffer stage requires exclusive: [\"" +
                         ExclusiveGBuffer + "\"]: " + key);
                    continue;
                }

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
                pack.InjectFiles.Add(full);
            }

            pack.InjectFiles.Sort(StringComparer.OrdinalIgnoreCase);
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

    static void ValidateDepthUnlocked()
    {
        if (depthProbeInProgress)
            return;
        if (OverlayFiles.Count == 0)
        {
            depthProbePending = false;
            return;
        }

        if (!ShaderCompileIntercept.IsLive || !TryDepthProbePaths(out var pixel, out var vertex))
        {
            depthProbePending = true;
            return;
        }

        depthProbeInProgress = true;
        try
        {
            if (ProbeDepthCore(pixel, vertex))
            {
                depthProbePending = false;
                Log("Depth probe ok (Standard DEPTH_ONLY)");
                return;
            }

            IsolateDepthFailureUnlocked(pixel, vertex);
            depthProbePending = false;
        }
        finally
        {
            depthProbeInProgress = false;
        }
    }

    static void IsolateDepthFailureUnlocked(string pixel, string vertex)
    {
        var candidates = CollectLiveOverlayPacks();
        if (candidates.Count == 0)
        {
            Warn("Depth probe failed with no live overlay packs — Keen/Anomaly baseline");
            return;
        }

        candidates.Sort(ComparePacks);
        for (var i = candidates.Count - 1; i >= 0; i--)
        {
            var pack = candidates[i];
            pack.RolledBack = true;
            ApplyUnlocked();
            if (ProbeDepthCore(pixel, vertex))
            {
                Warn("rolled back pack '" + pack.ManifestId + "' after Depth probe failure");
                return;
            }

            pack.RolledBack = false;
        }

        foreach (var pack in candidates)
            pack.RolledBack = true;
        ApplyUnlocked();
        if (ProbeDepthCore(pixel, vertex))
        {
            Warn("rolled back all overlay packs after Depth probe failure: " + JoinPackIds(candidates));
            return;
        }

        foreach (var pack in candidates)
            pack.RolledBack = false;
        ApplyUnlocked();
        Warn("Depth probe failed with overlays disabled — Keen/Anomaly baseline");
    }

    static bool ProbeDepthCore(string pixel, string vertex)
    {
        try
        {
            var macros = new[]
            {
                new ShaderMacro("DEPTH_ONLY", "1"),
                new ShaderMacro(ShaderCompileIntercept.RenderingPassMacro, "1")
            };
            var ps = MyShaderCompiler.Compile(pixel, macros, MyShaderProfile.ps_5_0,
                "Anomaly.DepthProbe.Standard.Pixel", invalidateCache: false);
            if (ps == null || ps.Length == 0)
                return false;
            if (string.IsNullOrEmpty(vertex) || !File.Exists(vertex))
                return true;
            var vs = MyShaderCompiler.Compile(vertex, macros, MyShaderProfile.vs_5_0,
                "Anomaly.DepthProbe.Standard.Vertex", invalidateCache: false);
            return vs != null && vs.Length != 0;
        }
        catch (Exception e)
        {
            Warn("Depth probe exception: " + e.GetType().Name + ": " + e.Message);
            return false;
        }
    }

    static bool TryDepthProbePaths(out string pixel, out string vertex)
    {
        pixel = null;
        vertex = null;
        try
        {
            var root = MyShaderCompiler.ShadersPath;
            if (string.IsNullOrEmpty(root))
                return false;
            pixel = Path.Combine(root, DepthProbePixel);
            vertex = Path.Combine(root, DepthProbeVertex);
            return File.Exists(pixel);
        }
        catch
        {
            return false;
        }
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
        var n = (key ?? "").Replace('\\', '/');
        return n.IndexOf("/Depth", StringComparison.OrdinalIgnoreCase) >= 0 ||
               n.StartsWith("Depth", StringComparison.OrdinalIgnoreCase) ||
               n.IndexOf("Geometry/Materials/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               n.IndexOf("Geometry/Passes/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               n.IndexOf("PixelTemplate", StringComparison.OrdinalIgnoreCase) >= 0 ||
               n.IndexOf("VertexTemplate", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static bool IsOwnedGBufferStage(string key)
    {
        for (var i = 0; i < OwnedGBufferStages.Length; i++)
        {
            if (KeysEqual(key, OwnedGBufferStages[i]))
                return true;
        }

        return false;
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
        var depth = n.IndexOf("/Depth", StringComparison.OrdinalIgnoreCase) >= 0 ||
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
            Exclusive = ReadJsonStringArray(json, "exclusive")
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
        public string Root;
        public bool Local;
        public bool Disabled;
        public bool RolledBack;
        public List<OverlayFile> Overlays;
        public List<string> InjectFiles;
    }

    struct OverlayFile
    {
        public string Key;
        public string FullPath;
    }

    sealed class Manifest
    {
        public string Id;
        public string Name;
        public int Priority;
        public string[] Exclusive;
    }
}
