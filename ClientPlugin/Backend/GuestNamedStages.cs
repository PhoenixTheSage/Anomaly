using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ClientPlugin.Shaders;

namespace ClientPlugin.Backend;

/// <summary>Detached generation for Anomaly-owned fullscreen raster programs.
/// Stock native owners and native mip blits are deliberately outside this transaction.</summary>
internal static class GuestNamedStages
{
    static readonly string[] Names={"CameraVelocity","LinearDepth","HiZ","HistoryCopy"};
    static readonly string[] Stages={ShaderStages.AnomalyCameraVelocity,ShaderStages.AnomalyLinearDepth,ShaderStages.AnomalyLinearDepth,ShaderStages.AnomalyHistoryColor};
    static readonly string[] Files={"CameraVelocity.hlsl","LinearDepth.hlsl","HiZDownsample.hlsl","HistoryCopy.hlsl"};
    static long attemptedRevision=-1,attemptedEpoch=-1;
    static string attemptedAssets;
    static Candidate pending;
    internal static bool DeferActivation;
    internal static bool CanActivate()=>Error==null&&(pending==null||pending.Revision==GuestPassList.SourceRevision&&pending.Epoch==GuestBridgeClient.ResourceEpoch&&pending.Originals.All(p=>GuestPassList.Snapshot().Contains(p)));
    internal static void DiscardPrepared(){if(pending!=null)attemptedRevision=-1;Discard(pending);pending=null;}
    internal static string Requested {get;private set;}
    internal static string Prepared=>pending?.Fingerprint;
    internal static string Error {get;private set;}
    internal static OwnedStageGenerationState State(string stage)
    {
        lock(GuestPassList.Gate)
        {
            var ids=Names.Where((name,index)=>string.Equals(Stages[index],stage,StringComparison.OrdinalIgnoreCase)).Select(name=>"anomaly.owned."+name).ToArray();
            var programs=GuestPassList.Snapshot().Where(p=>ids.Contains(p.Id)).ToArray();
            return new OwnedStageGenerationState
            {
                Stage=stage,ProgramIds=Array.AsReadOnly(ids),
                RasterReplacementSupported=RenderHost.IsVulkan&&ids.Length!=0&&GuestBridgeClient.SupportsPacketFlags(GuestBridgeClient.ResourceCommands|GuestBridgeClient.OwnedRasterGenerations),
                RequestedGeneration=Requested,PreparedGeneration=Prepared,
                ActiveGeneration=programs.Length==ids.Length&&programs.Length!=0&&programs.All(p=>p.Generation==programs[0].Generation)?programs[0].Generation:null,
                Error=Error
            };
        }
    }
    sealed class Candidate
    {
        internal long Revision,Epoch;
        internal string Fingerprint;
        internal GuestProgram[] Originals,Programs;
    }
    static GuestNamedStages(){GuestPassList.BeforeSnapshot=ActivateAtBoundary;}

    internal static void Prepare(string assets)
    {
        // Only renderer-thread callers create native resources. Registration itself is CPU-only.
        long revision,epoch=GuestBridgeClient.ResourceEpoch;PackSourceSnapshot[] sources;GuestProgram[] originals;
        lock(GuestPassList.Gate)
        {
            revision=GuestPassList.SourceRevision;
            if(revision==attemptedRevision&&epoch==attemptedEpoch&&assets==attemptedAssets)return;
            sources=GuestPassList.SourceSnapshot().OrderBy(s=>s.Definition.Priority).ThenBy(s=>s.Definition.Id,StringComparer.OrdinalIgnoreCase).ThenBy(s=>s.Fingerprint,StringComparer.Ordinal).ToArray();
            originals=Names.Select(n=>GuestPassList.Snapshot().FirstOrDefault(p=>p.Id=="anomaly.owned."+n)).ToArray();
        }
        if(originals.Any(p=>p==null||p.Pipeline==0))return;

        var disabled=new HashSet<PackSourceSnapshot>();
        foreach(var group in sources.SelectMany(s=>s.Definition.Exclusive.Select(stage=>new{Source=s,Stage=stage})).GroupBy(v=>v.Stage,StringComparer.OrdinalIgnoreCase))
            if(group.Select(claim=>claim.Source).Distinct().Count()>1)foreach(var claim in group)disabled.Add(claim.Source);
        sources=sources.Where(s=>!disabled.Contains(s)).ToArray();
        bool requested=sources.Any(s=>s.Definition.Defines.Count!=0||s.Overlays.Any(o=>IsOwnedKey(o.Key))||s.Injections.Any(i=>Stages.Contains(i.Stage,StringComparer.OrdinalIgnoreCase)));
        if(!requested&&originals.All(p=>p.Generation==null))
        {Discard(pending);pending=null;Requested=null;Error=null;attemptedRevision=revision;attemptedEpoch=epoch;attemptedAssets=assets;return;}
        if(!GuestBridgeClient.SupportsPacketFlags(GuestBridgeClient.ResourceCommands|GuestBridgeClient.OwnedRasterGenerations))
        {Error="Update SE-Vulkan native library: owned-stage replacement requires resource retirement (bit13) and format-aware raster generations (bit18).";return;}
        attemptedRevision=revision;attemptedEpoch=epoch;attemptedAssets=assets;
        Discard(pending);pending=null;Error=null;
        var candidate=new Candidate{Revision=revision,Epoch=epoch,Originals=originals,Programs=new GuestProgram[originals.Length]};
        try
        {
            string shaderRoot=Path.Combine(assets,"Shaders");if(!Directory.Exists(shaderRoot))shaderRoot=assets;
            var captured=new SortedDictionary<string,byte[]>(StringComparer.Ordinal);
            foreach(string path in Directory.GetFiles(shaderRoot,"*",SearchOption.AllDirectories))
                if(IsSource(path))captured["stock/"+Relative(shaderRoot,path)]=File.ReadAllBytes(path);
            string gameRoot=GuestCompiler.GameShaderDirectory();
            if(!string.IsNullOrEmpty(gameRoot)&&Directory.Exists(gameRoot))
                foreach(string path in Directory.GetFiles(gameRoot,"*",SearchOption.AllDirectories))
                    if(IsSource(path))captured["game/"+Relative(gameRoot,path)]=File.ReadAllBytes(path);
            var claims=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
            var injection=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
            var defines=new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            for(int i=0;i<sources.Length;i++)
            {
                var source=sources[i];string prefix="packs/"+i+"/";
                captured[prefix+"identity.hlsli"]=Encoding.UTF8.GetBytes("// "+source.Fingerprint+"\n");
                foreach(string file in source.Files.Where(IsSource))captured[prefix+file]=source.Bytes(file);
                foreach(var overlay in source.Overlays.Where(o=>IsOwnedKey(o.Key)))
                {
                    if(!claims.TryGetValue(overlay.Key,out var list))claims.Add(overlay.Key,list=new List<string>());
                    list.Add(prefix+overlay.RelativePath);
                }
                foreach(var inject in source.Injections.Where(v=>Stages.Contains(v.Stage,StringComparer.OrdinalIgnoreCase)))
                {
                    if(!injection.TryGetValue(inject.Stage,out var list))injection.Add(inject.Stage,list=new List<string>());
                    list.Add(prefix+inject.RelativePath);
                }
                foreach(string raw in source.Definition.Defines)
                {
                    string name=raw.Split('=')[0].Trim();
                    if(Regex.IsMatch(name,@"^[A-Za-z_][A-Za-z0-9_]*$")&&!Reserved(name))defines.Add(name);
                }
            }
            string prefixDefines=string.Concat(defines.Select(name=>"#define "+name+" 1\n"));
            foreach(string stage in Stages.Distinct())
            {
                var text=new StringBuilder("#pragma once\n");
                if(injection.TryGetValue(stage,out var list))foreach(string path in list)text.Append("#include \"").Append(path).Append("\"\n");
                captured[ShaderStages.ExtrasIncludePath(stage)]=Encoding.UTF8.GetBytes(text.ToString());
            }
            string Selected(string file)=>claims.TryGetValue(file,out var owners)&&owners.Count==1?owners[0]:"stock/"+file;
            for(int i=0;i<Names.Length;i++)
            {
                // Flip clip-space Y for Vulkan's positive viewport; preserve custom DX11 UV functions.
                string vertex=prefixDefines+"#include \""+Selected("Fullscreen.hlsl")+"\"\n"+
                    "void main(uint id:SV_VertexID,out float4 position:SV_Position,out float2 uv:TEXCOORD0){__vertex_shader(id,position,uv);position.y=-position.y;}\n";
                string pixel=prefixDefines+"#include \""+ShaderStages.ExtrasIncludePath(Stages[i])+"\"\n#include \""+Selected(Files[i])+"\"\n";
                captured[Names[i]+".vs.hlsl"]=Encoding.UTF8.GetBytes(vertex);
                captured[Names[i]+".ps.hlsl"]=Encoding.UTF8.GetBytes(pixel);
            }
            using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))
            {
                writer.Write("AnomalyOwnedRasterGeneration-2");
                foreach(var file in captured){writer.Write(file.Key);writer.Write(file.Value.Length);writer.Write(file.Value);}
                writer.Flush();using(var sha=SHA256.Create())candidate.Fingerprint=BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-","").ToLowerInvariant();
            }
            Requested=candidate.Fingerprint;
            if(originals.All(p=>p.Generation==candidate.Fingerprint&&p.ResourceEpoch==epoch))return;
            string root=Materialize(candidate.Fingerprint,captured);
            for(int i=0;i<Names.Length;i++)
            {
                var p=originals[i].FrameCopy();p.Pipeline=0;p.StorageImage=p.HistoryImage=0;p.VertexSpirv=p.PixelSpirv=null;
                p.File=Path.Combine(root,Names[i]+".ps.hlsl");p.VertexFile=Path.Combine(root,Names[i]+".vs.hlsl");p.VertexEntry="main";
                p.IncludeDirectories=Enumerable.Range(0,sources.Length).SelectMany(n=>new[]{Path.Combine(root,"packs",n.ToString()),Path.Combine(root,"packs",n.ToString(),"Inject")}).Concat(new[]{Path.Combine(root,"stock"),Path.Combine(root,"game")}).Where(Directory.Exists).ToArray();
                p.IncludeRoot=root;p.CapturedInputsOnly=true;p.Status="source";p.Error=null;p.Generation=candidate.Fingerprint;
                candidate.Programs[i]=p;
            }
            GuestCompiler.CompileDetached(Path.Combine(root,"stock"),candidate.Programs);
            var failure=candidate.Programs.FirstOrDefault(p=>p.Pipeline==0);
            if(failure!=null)throw new InvalidOperationException(failure.Id+": "+(failure.Error??failure.Status));
            // Activation rechecks epoch, registration revision and object identity under the frame lock.
            pending=candidate;
        }
        catch(Exception error){Error=error.GetBaseException().Message;Discard(candidate);}
    }

    internal static void ActivateAtBoundary(){if(!DeferActivation)ActivatePrepared();}
    internal static void ActivatePrepared()
    {
        // Called by SubmitFrame while holding GuestPassList.Gate, before freezing the frame.
        var candidate=pending;if(candidate==null)return;pending=null;
        var current=GuestPassList.Snapshot();
        if(candidate.Revision!=GuestPassList.SourceRevision||candidate.Epoch!=GuestBridgeClient.ResourceEpoch||candidate.Originals.Any(p=>!current.Contains(p)))
        {Error="Prepared owned-stage generation became stale before frame activation.";Discard(candidate);return;}
        for(int i=0;i<candidate.Programs.Length;i++)
        {
            var live=candidate.Originals[i];var next=candidate.Programs[i];
            GuestPassList.RetirePipeline(live);
            live.Pipeline=next.Pipeline;live.ResourceEpoch=candidate.Epoch;next.Pipeline=0;
            live.File=next.File;live.VertexFile=next.VertexFile;live.IncludeRoot=next.IncludeRoot;live.IncludeDirectories=next.IncludeDirectories;live.CapturedInputsOnly=true;
            live.VertexSpirv=next.VertexSpirv;live.PixelSpirv=next.PixelSpirv;live.VertexEntry=next.VertexEntry;live.PixelEntry=next.PixelEntry;
            live.Generation=candidate.Fingerprint;live.HistoryReady=false;live.HistoryFrame=uint.MaxValue;live.SubmittedThisFrame=false;
            live.Status="pipeline";live.Error=null;
        }
        Error=null;
    }
    static void Discard(Candidate candidate)
    {
        if(candidate?.Programs==null)return;
        foreach(var p in candidate.Programs)if(p!=null)GuestPassList.RetirePipeline(p);
    }
    static bool IsOwnedKey(string key)=>key.Equals("Fullscreen.hlsl",StringComparison.OrdinalIgnoreCase)||Files.Contains(key,StringComparer.OrdinalIgnoreCase);
    static bool Reserved(string name)=>name.StartsWith("ANOMALY_ATTACH_",StringComparison.OrdinalIgnoreCase)||new[]{"ANOMALY","ANOMALY_VELOCITY","ANOMALY_PACK_GBUFFER1A","RENDERING_PASS","DEPTH_ONLY","CUSTOM_DEPTH"}.Contains(name,StringComparer.OrdinalIgnoreCase);
    static bool IsSource(string path)=>new[]{".hlsl",".hlsli",".h",".inl",".inc"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase);
    static string Relative(string root,string path)=>Path.GetFullPath(path).Substring(Path.GetFullPath(root).TrimEnd('\\','/').Length+1).Replace('\\','/');
    static string Materialize(string hash,SortedDictionary<string,byte[]> files)
    {
        string cache=Path.Combine(Path.GetTempPath(),"anomaly-owned-generations"),root=Path.Combine(cache,hash);Directory.CreateDirectory(cache);
        if(!Directory.Exists(root))
        {
            string temporary=Path.Combine(cache,hash+"."+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
            try
            {
                foreach(var file in files){string target=PackDefinition.ResolveFile(temporary,file.Key);Directory.CreateDirectory(Path.GetDirectoryName(target));File.WriteAllBytes(target,file.Value);}
                try{Directory.Move(temporary,root);}catch(IOException){if(!Directory.Exists(root))throw;}
            }
            finally{if(Path.GetDirectoryName(Path.GetFullPath(temporary))==Path.GetFullPath(cache)&&Directory.Exists(temporary))Directory.Delete(temporary,true);}
        }
        foreach(var file in files)if(!File.ReadAllBytes(PackDefinition.ResolveFile(root,file.Key)).SequenceEqual(file.Value))throw new InvalidDataException("Owned-stage source cache differs: "+file.Key);
        return root;
    }
}

/// <summary>Vulkan owned-raster generation state. Production is queried separately by ProgramIds.</summary>
public sealed class OwnedStageGenerationState
{
    public string Stage {get;internal set;}
    public bool RasterReplacementSupported {get;internal set;}
    public string RequestedGeneration {get;internal set;}
    public string PreparedGeneration {get;internal set;}
    public string ActiveGeneration {get;internal set;}
    public IReadOnlyList<string> ProgramIds {get;internal set;}
    public string Error {get;internal set;}
}
