using System;
using System.Linq;
using System.Runtime.InteropServices;
using ClientPlugin.Backend;
using ClientPlugin.Rendering;
using VRageMath;

static partial class NativeProbe
{
    public static void TemporalContracts()
    {
        if(!Enabled)return;
        const ulong source=85301,reactive=85302,velocity=85303;
        ImageCreate(source,8,8,1,1);ImageCreate(reactive,8,8,1,0);ImageCreate(velocity,8,8,1,3);
        void Source(float rgb,float alpha)
        {
            var bytes=new byte[8*8*8];for(int p=0;p<64;p++)for(int c=0;c<4;c++)
                Buffer.BlockCopy(BitConverter.GetBytes(BitConverter.HalfToUInt16Bits((Half)(c==3?alpha:rgb))),0,bytes,p*8+c*2,2);
            ImageUpdate(source,bytes);
        }
        SceneView.Width=SceneView.Height=8;GuestBridge.MirrorGpu=true;
        ClientPlugin.Buffers.BufferCatalog.Set("reactiveMask",new ClientPlugin.Buffers.GuestRendererImage(reactive,8,8,1));
        ClientPlugin.Buffers.BufferCatalog.Set("velocity",new ClientPlugin.Buffers.GuestRendererImage(velocity,8,8,1));
        var programs=GuestPassList.Snapshot();var pass=new GuestProgram{Id="temporal.contract",Mark=1,Policy=4,Compose="IsolatedAdd"};
        void Stamp(string compose,float rgb,float alpha,float expected)
        {
            ResetQueue();ImageUpdate(reactive,new byte[8*8*4]);Source(rgb,alpha);pass.Compose=compose;
            GuestTemporal.FromIsolated(programs,pass,source);Replay(1);var bytes=ReadRaw(reactive,8*8*4);
            if(Enumerable.Range(0,64).Any(i=>Math.Abs(bytes[i*4]/255f-expected)>.01f))throw new Exception("Reactive stamp mismatch: "+compose);
        }
        Stamp("IsolatedAdd",.05f,1,.4f);Stamp("IsolatedSub",1,.025f,.2f);
        var frame=new byte[344];Buffer.BlockCopy(new uint[]{344,1,8,8,17,1},0,frame,0,24);
        void F(int at,float value)=>Buffer.BlockCopy(BitConverter.GetBytes(value),0,frame,at,4);
        F(60,1);F(64,1);F(264,1);F(284,1);F(304,1);F(324,1);
        void MatrixAt(int at,Matrix value){var pin=GCHandle.Alloc(frame,GCHandleType.Pinned);try{Marshal.StructureToPtr(value,IntPtr.Add(pin.AddrOfPinnedObject(),at),false);}finally{pin.Free();}}
        var projection=Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2,1,.1f,100000);
        MatrixAt(136,projection);MatrixAt(200,Matrix.CreateTranslation(.25f,0,0)*projection);
        GuestBridge.FrameOverride=frame;GuestFrame.ResetDevice();
        pass.Policy=2;pass.Compose="IsolatedAdd";pass.VelocityDistanceScale=1000;
        Source(.05f,.5f);ImageUpdate(velocity,new byte[8*8*4]);ResetQueue();GuestTemporal.FromIsolated(programs,pass,source);Replay(1);
        var mv=ReadRaw(velocity,8*8*4);float actual=(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(mv,0));
        double depth=500/Math.Sqrt(1+2*.875*.875),expectedMv=.25/depth*4;
        if(Math.Abs(actual-expectedMv)>.0001)throw new Exception("Velocity distance decoding/reconstruction mismatch: "+actual+" != "+expectedMv);
        GuestBridge.FrameOverride=null;GuestBridge.MirrorGpu=false;GuestTemporal.ResetDevice();ResetQueue();
        Console.WriteLine("GPU PASS: ordinary reactive RGB/occlusion-alpha stamps and 1000m/alpha velocity distance reconstruction");
    }
}
