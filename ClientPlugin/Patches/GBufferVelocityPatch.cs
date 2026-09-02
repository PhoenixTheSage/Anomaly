using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
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

    static bool Prefix(IncludeType type, string fileName, Stream parentStream, ref Stream __result)
    {
        if (!ShaderCompileIntercept.TryOpenOverlay(type, fileName, parentStream, out var overlay))
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

    static void Prefix(ref string filepath, ref ShaderMacro[] macros)
    {
        ShaderCompileIntercept.TryRemapSource(ref filepath);
        ShaderCompileIntercept.EnsureGBufferMacros(filepath, ref macros);
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
            ShaderCompileIntercept.EnsureGBufferMacros(macros);
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
static class GBufferPreparePackPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyPreparePass<MyColorPreparePass0, MyColorPreparePass1>), "PrepareInstanceableGroups");

    static void Postfix(MyPreparePass<MyColorPreparePass0, MyColorPreparePass1> __instance)
    {
        GBufferVelocity.PackAfterGBufferPrepare(__instance);
    }
}

[HarmonyPatch]
static class GBufferProxyConstantsPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyGBufferPass), "RecordCommandsInternal", new[] { typeof(MyRenderableProxy) });

    /// <summary>
    /// Harmony Prefix on this method showed up as Thread CPU Load: old-pipeline
    /// GBuffer records every voxel proxy on Parallel.Scheduler. A transpiler call
    /// is cheaper than a Prefix wrapper; <see cref="GBufferVelocity.OnGBufferProxy"/>
    /// returns immediately for voxels.
    /// </summary>
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var hook = AccessTools.Method(typeof(GBufferVelocity), nameof(GBufferVelocity.OnGBufferProxy));
        yield return new CodeInstruction(OpCodes.Ldarg_0);
        yield return new CodeInstruction(OpCodes.Ldarg_1);
        yield return new CodeInstruction(OpCodes.Call, hook);
        foreach (var ins in instructions)
            yield return ins;
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
