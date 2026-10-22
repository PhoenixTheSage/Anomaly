using System;
using System.Linq;
using ClientPlugin.Contracts;
using ClientPlugin.Backend;
using ClientPlugin.Rendering;

static partial class Program
{
    static void StageBindingContracts()
    {
        var model=new StageBindingModel();
        Require(model.Snapshot().Count==5,"Lost default velocity reservations");
        Require(model.Add("Lighting","first")&&model.Add("Lighting","second"),"Cannot register reserved textures");
        Require(model.Add("Lighting","first")&&model.Snapshot().Single(r=>r.CatalogName=="first").Slot==6,"Repeated automatic registration moved an existing slot");
        var frozen=model.Snapshot();
        Require(frozen.Single(r=>r.CatalogName=="first").Slot==6&&frozen.Single(r=>r.CatalogName=="second").Slot==7,"Wrong automatic lighting slots");
        Require(!model.Add("Lighting","first",7),"Moving a request silently overwrote an occupied slot");
        Require(model.Snapshot().Single(r=>r.CatalogName=="first").Slot==6,"Refused move mutated the request");
        Require(model.Add("Lighting","first",8)&&frozen.Single(r=>r.CatalogName=="first").Slot==6,"Frame snapshot changed after registration");
        Require(model.Add("Atmosphere","density")&&model.Snapshot().Single(r=>r.CatalogName=="density").Slot==7,"Atmosphere overwrote engine density/velocity");
        Require(model.Add("Post.HBAO","velocity",9)&&model.Snapshot().Single(r=>r.Stage=="Post.HBAO").Slot==5,"Velocity reservation moved");
        Require(model.Add("Post.Test","a")&&model.Add("Post.Test","b")&&model.Add("Post.Test","c")&&model.Add("Post.Test","d")&&!model.Add("Post.Test","e"),"Reserved slot exhaustion failed");
        Require(StageBindingModel.Matches("Lighting","Lighting.Dir")&&!StageBindingModel.Matches("Lighting","Post.Tonemap"),"Lighting family matching leaked");
        int features=GuestBridge.SupportedPacketFlags;
        try
        {
            Require(GuestStageBindings.Status.Contains("execution unavailable"),"Stored bindings falsely advertised native execution");
            GuestBridge.Ready=true;SceneView.Width=64;SceneView.Height=64;
            var programs=new[]{"CameraVelocity","LinearDepth","HiZ","ReactiveClear","HistoryCopy"}.Select((name,index)=>new GuestProgram{Id="anomaly.owned."+name,Mark=1,Compose="PublishOnly",Pipeline=(ulong)(800+index)}).ToArray();
            GuestBridge.SupportedPacketFlags=features&~2097152;
            GuestBridgeClient.Submit(programs);
            Require(GuestBridge.Packets.All(p=>(p.Flags&4096)==0),"Older host received unsupported early-product flag");
            GuestBridge.SupportedPacketFlags=features|2097152;
            GuestBridgeClient.Submit(programs);
            Require(GuestBridge.Packets.Count(p=>(p.Flags&4096)!=0)==4&&GuestBridge.Packets.Single(p=>p.Pipeline==804).Flags==0,"Internal producer selection changed HistoryCopy/public slot order");
        }
        finally{GuestBridge.SupportedPacketFlags=features;}
        Console.WriteLine("PASS: shared reservations, immutable frame requests, transactional conflict rollback, slot exhaustion and honest native binding status/early-product gating");
    }
}
