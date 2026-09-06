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
/// <c>UpdateMatrices</c> runs per view (GBuffer + shadows + env probe). Merge
/// every view into <c>Current</c> (skip an actor already recorded this frame).
/// Snapshot-once dropped GBuffer ships when shadows/env ran first.
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
    static readonly Dictionary<uint, LocalRows> PreviousLocal = new();
    static readonly Dictionary<uint, LocalRows> CurrentLocal = new();
    static readonly HashSet<uint> Teleported = new();
    static readonly List<uint> PruneScratch = new();

    int frame;

    struct Slot
    {
        public MatrixD World;
        public int LastSeen;
    }

    struct LocalRows
    {
        public Vector4 R0;
        public Vector4 R1;
        public Vector4 R2;
    }

    public int TrackedActorCount
    {
        get
        {
            lock (Gate)
                return Previous.Count;
        }
    }

    /// <summary>Stage 2 instances on the last Main-view UpdateMatrices.</summary>
    public int VisibleMainLast { get; private set; }

    public int TrackedLocalCount
    {
        get
        {
            lock (Gate)
                return PreviousLocal.Count;
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

    /// <summary>
    /// Last main-view GBuffer object-CB rows (<c>m_row0–2</c> / <c>get_object_matrix</c>).
    /// Old-pipeline cube VS cannot use absolute <see cref="MatrixD"/> packed with a
    /// different camera than the draw.
    /// </summary>
    public bool TryGetPreviousLocal(uint actorId, out Vector4 row0, out Vector4 row1, out Vector4 row2)
    {
        lock (Gate)
        {
            if (PreviousLocal.TryGetValue(actorId, out var rows))
            {
                row0 = rows.R0;
                row1 = rows.R1;
                row2 = rows.R2;
                return true;
            }
        }

        row0 = default;
        row1 = default;
        row2 = default;
        return false;
    }

    /// <summary>
    /// First main-view GBuffer draw this frame wins. Env-probe / extra ViewId
    /// must not replace these rows or next frame's prev grid is the wrong camera.
    /// </summary>
    internal void RecordGBufferLocal(uint actorId, Vector4 row0, Vector4 row1, Vector4 row2)
    {
        if (actorId == 0)
            return;
        lock (Gate)
        {
            if (CurrentLocal.ContainsKey(actorId))
                return;
            CurrentLocal[actorId] = new LocalRows { R0 = row0, R1 = row1, R2 = row2 };
        }
    }

    public bool WasTeleported(uint actorId)
    {
        lock (Gate)
            return Teleported.Contains(actorId);
    }

    internal void BeginFrame()
    {
        Interlocked.Increment(ref frame);
    }

    internal void EndFrame()
    {
        var now = Volatile.Read(ref frame);
        lock (Gate)
        {
            foreach (var kv in Current)
                Previous[kv.Key] = kv.Value;
            Current.Clear();

            PreviousLocal.Clear();
            foreach (var kv in CurrentLocal)
                PreviousLocal[kv.Key] = kv.Value;
            CurrentLocal.Clear();

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

        var instances = cullQuery.Results.Instances;
        var count = instances.Count;
        if (cullQuery.ViewType == MyViewType.Main)
            VisibleMainLast = count;
        var now = Volatile.Read(ref frame);
        for (var i = 0; i < count; i++)
            Record(instances[i], now);
    }

    internal void SnapshotOld(MyCullQuery cullQuery)
    {
        if (cullQuery?.Results?.CullProxies == null)
            return;

        // Object-CB rows are camera-relative to this view. Only Main matches
        // the GBuffer draw. ViewId==0 is not Main (env / extra views reuse 0).
        var recordLocal = cullQuery.ViewType == MyViewType.Main;
        var proxies = cullQuery.Results.CullProxies;
        var count = proxies.Count;
        var now = Volatile.Read(ref frame);
        for (var i = 0; i < count; i++)
            Record(proxies[i], now, recordLocal);
    }

    internal void Clear()
    {
        lock (Gate)
        {
            Previous.Clear();
            Current.Clear();
            PreviousLocal.Clear();
            CurrentLocal.Clear();
            Teleported.Clear();
            PruneScratch.Clear();
        }

        Volatile.Write(ref frame, 0);
    }

    void Record(MyInstance instance, int now)
    {
        if (instance == null)
            return;
        var actor = instance.Owner?.Owner;
        RecordActor(actor, instance.ActorID, now);
    }

    void Record(MyCullProxy proxy, int now, bool recordLocal)
    {
        if (proxy?.Parent == null)
            return;
        var rps = proxy.RenderableProxies;
        if (rps != null && rps.Length > 0 &&
            rps[0].VoxelCommonObjectData.IsValid &&
            !rps[0].NonVoxelObjectData.IsValid)
            return;
        var actor = proxy.Parent.Owner;
        var id = actor != null ? actor.ID : proxy.OwnerID;
        RecordActor(actor, id, now);
        if (!recordLocal || id == 0 || rps == null || rps.Length == 0)
            return;
        var common = rps[0].CommonObjectData;
        RecordGBufferLocal(id, common.m_row0, common.m_row1, common.m_row2);
    }

    void RecordActor(IMyActor actor, uint fallbackId, int now)
    {
        if (actor != null && actor.IsDestroyed)
            return;

        // Stage 2 packs t15 by MyInstance.ActorID (GPU indexes instance slots, not IDs);
        // old pipeline binds VS b6 by actor.ID.
        // Prefer the id the velocity bind will look up.
        var id = fallbackId != 0 ? fallbackId : actor != null ? actor.ID : 0;
        if (id == 0)
            return;

        // Pack looks up MyInstance.ActorID even when Owner is missing this view.
        if (actor == null)
            return;

        var world = actor.WorldMatrix;
        lock (Gate)
        {
            if (Current.TryGetValue(id, out var already) && already.LastSeen == now)
                return;

            if (Previous.TryGetValue(id, out var prev))
            {
                if (Vector3D.DistanceSquared(prev.World.Translation, world.Translation) > TeleportDistanceSq)
                    Teleported.Add(id);
            }

            Current[id] = new Slot { World = world, LastSeen = now };
        }
    }
}
