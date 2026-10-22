using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using ClientPlugin.Backend;
using ClientPlugin.Rendering;

static partial class Program
{
    static void NativeStageContracts()
    {
        var files=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        void Source(string key,string value)=>files[key]=Encoding.UTF8.GetBytes(value);
        var header=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"Shadows/Shared.hlsli"};
        object[] entry={256u,"Lighting/Main.hlsl","ps_5_0",new[]{"Lighting/Main.hlsl"}};
        Source("Lighting/Main.hlsl","#include<Shadows/Shared.hlsli>");Source("Shadows/Shared.hlsli","float Shared(){return 1;}");
        Require(GuestPostprocessStages.AffectedByHeaders(entry,files,"Lighting/Main.hlsl",header),"Cross-family include was not selected");
        Source("Lighting/Main.hlsl","/*before*/#include \"../Common.hlsli\"");
        // Continue an include argument, preserving ordinary preprocessor tokens.
        Source("Common.hlsli","#include \\\n<Shadows/Shared.hlsli>");
        Require(GuestPostprocessStages.AffectedByHeaders(entry,files,"Lighting/Main.hlsl",header),"Relative/transitive/continued include was not selected");
        Source("Lighting/Main.hlsl","#include \"_anomaly/packs/0/Replacement.hlsl\"");Source("_anomaly/packs/0/Replacement.hlsl","#include <Shadows/Shared.hlsli>");
        Require(GuestPostprocessStages.AffectedByHeaders(entry,files,"Lighting/Main.hlsl",header),"Pack-introduced dependency was not selected");
        Source("Lighting/Main.hlsl","#include SOME_HEADER");Require(GuestPostprocessStages.AffectedByHeaders(entry,files,"Lighting/Main.hlsl",header),"Macro include failed to choose conservative selection");
        Source("Lighting/Main.hlsl","// #include <Shadows/Shared.hlsli>\n/* #include <Shadows/Shared.hlsli> */\nfloat main(){return 1;}");
        Require(!GuestPostprocessStages.AffectedByHeaders(entry,files,"Lighting/Main.hlsl",header),"Comment-only dependency selected an unrelated owner");
        Require(GuestPostprocessStages.AffectedByHeaders(new object[]{256u,"Lighting/Main.hlsl","ps_5_0",new[]{"Shadows/Shared.hlsli"}},files,"Lighting/Main.hlsl",header),"Authenticated dependency fallback lost an active include");
        Require(GuestPostprocessStages.AffectedByHeaders(new object[]{256u,"Lighting/Main.hlsl","ps_5_0"},files,"Lighting/Main.hlsl",header),"Older catalog silently ignored a shared header");
        Source("Lighting/Main.hlsl","#include <Common.hlsli>");Source("Common.hlsli","#include <Lighting/Main.hlsl>");Require(!GuestPostprocessStages.AffectedByHeaders(entry,files,"Lighting/Main.hlsl",header),"Include cycle incorrectly selected an unrelated header");
        header.Clear();header.Add("Lighting/Aux.hlsl");Source("Lighting/Main.hlsl","#include <Lighting/Aux.hlsl>");Source("Lighting/Aux.hlsl","float Helper(){return 1;}");Require(GuestPostprocessStages.AffectedByHeaders(entry,files,"Lighting/Main.hlsl",header),"An entry-point source included by another owner lost its dependency");
        Console.WriteLine("PASS: shared native dependency selection across families, pack sources, relative/transitive/continued/macro includes, cycles, comments and older-host fallback");
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
