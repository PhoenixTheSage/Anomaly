using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ClientPlugin.Shaders;

namespace ClientPlugin.Backend;

/// <summary>Registration-time shader bytes shared by the DX11 and Vulkan adapters.</summary>
public sealed class PackSourceSnapshot
{
    public const string Contract="AnomalyPackSources-2;stage-source-model-1";
    readonly SortedDictionary<string,byte[]> sources;
    public PackDefinition Definition { get; }
    public string SourceRoot { get; }
    public string Root { get; }
    public string Fingerprint { get; }
    public IReadOnlyList<string> Files { get; }
    public IReadOnlyList<PackStageSource> Overlays { get; }
    public IReadOnlyList<PackStageSource> Injections { get; }
    public IReadOnlyList<string> Diagnostics { get; }
    public byte[] Bytes(string relative)=>sources.TryGetValue(relative.Replace('\\','/'),out var bytes)?(byte[])bytes.Clone():null;

    PackSourceSnapshot(string root,string id,string cacheRoot)
    {
        SourceRoot=Path.GetFullPath(root);
        byte[] manifest=File.ReadAllBytes(Path.Combine(SourceRoot,"anomaly.json"));
        using(var reader=new StreamReader(new MemoryStream(manifest),Encoding.UTF8,true))Definition=PackDefinition.Parse(reader.ReadToEnd());
        sources=new SortedDictionary<string,byte[]>(StringComparer.Ordinal);
        sources.Add("anomaly.json",manifest);
        var required=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var pass in Definition.Passes)required.Add(PackDefinition.ResolveFile(SourceRoot,pass.File));
        foreach(var attachment in Definition.Attachments)required.Add(PackDefinition.ResolveFile(SourceRoot,attachment.File));
        foreach(string file in Directory.GetFiles(SourceRoot,"*",SearchOption.AllDirectories))
        {
            string full=Path.GetFullPath(file),extension=Path.GetExtension(file).ToLowerInvariant();
            string relative=full.Substring(SourceRoot.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).Length+1).Replace('\\','/');
            if(relative=="anomaly.json")continue;
            if(!required.Contains(full)&&extension!=".hlsl"&&extension!=".hlsli"&&extension!=".h"&&extension!=".inl"&&extension!=".inc")continue;
            PackDefinition.ResolveFile(SourceRoot,relative);
            sources.Add(relative,File.ReadAllBytes(full));
        }
        Files=Array.AsReadOnly(sources.Keys.ToArray());
        var overlays=new List<PackStageSource>();var injections=new List<PackStageSource>();var diagnostics=new List<string>();
        foreach(var file in sources)
        {
            string extension=Path.GetExtension(file.Key).ToLowerInvariant();if(extension!=".hlsl"&&extension!=".hlsli"&&extension!=".h")continue;
            if(file.Key.StartsWith("Overlay/",StringComparison.OrdinalIgnoreCase))
            {
                string relative=file.Key.Substring(8);
                if(!ShaderStages.TryMapOverlayPath(relative,out string key,out string stage)){diagnostics.Add("skipped overlay (unknown named-stage file): "+relative);continue;}
                string refusal=OverlayRefusal(key,Definition.Exclusive);
                if(refusal!=null){diagnostics.Add(refusal);continue;}
                bool depth=string.Equals(stage,ShaderStages.Depth,StringComparison.OrdinalIgnoreCase)||key.IndexOf("/Depth",StringComparison.OrdinalIgnoreCase)>=0||key.StartsWith("Depth",StringComparison.OrdinalIgnoreCase);
                using(var reader=new StreamReader(new MemoryStream(file.Value),Encoding.UTF8,true))
                    if(depth&&reader.ReadToEnd().IndexOf("SV_Target3",StringComparison.OrdinalIgnoreCase)>=0){diagnostics.Add("Depth overlay must not add SV_Target3: "+key);continue;}
                overlays.Add(new PackStageSource(file.Key,key,stage));
            }
            else if(file.Key.StartsWith("Inject/",StringComparison.OrdinalIgnoreCase))
            {
                string relative=file.Key.Substring(7);
                if(!ShaderStages.TryMapInjectPath(relative,out string stage)){diagnostics.Add("skipped inject (unknown named-stage folder): "+relative);continue;}
                if(ShaderStages.IsForbiddenInjectStage(stage)){diagnostics.Add("skipped inject into Depth: "+relative);continue;}
                injections.Add(new PackStageSource(file.Key,ShaderStages.ExtrasIncludePath(stage),stage));
            }
        }
        injections.Sort((a,b)=>{int stage=string.Compare(a.Stage,b.Stage,StringComparison.OrdinalIgnoreCase);return stage!=0?stage:string.Compare(a.RelativePath,b.RelativePath,StringComparison.OrdinalIgnoreCase);});
        Overlays=overlays.AsReadOnly();Injections=injections.AsReadOnly();Diagnostics=diagnostics.AsReadOnly();
        using(var stream=new MemoryStream())
        using(var writer=new BinaryWriter(stream,Encoding.UTF8,true))
        {
            writer.Write(Contract);writer.Write(Definition.Id);
            foreach(var file in sources){writer.Write(file.Key);writer.Write(file.Value.Length);writer.Write(file.Value);}
            writer.Flush();using(var sha=SHA256.Create())Fingerprint=BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-","").ToLowerInvariant();
        }
        string cache=Path.GetFullPath(cacheRoot??Path.Combine(Path.GetTempPath(),"anomaly-pack-generations"));Directory.CreateDirectory(cache);
        Root=Path.Combine(cache,Fingerprint);
        if(!Directory.Exists(Root))
        {
            string temporary=Path.Combine(cache,Fingerprint+"."+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
            try
            {
                foreach(var file in sources){string target=PackDefinition.ResolveFile(temporary,file.Key);Directory.CreateDirectory(Path.GetDirectoryName(target));File.WriteAllBytes(target,file.Value);}
                try{Directory.Move(temporary,Root);}catch(IOException){if(!Directory.Exists(Root))throw;}
            }
            finally{if(Path.GetDirectoryName(Path.GetFullPath(temporary))==cache && Directory.Exists(temporary))Directory.Delete(temporary,true);}
        }
        foreach(var file in sources)
            if(!File.ReadAllBytes(PackDefinition.ResolveFile(Root,file.Key)).SequenceEqual(file.Value))
                throw new InvalidDataException("Captured pack source cache differs from its registration snapshot: "+file.Key);
    }
    public static PackSourceSnapshot Capture(string root,string id,string cacheRoot=null)=>new PackSourceSnapshot(root,id,cacheRoot);

    public static string OverlayRefusal(string key,IReadOnlyList<string> exclusive)
    {
        bool Has(string stage)=>exclusive!=null&&exclusive.Any(value=>string.Equals(value,stage,StringComparison.OrdinalIgnoreCase));
        if(ShaderStages.IsAnomalyOwnedGBuffer(key)&&!Has(ShaderStages.GBuffer))return "overlay of Anomaly GBuffer stage requires exclusive: [\"GBuffer\"]: "+key;
        if(ShaderStages.IsAnomalyOwnedGBufferRead(key)&&!Has(ShaderStages.GBuffer)&&!Has(ShaderStages.Lighting))return "overlay of Anomaly GBuffer read wrap requires exclusive: [\"GBuffer\"] or [\"Lighting\"]: "+key;
        if(ShaderStages.IsAnomalyOwnedLightingWrap(key)&&!Has(ShaderStages.Lighting))return "overlay of Anomaly Lighting wrap requires exclusive: [\"Lighting\"]: "+key;
        if(ShaderStages.IsAnomalyOwnedAtmosphereWrap(key)&&!Has(ShaderStages.Atmosphere))return "overlay of Anomaly Atmosphere wrap requires exclusive: [\"Atmosphere\"]: "+key;
        return null;
    }
}

public sealed class PackStageSource
{
    public string RelativePath {get;}
    public string Key {get;}
    public string Stage {get;}
    internal PackStageSource(string relative,string key,string stage){RelativePath=relative;Key=key;Stage=stage;}
}
