using System;
using System.IO;
using System.Linq;
using ClientPlugin.Backend;

namespace ClientPlugin.Shaders;

/// <summary>Shared CPU registration and immutable frame transactions; device work is routed separately.</summary>
/// <remarks>
/// Gate is a leaf lock: guest-pass updates run after it is released. Guest submission holds GuestPassList.Gate
/// while pack pass callbacks run, and packs call this facade from those callbacks (Clouds publishes its medium
/// each frame). Taking GuestPassList.Gate inside Gate deadlocked a game-thread update against the render thread.
/// Enable updates read the registry under GuestPassList.Gate (never the reverse), so concurrent callers converge
/// on its latest state.
/// </remarks>
public static class VolumetricMediumRegistry
{
    public const int ContractVersion=VolumeRegistryState.ContractVersion;
    public const int MaximumProviders=VolumeRegistryState.MaximumProviders;
    public const int UniformFloatCount=VolumeRegistryState.UniformFloatCount;
    public const int HighColumnPixels=VolumeRegistryState.HighColumnPixels;
    public const int HighDepthSlices=VolumeRegistryState.HighDepthSlices;
    public const float MaximumDistance=VolumeRegistryState.MaximumDistance;
    const string Dx="ClientPlugin.Shaders.VolumetricMediumRegistry";
    static readonly object Gate=new();
    static bool forwarded;
    static bool DxReady=>RendererRoute.UseDx11Device && Dx11Call.Ready && forwarded;

    public static string StatusLine { get { lock(Gate) return DxReady?Dx11Call.Get(Dx,"StatusLine") as string ?? "":VolumeRegistryState.StatusLine; } }
    public static long Revision { get { lock(Gate) return DxReady?Convert.ToInt64(Dx11Call.Get(Dx,"Revision") ?? 0L):VolumeRegistryState.Revision; } }
    public static void RequestRenderer(string id,bool enabled)=>Run("RequestRenderer",()=>VolumeRegistryState.RequestRenderer(id,enabled),id,enabled);
    public static void Configure(int quality,float distance,int debugView)=>Run("Configure",()=>VolumeRegistryState.Configure(quality,distance,debugView),quality,distance,debugView);
    public static void InvalidateHistory(string id)=>Run("InvalidateHistory",()=>VolumeRegistryState.InvalidateHistory(id),id);
    public static void SetIntervalCallback(string id,Action<float> callback)=>Run("SetIntervalCallback",()=>VolumeRegistryState.SetIntervalCallback(id,callback),id,callback);
    public static void SetOrigin(string id,double x,double y,double z)
    {
        if(!Local("SetOrigin",()=>VolumeRegistryState.SetOrigin(id,x,y,z),id,x,y,z) || !VolumeRegistryState.IsRegistered(id)) return;
        GuestPassList.SetOrigin(id,(float)x,(float)y,(float)z);
    }
    public static bool Register(string id,string shaderFile,string[] catalogBindings)
    {
        lock(Gate)
        {
            if(DxReady) return Dx11Call.Try(Dx,"Register",new object[]{id,shaderFile,catalogBindings},out var result) && result is true;
            if(!VolumeRegistryState.Register(id,shaderFile,catalogBindings)) return false;
        }
        // Retain the existing unsuppressed guest fallback until the shared
        // producer has a complete current-frame transaction.
        var existing=GuestPassList.Snapshot().FirstOrDefault(p=>string.Equals(p.Id,id,StringComparison.OrdinalIgnoreCase));
        if(existing==null || string.IsNullOrEmpty(existing.SourceFile??existing.File) || !string.Equals(Path.GetFullPath(existing.SourceFile??existing.File),Path.GetFullPath(shaderFile),StringComparison.OrdinalIgnoreCase))
        {
            GuestPassList.AddProgram(id,"AfterAtmosphere",shaderFile,"PublishOnly");
            GuestPassList.SetFlags(id,GuestBridgeClient.Compute|GuestBridgeClient.Volume,1);
            GuestPassList.SetMedium(id,catalogBindings,0,0,0,0,0,0);
            GuestPassList.SetEnabled(id,false);
        }
        return true;
    }
    public static void Unregister(string id)
    {
        bool registered=false;
        if(!Local("Unregister",()=>{registered=VolumeRegistryState.IsRegistered(id);VolumeRegistryState.Unregister(id);},id) || !registered) return;
        GuestPassList.SetEnabled(id,false); GuestPassList.SetEnabled(id+".present",false);
    }
    public static void SetEnabled(string id,bool enabled)
    {
        if(!Local("SetEnabled",()=>VolumeRegistryState.SetEnabled(id,enabled),id,enabled) || !VolumeRegistryState.IsRegistered(id)) return;
        // Read the registry under the pass list's gate so the last pass update applies the latest registry state.
        lock(GuestPassList.Gate)
        { bool current=VolumeRegistryState.IsEnabled(id); GuestPassList.SetEnabled(id,current); GuestPassList.SetEnabled(id+".present",current); }
    }
    public static void SetParameters(string id,float[] uniforms,double[] bounds,float[] motion)
    {
        if(!Local("SetParameters",()=>VolumeRegistryState.SetParameters(id,uniforms,bounds,motion),id,uniforms,bounds,motion) || !VolumeRegistryState.IsRegistered(id)) return;
        GuestPassList.SetUniforms(id,uniforms);
        GuestPassList.SetMotion(id,motion[0],motion[1],motion[2]);
    }
    public static bool OwnsNearInterval(string id,uint frame)
    {
        lock(Gate) return DxReady
            ? Dx11Call.Try(Dx,"OwnsNearInterval",new object[]{id,frame},out var result) && result is true
            : VolumeRegistryState.OwnsNearInterval(id,frame);
    }

    static void Run(string method,Action local,params object[] args)
    { lock(Gate) { if(DxReady) Dx11Call.Call(Dx,method,args); else local(); } }
    // Routes like Run; true when the shared state was updated locally, so the caller applies its guest passes
    // after Gate is released.
    static bool Local(string method,Action state,params object[] args)
    { lock(Gate) { if(DxReady) { Dx11Call.Call(Dx,method,args); return false; } state(); return true; } }

    // Called only after the companion and its assets have initialized. Holding
    // the routing gate keeps concurrent pack updates in the pending snapshot
    // until registration and all last-written values have reached DX11.
    internal static void ApplyDeferred()
    {
        lock(Gate)
        {
            if(forwarded || !RendererRoute.UseDx11Device || !Dx11Call.Ready) return;
            foreach(var m in VolumeRegistryState.CaptureRegistrations())
            {
                if(!Dx11Call.Try(Dx,"Register",new object[]{m.Id,m.ShaderFile,m.CatalogBindings},out var registered) || registered is not true)
                    throw new InvalidOperationException("Deferred volume registration failed: "+m.Id);
                Dx11Call.Call(Dx,"SetOrigin",m.Id,m.Origin[0],m.Origin[1],m.Origin[2]);
                if(m.ParametersSet) Dx11Call.Call(Dx,"SetParameters",m.Id,m.Uniforms,m.Bounds,m.Motion);
                Dx11Call.Call(Dx,"SetIntervalCallback",m.Id,m.Interval);
                Dx11Call.Call(Dx,"SetEnabled",m.Id,m.Enabled);
            }
            Dx11Call.Call(Dx,"Configure",VolumeRegistryState.Quality,VolumeRegistryState.Distance,VolumeRegistryState.DebugView);
            foreach(string id in VolumeRegistryState.CaptureRequests()) Dx11Call.Call(Dx,"RequestRenderer",id,true);
            forwarded=true;
        }
    }
}
