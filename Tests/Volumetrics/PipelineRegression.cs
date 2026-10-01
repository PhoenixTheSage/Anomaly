using System;
using System.IO;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.D3DCompiler;
using SharpDX.DXGI;
using ClientPlugin.ShaderFramework;
using ClientPlugin.Shaders;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;

// Executes the actual injection kernel. Shader compilation alone cannot detect
// mismatched bindings, invalid reprojection or history lighting a blocked sample.
static class PipelineRegression
{
    static Buffer Constants(Device d,float[] data)
    {
        using(var stream=DataStream.Create(data,true,false))
            return new Buffer(d,stream,new BufferDescription {SizeInBytes=data.Length*4,BindFlags=BindFlags.ConstantBuffer,Usage=ResourceUsage.Default});
    }
    static void Update(DeviceContext c,Buffer buffer,float[] data)
    { using(var stream=DataStream.Create(data,true,false)) c.UpdateSubresource(new DataBox(stream.DataPointer,0,0),buffer,0); }
    static Texture2D Texture(Device d) => new Texture2D(d,new Texture2DDescription {
        Width=1,Height=1,MipLevels=1,ArraySize=1,Format=Format.R32_Float,
        SampleDescription=new SampleDescription(1,0),BindFlags=BindFlags.RenderTarget|BindFlags.ShaderResource,Usage=ResourceUsage.Default});
    static void Clear(DeviceContext c,RenderTargetView r,float value)
        => c.ClearRenderTargetView(r,new SharpDX.Mathematics.Interop.RawColor4(value,value,value,value));
    static readonly float[] Identity={1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1};
    static readonly float[] Projection={1,0,0,0,0,1,0,0,0,0,-1.001001f,-1,0,0,-1.001001f,0};
    static void Matrix(float[] values,int offset,float[] m) => Array.Copy(m,0,values,offset,16);
    static float[] Read(DeviceContext c,Texture3D texture)
    {
        var desc=texture.Description;desc.BindFlags=BindFlags.None;desc.Usage=ResourceUsage.Staging;desc.CpuAccessFlags=CpuAccessFlags.Read;
        using(var staging=new Texture3D(texture.Device,desc)) {
            c.CopyResource(texture,staging);var map=c.MapSubresource(staging,0,MapMode.Read,SharpDX.Direct3D11.MapFlags.None);
            try {
                var bytes=new byte[8];Marshal.Copy(IntPtr.Add(map.DataPointer,map.SlicePitch*8),bytes,0,8);
                var result=new float[4];
                for(int i=0;i<4;i++) { int h=BitConverter.ToUInt16(bytes,i*2),e=(h>>10)&31,m=h&1023;
                    result[i]=(float)((e==0?m*Math.Pow(2,-24):e==31?double.NaN:(1+m/1024.0)*Math.Pow(2,e-15))*((h&32768)==0?1:-1)); }
                return result;
            } finally {c.UnmapSubresource(staging,0);}
        }
    }
    internal static void Run(Device d,string root,Action<bool,string> check)
    {
        string provider=Path.Combine(Path.GetTempPath(),"anomaly-volume-test-"+Guid.NewGuid().ToString("N")+".hlsli");
        File.WriteAllText(provider,@"AnomalyMediumSample EvaluateMedium(float3 p) {
            AnomalyMediumSample m=AnomalyEmptyMedium();
            if(MediumUniforms[0].z>0 && p.x<MediumUniforms[0].w) return m;
            m.extinction=MediumUniforms[0].x;
            m.scattering=m.extinction;m.anisotropy=MediumUniforms[0].y;m.velocity=MediumVelocity;return m; }");
        try {
            var medium=new VolumetricMediumRegistry.Medium("fixture",provider,Array.Empty<string>());
            string source=VolumetricShaderSource.Build(root,new[]{medium},"AnomalyVolumeInject.hlsl");
            var frame=new float[96];
            Matrix(frame,0,Identity);Matrix(frame,16,Identity);
            Matrix(frame,32,Projection);Matrix(frame,48,Identity);
            frame[64]=frame[65]=1;frame[66]=16;frame[67]=100;
            frame[68]=frame[69]=8;frame[70]=frame[71]=1;
            frame[74]=1;frame[75]=100;
            frame[76]=frame[77]=frame[78]=1;frame[79]=1;
            frame[84]=.016f;frame[92]=1;frame[94]=1;frame[95]=16;
            var uniforms=new float[320];uniforms[0]=.001f;
            for(int i=0;i<3;i++) {uniforms[272+i]=-1000;uniforms[288+i]=1000;}
            var shadows=new float[56];var sm=(float[])Identity.Clone();sm[0]=sm[5]=.001f;sm[10]=0;sm[14]=.5f;
            for(int i=0;i<3;i++) Matrix(shadows,i*16,sm);
            shadows[48]=128;shadows[49]=1000;shadows[50]=8000;shadows[51]=1;
            using(var include=new VolumeShaderInclude(root))
            using(var code=ShaderBytecode.Compile(source,"__compute_shader","cs_5_0",ShaderFlags.OptimizationLevel3,EffectFlags.None,null,include))
            using(var shader=new ComputeShader(d,code))
            using(var volume=new VolumetricFrameResources())
            using(var oldCoefficients=new VolumetricFrameResources.Volume(1,1,16,Format.R16G16B16A16_Float))
            using(var light=new VolumetricFrameResources.Volume(1,1,16,Format.R16_Float))
            using(var depth=Texture(d)) using(var depthSrv=new ShaderResourceView(d,depth)) using(var depthRtv=new RenderTargetView(d,depth))
            using(var oldDepth=Texture(d)) using(var oldDepthSrv=new ShaderResourceView(d,oldDepth)) using(var oldDepthRtv=new RenderTargetView(d,oldDepth))
            using(var shadow=Texture(d)) using(var shadowSrv=new ShaderResourceView(d,shadow)) using(var shadowRtv=new RenderTargetView(d,shadow))
            using(var cb=Constants(d,frame)) using(var providers=Constants(d,uniforms)) using(var shadowCb=Constants(d,shadows))
            using(var linear=new SamplerState(d,new SamplerStateDescription {Filter=Filter.MinMagMipLinear,AddressU=TextureAddressMode.Clamp,AddressV=TextureAddressMode.Clamp,AddressW=TextureAddressMode.Clamp,MaximumLod=float.MaxValue}))
            using(var compare=new SamplerState(d,new SamplerStateDescription {Filter=Filter.ComparisonMinMagLinearMipPoint,AddressU=TextureAddressMode.Clamp,AddressV=TextureAddressMode.Clamp,AddressW=TextureAddressMode.Clamp,ComparisonFunction=Comparison.LessEqual,MaximumLod=float.MaxValue}))
            {
                volume.Ensure(8,8,8,16);var c=d.ImmediateContext;
                Clear(c,depthRtv,0);Clear(c,oldDepthRtv,100);Clear(c,shadowRtv,1);
                c.ClearUnorderedAccessView(light.Uav,new SharpDX.Mathematics.Interop.RawVector4(0,0,0,0));
                c.ClearUnorderedAccessView(oldCoefficients.Uav,new SharpDX.Mathematics.Interop.RawVector4(0,0,0,.001f));
                float expected=.001f/(4*(float)Math.PI);
                c.ClearUnorderedAccessView(volume.History.Uav,new SharpDX.Mathematics.Interop.RawVector4(expected*1.1f,expected*1.1f,expected*1.1f,1));
                Action dispatch=()=> {
                    c.ClearState();Update(c,cb,frame);Update(c,providers,uniforms);
                    c.ComputeShader.Set(shader);c.ComputeShader.SetConstantBuffer(6,cb);c.ComputeShader.SetConstantBuffer(7,providers);c.ComputeShader.SetConstantBuffer(5,shadowCb);
                    c.ComputeShader.SetShaderResources(0,depthSrv,light.Srv,volume.History.Srv,oldCoefficients.Srv);
                    c.ComputeShader.SetShaderResource(9,oldDepthSrv);c.ComputeShader.SetShaderResources(20,shadowSrv,shadowSrv,shadowSrv);
                    c.ComputeShader.SetSampler(0,linear);c.ComputeShader.SetSampler(2,linear);c.ComputeShader.SetSampler(5,compare);
                    c.ComputeShader.SetUnorderedAccessViews(0,volume.Coefficients.Uav,volume.Source.Uav,volume.Motion.Uav,volume.SunVisibility.Uav,volume.DepthUav);
                    c.Dispatch(1,1,4);c.ClearState();
                };
                dispatch();var lit=Read(c,volume.Source.Texture);
                check(Math.Abs(lit[0]-expected)<1e-7 && lit[3]==1,"production injection sun source and visibility");
                check(Read(c,volume.Motion.Texture)[3]==1,"first frame rejects history");
                frame[85]=1;dispatch();
                check(Math.Abs(Read(c,volume.Source.Texture)[0]-expected*1.075)<2e-7,"stable volume reuses bounded history");
                check(Read(c,volume.Motion.Texture)[3]<.02,"stable volume confidence");
                Clear(c,oldDepthRtv,0);dispatch();
                check(Read(c,volume.Motion.Texture)[3]==1,"disoccluded volume rejects history");
                Clear(c,oldDepthRtv,100);uniforms[0]=.002f;dispatch();
                check(Read(c,volume.Motion.Texture)[3]==1,"density change rejects history");
                uniforms[0]=.001f;Clear(c,shadowRtv,0);dispatch();
                check(Read(c,volume.Source.Texture)[0]==0,"blocked injection does not reuse lit history");
                c.ClearUnorderedAccessView(volume.History.Uav,new SharpDX.Mathematics.Interop.RawVector4(1,1,1,0));dispatch();
                check(Read(c,volume.Source.Texture)[0]==0,"dark current sample stays dark even with matching visibility history");
                Clear(c,shadowRtv,1);Matrix(frame,32,Projection);frame[42]*=-1;frame[43]*=-1;dispatch();
                check(Read(c,volume.Motion.Texture)[3]==1,"camera turn behind previous camera rejects history");
                uniforms[1]=float.NaN;dispatch();
                check(Read(c,volume.Coefficients.Texture)[3]==0 && Read(c,volume.Source.Texture)[0]==0,"nonfinite provider phase cannot poison volume");
                uniforms[1]=0;uniforms[304]=float.PositiveInfinity;dispatch();
                check(Read(c,volume.Coefficients.Texture)[3]==0,"nonfinite provider velocity rejected");
                uniforms[304]=0;uniforms[0]=0;dispatch();
                check(Read(c,volume.Source.Texture)[0]==0 && Read(c,volume.Coefficients.Texture)[3]==0,"zero-density injection remains a no-op");

                string lightSource=VolumetricShaderSource.Build(root,new[]{medium},"AnomalyVolumeLight.hlsl");
                using(var li=new VolumeShaderInclude(root))
                using(var lb=ShaderBytecode.Compile(lightSource,"__compute_shader","cs_5_0",ShaderFlags.OptimizationLevel3,EffectFlags.None,null,li))
                using(var ls=new ComputeShader(d,lb))
                using(var field=new VolumetricFrameResources.Volume(2,2,16,Format.R16_Float))
                {
                    Func<int,double> readTau=x=> {
                        var desc=field.Texture.Description;desc.BindFlags=BindFlags.None;desc.Usage=ResourceUsage.Staging;desc.CpuAccessFlags=CpuAccessFlags.Read;
                        using(var stage=new Texture3D(d,desc)) {
                            c.CopyResource(field.Texture,stage);var map=c.MapSubresource(stage,0,MapMode.Read,SharpDX.Direct3D11.MapFlags.None);
                            try {int h=(ushort)Marshal.ReadInt16(map.DataPointer,map.SlicePitch*8+x*2),e=(h>>10)&31,m=h&1023;
                                return e==0?m*Math.Pow(2,-24):(1+m/1024.0)*Math.Pow(2,e-15);
                            } finally {c.UnmapSubresource(stage,0);}
                        }
                    };
                    Action lightDispatch=()=> {
                        c.ClearState();Update(c,cb,frame);Update(c,providers,uniforms);
                        c.ComputeShader.Set(ls);c.ComputeShader.SetConstantBuffer(6,cb);c.ComputeShader.SetConstantBuffer(7,providers);
                        c.ComputeShader.SetUnorderedAccessView(0,field.Uav);c.Dispatch(1,1,1);c.ClearState();
                    };
                    uniforms[0]=.001f;uniforms[2]=1;uniforms[3]=0;frame[94]=2;lightDispatch();
                    check(readTau(0)==0,"spatial light field preserves moving provider gap");
                    check(Math.Abs(readTau(1)-.053125)<.0001,"spatial light optical depth matches density integral");
                    uniforms[3]=-1;lightDispatch();
                    check(Math.Abs(readTau(0)-readTau(1))<.0001 && readTau(0)>.05,"light transmittance follows advected density boundary");
                    uniforms[2]=0;frame[94]=1;
                }
                // Invoke the production pixel function from a tiny compute wrapper
                // to inspect full-precision color/motion outputs without raster setup.
                string reconstruction=VolumetricShaderSource.Build(root,new[]{medium},"AnomalyVolumeReconstruct.hlsl")+@"
RWStructuredBuffer<float4> RegressionOutput:register(u0);
[numthreads(1,1,1)] void RegressionMain(uint3 id:SV_DispatchThreadID) {
    VolumePixelOutput p=__pixel_shader(float4(4,4,0,1),float2(.5,.5));
    RegressionOutput[0]=p.color;RegressionOutput[1]=p.motion;
}";
                using(var ri=new VolumeShaderInclude(root))
                using(var rb=ShaderBytecode.Compile(reconstruction,"RegressionMain","cs_5_0",ShaderFlags.OptimizationLevel3,EffectFlags.None,null,ri))
                using(var rs=new ComputeShader(d,rb))
                using(var ii=new VolumeShaderInclude(root))
                using(var ib=ShaderBytecode.Compile(File.ReadAllText(Path.Combine(root,"AnomalyVolumeIntegrate.hlsl")),"__compute_shader","cs_5_0",ShaderFlags.OptimizationLevel3,EffectFlags.None,null,ii))
                using(var integrate=new ComputeShader(d,ib))
                using(var result=new Buffer(d,new BufferDescription {SizeInBytes=32,StructureByteStride=16,BindFlags=BindFlags.UnorderedAccess,OptionFlags=ResourceOptionFlags.BufferStructured,Usage=ResourceUsage.Default}))
                using(var output=new UnorderedAccessView(d,result))
                using(var staging=new Buffer(d,new BufferDescription {SizeInBytes=32,CpuAccessFlags=CpuAccessFlags.Read,Usage=ResourceUsage.Staging}))
                {
                    var integrationBytes=new byte[32];System.Buffer.BlockCopy(new int[]{1,1,16},0,integrationBytes,0,12);
                    System.Buffer.BlockCopy(new float[]{100,1},0,integrationBytes,12,8);
                    using(var data=DataStream.Create(integrationBytes,true,false))
                    using(var integrationCb=new Buffer(d,data,new BufferDescription {SizeInBytes=32,BindFlags=BindFlags.ConstantBuffer,Usage=ResourceUsage.Immutable}))
                    {
                        Func<float[]> reconstruct=()=> {
                            dispatch();
                            c.ComputeShader.Set(integrate);c.ComputeShader.SetConstantBuffer(0,integrationCb);
                            c.ComputeShader.SetShaderResources(0,volume.Coefficients.Srv,volume.Source.Srv);
                            c.ComputeShader.SetUnorderedAccessView(0,volume.ScatteringTransmittance.Uav);c.Dispatch(1,1,1);c.ClearState();
                            c.ComputeShader.Set(rs);c.ComputeShader.SetConstantBuffer(6,cb);c.ComputeShader.SetConstantBuffer(7,providers);c.ComputeShader.SetConstantBuffer(5,shadowCb);
                            c.ComputeShader.SetShaderResources(0,depthSrv,light.Srv,volume.History.Srv,oldCoefficients.Srv,volume.ScatteringTransmittance.Srv,volume.Source.Srv,volume.Coefficients.Srv,volume.Motion.Srv,volume.SunVisibility.Srv,volume.DepthSrv);
                            c.ComputeShader.SetShaderResources(20,shadowSrv,shadowSrv,shadowSrv);c.ComputeShader.SetSampler(0,linear);c.ComputeShader.SetSampler(5,compare);
                            c.ComputeShader.SetUnorderedAccessView(0,output);c.Dispatch(1,1,1);c.ClearState();c.CopyResource(result,staging);
                            DataStream stream;c.MapSubresource(staging,MapMode.Read,SharpDX.Direct3D11.MapFlags.None,out stream);
                            try {return stream.ReadRange<float>(8);} finally {c.UnmapSubresource(staging,0);stream.Dispose();}
                        };
                        frame[85]=0;Matrix(frame,32,Projection);uniforms[0]=.001f;Clear(c,depthRtv,.1f);
                        var near=reconstruct();
                        check(Math.Abs(near[3]-Math.Exp(-.01))<1e-5,"reconstruction stops at scene depth (10 m)");
                        check(Math.Abs(near[0]-(1-Math.Exp(-.01))/(4*Math.PI))<1e-6,"near reconstruction analytic radiance");
                        Clear(c,depthRtv,0);var far=reconstruct();
                        check(Math.Abs(far[3]-Math.Exp(-.1))<.002,"far reconstruction integrates shared prefix exactly once");
                        Matrix(frame,32,Projection);frame[42]*=-1;frame[43]*=-1;var turn=reconstruct();
                        check(turn[4]==0 && turn[5]==0 && turn[6]>0,"invalid reprojected motion is zero with reactive coverage");
                        check(Array.TrueForAll(turn,v=>!float.IsNaN(v)&&!float.IsInfinity(v)),"camera turn output remains finite");
                        uniforms[0]=0;var empty=reconstruct();
                        check(empty[0]==0 && empty[1]==0 && empty[2]==0 && empty[3]==1 && empty[6]==0,"zero-density full pipeline is a no-op");
                    }
                }

            }
        } finally {File.Delete(provider);}
    }
}

