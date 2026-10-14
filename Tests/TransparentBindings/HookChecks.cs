using System;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;

static class HookChecks
{
    internal static void Run(string pluginFile, string gameBin, Action<bool,string> check)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) => {
            string path=Path.Combine(gameBin,new AssemblyName(e.Name).Name+".dll");
            return File.Exists(path)?Assembly.LoadFrom(path):null;
        };
        var plugin=Assembly.LoadFrom(Path.GetFullPath(pluginFile));
        var type=plugin.GetType("ClientPlugin.Patches.BillboardResourceBindingsPatch",true);
        var renderer=Assembly.LoadFrom(Path.Combine(gameBin,"VRage.Render11.dll")).GetType("VRageRender.MyBillboardRenderer",true);
        var target=renderer.GetMethod("BindResourcesCommon",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public);
        var harmony=new Harmony("Anomaly.BillboardBinding.Tests");
        try {
            var patched=harmony.CreateClassProcessor(type).Patch();
            check(patched.Count==1 && target!=null
                && Harmony.GetPatchInfo(target).Postfixes.Any(p=>p.owner==harmony.Id),
                "Actual Anomaly consumer postfix installs on installed billboard renderer");
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }
}
