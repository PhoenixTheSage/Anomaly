using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ClientPlugin.ShaderFramework;
using ClientPlugin.Shaders;
using ClientPlugin.Buffers;
using VRageMath;
namespace ClientPlugin.Backend;

// The host owns medium policy and one complete frame transaction. Native only
// validates copied resources and executes the shared shaders in declared phases.
internal static class GuestVolumes
{
    const int Before=1024,After=2048;
    static string signature;
    static long epoch=-1,historyRevision=-1,interiorRevision=-1,activeEpoch;
    static uint previousFrame,previousGeneration,activeFrame;
    static bool history,active;
    static double previousTime;
    static Vector3D previousCamera,previousPlanet;
    static float previousRadius,safety=1;
    static Vector3 previousForward=Vector3.Forward;
    static readonly HashSet<string> suppressed=new(StringComparer.OrdinalIgnoreCase);
    static GuestProgram light,inject,integrate,reconstruct,reactive,composite,copy,velocityMerge,reactiveMerge;
    static GuestProgram[] shaders=Array.Empty<GuestProgram>();
    static readonly VolumeInteriorData rooms=new();
    sealed class Image
    {
        internal ulong Id;int width,height,depth,format;long generation=-1;
        internal void Ensure(int w,int h,int d,int f,ulong pinned=0)
        {
            bool allocated=Id==0;
            if(allocated)Id=pinned!=0?pinned:GuestBridgeClient.AllocateImage(w,h,d,null,f);
            if((allocated && pinned!=0) || (!allocated && (width!=w || height!=h || depth!=d || format!=f || generation!=epoch)))GuestBridgeClient.CreateImage(Id,w,h,d,f);
            width=w;height=h;depth=d;format=f;generation=epoch;
        }
    }
    static Image coefficients=new(),oldCoefficients=new(),source=new(),oldSource=new(),representative=new(),oldDepth=new();
    static readonly Image integrated=new(),motionVolume=new(),visibility=new(),tau=new(),color=new(),motion=new(),reject=new(),mergedColor=new(),mergedVelocity=new(),mergedReactive=new(),grids=new(),cells=new(),cameraVelocity=new(),hostReactive=new();
    static int renderWidth,renderHeight,gridSlices,lightResolution;
    internal static bool Active=>Current();
    internal static void ResetDevice(){active=false;history=false;safety=1;rooms.Clear();suppressed.Clear();epoch=-1;}
    internal static void PreparePrograms(string assets)
    {
        if(!VolumeRegistryState.Requested || !VolumeRegistryState.HasReadyMedia)return;
        var media=VolumeRegistryState.Capture(out _);
        string key=string.Join("|",media.Select(m=>m.Id+":"+m.ShaderFile+":"+File.GetLastWriteTimeUtc(m.ShaderFile).Ticks));
        long current=GuestBridgeClient.ResourceEpoch;
        if(epoch!=current){epoch=current;history=false;active=false;foreach(var p in shaders){p.Pipeline=0;p.Status="source";p.Error=null;}}
        if(signature==key && shaders.Length!=0)return;
        string root=Path.Combine(assets,"Shaders"),folder=Path.Combine(Path.GetTempPath(),"anomaly-shared-volume");Directory.CreateDirectory(folder);
        GuestProgram Add(string name,string text,bool compute=false,int contract=1)
        {
            string file=Path.Combine(folder,name+".hlsl");File.WriteAllText(file,text);
            string id="anomaly.owned.Volumes."+name;GuestPassList.AddProgram(id,"AfterAtmosphere",file,"PublishOnly");
            var p=GuestPassList.Snapshot().Single(p=>p.Id==id);p.ResourceContract=contract;p.Flags=compute?GuestBridgeClient.Compute:0;return p;
        }
        try {
            light=Add("Light",VolumetricShaderSource.Build(root,media,"AnomalyVolumeLight.hlsl"),true);
            inject=Add("Inject",VolumetricShaderSource.Build(root,media,"AnomalyVolumeInject.hlsl"),true);
            integrate=Add("Integrate",File.ReadAllText(Path.Combine(root,"AnomalyVolumeIntegrate.hlsl")),true);
            reconstruct=Add("Reconstruct",VolumetricShaderSource.Build(root,media,"AnomalyVolumeReconstruct.hlsl"),false,2);
            reactive=Add("Reactive","#define VOLUME_REACTIVE 1\n"+File.ReadAllText(Path.Combine(root,"AnomalyVolumeTemporalCoverage.hlsl")));
            composite=Add("Composite","Texture2D<float4> Scene:register(t0);Texture2D<float4> Volume:register(t1);float4 __pixel_shader(float4 p:SV_Position):SV_Target {int3 c=int3((int2)p.xy,0);float4 s=Scene.Load(c),v=Volume.Load(c);return float4(v.rgb+s.rgb*v.a,s.a);}");
            copy=Add("Copy",File.ReadAllText(Path.Combine(root,"AnomalyVolumeComposite.hlsl")));
            velocityMerge=Add("VelocityMerge","Texture2D<float2> Scene:register(t0);Texture2D<float4> Volume:register(t1);float2 __pixel_shader(float4 p:SV_Position):SV_Target {int3 c=int3((int2)p.xy,0);float4 v=Volume.Load(c);return v.w>.95 && v.z<.1?v.xy:Scene.Load(c);}");
            reactiveMerge=Add("ReactiveMerge","Texture2D<float4> Scene:register(t0);Texture2D<float> Volume:register(t1);float4 __pixel_shader(float4 p:SV_Position):SV_Target {int3 c=int3((int2)p.xy,0);return max(Scene.Load(c),Volume.Load(c));}");
            shaders=new[]{light,inject,integrate,reconstruct,reactive,composite,copy,velocityMerge,reactiveMerge};signature=key;history=false;
        }catch(Exception error){signature=null;shaders=Array.Empty<GuestProgram>();history=false;VolumeRegistryState.SetStatus("Shared volume shaders unavailable: "+error.Message);}
    }
    internal static void BeginFrame(){active=false;suppressed.Clear();}
    internal static bool SuppressFallback(GuestProgram program)=>active && program!=null && suppressed.Contains(program.Id??"");
    internal static void TrySubmit(IReadOnlyList<GuestProgram> programs)
    {
        active=false;suppressed.Clear();
        if(!VolumeRegistryState.Requested){history=false;return;}
        if(!GuestBridgeClient.VolumeIntervalsReady || !GuestBridgeClient.SupportsPacketFlags(GuestBridgeClient.SharedFrame)){history=false;return;}
        try {
            var media=VolumeRegistryState.Capture(out long revision,out long capturedHistory,out int quality,out float distance,out int debug);
            if(media.Length==0 || media.Any(m=>!m.ParametersSet) || shaders.Length==0 || shaders.Any(p=>p.Pipeline==0))throw new InvalidOperationException("Provider parameters/shaders not ready");
            var depth=GuestBridgeClient.CaptureSceneDepth();var scene=GuestBridgeClient.CaptureVolumeScene();
            if(GuestVolumeShadows.Views==null || depth==null || depth.Length<6 || !(depth[0] is true) || scene==null || scene.Length<4 || !(depth[1] is uint generation) || !(scene[0] is uint sourceGeneration) || generation!=sourceGeneration || generation!=GuestVolumeShadows.Generation)throw new InvalidOperationException("Current complete directional depth collection unavailable");
            var position=(double[])scene[1];var camera=new Vector3D(position[0],position[1],position[2]);
            if(!position.SequenceEqual(GuestVolumeShadows.Camera))throw new InvalidOperationException("Directional camera changed after preparation");
            var nativeFrame=new byte[344];if(!GuestBridgeClient.TryGetFrame(nativeFrame))throw new InvalidOperationException("Current renderer frame unavailable");
            uint frame=BitConverter.ToUInt32(nativeFrame,16);int w=checked((int)BitConverter.ToUInt32(nativeFrame,8)),h=checked((int)BitConverter.ToUInt32(nativeFrame,12));
            if(!programs.Any(p=>p.Id=="anomaly.owned.CameraVelocity" && p.Enabled && p.Pipeline!=0) || !programs.Any(p=>p.Id=="anomaly.owned.ReactiveClear" && p.Enabled && p.Pipeline!=0))throw new InvalidOperationException("Host temporal producers unavailable");
            int column=quality==0?12:quality==1?8:6,slices=quality==0?64:quality==1?96:128,lightSize=quality==0?64:quality==1?128:160;
            int gw=(w+column-1)/column,gh=(h+column-1)/column;Ensure(gw,gh,slices,w,h,lightSize);
            var snapshot=VolumetricInteriorSnapshot.Capture();
            rooms.Build(snapshot.Grids.Select(g=>new VolumeInteriorData.Grid(g.Id,g.Generation,g.WorldToLocal,g.CellSize,g.Cells,g.IsCurrent)),snapshot.Grids.Length,snapshot.IsFresh,snapshot.Complete,snapshot.Status,camera);
            if(!rooms.Complete)throw new InvalidOperationException(rooms.Status);
            Upload(grids,rooms.Rows,rooms.RowCount);Upload(cells,rooms.Cells,rooms.Cells.Length);
            var planet=PlanetAtmosphere.Copy();double time=Convert.ToDouble(scene[2]);var delta=camera-previousCamera;
            bool useHistory=history && unchecked(previousFrame+1)==frame && unchecked(previousGeneration+1)==generation && historyRevision==capturedHistory && interiorRevision==rooms.Revision && previousPlanet==planet.Center && previousRadius==(planet.IsValid?planet.TerrainRadius:0) && time>=previousTime;
            var views=GuestVolumeShadows.Views;var lightToCamera=MatrixD.Invert(views[2].WorldToShadow)*MatrixD.CreateTranslation(-camera);
            var prepared=VolumeFrameData.FromGuest(nativeFrame,lightToCamera,gw,gh,slices,distance,lightSize,128,media.Length,rooms.GridCount,(float)(time-previousTime),useHistory,debug,delta);
            var forward=Vector3.Normalize(new Vector3(-prepared.CameraToWorld.M31,-prepared.CameraToWorld.M32,-prepared.CameraToWorld.M33));
            float Ramp(float v,float a,float b)=>v<=a?1:v>=b?0:1-(v-a)/(b-a);
            float instant=history?Math.Min(Ramp((float)delta.Length(),4,80),Ramp(Math.Max(0,1-Vector3.Dot(previousForward,forward)),.02f,.45f)):1;
            safety=instant<=safety?instant:Math.Min(instant,safety+1f/16);if(safety<=.8f)prepared.Temporal.Y=0;
            var shadows=new VolumeFrameData.Shadows {A=(Matrix)views[0].CameraToShadow,B=(Matrix)views[1].CameraToShadow,C=(Matrix)views[2].CameraToShadow,Radii=new Vector4(128,1000,8000,1),Planet=new Vector4((Vector3)(planet.Center-camera),planet.IsValid?planet.TerrainRadius:0)};
            var constants=new byte[8][];constants[5]=VolumeFrameData.Bytes(shadows);constants[6]=VolumeFrameData.Bytes(prepared);constants[7]=VolumeFrameData.Providers(media,camera);
            var common=new ulong[48];common[0]=GuestBridgeClient.Depth;common[1]=tau.Id;common[2]=oldSource.Id;common[3]=oldCoefficients.Id;common[4]=integrated.Id;common[5]=source.Id;common[6]=coefficients.Id;common[7]=motionVolume.Id;common[8]=visibility.Id;common[9]=oldDepth.Id;
            for(int i=0;i<3;i++)common[20+i]=GuestVolumeShadows.Images[i];common[23]=grids.Id;common[24]=cells.Id;
            for(int i=0;i<media.Length;i++)for(int j=0;j<media[i].CatalogBindings.Length;j++){ulong id=GuestBridgeClient.Catalog(media[i].CatalogBindings[j]);if(id==0)throw new InvalidOperationException("Missing medium texture: "+media[i].CatalogBindings[j]);common[32+i*3+j]=id;}
            ulong[] Inputs(params ulong[] outputs)=>common.Select(id=>outputs.Contains(id)?0:id).ToArray();
            var passes=new List<object[]>();
            void Pass(int mark,int phase,int x,int y,int z,GuestProgram shader,ulong[] outputs,byte[][] blocks,ulong[] inputs)=>passes.Add(new object[]{mark,5,x,y,z,shader.Pipeline,outputs,blocks,inputs,phase});
            Pass(2,Before,lightSize,lightSize,1,light,new[]{tau.Id},constants,Inputs(tau.Id));
            Pass(2,Before,gw,gh,slices,inject,new[]{coefficients.Id,source.Id,motionVolume.Id,visibility.Id,representative.Id},constants,Inputs(coefficients.Id,source.Id,motionVolume.Id,visibility.Id,representative.Id));
            var integration=new byte[32];Buffer.BlockCopy(new[]{gw,gh,slices},0,integration,0,12);Buffer.BlockCopy(new[]{distance,1f},0,integration,12,8);
            Pass(2,Before,gw,gh,1,integrate,new[]{integrated.Id},new[]{integration},new[]{coefficients.Id,source.Id});
            var reconstruction=(ulong[])common.Clone();reconstruction[9]=representative.Id;
            Pass(2,Before,w,h,1,reconstruct,new[]{color.Id,motion.Id},constants,reconstruction);
            Pass(2,Before,w,h,1,reactive,new[]{reject.Id},null,new[]{motion.Id});
            Pass(2,After,w,h,1,composite,new[]{mergedColor.Id},null,new[]{GuestBridgeClient.Hdr,color.Id});Pass(2,After,w,h,1,copy,new[]{GuestBridgeClient.Hdr},null,new[]{mergedColor.Id});
            Pass(2,After,w,h,1,reactiveMerge,new[]{mergedReactive.Id},null,new[]{GuestBridgeClient.CatalogReactiveMask,reject.Id});Pass(2,After,w,h,1,copy,new[]{GuestBridgeClient.CatalogReactiveMask},null,new[]{mergedReactive.Id});
            Pass(4,Before,w,h,1,velocityMerge,new[]{mergedVelocity.Id},null,new[]{GuestBridgeClient.CatalogVelocity,motion.Id});Pass(4,Before,w,h,1,copy,new[]{GuestBridgeClient.CatalogVelocity},null,new[]{mergedVelocity.Id});
            if(!GuestBridgeClient.SubmitExtendedBatch(passes.ToArray()))throw new InvalidOperationException("Shared frame batch hook unavailable");
            if(!VolumeRegistryState.Commit(frame,revision,true,true,rooms.Complete,true,media))throw new InvalidOperationException("Shared frame commit rejected");
            foreach(var medium in media)medium.Interval?.Invoke(distance);
            active=true;activeFrame=frame;activeEpoch=epoch;foreach(var medium in media){suppressed.Add(medium.Id);suppressed.Add(medium.Id+".present");}
            Publish("volumeScatteringTransmittance",integrated.Id,gw,gh,slices);Publish("volumeMotion",motion.Id,w,h,1);Publish("volumeReactive",reject.Id,w,h,1);Publish("volumeRepresentativeDepth",representative.Id,gw,gh,1);Publish("volumeSunVisibility",visibility.Id,gw,gh,slices);
            (source,oldSource)=(oldSource,source);(coefficients,oldCoefficients)=(oldCoefficients,coefficients);(representative,oldDepth)=(oldDepth,representative);
            history=true;previousFrame=frame;previousGeneration=generation;previousTime=time;previousCamera=camera;previousForward=forward;previousPlanet=planet.Center;previousRadius=planet.IsValid?planet.TerrainRadius:0;historyRevision=capturedHistory;interiorRevision=rooms.Revision;
            VolumeRegistryState.SetStatus("Shared volume frame queued; "+media.Length+" providers, "+gw+"x"+gh+"x"+slices+"; "+rooms.Status);
        }catch(Exception error){active=false;history=false;suppressed.Clear();try{VolumeRegistryState.ResetIntervals();}catch(Exception reset){System.Diagnostics.Trace.WriteLine(reset);}GuestBridgeClient.Reset();VolumeRegistryState.SetStatus("Shared volume fallback: "+error.GetBaseException().Message);}
    }
    static void Ensure(int gw,int gh,int slices,int w,int h,int lightSize)
    {
        if(renderWidth!=w || renderHeight!=h || gridSlices!=slices || lightResolution!=lightSize)history=false;
        renderWidth=w;renderHeight=h;gridSlices=slices;lightResolution=lightSize;
        coefficients.Ensure(gw,gh,slices,1);oldCoefficients.Ensure(gw,gh,slices,1);source.Ensure(gw,gh,slices,1);oldSource.Ensure(gw,gh,slices,1);representative.Ensure(gw,gh,1,2);oldDepth.Ensure(gw,gh,1,2);integrated.Ensure(gw,gh,slices,1);motionVolume.Ensure(gw,gh,slices,1);visibility.Ensure(gw,gh,slices,8);tau.Ensure(lightSize,lightSize,128,8);
        color.Ensure(w,h,1,1);motion.Ensure(w,h,1,1);reject.Ensure(w,h,1,2,GuestBridgeClient.CatalogVolumeReactive);mergedColor.Ensure(w,h,1,1);mergedVelocity.Ensure(w,h,1,3);mergedReactive.Ensure(w,h,1,0);cameraVelocity.Ensure(w,h,1,3,GuestBridgeClient.CatalogVelocity);hostReactive.Ensure(w,h,1,0,GuestBridgeClient.CatalogReactiveMask);
    }
    static void Upload<T>(Image image,T[] values,int count) where T:struct
    {
        var bytes=new byte[count*Marshal.SizeOf<T>()];var pin=GCHandle.Alloc(values,GCHandleType.Pinned);try{Marshal.Copy(pin.AddrOfPinnedObject(),bytes,0,bytes.Length);}finally{pin.Free();}image.Ensure(bytes.Length,1,1,4);GuestBridgeClient.UpdateImage(image.Id,bytes);
    }
    static bool Current(){if(!active || GuestBridgeClient.ResourceEpoch!=activeEpoch)return false;var frame=new byte[344];return GuestBridgeClient.TryGetFrame(frame) && BitConverter.ToUInt32(frame,16)==activeFrame;}
    static void Publish(string name,ulong id,int w,int h,int d)
    {
        uint frame=activeFrame;long device=activeEpoch;
        BufferCatalog.Set(name,new GuestRendererImage(id,w,h,d,()=>activeFrame==frame && activeEpoch==device && Current()));
    }
}
