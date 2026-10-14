using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using ClientPlugin.ShaderFramework;
using HarmonyLib;
using SharpDX.Direct3D11;
using VRage.Render11.RenderContext;
using VRage.Render11.GeometryStage2.RenderPass;
using VRageRender;

namespace ClientPlugin.Patches;

[HarmonyPatch]
static class FarSunClipPatch
{
    static readonly HashSet<MethodBase> Patched = new();
    static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(MyRenderUtils), nameof(MyRenderUtils.BindShaderBundle));
        yield return AccessTools.Method(typeof(MyDepthRenderPass), "DrawInstanceLodGroup");
        yield return AccessTools.Method(typeof(MyDepthPass), "Begin");
        foreach(var method in AccessTools.GetDeclaredMethods(typeof(MyDepthPass)))
            if(method.Name=="RecordCommandsInternal")yield return method;
    }
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var set = AccessTools.Method(typeof(MyPixelStage), nameof(MyPixelStage.Set), new[] { typeof(PixelShader) });
        var replacement = AccessTools.Method(typeof(FarSunClip), nameof(FarSunClip.Set));
        var bind=AccessTools.Method(typeof(MyRenderUtils),nameof(MyRenderUtils.BindShaderBundle));
        var replacementBind=AccessTools.Method(typeof(FarSunClip),nameof(FarSunClip.Bind));
        int matches = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(set)) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; matches++; }
            else if(instruction.Calls(bind)){instruction.opcode=OpCodes.Call;instruction.operand=replacementBind;matches++;}
            yield return instruction;
        }
        if (matches == 0) throw new System.InvalidOperationException("Far-sun depth binding hook missing");
        lock(Patched)
        {
            Patched.Add(__originalMethod);
            bool complete=true;
            foreach(var target in TargetMethods())complete &= target!=null && Patched.Contains(target);
            FarSunClip.HooksReady = complete;
        }
    }
}
