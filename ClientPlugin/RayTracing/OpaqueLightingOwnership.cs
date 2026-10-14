using System;
using System.Collections.Generic;

namespace ClientPlugin.Shaders;

public static class OpaqueLightingOwnership
{
    public const int Direct = 1, DiffuseIndirect = 2, SpecularIndirect = 4;
    static readonly object Gate = new();
    static readonly Dictionary<string, int> Claims = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, int> Contributions = new(StringComparer.OrdinalIgnoreCase);
    static string committed;
    static int committedSignals;
    public static string Owner { get { lock (Gate) return committed; } }
    public static bool Claim(string id, int signals)
    {
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
        lock (Gate)
        {
            if (!Claims.TryGetValue(id, out int signals)) return false;
            foreach (var other in Claims) if (!string.Equals(id, other.Key, StringComparison.OrdinalIgnoreCase) && (signals & other.Value) != 0) return false;
            committed = id; committedSignals = signals; return true;
        }
    }
    public static void BeginFrame() { lock (Gate) { committed = null; committedSignals = 0; } }
    public static void Release(string id) { lock (Gate) { Claims.Remove(id); if (committed == id) { committed = null; committedSignals = 0; } } }
    public static void RegisterContribution(string programId, int signals) { lock (Gate) Contributions[programId] = signals & 7; }
    public static bool ShouldRun(string programId)
    {
        lock (Gate)
        {
            if (committed == null) return true;
            if (Contributions.TryGetValue(programId, out int signals)) return (signals & committedSignals) == 0;
            // Compatibility for existing tenants; explicit declarations supersede these aliases.
            if (programId.StartsWith("prism.ssgi.", StringComparison.OrdinalIgnoreCase)) return (committedSignals & DiffuseIndirect) == 0;
            if (programId.StartsWith("sss.", StringComparison.OrdinalIgnoreCase) || programId.StartsWith("screen-space-shadows.", StringComparison.OrdinalIgnoreCase) || programId.StartsWith("screenspace.shadows.", StringComparison.OrdinalIgnoreCase)) return (committedSignals & Direct) == 0;
            return true;
        }
    }
}
