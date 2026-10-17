using System;
using System.Runtime.InteropServices;
using VRageMath;

namespace ClientPlugin.Backend;

// Shared HLSL byte layout. Renderer-specific owners supply their own resources;
// provider coordinates are subtracted in double precision before upload.
internal static class VolumeFrameData
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Frame
    {
        public Matrix CameraToWorld,CurrentProjection,PreviousProjection,LightToCamera;
        public Vector4 Grid,Screen,Sun,SunEnergy,Ambient,Temporal,CameraDelta,Control;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Shadows
    {
        public Matrix A,B,C;
        public Vector4 Radii,Planet;
    }
    internal static Vector4[] ProviderRows(VolumeRegistryState.Medium[] media,Vector3D camera)
    {
        if(media==null || media.Length>VolumeRegistryState.MaximumProviders || !camera.IsValid())throw new ArgumentException("Invalid volume providers/camera");
        var values=new Vector4[80];
        for(int i=0;i<media.Length;i++) {
            var m=media[i];
            for(int j=0;j<16;j++)values[i*16+j]=new Vector4(m.Uniforms[j*4],m.Uniforms[j*4+1],m.Uniforms[j*4+2],m.Uniforms[j*4+3]);
            values[64+i]=new Vector4((float)(m.Origin[0]-camera.X),(float)(m.Origin[1]-camera.Y),(float)(m.Origin[2]-camera.Z),0);
            values[68+i]=new Vector4((float)(m.Bounds[0]-camera.X),(float)(m.Bounds[1]-camera.Y),(float)(m.Bounds[2]-camera.Z),0);
            values[72+i]=new Vector4((float)(m.Bounds[3]-camera.X),(float)(m.Bounds[4]-camera.Y),(float)(m.Bounds[5]-camera.Z),0);
            values[76+i]=new Vector4(m.Motion[0],m.Motion[1],m.Motion[2],0);
        }
        return values;
    }
    internal static byte[] Bytes<T>(T value) where T:struct
    {
        int size=Marshal.SizeOf<T>();var bytes=new byte[size];var memory=Marshal.AllocHGlobal(size);
        try {Marshal.StructureToPtr(value,memory,false);Marshal.Copy(memory,bytes,0,size);return bytes;}
        finally {Marshal.FreeHGlobal(memory);}
    }
    internal static byte[] Providers(VolumeRegistryState.Medium[] media,Vector3D camera)
    {
        var rows=ProviderRows(media,camera);var bytes=new byte[1280];
        var pin=GCHandle.Alloc(rows,GCHandleType.Pinned);
        try {Marshal.Copy(pin.AddrOfPinnedObject(),bytes,0,bytes.Length);return bytes;}
        finally {pin.Free();}
    }
    internal static Frame FromGuest(byte[] guest,MatrixD lightToCamera,int gridWidth,int gridHeight,int slices,float distance,
        int lightSize,int lightSlices,int providers,uint interiorCount,float deltaSeconds,bool history,int debugView,Vector3D cameraDelta)
    {
        if(guest==null || guest.Length<344 || gridWidth<=0 || gridHeight<=0 || slices<16 || slices>128 ||
           distance<100 || distance>VolumeRegistryState.MaximumDistance || lightSize<=0 || lightSlices<=0 || providers<1 || providers>4 ||
           !cameraDelta.IsValid() || !lightToCamera.IsValid() || float.IsNaN(distance) || float.IsInfinity(distance) || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds))throw new ArgumentException("Invalid guest volume frame");
        float F(int at)=>BitConverter.ToSingle(guest,at);
        foreach(int at in new[]{32,36,60,64,72,76,80,88,92,96,104,108,112})if(float.IsNaN(F(at)) || float.IsInfinity(F(at)))throw new ArgumentException("Nonfinite guest volume environment");
        Matrix M(int at)=>new Matrix(F(at),F(at+4),F(at+8),F(at+12),F(at+16),F(at+20),F(at+24),F(at+28),F(at+32),F(at+36),F(at+40),F(at+44),F(at+48),F(at+52),F(at+56),F(at+60));
        var camera=M(264);camera.M41=camera.M42=camera.M43=0;camera.M44=1;
        var current=M(136);var previous=M(200);var projection=camera*current;
        uint width=BitConverter.ToUInt32(guest,8),height=BitConverter.ToUInt32(guest,12);
        if(width==0 || height==0 || !camera.IsValid() || !current.IsValid() || !previous.IsValid() || F(60)<=0 || F(64)<=0)throw new ArgumentException("Guest camera/projection unavailable");
        float diffuse=F(36);
        return new Frame {
            CameraToWorld=camera,CurrentProjection=current,PreviousProjection=previous,LightToCamera=(Matrix)lightToCamera,
            Grid=new Vector4(gridWidth,gridHeight,slices,distance),Screen=new Vector4(width,height,F(60),F(64)),
            Sun=new Vector4(F(88),F(92),F(96),47999),SunEnergy=new Vector4(F(72)*diffuse,F(76)*diffuse,F(80)*diffuse,projection.M43),
            Ambient=new Vector4(F(104),F(108),F(112),projection.M33),
            Temporal=new Vector4(Math.Max(0,Math.Min(.1f,deltaSeconds)),history && BitConverter.ToUInt32(guest,20)!=0 && F(32)>.8f?1:0,BitConverter.ToUInt32(guest,16),debugView),
            CameraDelta=new Vector4((Vector3)cameraDelta,0),Control=new Vector4(providers,interiorCount,lightSize,lightSlices)
        };
    }
}
