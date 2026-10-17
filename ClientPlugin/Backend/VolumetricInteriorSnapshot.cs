using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;
using ClientPlugin.Backend;

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
        if (MySession.Static == null || MySector.MainCamera == null || !VolumeRegistryState.Requested || !VolumeRegistryState.HasEnabledMedia)
        { Clear(); return; }
        var grids = new List<Grid>();
        var seen = new HashSet<long>();
        bool complete = true;
        string incompleteReason=null;
        int cellCount = 0, selectedGrids=0;
        Entities.Clear();
        var sphere = new BoundingSphereD(MySector.MainCamera.Position, VolumeRegistryState.MaximumDistance);
        MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref sphere, Entities);
        foreach (var entity in Entities)
        {
            // Projected/render-only grids cannot contain simulation rooms.
            if (!(entity is MyCubeGrid grid) || grid.Closed || grid.IsPreview || !grid.CreatePhysics) continue;
            if(grid.Physics==null){complete=false;incompleteReason="physical grid initialization pending";continue;}
            if (selectedGrids++ >= MaximumGrids) { complete = false; continue; }
            seen.Add(grid.EntityId);
            var gas = ((IMyCubeGrid)grid).GasSystem;
            if (gas == null) { complete = false; incompleteReason="gas system unavailable (world oxygen="+MySession.Static.Settings.EnableOxygen+", pressurization="+MySession.Static.Settings.EnableOxygenPressurization+")";continue; }
            if (!TrackedGrids.TryGetValue(grid.EntityId, out var tracked) || !ReferenceEquals(tracked.Gas, gas))
            {
                tracked?.Dispose();
                tracked = new Tracked(gas, (IMyCubeGrid)grid);
                TrackedGrids[grid.EntityId] = tracked;
            }
            if(tracked.NeedsTopologyRefresh){tracked.RequestInitialTopology();complete=false;continue;}
            long generation = Interlocked.Read(ref tracked.Generation);
            if (gas.IsProcessingData) { tracked.Cells = null; complete = false; continue; }
            if (tracked.Cells == null || tracked.CapturedGeneration != generation)
            {
                Rooms.Clear();
                if (!gas.GetRooms(Rooms)) { tracked.RequestInitialTopology();complete = false; continue; }
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
            complete ? "Current airtight topology" : "Degraded interior exclusion: "+(incompleteReason??"unavailable, rebuilding or budget exceeded"),
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
        readonly bool restoreForce;
        readonly IMyCubeGrid grid;
        readonly HashSet<Sandbox.ModAPI.IMyDoor> doors=new();
        bool initialRequested;
        internal bool NeedsTopologyRefresh {get;private set;}=true;
        internal Tracked(IMyGridGasSystem gas)
        {
            Gas=gas;restoreForce=!Gas.ForcePressurize;
            Gas.OnProcessingDataStart += Changed; Gas.OnProcessingDataComplete += Changed;
            // The engine otherwise discards topology for empty, unvented rooms.
            // Airtight exclusion must work independently of OxygenAmount.
            if(restoreForce)Gas.ForcePressurize=true;
        }
        internal Tracked(IMyGridGasSystem gas,IMyCubeGrid source):this(gas)
        {
            grid=source;
            grid.OnBlockAdded+=BlockAdded;grid.OnBlockRemoved+=BlockRemoved;grid.OnBlockIntegrityChanged+=BlockChanged;
            var blocks=new List<IMySlimBlock>();grid.GetBlocks(blocks);
            foreach(var block in blocks)AttachDoor(block);
        }
        void AttachDoor(IMySlimBlock block)
        {if(block.FatBlock is Sandbox.ModAPI.IMyDoor door && doors.Add(door))door.OnDoorStateChanged+=DoorChanged;}
        void BlockAdded(IMySlimBlock block){AttachDoor(block);TopologyChanged();}
        void BlockRemoved(IMySlimBlock block)
        {if(block.FatBlock is Sandbox.ModAPI.IMyDoor door && doors.Remove(door))door.OnDoorStateChanged-=DoorChanged;TopologyChanged();}
        void BlockChanged(IMySlimBlock block)=>TopologyChanged();
        void DoorChanged(Sandbox.ModAPI.IMyDoor door,bool open)=>TopologyChanged();
        internal void TopologyChanged()
        {
            // The engine's ForcePressurize-only incremental removal path has
            // no deleted-block list; regenerate ventless topology explicitly.
            initialRequested=false;NeedsTopologyRefresh=true;Cells=null;Changed();
        }
        internal void RequestInitialTopology()
        {
            if(initialRequested || Gas.IsProcessingData)return;
            // The ModAPI setter only changes a flag. Initial unvented grids
            // and their later block/door edits need an explicit full rebuild.
            var start=Gas.GetType().GetMethod("StartGenerateAirtightData",BindingFlags.Instance|BindingFlags.NonPublic,null,Type.EmptyTypes,null);
            if(start==null)return; // Keep the snapshot incomplete on unsupported engines.
            initialRequested=true;NeedsTopologyRefresh=false;
            try{start.Invoke(Gas,null);}catch{initialRequested=false;NeedsTopologyRefresh=true;throw;}
        }
        void Changed() => Interlocked.Increment(ref Generation);
        public void Dispose()
        {
            if(grid!=null){grid.OnBlockAdded-=BlockAdded;grid.OnBlockRemoved-=BlockRemoved;grid.OnBlockIntegrityChanged-=BlockChanged;}
            foreach(var door in doors)door.OnDoorStateChanged-=DoorChanged;doors.Clear();
            Gas.OnProcessingDataStart -= Changed; Gas.OnProcessingDataComplete -= Changed;
            if(restoreForce && Gas.ForcePressurize)Gas.ForcePressurize=false;Changed();
        }
    }
}
