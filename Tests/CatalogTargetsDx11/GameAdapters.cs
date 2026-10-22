using System;
using Resource=SharpDX.Direct3D11.Resource;
using Device=SharpDX.Direct3D11.Device;
using System.Collections.Generic;
using System.Collections.Concurrent;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using VRageMath;
using VRage.Render11.Resources;

namespace VRage.Render11.Resources
{
    public interface ISrvBindable { string Name{get;} Resource Resource{get;} ShaderResourceView Srv{get;} Vector2I Size{get;} Vector3I Size3{get;} }
    public interface IRtvBindable { RenderTargetView Rtv{get;} }
    public interface IRtvTexture:ISrvBindable,IRtvBindable { }
    public interface ICustomTexture { ISrvBindable Linear{get;} ISrvBindable SRgb{get;} }
    public sealed class RtvTexture:IRtvTexture
    {
        public string Name{get;private set;}
        public Resource Resource{get;private set;}
        public ShaderResourceView Srv{get;private set;}
        public RenderTargetView Rtv{get;private set;}
        public Vector2I Size{get;private set;}
        public Vector3I Size3=>new Vector3I(Size.X,Size.Y,1);
        internal Format Format;
        internal void Init(string name,int width,int height,Format format){Name=name;Size=new Vector2I(width,height);Format=format;Restore();}
        internal void End(){Rtv?.Dispose();Srv?.Dispose();Resource?.Dispose();Rtv=null;Srv=null;Resource=null;}
        internal void Restore(){Resource=new Texture2D(VRageRender.MyRender11.DeviceInstance,new Texture2DDescription{Width=Size.X,Height=Size.Y,MipLevels=1,ArraySize=1,Format=Format,SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.RenderTarget|BindFlags.ShaderResource});Srv=new ShaderResourceView(VRageRender.MyRender11.DeviceInstance,Resource);Rtv=new RenderTargetView(VRageRender.MyRender11.DeviceInstance,Resource);}
    }
    public sealed class RwManager
    {
        public int Created,Disposed;public bool FailNext;
        public readonly HashSet<RtvTexture> Active=new HashSet<RtvTexture>();
        readonly Queue<RtvTexture> unused=new Queue<RtvTexture>();
        public IRtvTexture CreateRtv(string name,int width,int height,Format format){CheckThread();if(FailNext){FailNext=false;throw new InvalidOperationException("fixture allocation refusal");}var t=unused.Count==0?new RtvTexture():unused.Dequeue();t.Init(name,width,height,format);Active.Add(t);Created++;return t;}
        public void DisposeTex(ref IRtvTexture texture){CheckThread();if(texture==null)return;var t=(RtvTexture)texture;if(!Active.Remove(t))throw new Exception("double pool release");t.End();unused.Enqueue(t);texture=null;Disposed++;}
        public void Recreate(){CheckThread();foreach(var t in Active){t.End();t.Restore();}}
        static void CheckThread(){if(VRageRender.MyRender11.RenderThread!=System.Threading.Thread.CurrentThread)throw new Exception("pool used off renderer thread");}
    }
}
namespace VRage.Render11.Common { }
namespace VRageRender
{
    public static class MyManagers { public static readonly RwManager RwTextures=new RwManager(); }
    public sealed class RenderContext { public bool IsInitialized=true;public void ClearRtv(IRtvTexture target,RawColor4 color)=>MyRender11.DeviceInstance.ImmediateContext.ClearRenderTargetView(target.Rtv,color); }
    public static class MyRender11
    {
        public static Device DeviceInstance;public static RenderContext RC;public static System.Threading.Thread RenderThread;public static Vector2I ResolutionI=new Vector2I(95,73);
        static readonly ConcurrentQueue<Action> updates=new ConcurrentQueue<Action>();
        public static void EnqueueUpdate(Action action)=>updates.Enqueue(action);
        public static void Drain(){while(updates.TryDequeue(out var action))action();ClientPlugin.Buffers.CatalogImages.Flush();}
    }
    public sealed class MyGBuffer { public static MyGBuffer Main;public IRtvTexture LBuffer; }
}
namespace ClientPlugin.Velocity
{
    public interface IVelocityBuffer { bool IsAvailable{get;} object Srv{get;} IntPtr NativeResource{get;} int Width{get;} int Height{get;} }
    public static class VelocityRegistry { public static IVelocityBuffer Active; }
}
namespace ClientPlugin.Buffers { static class BufferCatalogLifetime { internal static void Register(string id,Action resize,Action end){}internal static void Unregister(string id){} } }
namespace ClientPlugin.Shaders
{
    public sealed class GBufferAttachment { public object Srv;public IntPtr NativeResource; }
    public static class GBufferAttachments { public static bool TryGet(string name,out GBufferAttachment attachment){attachment=null;return false;} }
}
namespace ClientPlugin.ShaderFramework
{
    public static class OwnedBuffersPass { public static void RequestLitMips(int n){} }
    public static class PointShadowPass { public static string Status=>"fixture";public static void RequestPointShadows(int n,int size){} public static void RequestWorldBoxes(bool b){} public static void RequestOccupancy(bool b){} }
}
