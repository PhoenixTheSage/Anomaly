// Slice A: Keen compile entry (VRage.Render11).
//
//   MyShaderCompiler.Compile(...)
//   - Source path: Path.Combine(ShadersPath, info.File) where ShadersPath is
//     MyFileSystem.ShadersBasePath + "Shaders" (Content/Shaders).
//   - Defines: per-permutation ShaderMacro[] plus GlobalShaderMacros (PC: empty).
//   - Includes: private List m_includes, default { ShadersPath }.
//     MyIncludeProcessor uses this list for #include <...> (system includes).
//   - Cache key: MyShaderCache.GetShaderHash(preprocessedSource, profile).
//     Unused macros (ANOMALY with no #ifdef) do not change the preprocess text,
//     so the Keen cache still hits. Overlay files that #include Anomaly.hlsli will miss.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using ClientPlugin.Shaders;
using HarmonyLib;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using VRage.Utils;
using VRageRender;

namespace ClientPlugin.ShaderFramework;

public static class ShaderCompileIntercept
{
    public const string MacroName = "ANOMALY";
    public const string MacroValue = "1";
    public const string VelocityMacroName = "ANOMALY_VELOCITY";
    public const string VelocityMacroValue = "1";
    public const string RenderingPassMacro = "RENDERING_PASS";

    public static bool IsLive { get; private set; }
    public static bool GBufferOverlayPresent { get; private set; }
    public static string IncludeDirectory { get; private set; }
    public static string LastError { get; private set; }
    public static int CompileCount => Volatile.Read(ref compileCount);
    public static int FailureCount => Volatile.Read(ref failureCount);

    private static int compileCount;
    private static int failureCount;
    private static string assetFolder;
    private static string includeDirectoryOverride;
    private static readonly List<string> PackIncludes = new();
    private static readonly object Gate = new();

    public static void SetAssetFolder(string folder)
    {
        assetFolder = folder;
    }

    public static void SetIncludeDirectory(string directory)
    {
        includeDirectoryOverride = directory;
    }

    public static void SetPackIncludeDirectories(IReadOnlyList<string> directories)
    {
        lock (Gate)
        {
            PackIncludes.Clear();
            if (directories != null)
            {
                foreach (var dir in directories)
                {
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        PackIncludes.Add(Path.GetFullPath(dir));
                }
            }

            if (IsLive)
            {
                try
                {
                    EnsureIncludePath();
                }
                catch (Exception e)
                {
                    DebugLog.Write("ShaderCompileIntercept pack includes: " + e.Message);
                }
            }
        }
    }

    public static void TryRemapSource(ref string filepath)
    {
        ShaderPackRegistry.TryRemapCompilePath(ref filepath);
    }

    public static void Activate()
    {
        lock (Gate)
        {
            LastError = null;
            IncludeDirectory = ResolveIncludeDirectory();
            if (string.IsNullOrEmpty(IncludeDirectory))
            {
                IsLive = false;
                LastError = "shader include directory not found";
                DebugLog.Write("ShaderCompileIntercept: " + LastError);
                MyLog.Default.WriteLine("Anomaly: " + LastError);
                return;
            }

            try
            {
                EnsureGlobalMacro();
                EnsureIncludePath();
                GBufferOverlayPresent = File.Exists(Path.Combine(IncludeDirectory,
                    "Geometry", "Passes", "GBuffer", "VertexStage.hlsli"));
                IsLive = true;
                MyLog.Default.WriteLine("Anomaly compile intercept live. Include: " + IncludeDirectory
                    + " GBuffer overlay=" + GBufferOverlayPresent);
                DebugLog.Write("ShaderCompileIntercept live include=" + IncludeDirectory);
            }
            catch (Exception e)
            {
                IsLive = false;
                LastError = e.GetType().Name + ": " + e.Message;
                MyLog.Default.WriteLine("Anomaly compile intercept failed: " + LastError);
                DebugLog.Write("ShaderCompileIntercept failed: " + e);
            }
        }
    }

    public static void NoteCompile(string filepath, ShaderMacro[] macros, MyShaderProfile profile, string sourceDescriptor, byte[] bytecode)
    {
        Interlocked.Increment(ref compileCount);
        if (bytecode != null && bytecode.Length != 0)
        {
            if (ShaderPackRegistry.DepthProbePending && !ShaderPackRegistry.DepthProbeInProgress)
                ShaderPackRegistry.ValidateDepth();
            return;
        }

        if (ShaderPackRegistry.DepthProbeInProgress)
            return;

        Interlocked.Increment(ref failureCount);
        var desc = sourceDescriptor ?? filepath ?? "(unknown)";
        var defines = MacrosToString(macros);
        var owner = ShaderPackRegistry.DescribeCompileOwners(filepath);
        var live = ShaderPackRegistry.DescribeLivePackIds();
        var msg = "Anomaly: shader compile failed " + desc + " profile=" + profile + " defines=[" + defines + "]"
            + " pack=" + owner + " live=" + live + " packs=" + ShaderPackRegistry.Fingerprint;
        MyLog.Default.WriteLine(msg);
        DebugLog.Write(msg);

        if (IsDepthPermutation(macros))
            ShaderPackRegistry.OnDepthCompileFailed(filepath);
    }

    public static void EnsureIncludes(IReadOnlyList<string> includes)
    {
        if (includes is not List<string> list)
            return;
        lock (Gate)
        {
            AddIncludeIfMissing(list, IncludeDirectory);
            foreach (var packDir in PackIncludes)
                AddIncludeIfMissing(list, packDir);
        }
    }

    /// <summary>
    /// GBuffer only (<c>RENDERING_PASS=0</c>). Depth / forward / highlight stay 3-attachment.
    /// </summary>
    public static void EnsureVelocityMacro(ref ShaderMacro[] macros)
    {
        EnsureVelocityMacro(null, ref macros);
    }

    public static void EnsureVelocityMacro(string filepath, ref ShaderMacro[] macros)
    {
        if (IsDepthPermutation(macros) || ContainsNamed(macros, VelocityMacroName))
            return;
        if (!IsGBufferPermutation(macros) && !IsGeometryWithoutPass(filepath, macros))
            return;
        macros = AppendMacro(macros, VelocityMacroName, VelocityMacroValue);
    }

    public static void EnsureVelocityMacro(List<ShaderMacro> macros)
    {
        if (macros == null || !IsGBufferPermutation(macros) || ContainsNamed(macros, VelocityMacroName))
            return;
        macros.Add(new ShaderMacro(VelocityMacroName, VelocityMacroValue));
    }

    /// <summary>
    /// Local Keen includes do not search <c>m_includes</c>. Prefix
    /// <c>MyIncludeProcessor.Open</c> so <c>Geometry/Passes/GBuffer/*.hlsli</c>
    /// overlays resolve without replacing the pass dispatcher (Depth cache stays valid).
    /// </summary>
    public static bool TryOpenOverlay(IncludeType includeType, string fileName, Stream parentStream, out Stream stream)
    {
        stream = null;
        if (string.IsNullOrEmpty(fileName))
            return false;
        if (fileName.IndexOf("..", StringComparison.Ordinal) >= 0)
            return false;

        string relativeKey = null;
        if (includeType == IncludeType.System)
        {
            relativeKey = fileName;
        }
        else
        {
            string parentDir = null;
            if (parentStream is FileStream parentFile && !string.IsNullOrEmpty(parentFile.Name))
                parentDir = Path.GetDirectoryName(parentFile.Name);
            if (!string.IsNullOrEmpty(parentDir))
            {
                var resolved = Path.GetFullPath(Path.Combine(parentDir, fileName));
                try
                {
                    var shadersRoot = Path.GetFullPath(MyShaderCompiler.ShadersPath);
                    if (TryRelativize(shadersRoot, resolved, out var rel))
                        relativeKey = rel;
                }
                catch
                {
                    // ShadersPath not ready yet.
                }

                if (relativeKey == null && !string.IsNullOrEmpty(IncludeDirectory) &&
                    TryRelativize(Path.GetFullPath(IncludeDirectory), resolved, out var relInclude))
                    relativeKey = relInclude;
            }
        }

        if (!string.IsNullOrEmpty(relativeKey) && ShaderPackRegistry.TryOpenGenerated(relativeKey, out stream))
            return true;
        if (!string.IsNullOrEmpty(relativeKey) && ShaderPackRegistry.TryResolveOverlay(relativeKey, out var packFile) &&
            File.Exists(packFile))
        {
            stream = new FileStream(packFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }

        if (string.IsNullOrEmpty(IncludeDirectory) || string.IsNullOrEmpty(fileName))
            return false;

        var includeRoot = Path.GetFullPath(IncludeDirectory);
        string overlayPath = null;

        if (includeType == IncludeType.System)
        {
            overlayPath = Path.GetFullPath(Path.Combine(includeRoot, fileName));
        }
        else if (!string.IsNullOrEmpty(relativeKey))
        {
            overlayPath = Path.GetFullPath(Path.Combine(includeRoot, relativeKey));
        }

        if (string.IsNullOrEmpty(overlayPath) || !File.Exists(overlayPath))
            return false;
        if (!IsUnderRoot(includeRoot, overlayPath))
            return false;

        stream = new FileStream(overlayPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return true;
    }

    public static void EnsureGlobalMacros(ref ShaderMacro[] macros)
    {
        if (ContainsNamed(macros, MacroName))
            return;

        lock (Gate)
        {
            var field = AccessTools.Field(typeof(MyShaderCompiler), "m_globalShaderMacros");
            var current = field?.GetValue(null) as ShaderMacro[] ?? macros ?? Array.Empty<ShaderMacro>();
            if (ContainsNamed(current, MacroName))
            {
                macros = current;
                return;
            }

            var next = AppendMacro(current, MacroName, MacroValue);
            field?.SetValue(null, next);
            macros = next;
        }
    }

    private static string ResolveIncludeDirectory()
    {
        foreach (var candidate in IncludeCandidates())
        {
            if (!string.IsNullOrEmpty(candidate) && Directory.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    private static IEnumerable<string> IncludeCandidates()
    {
        if (!string.IsNullOrEmpty(includeDirectoryOverride))
            yield return includeDirectoryOverride;

        if (!string.IsNullOrEmpty(assetFolder))
        {
            yield return Path.Combine(assetFolder, "Shaders");
            yield return assetFolder;
        }

        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrEmpty(asmDir))
            yield return Path.Combine(asmDir, "Shaders");
    }

    private static void EnsureGlobalMacro()
    {
        var field = AccessTools.Field(typeof(MyShaderCompiler), "m_globalShaderMacros");
        if (field == null)
            throw new MissingFieldException(typeof(MyShaderCompiler).FullName, "m_globalShaderMacros");

        var current = field.GetValue(null) as ShaderMacro[] ?? Array.Empty<ShaderMacro>();
        if (ContainsNamed(current, MacroName))
            return;
        field.SetValue(null, AppendMacro(current, MacroName, MacroValue));
    }

    internal static bool IsDepthPermutation(ShaderMacro[] macros)
    {
        if (macros == null)
            return false;
        for (var i = 0; i < macros.Length; i++)
        {
            if (string.Equals(macros[i].Name, "DEPTH_ONLY", StringComparison.Ordinal))
                return true;
            if (string.Equals(macros[i].Name, RenderingPassMacro, StringComparison.Ordinal) &&
                macros[i].Definition == "1")
                return true;
        }

        return false;
    }

    private static bool IsGeometryWithoutPass(string filepath, ShaderMacro[] macros)
    {
        if (string.IsNullOrEmpty(filepath) || HasNamed(macros, RenderingPassMacro))
            return false;
        return filepath.IndexOf("Geometry", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool HasNamed(ShaderMacro[] macros, string name)
    {
        return ContainsNamed(macros, name);
    }

    private static bool IsGBufferPermutation(ShaderMacro[] macros)
    {
        if (macros == null)
            return false;
        for (var i = 0; i < macros.Length; i++)
        {
            if (string.Equals(macros[i].Name, RenderingPassMacro, StringComparison.Ordinal))
                return IsGBufferPassValue(macros[i].Definition);
        }

        return false;
    }

    private static bool IsGBufferPermutation(List<ShaderMacro> macros)
    {
        for (var i = 0; i < macros.Count; i++)
        {
            if (string.Equals(macros[i].Name, RenderingPassMacro, StringComparison.Ordinal))
                return IsGBufferPassValue(macros[i].Definition);
        }

        return false;
    }

    private static bool IsGBufferPassValue(string definition)
    {
        return definition == "0";
    }

    private static bool ContainsNamed(ShaderMacro[] macros, string name)
    {
        if (macros == null)
            return false;
        for (var i = 0; i < macros.Length; i++)
        {
            if (string.Equals(macros[i].Name, name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool ContainsNamed(List<ShaderMacro> macros, string name)
    {
        for (var i = 0; i < macros.Count; i++)
        {
            if (string.Equals(macros[i].Name, name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static ShaderMacro[] AppendMacro(ShaderMacro[] current, string name, string value)
    {
        current = current ?? Array.Empty<ShaderMacro>();
        var next = new ShaderMacro[current.Length + 1];
        Array.Copy(current, next, current.Length);
        next[current.Length] = new ShaderMacro(name, value);
        return next;
    }

    private static bool TryRelativize(string root, string fullPath, out string relative)
    {
        relative = null;
        if (!IsUnderRoot(root, fullPath))
            return false;
        relative = fullPath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return relative.Length > 0;
    }

    private static bool IsUnderRoot(string root, string fullPath)
    {
        var prefix = root;
        if (!prefix.EndsWith(Path.DirectorySeparatorChar.ToString()) &&
            !prefix.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            prefix += Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureIncludePath()
    {
        var field = AccessTools.Field(typeof(MyShaderCompiler), "m_includes");
        if (field == null)
            throw new MissingFieldException(typeof(MyShaderCompiler).FullName, "m_includes");

        var list = field.GetValue(null) as List<string>;
        if (list == null)
        {
            list = new List<string> { MyShaderCompiler.ShadersPath };
            field.SetValue(null, list);
        }

        AddIncludeIfMissing(list, IncludeDirectory);
        foreach (var packDir in PackIncludes)
            AddIncludeIfMissing(list, packDir);
    }

    private static void AddIncludeIfMissing(List<string> list, string directory)
    {
        if (string.IsNullOrEmpty(directory))
            return;

        foreach (var existing in list)
        {
            if (string.Equals(existing, directory, StringComparison.OrdinalIgnoreCase))
                return;
        }

        list.Add(directory);
    }

    private static string MacrosToString(ShaderMacro[] macros)
    {
        if (macros == null || macros.Length == 0)
            return "";
        var parts = new string[macros.Length];
        for (var i = 0; i < macros.Length; i++)
            parts[i] = macros[i].Name + "=" + macros[i].Definition;
        return string.Join("; ", parts);
    }
}
