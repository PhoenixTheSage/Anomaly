using System.Reflection;
using System.Threading;
using ClientPlugin.Shaders;
using HarmonyLib;
using Sandbox.Game.Lights;
using Sandbox.Game.World;
using VRage.Render11.Scene.Components;
using VRageRender;

namespace ClientPlugin.Patches;

// Capture the exact solar render ID; no assumptions about distant lights or materials.
[HarmonyPatch(typeof(MySector), nameof(MySector.UpdateSunLight))]
static class CelestialSunIdentityPatch
{
    internal static uint SunId = uint.MaxValue;
    static readonly FieldInfo Sun = AccessTools.Field(typeof(MySector), "m_sunFlare");
    static bool Prepare() => Sun != null;
    static void Postfix() => Volatile.Write(ref SunId, (Sun.GetValue(null) as MyLight)?.RenderObjectID ?? uint.MaxValue);
}

[HarmonyPatch(typeof(MySector), "UnloadData")]
static class CelestialSunUnloadPatch
{
    static void Postfix() => Volatile.Write(ref CelestialSunIdentityPatch.SunId, uint.MaxValue);
}

[HarmonyPatch]
static class CelestialSunGlarePatch
{
    static MethodBase TargetMethod() => AccessTools.Method(typeof(MyFlareRenderer), "Draw",
        new[] { typeof(MyLightComponent), typeof(float), typeof(bool) });
    static bool Prepare() => TargetMethod() != null;
    static bool Prefix(MyLightComponent light) =>
        light?.Owner == null || light.Owner.ID != Volatile.Read(ref CelestialSunIdentityPatch.SunId) ||
        !CelestialBackgroundRegistry.SuppressNativeSunGlare;
}
