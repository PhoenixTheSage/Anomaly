using System;
using System.Collections.Generic;
using System.Linq;
using ClientPlugin.Contracts;
using ClientPlugin.Shaders;

namespace ClientPlugin.Backend;

internal static class GuestStageBindings
{
    internal const int Feature = 4194304;
    internal static readonly StageBindingModel Model = new();
    internal sealed class Owner
    {
        internal readonly uint Id, Mask;
        internal readonly string Stage;
        internal readonly bool Extras;
        internal Owner(uint id,string stage,uint mask,bool extras){Id=id;Stage=stage;Mask=mask;Extras=extras;}
    }
    internal sealed class FrameBindings
    {
        internal readonly uint Frame;
        internal readonly long Epoch;
        internal readonly IReadOnlyList<StageBindingModel.Request> Requests;
        internal readonly Owner[] Owners;
        internal readonly Tuple<string,int>[] Attachments;
        internal readonly string[] Histories;
        internal readonly ulong PreviousColor;
        internal FrameBindings(uint frame,long epoch,IReadOnlyList<StageBindingModel.Request> requests,Owner[] owners,
            Tuple<string,int>[] attachments,string[] histories,ulong previousColor)
        {Frame=frame;Epoch=epoch;Requests=requests;Owners=owners;Attachments=attachments;Histories=histories;PreviousColor=previousColor;}
    }
    static Owner[] owners=Array.Empty<Owner>();
    static string status="Vulkan native stage binding execution unavailable; reservations retained";
    internal static string Status=>GuestBridgeClient.SupportsPacketFlags(Feature)?status:
        "Update SE-Vulkan: bit22 native stage binding execution unavailable; reservations retained";
    internal static void Reset(){owners=Array.Empty<Owner>();status="waiting for native owner activation";}
    internal static void Activated()
    {
        owners=Array.Empty<Owner>();
        if(!GuestBridgeClient.SupportsPacketFlags(Feature))return;
        var rows=GuestBridgeClient.Call("StageBindingOwners") as object[][];
        if(rows==null){status="Native stage binding owner service unavailable";return;}
        var next=new List<Owner>();
        foreach(var row in rows)
            if(ShaderStages.TryGetStageForKey((string)row[1],out var stage))
                next.Add(new Owner((uint)row[0],stage,(uint)row[2],(bool)row[3]));
        owners=next.ToArray();status=owners.Length==0?"no active native catalog/extras consumers":"native owner resources awaiting a frame";
    }
    internal static FrameBindings Freeze(IReadOnlyList<GuestProgram> programs)
    {
        var active=owners;
        if(active.Length==0||programs==null||!GuestFrame.Open)return null;
        var requests=Model.Snapshot();
        bool lighting=active.Any(o=>o.Stage.StartsWith("Lighting",StringComparison.OrdinalIgnoreCase));
        var attachments=lighting?GuestPassList.SnapshotAttachments().Where(a=>a.Slot>=0&&a.Slot<3)
            .Select(a=>Tuple.Create(a.Name,a.Slot)).ToArray():Array.Empty<Tuple<string,int>>();
        // A restored stock owner with an ordinary engine t5 input needs no packet
        // unless a matching catalog reservation actually requests that slot.
        if(!active.Any(o=>o.Extras||requests.Any(r=>StageBindingModel.Matches(r.Stage,o.Stage)&&(o.Mask&(1u<<r.Slot))!=0)||
            lighting&&attachments.Length!=0&&(o.Mask&0x3c0u)!=0))return null;
        ulong previousColor=requests.Any(r=>string.Equals(r.CatalogName,"historyColor",StringComparison.OrdinalIgnoreCase))
            ?GuestBridgeClient.PreviousColor(programs):0;
        return new FrameBindings(GuestFrame.U32(16),GuestBridgeClient.ResourceEpoch,requests,active,attachments,
            programs.Where(p=>p!=null&&!string.IsNullOrEmpty(p.HistoryName)).Select(p=>p.HistoryName).ToArray(),previousColor);
    }
    internal static void Submit(FrameBindings frame)
    {
        if(frame==null)return;
        try
        {
            if(frame.Epoch!=GuestBridgeClient.ResourceEpoch||!GuestFrame.Open||frame.Frame!=GuestFrame.U32(16))throw new InvalidOperationException("Stale native stage binding frame");
            var ids=new List<uint>();var images=new List<ulong>();var requested=new List<uint>();var fresh=new List<uint>();var attachments=new List<uint>();
            ulong velocity=GuestBridgeClient.Catalog("velocity");
            ulong Resolve(string name)=>string.Equals(name,"historyColor",StringComparison.OrdinalIgnoreCase)?frame.PreviousColor:GuestBridgeClient.Catalog(name);
            bool Fresh(string name)=>ClientPlugin.Buffers.BufferCatalog.Active(name) is ClientPlugin.Buffers.GuestRendererImage image&&image.RequiresCurrentFrame&&
                !string.Equals(name,"historyColor",StringComparison.OrdinalIgnoreCase)&&
                !frame.Histories.Contains(name,StringComparer.OrdinalIgnoreCase);
            uint velocityFresh=Fresh("velocity")?1u:0u;
            foreach(var owner in frame.Owners)
            {
                var row=new ulong[10];uint selected=0,needFresh=velocityFresh,attach=0;
                if(owner.Stage.StartsWith("Lighting",StringComparison.OrdinalIgnoreCase))
                    foreach(var a in frame.Attachments)
                    {
                        int slot=6+a.Item2;ulong image=GuestBridgeClient.Catalog(a.Item1);
                        if(image==0)continue;row[slot]=image;selected|=1u<<slot;attach|=1u<<slot;
                        if(Fresh(a.Item1))needFresh|=1u<<slot;
                    }
                foreach(var request in frame.Requests)
                {
                    uint bit=1u<<request.Slot;
                    if((selected&bit)!=0||(owner.Mask&bit)==0||!StageBindingModel.Matches(request.Stage,owner.Stage))continue;
                    selected|=bit;row[request.Slot]=Resolve(request.CatalogName);
                    if(Fresh(request.CatalogName))needFresh|=bit;
                }
                if(!owner.Extras&&selected==0)continue;
                ids.Add(owner.Id);images.AddRange(row);requested.Add(selected);fresh.Add(needFresh);attachments.Add(attach);
            }
            if(GuestBridgeClient.Call("SetStageBindings",frame.Frame,ids.ToArray(),images.ToArray(),requested.ToArray(),fresh.ToArray(),attachments.ToArray(),velocity) is not true)
                throw new InvalidOperationException("Native stage binding transport unavailable");
            status="frame "+frame.Frame+": "+ids.Count+" owner reservation rows queued; native stage status reports execution";
        }
        catch(Exception error){status="Native stage bindings refused: "+error.GetBaseException().Message;}
    }
    internal static object[] State()=>GuestBridgeClient.SupportsPacketFlags(Feature)&&GuestBridgeClient.ResourceEpoch!=0?
        GuestBridgeClient.Call("StageBindingState") as object[]:null;
    internal static void BindDx11()
    {
        foreach(var request in Model.Snapshot())
            Dx11Call.Try("ClientPlugin.Shaders.ShaderBindRegistry","RequestSrv",new object[]{request.Stage,request.CatalogName,request.Slot},out _);
    }
}
