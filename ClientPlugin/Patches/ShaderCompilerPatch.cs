using System.Collections.Generic;
using System.Reflection;
using ClientPlugin.ShaderFramework;
using HarmonyLib;
using SharpDX.Direct3D;
using VRageRender;

namespace ClientPlugin.Patches;

/// <summary>
/// Backup if <see cref="ShaderCompileIntercept.Activate"/> races the first compile,
/// plus compile-failure logging. Keen entry: <c>VRageRender.MyShaderCompiler</c>.
/// </summary>
[HarmonyPatch]
static class ShaderCompilerIncludesPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.PropertyGetter(typeof(MyShaderCompiler), "Includes");

    static void Postfix(IReadOnlyList<string> __result)
    {
        ShaderCompileIntercept.EnsureIncludes(__result);
    }
}

[HarmonyPatch]
static class ShaderCompilerMacrosPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.PropertyGetter(typeof(MyShaderCompiler), "GlobalShaderMacros");

    static void Postfix(ref ShaderMacro[] __result)
    {
        ShaderCompileIntercept.EnsureGlobalMacros(ref __result);
    }
}

[HarmonyPatch]
static class ShaderCompilerCompilePatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(
            typeof(MyShaderCompiler),
            "Compile",
            new[]
            {
                typeof(string),
                typeof(ShaderMacro[]),
                typeof(MyShaderProfile),
                typeof(string),
                typeof(bool)
            });

    static void Postfix(
        string filepath,
        ShaderMacro[] macros,
        MyShaderProfile profile,
        string sourceDescriptor,
        byte[] __result)
    {
        ShaderCompileIntercept.NoteCompile(filepath, macros, profile, sourceDescriptor, __result);
    }
}
