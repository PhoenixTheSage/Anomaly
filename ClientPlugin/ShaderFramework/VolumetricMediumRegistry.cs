using System;
using ClientPlugin.Backend;

namespace ClientPlugin.Shaders;

// DX11 surface over the same device-free state machine used by the core guest
// facade. This source is compiled into the companion; no core type crosses the
// assembly/reflection boundary.
public static class VolumetricMediumRegistry
{
    public const int ContractVersion=VolumeRegistryState.ContractVersion;
    public const int MaximumProviders=VolumeRegistryState.MaximumProviders;
    public const int UniformFloatCount=VolumeRegistryState.UniformFloatCount;
    public const int HighColumnPixels=VolumeRegistryState.HighColumnPixels;
    public const int HighDepthSlices=VolumeRegistryState.HighDepthSlices;
    public const float MaximumDistance=VolumeRegistryState.MaximumDistance;
    public static string StatusLine=>VolumeRegistryState.StatusLine;
    public static long Revision=>VolumeRegistryState.Revision;
    public static void RequestRenderer(string id,bool enabled)=>VolumeRegistryState.RequestRenderer(id,enabled);
    public static void Configure(int quality,float distance,int debugView)=>VolumeRegistryState.Configure(quality,distance,debugView);
    public static void SetOrigin(string id,double x,double y,double z)=>VolumeRegistryState.SetOrigin(id,x,y,z);
    public static void InvalidateHistory(string id)=>VolumeRegistryState.InvalidateHistory(id);
    public static void SetIntervalCallback(string id,Action<float> callback)=>VolumeRegistryState.SetIntervalCallback(id,callback);
    public static bool Register(string id,string shaderFile,string[] catalogBindings)=>VolumeRegistryState.Register(id,shaderFile,catalogBindings);
    public static void Unregister(string id)=>VolumeRegistryState.Unregister(id);
    public static void SetEnabled(string id,bool enabled)=>VolumeRegistryState.SetEnabled(id,enabled);
    public static void SetParameters(string id,float[] uniforms,double[] bounds,float[] motion)=>VolumeRegistryState.SetParameters(id,uniforms,bounds,motion);
    public static bool OwnsNearInterval(string id,uint frame)=>VolumeRegistryState.OwnsNearInterval(id,frame);
    internal static bool Requested=>VolumeRegistryState.Requested;
    internal static bool HasEnabledMedia=>VolumeRegistryState.HasEnabledMedia;
    internal static bool HasReadyMedia=>VolumeRegistryState.HasReadyMedia;
    internal static int Quality=>VolumeRegistryState.Quality;
    internal static int DebugView=>VolumeRegistryState.DebugView;
    internal static float Distance=>VolumeRegistryState.Distance;
    internal static long HistoryRevision=>VolumeRegistryState.HistoryRevision;
    internal static void SetStatus(string value)=>VolumeRegistryState.SetStatus(value);
    internal static void SetActiveStatus(Func<string> provider)=>VolumeRegistryState.SetActiveStatus(provider);
    internal static void ResetIntervals()=>VolumeRegistryState.ResetIntervals();
    internal static VolumeRegistryState.Medium[] Capture(out long revision)=>VolumeRegistryState.Capture(out revision);
    internal static VolumeRegistryState.Medium[] Capture(out long revision,out long history,out int quality,out float distance,out int debug)=>VolumeRegistryState.Capture(out revision,out history,out quality,out distance,out debug);
    internal static bool Commit(uint frame,long revision,bool resources,bool shadows,bool interiors,bool temporal,VolumeRegistryState.Medium[] media=null)=>VolumeRegistryState.Commit(frame,revision,resources,shadows,interiors,temporal,media);
    internal static void Release()=>VolumeRegistryState.Release();
}