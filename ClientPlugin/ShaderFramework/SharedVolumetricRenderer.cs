using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ClientPlugin.Buffers;
using ClientPlugin.Shaders;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.D3DCompiler;
using VRage.Render11.Common;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageMath;
using VRageRender;
using Resource = SharpDX.Direct3D11.Resource;

namespace ClientPlugin.ShaderFramework;

internal static class SharedVolumetricRenderer
{
    static readonly VolumetricFrameResources Volume=new();
    static readonly VolumetricInteriorUpload Interiors=new();
    static readonly VolumetricGpuTimer LightTimer=new(),InjectionTimer=new(),ReconstructionTimer=new();
    static VolumetricFrameResources.Volume lightTau,previousCoefficients;
    static Texture2D previousDepth;
    static ShaderResourceView previousDepthSrv;
    static IRtvTexture color,motion,reactive,velocity,velocityMask;
    static ComputeShader lightShader,injectShader;
    static PixelShader reconstructShader,compositeShader,reactiveShader,velocityShader;
    static VertexShader vertexShader;
    static IConstantBuffer frameCb,providerCb,shadowCb;
    static SamplerState linear,wrap,point,comparison;
    static BlendState compositeBlend,maxBlend;
    static string signature,lastError;
    static int nextShaderAttempt,lastDiagnosticTick;
    static bool diagnosticWritten;
    static int preparedQuality,preparedProviders;
    static float preparedDistance;
    static bool prepared,history;
    static uint preparedFrame;
    static long historyRevision=-1,epoch=-1,interiorRevision=-1;
    static Vector3D previousPlanet;
    static int outputWidth,outputHeight;
    static double previousTime;
    static readonly string[] ProductNames={"volumeScatteringTransmittance","volumeMotion","volumeReactive","volumeRepresentativeDepth","volumeSunVisibility"};

    [StructLayout(LayoutKind.Sequential)]
    struct Frame
    {
        public Matrix CameraToWorld,CurrentProjection,PreviousProjection,LightToCamera;
        public Vector4 Grid,Screen,Sun,SunEnergy,Ambient,Temporal,CameraDelta,Control;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Shadows
    {
        public Matrix A,B,C;
        public Vector4 Radii,Planet;
    }

    internal static void Install()
    {
        OwnedPassRegistry.Register("Anomaly.Volumes.Prepare",OwnedPassSlot.AfterAtmosphere,int.MaxValue,
            TemporalPolicy.InColor,Prepare,OwnedPassPhase.BeforeFullscreen);
        OwnedPassRegistry.Register("Anomaly.Volumes.Composite",OwnedPassSlot.AfterAtmosphere,int.MinValue,
            TemporalPolicy.InColor|TemporalPolicy.Reactive,Composite,OwnedPassPhase.AfterFullscreen);
        // CameraVelocityPass publishes after the geometry scheduler. Overlay later,
        // at BeforeTonemap, so camera motion cannot overwrite volume motion.
        OwnedPassRegistry.Register("Anomaly.Volumes.Motion",OwnedPassSlot.BeforeTonemap,int.MinValue,
            TemporalPolicy.ContributeVelocity,ContributeMotion,OwnedPassPhase.BeforeFullscreen);
    }
    internal static void BeginFrame()
    {
        prepared=false; Volume.BeginFrame();
        VolumetricMediumRegistry.ResetIntervals();
        foreach(var name in ProductNames) BufferCatalog.Set(name,null);
    }
    internal static void BeforeScheduler()
    {
        var rc=MyRender11.RC;
        LightTimer.Poll(rc.DeviceContext); InjectionTimer.Poll(rc.DeviceContext); ReconstructionTimer.Poll(rc.DeviceContext);
        DirectionalVolumeShadows.Requested=false;
        if(!VolumetricMediumRegistry.Requested || MyRender11.MultisamplingEnabled || (nextShaderAttempt!=0 && unchecked(Environment.TickCount-nextShaderAttempt)<0)) return;
        var media=VolumetricMediumRegistry.Capture(out _);
        if(media.Length==0 || media.Any(m=>!m.ParametersSet)) return;
        try { EnsureShaders(media); DirectionalVolumeShadows.Requested=true; }
        catch(Exception e) { nextShaderAttempt=unchecked(Environment.TickCount+5000); Fail(e); }
    }
    static void Prepare(OwnedPassContext context)
    {
        if(!VolumetricMediumRegistry.Requested) { history=false; return; }
        if(MyRender11.MultisamplingEnabled) { Fail(new InvalidOperationException("MSAA unsupported")); return; }
        if(!DirectionalVolumeShadows.IsValid(FrameTemporal.FrameIndex)) { history=false; VolumetricMediumRegistry.SetStatus(DirectionalVolumeShadows.Status); return; }
        var rc=context.Rc;
        var media=VolumetricMediumRegistry.Capture(out long revision,out long capturedHistoryRevision,out int quality,out float distance,out int debugView);
        if(media.Length==0 || media.Any(m=>!m.ParametersSet)) return;
        try
        {
            EnsureShaders(media);
            EnsureResources(context.Width,context.Height,quality);
            var camera=MyRender11.Environment.Matrices.CameraPosition;
            Interiors.Upload(VolumetricInteriorSnapshot.Capture(),camera);
            var planet=PlanetAtmosphere.Copy();
            bool useHistory=history && interiorRevision==Interiors.Revision && epoch==Volume.Epoch && historyRevision==capturedHistoryRevision &&
                FrameTemporal.HistoryValid && FrameTemporal.SafetyScale>.8f && previousPlanet==planet.Center;
            double time=MyCommon.FrameTime.Seconds;
            float dt=(float)Math.Max(0,Math.Min(.1,time-previousTime));
            var projection=MyRender11.Environment.Matrices.Projection;
            var sun=FrameTemporal.SunToward;
            var sunColor=FrameTemporal.SunColor*FrameTemporal.SunDiffuse;
            var ambient=FrameTemporal.SkyAmbient;
            var lightToCamera=MatrixD.Invert(DirectionalVolumeShadows.WorldToShadow[2])*MatrixD.CreateTranslation(-camera);
            var frame=new Frame {
                CameraToWorld=FrameTemporal.CameraToWorld,CurrentProjection=FrameTemporal.UnjitteredViewProj,
                PreviousProjection=FrameTemporal.PrevViewProj,LightToCamera=(Matrix)lightToCamera,
                Grid=new Vector4(Volume.Width,Volume.Height,Volume.Slices,distance),
                Screen=new Vector4(context.Width,context.Height,FrameTemporal.ProjScale.X,FrameTemporal.ProjScale.Y),
                Sun=new Vector4(sun,47999),SunEnergy=new Vector4(sunColor,projection.M43),Ambient=new Vector4(ambient,projection.M33),
                Temporal=new Vector4(dt,useHistory?1:0,FrameTemporal.FrameIndex,debugView),
                CameraDelta=new Vector4(FrameTemporal.CameraDelta,0),
                Control=new Vector4(media.Length,Interiors.GridCount,lightTau.Texture.Description.Width,lightTau.Texture.Description.Depth)
            };
            var shadows=new Shadows {
                A=(Matrix)(MatrixD.CreateTranslation(camera)*DirectionalVolumeShadows.WorldToShadow[0]),
                B=(Matrix)(MatrixD.CreateTranslation(camera)*DirectionalVolumeShadows.WorldToShadow[1]),
                C=(Matrix)(MatrixD.CreateTranslation(camera)*DirectionalVolumeShadows.WorldToShadow[2]),
                Radii=new Vector4(128,1000,8000,1),Planet=new Vector4((Vector3)(planet.Center-camera),planet.IsValid?planet.TerrainRadius:0)
            };
            Write(rc,frameCb,ref frame); Write(rc,shadowCb,ref shadows); WriteProviders(rc,media,camera);
            Bind(rc,media,false);
            LightTimer.Begin(rc.DeviceContext);
            rc.ComputeShader.Set(lightShader);
            rc.DeviceContext.ComputeShader.SetUnorderedAccessView(0,lightTau.Uav);
            rc.DeviceContext.Dispatch((lightTau.Texture.Description.Width+7)/8,(lightTau.Texture.Description.Height+7)/8,1);
            rc.DeviceContext.ComputeShader.SetUnorderedAccessView(0,null);
            LightTimer.End(rc.DeviceContext);
            rc.DeviceContext.ComputeShader.SetShaderResource(1,lightTau.Srv);
            InjectionTimer.Begin(rc.DeviceContext);
            rc.ComputeShader.Set(injectShader);
            rc.DeviceContext.ComputeShader.SetUnorderedAccessViews(0,Volume.Coefficients.Uav,Volume.Source.Uav,Volume.Motion.Uav,Volume.SunVisibility.Uav,Volume.DepthUav);
            rc.DeviceContext.Dispatch((Volume.Width+3)/4,(Volume.Height+3)/4,(Volume.Slices+3)/4);
            rc.DeviceContext.ComputeShader.SetUnorderedAccessViews(0,new UnorderedAccessView[5]);
            InjectionTimer.End(rc.DeviceContext);
            rc.ClearState();
            VolumetricIntegrator.Execute(rc,Volume,distance);
            ReconstructionTimer.Begin(rc.DeviceContext);
            Bind(rc,media,true);
            rc.DeviceContext.PixelShader.SetShaderResource(1,lightTau.Srv);
            rc.DeviceContext.PixelShader.SetShaderResource(9,Volume.DepthSrv);
            Draw(rc,reconstructShader,color,motion);
            rc.ClearState();
            DrawCoverage(rc,reactiveShader,reactive);
            DrawCoverage(rc,velocityShader,velocity);
            // Alpha/coverage mask has its own shader variant below.
            DrawCoverage(rc,maskShader,velocityMask);
            ReconstructionTimer.End(rc.DeviceContext);
            if(!VolumetricMediumRegistry.Commit(FrameTemporal.FrameIndex,revision,true,true,true,true,media)) return;
            // Only change legacy intervals after every shared draw has recorded successfully.
            foreach(var m in media) m.Interval?.Invoke(distance);
            prepared=true; preparedFrame=FrameTemporal.FrameIndex;
            preparedQuality=quality; preparedProviders=media.Length; preparedDistance=distance;
            rc.DeviceContext.CopyResource(Volume.Source.Texture,Volume.History.Texture);
            rc.DeviceContext.CopyResource(Volume.Coefficients.Texture,previousCoefficients.Texture);
            rc.DeviceContext.CopyResource(Volume.RepresentativeDepth,previousDepth);
            history=true; interiorRevision=Interiors.Revision; epoch=Volume.Epoch; historyRevision=capturedHistoryRevision;
            previousPlanet=planet.Center; previousTime=time;
        }
        catch(Exception e) { Fail(e); VolumetricMediumRegistry.ResetIntervals(); if(RenderTrace.IsLostDevice(e)) throw; }
        finally { LightTimer.End(rc.DeviceContext); InjectionTimer.End(rc.DeviceContext); ReconstructionTimer.End(rc.DeviceContext); rc.ClearState(); }
    }
    static void Composite(OwnedPassContext context)
    {
        if(!prepared || preparedFrame!=FrameTemporal.FrameIndex) return;
        var rc=context.Rc;
        try
        {
            rc.PixelShader.SetSrv(0,color);
            Draw(rc,compositeShader,MyGBuffer.Main.LBuffer,null,compositeBlend);
            rc.ClearState();
            TemporalParticipation.EnsureReactive(rc);
            // Max merge the current volume reactive coverage into the host mask.
            rc.PixelShader.SetSrv(0,reactive);
            Draw(rc,compositeShader,TemporalParticipation.ReactiveRtv as IRtvBindable,null,maxBlend);
            rc.ClearState();
            Volume.Publish(FrameTemporal.FrameIndex);
            Publish("volumeScatteringTransmittance",new VolumeBindable(Volume.ScatteringTransmittance));
            Publish("volumeMotion",motion); Publish("volumeReactive",reactive);
            Publish("volumeRepresentativeDepth",new TextureBindable(Volume.RepresentativeDepth,Volume.DepthSrv));
            Publish("volumeSunVisibility",new VolumeBindable(Volume.SunVisibility));
            VolumetricMediumRegistry.SetStatus("Shared volume active; "+Interiors.Status+"; GPU shadows/light/inject/integrate/reconstruct ms: "+
                Timing(DirectionalVolumeShadows.GpuMilliseconds)+" / "+Timing(LightTimer.LastMilliseconds)+" / "+Timing(InjectionTimer.LastMilliseconds)+" / "+Timing(VolumetricIntegrator.GpuMilliseconds)+" / "+Timing(ReconstructionTimer.LastMilliseconds));
            int now=Environment.TickCount;
            if(!diagnosticWritten || unchecked((uint)(now-lastDiagnosticTick))>=10000) {
                diagnosticWritten=true; lastDiagnosticTick=now;
                MyLog.Default.WriteLine("Anomaly volume sample: frame="+FrameTemporal.FrameIndex+
                    " size="+outputWidth+"x"+outputHeight+" quality="+preparedQuality+" providers="+preparedProviders+
                    " distance="+preparedDistance.ToString(System.Globalization.CultureInfo.InvariantCulture)+
                    " shadowsMs="+Timing(DirectionalVolumeShadows.GpuMilliseconds)+" lightMs="+Timing(LightTimer.LastMilliseconds)+
                    " injectMs="+Timing(InjectionTimer.LastMilliseconds)+" integrateMs="+Timing(VolumetricIntegrator.GpuMilliseconds)+
                    " reconstructMs="+Timing(ReconstructionTimer.LastMilliseconds)+" interiors="+Interiors.Status);
            }
        }
        catch(Exception e) { Fail(e); if(RenderTrace.IsLostDevice(e)) throw; }
        finally { rc.ClearState(); }
    }
    static string Timing(double value)=>double.IsNaN(value)?"pending":value.ToString("F2",System.Globalization.CultureInfo.InvariantCulture);
    static void ContributeMotion(OwnedPassContext context)
    { if(Volume.Matches(FrameTemporal.FrameIndex,Volume.Epoch)) context.ContributeVelocity(velocity,velocityMask); }
    static void Publish(string name,ISrvBindable resource) => BufferCatalog.Set(name,new FrameProduct(resource,FrameTemporal.FrameIndex,Volume.Epoch));
    static void Fail(Exception e)
    {
        prepared=false; history=false; Volume.BeginFrame();
        VolumetricMediumRegistry.SetStatus("Shared volume fallback: "+e.Message);
        if(lastError!=e.Message) { lastError=e.Message; MyLog.Default.WriteLine("Anomaly volumetrics: "+e); }
    }
    static void Write<T>(MyRenderContext rc,IConstantBuffer buffer,ref T data) where T:struct
    { var map=MyMapping.MapDiscard(rc,buffer); try { map.WriteAndPosition(ref data); } finally { map.Unmap(); } }
    static void WriteProviders(MyRenderContext rc,VolumetricMediumRegistry.Medium[] media,Vector3D camera)
    {
        var values=new Vector4[80];
        for(int i=0;i<media.Length;i++)
        {
            var m=media[i];
            for(int j=0;j<16;j++) values[i*16+j]=new Vector4(m.Uniforms[j*4],m.Uniforms[j*4+1],m.Uniforms[j*4+2],m.Uniforms[j*4+3]);
            values[64+i]=new Vector4((float)(m.Origin[0]-camera.X),(float)(m.Origin[1]-camera.Y),(float)(m.Origin[2]-camera.Z),0);
            values[68+i]=new Vector4((float)(m.Bounds[0]-camera.X),(float)(m.Bounds[1]-camera.Y),(float)(m.Bounds[2]-camera.Z),0);
            values[72+i]=new Vector4((float)(m.Bounds[3]-camera.X),(float)(m.Bounds[4]-camera.Y),(float)(m.Bounds[5]-camera.Z),0);
            values[76+i]=new Vector4(m.Motion[0],m.Motion[1],m.Motion[2],0);
        }
        var map=MyMapping.MapDiscard(rc,providerCb); try { foreach(var item in values) { var value=item; map.WriteAndPosition(ref value); } } finally { map.Unmap(); }
    }
    static void Bind(MyRenderContext rc,VolumetricMediumRegistry.Medium[] media,bool pixel)
    {
        var srvs=new ShaderResourceView[44];
        srvs[0]=MyGBuffer.Main.ResolvedDepthStencil.SrvDepth.Srv;
        srvs[2]=Volume.History.Srv; srvs[3]=previousCoefficients.Srv; srvs[4]=Volume.ScatteringTransmittance.Srv;
        srvs[5]=Volume.Source.Srv; srvs[6]=Volume.Coefficients.Srv; srvs[7]=Volume.Motion.Srv; srvs[8]=Volume.SunVisibility.Srv; srvs[9]=previousDepthSrv;
        // Never bind a target as SRV while injection writes it.
        if(!pixel) { srvs[4]=srvs[5]=srvs[6]=srvs[7]=srvs[8]=null; }
        for(int i=0;i<3;i++) srvs[20+i]=DirectionalVolumeShadows.Maps[i].Srv;
        srvs[23]=Interiors.Grids.Srv; srvs[24]=Interiors.Cells.Srv;
        for(int i=0;i<media.Length;i++) for(int j=0;j<media[i].CatalogBindings.Length;j++)
        {
            var buffer=BufferCatalog.Active(media[i].CatalogBindings[j]);
            if(!buffer.IsAvailable || !(buffer.Srv is ISrvBindable resource)) throw new InvalidOperationException("Missing medium texture: "+media[i].CatalogBindings[j]);
            srvs[32+i*3+j]=resource.Srv;
        }
        if(pixel) {
            rc.PixelShader.SetConstantBuffer(6,frameCb); rc.PixelShader.SetConstantBuffer(7,providerCb); rc.PixelShader.SetConstantBuffer(8,shadowCb);
            rc.DeviceContext.PixelShader.SetShaderResources(0,srvs); rc.DeviceContext.PixelShader.SetSamplers(0,linear,wrap,point);
            rc.DeviceContext.PixelShader.SetSampler(5,comparison);
        } else {
            rc.ComputeShader.SetConstantBuffer(6,frameCb); rc.ComputeShader.SetConstantBuffer(7,providerCb); rc.ComputeShader.SetConstantBuffer(8,shadowCb);
            rc.DeviceContext.ComputeShader.SetShaderResources(0,srvs); rc.DeviceContext.ComputeShader.SetSamplers(0,linear,wrap,point);
            rc.DeviceContext.ComputeShader.SetSampler(5,comparison);
        }
    }
    static void Draw(MyRenderContext rc,PixelShader shader,IRtvBindable target,IRtvBindable target1=null,BlendState blend=null)
    {
        if(target==null) throw new InvalidOperationException("Missing volume render target");
        rc.ResetTargets();
        rc.DeviceContext.OutputMerger.SetTargets(null,target1==null?new[]{target.Rtv}:new[]{target.Rtv,target1.Rtv});
        rc.SetViewport(0,0,outputWidth,outputHeight);
        rc.SetRasterizerState(MyRasterizerStateManager.NocullRasterizerState);
        rc.SetDepthStencilState(MyDepthStencilStateManager.IgnoreDepthStencil);
        rc.SetBlendState(MyBlendStateManager.BlendReplace);
        if(blend!=null) rc.DeviceContext.OutputMerger.SetBlendState(blend);
        rc.SetInputLayout(null); rc.SetPrimitiveTopology(PrimitiveTopology.TriangleList); rc.SetVertexBuffer(0,null);
        rc.GeometryShader.Set(null); rc.VertexShader.Set(vertexShader); rc.PixelShader.Set(shader); rc.Draw(3,0);
    }
    static void DrawCoverage(MyRenderContext rc,PixelShader shader,IRtvBindable target)
    { rc.PixelShader.SetSrv(0,motion); Draw(rc,shader,target); rc.ClearState(); }
    static PixelShader maskShader;
    static void EnsureResources(int width,int height,int quality)
    {
        Volume.Ensure(width,height,quality==0?12:quality==1?8:6,quality==0?64:quality==1?96:128);
        if(epoch!=Volume.Epoch) history=false;
        if(previousCoefficients==null || previousCoefficients.Texture.Description.Width!=Volume.Width || previousCoefficients.Texture.Description.Height!=Volume.Height || previousCoefficients.Texture.Description.Depth!=Volume.Slices)
        {
            previousCoefficients?.Dispose(); previousDepthSrv?.Dispose(); previousDepth?.Dispose();
            previousCoefficients=new VolumetricFrameResources.Volume(Volume.Width,Volume.Height,Volume.Slices,Format.R16G16B16A16_Float);
            var desc=Volume.RepresentativeDepth.Description; desc.BindFlags=BindFlags.ShaderResource;
            previousDepth=new Texture2D(MyRender11.DeviceInstance,desc); previousDepthSrv=new ShaderResourceView(MyRender11.DeviceInstance,previousDepth);
        }
        int lightSize=quality==0?64:quality==1?128:160;
        if(lightTau==null || lightTau.Texture.Description.Width!=lightSize) { lightTau?.Dispose(); lightTau=new VolumetricFrameResources.Volume(lightSize,lightSize,128,Format.R16_Float); }
        if(color!=null && outputWidth==width && outputHeight==height) return;
        ReleaseTargets(); outputWidth=width;outputHeight=height;
        color=MyManagers.RwTextures.CreateRtv("Anomaly.Volume.Color",width,height,Format.R16G16B16A16_Float);
        motion=MyManagers.RwTextures.CreateRtv("Anomaly.Volume.Motion",width,height,Format.R16G16B16A16_Float);
        reactive=MyManagers.RwTextures.CreateRtv("Anomaly.Volume.Reactive",width,height,Format.R16_Float);
        velocity=MyManagers.RwTextures.CreateRtv("Anomaly.Volume.Velocity",width,height,Format.R16G16_Float);
        velocityMask=MyManagers.RwTextures.CreateRtv("Anomaly.Volume.VelocityMask",width,height,Format.R16_Float);
    }
    static byte[] Compile(string source,string entry,string profile,string root)
    { using(var include=new VolumeShaderInclude(root)) using(var bytecode=ShaderBytecode.Compile(source,entry,profile,ShaderFlags.OptimizationLevel3,EffectFlags.None,null,include)) return bytecode.Bytecode.Data; }
    static void EnsureShaders(VolumetricMediumRegistry.Medium[] media)
    {
        var root=ShaderCompileIntercept.IncludeDirectory;
        var key=string.Join("|",media.Select(m=>m.ShaderFile+File.GetLastWriteTimeUtc(m.ShaderFile).Ticks));
        if(signature==key && lightShader!=null) return;
        ReleaseShaders(); history=false;
        try
        {
            var device=MyRender11.DeviceInstance;
            lightShader=new ComputeShader(device,Compile(VolumetricShaderSource.Build(root,media,"AnomalyVolumeLight.hlsl"),"__compute_shader","cs_5_0",root));
            injectShader=new ComputeShader(device,Compile(VolumetricShaderSource.Build(root,media,"AnomalyVolumeInject.hlsl"),"__compute_shader","cs_5_0",root));
            reconstructShader=new PixelShader(device,Compile(VolumetricShaderSource.Build(root,media,"AnomalyVolumeReconstruct.hlsl"),"__pixel_shader","ps_5_0",root));
            compositeShader=new PixelShader(device,Compile(File.ReadAllText(Path.Combine(root,"AnomalyVolumeComposite.hlsl")),"__pixel_shader","ps_5_0",root));
            var coverage=File.ReadAllText(Path.Combine(root,"AnomalyVolumeTemporalCoverage.hlsl"));
            reactiveShader=new PixelShader(device,Compile("#define VOLUME_REACTIVE 1\n"+coverage,"__pixel_shader","ps_5_0",root));
            velocityShader=new PixelShader(device,Compile(coverage,"__pixel_shader","ps_5_0",root));
            maskShader=new PixelShader(device,Compile("Texture2D<float4> T:register(t0); float4 __pixel_shader(float4 p:SV_Position,float2 u:TEXCOORD0):SV_Target { float4 v=T.Load(int3((int2)p.xy,0)); return v.w>.95 && v.z<.1?1:0; }","__pixel_shader","ps_5_0",root));
            vertexShader=new VertexShader(device,Compile("void main(uint id:SV_VertexID,out float4 p:SV_Position,out float2 uv:TEXCOORD0) { uv=float2((id<<1)&2,id&2); p=float4(uv*float2(2,-2)+float2(-1,1),0,1); }","main","vs_5_0",root));
            frameCb=MyManagers.Buffers.CreateConstantBuffer("Anomaly.Volume.Frame",Marshal.SizeOf<Frame>(),usage:ResourceUsage.Dynamic);
            providerCb=MyManagers.Buffers.CreateConstantBuffer("Anomaly.Volume.Providers",1280,usage:ResourceUsage.Dynamic);
            shadowCb=MyManagers.Buffers.CreateConstantBuffer("Anomaly.Volume.Shadows",Marshal.SizeOf<Shadows>(),usage:ResourceUsage.Dynamic);
            linear=Sampler(TextureAddressMode.Clamp,Filter.MinMagMipLinear);
            wrap=Sampler(TextureAddressMode.Wrap,Filter.MinMagMipLinear);
            point=Sampler(TextureAddressMode.Clamp,Filter.MinMagMipPoint);
            comparison=Sampler(TextureAddressMode.Clamp,Filter.ComparisonMinMagLinearMipPoint);
            var desc=new BlendStateDescription(); desc.RenderTarget[0]=new RenderTargetBlendDescription {
                IsBlendEnabled=true,SourceBlend=BlendOption.One,DestinationBlend=BlendOption.SourceAlpha,BlendOperation=BlendOperation.Add,
                SourceAlphaBlend=BlendOption.Zero,DestinationAlphaBlend=BlendOption.One,AlphaBlendOperation=BlendOperation.Add,RenderTargetWriteMask=ColorWriteMaskFlags.All
            }; compositeBlend=new BlendState(device,desc);
            desc.RenderTarget[0].DestinationBlend=BlendOption.One; desc.RenderTarget[0].BlendOperation=BlendOperation.Maximum;
            maxBlend=new BlendState(device,desc); signature=key;
        }
        catch { ReleaseShaders(); throw; }
    }
    static SamplerState Sampler(TextureAddressMode address,Filter filter)=>new(MyRender11.DeviceInstance,new SamplerStateDescription {
        AddressU=address,AddressV=address,AddressW=address,Filter=filter,MaximumLod=float.MaxValue,ComparisonFunction=Comparison.LessEqual
    });
    static void ReleaseTargets()
    {
        MyManagers.RwTextures.DisposeTex(ref color); MyManagers.RwTextures.DisposeTex(ref motion); MyManagers.RwTextures.DisposeTex(ref reactive);
        MyManagers.RwTextures.DisposeTex(ref velocity); MyManagers.RwTextures.DisposeTex(ref velocityMask);
    }
    static void ReleaseShaders()
    {
        lightShader?.Dispose(); injectShader?.Dispose(); reconstructShader?.Dispose(); compositeShader?.Dispose(); reactiveShader?.Dispose(); velocityShader?.Dispose(); maskShader?.Dispose(); vertexShader?.Dispose();
        lightShader=injectShader=null; reconstructShader=compositeShader=reactiveShader=velocityShader=maskShader=null;vertexShader=null;
        foreach(var cb in new[]{frameCb,providerCb,shadowCb}) if(cb!=null) MyManagers.Buffers.Dispose(cb);
        frameCb=providerCb=shadowCb=null;
        linear?.Dispose();wrap?.Dispose();point?.Dispose();comparison?.Dispose();compositeBlend?.Dispose();maxBlend?.Dispose();
        linear=wrap=point=comparison=null;compositeBlend=maxBlend=null;signature=null;
    }
    internal static void Release()
    {
        prepared=history=diagnosticWritten=false; Volume.Dispose(); Interiors.Dispose(); ReleaseShaders(); ReleaseTargets();
        lightTau?.Dispose();previousCoefficients?.Dispose();previousDepthSrv?.Dispose();previousDepth?.Dispose();
        lightTau=previousCoefficients=null;previousDepthSrv=null;previousDepth=null;
        LightTimer.Dispose();InjectionTimer.Dispose();ReconstructionTimer.Dispose();
        foreach(var name in ProductNames) BufferCatalog.Set(name,null);
    }
    sealed class FrameProduct : ISharedBuffer
    {
        readonly ISrvBindable resource; readonly uint frame; readonly long epoch;
        internal FrameProduct(ISrvBindable resource,uint frame,long epoch) { this.resource=resource;this.frame=frame;this.epoch=epoch; }
        public bool IsAvailable=>frame==FrameTemporal.FrameIndex && Volume.Matches(frame,epoch);
        public object Srv=>IsAvailable?resource:null;
        public IntPtr NativeResource=>IsAvailable?resource.Resource.NativePointer:IntPtr.Zero;
        public int Width=>resource.Size.X;
        public int Height=>resource.Size.Y;
        public int ContractVersion=>VolumetricFrameResources.Version;
        public uint Frame=>frame;
        public long ResourceEpoch=>epoch;
    }
    class TextureBindable : ISrvBindable
    {
        public string Name=>"Anomaly.VolumeProduct";
        public Resource Resource {get;}
        public ShaderResourceView Srv {get;}
        public Vector3I Size3 {get;}
        public Vector2I Size=>new(Size3.X,Size3.Y);
        internal TextureBindable(Texture2D texture,ShaderResourceView srv) { Resource=texture;Srv=srv;Size3=new Vector3I(texture.Description.Width,texture.Description.Height,1); }
        protected TextureBindable(Resource texture,ShaderResourceView srv,Vector3I size) { Resource=texture;Srv=srv;Size3=size; }
    }
    sealed class VolumeBindable : TextureBindable
    { internal VolumeBindable(VolumetricFrameResources.Volume volume):base(volume.Texture,volume.Srv,new Vector3I(volume.Texture.Description.Width,volume.Texture.Description.Height,volume.Texture.Description.Depth)) {} }
}
