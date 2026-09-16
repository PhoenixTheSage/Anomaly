using System.Reflection;
using ClientPlugin.ShaderFramework;
using ClientPlugin.Shaders;
using HarmonyLib;
using VRage.Render11.Render;
using VRageRender;

namespace ClientPlugin.Patches;

[HarmonyPatch]
static class CameraVelocitySchedulerDonePatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyRenderScheduler), "Done");

    static void Postfix()
    {
        // MyRenderScheduler.Done has executed every deferred geometry command
        // list and cleared the immediate context. A probe clear here is the
        // first unambiguous test of the final Target3 resource.
        GBufferVelocity.CaptureTarget3Checkpoint(Target3Checkpoint.SchedulerEnd);
        GBufferVelocity.ApplySchedulerEndProbe();
        CameraVelocityPass.Execute();
        OwnedBuffersPass.Execute();
    }
}

[HarmonyPatch]
static class CameraVelocityScreenResourcesPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyRender11), "CreateScreenResources");

    static void Postfix()
    {
        CameraVelocityPass.OnResolutionChanged();
        OwnedBuffersPass.OnResolutionChanged();
        OwnedPassRegistry.OnResolutionChanged();
    }
}

[HarmonyPatch]
static class CameraVelocityDeviceEndPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyRender11), "OnDeviceEnd");

    static void Prefix()
    {
        CameraVelocityPass.Release();
        OwnedBuffersPass.Release();
        VelocityDebugPass.Release();
        ShaderBindRegistry.Release();
        OwnedPassRegistry.Release();
        PointLightCatalog.Release();
        PointShadowPass.Release();
    }
}
