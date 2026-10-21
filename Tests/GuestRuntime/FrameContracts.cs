using System;
using ClientPlugin.Backend;
using ClientPlugin.Rendering;
using VRageMath;
using System.IO;
using System.Runtime.InteropServices;

static partial class Program
{
    static void FrameContracts()
    {
        GuestBridge.Ready=true;GuestBridge.SupportedPacketFlags|=32768;
        var light=new FrameEnvironment.Lighting(new Vector3(2f,1f,.5f),new Vector3(0,-4,0),.25f,.3f);
        Require(light.SunToward==Vector3.Up && light.SunDiffuse==1 && light.SunColor.X==2, "sun sign/normalization/raw color/diffuse changed");
        Require(Math.Abs(light.SkyLuma-(2*.2126f+.7152f+.5f*.0722f)*.3f)<1e-6f && light.SkyAmbient.X==.056f,"ambient policy changed");
        Require(new FrameEnvironment.Lighting(Vector3.One,Vector3.Zero,2,1).SunToward==Vector3.Up,"degenerate sun fallback changed");
        var motion=new FrameEnvironment.Motion();
        Require(motion.Capture(new Vector3D(1e12,0,0),Matrix.Identity)==1,"first-frame safety changed");
        Require(motion.Capture(new Vector3D(1e12+80,0,0),Matrix.Identity)==0 && motion.Delta.X==80,"camera cut/double precision delta lost");
        Require(motion.Capture(new Vector3D(1e12+80,0,0),Matrix.Identity)==1f/16,"safety release envelope changed");
        motion.Reset();motion.Capture(Vector3D.Zero,Matrix.Identity);
        Require(motion.Capture(Vector3D.Zero,Matrix.CreateRotationY(MathHelper.PiOver2))==0,"rotation did not reject history");
        var frame=new byte[344];
        void U(int at,uint value)=>Buffer.BlockCopy(BitConverter.GetBytes(value),0,frame,at,4);
        void F(int at,float value)=>Buffer.BlockCopy(BitConverter.GetBytes(value),0,frame,at,4);
        U(16,4);U(20,1);F(264,1);F(284,1);F(304,1);F(324,1);
        GuestBridge.FrameOverride=frame;
        GuestBridge.FrameInputs=new object[]{new double[]{1e12,0,0},new float[]{2,1,.5f,.25f,0,-4,0,.3f}};
        ClientPlugin.Shaders.PlanetAtmosphere.Current=new ClientPlugin.Shaders.PlanetAtmosphere.Snapshot {IsValid=true,AirTop=63000,VisualCeil=65000};
        GuestFrame.ResetDevice();Require(GuestFrame.Capture(),"frame snapshot unavailable");
        int reads=GuestBridge.FrameReads;
        Require(GuestFrame.Vec3(88)==Vector3.Up && GuestFrame.F32(44)==63000 && GuestFrame.F32(48)==65000,"shared guest environment missing");
        for(int i=0;i<1000;i++)Require(GuestFrame.Open && GuestFrame.U32(16)==4,"frame getter changed snapshot");
        Require(GuestBridge.FrameReads==reads,"getter performed native frame reads");
        ((float[])GuestBridge.FrameInputs[1])[0]=9;Require(GuestFrame.Capture() && GuestFrame.F32(72)==2,"same-frame recapture changed immutable snapshot");
        U(16,5);((double[])GuestBridge.FrameInputs[0])[0]+=80;
        Require(GuestFrame.Capture() && GuestFrame.F32(32)==0 && GuestFrame.F32(120)==80,"next frame did not update safety and delta");
        Require(BitConverter.ToSingle(GuestBridge.EnvironmentPacket,60)==0 && BitConverter.ToUInt32(GuestBridge.EnvironmentPacket,8)==5,"transport lost zero safety or frame key");
        GuestFrame.Invalidate();Require(!GuestFrame.History,"invalidation retained current-frame history");
        GuestBridge.FrameOverride=null;GuestBridge.FrameInputs=null;GuestFrame.ResetDevice();
        Console.WriteLine("PASS: shared lighting, large-coordinate camera motion/rotation, safety release, planet radii, immutable frame snapshot and native environment packet");
    }

    static void EnvironmentGpu(string assets)
    {
        if (!NativeProbe.Enabled) return;
        string shader=Path.Combine(Path.GetTempPath(),"anomaly-guest-sources","environment-contract.hlsl");
        File.WriteAllText(shader,"cbuffer Extras:register(b6){float4 E[20];} float4 main():SV_Target{return float4(E[16].x,E[17].y,E[2].z,E[19].x/100000);}");
        GuestPassList.AddProgram("fixture.environment","AfterLighting",shader,"PublishOnly");
        GuestCompiler.CompilePending(assets);
        foreach(var pass in GuestPassList.Snapshot())if(pass.Id=="fixture.environment")
        {
            Require(pass.Pipeline!=0,"environment probe failed to compile");NativeProbe.EnvironmentContract(pass.Pipeline);
        }
    }
}

static partial class NativeProbe
{
    public static void EnvironmentContract(ulong pipeline)
    {
        var begin=new byte[144];for(int i=0;i<4;i++)Buffer.BlockCopy(BitConverter.GetBytes(1f),0,begin,i*20,4);
        Buffer.BlockCopy(BitConverter.GetBytes(-4f),0,begin,84,4);
        void Invoke(string export,byte[] packet){var pin=GCHandle.Alloc(packet,GCHandleType.Pinned);try{Check(Fn<ResourceFn>(export)(context,pin.AddrOfPinnedObject()));}finally{pin.Free();}}
        ResetQueue();Invoke("sevk_begin_frame",begin);
        var frame=new byte[344];Buffer.BlockCopy(new[]{344,1},0,frame,0,8);Invoke("sevk_get_guest_frame",frame);
        if(Math.Abs(BitConverter.ToSingle(frame,92)-1f)>1e-6)throw new Exception("Guest sun fallback is not normalized toward-sun");
        var packet=new byte[96];Buffer.BlockCopy(new[]{96,1},0,packet,0,8);Buffer.BlockCopy(frame,16,packet,8,4);
        void F(int at,float value)=>Buffer.BlockCopy(BitConverter.GetBytes(value),0,packet,at,4);
        F(16,2);F(20,1);F(24,.5f);F(28,3);F(36,1);F(44,.8f);F(48,.056f);F(60,0);F(64,63000);F(68,65000);F(72,.38f);F(76,6);F(80,80);
        Invoke("sevk_guest_frame_environment",packet);Invoke("sevk_get_guest_frame",frame);
        if(BitConverter.ToSingle(frame,72)!=2 || BitConverter.ToSingle(frame,36)!=3 || BitConverter.ToSingle(frame,32)!=0 || BitConverter.ToSingle(frame,44)!=63000)
            throw new Exception("Native environment snapshot lost raw color/diffuse/zero safety/radius");
        const ulong target=83831;ImageCreate(target,8,8,1,1);
        Check(Fn<EndFrameFn>("sevk_end_frame")(context));
        Queue(1,5,8,8,pipeline,target,Array.Empty<byte>(),new ulong[14],0);Replay(1);
        var pixels=ReadRaw(target,8*8*8);var expected=new[]{2f,1f,0f,.63f};
        for(int c=0;c<4;c++)if(Math.Abs((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(pixels,c*2))-expected[c])>.001f)
            throw new Exception("Fullscreen extras GPU environment differs at channel "+c);
        ResetQueue();Console.WriteLine("GPU PASS: normalized toward-sun, raw color/diffuse snapshot and real b6 shader sees zero safety and planet radius");
    }
}
