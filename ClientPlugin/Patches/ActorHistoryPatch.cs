using System.Reflection;
using ClientPlugin.ShaderFramework;
using ClientPlugin.Velocity;
using HarmonyLib;
using VRage.Render11.Culling;
using VRage.Render11.GeometryStage2.Rendering;
using VRageRender;

namespace ClientPlugin.Patches;

[HarmonyPatch]
static class ActorHistoryStage2Patch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyGeometryRenderer), "UpdateMatrices");

    static void Postfix(MyCullQuery cullQuery)
    {
        ActorHistory.Instance.SnapshotStage2(cullQuery);
    }
}

[HarmonyPatch]
static class GBufferRendererPreparePackPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyGeometryRenderer), "Prepare");

    static void Postfix(MyGeometryRenderer __instance, MyCullQuery query)
    {
        GBufferVelocity.PackAfterRendererPrepare(__instance, query);
    }
}

[HarmonyPatch]
static class GBufferRendererRenderNotePatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyGeometryRenderer), "Render");

    static void Postfix(MyGeometryRenderer __instance, MyCullQuery query)
    {
        GBufferVelocity.NoteStage2Render(__instance, query);
    }
}

[HarmonyPatch]
static class ActorHistoryOldPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyGeometryRendererOld), "UpdateCullProxies");

    static void Postfix(MyCullQuery cullQuery)
    {
        ActorHistory.Instance.SnapshotOld(cullQuery);
    }
}

[HarmonyPatch]
static class ActorHistoryDrawGameScenePatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyRender11), "DrawGameScene");

    static void Prefix()
    {
        ActorHistory.Instance.BeginFrame();
        BoneHistory.Instance.BeginFrame();
        GBufferVelocity.BeginFrame();
    }

    static void Postfix()
    {
        ActorHistory.Instance.EndFrame();
        BoneHistory.Instance.EndFrame();
        GBufferVelocity.EndFrame();
    }
}

[HarmonyPatch]
static class ActorHistorySessionEndPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyRender11), "OnSessionEnd");

    static void Prefix()
    {
        ActorHistory.Instance.Clear();
        BoneHistory.Instance.Clear();
        GBufferVelocity.ResetWorldActivity();
    }
}
