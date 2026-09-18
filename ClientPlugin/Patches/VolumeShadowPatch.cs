using ClientPlugin.ShaderFramework;
using HarmonyLib;
using VRage.Render11.Render;

namespace ClientPlugin.Patches;

[HarmonyPatch(typeof(MyRenderScheduler), nameof(MyRenderScheduler.Init))]
static class VolumeShadowPatch
{
    // Serial render-thread boundary before the scheduler starts worker jobs.
    static void Prefix()
    {
        VolumetricIntegrator.PollTimings(VRageRender.MyRender11.RC.DeviceContext);
        SharedVolumetricRenderer.BeforeScheduler();
        DirectionalVolumeShadows.RenderBeforeScheduler();
    }
}
