using System;
using System.Linq;
using System.Runtime.InteropServices;
using SharpDX.Direct3D11;
using VRage.Render11.Common;
using VRage.Render11.Resources;
using VRage.Render11.RenderContext;
using VRageRender;
using VRageMath;

namespace ClientPlugin.ShaderFramework;

/// <summary>Device ownership over the shared sealed-room buffer layout.</summary>
internal sealed class VolumetricInteriorUpload : IDisposable
{
    internal ISrvBuffer Grids,Cells;
    readonly VolumeInteriorData data=new();
    int gridCapacity;
    bool gridsDynamic;
    long uploadedRevision=-1;
    internal long Revision=>data.Revision;
    internal uint GridCount=>data.GridCount;
    internal bool Complete=>data.Complete;
    internal string Status=>data.Status;
    internal void Upload(VolumetricInteriorSnapshot.Snapshot snapshot,Vector3D camera,MyRenderContext rc=null)
    {
        data.Build(snapshot.Grids.Select(grid=>new VolumeInteriorData.Grid(grid.Id,grid.Generation,grid.WorldToLocal,grid.CellSize,grid.Cells,grid.IsCurrent)),
            snapshot.Grids.Length,snapshot.IsFresh,snapshot.Complete,snapshot.Status,camera);
        try
        {
            WriteGrids(rc,data.RowCount);
            if(Cells==null || uploadedRevision!=data.Revision)
            {
                if(Cells!=null) MyManagers.Buffers.Dispose(Cells);
                Cells=null;Cells=Create("Anomaly.VolumeInteriorCells",data.Cells);uploadedRevision=data.Revision;
            }
        }
        catch { Dispose();throw; }
    }
    void WriteGrids(MyRenderContext rc,int count)
    {
        if(rc==null)
        {
            if(Grids!=null) MyManagers.Buffers.Dispose(Grids);
            var exact=new Vector4[count];Array.Copy(data.Rows,exact,count);
            Grids=Create("Anomaly.VolumeInteriorGrids",exact);gridCapacity=count;gridsDynamic=false;return;
        }
        if(Grids==null || !gridsDynamic || gridCapacity<count)
        {
            if(Grids!=null) MyManagers.Buffers.Dispose(Grids);
            Grids=null;gridCapacity=data.Rows.Length;
            Grids=MyManagers.Buffers.CreateSrv("Anomaly.VolumeInteriorGrids",gridCapacity,16,usage:ResourceUsage.Dynamic);gridsDynamic=true;
        }
        var map=MyMapping.MapDiscard(rc,Grids);
        try { for(int i=0;i<count;i++) { var row=data.Rows[i];map.WriteAndPosition(ref row); } }
        finally { map.Unmap(); }
    }
    static ISrvBuffer Create<T>(string name,T[] values) where T:struct
    {
        var pin=GCHandle.Alloc(values,GCHandleType.Pinned);
        try { return MyManagers.Buffers.CreateSrv(name,values.Length,16,pin.AddrOfPinnedObject(),ResourceUsage.Immutable); }
        finally { pin.Free(); }
    }
    public void Dispose()
    {
        if(Grids!=null) MyManagers.Buffers.Dispose(Grids);
        if(Cells!=null) MyManagers.Buffers.Dispose(Cells);
        Grids=Cells=null;data.Clear();gridCapacity=0;gridsDynamic=false;uploadedRevision=-1;
    }
}