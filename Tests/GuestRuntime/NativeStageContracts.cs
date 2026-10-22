using System;
using System.IO;
using ClientPlugin.Backend;
using ClientPlugin.Rendering;

static partial class Program
{
    static void NativeStageContracts()
    {
        int features=GuestBridge.SupportedPacketFlags;long epoch=GuestBridge.TestResourceEpoch;bool ready=GuestBridge.Ready,bound=GuestPostprocessStages.PreparationBound;
        string root=Path.Combine(Path.GetTempPath(),"anomaly-native-stage-contract-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            GuestBridge.Ready=true;GuestBridge.TestResourceEpoch=1;GuestBridge.SupportedPacketFlags=(features|524288)&~1048576;GuestPostprocessStages.PreparationBound=true;
            foreach(string kind in new[]{"overlay","family-injection","global-define"})
            {
                string folder=Path.Combine(root,kind);Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder,"anomaly.json"),kind=="global-define"?"{\"id\":\"fixture.old.native\",\"defines\":[\"PACK_NATIVE_LIGHT=1\"]}":"{\"id\":\"fixture.old.native\"}");
                string relative=kind=="overlay"?"Overlay/Lighting.Dir/LightDir.hlsl":"Inject/Lighting/Family.hlsli";
                if(kind!="global-define"){string source=Path.Combine(folder,relative);Directory.CreateDirectory(Path.GetDirectoryName(source));File.WriteAllText(source,kind=="overlay"?"float4 __pixel_shader():SV_Target{return 1;}":"float NativeHelper(){return 1;}");}
                GuestPassList.RegisterRoot("fixture.old.native",folder);
                Require(!GuestPostprocessStages.Prepare()&&GuestPostprocessStages.Error.Contains("bit20"),"Older native host did not actionably refuse "+kind+" before compilation/device assets");
                Require(GuestPostprocessStages.Query("Lighting.Dir").PreparedGeneration==null,"Older host prepared a lighting generation");
                GuestPassList.UnregisterRoot("fixture.old.native");
            }
            foreach(string id in new[]{"fixture.old.conflict-a","fixture.old.conflict-b"})
            {
                string folder=Path.Combine(root,id);Directory.CreateDirectory(Path.Combine(folder,"Overlay/Lighting.Dir"));
                File.WriteAllText(Path.Combine(folder,"anomaly.json"),"{\"id\":\""+id+"\",\"exclusive\":[\"Lighting\"]}");
                File.WriteAllText(Path.Combine(folder,"Overlay/Lighting.Dir/LightDir.hlsl"),"float4 __pixel_shader():SV_Target{return 1;}");GuestPassList.RegisterRoot(id,folder);
            }
            Require(GuestPostprocessStages.Prepare()&&GuestPostprocessStages.Error==null,"Inactive conflicting lighting packs demanded unsupported native execution");
            GuestPassList.UnregisterRoot("fixture.old.conflict-a");GuestPassList.UnregisterRoot("fixture.old.conflict-b");
            Console.WriteLine("PASS: older-host bit20 refusal for lighting overlays, family injection and global defines before shader assets/device work; inactive conflicting owners preserve stock");
        }
        finally
        {
            GuestPassList.UnregisterRoot("fixture.old.native");GuestPassList.UnregisterRoot("fixture.old.conflict-a");GuestPassList.UnregisterRoot("fixture.old.conflict-b");GuestBridge.SupportedPacketFlags=features;GuestBridge.TestResourceEpoch=epoch;GuestBridge.Ready=ready;GuestPostprocessStages.PreparationBound=bound;
            if(Path.GetDirectoryName(root)==Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar))Directory.Delete(root,true);
        }
    }
}
