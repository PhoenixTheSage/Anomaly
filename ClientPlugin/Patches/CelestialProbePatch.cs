using System.Reflection;
using ClientPlugin.Shaders;
using HarmonyLib;
using VRage.Render11.LightingStage.EnvironmentProbe;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRageMath;

namespace ClientPlugin.Patches;

[HarmonyPatch]
static class CelestialProbePatch
{
    static bool Prepare() => CelestialBackgroundRegistry.ProbeHookAvailable = TargetMethod() != null;
    static MethodBase TargetMethod() => AccessTools.Method(typeof(MyEnvProbeProcessing), "RunForwardPostprocess");
    static void Postfix(MyRenderContext rc, IRtvBindable rt, ISrvBindable depthSrv, ref Matrix viewMatrix, ref Matrix projMatrix) =>
        CelestialBackgroundRegistry.DrawProbe(rc, rt, depthSrv, viewMatrix, projMatrix);
}
