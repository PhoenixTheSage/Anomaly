using System;
using System.Collections.Generic;
using System.Linq;
using ClientPlugin.Backend;

namespace ClientPlugin.Buffers;

public static class CatalogImages
{
    public static bool EnsureColorTarget(string packId, string name, int width, int height, int format)
    {
        packId=packId??"anomaly";
        if(string.IsNullOrWhiteSpace(packId)||string.IsNullOrWhiteSpace(name)||width<1||height<1||
            !ClientPlugin.Contracts.CatalogContract.ColorFormat(format,out int dxgi,out _))return false;
        packId=packId.Trim();name=name.Trim();
        lock(BufferCatalog.SyncRoot)
        {
            if(!BufferCatalog.CanOwn(packId,name))return false;
            bool dispatched=RendererRoute.UseDx11Device&&Dx11Call.Ready;
            if(dispatched)
            {
                BufferCatalog.BindDx11Catalog();
                if(!Dx11Call.Try(Dx,"EnsureColorTarget",new object[]{packId,name,width,height,dxgi},out var result)||result is not true)return false;
            }
            bool accepted=GuestCatalogTargets.Ensure(packId,name,width,height,dxgi);
            if(accepted&&dispatched)GuestCatalogTargets.MarkDx11(name);
            return accepted;
        }
    }

    public static void ClearColorTarget(string name,float r,float g,float b,float a)
    {
        GuestCatalogTargets.Clear(name,r,g,b,a);
        if(RendererRoute.UseDx11Device&&Dx11Call.Ready)
        {
            BufferCatalog.BindDx11Catalog();
            bool described=GuestCatalogTargets.Contains(name);
            GuestCatalogTargets.ReplayDx11();
            if(described)return;
            // Preserve direct-satellite entries that have no core description.
            Dx11Call.Call(Dx,"ClearColorTarget",name,r,g,b,a);
        }
    }

    const string Dx = "ClientPlugin.Buffers.CatalogImages";
}

public sealed class GuestRendererImage
{
    readonly Func<bool> available;
    readonly long resourceEpoch;
    internal GuestRendererImage(ulong imageId,int width,int height,int depth,Func<bool> available):this(imageId,width,height,depth){this.available=available;}
    public GuestRendererImage(ulong imageId, int width, int height, int depth):this(imageId,width,height,depth,true) {}
    public GuestRendererImage(ulong imageId, int width, int height, int depth, bool requiresCurrentFrame)
    {
        RequiresCurrentFrame=requiresCurrentFrame;
        resourceEpoch=GuestBridgeClient.ResourceEpoch;
        ImageId = imageId;
        Width = width;
        Height = height;
        Depth = depth < 1 ? 1 : depth;
    }

    internal GuestRendererImage(ulong imageId,int width,int height,int depth,bool current,int format,Func<bool> available):this(imageId,width,height,depth,current){Format=format;this.available=available;}
    public int Format { get; }
    public object Srv => null;
    public IntPtr NativeResource => IntPtr.Zero;
    public bool RequiresCurrentFrame { get; }
    public ulong ImageId { get; }
    public bool IsAvailable => ImageId != 0 && resourceEpoch!=0 && resourceEpoch==GuestBridgeClient.ResourceEpoch && (available==null || available());
    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }
}

public static class BufferCatalog
{
    public const string Velocity=ClientPlugin.Contracts.CatalogContract.Velocity;
    public const string LinearDepth=ClientPlugin.Contracts.CatalogContract.LinearDepth;
    public const string ObjectId=ClientPlugin.Contracts.CatalogContract.ObjectId;
    public const string HistoryColor=ClientPlugin.Contracts.CatalogContract.HistoryColor;
    public const string HiZ=ClientPlugin.Contracts.CatalogContract.HiZ;
    public const string ReactiveMask=ClientPlugin.Contracts.CatalogContract.ReactiveMask;
    public const string FullscreenIsolated=ClientPlugin.Contracts.CatalogContract.FullscreenIsolated;
    public const string HdrColor=ClientPlugin.Contracts.CatalogContract.HdrColor;
    public const string UpscaledColor=ClientPlugin.Contracts.CatalogContract.UpscaledColor;
    public const string AvgLuminance=ClientPlugin.Contracts.CatalogContract.AvgLuminance;
    public const string Bloom=ClientPlugin.Contracts.CatalogContract.Bloom;
    public const string Dirt=ClientPlugin.Contracts.CatalogContract.Dirt;
    public const string LitMips=ClientPlugin.Contracts.CatalogContract.LitMips;
    public const string PointLights=ClientPlugin.Contracts.CatalogContract.PointLights;
    public const string TileIndices=ClientPlugin.Contracts.CatalogContract.TileIndices;
    public const string HistoryDepth=ClientPlugin.Contracts.CatalogContract.HistoryDepth;
    public const string Occupancy=ClientPlugin.Contracts.CatalogContract.Occupancy;
    public const string PointShadowAtlas=ClientPlugin.Contracts.CatalogContract.PointShadowAtlas;
    public const string PlayerDepth=ClientPlugin.Contracts.CatalogContract.PlayerDepth;
    public const string VolumeSunShadow=ClientPlugin.Contracts.CatalogContract.VolumeSunShadow;
    static readonly Dictionary<string, object> Local = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string> Owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    internal static object SyncRoot => Local;
    static object dxBinding;
    internal static bool CanOwn(string packId,string name) => !IsReservedName(name)&&
        (!Owners.TryGetValue(name,out var owner)||string.Equals(owner,packId,StringComparison.OrdinalIgnoreCase));
    internal static void BindDx11Catalog()
    {
        lock(Local)
        {
            if(!Dx11Call.Ready||ReferenceEquals(dxBinding,Dx11Call.BindingIdentity))return;
            if(!Dx11Call.Call(Dx,"BindCoreCatalog",Local,(Func<string,string,bool>)CanOwn,(Action<string,string,bool>)OnDxRetired))
                throw new NotSupportedException("Update Anomaly.Dx11.dll: shared catalog ownership and named target formats require the matching satellite.");
            dxBinding=Dx11Call.BindingIdentity;
            GuestCatalogTargets.ReplayDx11(true);
        }
    }
    static void OnDxRetired(string owner,string name,bool keepTarget)
    {
        lock(Local)
        {
            if(keepTarget){if(Owners.TryGetValue(name,out var pack)&&string.Equals(owner,pack,StringComparison.OrdinalIgnoreCase))Set(name,null);}
            else {GuestCatalogTargets.Release(owner,name);Forget(owner,name);}
        }
    }
    static readonly Dictionary<string, Action[]> Lifetimes = new Dictionary<string, Action[]>(StringComparer.OrdinalIgnoreCase);

    public static void RegisterLifetime(string packId, Action onResolutionChanged, Action onDeviceEnd)
    {
        if (string.IsNullOrWhiteSpace(packId)) return;
        lock (Local) Lifetimes[packId] = new[] { onResolutionChanged, onDeviceEnd };
        if (RendererRoute.UseDx11Device)
            Dx11Call.Call(Dx, "RegisterLifetime", packId, onResolutionChanged, onDeviceEnd);
    }

    public static void UnregisterLifetime(string packId)
    {
        if (string.IsNullOrWhiteSpace(packId)) return;
        lock (Local) Lifetimes.Remove(packId);
        if (RendererRoute.UseDx11Device) Dx11Call.Call(Dx, "UnregisterLifetime", packId);
    }

    public static void Set(string name, object buffer)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        lock (Local)
        {
            if (Local.TryGetValue(name, out var previous) && !ReferenceEquals(previous, buffer))
                (previous as PublishedBuffer)?.ReleaseUpload();
            if (buffer == null)
                Local.Remove(name);
            else
                Local[name] = buffer;
        }
    }

    public static object Active(string name)
    {
        if (RendererRoute.UseDx11Device && Dx11Call.Try(Dx, "Active", new object[] { name }, out var result) && result != null)
            return result;
        lock (Local)
            return Local.TryGetValue(name ?? "", out var value) ? value : null;
    }

    public static bool IsReservedName(string name) => ClientPlugin.Contracts.CatalogContract.IsReserved(name);

    internal static bool Reserve(string packId, string name)
    {
        // Caller holds SyncRoot; targets and ordinary publications share ownership.
        if (IsReservedName(name) || (Owners.TryGetValue(name, out var owner) &&
            !string.Equals(owner, packId, StringComparison.OrdinalIgnoreCase))) return false;
        Owners[name] = packId;
        return true;
    }

    internal static void Forget(string packId, string name)
    {
        // Caller holds SyncRoot. Keep CPU data available for an explicit republish.
        if (!Owners.TryGetValue(name, out var owner) || !string.Equals(owner, packId, StringComparison.OrdinalIgnoreCase)) return;
        Owners.Remove(name);
        Set(name, null);
    }

    public static bool Publish(string packId, string name, object buffer)
    {
        if (string.IsNullOrWhiteSpace(packId) || string.IsNullOrWhiteSpace(name) || buffer == null || IsReservedName(name)) return false;
        packId = packId.Trim(); name = name.Trim();
        lock (Local)
        {
            if (Owners.TryGetValue(name, out var owner) && !string.Equals(owner, packId, StringComparison.OrdinalIgnoreCase)) return false;
            if(RendererRoute.UseDx11Device&&Dx11Call.Ready)BindDx11Catalog();
            (buffer as PublishedBuffer)?.ApplyDeferred();
            if (RendererRoute.UseDx11Device)
            {
                object inner = buffer is PublishedBuffer published ? published.Inner ?? published : buffer;
                if (!Dx11Call.Try(Dx, "Publish", new object[] { packId, name, inner }, out var result) || result is not true) return false;
            }
            GuestCatalogTargets.Release(packId, name);
            Owners[name] = packId;
            Set(name, buffer);
            return true;
        }
    }

    public static void Unpublish(string packId, string name)
    {
        if (string.IsNullOrWhiteSpace(packId) || string.IsNullOrWhiteSpace(name)) return;
        packId = packId.Trim(); name = name.Trim();
        lock (Local)
        {
            // DX11 may own entries created directly by its compatibility facade.
            if (RendererRoute.UseDx11Device) Dx11Call.Call(Dx, "Unpublish", packId, name);
            if (!Owners.TryGetValue(name, out var owner) || !string.Equals(owner, packId, StringComparison.OrdinalIgnoreCase)) return;
            GuestCatalogTargets.Release(packId, name);
            Forget(packId, name);
        }
    }

    public static void UnpublishAll(string packId)
    {
        if (string.IsNullOrWhiteSpace(packId)) return;
        packId = packId.Trim();
        lock (Local)
        {
            if (RendererRoute.UseDx11Device) Dx11Call.Call(Dx, "UnpublishAll", packId);
            GuestCatalogTargets.Release(packId);
            foreach (string name in Owners.Where(pair => string.Equals(pair.Value, packId, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Key).ToArray())
                Forget(packId, name);
        }
    }

    public static void RequestLitMips(int mipLevels = 5)
    {
        GuestOwned.RequestLitMips(mipLevels);
        if (RendererRoute.UseDx11Device) Dx11Call.Call(Dx, "RequestLitMips", mipLevels);
    }

    public static void RequestPointShadows(int maxLights = 4, int faceResolution = 128)
    {
        GuestOccupancy.RequestPointShadows(maxLights, faceResolution);
        if (RendererRoute.UseDx11Device) Dx11Call.Call(Dx, "RequestPointShadows", maxLights, faceResolution);
    }

    public static string PointShadowStatus => RendererRoute.UseDx11Device ? Dx11Call.Get(Dx, "PointShadowStatus") as string ?? "" : GuestPointShadows.Status;
    public static void RequestWorldBoxes(bool enabled = true)
    {
        GuestPointShadows.RequestWorldBoxes(enabled);
        if (RendererRoute.UseDx11Device) Dx11Call.Call(Dx, "RequestWorldBoxes", enabled);
    }

    public static void RequestOccupancy(bool enabled = true)
    {
        GuestOccupancy.Request(enabled);
        if (RendererRoute.UseDx11Device) Dx11Call.Call(Dx, "RequestOccupancy", enabled);
    }

    internal static void ApplyDeferred()
    {
        if(RendererRoute.UseDx11Device){BindDx11Catalog();GuestCatalogTargets.ReplayDx11();}
        PublishedBuffer[] pending;
        KeyValuePair<string, Action[]>[] lifetimes;
        lock (Local)
        {
            lifetimes = new KeyValuePair<string, Action[]>[Lifetimes.Count];
            int registration = 0;
            foreach (var pair in Lifetimes) lifetimes[registration++] = pair;
            pending = new PublishedBuffer[Local.Count];
            int index = 0;
            foreach (var value in Local.Values)
            {
                if (value is PublishedBuffer buffer)
                    pending[index++] = buffer;
            }

            if (index != pending.Length)
                Array.Resize(ref pending, index);
        }

        if (RendererRoute.UseDx11Device)
        {
            Dx11Call.Call(Dx, "RequestLitMips", GuestOwned.ExplicitLitMipLevels);
            Dx11Call.Call(Dx, "RequestOccupancy", GuestOccupancy.Requested);
            Dx11Call.Call(Dx, "RequestPointShadows", GuestOccupancy.PointCap, GuestOccupancy.PointFace);
            Dx11Call.Call(Dx, "RequestWorldBoxes", GuestPointShadows.WorldBoxes);
        }
        if (RendererRoute.UseDx11Device)
            foreach (var pair in lifetimes)
                Dx11Call.Call(Dx, "RegisterLifetime", pair.Key, pair.Value[0], pair.Value[1]);

        foreach (var buffer in pending)
            buffer.ApplyDeferred();
        // Replay publications accepted while renderer selection was pending.
        if (RendererRoute.UseDx11Device)
        {
            lock (Local)
                foreach (var pair in Owners)
                    if (Local.TryGetValue(pair.Key, out var value))
                    {
                        object inner = value is PublishedBuffer buffer ? buffer.Inner ?? buffer : value;
                        Dx11Call.Call(Dx, "Publish", pair.Value, pair.Key, inner);
                    }
        }
    }

    internal static void ResetGuestDevice(bool notifyEnd)
    {
        Action[] callbacks;lock(Local)callbacks=notifyEnd?Lifetimes.Values.Select(v=>v[1]).Where(v=>v!=null).ToArray():Array.Empty<Action>();
        var errors=new List<Exception>();
        foreach(var callback in callbacks)try{callback();}catch(Exception error){errors.Add(error);}
        lock(Local) {
            foreach(string key in Local.Where(v=>v.Value is GuestRendererImage).Select(v=>v.Key).ToArray())Local.Remove(key);
            foreach(var buffer in Local.Values.OfType<PublishedBuffer>().Distinct())buffer.ResetGuestDevice();
        }
        if(errors.Count!=0)throw new AggregateException("Guest device-end callbacks failed",errors);
    }
    const string Dx = "ClientPlugin.Buffers.BufferCatalog";
}

public sealed class PublishedBuffer
{
    public object Srv
    {
        get
        {
            if (RendererRoute.RefuseDx11Device("PublishedBuffer.Srv"))
                return null;
            return Inner == null ? null : Dx11Call.CallInstance(Inner, "get_Srv") ?? Read("Srv");
        }
    }

    IntPtr nativeResource;
    public IntPtr NativeResource { get => RendererRoute.UseDx11Device?(Read("NativeResource") is IntPtr pointer?pointer:nativeResource):IntPtr.Zero; private set=>nativeResource=value; }
    public bool IsAvailable => RendererRoute.UseDx11Device ? Read("IsAvailable") is true : deviceApplied&&ImageId!=0&&guestEpoch!=0&&guestEpoch==GuestBridgeClient.ResourceEpoch;
    public int Format => RendererRoute.UseDx11Device&&Read("Format") is int format?format:0;
    public bool IsStructured => RendererRoute.UseDx11Device?Read("IsStructured") is true:uploadKind==4;
    public int Stride => RendererRoute.UseDx11Device&&Read("Stride") is int stride?stride:uploadKind==4?structuredStride:0;
    public object Uav => RendererRoute.UseDx11Device?Read("Uav"):null;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Depth { get; private set; } = 1;
    public byte[] Rgba { get; private set; }
    public ulong ImageId { get; private set; }
    internal object Inner { get; private set; }
    byte[][] mipData;
    float[] structured;
    int structuredStride;
    int uploadKind;
    bool deviceApplied;
    int guestWidth,guestHeight,guestDepth,guestFormat;
    long guestEpoch;

    public PublishedBuffer() => EnsureInner();
    public PublishedBuffer(object srv,IntPtr native,int width,int height){Publish(srv,native,width,height);}

    public void PublishBake(object srv,IntPtr native,int width,int height,int depth,int format,object uav)
    {
        if(RendererRoute.RefuseDx11Device("PublishedBuffer.PublishBake"))return;
        EnsureInner();NativeResource=native;Width=width;Height=height;Depth=depth;uploadKind=0;
        if(Inner!=null)Dx11Call.CallInstance(Inner,"PublishBake",srv,native,width,height,depth,format,uav);
    }
    public void PublishStructured(object srv,IntPtr native,int elementCount,int stride,object uav=null)
    {
        if(RendererRoute.RefuseDx11Device("PublishedBuffer.PublishStructured"))return;
        EnsureInner();NativeResource=native;Width=elementCount;Height=Depth=1;uploadKind=0;
        if(Inner!=null)Dx11Call.CallInstance(Inner,"PublishStructured",srv,native,elementCount,stride,uav);
    }

    public void Publish(object srv, IntPtr native, int width, int height)
    {
        if (RendererRoute.RefuseDx11Device("PublishedBuffer.Publish"))
            return;
        EnsureInner();
        NativeResource = native;
        Width = width;
        Height = height;
        uploadKind = 0;
        if (Inner != null)
            Dx11Call.CallInstance(Inner, "Publish", srv, native, width, height);
    }

    public void Publish3D(object srv, IntPtr native, int width, int height, int depth)
    {
        if (RendererRoute.RefuseDx11Device("PublishedBuffer.Publish3D"))
            return;
        EnsureInner();
        NativeResource = native;
        Width = width;
        Height = height;
        Depth = depth;
        uploadKind = 0;
        if (Inner != null)
            Dx11Call.CallInstance(Inner, "Publish3D", srv, native, width, height, depth);
    }

    public void PublishRgba8(byte[] rgba, int width, int height)
    {
        Rgba = rgba==null?null:(byte[])rgba.Clone();
        Width = width;
        Height = height;
        Depth = 1;
        uploadKind = 1;
        deviceApplied = false;
        ApplyDeferred();
    }

    public void PublishVolumeRgba8(byte[] rgba, int width, int height, int depth)
    {
        Rgba = rgba==null?null:(byte[])rgba.Clone();
        Width = width;
        Height = height;
        Depth = depth;
        uploadKind = 2;
        deviceApplied = false;
        ApplyDeferred();
    }

    public void PublishVolumeMips(byte[][] mips, int dim)
    {
        mipData = mips?.Select(m=>m==null?null:(byte[])m.Clone()).ToArray();
        Rgba = mipData != null && mipData.Length > 0 ? mipData[0] : null;
        Width = dim;
        Height = dim;
        Depth = dim;
        uploadKind = 3;
        deviceApplied = false;
        ApplyDeferred();
    }

    public void PublishStructuredFloats(float[] values, int stride)
    {
        if(values==null||values.Length==0||stride<=0||stride%4!=0||(long)values.Length*4%stride!=0)return;
        structured = (float[])values.Clone();
        structuredStride = stride;
        Width = (int)((long)values.Length*4/stride);
        Height = 1;
        uploadKind = 4;
        deviceApplied = false;
        ApplyDeferred();
    }

    internal void ReleaseUpload()
    {
        GuestBridgeClient.RetireImage(ImageId, guestEpoch);
        if (Inner != null && uploadKind != 0) Dx11Call.CallInstance(Inner, "Clear");
        ImageId = 0; guestEpoch = 0; deviceApplied = false;
    }

    public void Clear()
    {
        ReleaseUpload();
        if (Inner != null && uploadKind == 0) Dx11Call.CallInstance(Inner, "Clear");
        Rgba = null;
        mipData = null;
        structured = null;
        Width = 0;
        Height = 0;
        Depth = 1;
        NativeResource = IntPtr.Zero;
        uploadKind = 0;
        deviceApplied = false;
        ImageId = 0;
    }

    internal void ResetGuestDevice(){ImageId=0;deviceApplied=false;}
    internal void ApplyDeferred()
    {
        if (deviceApplied || uploadKind == 0)
            return;
        if (RendererRoute.UseDx11Device)
        {
            EnsureInner();
            if (Inner == null)
                return;
            if (uploadKind == 1)
                Dx11Call.CallInstance(Inner, "PublishRgba8", Rgba, Width, Height);
            else if (uploadKind == 2)
                Dx11Call.CallInstance(Inner, "PublishVolumeRgba8", Rgba, Width, Height, Depth);
            else if (uploadKind == 3)
                Dx11Call.CallInstance(Inner, "PublishVolumeMips", mipData, Width);
            else if (uploadKind == 4)
                Dx11Call.CallInstance(Inner, "PublishStructuredFloats", structured, structuredStride);
            deviceApplied = true;
            return;
        }

        if (!RendererRoute.UseGuestImages || GuestBridgeClient.ResourceEpoch==0)
            return;
        if(uploadKind==4) {
            if(structured==null || structured.Length==0 || structuredStride<=0 || structuredStride%4!=0 || (structured.Length*4)%structuredStride!=0)return;
            var bytes=new byte[structured.Length*4];System.Buffer.BlockCopy(structured,0,bytes,0,bytes.Length);
            UploadGuest(bytes.Length,1,1,bytes,4);
        }
        else if (uploadKind == 3)
            ImageId = GuestBridgeClient.UploadVolumeMips(ImageId,Math.Max(Width, 1), mipData);
        else
        {
            int depth = uploadKind == 1 ? 1 : Depth;
            UploadGuest(Math.Max(Width,1),Math.Max(Height,1),Math.Max(depth,1),Rgba,0);
        }
        deviceApplied = ImageId != 0;
        if(uploadKind==3 && ImageId!=0 && mipData!=null){guestWidth=Width;guestHeight=Height;guestDepth=Depth;guestFormat=(mipData.Length<<16);guestEpoch=GuestBridgeClient.ResourceEpoch;}
    }

    void UploadGuest(int width,int height,int depth,byte[] bytes,int format)
    {
        if(ImageId==0)ImageId=GuestBridgeClient.AllocateImage(width,height,depth,bytes,format);
        else {
            if(guestWidth!=width || guestHeight!=height || guestDepth!=depth || guestFormat!=format)
                GuestBridgeClient.CreateImage(ImageId,width,height,depth,format);
            if(bytes!=null)GuestBridgeClient.UpdateImage(ImageId,bytes);
        }
        guestWidth=width;guestHeight=height;guestDepth=depth;guestFormat=format;
        guestEpoch = GuestBridgeClient.ResourceEpoch;
    }

    void EnsureInner()
    {
        if (Inner != null || !RendererRoute.UseDx11Device || !Dx11Call.Ready)
            return;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.Equals(assembly.GetName().Name, "Anomaly.Dx11", StringComparison.Ordinal))
                continue;
            var type = assembly.GetType("ClientPlugin.Buffers.PublishedBuffer");
            if (type != null)
                Inner = Activator.CreateInstance(type);
            return;
        }
    }

    object Read(string property)
    {
        var info = Inner?.GetType().GetProperty(property);
        return info?.GetValue(Inner);
    }
}
