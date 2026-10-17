using System;
using ClientPlugin.ShaderFramework;
using VRageMath;

namespace ClientPlugin.Backend;

// Anomaly owns directional policy and target IDs. The renderer collects its
// engine geometry and records the requested depth jobs before volume consumers.
internal static class GuestVolumeShadows
{
    static ulong[] images;
    static long epoch;
    internal static uint Generation {get;private set;}
    internal static VolumeShadowViews.View[] Views {get;private set;}
    internal static double[] Camera {get;private set;}
    internal static string Status {get;private set;}="Not requested";
    internal static ulong[] Images=>images;
    internal static void Prepare(double[] camera,float[] sunToward)
    {
        Views=null;Camera=null;
        long currentEpoch=GuestBridgeClient.ResourceEpoch;
        if(epoch!=currentEpoch){images=null;epoch=currentEpoch;}
        if(!VolumeRegistryState.Requested || !VolumeRegistryState.HasReadyMedia) {Status="Not requested";return;}
        if(!GuestBridgeClient.SupportsPacketFlags(GuestBridgeClient.SceneDepth)) {Status="Scene-depth capability unavailable";return;}
        try {
            if(camera==null || camera.Length!=3 || sunToward==null || sunToward.Length!=3)throw new InvalidOperationException("Scene camera/sun unavailable");
            var views=VolumeShadowViews.Build(new Vector3D(camera[0],camera[1],camera[2]),new Vector3D(sunToward[0],sunToward[1],sunToward[2]));
            if(images==null) {
                images=new ulong[3];
            }
            for(int i=0;i<3;i++)if(images[i]==0)images[i]=GuestBridgeClient.AllocateImage(VolumeShadowViews.Resolution,VolumeShadowViews.Resolution,1,null,7);
            var matrices=new double[48];
            for(int i=0;i<3;i++) {
                var m=views[i].WorldToShadow;
                var values=new[]{m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};
                Array.Copy(values,0,matrices,i*16,16);
            }
            if(!GuestBridgeClient.ConfigureSceneDepthViews(images,matrices))throw new InvalidOperationException("Renderer scene-depth hook unavailable");
            var scene=GuestBridgeClient.CaptureVolumeScene();
            Generation=scene!=null && scene.Length>0 && scene[0] is uint generation?generation:0;
            Views=views;Camera=(double[])camera.Clone();Status="Directional scene-depth views requested";
        } catch(Exception error) {
            Status="Directional scene-depth unavailable: "+error.GetBaseException().Message;
        }
        VolumeRegistryState.SetStatus(Status+"; shared volume transaction not yet active");
    }
}
