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
            bool foliage=sources.Any(s=>s.Definition.Defines.Count!=0||s.Overlays.Any(o=>o.Key.StartsWith("Foliage/",StringComparison.OrdinalIgnoreCase))||s.Injections.Any(i=>string.Equals(i.Stage,ShaderStages.Foliage,StringComparison.OrdinalIgnoreCase)));
            if(foliage&&!GuestBridgeClient.SupportsPacketFlags(33554432))throw new NotSupportedException("Update coordinated Anomaly/SE-Vulkan: foliage owner generations require native bit25.");
            bool decals=sources.Any(s=>s.Definition.Defines.Count!=0||s.Overlays.Any(o=>o.Key.StartsWith("Decals/",StringComparison.OrdinalIgnoreCase))||s.Injections.Any(i=>string.Equals(i.Stage,ShaderStages.Decals,StringComparison.OrdinalIgnoreCase)));
            if(decals&&!GuestBridgeClient.SupportsPacketFlags(67108864))throw new NotSupportedException("Update coordinated Anomaly/SE-Vulkan: decal owner generations require native bit26.");
            bool probes=sources.Any(s=>s.Definition.Defines.Count!=0||s.Overlays.Any(o=>o.Key.StartsWith("EnvProbe/",StringComparison.OrdinalIgnoreCase))||s.Injections.Any(i=>string.Equals(i.Stage,ShaderStages.EnvProbe,StringComparison.OrdinalIgnoreCase)));
            if(probes&&!GuestBridgeClient.SupportsPacketFlags(134217728))throw new NotSupportedException("Update coordinated Anomaly/SE-Vulkan: probe processing generations require native bit27.");
            bool particles=sources.Any(s=>s.Definition.Defines.Count!=0||s.Overlays.Any(o=>o.Key.StartsWith("Transparent/GPUParticles/",StringComparison.OrdinalIgnoreCase))||s.Injections.Any(i=>string.Equals(i.Stage,ShaderStages.GpuParticles,StringComparison.OrdinalIgnoreCase)));
            if(particles&&!GuestBridgeClient.SupportsPacketFlags(268435456))throw new NotSupportedException("Update coordinated Anomaly/SE-Vulkan: particle owner generations require native bit28.");
            bool shadows=sources.Any(s=>s.Definition.Defines.Count!=0||s.Overlays.Any(o=>o.Key.StartsWith("Shadows/",StringComparison.OrdinalIgnoreCase))||s.Injections.Any(i=>string.Equals(i.Stage,ShaderStages.Shadows,StringComparison.OrdinalIgnoreCase)));
            if(shadows&&!GuestBridgeClient.SupportsPacketFlags(536870912))throw new NotSupportedException("Update coordinated Anomaly/SE-Vulkan: shadow owner generations require native bit29.");
            bool oit=sources.Any(s=>s.Definition.Defines.Count!=0||s.Overlays.Any(o=>o.Key.StartsWith("Transparent/OIT/",StringComparison.OrdinalIgnoreCase))||s.Injections.Any(i=>string.Equals(i.Stage,ShaderStages.Transparent,StringComparison.OrdinalIgnoreCase)));
            if(oit&&!GuestBridgeClient.SupportsPacketFlags(1073741824))throw new NotSupportedException("Update coordinated Anomaly/SE-Vulkan: OIT resolve generations require native bit30.");
            bool geometry=sources.Any(s=>s.Definition.Defines.Count!=0||s.Overlays.Any(o=>o.Key.StartsWith("Geometry/",StringComparison.OrdinalIgnoreCase)||o.Key.StartsWith("GBuffer/",StringComparison.OrdinalIgnoreCase))||s.Injections.Any(i=>new[]{ShaderStages.GBuffer,ShaderStages.Forward,ShaderStages.Highlight}.Contains(i.Stage,StringComparer.OrdinalIgnoreCase)));
            if(geometry&&!GuestBridgeClient.SupportsPacketFlags(unchecked((int)0x80000000)))throw new NotSupportedException("Update coordinated Anomaly/SE-Vulkan: stock world geometry generations require native bit31.");
            ulong extended=GuestBridgeClient.Call("NativeOwnerFeatures") is ulong features?features:0;
            if(geometry&&(extended&7)!=7)throw new NotSupportedException("Update coordinated Anomaly/SE-Vulkan: native-cell/GPU owner generation capability extension (flags1/2/4) is required.");
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
            // DX11 compiles every GBuffer permutation against Anomaly's velocity delta;
            // a pack overlay of the same key still wins, exactly as the DX11 resolver does.
            bool velocity=geometry&&(extended&VelocityContract)!=0&&(claims.Any(c=>c.Value.Count==1)||injections.Count!=0||defines.Count!=0);
            if(velocity)AddVelocitySources(files,stock,sources);
            // Publish every unique override before walking includes. Shared headers can
            // affect owners outside their advertised named-stage family, including
            // dependencies introduced by another pack's replacement entry point.
            var changedSources=new HashSet<string>(claims.Where(c=>c.Value.Count==1).Select(c=>c.Key),StringComparer.OrdinalIgnoreCase);
            foreach(var claim in claims.Where(c=>c.Value.Count==1))files[claim.Key]=Encoding.UTF8.GetBytes("#include \""+claim.Value[0]+"\"\n");
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
                bool dependency=changedSources.Count!=0&&AffectedByHeaders(entry,files,key,changedSources);
                if(!overlay&&!inject&&defines.Count==0&&!dependency)continue;
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
            if(selected.Any(id=>id>=1536&&id<1546))foreach(uint id in Enumerable.Range(1536,10).Select(v=>(uint)v))selected.Add(id);
            foreach(uint id in selected.Where(id=>id>=4096&&id<10912).ToArray()){selected.Add(id&~1u);selected.Add(id|1u);}
            foreach(uint id in selected.Where(id=>id>=16384&&id<16440||id>=32768&&id<32824||id>=49152&&id<54464).ToArray()){selected.Add(id&~1u);selected.Add(id|1u);}
            // Include the negative-border HBAO companions in the same owner generation.
            foreach(uint id in selected.ToArray())if(id>=36&&id<=51)selected.Add(id+92);
            if(selected.Count==0)return true;
            bool restoresStock=!claims.Any(c=>c.Value.Count==1)&&injections.Count==0&&defines.Count==0;
            // COMPOSITE belongs to owned camera-velocity variants, not native named stages.
            defines["ANOMALY"]="1";defines["ANOMALY_SPIRV"]="1";
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))
            {
                writer.Write("AnomalyNativeOwners-6;gbuffer-velocity");writer.Write(velocity);foreach(var file in files.OrderBy(p=>p.Key,StringComparer.Ordinal)){writer.Write(file.Key);writer.Write(file.Value.Length);writer.Write(file.Value);}
                foreach(var define in defines.OrderBy(p=>p.Key,StringComparer.Ordinal)){writer.Write(define.Key);writer.Write(define.Value);}foreach(uint id in selected.OrderBy(v=>v))writer.Write(id);
                writer.Flush();using(var sha=SHA256.Create())requested=BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-","").ToLowerInvariant();
            }
            if(activeFingerprint==requested&&preparedEpoch==epoch&&activeFormat==format){if(restoresStock)touched=Array.Empty<uint>();return true;}
            var result=(velocity?GuestBridgeClient.Call("CompileNativeOwnerGenerationWithContracts",requested,files,defines,selected.OrderBy(v=>v).ToArray(),restoresStock,1u):
                (extended&7)==7?GuestBridgeClient.Call("CompileNativeOwnerGeneration",requested,files,defines,selected.OrderBy(v=>v).ToArray(),restoresStock):GuestBridgeClient.Call("CompilePostprocessGeneration",requested,files,defines,selected.OrderBy(v=>v).ToArray())) as object[];
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
    // Native owner extension flag8: GBuffer owners admit the set-2 velocity contract.
    internal const ulong VelocityContract=8;
    static void AddVelocitySources(Dictionary<string,byte[]> files,Dictionary<string,byte[]> stock,PackSourceSnapshot[] sources)
    {
        string shaders=RenderHost.AssetFolder==null?null:Path.Combine(RenderHost.AssetFolder,"Shaders");
        if(shaders==null||!File.Exists(Path.Combine(shaders,"Anomaly.hlsli")))throw new FileNotFoundException("Anomaly shader headers unavailable for the GBuffer velocity contract.");
        var headers=new[]{Path.Combine(shaders,"Anomaly.hlsli")}.Concat(Directory.GetFiles(Path.Combine(shaders,"Anomaly"),"*",SearchOption.AllDirectories).Where(IsSource));
        foreach(string file in headers){string key=Relative(shaders,file);files[key]=stock[key]=File.ReadAllBytes(file);}
        foreach(string key in new[]{ClientPlugin.Contracts.GBufferVelocitySource.VertexStage,ClientPlugin.Contracts.GBufferVelocitySource.PixelStage,ClientPlugin.Contracts.GBufferVelocitySource.GBufferWrite})
        {
            var stockKey=stock.Keys.FirstOrDefault(k=>string.Equals(k,key,StringComparison.OrdinalIgnoreCase))??throw new FileNotFoundException("Keen source missing: "+key);
            files[stockKey]=stock[stockKey]=Encoding.UTF8.GetBytes(ClientPlugin.Contracts.GBufferVelocitySource.Patch(key,stock[stockKey],true));
        }
        AddAttachmentSources(files,stock,sources);
    }
    // DX11 adds ANOMALY_ATTACH_* and ANOMALY_PACK_GBUFFER1A to GBuffer permutations and generates the
    // attachment fields at SV_Target4+slot. Vulkan owners get the same headers, gated on ANOMALY_VELOCITY
    // (defined only for admitted GBuffer owners), and each slot's format is declared to the host.
    static void AddAttachmentSources(Dictionary<string,byte[]> files,Dictionary<string,byte[]> stock,PackSourceSnapshot[] sources)
    {
        var live=GuestPassList.SnapshotAttachments().Where(a=>a.Slot>=0&&a.Slot<3&&a.NativeFormat>=0&&a.Status!="format-conflict"&&a.Status!="capacity").GroupBy(a=>a.Slot).Select(g=>g.First()).OrderBy(a=>a.Slot).ToArray();
        string packed=sources.SelectMany(s=>s.Definition.Attachments).FirstOrDefault(a=>string.Equals(a.Format?.Trim(),"GBuffer1.a",StringComparison.OrdinalIgnoreCase))?.Name?.Trim();
        var defs=new StringBuilder("#ifndef ANOMALY_GBUFFER_ATTACHMENT_DEFS_HLSLI\n#define ANOMALY_GBUFFER_ATTACHMENT_DEFS_HLSLI\n#ifdef ANOMALY_VELOCITY\n");
        var fields=new StringBuilder("#ifndef ANOMALY_GBUFFER_ATTACHMENT_FIELDS_HLSLI\n#define ANOMALY_GBUFFER_ATTACHMENT_FIELDS_HLSLI\n");
        string[] types={"float4","float4","float","float2","uint","float2"};
        foreach(var a in live)
        {
            string macro="ANOMALY_ATTACH_"+a.Name.ToUpperInvariant();int target=4+a.Slot;
            defs.Append("#define ").Append(macro).Append(" 1\n#define ").Append(macro).Append("_TARGET ").Append(target).Append('\n');
            fields.Append("#ifdef ").Append(macro).Append("\n    ").Append(types[a.NativeFormat]).Append(' ').Append(a.Name).Append(" : SV_Target").Append(target).Append(";\n#endif\n");
            if(GuestBridgeClient.Call("DeclareGBufferAttachment",a.Slot,a.NativeFormat) is not true)throw new NotSupportedException("Update coordinated SE-Vulkan: owner GBuffer attachments require attachment slot declaration.");
        }
        if(!string.IsNullOrEmpty(packed))defs.Append("#define ANOMALY_ATTACH_").Append(packed.ToUpperInvariant()).Append(" 1\n#define ANOMALY_PACK_GBUFFER1A 1\n#define ANOMALY_ATTACH_PACKED_NAME ").Append(packed).Append('\n');
        defs.Append("#endif\n#endif\n");fields.Append("#endif\n");
        files["Anomaly/Extras/GBufferAttachmentDefs.hlsli"]=stock["Anomaly/Extras/GBufferAttachmentDefs.hlsli"]=Encoding.UTF8.GetBytes(defs.ToString());
        files["Anomaly/Extras/GBufferAttachmentFields.hlsli"]=stock["Anomaly/Extras/GBufferAttachmentFields.hlsli"]=Encoding.UTF8.GetBytes(fields.ToString());
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
    // Stock metadata is authenticated by SE-Vulkan. The copied-source walk also
    // covers includes introduced by overlays, independent of original ownership.
    internal static bool AffectedByHeaders(object[] entry,Dictionary<string,byte[]> files,string key,HashSet<string> headers)
    {
        if(entry.Length>3&&entry[3] is string[] dependencies&&dependencies.Any(headers.Contains))return true;
        var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var pending=new Stack<string>();pending.Push(key);
        while(pending.Count!=0)
        {
            string current=pending.Pop();if(!visited.Add(current))continue;if(headers.Contains(current))return true;
            if(!files.TryGetValue(current,out var bytes))continue;
            string text=IncludeText(Encoding.UTF8.GetString(bytes));
            foreach(Match include in Regex.Matches(text,@"(?m)^\s*#\s*include\b\s*([^\r\n]+)"))
            {
                string value=include.Groups[1].Value.Trim();
                if(value.Length<3||value[0]!='\"'&&value[0]!='<')return true; // Macro includes conservatively select all affected candidates.
                int end=value.IndexOf(value[0]=='<'?'>':'\"',1);if(end<0)return true;
                string path=value.Substring(1,end-1).Replace('\\','/');string folder=Path.GetDirectoryName(current).Replace('\\','/');
                foreach(string candidate in new[]{folder+"/"+path,path})
                {
                    string normalized;try{normalized=Relative(Path.GetTempPath(),PackDefinition.ResolveFile(Path.GetTempPath(),candidate));}catch{continue;}
                    if(files.ContainsKey(normalized)){pending.Push(normalized);break;}
                }
            }
        }
        // Older managed hosts have no authenticated dependency column. A header
        // must never be silently lost because its family differs from the owner.
        return entry.Length<=3;
    }
    static string IncludeText(string text)
    {
        // C preprocessing joins continued lines before replacing comments.
        text=text.Replace("\\\r\n","").Replace("\\\n","");var output=new StringBuilder(text.Length);
        bool block=false,line=false,quoted=false;
        for(int i=0;i<text.Length;i++)
        {
            char c=text[i],next=i+1<text.Length?text[i+1]:'\0';
            if(block){if(c=='*'&&next=='/'){block=false;output.Append("  ");i++;}else output.Append(c=='\n'||c=='\r'?c:' ');continue;}
            if(line){if(c=='\n'||c=='\r'){line=false;output.Append(c);}else output.Append(' ');continue;}
            if(quoted){output.Append(c);if(c=='\\'&&i+1<text.Length)output.Append(text[++i]);else if(c=='\"')quoted=false;continue;}
            if(c=='\"'){quoted=true;output.Append(c);}
            else if(c=='/'&&next=='*'){block=true;output.Append("  ");i++;}
            else if(c=='/'&&next=='/'){line=true;output.Append("  ");i++;}
            else output.Append(c);
        }
        return output.ToString();
    }
    static bool NativeKey(string key)=>key.StartsWith("Geometry/",StringComparison.OrdinalIgnoreCase)||key.StartsWith("GBuffer/",StringComparison.OrdinalIgnoreCase)||key.StartsWith("Postprocess/",StringComparison.OrdinalIgnoreCase)||key.StartsWith("Lighting/",StringComparison.OrdinalIgnoreCase)||key.StartsWith("Transparent/Atmosphere/",StringComparison.OrdinalIgnoreCase)||key.StartsWith("Foliage/",StringComparison.OrdinalIgnoreCase)||key.StartsWith("Decals/",StringComparison.OrdinalIgnoreCase)||key.StartsWith("EnvProbe/",StringComparison.OrdinalIgnoreCase)||key.StartsWith("Transparent/GPUParticles/",StringComparison.OrdinalIgnoreCase)||key.StartsWith("Shadows/",StringComparison.OrdinalIgnoreCase)||key.StartsWith("Transparent/OIT/",StringComparison.OrdinalIgnoreCase)||new[]{".hlsli",".h",".inl",".inc"}.Contains(Path.GetExtension(key),StringComparer.OrdinalIgnoreCase);
    static bool NativeStage(string stage)=>new[]{ShaderStages.GBuffer,ShaderStages.Forward,ShaderStages.Highlight}.Contains(stage,StringComparer.OrdinalIgnoreCase)||stage.StartsWith("Post.",StringComparison.OrdinalIgnoreCase)||stage.StartsWith("Lighting",StringComparison.OrdinalIgnoreCase)||string.Equals(stage,ShaderStages.Atmosphere,StringComparison.OrdinalIgnoreCase)||string.Equals(stage,ShaderStages.Foliage,StringComparison.OrdinalIgnoreCase)||string.Equals(stage,ShaderStages.Decals,StringComparison.OrdinalIgnoreCase)||string.Equals(stage,ShaderStages.EnvProbe,StringComparison.OrdinalIgnoreCase)||string.Equals(stage,ShaderStages.GpuParticles,StringComparison.OrdinalIgnoreCase)||string.Equals(stage,ShaderStages.Shadows,StringComparison.OrdinalIgnoreCase)||string.Equals(stage,ShaderStages.Transparent,StringComparison.OrdinalIgnoreCase);
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
