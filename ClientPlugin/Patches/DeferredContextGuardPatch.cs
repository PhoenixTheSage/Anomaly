using System.Reflection;
using ClientPlugin.ShaderFramework;
using HarmonyLib;
using SharpDX.Direct3D11;
using VRage.Render11.RenderContext;
using VRageRender;

namespace ClientPlugin.Patches;

[HarmonyPatch]
static class ImmediateRcGuardPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.PropertyGetter(typeof(MyRender11), "RC");

    static bool Prefix(ref MyRenderContext __result)
    {
        if (!DeferredContextGuard.TryRedirect(out var rc, out var steal))
            return true;
        if (steal)
            DeferredContextGuard.Warn("MyRender11.RC");
        __result = rc;
        return false;
    }
}

[HarmonyPatch]
static class ImmediateContextGuardPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.PropertyGetter(typeof(Device), "ImmediateContext");

    static bool Prefix(ref DeviceContext __result)
    {
        if (!DeferredContextGuard.TryRedirectDeviceContext(out var context, out var steal))
            return true;
        if (steal)
            DeferredContextGuard.Warn("Device.ImmediateContext");
        __result = context;
        return false;
    }
}

[HarmonyPatch]
static class ImmediateContext1GuardPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.PropertyGetter(typeof(Device1), "ImmediateContext1");

    static bool Prefix(ref DeviceContext1 __result)
    {
        if (!DeferredContextGuard.TryRedirectDeviceContext(out var context, out var steal) ||
            context is not DeviceContext1 typed)
            return true;
        if (steal)
            DeferredContextGuard.Warn("Device.ImmediateContext1");
        __result = typed;
        return false;
    }
}
