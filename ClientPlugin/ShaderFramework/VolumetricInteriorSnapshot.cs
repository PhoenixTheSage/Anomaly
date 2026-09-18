using System;
using System.Collections.Generic;
using System.Threading;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;
using ClientPlugin.Shaders;

namespace ClientPlugin.ShaderFramework;

/// <summary>Copies gas topology on the simulation thread; render code never reads live rooms.</summary>
internal static class VolumetricInteriorSnapshot
{
    const int MaximumGrids = 64, MaximumCells = 262144;
    static readonly Dictionary<long, Tracked> TrackedGrids = new();
    static readonly List<MyEntity> Entities = new();
    static readonly List<IMyOxygenRoom> Rooms = new();
    static Snapshot current = new(Array.Empty<Grid>(), false, "Not sampled", 0);
    internal static Snapshot Capture() => Volatile.Read(ref current);

    internal static void UpdateFromGameThread()
    {
        if (MySession.Static == null || MySector.MainCamera == null || (!VolumetricMediumRegistry.Requested || VolumetricMediumRegistry.Capture(out _).Length == 0))
        { Clear(); return; }
        var grids = new List<Grid>();
        var seen = new HashSet<long>();
        bool complete = true;
        int cellCount = 0;
        Entities.Clear();
        var sphere = new BoundingSphereD(MySector.MainCamera.Position, VolumetricMediumRegistry.MaximumDistance);
        MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, Entities);
        foreach (var entity in Entities)
        {
            if (!(entity is MyCubeGrid grid) || grid.Closed) continue;
            seen.Add(grid.EntityId);
            if (grids.Count >= MaximumGrids) { complete = false; continue; }
            var gas = ((IMyCubeGrid)grid).GasSystem;
            if (gas == null) { complete = false; continue; }
            if (!TrackedGrids.TryGetValue(grid.EntityId, out var tracked) || !ReferenceEquals(tracked.Gas, gas))
            {
                tracked?.Dispose();
                tracked = new Tracked(gas);
                TrackedGrids[grid.EntityId] = tracked;
            }
            long generation = Interlocked.Read(ref tracked.Generation);
            if (gas.IsProcessingData) { tracked.Cells = null; complete = false; continue; }
            if (tracked.Cells == null || tracked.CapturedGeneration != generation)
            {
                Rooms.Clear();
                if (!gas.GetRooms(Rooms)) { complete = false; continue; }
                var cells = new HashSet<Vector3I>();
                bool ready = true;
                foreach (var room in Rooms)
                {
                    if (room.IsDirty) { ready = false; break; }
                    // Airtightness, never OxygenAmount, decides fog exclusion.
                    if (!room.IsAirtight) continue;
                    if (room.BlockCount > MaximumCells - cells.Count) { ready = false; break; }
                    foreach (var cell in room.Blocks)
                    {
                        cells.Add(cell);
                        if (cells.Count > MaximumCells) { ready = false; break; }
                    }
                    if (!ready) break;
                }
                if (!ready || gas.IsProcessingData || generation != Interlocked.Read(ref tracked.Generation))
                { tracked.Cells = null; complete = false; continue; }
                tracked.Cells = new Vector3I[cells.Count];
                cells.CopyTo(tracked.Cells);
                tracked.CapturedGeneration = generation;
            }
            cellCount += tracked.Cells.Length;
            if (cellCount > MaximumCells) { complete = false; continue; }
            grids.Add(new Grid(grid.EntityId, grid.PositionComp.WorldMatrixNormalizedInv,
                grid.GridSize, tracked.Cells, tracked, generation));
        }
        foreach (var id in new List<long>(TrackedGrids.Keys))
            if (!seen.Contains(id)) { TrackedGrids[id].Dispose(); TrackedGrids.Remove(id); }
        Volatile.Write(ref current, new Snapshot(grids.ToArray(), complete,
            complete ? "Current airtight topology" : "Degraded interior exclusion: unavailable, rebuilding or budget exceeded",
            Environment.TickCount));
        Entities.Clear(); Rooms.Clear();
    }

    internal static void Clear()
    {
        foreach (var item in TrackedGrids.Values) item.Dispose();
        TrackedGrids.Clear();
        Volatile.Write(ref current, new Snapshot(Array.Empty<Grid>(), false, "No world or no provider", 0));
    }

    internal sealed class Snapshot
    {
        internal readonly Grid[] Grids;
        internal readonly bool Complete;
        internal readonly string Status;
        internal readonly int Tick;
        internal Snapshot(Grid[] grids, bool complete, string status, int tick)
        { Grids = grids; Complete = complete; Status = status; Tick = tick; }
        internal bool IsFresh => unchecked((uint)(Environment.TickCount - Tick)) < 250;
    }
    internal sealed class Grid
    {
        internal readonly long Id, Generation;
        internal readonly MatrixD WorldToLocal;
        internal readonly float CellSize;
        internal readonly Vector3I[] Cells;
        readonly Tracked owner;
        internal Grid(long id, MatrixD inverse, float size, Vector3I[] cells, Tracked source, long generation)
        { Id=id; WorldToLocal=inverse; CellSize=size; Cells=cells; owner=source; Generation=generation; }
        internal bool IsCurrent => Interlocked.Read(ref owner.Generation) == Generation;
    }
    internal sealed class Tracked : IDisposable
    {
        internal readonly IMyGridGasSystem Gas;
        internal long Generation, CapturedGeneration = -1;
        internal Vector3I[] Cells;
        internal Tracked(IMyGridGasSystem gas)
        { Gas=gas; Gas.OnProcessingDataStart += Changed; Gas.OnProcessingDataComplete += Changed; }
        void Changed() => Interlocked.Increment(ref Generation);
        public void Dispose()
        { Gas.OnProcessingDataStart -= Changed; Gas.OnProcessingDataComplete -= Changed; Changed(); }
    }
}
