using System.Reflection;
using ClientPlugin.ShaderFramework;
using HarmonyLib;
using VRage.Render11.Resources;
using VRageRender;

namespace ClientPlugin.Patches;

/// <summary>
/// After LDR is copied to the scene target, before Keen debug/HUD. Stretch-covers DRS.
/// </summary>
[HarmonyPatch]
static class VelocityDebugDrawPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyRender11), "DrawGameScene");

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    static void Postfix(IRtvBindable renderTarget)
    {
        VelocityDebugPass.Draw(renderTarget);
    }
}
