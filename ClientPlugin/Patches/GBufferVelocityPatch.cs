using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
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
using VRage.Render11.GBufferResolve;
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

    static void Postfix(Stream __result)
    {
        ShaderCompileIntercept.NoteOpenedInclude(__result);
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
        AccessTools.Method(typeof(MyGBufferPass), "Begin");

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
        GBufferVelocity.BindStage2(RC, ___m_gbuffer);
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
        AccessTools.Method(typeof(MyGBuffer), "Clear");

    static void Postfix(MyRenderContext rc)
    {
        GBufferVelocity.ClearTarget(rc);
    }
}

[HarmonyPatch]
static class Target3AfterOldGeometryPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyGeometryRendererOld), "DoneFrame");

    static void Postfix() =>
        GBufferVelocity.CaptureTarget3Checkpoint(Target3Checkpoint.AfterOldGeometry);
}

[HarmonyPatch]
static class Target3AfterStage2Patch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyGeometryRenderer), "DoneFrame");

    static void Postfix() =>
        GBufferVelocity.CaptureTarget3Checkpoint(Target3Checkpoint.AfterStage2);
}

[HarmonyPatch]
static class Target3AfterDecalsPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyScreenDecals), "ConsumeDrawDeferred");

    static void Postfix() =>
        GBufferVelocity.CaptureTarget3Checkpoint(Target3Checkpoint.AfterDecals);
}

[HarmonyPatch]
static class Target3AfterResolverPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyGBufferResolver), "ConsumeWork");

    static void Postfix() =>
        GBufferVelocity.CaptureTarget3Checkpoint(Target3Checkpoint.AfterResolver);
}

[HarmonyPatch]
static class Target3AfterTransparentPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyTransparentRendering), "ConsumeWork");

    static void Postfix() =>
        GBufferVelocity.CaptureTarget3Checkpoint(Target3Checkpoint.AfterTransparent);
}

[HarmonyPatch]
static class Target3BeforeGBufferDonePatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyGBuffer), "DoneFrame");

    static void Prefix() =>
        GBufferVelocity.CaptureTarget3Checkpoint(Target3Checkpoint.BeforeGBufferDone);
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
static class GBufferColorPrepareWorkPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyPreparePass<MyColorPreparePass0, MyColorPreparePass1>), "DoWork");

    static void Prefix() => GBufferVelocity.BeginGBufferColorPrepare();

    static void Postfix() => GBufferVelocity.EndGBufferColorPrepare();
}

[HarmonyPatch]
static class GBufferColorPrepareSlotPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyColorPreparePass0), "AddInstanceIntoInstanceElements");

    static void Postfix(int bufferOffset, MyInstance instance)
    {
        GBufferVelocity.PackColorPrepareSlot(bufferOffset, instance);
    }
}

[HarmonyPatch]
static class GBufferStage2ProcessPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyRenderPass), "ProcessRenderData");

    /// <summary>
    /// Before each <c>DrawInstanceLodGroup</c>: bind GBuffer InstanceBase.
    /// Does not patch the protected override (that transpiler did not apply).
    /// </summary>
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
    {
        var hook = AccessTools.Method(typeof(GBufferVelocity), nameof(GBufferVelocity.BindStage2BeforeDraw));
        var locPass = il.DeclareLocal(typeof(MyRenderPass));
        var locRc = il.DeclareLocal(typeof(MyRenderContext));
        var locGroup = il.DeclareLocal(typeof(MyInstanceLodGroup));
        foreach (var ins in instructions)
        {
            if (hook != null &&
                ins.operand is MethodInfo mi &&
                mi.Name == "DrawInstanceLodGroup")
            {
                var storeGroup = new CodeInstruction(OpCodes.Stloc, locGroup);
                storeGroup.labels.AddRange(ins.labels);
                ins.labels.Clear();
                yield return storeGroup;
                yield return new CodeInstruction(OpCodes.Stloc, locRc);
                yield return new CodeInstruction(OpCodes.Stloc, locPass);
                yield return new CodeInstruction(OpCodes.Ldloc, locPass);
                yield return new CodeInstruction(OpCodes.Ldloc, locRc);
                yield return new CodeInstruction(OpCodes.Ldloc, locGroup);
                yield return new CodeInstruction(OpCodes.Call, hook);
                yield return new CodeInstruction(OpCodes.Ldloc, locPass);
                yield return new CodeInstruction(OpCodes.Ldloc, locRc);
                yield return new CodeInstruction(OpCodes.Ldloc, locGroup);
            }

            yield return ins;
        }
    }
}

[HarmonyPatch]
static class GBufferDrawBoundaryPatch
{
    static IEnumerable<MethodBase> TargetMethods()
    {
        var signatures = new[]
        {
            ("Draw", new[] { typeof(int), typeof(int) }),
            ("DrawIndexed", new[] { typeof(int), typeof(int), typeof(int) }),
            ("DrawInstanced", new[] { typeof(int), typeof(int), typeof(int), typeof(int) }),
            ("DrawIndexedInstanced", new[]
            {
                typeof(int), typeof(int), typeof(int), typeof(int), typeof(int)
            })
        };

        foreach (var signature in signatures)
        {
            var method = AccessTools.Method(typeof(MyRenderContext), signature.Item1, signature.Item2);
            if (method != null)
                yield return method;
        }
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var hook = AccessTools.Method(typeof(GBufferVelocity), nameof(GBufferVelocity.OnDrawIndexedInstanced));
        foreach (var instruction in instructions)
        {
            if (hook != null &&
                (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                instruction.operand is MethodInfo method &&
                method.DeclaringType == typeof(SharpDX.Direct3D11.DeviceContext) &&
                (method.Name == "Draw" || method.Name == "DrawIndexed" ||
                 method.Name == "DrawInstanced" || method.Name == "DrawIndexedInstanced"))
            {
                // Prefix ordering is not a sufficiently strong draw boundary: a
                // later Harmony prefix can restore Keen's three-target state.
                // Insert immediately before the native D3D draw instead. Move
                // branch labels onto the hook so no control-flow path skips it.
                var loadContext = new CodeInstruction(OpCodes.Ldarg_0);
                loadContext.labels.AddRange(instruction.labels);
                instruction.labels.Clear();
                yield return loadContext;
                yield return new CodeInstruction(OpCodes.Call, hook);
            }

            yield return instruction;
        }
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
    /// after BindShaderBundle is cheaper than a Prefix wrapper;
    /// <see cref="GBufferVelocity.OnGBufferProxy"/> returns immediately for voxels.
    /// Slot 6 must be set after the VS bind so a driver that resets CBs on
    /// VSSetShader cannot drop HasPrevWorld.
    /// </summary>
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var hook = AccessTools.Method(typeof(GBufferVelocity), nameof(GBufferVelocity.OnGBufferProxy));
        var bind = AccessTools.Method(typeof(MyRenderUtils), "BindShaderBundle",
            new[] { typeof(MyRenderContext), typeof(MyMaterialShadersBundleId) });
        var list = new List<CodeInstruction>(instructions);
        var injected = false;
        if (hook != null && bind != null)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (!Equals(list[i].operand, bind))
                    continue;
                list.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_0));
                list.Insert(i + 2, new CodeInstruction(OpCodes.Ldarg_1));
                list.Insert(i + 3, new CodeInstruction(OpCodes.Call, hook));
                injected = true;
                break;
            }
        }

        if (!injected && hook != null)
        {
            list.Insert(0, new CodeInstruction(OpCodes.Ldarg_0));
            list.Insert(1, new CodeInstruction(OpCodes.Ldarg_1));
            list.Insert(2, new CodeInstruction(OpCodes.Call, hook));
        }

        return list;
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
