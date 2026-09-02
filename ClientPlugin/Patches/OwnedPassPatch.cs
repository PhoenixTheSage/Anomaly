using System.Reflection;
using ClientPlugin.ShaderFramework;
using ClientPlugin.Shaders;
using HarmonyLib;
using VRage.Render11.RenderContext;
using VRageRender;

namespace ClientPlugin.Patches;

[HarmonyPatch]
static class OwnedPassDrawGameScenePatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyRender11), "DrawGameScene");

    [HarmonyPrefix]
    static void Prefix() => OwnedPassRegistry.BeginFrame();

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)]
    static void Postfix() => OwnedPassRegistry.RunFallbackAfterUpscale();
}

[HarmonyPatch]
static class OwnedPassAfterLightingPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyTransparentRendering), "Render");

    [HarmonyPrefix]
    static void Prefix(MyRenderContext rc) =>
        OwnedPassRegistry.Run(OwnedPassSlot.AfterLighting, rc);

    [HarmonyPostfix]
    static void Postfix(MyRenderContext rc) =>
        OwnedPassRegistry.Run(OwnedPassSlot.AfterTransparent, rc);
}

[HarmonyPatch]
static class OwnedPassAtmospherePatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyAtmosphereRenderer), nameof(MyAtmosphereRenderer.RenderGBuffer));

    [HarmonyPrefix]
    static void Prefix(MyRenderContext rc) =>
        ShaderBindRegistry.Bind(rc, ShaderStages.Atmosphere);

    [HarmonyPostfix]
    static void Postfix(MyRenderContext rc)
    {
        ShaderBindRegistry.Unbind(rc, ShaderStages.Atmosphere);
        OwnedPassRegistry.Run(OwnedPassSlot.AfterAtmosphere, rc);
    }
}

/// <summary>
/// Keen <c>RenderEnd</c> clears t5–t6 after each planet. Rebind extras
/// so a second atmosphere still sees velocity at t6.
/// </summary>
[HarmonyPatch]
static class OwnedPassAtmosphereOnePatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyAtmosphereRenderer), "RenderOne");

    [HarmonyPrefix]
    static void Prefix(MyRenderContext rc) =>
        ShaderBindRegistry.Bind(rc, ShaderStages.Atmosphere);

    [HarmonyPostfix]
    static void Postfix(MyRenderContext rc) =>
        ShaderBindRegistry.Unbind(rc, ShaderStages.Atmosphere);
}

[HarmonyPatch]
static class OwnedPassTonemapPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyToneMapping), nameof(MyToneMapping.Run));

    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    static void Prefix() =>
        OwnedPassRegistry.Run(OwnedPassSlot.BeforeTonemap, MyRender11.RC);

    [HarmonyPostfix]
    [HarmonyPriority(Priority.First)]
    static void Postfix(object __result) =>
        OwnedPassRegistry.Run(OwnedPassSlot.AfterTonemap, MyRender11.RC, dest: __result);
}
