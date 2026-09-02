using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using VRageMath;

namespace ClientPlugin.Velocity;

/// <summary>
/// Previous skinning palettes keyed by ActorID. Snapshot from
/// <c>MySkinningComponent.SetAnimationBones</c>; swap with actor history.
/// Count mismatch → no prev bones (camera term).
/// </summary>
public sealed class BoneHistory
{
    public const int MaxBones = 60;

    public static readonly BoneHistory Instance = new();

    static readonly ConcurrentDictionary<uint, Slot> Previous = new();
    static readonly ConcurrentDictionary<uint, Slot> Current = new();
    static readonly List<uint> PruneScratch = new();

    int frame;

    struct Slot
    {
        public Matrix[] Bones;
        public int LastSeen;
    }

    public int TrackedCount => Previous.Count;

    public bool TryGetPrevious(uint actorId, int expectedCount, out Matrix[] bones)
    {
        if (Previous.TryGetValue(actorId, out var slot) &&
            slot.Bones != null &&
            slot.Bones.Length == expectedCount)
        {
            bones = slot.Bones;
            return true;
        }

        bones = null;
        return false;
    }

    internal void BeginFrame()
    {
        Interlocked.Increment(ref frame);
    }

    internal void EndFrame()
    {
        var now = Volatile.Read(ref frame);
        foreach (var kv in Current)
            Previous[kv.Key] = kv.Value;
        Current.Clear();

        PruneScratch.Clear();
        foreach (var kv in Previous)
        {
            if (now - kv.Value.LastSeen > ActorHistory.KeepFrames)
                PruneScratch.Add(kv.Key);
        }

        for (var i = 0; i < PruneScratch.Count; i++)
            Previous.TryRemove(PruneScratch[i], out _);
        PruneScratch.Clear();
    }

    internal void Snapshot(uint actorId, Matrix[] bones)
    {
        if (actorId == 0 || bones == null || bones.Length == 0)
            return;

        var now = Volatile.Read(ref frame);
        if (Current.TryGetValue(actorId, out var existing) &&
            existing.Bones != null &&
            existing.Bones.Length == bones.Length)
        {
            Array.Copy(bones, existing.Bones, bones.Length);
            existing.LastSeen = now;
            Current[actorId] = existing;
            return;
        }

        var copy = new Matrix[bones.Length];
        Array.Copy(bones, copy, bones.Length);
        Current[actorId] = new Slot { Bones = copy, LastSeen = now };
    }

    internal void Clear()
    {
        Previous.Clear();
        Current.Clear();
        PruneScratch.Clear();
        Volatile.Write(ref frame, 0);
    }
}
