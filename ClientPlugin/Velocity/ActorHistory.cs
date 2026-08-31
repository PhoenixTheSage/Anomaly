using System.Collections.Generic;
using VRage.Render.Scene;
using VRage.Render11.Culling;
using VRage.Render11.GeometryStage2.Instancing;
using VRageMath;

namespace ClientPlugin.Velocity;

/// <summary>
/// Absolute <see cref="MatrixD"/> history keyed by Keen ActorID. Snapshot after
/// Stage 2 <c>UpdateMatrices</c> and old <c>UpdateCullProxies</c>; swap at
/// <c>DrawGameScene</c> postfix. Do not hook <c>MyInstance.UpdateWorldMatrix</c>.
/// </summary>
public sealed class ActorHistory : IVelocityHistory
{
    public const float TeleportMeters = 30f;
    public const int KeepFrames = 3;

    public static readonly ActorHistory Instance = new();

    static readonly float TeleportDistanceSq = TeleportMeters * TeleportMeters;
    static readonly object Gate = new();
    static readonly Dictionary<uint, Slot> Previous = new();
    static readonly Dictionary<uint, Slot> Current = new();
    static readonly HashSet<uint> Teleported = new();
    static readonly List<uint> PruneScratch = new();

    int frame;

    struct Slot
    {
        public MatrixD World;
        public int LastSeen;
    }

    public int TrackedActorCount
    {
        get
        {
            lock (Gate)
                return Previous.Count;
        }
    }

    public bool TryGetPrevious(uint actorId, out MatrixD world)
    {
        lock (Gate)
        {
            if (Previous.TryGetValue(actorId, out var slot))
            {
                world = slot.World;
                return true;
            }
        }

        world = default;
        return false;
    }

    public bool WasTeleported(uint actorId)
    {
        lock (Gate)
            return Teleported.Contains(actorId);
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
            Teleported.Clear();

            PruneScratch.Clear();
            foreach (var kv in Previous)
            {
                if (frame - kv.Value.LastSeen > KeepFrames)
                    PruneScratch.Add(kv.Key);
            }

            for (var i = 0; i < PruneScratch.Count; i++)
                Previous.Remove(PruneScratch[i]);
            PruneScratch.Clear();
        }
    }

    internal void SnapshotStage2(MyCullQuery cullQuery)
    {
        if (cullQuery?.Results?.Instances == null)
            return;

        var instances = cullQuery.Results.Instances;
        lock (Gate)
        {
            var count = instances.Count;
            for (var i = 0; i < count; i++)
                RecordUnlocked(instances[i]);
        }
    }

    internal void SnapshotOld(MyCullQuery cullQuery)
    {
        if (cullQuery?.Results?.CullProxies == null)
            return;

        var proxies = cullQuery.Results.CullProxies;
        lock (Gate)
        {
            var count = proxies.Count;
            for (var i = 0; i < count; i++)
                RecordUnlocked(proxies[i]);
        }
    }

    internal void Clear()
    {
        lock (Gate)
        {
            Previous.Clear();
            Current.Clear();
            Teleported.Clear();
            PruneScratch.Clear();
            frame = 0;
        }
    }

    void RecordUnlocked(MyInstance instance)
    {
        if (instance == null)
            return;
        var actor = instance.Owner?.Owner;
        RecordActorUnlocked(actor, instance.ActorID);
    }

    void RecordUnlocked(MyCullProxy proxy)
    {
        if (proxy?.Parent == null)
            return;
        RecordActorUnlocked(proxy.Parent.Owner, proxy.OwnerID);
    }

    void RecordActorUnlocked(IMyActor actor, uint fallbackId)
    {
        if (actor != null && actor.IsDestroyed)
            return;

        var id = actor != null ? actor.ID : fallbackId;
        if (id == 0)
            return;

        var world = actor != null ? actor.LastWorldMatrix : default;
        if (actor == null)
            return;

        if (Previous.TryGetValue(id, out var prev))
        {
            if (Vector3D.DistanceSquared(prev.World.Translation, world.Translation) > TeleportDistanceSq)
                Teleported.Add(id);
        }

        Current[id] = new Slot { World = world, LastSeen = frame };
    }
}
