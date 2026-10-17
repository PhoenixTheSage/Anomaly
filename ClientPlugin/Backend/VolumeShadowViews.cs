using System;
using VRageMath;

namespace ClientPlugin.ShaderFramework;

/// <summary>Anomaly's directional volume-view policy, shared by both device owners.</summary>
internal static class VolumeShadowViews
{
    internal const int Resolution=2048;
    internal static readonly double[] ReceiverRadii={128,1000,8000};
    internal readonly struct View
    {
        internal readonly MatrixD WorldToShadow,CameraToShadow;
        internal readonly Matrix Projection;
        internal readonly Vector3D Eye,Center;
        internal View(MatrixD world,MatrixD local,Matrix projection,Vector3D eye,Vector3D center)
        { WorldToShadow=world;CameraToShadow=local;Projection=projection;Eye=eye;Center=center; }
    }
    internal static View[] Build(Vector3D camera,Vector3D sunToward,int resolution=Resolution)
    {
        if(!camera.IsValid() || !sunToward.IsValid() || sunToward.LengthSquared()<.5 || resolution<32 || resolution>16384)
            throw new ArgumentException("Volume shadow camera, sun or resolution unavailable");
        sunToward.Normalize();
        var up=Math.Abs(Vector3D.Dot(sunToward,Vector3D.Up))>.95?Vector3D.Right:Vector3D.Up;
        var right=Vector3D.Normalize(Vector3D.Cross(up,sunToward));
        up=Vector3D.Normalize(Vector3D.Cross(sunToward,right));
        var views=new View[ReceiverRadii.Length];
        for(int i=0;i<views.Length;i++)
        {
            double radius=ReceiverRadii[i],texel=2*radius/resolution;
            var center=camera;
            center+=right*(Math.Round(Vector3D.Dot(center,right)/texel)*texel-Vector3D.Dot(center,right));
            center+=up*(Math.Round(Vector3D.Dot(center,up)/texel)*texel-Vector3D.Dot(center,up));
            double reach=32000+radius;
            var eye=center+sunToward*reach;
            var view=MatrixD.CreateLookAt(eye,center,up);
            var projection=Matrix.CreateOrthographic((float)(radius*2),(float)(radius*2),1,(float)(reach+radius));
            var world=view*(MatrixD)projection;
            views[i]=new View(world,MatrixD.CreateTranslation(camera)*world,projection,eye,center);
        }
        return views;
    }
}
