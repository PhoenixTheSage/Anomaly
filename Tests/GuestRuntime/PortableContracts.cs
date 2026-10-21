using System;
using System.Collections.Generic;
using System.Linq;
using ClientPlugin.Backend;
using ClientPlugin.Shaders;

static partial class Program
{
    static void PortableContracts()
    {
        var order=new List<string>();PortablePassContext saved=null;
        void Register(string id,string slot,int priority,int phase,Action<object> callback=null)
            =>PortablePassRegistry.Register("portable.test."+id,slot,priority,0,callback??(c=>order.Add(id)),phase);
        Register("z","AfterLighting",10,0);Register("b","AfterLighting",0,0);
        Register("a","AfterLighting",0,0,c=>{order.Add("a");saved=(PortablePassContext)c;Register("late","AfterLighting",-10,0);});
        Register("after","AfterLighting",0,1);Register("empty","BeforeTonemap",0,0);
        var snapshot=PortablePassRegistry.Snapshot();
        PortablePassRegistry.Run(snapshot,1,0,Array.Empty<GuestProgram>());
        PortablePassRegistry.Run(snapshot,1,1,Array.Empty<GuestProgram>());
        PortablePassRegistry.Run(snapshot,4,0,Array.Empty<GuestProgram>());
        Require(string.Join(",",order)=="a,b,z,after,empty","Portable ordering/phase/empty-slot or immutable registration snapshot failed");
        bool expired=false;try{saved.Clear("private",0,0,0,-1);}catch(InvalidOperationException){expired=true;}
        Require(expired,"Portable callback context remained writable after completion");
        Register("failure","AfterLighting",0,1,c=>{saved=(PortablePassContext)c;throw new Exception("controlled callback failure");});
        PortablePassRegistry.Run(PortablePassRegistry.Snapshot(),1,1,Array.Empty<GuestProgram>());
        Require(PortablePassRegistry.Error("portable.test.failure")=="controlled callback failure","Portable callback error was not queryable");
        expired=false;try{saved.Copy("a","b");}catch(InvalidOperationException){expired=true;}
        Require(expired,"Failed callback context remained writable");
        foreach(var pass in PortablePassRegistry.Snapshot().Where(p=>p.Id.StartsWith("portable.test.")))PortablePassRegistry.Unregister(pass.Id);
        var context=new PortablePassContext("AfterUpscale",0,2,17,9,123);
        context.Clear("target",2,3,4,-1);context.Copy("target","copy");context.ContributeVelocity("overlay","mask");
        var commands=context.Finish();
        Require(context.IsOutputResolution && context.FrameIndex==123 && context.Width==17 && context.Height==9 && commands.Length==3 && ((float[])commands[0][2])[3]==-1,"Portable metadata/command copying lost HDR or negative alpha");
        int features=ClientPlugin.Rendering.GuestBridge.SupportedPacketFlags;
        ClientPlugin.Rendering.GuestBridge.SupportedPacketFlags&=~131072;
        bool refused=false;try{GuestBridgeClient.ExecutePortable(1,commands,Array.Empty<GuestProgram>());}catch(NotSupportedException error){refused=error.Message.Contains("Update SE-Vulkan");}
        finally{ClientPlugin.Rendering.GuestBridge.SupportedPacketFlags=features;}
        Require(refused,"Portable commands accepted an older native host without actionable refusal");
        GuestPassList.AddProgram("portable.snapshot","AfterLighting","unused.hlsl","PublishOnly");
        var original=GuestPassList.Snapshot().Single(p=>p.Id=="portable.snapshot");original.Owner="portable.snapshot";
        GuestPassList.SetUniforms(original.Id,new[]{1f});
        GuestPassList.SubmitFrame(frame=>
        {
            var copy=frame.Single(p=>p.Id==original.Id);
            GuestPassList.SetScale(original.Id,.25f);GuestPassList.SetUniforms(original.Id,new[]{3f});
            Require(copy.Scale==1 && copy.Uniforms[0]==1,"Current-frame settings or copied uniforms mutated through registration");
            GuestPassList.RefreshPreparedInputs(copy);
            Require(copy.Uniforms[0]==3 && copy.Scale==1,"Renderer preparation did not update uniforms independently of frozen frame settings");
        });
        GuestPassList.SubmitFrame(frame=>Require(frame.Single(p=>p.Id==original.Id).Scale==.25f,"Next-frame settings did not advance"));
        GuestPassList.UnregisterRoot(original.Owner);
        Console.WriteLine("PASS: portable stable ordering, phases, empty slots, immutable membership, expiry, failure status and copied HDR commands");
    }
}

static partial class NativeProbe
{
    public static void CapturedPackGpu(string assets)
    {
        if(!Enabled)return;
        string root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"AnomalyCapturedGpu-"+Guid.NewGuid().ToString("N"));System.IO.Directory.CreateDirectory(root);
        int features=ClientPlugin.Rendering.GuestBridge.SupportedPacketFlags;
        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(root,"anomaly.json"),"{\"id\":\"captured.fixture\",\"passes\":[{\"id\":\"captured.pixel\",\"slot\":\"AfterLighting\",\"file\":\"pixel.hlsl\",\"compose\":\"PublishOnly\"}]}");
            string source=System.IO.Path.Combine(root,"pixel.hlsl");System.IO.File.WriteAllText(source,"float4 main():SV_Target{return float4(3,.5,.25,-1);}");
            GuestPassList.RegisterRoot("captured.fixture",root,"captured.pixel");
            System.IO.File.WriteAllText(source,"float4 main():SV_Target{return float4(0,1,0,1);}");
            GuestCompiler.CompilePending(assets);var program=GuestPassList.Snapshot().Single(p=>p.Id=="captured.pixel");
            if(program.Pipeline==0)throw new Exception("Captured pack pipeline failed: "+program.Error);
            const ulong output=85420;ImageCreate(output,3,2,1,1);
            ClientPlugin.Buffers.BufferCatalog.Set("captured.output",new ClientPlugin.Buffers.GuestRendererImage(output,3,2,1));
            ClientPlugin.Rendering.GuestBridge.SupportedPacketFlags|=131072;ClientPlugin.Rendering.GuestBridge.MirrorGpu=true;
            ResetQueue();var context=new PortablePassContext("AfterLighting",0,0,3,2,0);context.Fullscreen(program.Id,"captured.output");
            GuestBridgeClient.ExecutePortable(1,context.Finish(),GuestPassList.Snapshot());Replay(1);
            var actual=ReadRaw(output,3*2*8);float[] expected={3,.5f,.25f,-1};
            for(int pixel=0;pixel<6;pixel++)for(int c=0;c<4;c++)if((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(actual,pixel*8+c*2))!=expected[c])throw new Exception("Compilation followed edited pack paths instead of registered bytes");
            Console.WriteLine("GPU PASS: registered pack source generation executes captured HDR pixels after original shader bytes are edited");
        }
        finally
        {
            GuestPassList.UnregisterRoot("captured.fixture");ClientPlugin.Rendering.GuestBridge.SupportedPacketFlags=features;ClientPlugin.Rendering.GuestBridge.MirrorGpu=false;ResetQueue();
            if(System.IO.Path.GetFullPath(root).StartsWith(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))System.IO.Directory.Delete(root,true);
        }
    }
    public static void PortableGpuContracts()
    {
        if(!Enabled)return;
        const ulong source=85401,copy=85402,r16=85403,draw=85404,velocity=85405,overlay=85406;
        ImageCreate(source,7,5,1,1);ImageCreate(copy,7,5,1,1);ImageCreate(r16,7,5,1,8);
        ImageCreate(draw,7,5,1,1);ImageCreate(velocity,7,5,1,3);ImageCreate(overlay,7,5,1,3);
        ClientPlugin.Buffers.BufferCatalog.Set("portable.source",new ClientPlugin.Buffers.GuestRendererImage(source,7,5,1));
        ClientPlugin.Buffers.BufferCatalog.Set("portable.copy",new ClientPlugin.Buffers.GuestRendererImage(copy,7,5,1));
        ClientPlugin.Buffers.BufferCatalog.Set("portable.r16",new ClientPlugin.Buffers.GuestRendererImage(r16,7,5,1));
        ClientPlugin.Buffers.BufferCatalog.Set("portable.draw",new ClientPlugin.Buffers.GuestRendererImage(draw,7,5,1));
        ClientPlugin.Buffers.BufferCatalog.Set("velocity",new ClientPlugin.Buffers.GuestRendererImage(velocity,7,5,1));
        ClientPlugin.Buffers.BufferCatalog.Set("portable.overlay",new ClientPlugin.Buffers.GuestRendererImage(overlay,7,5,1));
        var pixels=new byte[7*5*4];for(int i=0;i<35;i++){Buffer.BlockCopy(BitConverter.GetBytes(BitConverter.HalfToUInt16Bits((Half).25)),0,pixels,i*4,2);Buffer.BlockCopy(BitConverter.GetBytes(BitConverter.HalfToUInt16Bits((Half)(-.5))),0,pixels,i*4+2,2);}ImageUpdate(overlay,pixels);ImageUpdate(velocity,new byte[pixels.Length]);
        SceneView.Width=7;SceneView.Height=5;
        var programs=GuestPassList.Snapshot().ToList();var manual=programs.Single(p=>p.Id=="anomaly.helper.Merge0").FrameCopy();
        manual.Helper=false;manual.Id="portable.manual";programs.Add(manual);
        int features=ClientPlugin.Rendering.GuestBridge.SupportedPacketFlags;
        ClientPlugin.Rendering.GuestBridge.SupportedPacketFlags|=131072;
        ClientPlugin.Rendering.GuestBridge.MirrorGpu=true;
        try
        {
            ResetQueue();var context=new PortablePassContext("AfterLighting",0,2,7,5,0);
            context.Clear("portable.source",2.5f,.125f,.75f,-1);context.Copy("portable.source","portable.copy");context.Clear("portable.r16",3.5f,0,0,0);
            context.Fullscreen("portable.manual","portable.draw","portable.copy");context.ContributeVelocity("portable.overlay","portable.source");
            GuestBridgeClient.ExecutePortable(1,context.Finish(),programs);Replay(1);
            var actual=ReadRaw(copy,7*5*8);float[] expected={2.5f,.125f,.75f,-1};
            for(int pixel=0;pixel<35;pixel++)for(int c=0;c<4;c++)
                if(Math.Abs((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(actual,pixel*8+c*2))-expected[c])>.001)throw new Exception("Portable clear/copy pixel mismatch");
            actual=ReadRaw(r16,7*5*2);
            for(int pixel=0;pixel<35;pixel++)if((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(actual,pixel*2))!=3.5f)throw new Exception("Portable R16 clear pipeline mismatch");
            actual=ReadRaw(draw,7*5*8);
            for(int pixel=0;pixel<35;pixel++)for(int c=0;c<4;c++)if((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(actual,pixel*8+c*2))!=expected[c])throw new Exception("Portable fullscreen draw mismatch");
            actual=ReadRaw(velocity,7*5*4);
            for(int pixel=0;pixel<35;pixel++)if((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(actual,pixel*4))!=.25f || (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(actual,pixel*4+2))!=-.5f)throw new Exception("Portable velocity command mismatch");
            Console.WriteLine("GPU PASS: portable HDR/negative-alpha clear, ordered copy, fullscreen, masked velocity and odd-size FP16/R16 targets");
        }
        finally{ClientPlugin.Rendering.GuestBridge.SupportedPacketFlags=features;ClientPlugin.Rendering.GuestBridge.MirrorGpu=false;GuestTemporal.ResetDevice();ResetQueue();}
    }
}
