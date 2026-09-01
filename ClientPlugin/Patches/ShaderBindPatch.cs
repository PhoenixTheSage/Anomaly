using System.Reflection;
using ClientPlugin.ShaderFramework;
using ClientPlugin.Shaders;
using HarmonyLib;
using VRage.Render11.LightingStage;
using VRage.Render11.RenderContext;
using VRageRender;

namespace ClientPlugin.Patches;

[HarmonyPatch]
static class LightingPointBindPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyLightsRendering), "RenderPointlightsTiled");

    static void Prefix(MyRenderContext rc) => ShaderBindRegistry.Bind(rc, ShaderStages.Lighting);

    static void Postfix(MyRenderContext rc) => ShaderBindRegistry.Unbind(rc, ShaderStages.Lighting);
}

[HarmonyPatch]
static class LightingSpotBindPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyLightsRendering), "RenderSpotlights");

    static void Prefix(MyRenderContext rc) => ShaderBindRegistry.Bind(rc, ShaderStages.Lighting);

    static void Postfix(MyRenderContext rc) => ShaderBindRegistry.Unbind(rc, ShaderStages.Lighting);
}

[HarmonyPatch]
static class LightingDirBindPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyLightsRendering), "RenderDirectionalEnvironmentLight");

    static void Prefix(MyRenderContext rc) => ShaderBindRegistry.Bind(rc, ShaderStages.Lighting);

    static void Postfix(MyRenderContext rc) => ShaderBindRegistry.Unbind(rc, ShaderStages.Lighting);
}

[HarmonyPatch]
static class TonemapBindPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyToneMapping), "Run");

    static void Prefix() => ShaderBindRegistry.Bind(MyRender11.RC, ShaderStages.PostTonemap);

    static void Postfix() => ShaderBindRegistry.Unbind(MyRender11.RC, ShaderStages.PostTonemap);
}

[HarmonyPatch]
static class HbaoBindPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyHBAO), "Run");

    static void Prefix(MyRenderContext rc) => ShaderBindRegistry.Bind(rc, ShaderStages.PostHbao);

    static void Postfix(MyRenderContext rc) => ShaderBindRegistry.Unbind(rc, ShaderStages.PostHbao);
}

[HarmonyPatch]
static class OitResolveBindPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyTransparentRendering), "ResolveOIT");

    static void Prefix(MyRenderContext rc) => ShaderBindRegistry.Bind(rc, ShaderStages.Transparent);

    static void Postfix(MyRenderContext rc) => ShaderBindRegistry.Unbind(rc, ShaderStages.Transparent);
}
