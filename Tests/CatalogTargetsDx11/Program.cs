using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using ClientPlugin.Buffers;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using VRageRender;
using VRage.Render11.Resources;
using MapFlags=SharpDX.Direct3D11.MapFlags;
using Device=SharpDX.Direct3D11.Device;

class Program
{
    static int checks;
    static void Check(bool condition,string text){if(!condition)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
    const BindingFlags Static=BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    static Assembly core;
    static object Call(string type,string method,params object[] args)=>core.GetType("ClientPlugin."+type,true).GetMethod(method,Static).Invoke(null,args);
    static bool Ensure(string owner,string name,int width,int height,int format)=>Call("Buffers.CatalogImages","EnsureColorTarget",owner,name,width,height,format) is true;
    static void Clear(string name,float r,float g,float b,float a)=>Call("Buffers.CatalogImages","ClearColorTarget",name,r,g,b,a);
    static void Unpublish(string owner,string name)=>Call("Buffers.BufferCatalog","Unpublish",owner,name);
    static float Half(ushort bits){int sign=(bits&32768)==0?1:-1,exp=(bits>>10)&31,m=bits&1023;return (float)(sign*(exp==0?m*Math.Pow(2,-24):(1+m/1024.0)*Math.Pow(2,exp-15)));}
    static float[] Read(ISharedBuffer buffer)
    {
        var source=(Texture2D)((ISrvBindable)buffer.Srv).Resource;var desc=source.Description;int channels=desc.Format==Format.R16G16B16A16_Float||desc.Format==Format.R32G32B32A32_Float||desc.Format==Format.R8G8B8A8_UNorm?4:desc.Format==Format.R16G16_Float?2:1;
        bool half=desc.Format==Format.R16_Float||desc.Format==Format.R16G16_Float||desc.Format==Format.R16G16B16A16_Float,unorm=desc.Format==Format.R8G8B8A8_UNorm;
        desc.BindFlags=BindFlags.None;desc.Usage=ResourceUsage.Staging;desc.CpuAccessFlags=CpuAccessFlags.Read;
        using(var staging=new Texture2D(MyRender11.DeviceInstance,desc)){
            MyRender11.DeviceInstance.ImmediateContext.CopyResource(source,staging);DataStream stream;MyRender11.DeviceInstance.ImmediateContext.MapSubresource(staging,0,MapMode.Read,MapFlags.None,out stream);
            try{var data=new float[channels];for(int i=0;i<channels;i++)data[i]=unorm?stream.Read<byte>()/255f:half?Half(stream.Read<ushort>()):stream.Read<float>();return data;}
            finally{MyRender11.DeviceInstance.ImmediateContext.UnmapSubresource(staging,0);stream.Dispose();}
        }
    }
    static int Main(string[] args){try{Run(args[0]);Console.WriteLine("PASS "+checks+" actual-core/linked-DX11 catalog checks on "+typeof(object).Assembly.GetName().Version);return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
    static void Run(string anomaly)
    {
        string bin="T:/SteamLibrary/steamapps/common/SpaceEngineers/Bin64";
        AppDomain.CurrentDomain.AssemblyResolve+=(_,e)=>{string file=Path.Combine(bin,new AssemblyName(e.Name).Name+".dll");return File.Exists(file)?Assembly.LoadFrom(file):null;};
#if NET48
        string target="net48";
#else
        string target="net10.0";
#endif
        core=Assembly.LoadFrom(Path.Combine(anomaly,"ClientPlugin/bin/Release",target,"Anomaly.dll"));
        var host=core.GetType("ClientPlugin.Backend.RenderHost",true);host.GetField("committed",Static).SetValue(null,false);host.GetField("choice",Static).SetValue(null,0);
        foreach(var field in typeof(BufferCatalog).GetFields(BindingFlags.Public|BindingFlags.Static).Where(f=>f.IsLiteral&&f.FieldType==typeof(string))){var counterpart=core.GetType("ClientPlugin.Buffers.BufferCatalog",true).GetField(field.Name,BindingFlags.Public|BindingFlags.Static);Check(counterpart!=null&&Equals(field.GetRawConstantValue(),counterpart.GetRawConstantValue()),"matching public catalog constant: "+field.Name);}
        var corePublished=core.GetType("ClientPlugin.Buffers.PublishedBuffer",true);
        foreach(var property in typeof(PublishedBuffer).GetProperties(BindingFlags.Public|BindingFlags.Instance))Check(corePublished.GetProperty(property.Name)?.PropertyType==property.PropertyType,"matching published-buffer property: "+property.Name);
        foreach(var method in typeof(PublishedBuffer).GetMethods(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly).Where(m=>!m.IsSpecialName))Check(corePublished.GetMethod(method.Name,method.GetParameters().Select(p=>p.ParameterType).ToArray())!=null,"matching published-buffer method: "+method.Name);
        foreach(var constructor in typeof(PublishedBuffer).GetConstructors())Check(corePublished.GetConstructor(constructor.GetParameters().Select(p=>p.ParameterType).ToArray())!=null,"matching published-buffer constructor arity "+constructor.GetParameters().Length);
        Call("Backend.Dx11Call","Bind",Assembly.GetExecutingAssembly());Call("Buffers.BufferCatalog","BindDx11Catalog");
        const string owner="fixture.targets",pending="fixture.targets.pending";
        Check(Ensure(owner,pending,7,5,1),"pre-selection FP16 description accepted");Clear(pending,2.5f,.25f,.75f,-1);
        Check(!BufferCatalog.Publish("other",pending,new PublishedBuffer()),"direct satellite cannot steal pending core ownership");
        Check(MyManagers.RwTextures.Created==0,"pending registration creates no GPU resources");
        host.GetField("committed",Static).SetValue(null,true);Call("Buffers.BufferCatalog","ApplyDeferred");
        var retained=BufferCatalog.Active(pending);Check(!retained.IsAvailable,"device-free accepted target is not advertised as GPU-ready");
        Check(Ensure("fixture.cancel","fixture.targets.cancel",3,3,10),"second target can register before device availability");var canceled=BufferCatalog.Active("fixture.targets.cancel");Call("Buffers.BufferCatalog","UnpublishAll","fixture.cancel");Call("Buffers.BufferCatalog","ApplyDeferred");Check(!canceled.IsAvailable&&MyManagers.RwTextures.Created==0,"unregister before activation cancels all queued allocation work");
        using(var device=new Device(SharpDX.Direct3D.DriverType.Warp,DeviceCreationFlags.None)){
            MyRender11.DeviceInstance=device;MyRender11.RenderThread=System.Threading.Thread.CurrentThread;MyRender11.RC=new RenderContext();MyRender11.Drain();
            Check(retained.IsAvailable&&retained.Format==10&&Read(retained)[3]==-1,"queued legacy FP16 clear produces negative alpha on WARP");
            int created=MyManagers.RwTextures.Created;Check(Ensure(owner.ToUpperInvariant(),pending.ToUpperInvariant(),7,5,10)&&MyManagers.RwTextures.Created==created,"canonical/legacy idempotence preserves allocation");Check(Read(retained)[0]==2.5f,"identical ensure preserves initialized HDR pixels");
            foreach(var pair in new[]{new[]{0,28},new[]{1,10},new[]{2,2},new[]{10,10},new[]{28,28},new[]{34,34},new[]{41,41},new[]{54,54}}){
                string name="fixture.targets.format"+pair[0];Check(Ensure(owner,name,7,5,pair[0]),"format accepted: "+pair[0]);
                bool unorm=pair[1]==28;float[] expected=unorm?new[]{.75f,.25f,.5f,1f}:new[]{2.5f,.25f,.75f,-1f};Clear(name,expected[0],expected[1],expected[2],expected[3]);
                var buffer=BufferCatalog.Active(name);Check(buffer.IsAvailable&&buffer.Format==pair[1]&&((Texture2D)((IRtvTexture)buffer.Srv).Resource).Description.Format==(Format)pair[1],"real DX11 target honors DXGI format "+pair[1]);var actual=Read(buffer);Check(actual.Select((v,i)=>Math.Abs(v-expected[i])<=(unorm?1f/255:.001f)).All(v=>v),"WARP typed clear retains HDR/channel values for "+pair[1]);Unpublish(owner,name);
            }
            foreach(string bad in new[]{"velocity","HDRCOLOR","linearDepth","historyColor","playerDepth"})Check(!Ensure(owner,bad,1,1,10),"reserved target refuses: "+bad);
            created=MyManagers.RwTextures.Created;Check(!Ensure("other",pending,9,9,10)&&!Ensure(owner,"bad",1,1,999)&&!Ensure(" ","bad",1,1,10)&&MyManagers.RwTextures.Created==created,"ownership/format/malformed refusal allocates nothing");
            Unpublish("other",pending);Check(retained.IsAvailable&&Read(retained)[3]==-1,"wrong-owner unpublish preserves live target");
            var source=(Texture2D)((IRtvTexture)retained.Srv).Resource;var desc=source.Description;desc.Usage=ResourceUsage.Staging;desc.BindFlags=BindFlags.None;desc.CpuAccessFlags=CpuAccessFlags.Read;
            using(var oldRead=new Texture2D(device,desc))using(var deferred=new DeviceContext(device)){
                deferred.CopyResource(source,oldRead);using(var commands=deferred.FinishCommandList(false)){
                    Check(Ensure(owner,pending,9,3,10)&&retained.Width==9&&retained.Height==3,"resize refreshes the retained catalog wrapper");device.ImmediateContext.ExecuteCommandList(commands,false);DataStream data;device.ImmediateContext.MapSubresource(oldRead,0,MapMode.Read,MapFlags.None,out data);try{Check(Half(data.Read<ushort>())==2.5f,"recorded read survives manager disposal of replaced texture");}finally{device.ImmediateContext.UnmapSubresource(oldRead,0);data.Dispose();}
                }
            }
            MyManagers.RwTextures.FailNext=true;try{Ensure(owner,pending,11,3,10);throw new Exception("allocation refusal was hidden");}catch(TargetInvocationException e){Check(e.InnerException!=null,"allocation refusal is propagated");}
            MyRender11.Drain();Check(retained.IsAvailable&&retained.Width==9,"failed resize cannot activate a rejected size on the next frame");Ensure(owner,pending,9,3,10);Clear(pending,2,0,0,-1);
            var uploaded=new PublishedBuffer();uploaded.PublishRgba8(new byte[]{42,0,0,255},1,1);Check(BufferCatalog.Publish(owner,"fixture.targets.convert",uploaded),"uploaded source registered before target conversion");
            MyManagers.RwTextures.FailNext=true;try{Ensure(owner,"fixture.targets.convert",3,3,10);throw new Exception("conversion refusal was hidden");}catch(TargetInvocationException){ }
            MyRender11.Drain();Check(ReferenceEquals(uploaded,BufferCatalog.Active("fixture.targets.convert"))&&uploaded.IsAvailable&&Math.Abs(Read(uploaded)[0]-42f/255)<.001,"failed target conversion preserves the old upload and pixels");
            Check(Ensure(owner,"fixture.targets.convert",3,3,10)&&!uploaded.IsAvailable,"successful conversion releases the replaced upload");
            var control=new PublishedBuffer();control.PublishVolumeRgba8(new byte[32],2,2,2);control.PublishStructuredFloats(new float[]{1,2,3,4,5,6,7,8},16);Check(control.Depth==1&&control.Width==2,"DX11 control resets volume depth for structured mode");control.Clear();
            var modes=Activator.CreateInstance(corePublished);corePublished.GetMethod("PublishVolumeRgba8").Invoke(modes,new object[]{new byte[32],2,2,2});corePublished.GetMethod("PublishStructuredFloats").Invoke(modes,new object[]{new float[]{1,2,3,4,5,6,7,8},16});
            Check((int)corePublished.GetProperty("Depth").GetValue(modes)==1,"core volume-to-structured transition matches DX11 depth");
            var mipField=corePublished.GetField("mipData",BindingFlags.Instance|BindingFlags.NonPublic);var structField=corePublished.GetField("structured",BindingFlags.Instance|BindingFlags.NonPublic);
            Check(corePublished.GetProperty("Rgba").GetValue(modes)==null&&mipField.GetValue(modes)==null,"structured mode releases inactive pixel payloads");
            var core2d=corePublished.GetMethod("PublishRgba8");var coreVolume=corePublished.GetMethod("PublishVolumeRgba8");var coreMips=corePublished.GetMethod("PublishVolumeMips");
            core2d.Invoke(modes,new object[]{new byte[]{17,0,0,255},1,1});Check(structField.GetValue(modes)==null&&(int)corePublished.GetProperty("Depth").GetValue(modes)==1&&corePublished.GetProperty("IsStructured").GetValue(modes) is false,"2D mode releases inactive structured payload");
            var retainedPointer=(IntPtr)corePublished.GetProperty("NativeResource").GetValue(modes);
            core2d.Invoke(modes,new object[]{new byte[3],1,1});coreVolume.Invoke(modes,new object[]{new byte[31],2,2,2});coreMips.Invoke(modes,new object[]{new byte[][]{new byte[32],new byte[3]},2});
            corePublished.GetMethod("PublishStructuredFloats").Invoke(modes,new object[]{new float[]{1,2,3},16});Check((IntPtr)corePublished.GetProperty("NativeResource").GetValue(modes)==retainedPointer,"malformed uploads preserve the preceding valid resource");
            coreMips.Invoke(modes,new object[]{new byte[][]{new byte[36],new byte[]{201,0,0,255,99,99,99,99}},2});var stored=(byte[][])mipField.GetValue(modes);Check(stored[0].Length==32&&stored[1].Length==4,"mip copies ignore trailing padding as DX11 subresource uploads do");
            core2d.Invoke(modes,new object[]{new byte[]{19,0,0,255},1,1});Check(mipField.GetValue(modes)==null,"raster replacement releases inactive mip chains");
            coreVolume.Invoke(modes,new object[]{new byte[32],2,2,2});corePublished.GetMethod("Publish").Invoke(modes,new object[]{retained.Srv,retained.NativeResource,retained.Width,retained.Height});
            Check((int)corePublished.GetProperty("Depth").GetValue(modes)==1&&corePublished.GetProperty("Rgba").GetValue(modes)==null,"raw 2D publication resets depth and owned CPU payload");corePublished.GetMethod("Clear").Invoke(modes,null);Check(retained.IsAvailable,"clearing a borrowed publication preserves caller ownership");
            var nodes=Activator.CreateInstance(corePublished);corePublished.GetMethod("PublishStructuredFloats").Invoke(nodes,new object[]{new float[]{1,2,3,4,5,6,7,8},16});
            Check(corePublished.GetProperty("IsAvailable").GetValue(nodes) is true&&corePublished.GetProperty("IsStructured").GetValue(nodes) is true&&(int)corePublished.GetProperty("Stride").GetValue(nodes)==16&&(int)corePublished.GetProperty("Width").GetValue(nodes)==2&&(IntPtr)corePublished.GetProperty("NativeResource").GetValue(nodes)!=IntPtr.Zero,"core structured metadata matches actual DX11 resource and element count");corePublished.GetMethod("Clear").Invoke(nodes,null);
            var outer=Activator.CreateInstance(core.GetType("ClientPlugin.Buffers.PublishedBuffer",true));outer.GetType().GetMethod("PublishRgba8").Invoke(outer,new object[]{new byte[]{64,0,0,255},1,1});
            Check(Call("Buffers.BufferCatalog","Publish",owner,"fixture.targets.convert",outer) is true,"core upload replaces a satellite-owned target");Call("Buffers.BufferCatalog","ApplyDeferred");MyRender11.Drain();
            Check(BufferCatalog.Active("fixture.targets.convert") is PublishedBuffer&&Math.Abs(Read(BufferCatalog.Active("fixture.targets.convert"))[0]-64f/255)<.001,"target-to-upload conversion does not resurrect a core target description");Unpublish(owner,"fixture.targets.convert");
            var oldResource=((IRtvTexture)retained.Srv).Resource;MyManagers.RwTextures.Recreate();MyRender11.Drain();Check(retained.IsAvailable&&!ReferenceEquals(oldResource,((IRtvTexture)retained.Srv).Resource)&&Read(retained).All(v=>v==0),"device resource recreation refreshes native pointer and initializes pixels");
            using(var replacementDevice=new Device(SharpDX.Direct3D.DriverType.Warp,DeviceCreationFlags.None)){
                MyRender11.DeviceInstance=replacementDevice;MyRender11.RC=new RenderContext();MyManagers.RwTextures.Recreate();MyRender11.Drain();
                var resourceDevice=((ISrvBindable)retained.Srv).Resource.Device;Check(resourceDevice.NativePointer==replacementDevice.NativePointer&&Read(retained).All(v=>v==0),"retained target follows actual D3D11 device replacement and initialization");
                MyRender11.DeviceInstance=device;MyRender11.RC=new RenderContext();MyManagers.RwTextures.Recreate();MyRender11.Drain();
            }
            Task.Run(()=>{Check(Ensure(owner,"fixture.targets.worker",3,5,54),"worker registration accepted");Clear("fixture.targets.worker",3.5f,0,0,0);}).GetAwaiter().GetResult();Check(!BufferCatalog.Active("fixture.targets.worker").IsAvailable,"worker registration defers all pool work");MyRender11.Drain();Check(Read(BufferCatalog.Active("fixture.targets.worker"))[0]==3.5f,"renderer queue creates and clears worker request");
            var worker=BufferCatalog.Active("fixture.targets.worker");int activeBefore=MyManagers.RwTextures.Active.Count;Task.Run(()=>Unpublish(owner,"fixture.targets.worker")).GetAwaiter().GetResult();
            Check(!worker.IsAvailable&&MyManagers.RwTextures.Active.Count==activeBefore,"worker unpublish invalidates publication before deferred pool retirement");MyRender11.Drain();Check(MyManagers.RwTextures.Active.Count==activeBefore-1,"renderer queue returns worker target to its owning pool");
            BufferCatalog.Unpublish(owner,pending);Call("Buffers.BufferCatalog","ApplyDeferred");MyRender11.Drain();Check(!retained.IsAvailable&&!BufferCatalog.Active(pending).IsAvailable,"direct satellite unpublish cancels core replay and retained view");
            for(int i=0;i<16;i++){Check(Ensure(owner,pending,7+i,5,10),"reload target accepted "+i);Call("Buffers.BufferCatalog","UnpublishAll",owner);MyRender11.Drain();Check(MyManagers.RwTextures.Active.Count==0,"pool returns to zero after unregister cycle "+i);}
            Check(MyManagers.RwTextures.Created==MyManagers.RwTextures.Disposed,"all catalog-owned manager allocations were returned");
            var asmName=new AssemblyName("Fixture.OlderDx11");
#if NET48
            var old=AppDomain.CurrentDomain.DefineDynamicAssembly(asmName,AssemblyBuilderAccess.Run);
#else
            var old=AssemblyBuilder.DefineDynamicAssembly(asmName,AssemblyBuilderAccess.Run);
#endif
            var type=old.DefineDynamicModule("old").DefineType("ClientPlugin.Buffers.CatalogImages",TypeAttributes.Public|TypeAttributes.Abstract|TypeAttributes.Sealed);var invoked=type.DefineField("Invoked",typeof(bool),FieldAttributes.Public|FieldAttributes.Static);
            var method=type.DefineMethod("EnsureColorTarget",MethodAttributes.Public|MethodAttributes.Static,typeof(bool),new[]{typeof(string),typeof(string),typeof(int),typeof(int),typeof(int)});var il=method.GetILGenerator();il.Emit(OpCodes.Ldc_I4_1);il.Emit(OpCodes.Stsfld,invoked);il.Emit(OpCodes.Ldc_I4_1);il.Emit(OpCodes.Ret);var olderType=type.CreateType();
            Call("Backend.Dx11Call","Bind",old);try{Ensure(owner,"fixture.targets.old",1,1,10);throw new Exception("older satellite was silently accepted");}catch(TargetInvocationException error){Check(error.InnerException is NotSupportedException&&error.InnerException.Message.Contains("Update Anomaly.Dx11.dll"),"older satellite refusal is actionable");}
            Check(!(bool)olderType.GetField("Invoked").GetValue(null),"older satellite does not receive a format request it would misinterpret");Call("Backend.Dx11Call","Bind",Assembly.GetExecutingAssembly());

            MyRender11.RC=null;MyRender11.DeviceInstance=null;
        }
    }
}
