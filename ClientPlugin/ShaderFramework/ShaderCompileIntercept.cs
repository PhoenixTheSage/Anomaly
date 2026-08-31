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
using HarmonyLib;
using SharpDX.Direct3D;
using VRage.Utils;
using VRageRender;

namespace ClientPlugin.ShaderFramework;

public static class ShaderCompileIntercept
{
    public const string MacroName = "ANOMALY";
    public const string MacroValue = "1";

    public static bool IsLive { get; private set; }
    public static string IncludeDirectory { get; private set; }
    public static string LastError { get; private set; }
    public static int CompileCount => Volatile.Read(ref compileCount);
    public static int FailureCount => Volatile.Read(ref failureCount);

    private static int compileCount;
    private static int failureCount;
    private static string assetFolder;
    private static readonly object Gate = new();

    public static void SetAssetFolder(string folder)
    {
        assetFolder = folder;
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
                IsLive = true;
                MyLog.Default.WriteLine("Anomaly compile intercept live. Include: " + IncludeDirectory);
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
            return;

        Interlocked.Increment(ref failureCount);
        var desc = sourceDescriptor ?? filepath ?? "(unknown)";
        var defines = MacrosToString(macros);
        var msg = "Anomaly: shader compile failed " + desc + " profile=" + profile + " defines=[" + defines + "]";
        MyLog.Default.WriteLine(msg);
        DebugLog.Write(msg);
    }

    public static void EnsureIncludes(IReadOnlyList<string> includes)
    {
        if (string.IsNullOrEmpty(IncludeDirectory) || includes == null)
            return;
        if (includes is not List<string> list)
            return;
        AddIncludeIfMissing(list);
    }

    public static void EnsureGlobalMacros(ref ShaderMacro[] macros)
    {
        if (ContainsAnomaly(macros))
            return;

        lock (Gate)
        {
            var field = AccessTools.Field(typeof(MyShaderCompiler), "m_globalShaderMacros");
            var current = field?.GetValue(null) as ShaderMacro[] ?? macros ?? Array.Empty<ShaderMacro>();
            if (ContainsAnomaly(current))
            {
                macros = current;
                return;
            }

            var next = AppendAnomaly(current);
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
        if (ContainsAnomaly(current))
            return;
        field.SetValue(null, AppendAnomaly(current));
    }

    private static bool ContainsAnomaly(ShaderMacro[] macros)
    {
        if (macros == null)
            return false;
        for (var i = 0; i < macros.Length; i++)
        {
            if (string.Equals(macros[i].Name, MacroName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static ShaderMacro[] AppendAnomaly(ShaderMacro[] current)
    {
        current = current ?? Array.Empty<ShaderMacro>();
        var next = new ShaderMacro[current.Length + 1];
        Array.Copy(current, next, current.Length);
        next[current.Length] = new ShaderMacro(MacroName, MacroValue);
        return next;
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

        AddIncludeIfMissing(list);
    }

    private static void AddIncludeIfMissing(List<string> list)
    {
        if (string.IsNullOrEmpty(IncludeDirectory))
            return;

        foreach (var existing in list)
        {
            if (string.Equals(existing, IncludeDirectory, StringComparison.OrdinalIgnoreCase))
                return;
        }

        list.Add(IncludeDirectory);
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
