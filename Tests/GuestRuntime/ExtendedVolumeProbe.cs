using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ClientPlugin.Backend;
using ClientPlugin.Rendering;
using ClientPlugin.ShaderFramework;
using ClientPlugin.Shaders;
using VRageMath;

static partial class Program
{
    static void ExtendedVolume(string assets,bool packs)
    {
        int savedFlags=GuestBridge.SupportedPacketFlags;
        try
        {
        GuestBridge.SupportedPacketFlags = savedFlags | 1023;
        string root = Path.Combine(assets,"Shaders");
        string folder = Path.Combine(Path.GetTempPath(),"anomaly-volume-transport-probe");
        Directory.CreateDirectory(folder);
        string provider = Path.Combine(folder,"medium.hlsli");
        File.WriteAllText(provider,"AnomalyMediumSample EvaluateMedium(float3 p) { AnomalyMediumSample m=AnomalyEmptyMedium(); m.extinction=.01; m.emission=float3(.02,.03,.04); m.velocity=float3(2,0,0); return m; }\n");
        var medium = new ClientPlugin.Backend.VolumeRegistryState.Medium("fixture",provider,Array.Empty<string>());
        GuestProgram Add(string name,string source,bool compute,int contract=1)
        {
            string path=Path.Combine(folder,name+".hlsl"); File.WriteAllText(path,source);
            GuestPassList.AddProgram("fixture.volume."+name,"AfterAtmosphere",path,"PublishOnly");
            var p=GuestPassList.Snapshot().Single(p=>p.Id=="fixture.volume."+name);
            p.Flags=compute?GuestBridgeClient.Compute:0; p.ResourceContract=contract;
            return p;
        }
        var light=Add("light",VolumetricShaderSource.Build(root,new[]{medium},"AnomalyVolumeLight.hlsl"),true);
        var inject=Add("inject",VolumetricShaderSource.Build(root,new[]{medium},"AnomalyVolumeInject.hlsl"),true);
        var integrate=Add("integrate",File.ReadAllText(Path.Combine(root,"AnomalyVolumeIntegrate.hlsl")),true);
        var reconstruct=Add("reconstruct",VolumetricShaderSource.Build(root,new[]{medium},"AnomalyVolumeReconstruct.hlsl"),false,2);
        var reactive=Add("reactive","#define VOLUME_REACTIVE 1\n"+File.ReadAllText(Path.Combine(root,"AnomalyVolumeTemporalCoverage.hlsl")),false);
        var transport=Add("transport",@"
cbuffer Frame:register(b6) { float4 FrameValues[24]; };
cbuffer Providers:register(b7) { float4 ProviderValues[80]; };
Texture3D<float> Input:register(t43);
StructuredBuffer<float4> Data:register(t24);
[[vk::image_format(""rgba16f"")]] RWTexture3D<float4> A:register(u0);
[[vk::image_format(""rgba16f"")]] RWTexture3D<float4> B:register(u1);
[[vk::image_format(""rgba16f"")]] RWTexture3D<float4> C:register(u2);
RWTexture3D<float> D:register(u3);
RWTexture2D<float> E:register(u4);
[numthreads(4,4,4)] void __compute_shader(uint3 p:SV_DispatchThreadID) {
 uint w,h,d; A.GetDimensions(w,h,d); if(any(p>=uint3(w,h,d))) return;
 float v=FrameValues[23].w+ProviderValues[79].w+Data[0].x+Input.Load(int4(p,0));
 A[p]=v; B[p]=v*2; C[p]=v*3; D[p]=v*4; if(p.z==0) E[p.xy]=v*5;
}",true);
        var compare=Add("compare","Texture2D<float> Shadow:register(t20); SamplerComparisonState Compare:register(s5); float __pixel_shader(float4 p:SV_Position):SV_Target { return Shadow.SampleCmpLevelZero(Compare,float2(.5,.5),.5); }",false);
        var products=new List<GuestProgram>{light,inject,integrate,reconstruct,reactive,transport,compare};
        if(packs) {
            string projects=Path.GetFullPath(Path.Combine(assets,"../.."));
            var fog=new ClientPlugin.Backend.VolumeRegistryState.Medium("fog",Path.Combine(projects,"FinalFrontier/Assets/Celestial/Atmosphere/Fog.hlsli"),Array.Empty<string>());
            var clouds=new ClientPlugin.Backend.VolumeRegistryState.Medium("clouds",Path.Combine(projects,"VolumetricClouds/Pack/Medium/Clouds.hlsli"),new[]{"shape","detail","weather"});
            int configuration=0;
            foreach(var media in new[]{new[]{fog},new[]{clouds},new[]{fog,clouds},new[]{clouds,fog}}) {
                foreach(string kernel in new[]{"AnomalyVolumeLight.hlsl","AnomalyVolumeInject.hlsl","AnomalyVolumeReconstruct.hlsl"}) {
                    bool pixel=kernel.Contains("Reconstruct");
                    var program=Add("providers"+configuration+"."+Path.GetFileNameWithoutExtension(kernel),VolumetricShaderSource.Build(root,media,kernel),!pixel,pixel?2:1);
                    program.IncludeDirectories=media.Select(m=>GuestVolumes.ProviderSourceRoot(m.ShaderFile)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    products.Add(program);
                }
                configuration++;
            }
        }
        GuestBridge.SupportedPacketFlags=(savedFlags | 511) & ~512;
        GuestCompiler.CompilePending(assets);
        Require(light.Status=="capability-missing" && light.PixelSpirv!=null,"older native advertised extended volume support");
        var compiledLight=light.PixelSpirv;
        GuestBridge.SupportedPacketFlags=savedFlags | 1023;
        GuestCompiler.CompilePending(assets);
        Require(ReferenceEquals(compiledLight,light.PixelSpirv),"capability recovery recompiled shared volume source");
        foreach(var p in products)
            Require(p.Pipeline!=0,"shared volume shader transport failed: "+p.Id+" "+p.Error);
        if(!NativeProbe.Enabled) { Console.WriteLine("PASS: production shared-volume kernels compile with extended resources"); return; }
        Require(NativeProbe.Supports(512),"extended guest capability missing");
        const int w=9,h=3,d=5;
        const ulong a=960,b=961,c=962,visibility=963,depth=964,source=965,grid=966,tau=967,previous=968,coefficients=969,integrated=970,color=971,motion=972,reject=973,scene=974,shadow=975,previousDepth=976;
        void Image(ulong id,int width,int height,int slices,int format,byte[] data=null)
        { NativeProbe.ImageCreate(id,width,height,slices,format); if(data!=null) NativeProbe.ImageUpdate(id,data); }
        foreach(ulong id in new[]{a,b,c,previous,coefficients,integrated}) Image(id,w,h,d,1);
        Image(visibility,w,h,d,2); Image(depth,w,h,1,2);
        Image(source,w,h,d,2); Image(grid,16,1,1,4,new byte[16]);
        Image(tau,4,4,8,8);
        Image(color,w,h,1,1); Image(motion,w,h,1,1); Image(reject,w,h,1,2);
        Image(scene,w,h,1,2,new byte[w*h*4]);
        Image(previousDepth,w,h,1,2,new byte[w*h*4]);
        Image(shadow,4,4,1,7,Enumerable.Repeat(BitConverter.GetBytes(1f),16).SelectMany(x=>x).ToArray());
        var layers=Enumerable.Range(0,3*2*2*4).Select(i=>(byte)(i*5)).ToArray();
        Image(978,3,2,2,5,layers);
        Require(NativeProbe.ReadRaw(978,layers.Length).SequenceEqual(layers),"base readback did not include every array layer");
        var input=new byte[w*h*d*4];
        for(int i=0;i<w*h*d;i++) Buffer.BlockCopy(BitConverter.GetBytes((float)i),0,input,i*4,4);
        NativeProbe.ImageUpdate(source,input);
        NativeProbe.ImageUpdate(grid,BitConverter.GetBytes(3f));
        var frame=new byte[384]; var providers=new byte[1280];
        void F(byte[] bytes,int at,float value)=>Buffer.BlockCopy(BitConverter.GetBytes(value),0,bytes,at,4);
        F(frame,380,7); F(providers,1276,11);
        var sampled=new ulong[48]; sampled[43]=source; sampled[24]=grid;
        var constants=new byte[8][]; constants[6]=frame; constants[7]=providers;
        NativeProbe.ResetQueue();
        void Reject(Action action,string message)
        {
            bool rejected=false;
            try { action(); } catch(Exception error) { rejected=error.Message.Contains(message); }
            Require(rejected,"invalid extended packet was not rejected: "+message);
        }
        Reject(()=>NativeProbe.Queue2(2,5,w,h,d,transport.Pipeline,new[]{a,b,c,visibility,depth},Array.Empty<byte[]>(),sampled),"constants incomplete");
        var missing=(ulong[])sampled.Clone(); missing[43]=0;
        Reject(()=>NativeProbe.Queue2(2,5,w,h,d,transport.Pipeline,new[]{a,b,c,visibility,depth},constants,missing),"t43");
        Reject(()=>NativeProbe.Queue2(2,5,w,h,d,transport.Pipeline,new[]{a,a,c,visibility,depth},constants,sampled),"duplicated");
        Reject(()=>NativeProbe.Queue2(2,5,w,h,d,transport.Pipeline,new[]{source,b,c,visibility,depth},constants,sampled),"format mismatch");
        var aliased=(ulong[])sampled.Clone(); aliased[43]=a;
        Reject(()=>NativeProbe.Queue2(2,5,w,h,d,transport.Pipeline,new[]{a,b,c,visibility,depth},constants,aliased),"aliases output");
        NativeProbe.Queue2(2,5,w,h,d,transport.Pipeline,new[]{a,b,c,visibility,depth},constants,sampled);
        // Native owns a byte copy; caller mutations cannot change queued work.
        F(frame,380,1000); F(providers,1276,1000);
        NativeProbe.Replay(2);
        for(int output=0;output<5;output++)
        {
            int count=output==4?w*h:w*h*d, stride=output<3?8:4;
            var bytes=NativeProbe.ReadRaw(new[]{a,b,c,visibility,depth}[output],count*stride);
            for(int i=0;i<count;i++)
            {
                float value=output<3?(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes,i*stride)):BitConverter.ToSingle(bytes,i*stride);
                Require(Math.Abs(value-(21+i)*(output+1))<.1,"extended XYZ/UAV/CB copy mismatch at output "+output+" cell "+i+": "+value);
            }
        }
        var beforeBatch=NativeProbe.ReadRaw(a,w*h*d*8);
        F(frame,380,31);F(providers,1276,11);
        object[] Transport(ulong[] inputs,byte[][] blocks)=>new object[]{2,5,w,h,d,transport.Pipeline,new[]{a,b,c,visibility,depth},blocks,inputs,0};
        var nativeBatch=new ClientPlugin.Rendering.Interop.NativeBackend();
        NativeProbe.ResetQueue();
        var compareInputs=new ulong[48];compareInputs[20]=shadow;
        NativeProbe.Queue2(2,5,w,h,1,compare.Pipeline,new[]{previousDepth},Array.Empty<byte[]>(),compareInputs);
        Reject(()=>nativeBatch.GuestSubmitBatch2(new[]{Transport(sampled,constants),Transport(missing,constants)}),"t43");
        NativeProbe.Replay(2);
        Require(beforeBatch.SequenceEqual(NativeProbe.ReadRaw(a,w*h*d*8)),"rejected native batch queued its valid prefix");
        var preservedWork=NativeProbe.ReadRaw(previousDepth,w*h*4);
        Require(Enumerable.Range(0,w*h).All(i=>BitConverter.ToSingle(preservedWork,i*4)==1),"rejected batch discarded previously accepted work");
        NativeProbe.ResetQueue();
        var oversized=new byte[8][];oversized[6]=new byte[4097];
        Reject(()=>nativeBatch.GuestSubmitBatch2(new[]{Transport(sampled,constants),Transport(sampled,oversized)}),"4096");
        NativeProbe.Replay(2);
        Require(beforeBatch.SequenceEqual(NativeProbe.ReadRaw(a,w*h*d*8)),"managed serialization failure queued its valid prefix");
        Require(nativeBatch.GuestSubmitBatch2(Array.Empty<object[]>()),"empty extended transaction rejected");
        Console.WriteLine("PASS: extended batch rejects invalid tails atomically and preserves earlier queued work");
        Image(visibility,w,h,d,8);
        Array.Clear(frame); Array.Clear(providers);
        void V(byte[] bytes,int at,float x,float y,float z,float q) { F(bytes,at,x);F(bytes,at+4,y);F(bytes,at+8,z);F(bytes,at+12,q); }
        frame=VolumeFrameData.Bytes(new VolumeFrameData.Frame {CameraToWorld=Matrix.Identity,CurrentProjection=Matrix.Identity,PreviousProjection=Matrix.Identity,LightToCamera=Matrix.Identity,
            Grid=new Vector4(w,h,d,100),Screen=new Vector4(w,h,1,1),Sun=new Vector4(0,1,0,100),SunEnergy=new Vector4(1,1,1,.1f),Ambient=new Vector4(.1f,.1f,.1f,0),
            Temporal=new Vector4(.016f,0,1,0),Control=new Vector4(1,0,4,8)});constants[6]=frame;
        V(providers,1088,-1000,-1000,-1000,0); V(providers,1152,1000,1000,1000,0);
        var shadows=new byte[224];
        for(int m=0;m<3;m++) for(int i=0;i<4;i++) F(shadows,m*64+i*20,1);
        V(shadows,192,128,1000,8000,1);
        constants[5]=shadows;
        NativeProbe.ImageUpdate(previous,new byte[w*h*d*8]); NativeProbe.ImageUpdate(coefficients,new byte[w*h*d*8]);
        var common=new ulong[48]; common[0]=scene;common[1]=tau;common[2]=previous;common[3]=coefficients;
        common[4]=integrated;common[5]=b;common[6]=a;common[7]=c;common[8]=visibility;common[9]=depth;
        common[20]=common[21]=common[22]=shadow;common[23]=common[24]=grid;
        ulong[] Inputs(params ulong[] outputs)=>common.Select(id=>outputs.Contains(id)?0:id).ToArray();
        NativeProbe.ResetQueue();
        var injectionInputs=Inputs(a,b,c,visibility,depth); injectionInputs[9]=previousDepth;
        var integration=new byte[32];
        Buffer.BlockCopy(BitConverter.GetBytes(w),0,integration,0,4); Buffer.BlockCopy(BitConverter.GetBytes(h),0,integration,4,4); Buffer.BlockCopy(BitConverter.GetBytes(d),0,integration,8,4);F(integration,12,100);F(integration,16,1);
        Require(nativeBatch.GuestSubmitBatch2(new[]{
            new object[]{2,5,4,4,1,light.Pipeline,new[]{tau},constants,Inputs(tau),0},
            new object[]{2,5,w,h,d,inject.Pipeline,new[]{a,b,c,visibility,depth},constants,injectionInputs,0},
            new object[]{2,5,w,h,1,integrate.Pipeline,new[]{integrated},new[]{integration},new[]{a,b},0},
            new object[]{2,5,w,h,1,reconstruct.Pipeline,new[]{color,motion},constants,common,0},
            new object[]{2,5,w,h,1,reactive.Pipeline,new[]{reject},Array.Empty<byte[]>(),new[]{motion},0}
        }),"shared volume batch was not accepted");
        NativeProbe.Replay(2);
        var optical=NativeProbe.ReadRaw(tau,4*4*8*2);
        for(int z=0;z<8;z++) for(int i=0;i<16;i++) Require(Math.Abs((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(optical,(z*16+i)*2))-(z+.5f)*.125f)<.001,"shared light optical depth differs");
        var pixels=NativeProbe.ReadRaw(color,w*h*8); var reactivePixels=NativeProbe.ReadRaw(reject,w*h*4);
        double transmittance=Math.Exp(-1),coverage=1-transmittance;
        for(int i=0;i<w*h;i++)
        {
            for(int channel=0;channel<4;channel++)
            {
                float actual=(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(pixels,i*8+channel*2));
                double expected=channel==3?transmittance:(channel+2)*coverage;
                Require(Math.Abs(actual-expected)<.012,"shared froxel emission/transmittance mismatch "+i+" channel "+channel+": "+actual+" expected "+expected);
            }
            double expectedReactive=Math.Max(coverage,2*Math.Min(coverage,1-coverage));
            Require(Math.Abs(BitConverter.ToSingle(reactivePixels,i*4)-expectedReactive)<.006,"shared volume reject coverage mismatch");
        }
        byte[] Structured<T>(T[] values,int count) where T:struct {
            var bytes=new byte[count*Marshal.SizeOf<T>()];var pin=GCHandle.Alloc(values,GCHandleType.Pinned);
            try { Marshal.Copy(pin.AddrOfPinnedObject(),bytes,0,bytes.Length);return bytes; } finally {pin.Free();}
        }
        var rooms=new VolumeInteriorData();
        foreach(bool sealedRoom in new[]{true,false}) {
            var roomCells=new[]{Vector3I.Zero};
            var records=sealedRoom?new[]{new VolumeInteriorData.Grid(1,1,MatrixD.CreateTranslation(-1e12,0,0),1000,roomCells,true)}:Array.Empty<VolumeInteriorData.Grid>();
            rooms.Build(records,records.Length,true,true,"controlled topology",new Vector3D(1e12,0,0));
            var rows=Structured(rooms.Rows,rooms.RowCount);var packedCells=Structured(rooms.Cells,rooms.Cells.Length);
            Image(grid,rows.Length,1,1,4,rows);Image(979,packedCells.Length,1,1,4,packedCells);
            common[23]=grid;common[24]=979;F(frame,372,rooms.GridCount);
            injectionInputs=Inputs(a,b,c,visibility,depth);injectionInputs[9]=previousDepth;
            NativeProbe.ResetQueue();
            NativeProbe.Queue2(2,5,4,4,1,light.Pipeline,new[]{tau},constants,Inputs(tau));
            NativeProbe.Queue2(2,5,w,h,d,inject.Pipeline,new[]{a,b,c,visibility,depth},constants,injectionInputs);
            NativeProbe.Queue2(2,5,w,h,1,integrate.Pipeline,new[]{integrated},new[]{integration},new[]{a,b});
            NativeProbe.Queue2(2,5,w,h,1,reconstruct.Pipeline,new[]{color,motion},constants,common);
            NativeProbe.Queue2(2,5,w,h,1,reactive.Pipeline,new[]{reject},Array.Empty<byte[]>(),new[]{motion});
            NativeProbe.Replay(2);
            var roomPixels=NativeProbe.ReadRaw(color,w*h*8);var roomReactive=NativeProbe.ReadRaw(reject,w*h*4);
            for(int i=0;i<w*h;i++) for(int channel=0;channel<4;channel++) {
                float actual=(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(roomPixels,i*8+channel*2));
                double expected=sealedRoom?(channel==3?1:0):channel==3?transmittance:(channel+2)*coverage;
                Require(Math.Abs(actual-expected)<.012,"shared sealed-room exclusion/open recovery mismatch");
                if(sealedRoom) Require(BitConverter.ToSingle(roomReactive,i*4)==0,"sealed room retained volume rejection");
            }
        }
        var shadowInputs=new ulong[48]; shadowInputs[20]=shadow;
        foreach(float comparisonDepth in new[]{1f,0f}) {
            NativeProbe.ImageUpdate(shadow,Enumerable.Repeat(BitConverter.GetBytes(comparisonDepth),16).SelectMany(x=>x).ToArray());
            NativeProbe.ResetQueue(); NativeProbe.Queue2(2,5,w,h,1,compare.Pipeline,new[]{reject},Array.Empty<byte[]>(),shadowInputs); NativeProbe.Replay(2);
            var comparison=NativeProbe.ReadRaw(reject,w*h*4);
            for(int i=0;i<w*h;i++) Require(BitConverter.ToSingle(comparison,i*4)==comparisonDepth,"depth comparison sampler did not follow volume shadow contract");
        }
        Console.WriteLine("GPU PASS: extended 3D/five-UAV/copied-CB transport, resource rejection, shadow comparison, shared sealed-room packing/open recovery and production shared volume kernels");
        }
        finally { GuestBridge.SupportedPacketFlags=savedFlags; }
    }
}

static partial class NativeProbe
{
    public static ulong Pipeline2(byte[] vs,byte[] ps,string ve,string pe,int contract)
    {
        ulong id=new ClientPlugin.Rendering.Interop.NativeBackend().GuestCreatePipeline2(vs,ps,ve,pe,contract);
        if(id==0) throw new Exception("extended native pipeline rejected");
        return id;
    }
    public static void Queue2(int mark,int compose,int width,int height,int depth,ulong pipeline,ulong[] outputs,byte[][] constants,ulong[] sampled,int flags=0)
    {
        if(!new ClientPlugin.Rendering.Interop.NativeBackend().GuestSubmit2(mark,compose,width,height,depth,pipeline,outputs,constants,sampled,flags))
            throw new Exception("extended native bridge unavailable");
    }
}

// Supplies only the loader hooks for the production NativeBackend partial.
// Its pipeline creation, pointer pinning and 568-byte serializer run unchanged.
namespace ClientPlugin.Rendering.Interop
{
    public sealed partial class NativeBackend
    {
        IntPtr context => NativeProbe.Context;
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint GuestFeaturesFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GuestSubmitFn(IntPtr context,IntPtr packets,uint count);
        static GuestFeaturesFn guestFeatures;
        static IntPtr GetSymbol(string name)=>NativeProbe.Symbol(name);
        static void EnsureGuest() => guestFeatures ??= Marshal.GetDelegateForFunctionPointer<GuestFeaturesFn>(GetSymbol("sevk_guest_features"));
        static void Check(int rc,IntPtr _) => NativeProbe.Check(rc);
    }
}
