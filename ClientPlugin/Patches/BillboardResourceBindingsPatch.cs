using ClientPlugin.ShaderFramework;
using HarmonyLib;
using VRage.Render11.RenderContext;
using VRageRender;

namespace ClientPlugin.Patches;

// Restore at the consumer boundary, after any native/upscaler context clear.
// Keen BindResourcesCommon binds VS frame/samplers but inherits their PS peers.
// Covers native, replacement LDR and PostPP calls without changing draw order.
[HarmonyPatch(typeof(MyBillboardRenderer), "BindResourcesCommon")]
static class BillboardResourceBindingsPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    static void Postfix(MyRenderContext rc) =>
        TransparentStageBindings.RestoreBillboardResources(rc);
}
