using System;
using System.IO;
using System.Linq;
using ClientPlugin.Backend;

static partial class NativeProbe
{
    public static void OwnedStageGpu(string assets)
    {
        if(!Enabled)throw new Exception("Owned-stage acceptance requires a real Vulkan device");
        string root=Path.Combine(Path.GetTempPath(),"AnomalyOwnedStages-"+Guid.NewGuid().ToString("N"));
        string oldContent=Environment.GetEnvironmentVariable("SE_GAME_CONTENT");
        string gameRoot=Path.Combine(Path.GetTempPath(),"AnomalyExternalGame-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(gameRoot,"Shaders"));
        string external=Path.Combine(gameRoot,"Shaders/External.hlsli");File.WriteAllText(external,"#define ExternalColor 2\n");
        Environment.SetEnvironmentVariable("SE_GAME_CONTENT",gameRoot);
        string overlay=Path.Combine(root,"Overlay/Anomaly.HistoryColor");Directory.CreateDirectory(overlay);
        string shader=Path.Combine(overlay,"HistoryCopy.hlsl");
        File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\"owned.fixture\",\"exclusive\":[\"Anomaly.HistoryColor\",\"anomaly.historycolor\"]}");
        var history=GuestPassList.Snapshot().Single(p=>p.Id=="anomaly.owned.HistoryCopy");
        ulong stock=history.Pipeline;int features=ClientPlugin.Rendering.GuestBridge.SupportedPacketFlags;
        long epoch=ClientPlugin.Rendering.GuestBridge.TestResourceEpoch;
        ClientPlugin.Rendering.GuestBridge.SupportedPacketFlags|=8192|262144;
        ClientPlugin.Rendering.GuestBridge.TestResourceEpoch=1;
        // Existing stock resources were created in this fixture context.
        foreach(var p in GuestPassList.Snapshot())p.ResourceEpoch=1;
        const ulong image=85500;ImageCreate(image,7,5,1,1);
        history.StorageImage=85501;history.HistoryImage=85502;history.ImageWidth=7;history.ImageHeight=5;history.ImageFormat=1;
        void Require(bool ok,string message){if(!ok)throw new Exception(message);}
        byte[] Draw()
        {
            ResetQueue();Queue(5,5,7,5,history.Pipeline,image,Array.Empty<byte>(),new ulong[14],0);Replay(5);
            return ReadRaw(image,7*5*8);
        }
        void Pixel(byte[] bytes,int x,int y,float r,float g,float b,float a)
        {
            float[] expected={r,g,b,a};for(int c=0;c<4;c++)
            {float value=(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes,((y*7+x)*4+c)*2));Require(Math.Abs(value-expected[c])<.003,"Owned-stage pixel mismatch at "+x+","+y+" channel "+c+": "+value+" expected "+expected[c]);}
        }
        void Prepare(){GuestNamedStages.Prepare(assets);Require(GuestNamedStages.Prepared!=null,"Owned-stage preparation failed: "+GuestNamedStages.Error);}
        void Boundary()=>GuestPassList.SubmitFrame(_=>{});
        void Retire()
        {
            foreach(var p in GuestPassList.TakeRetired())
            {
                Require(p.StorageImage!=85501&&p.HistoryImage!=85502,"Replacement retired images shared by the active generation");
                if(p.ResourceEpoch==1&&p.Pipeline!=0)
                {
                    var packet=new byte[48];Buffer.BlockCopy(new[]{48,1,2,1},0,packet,0,16);Buffer.BlockCopy(BitConverter.GetBytes(p.Pipeline),0,packet,16,8);Require(Resource(packet),"Pipeline retirement failed");
                }
            }
        }
        try
        {
            File.WriteAllText(shader,"#include \"External.hlsli\"\nfloat4 __pixel_shader(float4 p:SV_Position,float2 uv:TEXCOORD0):SV_Target{return float4(ExternalColor,.25,.5,-1);}");
            GuestPassList.RegisterRoot("owned.fixture",root);
            ClientPlugin.Rendering.GuestBridge.SupportedPacketFlags&=~262144;GuestNamedStages.Prepare(assets);
            Require(GuestNamedStages.Prepared==null&&GuestNamedStages.Error.Contains("bit18")&&history.Pipeline==stock,"Older native host did not refuse owned-stage activation actionably");
            ClientPlugin.Rendering.GuestBridge.SupportedPacketFlags|=262144;
            Require(Supports(262144),"Native owned-raster generation capability missing");
            ClientPlugin.Rendering.GuestBridge.AfterPipeline=()=>{File.WriteAllText(external,"#define ExternalColor 9\n");ClientPlugin.Rendering.GuestBridge.AfterPipeline=null;};
            Prepare();
            Require(history.Pipeline==stock&&history.Generation==null,"Preparation activated a generation before the boundary");
            string a=GuestNamedStages.Prepared;Boundary();Require(history.Generation==a&&history.Pipeline!=stock,"Prepared A was not atomically activated");
            Require(NamesShareGeneration(a),"Shared fullscreen dependency closure activated different generations");
            Pixel(Draw(),3,2,2,.25f,.5f,-1);
            Require(BitConverter.ToUInt64(Execution(history.Pipeline),16)>0,"Active generation lacks native execution evidence");Retire();
            string injection=Path.Combine(root,"Inject/Anomaly.HistoryColor/Color.hlsli");Directory.CreateDirectory(Path.GetDirectoryName(injection));
            File.WriteAllText(Path.Combine(root,"Inject/Common.h"),"#define FIXTURE_EXTRA 1\n");
            File.WriteAllText(injection,"#include \"Common.h\"\nfloat3 FixtureColor(float2 uv){return float3(uv,FIXTURE_COLOR*FIXTURE_EXTRA);}");
            File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\"owned.fixture\",\"defines\":[\"FIXTURE_COLOR\"]}");
            File.WriteAllText(shader,"float4 __pixel_shader(float4 p:SV_Position,float2 uv:TEXCOORD0):SV_Target{return float4(FixtureColor(uv),-1);}");
            // Exercise the named vertex, rather than allowing the synthetic guest vertex to hide it.
            File.WriteAllText(Path.Combine(root,"Inject/VertexCommon.h"),"float2 FixtureVertexUv(float2 q){return float2(1-q.x,q.y*.25+.1);}");
            File.WriteAllText(Path.Combine(overlay,"Fullscreen.hlsl"),"#include \"VertexCommon.h\"\nvoid __vertex_shader(uint id:SV_VertexID,out float4 p:SV_Position,out float2 uv:TEXCOORD0){float2 q=float2((id<<1)&2,id&2);p=float4(q*float2(2,-2)+float2(-1,1),0,1);uv=FixtureVertexUv(q);}");
            GuestPassList.RegisterRoot("owned.fixture",root);Prepare();string b=GuestNamedStages.Prepared;
            Require(b!=a&&history.Generation==a,"Preparing B changed active A");Pixel(Draw(),3,2,2,.25f,.5f,-1);
            Boundary();Require(history.Generation==b&&history.StorageImage==85501&&history.HistoryImage==85502,"Boundary failed to retain target ownership");
            var pixels=Draw();foreach(int y in new[]{0,2,4})foreach(int x in new[]{0,3,6})Pixel(pixels,x,y,1-(x+.5f)/7,(y+.5f)/5*.25f+.1f,1,-1);
            Require(NamesShareGeneration(b),"Fullscreen replacement omitted a shared vertex consumer");Retire();
            var state=GuestNamedStages.State("Anomaly.HistoryColor");
            Require(state.ActiveGeneration==b&&state.PreparedGeneration==null&&state.ProgramIds.Single()==history.Id,"Generation state conflated preparation with activation");
            ulong active=history.Pipeline;
            GuestPassList.RegisterRoot("owned.fixture",root);GuestNamedStages.Prepare(assets);
            Require(GuestNamedStages.Prepared==null&&history.Pipeline==active&&GuestNamedStages.Requested==b,"Unchanged registration created a redundant replacement generation");
            File.WriteAllText(shader,"#error owned-stage-replacement-failed\n");GuestPassList.RegisterRoot("owned.fixture",root);GuestNamedStages.Prepare(assets);Boundary();
            Require(GuestNamedStages.Prepared==null&&GuestNamedStages.Error.Contains("owned-stage-replacement-failed")&&history.Pipeline==active&&history.Generation==b,"Compile failure replaced the previous valid generation");
            Pixel(Draw(),3,2,.5f,.225f,1,-1);Retire();
            File.WriteAllText(shader,"Texture2D<float4> Bad:register(t30);float4 __pixel_shader(float4 p:SV_Position):SV_Target{return Bad.Load(int3(0,0,0));}");
            GuestPassList.RegisterRoot("owned.fixture",root);GuestNamedStages.Prepare(assets);Boundary();
            Require(GuestNamedStages.Prepared==null&&GuestNamedStages.Error!=null&&history.Pipeline==active,"Native descriptor rejection replaced active shaders");Retire();
            File.WriteAllText(shader,"float4 __pixel_shader():SV_Target{return float4(4,0,0,-1);}");GuestPassList.RegisterRoot("owned.fixture",root);Prepare();
            GuestPassList.UnregisterRoot("owned.fixture");Boundary();Require(history.Pipeline==active&&GuestNamedStages.Error.Contains("stale"),"Stale source revision activated");Retire();
            // An epoch change must refuse old device handles, even if the source revision is unchanged.
            GuestPassList.RegisterRoot("owned.fixture",root);Prepare();
            ClientPlugin.Rendering.GuestBridge.TestResourceEpoch=2;Boundary();
            Require(history.Pipeline==active&&GuestNamedStages.Prepared==null&&GuestNamedStages.Error.Contains("stale"),"Stale device epoch activated");
            ClientPlugin.Rendering.GuestBridge.TestResourceEpoch=1;Retire();GuestPassList.UnregisterRoot("owned.fixture");
            GuestNamedStages.Prepare(assets);Require(GuestNamedStages.Prepared!=null,"Unregister failed to prepare stock restoration: "+GuestNamedStages.Error);Boundary();
            Require(history.Generation!=b&&NamesShareGeneration(history.Generation),"Unregister failed to restore the owned stock generation atomically");Retire();
            Console.WriteLine("GPU PASS: owned-stage detached preparation, atomic dependency closure, named vertex orientation, injection/defines, HDR pixels, native execution, compile/binding rollback, stale refusal, stock restoration and pipeline-only retirement");
        }
        finally{ClientPlugin.Rendering.GuestBridge.AfterPipeline=null;Environment.SetEnvironmentVariable("SE_GAME_CONTENT",oldContent);if(Path.GetDirectoryName(gameRoot)==Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar))Directory.Delete(gameRoot,true);GuestPassList.UnregisterRoot("owned.fixture");ClientPlugin.Rendering.GuestBridge.SupportedPacketFlags=features;ClientPlugin.Rendering.GuestBridge.TestResourceEpoch=epoch;if(Path.GetDirectoryName(root)==Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar))Directory.Delete(root,true);}
    }
    static bool NamesShareGeneration(string generation)=>new[]{"CameraVelocity","LinearDepth","HiZ","HistoryCopy"}.All(name=>GuestPassList.Snapshot().Single(p=>p.Id=="anomaly.owned."+name).Generation==generation);
}
