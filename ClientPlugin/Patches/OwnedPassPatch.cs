using System;
using System.Reflection;
using ClientPlugin.ShaderFramework;
using ClientPlugin.Shaders;
using HarmonyLib;
using VRage.Render11.RenderContext;
using VRage.Render11.GBufferResolve;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageRender;

namespace ClientPlugin.Patches;

[HarmonyPatch]
static class OwnedPassDrawGameScenePatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyRender11), "DrawGameScene");

    [HarmonyPrefix]
    static void Prefix()
    {
        RenderTrace.Begin("DrawGameScene");
        OwnedPassRegistry.BeginFrame();
        SharedVolumetricRenderer.BeginFrame();
    }

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)]
    static void Postfix() => OwnedPassRegistry.RunFallbackAfterUpscale();

    static Exception Finalizer(Exception __exception)
    {
        if (__exception != null)
            RenderTrace.Dump("DrawGameScene", __exception);
        else
            RenderTrace.End("DrawGameScene");
        return __exception;
    }
}

[HarmonyPatch]
static class PresentCrashTracePatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyRender11), "Present");

    static Exception Finalizer(Exception __exception)
    {
        if (__exception != null)
            RenderTrace.Dump("Present", __exception);
        return __exception;
    }
}

[HarmonyPatch]
static class OwnedPassAfterLightingPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyGBufferResolver), "ConsumeWork");

    // Lighting and transparency RECORD on independent workers. ConsumeWork
    // is the ordered render-thread boundary after current-frame lighting.
    [HarmonyPostfix]
    static void Postfix()
    {
        // Current GBuffer depth must be produced before any HDR pack reads it.
        // Scheduler.Done postfix is later than both HDR stages (one frame late).
        OwnedBuffersPass.Execute();
        OwnedPassRegistry.Run(OwnedPassSlot.AfterLighting, MyRender11.RC);
    }
}

[HarmonyPatch]
static class OwnedPassAfterTransparentPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyTransparentRendering), "ConsumeWork");

    [HarmonyPostfix]
    static void Postfix() =>
        OwnedPassRegistry.Run(OwnedPassSlot.AfterTransparent, MyRender11.RC);
}

[HarmonyPatch]
static class OwnedPassAtmospherePatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyAtmosphereRenderer), "RenderGBuffer");

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

/// <summary>
/// Capture Keen tonemap SRVs before any skipper (SE-DLSS Priority.First).
/// Does not skip <c>Run</c>. BeforeTonemap runs at First+25 so skippers
/// cannot starve it. Dest salvage lives on the <c>Run</c> postfix.
/// </summary>
[HarmonyPatch]
static class TonemapInputCapturePatch
{
    // Higher than Harmony Priority.First (800) so this runs before SE-DLSS.
    const int BeforeFirst = Priority.First + 100;

    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyToneMapping), "Run");

    [HarmonyPrefix]
    [HarmonyPriority(BeforeFirst)]
    static void Prefix(ISrvBindable avgLum, ISrvBindable bloom, bool enableTonemapping,
        string dirtTexture, bool needsAlphaLuminance) =>
        TonemapInputs.Capture(avgLum, bloom, enableTonemapping, dirtTexture, needsAlphaLuminance);
}

[HarmonyPatch]
static class OwnedPassTonemapPatch
{
    static string lastDestError;

    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyToneMapping), "Run");

    // Before SE-DLSS Priority.First skip so BeforeTonemap still runs when
    // the unique upscaler returns false from its prefix.
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First + 25)]
    static void BeforeTonemapPrefix() =>
        OwnedPassRegistry.Run(OwnedPassSlot.BeforeTonemap, MyRender11.RC);

    [HarmonyPrefix]
    [HarmonyPriority(Priority.Last)]
    static bool Prefix(ref IBorrowedCustomTexture __result)
    {
        if (!OwnedPassRegistry.HasDisplayTenant || OwnedPassRegistry.HasUpscaleConsumer)
            return true;

        try
        {
            __result = DisplayDest.BorrowTonemapped();
        }
        catch (Exception e)
        {
            var message = e.Message;
            if (lastDestError != message)
            {
                lastDestError = message;
                MyLog.Default.WriteLine("Anomaly display dest: " + message);
            }

            return true;
        }

        // false skips Keen compute. A null dest still NREs in DrawGameScene
        // (borrowedCustomTexture.Linear / .SRgb).
        if (__result == null)
            return true;
        RenderTrace.Note("SkipKeenTonemap dest");
        return false;
    }

    [HarmonyPostfix]
    [HarmonyPriority(Priority.First)]
    static void Postfix(ref IBorrowedCustomTexture __result)
    {
        if (__result == null &&
            (OwnedPassRegistry.HasDisplayTenant || OwnedPassRegistry.WasUpscaleNotified))
            __result = OwnedPassRegistry.EnsureDrawSceneDest(null);

        OwnedPassRegistry.Run(OwnedPassSlot.AfterTonemap, MyRender11.RC, dest: __result);
        if (OwnedPassRegistry.HasDisplayTenant && !OwnedPassRegistry.HasUpscaleConsumer)
            OwnedPassRegistry.CompleteDisplayWithoutUpscale(__result);
    }

    // Harmony still runs postfixes when an earlier prefix skipped Keen.
    // Last so a skipper that omitted __result cannot leave DrawGameScene
    // with a null dest after other postfix patches.
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    static void SalvageDest(ref IBorrowedCustomTexture __result)
    {
        if (__result != null)
            return;
        if (!OwnedPassRegistry.HasDisplayTenant && !OwnedPassRegistry.WasUpscaleNotified)
            return;
        __result = OwnedPassRegistry.EnsureDrawSceneDest(null);
    }
}

/// <summary>
/// FXAA / chromatic dests are the same 8-bit <c>MyCustomTexture</c> as
/// Tonemapped. Wrap them when a Display tenant is live so highlight
/// values above 1 survive the rest of <c>DrawGameScene</c>.
/// </summary>
[HarmonyPatch]
static class DisplayDestBorrowCustomPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyBorrowedRwTextureManager), "BorrowCustom",
            new[] { typeof(string), typeof(int), typeof(int) });

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    static void Postfix(string debugName, ref IBorrowedCustomTexture __result)
    {
        if (__result == null || !DisplayDest.IsPostTonemapName(debugName))
            return;
        if (!OwnedPassRegistry.HasDisplayTenant)
            return;
        __result = DisplayDest.EnsureHdr(debugName, __result);
    }
}
