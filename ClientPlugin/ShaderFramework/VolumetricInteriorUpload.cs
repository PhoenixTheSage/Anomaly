using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SharpDX.Direct3D11;
using VRage.Render11.Common;
using VRage.Render11.Resources;
using VRage.Render11.RenderContext;
using VRageMath;
using VRageRender;

namespace ClientPlugin.ShaderFramework;

/// <summary>Immutable topology buffers and discard-written transforms, owned by the render thread.</summary>
internal sealed class VolumetricInteriorUpload : IDisposable
{
    internal ISrvBuffer Grids, Cells;
    readonly Dictionary<long,Entry> cache=new();
    readonly List<Entry> entries = new();
    readonly List<long> removed = new();
    Vector4[] rows = new Vector4[7];
    int gridCapacity;
    bool gridsDynamic;
    Entry[] uploaded=Array.Empty<Entry>();
    sealed class Entry
    {
        internal long Generation;
        internal Vector3I[] Original;
        internal Vector4I[] Cells;
        internal Vector3I Min, Max;
        internal bool Seen;
    }
    internal long Revision { get; private set; }
    internal uint GridCount { get; private set; }
    internal bool Complete { get; private set; }
    internal string Status { get; private set; } = "Not uploaded";

    internal void Upload(VolumetricInteriorSnapshot.Snapshot snapshot, Vector3D camera, MyRenderContext rc = null)
    {
        GridCount=0; entries.Clear();
        foreach (var entry in cache.Values) entry.Seen = false;
        bool fresh = snapshot.IsFresh;
        Complete=fresh && snapshot.Complete;
        Status=fresh?snapshot.Status:"Degraded interior exclusion: stale simulation snapshot";
        int rowCount = 0, cellCount = 0;
        if (rows.Length < Math.Max(1, snapshot.Grids.Length * 7))
            Array.Resize(ref rows, Math.Max(rows.Length * 2, snapshot.Grids.Length * 7));
        if(fresh)
        foreach(var grid in snapshot.Grids)
        {
            if(!grid.IsCurrent) { Complete=false; Status="Degraded interior exclusion: topology changed"; continue; }
            if(!cache.TryGetValue(grid.Id,out var entry) || entry.Generation!=grid.Generation || !ReferenceEquals(entry.Original,grid.Cells))
            {
                var sorted=(Vector3I[])grid.Cells.Clone();
                Array.Sort(sorted,(a,b)=>a.X!=b.X?a.X.CompareTo(b.X):a.Y!=b.Y?a.Y.CompareTo(b.Y):a.Z.CompareTo(b.Z));
                var packed = new Vector4I[sorted.Length];
                var min = sorted.Length == 0 ? Vector3I.Zero : sorted[0];
                var max = min;
                for (int i = 0; i < sorted.Length; i++)
                {
                    var cell = sorted[i];
                    min = Vector3I.Min(min, cell); max = Vector3I.Max(max, cell);
                    packed[i] = new Vector4I(cell.X, cell.Y, cell.Z, 0);
                }
                entry=new Entry{Generation=grid.Generation,Original=grid.Cells,Cells=packed,Min=min,Max=max}; cache[grid.Id]=entry;
            }
            entry.Seen = true;
            var copy=entry.Cells;
            if(copy.Length==0) continue;
            entries.Add(entry);
            var transform=MatrixD.CreateTranslation(camera)*grid.WorldToLocal;
            rows[rowCount++] = new Vector4((float)transform.M11,(float)transform.M12,(float)transform.M13,(float)transform.M14);
            rows[rowCount++] = new Vector4((float)transform.M21,(float)transform.M22,(float)transform.M23,(float)transform.M24);
            rows[rowCount++] = new Vector4((float)transform.M31,(float)transform.M32,(float)transform.M33,(float)transform.M34);
            rows[rowCount++] = new Vector4((float)transform.M41,(float)transform.M42,(float)transform.M43,(float)transform.M44);
            rows[rowCount++] = new Vector4(grid.CellSize,cellCount,copy.Length,1);
            rows[rowCount++] = new Vector4(entry.Min.X,entry.Min.Y,entry.Min.Z,0);
            rows[rowCount++] = new Vector4(entry.Max.X,entry.Max.Y,entry.Max.Z,0);
            cellCount += copy.Length;
            GridCount++;
        }
        if(rowCount==0) { rows[0] = Vector4.Zero; rowCount = 1; }
        removed.Clear();
        foreach (var pair in cache) if (!pair.Value.Seen) removed.Add(pair.Key);
        foreach (var id in removed) cache.Remove(id);
        try {
            WriteGrids(rc, rowCount);
            bool changed = Cells == null || uploaded.Length != entries.Count;
            for (int i = 0; !changed && i < uploaded.Length; i++) changed = !ReferenceEquals(uploaded[i], entries[i]);
            if(changed) {
                var cells = new Vector4I[Math.Max(cellCount, 1)];
                int offset = 0;
                foreach (var entry in entries) { Array.Copy(entry.Cells, 0, cells, offset, entry.Cells.Length); offset += entry.Cells.Length; }
                if(Cells!=null) MyManagers.Buffers.Dispose(Cells);
                Revision++;
                Cells=null; Cells=Create("Anomaly.VolumeInteriorCells",cells); uploaded=entries.ToArray();
            }
        }
        catch { Dispose(); throw; }
    }

    void WriteGrids(MyRenderContext rc, int count)
    {
        if (rc == null)
        {
            // Offline callers do not supply a render context; immutable buffers retain that path.
            if (Grids != null) MyManagers.Buffers.Dispose(Grids);
            var exact = new Vector4[count]; Array.Copy(rows, exact, count);
            Grids = Create("Anomaly.VolumeInteriorGrids", exact);
            gridCapacity = count; gridsDynamic = false;
            return;
        }
        if (Grids == null || !gridsDynamic || gridCapacity < count)
        {
            if (Grids != null) MyManagers.Buffers.Dispose(Grids);
            Grids = null;
            gridCapacity = rows.Length;
            Grids = MyManagers.Buffers.CreateSrv("Anomaly.VolumeInteriorGrids", gridCapacity, 16, usage: ResourceUsage.Dynamic);
            gridsDynamic = true;
        }
        // WRITE_DISCARD lets D3D rename storage referenced by earlier deferred draws.
        var map = MyMapping.MapDiscard(rc, Grids);
        try { map.WriteAndPosition(rows, count); }
        finally { map.Unmap(); }
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
        entries.Clear(); removed.Clear(); gridCapacity = 0; gridsDynamic = false;
    }
}
