using System.Collections.Generic;
using System.Threading;
using VRage.Render.Scene;
using VRage.Render11.Culling;
using VRage.Render11.GeometryStage2.Instancing;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Velocity;

/// <summary>
/// Absolute <see cref="MatrixD"/> history keyed by Keen ActorID. Snapshot after
/// Stage 2 <c>UpdateMatrices</c> and old <c>UpdateCullProxies</c>; swap at
/// <c>DrawGameScene</c> postfix. Do not hook <c>MyInstance.UpdateWorldMatrix</c>.
/// <c>UpdateMatrices</c> runs per view (GBuffer + shadows + env probe). Snapshot
/// once per frame — repeating it was Parallel.Scheduler / Thread CPU Load.
/// Stage 2 and the old pipeline still run on different scheduler workers, so
/// dictionary writes take a lock (unsynchronized <c>Current[id]=</c> resized
/// under two threads and crashed after world load).
/// </summary>
public sealed class ActorHistory : IVelocityHistory
{
    public const float TeleportMeters = 30f;
    public const int KeepFrames = 3;

    public static readonly ActorHistory Instance = new();

    static readonly object Gate = new();
    static readonly float TeleportDistanceSq = TeleportMeters * TeleportMeters;
    static readonly Dictionary<uint, Slot> Previous = new();
    static readonly Dictionary<uint, Slot> Current = new();
    static readonly HashSet<uint> Teleported = new();
    static readonly List<uint> PruneScratch = new();

    int frame;
    int stage2Once;
    int oldOnce;

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
        Interlocked.Increment(ref frame);
        Interlocked.Exchange(ref stage2Once, 0);
        Interlocked.Exchange(ref oldOnce, 0);
    }

    internal void EndFrame()
    {
        var now = Volatile.Read(ref frame);
        lock (Gate)
        {
            foreach (var kv in Current)
                Previous[kv.Key] = kv.Value;
            Current.Clear();
            Teleported.Clear();

            PruneScratch.Clear();
            foreach (var kv in Previous)
            {
                if (now - kv.Value.LastSeen > KeepFrames)
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
        if (Interlocked.CompareExchange(ref stage2Once, 1, 0) != 0)
            return;

        var instances = cullQuery.Results.Instances;
        var count = instances.Count;
        var now = Volatile.Read(ref frame);
        for (var i = 0; i < count; i++)
            Record(instances[i], now);
    }

    internal void SnapshotOld(MyCullQuery cullQuery)
    {
        if (cullQuery?.Results?.CullProxies == null)
            return;
        if (Interlocked.CompareExchange(ref oldOnce, 1, 0) != 0)
            return;

        var proxies = cullQuery.Results.CullProxies;
        var count = proxies.Count;
        var now = Volatile.Read(ref frame);
        for (var i = 0; i < count; i++)
            Record(proxies[i], now);
    }

    internal void Clear()
    {
        lock (Gate)
        {
            Previous.Clear();
            Current.Clear();
            Teleported.Clear();
            PruneScratch.Clear();
        }

        Volatile.Write(ref frame, 0);
        Volatile.Write(ref stage2Once, 0);
        Volatile.Write(ref oldOnce, 0);
    }

    void Record(MyInstance instance, int now)
    {
        if (instance == null)
            return;
        var actor = instance.Owner?.Owner;
        RecordActor(actor, instance.ActorID, now);
    }

    void Record(MyCullProxy proxy, int now)
    {
        if (proxy?.Parent == null)
            return;
        var rps = proxy.RenderableProxies;
        if (rps != null && rps.Length > 0 && rps[0].VoxelCommonObjectData.IsValid)
            return;
        RecordActor(proxy.Parent.Owner, proxy.OwnerID, now);
    }

    void RecordActor(IMyActor actor, uint fallbackId, int now)
    {
        if (actor != null && actor.IsDestroyed)
            return;

        var id = actor != null ? actor.ID : fallbackId;
        if (id == 0 || actor == null)
            return;

        var world = actor.LastWorldMatrix;
        lock (Gate)
        {
            if (Previous.TryGetValue(id, out var prev))
            {
                if (Vector3D.DistanceSquared(prev.World.Translation, world.Translation) > TeleportDistanceSq)
                    Teleported.Add(id);
            }

            Current[id] = new Slot { World = world, LastSeen = now };
        }
    }
}
