using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ClientPlugin.Buffers;
using VRageMath;

namespace ClientPlugin.Backend;

static class GuestTemporal
{
    static ulong reactiveScratch,velocityScratch;
    static int width,height;
    internal static void ResetDevice(){reactiveScratch=velocityScratch=0;width=height=0;}
    static ulong Pipeline(IReadOnlyList<GuestProgram> programs,string name)
    {
        foreach(var p in programs)if(p.Helper && p.Id=="anomaly.helper."+name)return p.Pipeline;
        return 0;
    }
    static void Ensure(int w,int h)
    {
        if(width==w && height==h && reactiveScratch!=0 && velocityScratch!=0)return;
        if(reactiveScratch==0)reactiveScratch=GuestBridgeClient.AllocateImage(w,h,1,null,0);
        else GuestBridgeClient.CreateImage(reactiveScratch,w,h,1,0);
        if(velocityScratch==0)velocityScratch=GuestBridgeClient.AllocateImage(w,h,1,null,3);
        else GuestBridgeClient.CreateImage(velocityScratch,w,h,1,3);
        width=w;height=h;
    }
    static void Draw(int mark,ulong pipeline,ulong target,ulong[] inputs,byte[] constants=null)
        =>GuestBridgeClient.SubmitPacket(mark,5,width,height,pipeline,target,constants??Array.Empty<byte>(),inputs,constants==null?0:32);
    static void Copy(IReadOnlyList<GuestProgram> programs,int mark,ulong source,ulong target)
    {
        var inputs=new ulong[14];inputs[0]=source;Draw(mark,Pipeline(programs,"Merge0"),target,inputs);
    }
    internal static void FromIsolated(IReadOnlyList<GuestProgram> programs,GuestProgram pass,ulong isolated)
    {
        if((pass.Policy&6)==0 || string.Equals(pass.Compose,"Replace",StringComparison.OrdinalIgnoreCase))return;
        int w=Math.Max(1,SceneView.Width),h=Math.Max(1,SceneView.Height);Ensure(w,h);
        if((pass.Policy&4)!=0)
        {
            ulong current=GuestBridgeClient.Catalog("reactiveMask");
            ulong pipeline=Pipeline(programs,string.Equals(pass.Compose,"IsolatedSub",StringComparison.OrdinalIgnoreCase)?"ReactiveStampAlpha":"ReactiveStamp");
            if(current==0 || pipeline==0)throw new InvalidOperationException("Reactive participation requires the current Anomaly mask and stamp helper.");
            var inputs=new ulong[14];inputs[0]=isolated;inputs[1]=current;Draw(pass.Mark,pipeline,reactiveScratch,inputs);Copy(programs,pass.Mark,reactiveScratch,current);
        }
        if((pass.Policy&2)!=0)
        {
            ulong current=GuestBridgeClient.Catalog("velocity"),pipeline=Pipeline(programs,"IsolatedVelocity");
            if(current==0 || pipeline==0)throw new InvalidOperationException("Velocity participation requires current Anomaly velocity and reconstruction helper.");
            var inputs=new ulong[14];inputs[0]=isolated;inputs[1]=current;
            Draw(pass.Mark,pipeline,velocityScratch,inputs,VelocityConstants(pass.VelocityDistanceScale));Copy(programs,pass.Mark,velocityScratch,current);
        }
    }
    internal static void Contribute(IReadOnlyList<GuestProgram> programs,int mark,ulong overlay,ulong mask)
    {
        ulong current=GuestBridgeClient.Catalog("velocity"),pipeline=Pipeline(programs,"VelocityContribute");
        if(current==0 || overlay==0 || mask==0 || pipeline==0)throw new InvalidOperationException("Velocity contribution inputs are unavailable.");
        Ensure(Math.Max(1,SceneView.Width),Math.Max(1,SceneView.Height));
        var inputs=new ulong[14];inputs[0]=current;inputs[1]=overlay;inputs[2]=mask;Draw(mark,pipeline,velocityScratch,inputs);Copy(programs,mark,velocityScratch,current);
    }
    internal static byte[] VelocityConstants(float scale)
    {
        var frame=new byte[344];if(!GuestBridgeClient.TryGetFrame(frame))throw new InvalidOperationException("Velocity frame unavailable.");
        var constants=new byte[256];Buffer.BlockCopy(frame,136,constants,0,64);
        var previous=ReadMatrix(frame,200);
        if(BitConverter.ToUInt32(frame,20)!=0)previous=Matrix.CreateTranslation(new Vector3(BitConverter.ToSingle(frame,120),BitConverter.ToSingle(frame,124),BitConverter.ToSingle(frame,128)))*previous;
        var pin=GCHandle.Alloc(constants,GCHandleType.Pinned);
        try{Marshal.StructureToPtr(previous,IntPtr.Add(pin.AddrOfPinnedObject(),64),false);}finally{pin.Free();}
        Buffer.BlockCopy(frame,264,constants,128,48);
        void F(int at,float value)=>Buffer.BlockCopy(BitConverter.GetBytes(value),0,constants,at,4);
        F(176,width);F(180,height);Buffer.BlockCopy(frame,60,constants,184,8);
        Buffer.BlockCopy(BitConverter.GetBytes(GuestFrame.Open?GuestFrame.History?1u:0u:BitConverter.ToUInt32(frame,20)),0,constants,192,4);
        F(196,Math.Max(1f,scale));return constants;
    }
    static Matrix ReadMatrix(byte[] frame,int offset)
    {
        var pin=GCHandle.Alloc(frame,GCHandleType.Pinned);try{return Marshal.PtrToStructure<Matrix>(IntPtr.Add(pin.AddrOfPinnedObject(),offset));}finally{pin.Free();}
    }
}
