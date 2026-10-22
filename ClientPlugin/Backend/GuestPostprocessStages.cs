using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ClientPlugin.Shaders;

namespace ClientPlugin.Backend;

internal static class GuestPostprocessStages
{
    static long attemptedRevision=-1,attemptedEpoch=-1;
    static uint attemptedFormat,activeFormat;
    static bool preparedRestoresStock;
    static ulong prepared;
    static long preparedEpoch,preparedRevision;
    static string requested,preparedFingerprint,activeFingerprint;
    static uint[] touched=Array.Empty<uint>();
    internal static string Error {get;private set;}
    internal static bool PreparationBound;
    static object[][] catalog;
    static long catalogEpoch=-1;
    internal static object[] State(uint variant)=>GuestBridgeClient.ResourceEpoch==0||!GuestBridgeClient.SupportsPacketFlags(524288)?null:GuestBridgeClient.Call("PostprocessGenerationState",variant) as object[];
    internal static bool Prepare()
    {
        long epoch=GuestBridgeClient.ResourceEpoch,revision;PackSourceSnapshot[] sources;
        lock(GuestPassList.Gate){revision=GuestPassList.SourceRevision;sources=GuestPassList.SourceSnapshot().OrderBy(s=>s.Definition.Priority).ThenBy(s=>s.Definition.Id,StringComparer.OrdinalIgnoreCase).ThenBy(s=>s.Fingerprint,StringComparer.Ordinal).ToArray();}
        bool relevant=sources.Any(s=>s.Definition.Defines.Count!=0||s.Overlays.Any(o=>NativeKey(o.Key))||s.Injections.Any(i=>NativeStage(i.Stage)));
        if(!relevant&&touched.Length==0){Error=null;return true;}
        // Resolve ownership before demanding capabilities for inactive conflicting packs.
        var disabled=new HashSet<PackSourceSnapshot>();
        foreach(var claim in sources.SelectMany(s=>s.Definition.Exclusive.Distinct(StringComparer.OrdinalIgnoreCase).Select(stage=>new{Source=s,Stage=stage})).GroupBy(v=>v.Stage,StringComparer.OrdinalIgnoreCase))
            if(claim.Select(v=>v.Source).Distinct().Count()>1)foreach(var entry in claim)disabled.Add(entry.Source);
        sources=sources.Where(s=>!disabled.Contains(s)).ToArray();
        relevant=sources.Any(s=>s.Definition.Defines.Count!=0||s.Overlays.Any(o=>NativeKey(o.Key))||s.Injections.Any(i=>NativeStage(i.Stage)));
        if(!relevant&&touched.Length==0){Error=null;return true;}
        if(!PreparationBound||!GuestBridgeClient.SupportsPacketFlags(524288)){Error="Update coordinated Anomaly/SE-Vulkan: native postprocess generations require preparation callbacks and native bit19.";return false;}
        try
        {
            bool lighting=sources.Any(s=>s.Definition.Defines.Count!=0||s.Overlays.Any(o=>o.Key.StartsWith("Lighting/",StringComparison.OrdinalIgnoreCase))||s.Injections.Any(i=>i.Stage.StartsWith("Lighting",StringComparison.OrdinalIgnoreCase)));
            if(lighting&&!GuestBridgeClient.SupportsPacketFlags(1048576))throw new NotSupportedException("Update coordinated Anomaly/SE-Vulkan: lighting owner generations require native bit20.");
            bool atmosphere=sources.Any(s=>s.Definition.Defines.Count!=0||s.Overlays.Any(o=>o.Key.StartsWith("Transparent/Atmosphere/",StringComparison.OrdinalIgnoreCase))||s.Injections.Any(i=>string.Equals(i.Stage,ShaderStages.Atmosphere,StringComparison.OrdinalIgnoreCase)));
            if(atmosphere&&!GuestBridgeClient.SupportsPacketFlags(16777216))throw new NotSupportedException("Update coordinated Anomaly/SE-Vulkan: atmosphere owner generations require native bit24.");
            bool decals=sources.Any(s=>s.Definition.Defines.Count!=0||s.Overlays.Any(o=>o.Key.StartsWith("Decals/",StringComparison.OrdinalIgnoreCase))||s.Injections.Any(i=>string.Equals(i.Stage,ShaderStages.Decals,StringComparison.OrdinalIgnoreCase)));
            if(decals&&!GuestBridgeClient.SupportsPacketFlags(67108864))throw new NotSupportedException("Update coordinated Anomaly/SE-Vulkan: decal owner generations require native bit26.");
            bool foliage=sources.Any(s=>s.Definition.Defines.Count!=0||s.Overlays.Any(o=>o.Key.StartsWith("Foliage/",StringComparison.OrdinalIgnoreCase))||s.Injections.Any(i=>string.Equals(i.Stage,ShaderStages.Foliage,StringComparison.OrdinalIgnoreCase)));
            if(foliage&&!GuestBridgeClient.SupportsPacketFlags(33554432))throw new NotSupportedException("Update coordinated Anomaly/SE-Vulkan: foliage owner generations require native bit25.");
            var state=State(13);if(state==null)throw new InvalidOperationException("Postprocess generation service unavailable.");
            uint format=(uint)state[9];
            if(revision==attemptedRevision&&epoch==attemptedEpoch&&format==attemptedFormat)return Error==null;
            Discard();Error=null;
            if(catalogEpoch!=epoch||catalog==null||catalog.Length==0){catalog=GuestBridgeClient.Call("PostprocessCatalog") as object[][];catalogEpoch=epoch;}
            if(catalog==null||catalog.Length==0)return true; // Owner can initialize after CPU registration.
            attemptedRevision=revision;attemptedEpoch=epoch;attemptedFormat=format;
            string game=GuestCompiler.GameShaderDirectory();if(string.IsNullOrEmpty(game)||!Directory.Exists(game))throw new DirectoryNotFoundException("Game shader sources unavailable for authenticated native generation.");
            var files=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach(string file in Directory.GetFiles(game,"*",SearchOption.AllDirectories).Where(IsSource))files.Add(Relative(game,file),File.ReadAllBytes(file));
            var stock=files.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.OrdinalIgnoreCase);
            var claims=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
            var injections=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
            var defines=new Dictionary<string,string>(StringComparer.Ordinal);
            for(int i=0;i<sources.Length;i++)
            {
                var pack=sources[i];string prefix="_anomaly/packs/"+i+"/";
                foreach(string file in pack.Files.Where(IsSource))files.Add(prefix+file,RewritePackIncludes(pack,file,prefix));
                files.Add(prefix+"identity.hlsli",Encoding.UTF8.GetBytes("// "+pack.Fingerprint+"\n"));
                foreach(var overlay in pack.Overlays.Where(o=>NativeKey(o.Key)))
                {if(!claims.TryGetValue(overlay.Key,out var list))claims.Add(overlay.Key,list=new List<string>());list.Add(prefix+overlay.RelativePath);}
                foreach(var injection in pack.Injections.Where(v=>NativeStage(v.Stage)))
                {if(!injections.TryGetValue(injection.Stage,out var list))injections.Add(injection.Stage,list=new List<string>());list.Add(prefix+injection.RelativePath);}
                foreach(string raw in pack.Definition.Defines){string name=raw.Split('=')[0].Trim();if(Regex.IsMatch(name,@"^[A-Za-z_][A-Za-z0-9_]*$")&&!name.StartsWith("ANOMALY_ATTACH_",StringComparison.OrdinalIgnoreCase)&&!new[]{"ANOMALY","ANOMALY_VELOCITY","ANOMALY_PACK_GBUFFER1A","RENDERING_PASS","DEPTH_ONLY","CUSTOM_DEPTH"}.Contains(name,StringComparer.OrdinalIgnoreCase))defines[name]="1";}
            }
            // Headers are first-class overlays. Publish them at their canonical include key,
            // then rebuild the owning family (all families for an uncatalogued shared header).
            var headerFamilies=new HashSet<string>(StringComparer.OrdinalIgnoreCase);bool sharedPostHeader=false,sharedLightingHeader=false;
            var entryKeys=new HashSet<string>(catalog.Select(e=>(string)e[1]),StringComparer.OrdinalIgnoreCase);
            foreach(var claim in claims.Where(c=>c.Value.Count==1&&!entryKeys.Contains(c.Key)))
            {
                files[claim.Key]=Encoding.UTF8.GetBytes("#include \""+claim.Value[0]+"\"\n");
                if(ShaderStages.TryGetStageForKey(claim.Key,out var family))headerFamilies.Add(family);else if(claim.Key.StartsWith("Lighting/",StringComparison.OrdinalIgnoreCase))sharedLightingHeader=true;else sharedPostHeader=true;
            }
            var selected=new HashSet<uint>(touched);
            foreach(var entry in catalog)
            {
                uint id=(uint)entry[0];string key=(string)entry[1];
                if(!ShaderStages.TryGetStageForKey(key,out var stage))continue;
                bool overlay=claims.TryGetValue(key,out var owners)&&owners.Count==1;
                injections.TryGetValue(stage,out var stageInjects);
                injections.TryGetValue(ShaderStages.Lighting,out var familyInjects);
                var injects=(stage.StartsWith("Lighting.",StringComparison.OrdinalIgnoreCase)?(familyInjects??new List<string>()).Concat(stageInjects??new List<string>()):stageInjects??new List<string>()).ToArray();
                bool inject=injects.Length!=0;
                if(!overlay&&!inject&&defines.Count==0&&!(stage.StartsWith("Lighting.",StringComparison.OrdinalIgnoreCase)?sharedLightingHeader:sharedPostHeader)&&!headerFamilies.Contains(stage))continue;
                selected.Add(id);
                string extras=ShaderStages.ExtrasIncludePath(stage);
                files[extras]=Encoding.UTF8.GetBytes("#pragma once\n"+(inject?string.Concat(injects.Select(p=>"#include \""+p+"\"\n")):""));
                string original="_anomaly/stock/"+key;
                if(!files.ContainsKey(original))files.Add(original,RewriteStockIncludes(key,stock[key],stock));
                files[key]=Encoding.UTF8.GetBytes("#include \""+extras+"\"\n#include \""+(overlay?owners[0]:original)+"\"\n");
            }
            // Foliage shares one VS across both GS/PS pairs; preserve its complete dependency closure.
            if(selected.Any(id=>id>=768&&id<773))foreach(uint id in new uint[]{768,769,770,771,772})selected.Add(id);
            foreach(uint id in selected.Where(id=>id>=1024&&id<1034).ToArray()){selected.Add(id&~1u);selected.Add(id|1u);}
            // Include the negative-border HBAO companions in the same owner generation.
            foreach(uint id in selected.ToArray())if(id>=36&&id<=51)selected.Add(id+92);
            if(selected.Count==0)return true;
            bool restoresStock=!claims.Any(c=>c.Value.Count==1)&&injections.Count==0&&defines.Count==0;
            // COMPOSITE belongs to owned camera-velocity variants, not native named stages.
            defines["ANOMALY"]="1";defines["ANOMALY_SPIRV"]="1";
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))
            {
                writer.Write("AnomalyNativeOwners-2");foreach(var file in files.OrderBy(p=>p.Key,StringComparer.Ordinal)){writer.Write(file.Key);writer.Write(file.Value.Length);writer.Write(file.Value);}
                foreach(var define in defines.OrderBy(p=>p.Key,StringComparer.Ordinal)){writer.Write(define.Key);writer.Write(define.Value);}foreach(uint id in selected.OrderBy(v=>v))writer.Write(id);
                writer.Flush();using(var sha=SHA256.Create())requested=BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-","").ToLowerInvariant();
            }
            if(activeFingerprint==requested&&preparedEpoch==epoch&&activeFormat==format){if(restoresStock)touched=Array.Empty<uint>();return true;}
            var result=GuestBridgeClient.Call("CompilePostprocessGeneration",requested,files,defines,selected.OrderBy(v=>v).ToArray()) as object[];
            if(result==null)throw new InvalidOperationException("Native postprocess compiler service unavailable.");
            preparedRestoresStock=restoresStock;
            prepared=(ulong)result[0];preparedFingerprint=(string)result[1];preparedEpoch=epoch;preparedRevision=revision;touched=selected.OrderBy(v=>v).ToArray();return true;
        }
        catch(Exception error){Error=error.GetBaseException().Message;Discard();return false;}
    }
    internal static bool Activate()
    {
        if(prepared==0)return Error==null;
        if(preparedRevision!=GuestPassList.SourceRevision||preparedEpoch!=GuestBridgeClient.ResourceEpoch){Error="Stale native postprocess source/device generation.";Discard();return false;}
        try
        {
            ulong token=prepared;long revision=preparedRevision,epoch=preparedEpoch;
            GuestBridgeClient.Call("QueuePostprocessGeneration",token,(Func<bool>)(()=>GuestPassList.SourceRevision==revision&&GuestBridgeClient.ResourceEpoch==epoch));
            bool applied=GuestBridgeClient.Call("ApplyPostprocessGeneration") is true;
            if(!applied){Error="Native postprocess generation activation refused: "+(State(13)?[7] as string);prepared=0;attemptedRevision=-1;return false;}
            GuestStageBindings.Activated();
            prepared=0;activeFingerprint=requested;activeFormat=attemptedFormat;if(preparedRestoresStock)touched=Array.Empty<uint>();Error=null;return true;
        }
        catch(Exception error){Error=error.GetBaseException().Message;Discard();return false;}
    }
    internal static void Discard()
    {if(prepared!=0)attemptedRevision=-1;if(prepared!=0&&preparedEpoch==GuestBridgeClient.ResourceEpoch)try{GuestBridgeClient.Call("DiscardPostprocessGeneration",prepared);}catch{}prepared=0;preparedFingerprint=null;}
    internal static NativeStageGenerationState Query(string stage)
    {
        var variants=new List<NativeStageVariantState>();
        if(catalog!=null)foreach(var entry in catalog)if(ShaderStages.TryGetStageForKey((string)entry[1],out var name)&&(string.Equals(name,stage,StringComparison.OrdinalIgnoreCase)||string.Equals(stage,ShaderStages.Lighting,StringComparison.OrdinalIgnoreCase)&&name.StartsWith("Lighting.",StringComparison.OrdinalIgnoreCase)))
        {
            var data=State((uint)entry[0]);if(data==null)continue;
            variants.Add(new NativeStageVariantState{Variant=(uint)entry[0],Source=(string)entry[1],Profile=entry.Length>2?entry[2] as string:null,ActiveToken=(ulong)data[0],Executions=(ulong)data[1],LastSubmissionCompleted=(ulong)data[1]!=0&&(ulong)data[2]<=(ulong)data[3],LastProducedFrame=(uint)data[4],ActiveGeneration=data[6] as string});
        }
        return new NativeStageGenerationState{Stage=stage,RequestedGeneration=requested,PreparedGeneration=prepared==0?null:preparedFingerprint,Error=Error,Variants=variants.AsReadOnly()};
    }
    static byte[] RewriteStockIncludes(string key,byte[] bytes,Dictionary<string,byte[]> stock)
    {
        string text=Encoding.UTF8.GetString(bytes);
        return Encoding.UTF8.GetBytes(Regex.Replace(text,"(?m)(#\\s*include\\s*)[\"<]([^\">]+)[\">]",match=>{
            string include=match.Groups[2].Value.Replace('\\','/');
            string folder=Path.GetDirectoryName(key).Replace('\\','/');
            foreach(string candidate in new[]{folder+"/"+include,include})
            {
                string normalized;try{normalized=Relative(Path.GetTempPath(),PackDefinition.ResolveFile(Path.GetTempPath(),candidate));}catch{continue;}
                string found=stock.Keys.FirstOrDefault(p=>string.Equals(p,normalized,StringComparison.OrdinalIgnoreCase));
                if(found!=null)return match.Groups[1].Value+"\""+found+"\"";
            }
            return match.Value;
        }));
    }
    static byte[] RewritePackIncludes(PackSourceSnapshot pack,string file,string prefix)
    {
        string content=Encoding.UTF8.GetString(pack.Bytes(file));
        return Encoding.UTF8.GetBytes(Regex.Replace(content,"(?m)(#\\s*include\\s*)[\"<]([^\">]+)[\">]",match=>{
            string include=match.Groups[2].Value.Replace('\\','/');
            foreach(string candidate in new[]{Path.GetDirectoryName(file).Replace('\\','/')+"/"+include,"Inject/"+include,include})
            {
                string normalized;try{normalized=Relative(pack.Root,PackDefinition.ResolveFile(pack.Root,candidate));}catch{continue;}
                string found=pack.Files.FirstOrDefault(p=>string.Equals(p,normalized,StringComparison.OrdinalIgnoreCase));
                if(found!=null)return match.Groups[1].Value+"\""+prefix+found+"\"";
            }
            return match.Value;
        }));
    }
    static bool NativeKey(string key)=>key.StartsWith("Postprocess/",StringComparison.OrdinalIgnoreCase)||key.StartsWith("Lighting/",StringComparison.OrdinalIgnoreCase)||key.StartsWith("Transparent/Atmosphere/",StringComparison.OrdinalIgnoreCase)||key.StartsWith("Foliage/",StringComparison.OrdinalIgnoreCase)||key.StartsWith("Decals/",StringComparison.OrdinalIgnoreCase);
    static bool NativeStage(string stage)=>stage.StartsWith("Post.",StringComparison.OrdinalIgnoreCase)||stage.StartsWith("Lighting",StringComparison.OrdinalIgnoreCase)||string.Equals(stage,ShaderStages.Atmosphere,StringComparison.OrdinalIgnoreCase)||string.Equals(stage,ShaderStages.Foliage,StringComparison.OrdinalIgnoreCase)||string.Equals(stage,ShaderStages.Decals,StringComparison.OrdinalIgnoreCase);
    static bool IsSource(string path)=>new[]{".hlsl",".hlsli",".h",".inl",".inc"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase);
    static string Relative(string root,string path)=>Path.GetFullPath(path).Substring(Path.GetFullPath(root).TrimEnd('\\','/').Length+1).Replace('\\','/');
}
public sealed class NativeStageGenerationState
{
    public string Stage {get;internal set;}
    public string RequestedGeneration {get;internal set;}
    public string PreparedGeneration {get;internal set;}
    public string Error {get;internal set;}
    public IReadOnlyList<NativeStageVariantState> Variants {get;internal set;}
}
public sealed class NativeStageVariantState
{
    public uint Variant {get;internal set;}
    public string Source {get;internal set;}
    public string Profile {get;internal set;}
    public ulong ActiveToken {get;internal set;}
    public string ActiveGeneration {get;internal set;}
    public ulong Executions {get;internal set;}
    public bool LastSubmissionCompleted {get;internal set;}
    public uint LastProducedFrame {get;internal set;}
}
