using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using ClientPlugin.Backend;
using ClientPlugin.Buffers;
using ClientPlugin.Rendering;
using ClientPlugin.Rendering.Scene;
using VRageMath;

static partial class Program
{
    static void PointShadows(string assets)
    {
        int savedFlags = GuestBridge.SupportedPacketFlags;
        Require(NativeProbe.Supports(448), "native shadow geometry features missing; rebuild native");
        var all = GuestPassList.Snapshot();
        var geometry = all.Single(p => p.Id == GuestPointShadows.GeometryId);
        var header = all.Single(p => p.Id == GuestPointShadows.HeaderId);
        var programs = new List<GuestProgram> { geometry, header };
        int savedWidth = SceneView.Width, savedHeight = SceneView.Height;
        const int face = 32, view = 64;
        const double world = 1e12;
        SceneView.Width = SceneView.Height = view;
        var frame = new byte[344];
        void F(int at, float value) => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, frame, at, 4);
        void U(int at, uint value) => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, frame, at, 4);
        U(8,view); U(12,view); F(60,1); F(64,1); F(264,1); F(284,1); F(304,1); F(324,1);
        var projection = MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,1,.02,1000);
        var projectionBytes = GuestPointShadows.GeometryConstants(projection,MatrixD.Identity,0,0,view,view,0,0,0,0);
        Buffer.BlockCopy(projectionBytes,0,frame,136,64);
        GuestBridge.FrameOverride = frame;
        var lights = new double[] {world+4,0,0,20, world-4,0,0,20, world,4,0,20, world,-4,0,20, world,0,4,20, world,0,-4,20};
        var ids = new uint[] {10,11,12,13,14,15};
        // Avoid exactly tangent ray/edge ties: raster top-left coverage at a
        // zero-length slab intersection is not an analytic volume oracle.
        var bounds = new double[] {world,0,0,1,1,1, world-1.5,0,2.5,.41,.83,.37};
        var anchor = new double[] {world,0,0,world,0,8};
        GuestBridge.ShadowInputs = new object[] {lights,ids,bounds,new uint[]{20,30},anchor};
        var actorWorld = new double[] {1,0,0,0,0,1,0,0,0,0,1,0,world,0,0,1};
        var model = new float[8*8];
        for (int i=0;i<8;i++) {model[i*8]=(i&1)==0?-1:1; model[i*8+1]=(i&2)==0?-1:1; model[i*8+2]=(i&4)==0?-1:1;}
        uint[] triangles = {1,3,7,1,7,5,4,6,2,4,2,0,2,6,7,2,7,3,4,0,1,4,1,5,4,5,7,4,7,6,1,0,2,1,2,3};
        var blend = Enumerable.Repeat(new Vector4I(0,0,0,0),8).ToArray();
        var weights = Enumerable.Repeat(new Vector4(1,0,0,0),8).ToArray();
        float[] Copy(Matrix bone) => GuestCharacterGeometry.CopyTriangles(model,blend,weights,new[]{bone},new[]{triangles});
        float[] mesh = Copy(Matrix.Identity);
        GuestBridge.Character = new object[] {mesh,mesh,actorWorld,new double[]{world,0,0,1,1,1},"synthetic controlled skin pose"};
        var relative = GuestPointShadows.RelativeWorld(actorWorld,new Vector3D(world+4,0,0));
        Require(relative.Translation.X == -4, "light origin lost double precision");
        var partialWeights = Enumerable.Repeat(new Vector4(.25f,0,0,0),8).ToArray();
        var normalized = GuestCharacterGeometry.CopyTriangles(model,blend,partialWeights,new[]{Matrix.CreateTranslation(2,0,0)},new[]{triangles});
        Require(normalized[0] == 3, "skin position did not divide by homogeneous weight");
        var fourIndices=Enumerable.Repeat(new Vector4I(0,1,2,3),8).ToArray();
        var fourWeights=Enumerable.Repeat(new Vector4(.1f,.2f,.3f,.4f),8).ToArray();
        var mixed=GuestCharacterGeometry.CopyTriangles(model,fourIndices,fourWeights,
            new[]{Matrix.Identity,Matrix.CreateTranslation(2,0,0),Matrix.CreateScale(2),Matrix.CreateRotationZ((float)(Math.PI/2))},new[]{triangles});
        Require(Math.Abs(mixed[0]-1.7f)<1e-5 && Math.Abs(mixed[1]+.5f)<1e-5 && Math.Abs(mixed[2]+1.3f)<1e-5,"four-bone blend did not match stock homogeneous skinning");
        bool rejected = false;
        try { GuestCharacterGeometry.CopyTriangles(model,blend,new Vector4[8],new[]{Matrix.Identity},new[]{triangles}); }
        catch (InvalidOperationException) { rejected=true; }
        Require(rejected, "missing skin pose was silently treated as valid geometry");
        var unusedVertex = model.Concat(new float[8]).ToArray();
        var unusedWeights = weights.Concat(new[] { Vector4.Zero }).ToArray();
        Require(GuestCharacterGeometry.CopyTriangles(unusedVertex,blend,unusedWeights,new[]{Matrix.Identity},new[]{triangles}).SequenceEqual(mesh),
            "unreferenced zero-weight vertex invalidated opaque indexed geometry");
        GuestOccupancy.LocalActorId=20; GuestOccupancy.RequestPointShadows(6,face);
        GuestBridge.SupportedPacketFlags=savedFlags & ~448;
        GuestBridgeClient.Submit(programs);
        Require(BufferCatalog.Active("pointShadowAtlas")==null && geometry.Status=="capability-missing", "older guest advertised a character atlas");
        GuestBridge.SupportedPacketFlags=savedFlags | 511;
        GuestBridge.MirrorGpu=NativeProbe.Enabled;
        void Submit()
        {
            GuestBridgeClient.Submit(programs);
            Require(BufferCatalog.Active("pointShadowAtlas") is GuestRendererImage && BufferCatalog.Active("playerDepth") is GuestRendererImage,
                "point products unavailable: "+GuestPointShadows.Status);
            var packets=GuestBridge.Packets.Where(p=>p.Pipeline==geometry.Pipeline).ToArray();
            Require(packets.Length==37, "character was not stamped on all six faces for six lights plus mask");
            Require(GuestBridge.Packets.Last().Pipeline==header.Pipeline, "atlas header preceded its mesh draws");
            if(NativeProbe.Enabled)NativeProbe.Replay(1);
        }
        byte[] ReadAtlas()=>NativeProbe.ReadRaw(geometry.StorageImage,geometry.ImageWidth*geometry.ImageHeight*16);
        byte[] ReadMask(){var maskImage=(GuestRendererImage)BufferCatalog.Active("playerDepth");return NativeProbe.ReadRaw(maskImage.ImageId,maskImage.Width*maskImage.Height*4);}
        void CheckAtlas(byte[] image,float shift,bool includeBoxes)
        {
            double[] selected=GuestOccupancy.SelectedLightRecords;
            for(int row=0;row<selected.Length/4;row++)
            {
                var light=new Vector3D(selected[row*4]-world,selected[row*4+1],selected[row*4+2]);
                for(int f=0;f<6;f++)for(int y=0;y<face;y++)for(int x=0;x<face;x++)
                {
                    double u=2*(x+.5)/face-1,v=2*(y+.5)/face-1;
                    Vector3D direction;
                    switch(f){case 0:direction=new Vector3D(1,v,-u);break;case 1:direction=new Vector3D(-1,v,u);break;case 2:direction=new Vector3D(u,1,-v);break;case 3:direction=new Vector3D(u,-1,v);break;case 4:direction=new Vector3D(u,v,1);break;default:direction=new Vector3D(-u,v,-1);break;}
                    direction.Normalize();
                    double expected=RayBox(light,direction,new Vector3D(-1+shift,-1,-1),new Vector3D(1+shift,1,1));
                    if(includeBoxes)expected=Math.Min(expected,RayBox(light,direction,new Vector3D(-1.91,-.83,2.13),new Vector3D(-1.09,.83,2.87)));
                    int pixel=((1+row*face+y)*geometry.ImageWidth+f*face+x)*16;
                    float actual=BitConverter.ToSingle(image,pixel);
                    if(Math.Abs(actual-expected)>.003)throw new Exception("atlas ray mismatch row="+row+" face="+f+" xy="+x+","+y+" actual="+actual+" expected="+expected);
                    Require(BitConverter.ToSingle(image,pixel+4)==actual && BitConverter.ToSingle(image,pixel+8)==actual, "atlas distance channels diverged");
                }
                for(int axis=0;axis<3;axis++)Require(Math.Abs(BitConverter.ToSingle(image,row*16+axis*4)-(selected[row*4+axis]-anchor[axis+3]))<.01,"light header origin mismatch");
            }
            Require(BitConverter.ToSingle(image,64*16+12)==1 && BitConverter.ToSingle(image,65*16+12)==1,"mesh/bounds header metadata not valid");
            Require(Math.Abs(BitConverter.ToSingle(image,64*16)-shift)<.001 && BitConverter.ToSingle(image,64*16+8)==-8,"caster header did not follow pose/camera");
        }
        void CheckMask(byte[] image,float shift)
        {
            for(int y=0;y<view;y++)for(int x=0;x<view;x++)
            {
                var ray=new Vector3D(2*(x+.5)/view-1,1-2*(y+.5)/view,-1);ray.Normalize();
                double expected=RayBox(new Vector3D(0,0,8),ray,new Vector3D(-1+shift,-1,-1),new Vector3D(1+shift,1,1));
                float actual=BitConverter.ToSingle(image,(y*view+x)*4);
                if(Math.Abs(actual-expected)>.003)throw new Exception("player mask mismatch at "+x+","+y+" actual="+actual+" expected="+expected);
            }
        }
        Submit();
        byte[] first=null;
        if(NativeProbe.Enabled){first=ReadAtlas();CheckAtlas(first,0,false);CheckMask(ReadMask(),0);}
        if(NativeProbe.Enabled)
        {
            string source=Path.Combine(Path.GetTempPath(),"anomaly-guest-sources","point-sampler-probe.hlsl");
            File.WriteAllText(source,"#include <AnomalyPointShadows.hlsli>\nTexture2D<float4> Atlas : register(t7); float4 __pixel_shader(float4 p:SV_Position,float2 uv:TEXCOORD0):SV_Target0 { uint row=AnomalyPointShadowMatchRow(Atlas,float3(4,0,-8),20); float visibility=AnomalyPointShadowVisibility(Atlas,row,float3(uv.x<.5?-1:1,0,0),8,.01); return float4(visibility,1-visibility,0,1); }\n");
            GuestPassList.AddProgram("fixture.pointsampler","AfterLighting",source,"PublishOnly");GuestCompiler.CompilePending(assets);
            var sampler=GuestPassList.Snapshot().Single(p=>p.Id=="fixture.pointsampler");Require(sampler.Pipeline!=0,"shared point sampler pipeline missing: "+sampler.Status+"; "+sampler.Error);
            NativeProbe.ImageCreate(930,8,8,1,0);NativeProbe.ResetQueue();var sampled=new ulong[14];sampled[7]=geometry.StorageImage;
            NativeProbe.Queue(1,5,8,8,sampler.Pipeline,930,Array.Empty<byte>(),sampled,0);NativeProbe.Replay(1);
            var pixels=NativeProbe.ReadRaw(930,8*8*4);
            for(int y=0;y<8;y++)for(int x=0;x<8;x++){int at=(y*8+x)*4;Require(pixels[at]==(x<4?0:255)&&pixels[at+1]==(x<4?255:0),"shared point-shadow consumer did not distinguish blocked/lit receivers");}
        }
        anchor[3]=world+1000000;anchor[4]=20;anchor[5]=800;
        Submit();
        if(NativeProbe.Enabled)
        {
            var observed=ReadAtlas();
            for(int i=geometry.ImageWidth*16;i<observed.Length;i++)Require(observed[i]==first[i],"atlas silhouette changed with observer position");
        }
        anchor[3]=world;anchor[4]=0;anchor[5]=8;
        mesh=Copy(Matrix.CreateTranslation(.5f,0,0));bounds[0]=anchor[0]=world+.5;
        GuestBridge.Character=new object[]{mesh,mesh,actorWorld,new double[]{world+.5,0,0,1,1,1},"animated skin pose"};
        Submit();if(NativeProbe.Enabled){CheckAtlas(ReadAtlas(),.5f,false);CheckMask(ReadMask(),.5f);}
        GuestPointShadows.RequestWorldBoxes(true);
        Submit();if(NativeProbe.Enabled)CheckAtlas(ReadAtlas(),.5f,true);
        U(8,80);U(12,48);SceneView.Width=80;SceneView.Height=48;
        var resizedProjection=GuestPointShadows.GeometryConstants(MatrixD.CreatePerspectiveFieldOfView(Math.PI/2,80.0/48,.02,1000),MatrixD.Identity,0,0,80,48,0,0,0,0);
        Buffer.BlockCopy(resizedProjection,0,frame,136,64);Submit();
        var resizedMask=(GuestRendererImage)BufferCatalog.Active("playerDepth");Require(resizedMask.Width==80&&resizedMask.Height==48,"player depth did not follow scene resize");
        GuestPointShadows.RequestWorldBoxes(false);
        GuestBridge.Character=new object[]{Array.Empty<float>(),Array.Empty<float>(),Array.Empty<double>(),Array.Empty<double>(),"no controlled character"};
        GuestBridgeClient.Submit(programs);Require(BufferCatalog.Active("pointShadowAtlas")!=null&&BufferCatalog.Active("playerDepth")!=null,"spectator frame did not publish cleared products");
        if(NativeProbe.Enabled)
        {
            NativeProbe.Replay(1);var empty=ReadAtlas();for(int i=geometry.ImageWidth*16;i<empty.Length;i+=16)Require(BitConverter.ToSingle(empty,i)==100000,"spectator atlas retained old suit geometry");
            var emptyMask=ReadMask();for(int i=0;i<emptyMask.Length;i+=4)Require(BitConverter.ToSingle(emptyMask,i)==100000,"spectator mask retained old suit geometry");
            Require(BitConverter.ToSingle(empty,64*16+12)==0&&BitConverter.ToSingle(empty,65*16+12)==0,"spectator header claimed a valid mesh");
        }
        GuestBridge.Character=null;GuestBridgeClient.Submit(programs);
        Require(BufferCatalog.Active("pointShadowAtlas")==null && BufferCatalog.Active("playerDepth")==null,"missing character API retained stale products");
        GuestOccupancy.RequestPointShadows(0,face);GuestPointShadows.RequestWorldBoxes(false);GuestBridgeClient.Submit(programs);
        GuestBridge.MirrorGpu=false;GuestBridge.FrameOverride=null;GuestBridge.Character=null;
        SceneView.Width=savedWidth;SceneView.Height=savedHeight;
        GuestBridge.SupportedPacketFlags=savedFlags;
        Console.WriteLine((NativeProbe.Enabled?"GPU PASS: ":"PASS: ")+"shared character atlas/mask submission, skin equation, six faces, independent observer, animated pose, world boxes and missing-producer cleanup");
    }

    static double RayBox(Vector3D origin,Vector3D direction,Vector3D minimum,Vector3D maximum)
    {
        double near=0,far=double.PositiveInfinity;
        for(int axis=0;axis<3;axis++)
        {
            double o=axis==0?origin.X:axis==1?origin.Y:origin.Z,d=axis==0?direction.X:axis==1?direction.Y:direction.Z,
                lo=axis==0?minimum.X:axis==1?minimum.Y:minimum.Z,hi=axis==0?maximum.X:axis==1?maximum.Y:maximum.Z;
            if(Math.Abs(d)<1e-12){if(o<lo||o>hi)return 100000;continue;}
            double a=(lo-o)/d,b=(hi-o)/d;if(a>b){double swap=a;a=b;b=swap;}
            near=Math.Max(near,a);far=Math.Min(far,b);if(far<near)return 100000;
        }
        return near>=.04?near:far>=.04?far:100000;
    }
}
