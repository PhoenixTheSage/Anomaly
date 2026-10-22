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

    static readonly Dictionary<string, string> WrapperHashes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Lighting/Light.hlsli"] = "A3E9ED30E5BB43DE8F5A2092CC04AA2317EAB26F33138A7E8CD7893A37895A8A",
        ["Transparent/Atmosphere/AtmosphereCommon.hlsli"] = "6779DECC5B4BAEB9456534CF7629AA66E1741EDF3ADD78391A94EE6E8FC9FDC3"
    };
    static readonly HashSet<string> ReadyWrappers = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsWrapperBlocked(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        key = NormalizeKey(key);
        lock (Gate) return WrapperHashes.ContainsKey(key) && !ReadyWrappers.Contains(key);
    }

    public static bool GBufferPatchesReady { get; private set; }
    public static string LastError { get; private set; }
    public static string StatusLine { get; private set; } = "not prepared";

    public static void Prepare()
    {
        lock (Gate)
        {
            Patched.Clear();
            ReadyWrappers.Clear();
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

            // Wrapper slots are independent of GBuffer velocity. Validate each original
            // before exposing Anomaly's fixed-slot additions to the include resolver.
            foreach (var wrapper in WrapperHashes)
            {
                try
                {
                    var raw = File.ReadAllBytes(Path.Combine(shadersRoot, wrapper.Key));
                    if (!string.Equals(Sha256Hex(raw), wrapper.Value, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("unrecognized Keen shader hash");
                    ReadyWrappers.Add(wrapper.Key);
                }
                catch (Exception e)
                {
                    MyLog.Default.WriteLine("Anomaly: wrapper disabled for " + wrapper.Key + ": " + e.Message);
                }
            }

            try
            {
                PatchFile(shadersRoot, "Geometry/Passes/GBuffer/VertexStage.hlsli",
                    "2B0AF7CD0F1A65F0C813060F1BB77B81016181951E6AFE419F31C4D472EA15C4",
                    ClientPlugin.Contracts.GBufferVelocitySource.PatchVertexStage);
                PatchFile(shadersRoot, "Geometry/Passes/GBuffer/PixelStage.hlsli",
                    "7D9953942B84D44CFBDD0682D465B6017F1059DFE253F632095B33059CCCC133",
                    ClientPlugin.Contracts.GBufferVelocitySource.PatchPixelStage);
                PatchFile(shadersRoot, "GBuffer/GBufferWrite.hlsli",
                    "600067F3562DB23F20BC6D4D3EC012BD8C3C7046951E107DA6D87C21346D5F51",
                    s => ClientPlugin.Contracts.GBufferVelocitySource.PatchGBufferWrite(s));
                PatchFile(shadersRoot, "GBuffer/GBuffer.hlsli",
                    "275317C25DD1D5D45ED5BA3E43AEB6C3CE4AEF06D95AF31FDDD603CB7C275FEE",
                    ClientPlugin.Contracts.GBufferVelocitySource.PatchGBufferRead);
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
