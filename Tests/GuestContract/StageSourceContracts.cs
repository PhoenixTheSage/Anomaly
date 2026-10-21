using System;
using System.IO;
using System.Linq;
using ClientPlugin.Backend;
using ClientPlugin.Shaders;

static class StageSourceContracts
{
    internal static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"AnomalyStageSources-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        void Write(string relative,string source){string path=PackDefinition.ResolveFile(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,source);}
        try
        {
            Write("anomaly.json","{\"id\":\"stages.fixture\",\"exclusive\":[\"GBuffer\",\"Lighting\",\"Atmosphere\"]}");
            foreach(string stage in ShaderStages.Names)
            {
                if(!ShaderStages.TryGetFiles(stage,out var files)||files.Count==0)throw new Exception("Advertised stage has no source mapping: "+stage);
                Write("Overlay/"+stage+"/"+files[0],"// captured stage "+stage);
                Write("Inject/"+stage+"/fixture.h","// captured injection "+stage);
            }
            Write("Inject/Lighting/family.hlsli","// lighting family");
            Write("Inject/Unknown/fixture.hlsl","// unknown stage");
            Write("Overlay/Post.HBAO/not-advertised.hlsl","// unknown named stage file");
            var snapshot=PackSourceSnapshot.Capture(root,"stages.fixture");
            if(snapshot.Overlays.Count!=ShaderStages.Names.Count || ShaderStages.Names.Any(stage=>!snapshot.Overlays.Any(file=>file.Stage==stage)))throw new Exception("Shared model did not map every advertised overlay stage");
            if(snapshot.Injections.Any(file=>file.Stage==ShaderStages.Depth)||snapshot.Injections.Count!=ShaderStages.Names.Count)throw new Exception("Shared injection legality or Lighting-family mapping differs from DX11");
            if(!snapshot.Diagnostics.Any(d=>d.Contains("Depth"))||!snapshot.Diagnostics.Any(d=>d.Contains("unknown named-stage")))throw new Exception("Skipped stage sources lost their diagnostic");
            if(PackSourceSnapshot.OverlayRefusal("GBuffer/GBufferWrite.hlsli",new string[0])==null || PackSourceSnapshot.OverlayRefusal("GBuffer/GBufferWrite.hlsli",new[]{"GBuffer"})!=null)throw new Exception("GBuffer ownership restriction differs from DX11");
            ShaderStages.TryGetFiles(ShaderStages.Depth,out var depthFiles);
            Write("Overlay/Depth/"+depthFiles[0],"float4 main():SV_Target3{return 0;}");
            var rejected=PackSourceSnapshot.Capture(root,"stages.fixture");
            if(rejected.Overlays.Any(file=>file.Stage==ShaderStages.Depth)||!rejected.Diagnostics.Any(d=>d.Contains("SV_Target3")))throw new Exception("Depth overlay added a reserved MRT output");
            Console.WriteLine("PASS: shared captured source mapping for all "+ShaderStages.Names.Count+" stages, Lighting-family injection, .h headers, ownership and forbidden Depth/MRT rules (source-model evidence only)");
        }
        finally{if(Path.GetDirectoryName(Path.GetFullPath(root))==Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar))Directory.Delete(root,true);}
    }
}
