using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SharpDX.Direct3D11;
using VRage.Render11.Common;
using VRage.Render11.Resources;
using VRageMath;

namespace ClientPlugin.ShaderFramework;

/// <summary>Render-thread immutable structured buffers; world->local transforms refresh per snapshot.</summary>
internal sealed class VolumetricInteriorUpload : IDisposable
{
    internal ISrvBuffer Grids, Cells;
    readonly Dictionary<long,Entry> cache=new();
    Entry[] uploaded=Array.Empty<Entry>();
    sealed class Entry
    {
        internal long Generation;
        internal Vector3I[] Original,Sorted;
    }
    internal long Revision { get; private set; }
    internal uint GridCount { get; private set; }
    internal bool Complete { get; private set; }
    internal string Status { get; private set; } = "Not uploaded";

    internal void Upload(VolumetricInteriorSnapshot.Snapshot snapshot, Vector3D camera)
    {
        if(Grids!=null) MyManagers.Buffers.Dispose(Grids);
        Grids=null; GridCount=0;
        var entries=new List<Entry>();
        var seen=new HashSet<long>();
        Complete=snapshot.IsFresh && snapshot.Complete;
        Status=snapshot.IsFresh?snapshot.Status:"Degraded interior exclusion: stale simulation snapshot";
        var rows=new List<Vector4>();
        var cells=new List<Vector4I>();
        if(snapshot.IsFresh)
        foreach(var grid in snapshot.Grids)
        {
            if(!grid.IsCurrent) { Complete=false; Status="Degraded interior exclusion: topology changed"; continue; }
            seen.Add(grid.Id);
            if(!cache.TryGetValue(grid.Id,out var entry) || entry.Generation!=grid.Generation || !ReferenceEquals(entry.Original,grid.Cells))
            {
                var sorted=(Vector3I[])grid.Cells.Clone();
                Array.Sort(sorted,(a,b)=>a.X!=b.X?a.X.CompareTo(b.X):a.Y!=b.Y?a.Y.CompareTo(b.Y):a.Z.CompareTo(b.Z));
                entry=new Entry{Generation=grid.Generation,Original=grid.Cells,Sorted=sorted}; cache[grid.Id]=entry;
            }
            var copy=entry.Sorted;
            if(copy.Length==0) continue;
            entries.Add(entry);
            var transform=MatrixD.CreateTranslation(camera)*grid.WorldToLocal;
            rows.Add(new Vector4((float)transform.M11,(float)transform.M12,(float)transform.M13,(float)transform.M14));
            rows.Add(new Vector4((float)transform.M21,(float)transform.M22,(float)transform.M23,(float)transform.M24));
            rows.Add(new Vector4((float)transform.M31,(float)transform.M32,(float)transform.M33,(float)transform.M34));
            rows.Add(new Vector4((float)transform.M41,(float)transform.M42,(float)transform.M43,(float)transform.M44));
            rows.Add(new Vector4(grid.CellSize,cells.Count,copy.Length,1));
            var min=copy[0]; var max=copy[0];
            foreach(var cell in copy) { min=Vector3I.Min(min,cell); max=Vector3I.Max(max,cell); cells.Add(new Vector4I(cell.X,cell.Y,cell.Z,0)); }
            rows.Add(new Vector4(min.X,min.Y,min.Z,0)); rows.Add(new Vector4(max.X,max.Y,max.Z,0));
            GridCount++;
        }
        if(rows.Count==0) rows.Add(Vector4.Zero);
        if(cells.Count==0) cells.Add(default);
        foreach(var id in cache.Keys.ToArray()) if(!seen.Contains(id)) cache.Remove(id);
        try {
            Grids=Create("Anomaly.VolumeInteriorGrids",rows.ToArray());
            if(Cells==null || !uploaded.SequenceEqual(entries)) {
                if(Cells!=null) MyManagers.Buffers.Dispose(Cells);
                Revision++;
                Cells=null; Cells=Create("Anomaly.VolumeInteriorCells",cells.ToArray()); uploaded=entries.ToArray();
            }
        }
        catch { Dispose(); throw; }
    }

    static ISrvBuffer Create<T>(string name,T[] data) where T:struct
    {
        var pinned=GCHandle.Alloc(data,GCHandleType.Pinned);
        try { return MyManagers.Buffers.CreateSrv(name,data.Length,16,pinned.AddrOfPinnedObject(),ResourceUsage.Immutable); }
        finally { pinned.Free(); }
    }
    public void Dispose()
    {
        if(Grids!=null) MyManagers.Buffers.Dispose(Grids);
        if(Cells!=null) MyManagers.Buffers.Dispose(Cells);
        Grids=Cells=null; GridCount=0; Complete=false; cache.Clear(); uploaded=Array.Empty<Entry>();
    }
}
