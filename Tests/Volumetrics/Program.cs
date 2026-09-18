using System;
using System.IO;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.D3DCompiler;
using ClientPlugin.Shaders;
using ClientPlugin.ShaderFramework;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;
namespace VRageRender { static class MyRender11 { internal static Device DeviceInstance; } }
static class Program
{
    static int checks;
    static void Check(bool value,string message) { if(!value) throw new Exception(message); checks++; }
    static void Near(double a,double b,double tolerance,string label) => Check(Math.Abs(a-b)<=tolerance,label+": "+a+" != "+b);
    static void Main(string[] args)
    {
        string shaders=Path.GetFullPath(args[0]);
        string helper=Path.Combine(shaders,"AnomalyMedium.hlsli");
        if(args.Length>=3) TestProductionShaders(shaders,Path.GetFullPath(args[1]),Path.GetFullPath(args[2]));
        Check(VolumetricMediumRegistry.Register("test",helper,Array.Empty<string>()),"register");
        VolumetricMediumRegistry.SetEnabled("test",true);
        VolumetricMediumRegistry.Capture(out var rev);
        Check(!VolumetricMediumRegistry.Commit(3,rev,true,true,true,true),"uninitialized provider stays legacy");
        float[] parameters={1}; double[] bounds={-10,-10,-10,10,10,10}; float[] motion={0,0,0};
        VolumetricMediumRegistry.SetParameters("test",parameters,bounds,motion);
        parameters[0]=9; bounds[0]=-99; motion[0]=9;
        var captured=VolumetricMediumRegistry.Capture(out rev);
        Check(captured[0].Uniforms[0]==1 && captured[0].Bounds[0]==-10 && captured[0].Motion[0]==0,"copy isolation");
        Check(!VolumetricMediumRegistry.Commit(3,rev,true,false,true,true),"missing shadows refuse ownership");
        Check(!VolumetricMediumRegistry.Commit(3,rev,true,true,true,false),"missing temporal inputs refuse ownership");
        Check(VolumetricMediumRegistry.Commit(3,rev,true,true,true,true),"complete transaction");
        Check(VolumetricMediumRegistry.OwnsNearInterval("test",3),"same frame owns");
        Check(!VolumetricMediumRegistry.OwnsNearInterval("test",4),"stale frame cannot own");
        VolumetricMediumRegistry.SetEnabled("test",false);
        Check(VolumetricMediumRegistry.OwnsNearInterval("test",3),"committed frame owns immutable interval until next frame");
        VolumetricMediumRegistry.ResetIntervals();
        Check(!VolumetricMediumRegistry.OwnsNearInterval("test",3),"next frame resets interval");
        Check(!VolumetricMediumRegistry.Commit(3,rev,true,true,true,true),"stale revision fails");
        VolumetricMediumRegistry.Configure(1,8000,0);
        VolumetricMediumRegistry.Capture(out _,out long oldHistory,out int frozenQuality,out float frozenDistance,out int frozenDebug);
        VolumetricMediumRegistry.Configure(0,1500,2);
        Check(frozenQuality==1 && frozenDistance==8000 && frozenDebug==0,"frame configuration is immutable");
        Check(VolumetricMediumRegistry.HistoryRevision!=oldHistory,"configuration change invalidates following frame history");
        VolumetricMediumRegistry.Configure(1,8000,0);
        VolumetricMediumRegistry.Unregister("test");
        using(var d=new Device(DriverType.Warp,DeviceCreationFlags.None))
        {
            VRageRender.MyRender11.DeviceInstance=d;
            using(var resources=new VolumetricFrameResources())
            {
                resources.Ensure(65,33,8,96);
                Check(resources.Width==9 && resources.Height==5 && resources.Slices==96,"ceil dimensions");
                Check(resources.History.Texture.Description.Format==SharpDX.DXGI.Format.R16G16B16A16_Float,"float history");
                var epoch=resources.Epoch; resources.Publish(7);
                Check(resources.Matches(7,epoch) && !resources.Matches(8,epoch),"frame validity");
                resources.BeginFrame(); Check(!resources.Matches(7,epoch),"begin invalidates");
                resources.Ensure(128,64,8,96);
                Check(resources.Epoch!=epoch && !resources.Ready,"resize invalidates");
                resources.Dispose(); resources.Ensure(64,64,8,96);
                Check(resources.Coefficients!=null && !resources.Ready,"resource recreation");
            }
            using(var timer=new VolumetricGpuTimer())
            using(var deferred=new DeviceContext(d))
            {
                Check(timer.Begin(deferred),"deferred GPU timestamp begins without GetData");
                timer.End(deferred);
                using(var commands=deferred.FinishCommandList(false)) d.ImmediateContext.ExecuteCommandList(commands,false);
                d.ImmediateContext.Flush();
                var deadline=System.Diagnostics.Stopwatch.StartNew();
                while(timer.Measurements==0 && deadline.ElapsedMilliseconds<5000) { timer.Poll(d.ImmediateContext); System.Threading.Thread.Sleep(1); }
                Check(timer.Measurements==1 && timer.LastMilliseconds>=0 && !double.IsNaN(timer.LastMilliseconds),"deferred GPU timestamp consumed on immediate context");
            }
            PipelineRegression.Run(d,shaders,Check);
            TestIntegration(d,shaders,helper);
            TestInteriors(d,shaders);
            TestShadowVisibility(d,shaders);
            string source=File.ReadAllText(helper)+@"
RWStructuredBuffer<float4> Result : register(u0);
[numthreads(1,1,1)] void main(uint3 id:SV_DispatchThreadID)
{
    uint test=id.x; float3 L=0; float T=1;
    if(test==0) AnomalyIntegrateMedium(.01,float3(.02,.01,.005),100,L,T);
    if(test==1) AnomalyIntegrateMedium(0,0,8000,L,T);
    if(test==2) { for(uint i=0;i<96;i++) AnomalyIntegrateMedium(.01,float3(.02,.01,.005),100.0/96,L,T); }
    if(test==3 || test==4 || test==5)
    {
        AnomalyMediumSample a=AnomalyEmptyMedium(),b=AnomalyEmptyMedium();
        a.extinction=.01; a.scattering=.008; a.anisotropy=.8;
        b.extinction=.02; b.scattering=float3(.01,.008,.006); b.anisotropy=-.2;
        float sigma=0; float3 Q=0,V=0; float visibility=test==5?0:1;
        if(test==4) { AnomalyAccumulateMedium(b,.75,visibility,10,0,sigma,Q,V); AnomalyAccumulateMedium(a,.75,visibility,10,0,sigma,Q,V); }
        else { AnomalyAccumulateMedium(a,.75,visibility,10,0,sigma,Q,V); AnomalyAccumulateMedium(b,.75,visibility,10,0,sigma,Q,V); }
        AnomalyIntegrateMedium(sigma,Q,100,L,T);
    }
    if(test==6) AnomalyIntegrateMedium(1e-10,1e-10,100,L,T);
    if(test==7) AnomalyIntegrateMedium(1000,10000,8000,L,T);
    if(test==8) { L=float3(AnomalyVolumeSliceDistance(0,96,1,8000),AnomalyVolumeSliceDistance(96,96,1,8000),AnomalyVolumeSliceDistance(48,96,1,8000)); }
    Result[test]=float4(L,T);
}";
            using(var code=ShaderBytecode.Compile(source,"main","cs_5_0"))
            using(var shader=new ComputeShader(d,code))
            using(var output=new Buffer(d,new BufferDescription{SizeInBytes=9*16,StructureByteStride=16,BindFlags=BindFlags.UnorderedAccess,OptionFlags=ResourceOptionFlags.BufferStructured,Usage=ResourceUsage.Default}))
            using(var uav=new UnorderedAccessView(d,output))
            using(var stage=new Buffer(d,new BufferDescription{SizeInBytes=9*16,CpuAccessFlags=CpuAccessFlags.Read,Usage=ResourceUsage.Staging}))
            {
                var c=d.ImmediateContext; c.ComputeShader.Set(shader); c.ComputeShader.SetUnorderedAccessView(0,uav); c.Dispatch(9,1,1); c.ComputeShader.SetUnorderedAccessView(0,null); c.CopyResource(output,stage);
                DataStream stream; c.MapSubresource(stage,MapMode.Read,MapFlags.None,out stream);
                var v=new float[36]; try { stream.ReadRange(v,0,v.Length); } finally { c.UnmapSubresource(stage,0); stream.Dispose(); }
                Near(v[3],Math.Exp(-1),1e-6,"Beer Lambert");
                Near(v[0],2*(1-Math.Exp(-1)),2e-6,"analytic scattering");
                Check(v[4]==0 && v[5]==0 && v[6]==0 && v[7]==1,"zero density no op");
                for(int j=0;j<4;j++) Near(v[j],v[8+j],2e-5,"subdivision invariance");
                for(int j=0;j<4;j++) Near(v[12+j],v[16+j],1e-6,"overlap order independent");
                Near(v[15],Math.Exp(-3),1e-6,"overlap no duplicate extinction");
                Check(v[20]==0 && v[21]==0 && v[22]==0,"blocked samples have no direct light");
                Near(v[24],1e-8,1e-12,"tiny density Taylor limit");
                Near(v[28],10,1e-5,"dense HDR finite");
                Near(v[32],0,1e-6,"slice near"); Near(v[33],8000,.02,"slice far");
                foreach(var x in v) Check(!float.IsNaN(x) && !float.IsInfinity(x),"finite output");
            }
        }
        Console.WriteLine("PASS "+checks+" shared-volume contract, resource lifecycle and actual HLSL/WARP checks. Geometry scenes and timing are NOT established by these tests.");
    }
    static void TestIntegration(Device d,string shaders,string helper)
    {
        string source=File.ReadAllText(Path.Combine(shaders,"AnomalyVolumeIntegrate.hlsl"))
            .Replace("#include \"AnomalyMedium.hlsli\"",File.ReadAllText(helper));
        using(var resources=new VolumetricFrameResources())
        using(var code=ShaderBytecode.Compile(source,"__compute_shader","cs_5_0"))
        using(var shader=new ComputeShader(d,code))
        {
            resources.Ensure(16,16,8,96);
            var c=d.ImmediateContext;
            c.ClearUnorderedAccessView(resources.Coefficients.Uav,new SharpDX.Mathematics.Interop.RawVector4(0,0,0,.000125f));
            c.ClearUnorderedAccessView(resources.Source.Uav,new SharpDX.Mathematics.Interop.RawVector4(.00025f,.000125f,0,0));
            // uint3 dimensions then far/near distances, padded to two float4s.
            var bytes=new byte[32];
            System.Buffer.BlockCopy(new int[]{2,2,96},0,bytes,0,12);
            System.Buffer.BlockCopy(new float[]{8000,1},0,bytes,12,8);
            using(var stream=DataStream.Create(bytes,true,false))
            using(var cb=new Buffer(d,stream,new BufferDescription{SizeInBytes=32,BindFlags=BindFlags.ConstantBuffer,Usage=ResourceUsage.Immutable}))
            {
                c.ComputeShader.Set(shader); c.ComputeShader.SetConstantBuffer(0,cb);
                c.ComputeShader.SetShaderResource(0,resources.Coefficients.Srv);
                c.ComputeShader.SetShaderResource(1,resources.Source.Srv);
                c.ComputeShader.SetUnorderedAccessView(0,resources.ScatteringTransmittance.Uav);
                c.Dispatch(1,1,1);
                c.ComputeShader.SetUnorderedAccessView(0,null);
                c.ComputeShader.SetShaderResources(0,new ShaderResourceView[2]);
                var description=resources.ScatteringTransmittance.Texture.Description;
                description.Usage=ResourceUsage.Staging; description.CpuAccessFlags=CpuAccessFlags.Read; description.BindFlags=BindFlags.None;
                using(var staging=new Texture3D(d,description))
                {
                    c.CopyResource(resources.ScatteringTransmittance.Texture,staging);
                    var mapped=c.MapSubresource(staging,0,MapMode.Read,MapFlags.None);
                    try
                    {
                        var result=new byte[8];
                        System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(mapped.DataPointer,mapped.SlicePitch*95),result,0,8);
                        Near(Half(result,0),2*(1-Math.Exp(-1)),.003,"3D integration accumulated radiance");
                        Near(Half(result,6),Math.Exp(-1),.001,"3D integration accumulated transmittance");
                    }
                    finally { c.UnmapSubresource(staging,0); }
                }
            }
        }
    }
    static double Half(byte[] bytes,int offset)
    {
        int h=BitConverter.ToUInt16(bytes,offset),e=(h>>10)&31,m=h&1023;
        return (e==0?m*Math.Pow(2,-24):(1+m/1024.0)*Math.Pow(2,e-15))*((h&32768)==0?1:-1);
    }
    static void TestInteriors(Device d,string shaders)
    {
        string source=File.ReadAllText(Path.Combine(shaders,"AnomalyVolumeInteriors.hlsli"))+@"
RWStructuredBuffer<float4> R:register(u0);
[numthreads(1,1,1)] void main(uint3 id:SV_DispatchThreadID) {
    float3 p=id.x==0?float3(100,0,0):id.x==1?float3(101,0,0):id.x==2?float3(103,0,0):float3(0,0,0);
    R[id.x]=AnomalyInsideSealedRoom(p,1)?1:0;
}";
        // A rotated grid at x=100: world X maps to local Y; exactly one sealed cell.
        float[] grids={0,1,0,0,-1,0,0,0,0,0,1,0,0,-100,0,1, 2.5f,0,1,1, 0,0,0,0,0,0,0,0};
        using(var gs=DataStream.Create(grids,true,false))
        using(var cs=DataStream.Create(new int[]{0,0,0,0},true,false))
        using(var gb=new Buffer(d,gs,new BufferDescription{SizeInBytes=grids.Length*4,StructureByteStride=16,BindFlags=BindFlags.ShaderResource,OptionFlags=ResourceOptionFlags.BufferStructured,Usage=ResourceUsage.Immutable}))
        using(var cells=new Buffer(d,cs,new BufferDescription{SizeInBytes=16,StructureByteStride=16,BindFlags=BindFlags.ShaderResource,OptionFlags=ResourceOptionFlags.BufferStructured,Usage=ResourceUsage.Immutable}))
        using(var gsv=new ShaderResourceView(d,gb))
        using(var csv=new ShaderResourceView(d,cells))
        using(var code=ShaderBytecode.Compile(source,"main","cs_5_0"))
        using(var shader=new ComputeShader(d,code))
        using(var output=new Buffer(d,new BufferDescription{SizeInBytes=64,StructureByteStride=16,BindFlags=BindFlags.UnorderedAccess,OptionFlags=ResourceOptionFlags.BufferStructured,Usage=ResourceUsage.Default}))
        using(var uav=new UnorderedAccessView(d,output))
        using(var stage=new Buffer(d,new BufferDescription{SizeInBytes=64,CpuAccessFlags=CpuAccessFlags.Read,Usage=ResourceUsage.Staging}))
        {
            var c=d.ImmediateContext; c.ComputeShader.Set(shader); c.ComputeShader.SetShaderResources(23,gsv,csv); c.ComputeShader.SetUnorderedAccessView(0,uav); c.Dispatch(4,1,1);
            c.ComputeShader.SetUnorderedAccessView(0,null); c.ComputeShader.SetShaderResources(23,new ShaderResourceView[2]); c.CopyResource(output,stage);
            DataStream stream; c.MapSubresource(stage,MapMode.Read,MapFlags.None,out stream);
            try { var values=stream.ReadRange<float>(16); Check(values[0]==1 && values[4]==1 && values[8]==0 && values[12]==0,"transformed room mask inside/outside cells"); }
            finally { c.UnmapSubresource(stage,0); stream.Dispose(); }
        }
    }

    static void TestShadowVisibility(Device d,string shaders)
    {
        string source=File.ReadAllText(Path.Combine(shaders,"AnomalyVolumeShadows.hlsli"))+@"
RWStructuredBuffer<float4> R:register(u0);
[numthreads(1,1,1)] void main(uint3 id:SV_DispatchThreadID) {
    R[0]=float4(AnomalyVolumeGeometryVisibility(float3(0,0,.500001),float3(0,1,0)),
        AnomalyVolumeGeometryVisibility(float3(0,0,.25),float3(0,1,0)),
        AnomalyPlanetSunVisibility(float3(0,2,0),float3(0,-1,0)),
        AnomalyPlanetSunVisibility(float3(0,2,0),float3(0,1,0)));
}";
        var cbValues=new float[56];
        for(int i=0;i<3;i++) for(int j=0;j<4;j++) cbValues[i*16+j*5]=1;
        cbValues[48]=128;cbValues[49]=1000;cbValues[50]=8000;cbValues[51]=1;
        // Planet far from tested shadow receiver, with radius 1. Separate
        // planet cases use the CPU expectations below after moving its center.
        cbValues[52]=0;cbValues[53]=0;cbValues[54]=0;cbValues[55]=1;
        // Receiver points must be outside the opaque sphere for map comparisons.
        source=source.Replace("float3(0,0,.500001)","float3(0,2,.500001)").Replace("float3(0,0,.25)","float3(0,2,.25)");
        // Map Y is centered on the receiver (world Y=2).
        for(int i=0;i<3;i++) cbValues[i*16+13]=-2;
        using(var cbs=DataStream.Create(cbValues,true,false))
        using(var cb=new Buffer(d,cbs,new BufferDescription{SizeInBytes=224,BindFlags=BindFlags.ConstantBuffer,Usage=ResourceUsage.Immutable}))
        using(var pixels=DataStream.Create(new float[]{.5f,.5f,.5f,.5f},true,false))
        using(var texture=new Texture2D(d,new Texture2DDescription{Width=2,Height=2,MipLevels=1,ArraySize=1,Format=SharpDX.DXGI.Format.R32_Float,SampleDescription=new SharpDX.DXGI.SampleDescription(1,0),BindFlags=BindFlags.ShaderResource,Usage=ResourceUsage.Immutable},new DataRectangle(pixels.DataPointer,8)))
        using(var srv=new ShaderResourceView(d,texture))
        using(var sampler=new SamplerState(d,new SamplerStateDescription{Filter=Filter.ComparisonMinMagLinearMipPoint,AddressU=TextureAddressMode.Clamp,AddressV=TextureAddressMode.Clamp,AddressW=TextureAddressMode.Clamp,ComparisonFunction=Comparison.LessEqual,MaximumLod=float.MaxValue}))
        using(var code=ShaderBytecode.Compile(source,"main","cs_5_0"))
        using(var shader=new ComputeShader(d,code))
        using(var output=new Buffer(d,new BufferDescription{SizeInBytes=16,StructureByteStride=16,BindFlags=BindFlags.UnorderedAccess,OptionFlags=ResourceOptionFlags.BufferStructured,Usage=ResourceUsage.Default}))
        using(var uav=new UnorderedAccessView(d,output))
        using(var stage=new Buffer(d,new BufferDescription{SizeInBytes=16,CpuAccessFlags=CpuAccessFlags.Read,Usage=ResourceUsage.Staging}))
        {
            var c=d.ImmediateContext; c.ComputeShader.Set(shader); c.ComputeShader.SetConstantBuffer(8,cb);
            c.ComputeShader.SetShaderResources(20,srv,srv,srv); c.ComputeShader.SetSampler(5,sampler);
            c.ComputeShader.SetUnorderedAccessView(0,uav); c.Dispatch(1,1,1); c.ComputeShader.SetUnorderedAccessView(0,null);
            c.ComputeShader.SetShaderResources(20,new ShaderResourceView[3]); c.CopyResource(output,stage);
            DataStream stream; c.MapSubresource(stage,MapMode.Read,MapFlags.None,out stream);
            try { var v=stream.ReadRange<float>(4); Check(v[0]==0,"receiver just behind blocker stays blocked with metre-scale bias"); Check(v[1]==1,"receiver before blocker is lit"); Check(v[2]==0 && v[3]==1,"planetary night/day occultation"); }
            finally { c.UnmapSubresource(stage,0); stream.Dispose(); }
        }
    }

    static void TestProductionShaders(string root,string fog,string clouds)
    {
        var a=new VolumetricMediumRegistry.Medium("fog",fog,Array.Empty<string>());
        var b=new VolumetricMediumRegistry.Medium("clouds",clouds,new[]{"shape","detail","weather"});
        foreach(var media in new[]{new[]{a},new[]{b},new[]{a,b},new[]{b,a}})
        foreach(var kernel in new[]{"AnomalyVolumeLight.hlsl","AnomalyVolumeInject.hlsl","AnomalyVolumeReconstruct.hlsl"})
        {
            string source=VolumetricShaderSource.Build(root,media,kernel);
            bool pixel=kernel.Contains("Reconstruct");
            using(var include=new VolumeShaderInclude(root))
            using(var code=ShaderBytecode.Compile(source,pixel?"__pixel_shader":"__compute_shader",pixel?"ps_5_0":"cs_5_0",ShaderFlags.OptimizationLevel3,EffectFlags.None,null,include))
                Check(code.Bytecode.Data.Length>0,"production shader "+kernel);
        }
    }

}
