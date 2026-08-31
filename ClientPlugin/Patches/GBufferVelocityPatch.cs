using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System;
using ClientPlugin.ShaderFramework;
using ClientPlugin.Velocity;
using HarmonyLib;
using VRage.Render11.Scene.Components;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using VRage.Render11.GeometryStage2.Common;
using VRage.Render11.GeometryStage2.Instancing;
using VRage.Render11.GeometryStage2.PreparePass;
using VRage.Render11.GeometryStage2.RenderPass;
using VRage.Render11.GeometryStage2.Rendering;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRageRender;

namespace ClientPlugin.Patches;

[HarmonyPatch]
static class ShaderIncludeOverlayPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod()
    {
        var nested = AccessTools.Inner(typeof(MyShaderCompiler), "MyIncludeProcessor");
        return nested == null ? null : AccessTools.Method(nested, "Open", new[]
        {
            typeof(IncludeType),
            typeof(string),
            typeof(Stream)
        });
    }

    static bool Prefix(IncludeType includeType, string fileName, Stream parentStream, ref Stream __result)
    {
        if (!ShaderCompileIntercept.TryOpenOverlay(includeType, fileName, parentStream, out var overlay))
            return true;
        __result = overlay;
        return false;
    }
}

[HarmonyPatch]
static class ShaderCompilerVelocityMacroPatch
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

    static void Prefix(string filepath, ref ShaderMacro[] macros)
    {
        ShaderCompileIntercept.EnsureVelocityMacro(filepath, ref macros);
    }
}

[HarmonyPatch]
static class ShaderBundleGBufferMacroPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyShaderBundleManager), "AddMacrosForRenderingPass");

    static void Postfix(MyRenderPassType pass, List<ShaderMacro> macros)
    {
        if (pass == MyRenderPassType.GBuffer)
            ShaderCompileIntercept.EnsureVelocityMacro(macros);
    }
}

[HarmonyPatch]
static class GBufferPassBeginPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyGBufferPass), nameof(MyGBufferPass.Begin));

    static void Postfix(MyGBufferPass __instance)
    {
        GBufferVelocity.Bind(__instance.RC, __instance.GBuffer);
    }
}

[HarmonyPatch]
static class GBufferPassEndPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyGBufferPass), "End");

    static void Prefix(MyGBufferPass __instance)
    {
        GBufferVelocity.Unbind(__instance.RC);
    }
}

[HarmonyPatch]
static class GBufferRenderPassBeginPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyGBufferRenderPass), "BeginDraw");

    static void Postfix(MyRenderContext RC, MyGBuffer ___m_gbuffer)
    {
        GBufferVelocity.Bind(RC, ___m_gbuffer);
    }
}

[HarmonyPatch]
static class GBufferRenderPassEndPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyGBufferRenderPass), "EndDraw");

    static void Prefix(MyRenderContext RC)
    {
        GBufferVelocity.Unbind(RC);
    }
}

[HarmonyPatch]
static class GBufferClearPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyGBuffer), nameof(MyGBuffer.Clear));

    static void Postfix(MyRenderContext rc)
    {
        GBufferVelocity.ClearTarget(rc);
    }
}

[HarmonyPatch]
static class ColorPrepareInitPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyColorPreparePass0), nameof(MyColorPreparePass0.InitInstanceElements));

    static void Postfix(int elementsCount)
    {
        GBufferVelocity.OnInitInstanceElements(elementsCount);
    }
}

[HarmonyPatch]
static class ColorPrepareAddPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyColorPreparePass0), nameof(MyColorPreparePass0.AddInstanceIntoInstanceElements));

    static void Postfix(int bufferOffset, MyInstance instance)
    {
        GBufferVelocity.OnAddInstance(bufferOffset, instance);
    }
}

[HarmonyPatch]
static class ColorPrepareWritePatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyColorPreparePass0), nameof(MyColorPreparePass0.WriteData));

    static void Postfix()
    {
        GBufferVelocity.OnWriteInstanceData();
    }
}

[HarmonyPatch]
static class GBufferProxyConstantsPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyRenderingPass), "SetProxyConstants", new[] { typeof(MyRenderableProxy) });

    static void Postfix(MyRenderingPass __instance, MyRenderableProxy proxy)
    {
        if (__instance is MyGBufferPass)
            GBufferVelocity.OnProxyDraw(__instance.RC, proxy);
    }
}

[HarmonyPatch]
static class SkinningBonesPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MySkinningComponent), "SetAnimationBones");

    static void Postfix(MySkinningComponent __instance)
    {
        var owner = __instance.Owner;
        if (owner != null)
            BoneHistory.Instance.Snapshot(owner.ID, __instance.SkinMatrices);
    }
}
