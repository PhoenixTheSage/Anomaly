using System;
using System.Collections.Generic;
using VRageMath;

namespace ClientPlugin.ShaderFramework;

/// <summary>Shared CPU layout for the t23/t24 sealed-room buffers; no device resources.</summary>
internal sealed class VolumeInteriorData
{
    internal readonly struct Grid
    {
        internal readonly long Id,Generation;
        internal readonly MatrixD WorldToLocal;
        internal readonly float CellSize;
        internal readonly Vector3I[] Cells;
        internal readonly bool Current;
        internal Grid(long id,long generation,MatrixD worldToLocal,float cellSize,Vector3I[] cells,bool current)
        { Id=id;Generation=generation;WorldToLocal=worldToLocal;CellSize=cellSize;Cells=cells;Current=current; }
    }
    sealed class Entry
    {
        internal long Generation;
        internal Vector3I[] Original;
        internal Vector4I[] Cells;
        internal Vector3I Min,Max;
        internal bool Seen;
    }
    readonly Dictionary<long,Entry> cache=new();
    readonly List<Entry> entries=new();
    readonly List<long> removed=new();
    Entry[] packed=Array.Empty<Entry>();
    Vector4[] rows=new Vector4[7];
    internal Vector4[] Rows=>rows;
    internal int RowCount {get;private set;}=1;
    internal Vector4I[] Cells {get;private set;}=new Vector4I[1];
    internal uint GridCount {get;private set;}
    internal long Revision {get;private set;}
    internal bool Complete {get;private set;}
    internal string Status {get;private set;}="Not packed";
    bool initialized;

    internal void Build(IEnumerable<Grid> grids,int count,bool fresh,bool complete,string status,Vector3D camera)
    {
        GridCount=0;entries.Clear();
        foreach(var entry in cache.Values) entry.Seen=false;
        Complete=fresh && complete;
        Status=fresh?status:"Degraded interior exclusion: stale simulation snapshot";
        int rowCount=0,cellCount=0;
        if(rows.Length<Math.Max(1,count*7)) Array.Resize(ref rows,Math.Max(rows.Length*2,count*7));
        if(fresh)
        foreach(var grid in grids)
        {
            if(!grid.Current) { Complete=false;Status="Degraded interior exclusion: topology changed";continue; }
            if(!cache.TryGetValue(grid.Id,out var entry) || entry.Generation!=grid.Generation || !ReferenceEquals(entry.Original,grid.Cells))
            {
                var sorted=(Vector3I[])grid.Cells.Clone();
                Array.Sort(sorted,(a,b)=>a.X!=b.X?a.X.CompareTo(b.X):a.Y!=b.Y?a.Y.CompareTo(b.Y):a.Z.CompareTo(b.Z));
                var cells=new Vector4I[sorted.Length];
                var min=sorted.Length==0?Vector3I.Zero:sorted[0];var max=min;
                for(int i=0;i<sorted.Length;i++)
                {
                    var cell=sorted[i];min=Vector3I.Min(min,cell);max=Vector3I.Max(max,cell);
                    cells[i]=new Vector4I(cell.X,cell.Y,cell.Z,0);
                }
                entry=new Entry{Generation=grid.Generation,Original=grid.Cells,Cells=cells,Min=min,Max=max};
                cache[grid.Id]=entry;
            }
            entry.Seen=true;
            if(entry.Cells.Length==0) continue;
            entries.Add(entry);
            var transform=MatrixD.CreateTranslation(camera)*grid.WorldToLocal;
            rows[rowCount++]=new Vector4((float)transform.M11,(float)transform.M12,(float)transform.M13,(float)transform.M14);
            rows[rowCount++]=new Vector4((float)transform.M21,(float)transform.M22,(float)transform.M23,(float)transform.M24);
            rows[rowCount++]=new Vector4((float)transform.M31,(float)transform.M32,(float)transform.M33,(float)transform.M34);
            rows[rowCount++]=new Vector4((float)transform.M41,(float)transform.M42,(float)transform.M43,(float)transform.M44);
            rows[rowCount++]=new Vector4(grid.CellSize,cellCount,entry.Cells.Length,1);
            rows[rowCount++]=new Vector4(entry.Min.X,entry.Min.Y,entry.Min.Z,0);
            rows[rowCount++]=new Vector4(entry.Max.X,entry.Max.Y,entry.Max.Z,0);
            cellCount+=entry.Cells.Length;GridCount++;
        }
        if(rowCount==0) { rows[0]=Vector4.Zero;rowCount=1; }
        RowCount=rowCount;
        removed.Clear();
        foreach(var pair in cache) if(!pair.Value.Seen) removed.Add(pair.Key);
        foreach(var id in removed) cache.Remove(id);
        bool changed=!initialized || packed.Length!=entries.Count;
        for(int i=0;!changed && i<packed.Length;i++) changed=!ReferenceEquals(packed[i],entries[i]);
        if(changed)
        {
            var cells=new Vector4I[Math.Max(1,cellCount)];int offset=0;
            foreach(var entry in entries) { Array.Copy(entry.Cells,0,cells,offset,entry.Cells.Length);offset+=entry.Cells.Length; }
            Cells=cells;packed=entries.ToArray();Revision++;initialized=true;
        }
    }
    internal void Clear()
    {
        cache.Clear();entries.Clear();removed.Clear();packed=Array.Empty<Entry>();initialized=false;
        GridCount=0;Complete=false;RowCount=1;rows[0]=Vector4.Zero;Cells=new Vector4I[1];
    }
}
