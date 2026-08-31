using System;
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

    static readonly object Gate = new();
    static readonly System.Collections.Generic.Dictionary<uint, Slot> Previous = new();
    static readonly System.Collections.Generic.Dictionary<uint, Slot> Current = new();
    static readonly System.Collections.Generic.List<uint> PruneScratch = new();

    int frame;

    struct Slot
    {
        public Matrix[] Bones;
        public int LastSeen;
    }

    public int TrackedCount
    {
        get
        {
            lock (Gate)
                return Previous.Count;
        }
    }

    public bool TryGetPrevious(uint actorId, int expectedCount, out Matrix[] bones)
    {
        lock (Gate)
        {
            if (Previous.TryGetValue(actorId, out var slot) &&
                slot.Bones != null &&
                slot.Bones.Length == expectedCount)
            {
                bones = slot.Bones;
                return true;
            }
        }

        bones = null;
        return false;
    }

    internal void BeginFrame()
    {
        lock (Gate)
            frame++;
    }

    internal void EndFrame()
    {
        lock (Gate)
        {
            foreach (var kv in Current)
                Previous[kv.Key] = kv.Value;
            Current.Clear();

            PruneScratch.Clear();
            foreach (var kv in Previous)
            {
                if (frame - kv.Value.LastSeen > ActorHistory.KeepFrames)
                    PruneScratch.Add(kv.Key);
            }

            for (var i = 0; i < PruneScratch.Count; i++)
                Previous.Remove(PruneScratch[i]);
            PruneScratch.Clear();
        }
    }

    internal void Snapshot(uint actorId, Matrix[] bones)
    {
        if (actorId == 0 || bones == null || bones.Length == 0)
            return;

        var copy = new Matrix[bones.Length];
        Array.Copy(bones, copy, bones.Length);
        lock (Gate)
            Current[actorId] = new Slot { Bones = copy, LastSeen = frame };
    }

    internal void Clear()
    {
        lock (Gate)
        {
            Previous.Clear();
            Current.Clear();
            PruneScratch.Clear();
            frame = 0;
        }
    }
}
