using System.Reflection;
using ClientPlugin.ShaderFramework;
using HarmonyLib;
using VRageRender;

namespace ClientPlugin.Patches;

/// <summary>
/// Fills ShaderCache2 in parallel before Keen's serial Recompile create loop.
/// </summary>
[HarmonyPatch]
static class ShaderManagerRecompileParallelPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyShaders), "Recompile");

    static void Prefix()
    {
        ShaderRecompileCacheFill.FillManagers();
    }
}

[HarmonyPatch]
static class MaterialShadersRecompileParallelPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyMaterialShaders), "Recompile");

    static void Prefix()
    {
        ShaderRecompileCacheFill.FillMaterials();
    }
}
