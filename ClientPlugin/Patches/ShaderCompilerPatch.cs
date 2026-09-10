using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using ClientPlugin.ShaderFramework;
using HarmonyLib;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using VRageRender;
using VRageRender.Messages;

namespace ClientPlugin.Patches;

/// <summary>
/// Backup if <see cref="ShaderCompileIntercept.Activate"/> races the first compile,
/// plus compile-failure logging. Keen entry: <c>VRageRender.MyShaderCompiler</c>.
/// </summary>
[HarmonyPatch]
static class ShaderCompilerIncludesPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.PropertyGetter(typeof(MyShaderCompiler), "Includes");

    static void Postfix(IReadOnlyList<string> __result)
    {
        ShaderCompileIntercept.EnsureIncludes(__result);
    }
}

[HarmonyPatch]
static class ShaderCompilerMacrosPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.PropertyGetter(typeof(MyShaderCompiler), "GlobalShaderMacros");

    static void Postfix(ref ShaderMacro[] __result)
    {
        ShaderCompileIntercept.EnsureGlobalMacros(ref __result);
    }
}

[HarmonyPatch]
static class ShaderCompilerCompilePatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(
            typeof(MyShaderCompiler),
            "Compile",
            new[]
            {
                typeof(string),
                typeof(ShaderMacro[]),
                typeof(MyShaderProfile),
                typeof(string),
                typeof(bool)
            });

    static void Postfix(
        string filepath,
        ShaderMacro[] macros,
        MyShaderProfile profile,
        string sourceDescriptor,
        byte[] __result)
    {
        ShaderCompileIntercept.NoteCompile(filepath, macros, profile, sourceDescriptor, __result);
        ShaderCompileIntercept.NoteCompiledShaderBytecode(macros, profile, __result);
    }
}

/// <summary>
/// Common compiler endpoint used by both object-facing wrappers and both
/// cache/fresh paths.  Mutating the macro array here closes routes that bypass
/// manager Create/Init interception, while the returned bytecode is the exact
/// payload that Keen will cache and hand to the native shader constructor.
/// </summary>
[HarmonyPatch]
static class ShaderCompilerCommonEndpointPatch
{
    static bool Prepare()
    {
        var target = TargetMethod();
        ShaderCompileIntercept.NoteDeepCompileHook(target != null);
        return target != null;
    }

    static MethodBase TargetMethod() =>
        AccessTools.Method(
            typeof(MyShaderCompiler),
            "Compile",
            new[]
            {
                typeof(string),
                typeof(ShaderMacro[]),
                typeof(MyShaderProfile),
                typeof(string),
                typeof(bool),
                typeof(bool),
                typeof(bool).MakeByRefType(),
                typeof(string).MakeByRefType(),
                typeof(string).MakeByRefType(),
                typeof(bool),
                typeof(bool)
            });

    static void Prefix(
        string __0,
        ref ShaderMacro[] __1,
        MyShaderProfile __2,
        bool __5,
        out ShaderCompileIntercept.DeepCompileState __state)
    {
        __state = ShaderCompileIntercept.PrepareDeepCompile(__0, ref __1, __2, __5);
    }

    static void Postfix(
        ref bool __6,
        ref string __8,
        byte[] __result,
        ShaderCompileIntercept.DeepCompileState __state)
    {
        ShaderCompileIntercept.NoteDeepCompile(__6, __8, __result, __state);
    }
}

/// <summary>
/// Put the velocity contract into Keen's manager key before Create performs
/// its key lookup. Init-time mutation alone can produce a stock-keyed object
/// whose native bytecode no longer matches its cache identity.
/// </summary>
[HarmonyPatch]
static class PixelShaderCreateKeyPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyPixelShaders), "Create", new[]
        {
            typeof(string),
            typeof(ShaderMacro[])
        });

    static void Prefix(string __0, ref ShaderMacro[] __1)
    {
        ShaderCompileIntercept.PreparePixelShaderCreate(__0, ref __1);
    }
}

[HarmonyPatch]
static class VertexShaderCreateKeyPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyVertexShaders), "Create", new[]
        {
            typeof(string),
            typeof(ShaderMacro[])
        });

    static void Prefix(string __0, ref ShaderMacro[] __1)
    {
        ShaderCompileIntercept.PrepareVertexShaderCreate(__0, ref __1);
    }
}

/// <summary>
/// Captures the bytecode returned by Keen's object-facing compiler overload.
/// Unlike the five-argument overload above, this runs for both cache hits and
/// fresh compiles, immediately before MyPixelShaders/MyVertexShaders construct
/// the native shader object.
/// </summary>
[HarmonyPatch]
static class ShaderCompilerObjectBytecodePatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(
            typeof(MyShaderCompiler),
            "Compile",
            new[]
            {
                typeof(MyShaderCompilationInfo).MakeByRefType(),
                typeof(bool)
            });

    static void Prefix(ref MyShaderCompilationInfo info, ref bool invalidateCache)
    {
        ShaderCompileIntercept.PrepareObjectCompile(ref info, ref invalidateCache);
    }

    static void Postfix(ref MyShaderCompilationInfo info, byte[] __result)
    {
        ShaderCompileIntercept.NoteShaderObjectBytecode(ref info, __result);
    }
}

/// <summary>
/// Legacy cache observation retained as a cross-check. The common compiler
/// endpoint above now supplies the authoritative cache/fresh result and exact
/// bytecode association.
/// </summary>
[HarmonyPatch]
static class ShaderCompilerCacheFetchPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod()
    {
        var cacheType = AccessTools.TypeByName("VRage.Render11.Shader.MyShaderCache");
        return cacheType == null
            ? null
            : AccessTools.Method(cacheType, "TryFetch", new[]
            {
                typeof(string),
                typeof(MyShaderProfile),
                typeof(string),
                typeof(byte[]).MakeByRefType()
            });
    }

    static void Postfix(MyShaderProfile profile, bool __result)
    {
        ShaderCompileIntercept.NoteShaderCacheResult(profile, __result);
    }
}

[HarmonyPatch]
static class PixelShaderObjectPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyPixelShaders), "Init", new[]
        {
            typeof(MyShaderCompilationInfo).MakeByRefType(),
            typeof(PixelShader).MakeByRefType()
        });

    static void Prefix(ref MyShaderCompilationInfo info, out ShaderCompileIntercept.ShaderObjectCreationState __state)
    {
        __state = ShaderCompileIntercept.PreparePixelShaderObject(ref info);
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var hook = AccessTools.Method(typeof(ShaderCompileIntercept),
            nameof(ShaderCompileIntercept.NotePixelShaderInitBytecode));
        return ShaderObjectCompileResultTranspiler.Inject(
            instructions, hook, MyShaderProfile.ps_5_0);
    }

    static void Postfix(ref MyShaderCompilationInfo info, ref PixelShader shader, ShaderCompileIntercept.ShaderObjectCreationState __state)
    {
        ShaderCompileIntercept.RepairPixelShader(ref info, ref shader, __state);
        ShaderCompileIntercept.NotePixelShaderObject(shader, __state);
    }
}

[HarmonyPatch]
static class VertexShaderObjectPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyVertexShaders), "Init", new[]
        {
            typeof(MyShaderCompilationInfo).MakeByRefType(),
            typeof(byte[]).MakeByRefType(),
            typeof(VertexShader).MakeByRefType()
        });

    static void Prefix(ref MyShaderCompilationInfo info, out ShaderCompileIntercept.ShaderObjectCreationState __state)
    {
        __state = ShaderCompileIntercept.PrepareVertexShaderObject(ref info);
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var hook = AccessTools.Method(typeof(ShaderCompileIntercept),
            nameof(ShaderCompileIntercept.NoteVertexShaderInitBytecode));
        return ShaderObjectCompileResultTranspiler.Inject(
            instructions, hook, MyShaderProfile.vs_5_0);
    }

    static void Postfix(ref MyShaderCompilationInfo info, ref byte[] byteCode, ref VertexShader shader,
        ShaderCompileIntercept.ShaderObjectCreationState __state)
    {
        // MyVertexShaders preserves the exact compiler result as an out value.
        // Capture it independently of the IL route so one test proves both the
        // manager boundary and the object association.
        ShaderCompileIntercept.RepairVertexShader(ref info, ref byteCode, ref shader, __state);
        ShaderCompileIntercept.NoteVertexShaderOutputBytecode(byteCode);
        ShaderCompileIntercept.NoteVertexShaderObject(shader, __state);
    }
}

/// <summary>
/// Captures the exact byte array returned by MyShaderCompiler inside Keen's
/// manager Init method.  This is deliberately matched at the compile-return
/// boundary rather than at SharpDX's constructor: Harmony sees the manager's
/// call even when the C# constructor is lowered through an inlined wrapper or
/// a different native overload.
/// </summary>
static class ShaderObjectCompileResultTranspiler
{
    public static IEnumerable<CodeInstruction> Inject(
        IEnumerable<CodeInstruction> instructions,
        MethodInfo hook,
        MyShaderProfile profile)
    {
        var list = new List<CodeInstruction>(instructions);
        var inserted = 0;
        if (hook != null)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if ((list[i].opcode != OpCodes.Call && list[i].opcode != OpCodes.Callvirt) ||
                    list[i].operand is not MethodInfo method ||
                    method.DeclaringType != typeof(MyShaderCompiler) ||
                    !string.Equals(method.Name, "Compile", StringComparison.Ordinal) ||
                    method.ReturnType != typeof(byte[]))
                    continue;

                var parameters = method.GetParameters();
                if (parameters.Length == 0 ||
                    parameters[0].ParameterType != typeof(MyShaderCompilationInfo).MakeByRefType())
                    continue;

                // Compile leaves byte[] on the stack. Duplicate it for evidence
                // capture while preserving the original result for Keen's local
                // store and subsequent native shader construction.
                list.Insert(i + 1, new CodeInstruction(OpCodes.Dup));
                list.Insert(i + 2, new CodeInstruction(OpCodes.Call, hook));
                inserted++;
                i += 2;
            }
        }
        ShaderCompileIntercept.NoteShaderInitTranspiler(profile, inserted);
        return list;
    }
}

[HarmonyPatch]
static class PixelShaderLookupPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyPixelShaders), "GetShader");

    static void Postfix(PixelShader __result)
    {
        GBufferVelocity.OnPixelShaderLookup(__result);
    }
}

[HarmonyPatch]
static class VertexShaderLookupPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyVertexShaders), "GetShader");

    static void Postfix(VertexShader __result)
    {
        GBufferVelocity.OnVertexShaderLookup(__result);
    }
}

[HarmonyPatch]
static class PixelShaderDeviceEndPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyPixelShaders), "OnDeviceEnd");

    static void Prefix()
    {
        ShaderCompileIntercept.ClearPixelShaderObjects();
    }
}

[HarmonyPatch]
static class VertexShaderDeviceEndPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyVertexShaders), "OnDeviceEnd");

    static void Prefix()
    {
        ShaderCompileIntercept.ClearVertexShaderObjects();
    }
}

[HarmonyPatch]
static class ShaderResidentRefreshPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyRender11), "ProcessMessageInternal", new[]
        {
            typeof(MyRenderMessageBase),
            typeof(int)
        });

    static void Prefix(MyRenderMessageBase message, out bool __state)
    {
        ShaderWarmup.TryRun();
        __state = message != null && message.MessageType == MyRenderMessageEnum.ReloadEffects;
        if (__state)
            ShaderCompileIntercept.BeginResidentShaderRefresh();
    }

    static void Postfix(bool __state)
    {
        if (__state)
            ShaderCompileIntercept.CompleteResidentShaderRefresh(null);
    }

    static Exception Finalizer(Exception __exception, bool __state)
    {
        if (__state && __exception != null)
            ShaderCompileIntercept.CompleteResidentShaderRefresh(__exception);
        return __exception;
    }
}

[HarmonyPatch]
static class ShaderSessionStartWarmupPatch
{
    static bool Prepare() => TargetMethod() != null;

    static MethodBase TargetMethod() =>
        AccessTools.Method(typeof(MyRender11), "OnSessionStart");

    static void Postfix()
    {
        ShaderWarmup.TryRun();
    }
}
