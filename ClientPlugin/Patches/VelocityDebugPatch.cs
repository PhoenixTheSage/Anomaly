using System.Reflection;
using ClientPlugin.ShaderFramework;
using HarmonyLib;
using VRage.Render11.Resources;
using VRageRender;

namespace ClientPlugin.Patches;

/// <summary>
/// After LDR is copied to the backbuffer (and after SE-DLSS stretch-copy).
/// Viewport is the presented size so the overlay covers DLSS/DRS output, not
/// the internal <c>Backbuffer.Size</c>.
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
        OwnedBuffersPass.CaptureHistory();
    }
}
