using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using ClientPlugin.Backend;

namespace ClientPlugin.Shaders;

/// <summary>
/// Portable opaque-lighting ownership (Codex audit P3). A ray tenant claims Direct, DiffuseIndirect and/or
/// SpecularIndirect signals; once committed for the frame, other programs that contribute those signals skip.
/// On DX11 every call forwards to the companion, which executes the DX11 passes; on Vulkan the core holds the
/// same state and guest submission consults <see cref="ShouldRun"/>.
/// </summary>
public static class OpaqueLightingOwnership
{
    public const int Direct = 1, DiffuseIndirect = 2, SpecularIndirect = 4;
    const string Dx = "ClientPlugin.Shaders.OpaqueLightingOwnership";
    static readonly object Gate = new();
    static readonly Dictionary<string, int> Claims = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, int> Contributions = new(StringComparer.OrdinalIgnoreCase);
    static string committed;
    static int committedSignals;

    public static string Owner
    {
        get
        {
            if (RenderHost.IsDx11) return Dx11Call.Get(Dx, "Owner") as string;
            lock (Gate) return committed;
        }
    }

    public static bool Claim(string id, int signals)
    {
        if (RenderHost.IsDx11) return Forward("Claim", id, signals) is true;
        if (string.IsNullOrWhiteSpace(id) || signals <= 0 || (signals & ~7) != 0) return false;
        lock (Gate)
        {
            Claims[id] = signals;
            foreach (var other in Claims) if (!string.Equals(id, other.Key, StringComparison.OrdinalIgnoreCase) && (signals & other.Value) != 0) return false;
            return true;
        }
    }

    public static bool Commit(string id)
    {
        if (RenderHost.IsDx11) return Forward("Commit", id) is true;
        lock (Gate)
        {
            if (id == null || !Claims.TryGetValue(id, out int signals)) return false;
            foreach (var other in Claims) if (!string.Equals(id, other.Key, StringComparison.OrdinalIgnoreCase) && (signals & other.Value) != 0) return false;
            committed = id; committedSignals = signals; return true;
        }
    }

    /// <summary>Clears the frame's commitment; called once per frame before tenants commit.</summary>
    public static void BeginFrame()
    {
        if (RenderHost.IsDx11) { Forward("BeginFrame"); return; }
        lock (Gate) { committed = null; committedSignals = 0; }
    }

    public static void Release(string id)
    {
        if (RenderHost.IsDx11) { Forward("Release", id); return; }
        if (id == null) return;
        lock (Gate) { Claims.Remove(id); if (string.Equals(committed, id, StringComparison.OrdinalIgnoreCase)) { committed = null; committedSignals = 0; } }
    }

    public static void RegisterContribution(string programId, int signals)
    {
        if (RenderHost.IsDx11) { Forward("RegisterContribution", programId, signals); return; }
        if (programId == null) return;
        lock (Gate) Contributions[programId] = signals & 7;
    }

    public static bool ShouldRun(string programId)
    {
        if (RenderHost.IsDx11) return Forward("ShouldRun", programId) is not false;
        lock (Gate)
        {
            if (committed == null || programId == null) return true;
            if (Contributions.TryGetValue(programId, out int signals)) return (signals & committedSignals) == 0;
            // Compatibility aliases for existing tenants, as on DX11; explicit declarations supersede them.
            if (programId.StartsWith("prism.ssgi.", StringComparison.OrdinalIgnoreCase)) return (committedSignals & DiffuseIndirect) == 0;
            if (programId.StartsWith("sss.", StringComparison.OrdinalIgnoreCase) || programId.StartsWith("screen-space-shadows.", StringComparison.OrdinalIgnoreCase) || programId.StartsWith("screenspace.shadows.", StringComparison.OrdinalIgnoreCase))
                return (committedSignals & Direct) == 0;
            return true;
        }
    }

    internal static object Forward(string method, params object[] args) => RayFacade.Forward(Dx, method, args);
}

/// <summary>
/// Portable RayScene API v1 (Codex audit P3): ray tenants register a per-frame callback that receives the
/// captured scene (meshes, material parts, instances, emitters, far sun). DX11 forwards to the companion's
/// capture. On Vulkan the core keeps the same registration state; the renderer-side producer that fills the
/// frame from the native scene is P3 step 3, so frames are not delivered yet.
/// </summary>
public static class RayScene
{
    public const int ApiVersion = 1;
    const string Dx = "ClientPlugin.Shaders.RayScene";
    static readonly object Gate = new();
    static readonly Dictionary<string, Action<object[]>> Consumers = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, bool> FrameEnabled = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> FarSunRequested = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, Action<object>> Completed = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<uint, object[]> Emitters = new();
    static int requested;
    static string status = "idle";

    public static string Status => RenderHost.IsDx11 ? Dx11Call.Get(Dx, "Status") as string ?? "DX11 RayScene unavailable" : status;
    public static bool CaptureRequested => RenderHost.IsDx11 ? Dx11Call.Get(Dx, "CaptureRequested") is true : Volatile.Read(ref requested) != 0;

    /// <summary>Registers a tenant. Returns false until the renderer route is decided, so tenants retry.</summary>
    public static bool Register(string id, int apiVersion, Action<object[]> frame)
    {
        if (RenderHost.IsDx11) return Forward("Register", id, apiVersion, frame) is true;
        if (!RenderHost.IsVulkan || apiVersion != ApiVersion || string.IsNullOrWhiteSpace(id) || frame == null) return false;
        lock (Gate) { Consumers[id] = frame; FrameEnabled[id] = true; Volatile.Write(ref requested, Consumers.Count); }
        status = "Vulkan RayScene registered; the renderer scene producer is pending (P3 step 3)";
        return true;
    }

    public static void Unregister(string id)
    {
        if (RenderHost.IsDx11) { Forward("Unregister", id); return; }
        if (id == null) return;
        lock (Gate) { Consumers.Remove(id); FrameEnabled.Remove(id); FarSunRequested.Remove(id); Completed.Remove(id); Volatile.Write(ref requested, Consumers.Count); }
        OpaqueLightingOwnership.Release(id);
    }

    public static void RegisterCompleted(string id, Action<object> completed)
    {
        if (RenderHost.IsDx11) { Forward("RegisterCompleted", id, completed); return; }
        if (id != null) lock (Gate) Completed[id] = completed;
    }

    public static void SetFarSunEnabled(string id, bool enabled)
    {
        if (RenderHost.IsDx11) { Forward("SetFarSunEnabled", id, enabled); return; }
        if (id == null) return;
        lock (Gate) { if (enabled && Consumers.ContainsKey(id)) FarSunRequested.Add(id); else FarSunRequested.Remove(id); }
    }

    public static void SetFrameEnabled(string id, bool enabled)
    {
        if (RenderHost.IsDx11) { Forward("SetFrameEnabled", id, enabled); return; }
        if (id == null) return;
        lock (Gate) if (Consumers.ContainsKey(id)) FrameEnabled[id] = enabled;
    }

    /// <summary>Explicit source ownership is the only emitter deduplication criterion; positions never identify a light.</summary>
    public static void SetEmitter(uint id, float[] cameraRelativeDisc, object texture, uint nativeLightOwner, uint sourceRevision)
    {
        if (RenderHost.IsDx11) { Forward("SetEmitter", id, cameraRelativeDisc, texture, nativeLightOwner, sourceRevision); return; }
        if (id == 0 || cameraRelativeDisc == null || cameraRelativeDisc.Length != 16) throw new ArgumentException("Emitter ABI");
        lock (Gate) Emitters[id] = new object[] { id, (float[])cameraRelativeDisc.Clone(), texture, nativeLightOwner, sourceRevision };
    }

    public static void RemoveEmitter(uint id)
    {
        if (RenderHost.IsDx11) { Forward("RemoveEmitter", id); return; }
        lock (Gate) Emitters.Remove(id);
    }

    /// <summary>DX11 compiles a tenant shader to DXBC; Vulkan tenants compile through the guest compiler (P3 step 4).</summary>
    public static byte[] CompileShader(string path, int profile)
    {
        if (RenderHost.IsDx11) return Forward("CompileShader", path, profile) as byte[];
        throw new NotSupportedException("RayScene.CompileShader produces DX11 bytecode; Vulkan ray tenants compile through the guest compiler.");
    }

    public static void Clear()
    {
        if (RenderHost.IsDx11) { Forward("Clear"); return; }
        lock (Gate) Emitters.Clear();
    }

    // Vulkan state snapshots for the renderer-side producer (P3 step 3).
    internal static (string Id, Action<object[]> Frame, bool FarSun)[] EnabledConsumers()
    {
        lock (Gate)
        {
            var list = new List<(string, Action<object[]>, bool)>();
            foreach (var pair in Consumers) if (FrameEnabled.TryGetValue(pair.Key, out bool on) && on) list.Add((pair.Key, pair.Value, FarSunRequested.Contains(pair.Key)));
            return list.ToArray();
        }
    }
    internal static object[][] EmitterSnapshot() { lock (Gate) { var list = new object[Emitters.Count][]; Emitters.Values.CopyTo(list, 0); return list; } }
    internal static Action<object>[] CompletedSnapshot() { lock (Gate) { var list = new Action<object>[Completed.Count]; Completed.Values.CopyTo(list, 0); return list; } }

    static object Forward(string method, params object[] args) => RayFacade.Forward(Dx, method, args);
}

static class RayFacade
{
    /// <summary>Calls the DX11 companion and rethrows its own exception (not the reflection wrapper).</summary>
    internal static object Forward(string type, string method, object[] args)
    {
        try { return Dx11Call.Try(type, method, args, out var result) ? result : null; }
        catch (TargetInvocationException error) when (error.InnerException != null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
}
