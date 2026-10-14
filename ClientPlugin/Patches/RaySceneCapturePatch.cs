using System.Reflection;
using ClientPlugin.Shaders;
using HarmonyLib;
using VRage.Render11.GeometryStage2.Model;
using VRageRender;
using VRage.Render11.LightingStage;
using VRage.Render11.Resources;

namespace ClientPlugin.Patches;

[HarmonyPatch(typeof(MyLightsRendering), "Render")]
static class RaySunCoveragePatch
{
    static void Prefix(ISrvTexture postProcessedShadows) => RayScene.SunShadows = postProcessedShadows?.Srv;
}

[HarmonyPatch(typeof(MyLod), "Create")]
static class RayModernMeshCapturePatch
{
    static void Prefix(MyLod __instance, ref MyMwmData mwmData) => RayScene.CaptureModern(__instance, ref mwmData);
}

[HarmonyPatch]
static class RayLegacyMeshCapturePatch
{
    static MethodBase TargetMethod() => AccessTools.Method(typeof(MyMeshes), "MoveData", new[] { typeof(LodMeshId), typeof(MyMeshes.MyDeviceUpdateBatch) });
    static void Prefix(LodMeshId lodMeshId) => RayScene.CaptureLegacy(lodMeshId);
}

[HarmonyPatch(typeof(MyLod), "UnloadData")]
static class RayModernMeshRemovePatch
{
    static void Prefix(MyLod __instance) => RayScene.Remove(__instance);
}

[HarmonyPatch(typeof(MyMeshes), "DisposeLodMeshInfos")]
static class RayLegacyMeshRemovePatch
{
    static void Prefix(LodMeshId lodMeshId) => RayScene.Remove("legacy:" + lodMeshId.Index);
}

[HarmonyPatch(typeof(MyRender11), "OnDeviceEnd")]
static class RaySceneDeviceEndPatch
{
    static void Prefix() => RayScene.BeginDeviceEnd();
    static void Finalizer() => RayScene.FinishDeviceEnd();
}

[HarmonyPatch(typeof(MyRender11), "OnDeviceReset")]
static class RaySceneDeviceResetPatch
{
    static void Prefix() => RayScene.ResetDevice();
}

[HarmonyPatch(typeof(MyRender11), "OnSessionEnd")]
static class RaySceneSessionEndPatch
{
    static void Prefix() => RayScene.Clear();
}
