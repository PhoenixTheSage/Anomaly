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
        AccessTools.Method(typeof(MyRenderScheduler), nameof(MyRenderScheduler.Done));

    static void Postfix()
    {
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
    }
}
