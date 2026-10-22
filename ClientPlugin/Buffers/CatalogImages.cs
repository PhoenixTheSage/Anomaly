using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using VRage.Render11.Common;
using VRage.Render11.Resources;
using VRageRender;
using VRageMath;

namespace ClientPlugin.Buffers;

/// <summary>
/// DX11 image allocation for the catalog. Packs call the same method names
/// on the core assembly; the core forwards here when the renderer is DX11.
/// </summary>
public static class CatalogImages
{
    sealed class ColorTarget
    {
        internal string Owner;
        internal IRtvTexture Texture;
        internal int Width,Height,Format,AppliedWidth,AppliedHeight,AppliedFormat;
        internal float[] Clear;
        internal SharpDX.Direct3D11.Resource LastResource;
        internal bool Active=true;
        internal TargetBuffer Published;
    }
    sealed class TargetBuffer : ISharedBuffer
    {
        readonly ColorTarget target;
        internal TargetBuffer(ColorTarget target){this.target=target;}
        public bool IsAvailable { get { lock(BufferCatalog.SyncRoot)return target.Active&&target.Texture?.Resource!=null&&target.Texture.Srv!=null; } }
        public object Srv { get { lock(BufferCatalog.SyncRoot)return target.Active?target.Texture:null; } }
        public IntPtr NativeResource { get { lock(BufferCatalog.SyncRoot)return target.Active&&target.Texture?.Resource!=null?target.Texture.Resource.NativePointer:IntPtr.Zero; } }
        public int Width { get { lock(BufferCatalog.SyncRoot)return target.AppliedWidth; } }
        public int Height { get { lock(BufferCatalog.SyncRoot)return target.AppliedHeight; } }
        public int Depth => 1;
        public int Format { get { lock(BufferCatalog.SyncRoot)return target.AppliedFormat; } }
    }
    static readonly Dictionary<string,ColorTarget> Targets = new(StringComparer.OrdinalIgnoreCase);
    static readonly List<IRtvTexture> Retired = new();
    static bool queued;
    static bool OnRenderer => MyRender11.RenderThread==System.Threading.Thread.CurrentThread;
    static bool Ready => OnRenderer&&MyRender11.DeviceInstance!=null&&MyRender11.RC!=null&&MyRender11.RC.IsInitialized;
    static void QueueFlush()
    {
        if(queued)return;
        queued=true;MyRender11.EnqueueUpdate(()=>{lock(BufferCatalog.SyncRoot)queued=false;Flush();});
    }
    static void ApplyTarget(string name,ColorTarget target)
    {
        if(target.Texture==null||target.AppliedWidth!=target.Width||target.AppliedHeight!=target.Height||target.AppliedFormat!=target.Format)
        {
            var replacement=MyManagers.RwTextures.CreateRtv("Anomaly.Catalog."+name,target.Width,target.Height,(Format)target.Format);
            var previous=target.Texture;target.Texture=replacement;
            target.AppliedWidth=target.Width;target.AppliedHeight=target.Height;target.AppliedFormat=target.Format;
            if(previous!=null)MyManagers.RwTextures.DisposeTex(ref previous);
        }
        var resource=target.Texture.Resource;
        if(resource==null||target.Texture.Rtv==null)return;
        if(!ReferenceEquals(resource,target.LastResource)){target.LastResource=resource;if(target.Clear==null)target.Clear=new float[4];}
        if(target.Clear!=null)
        {
            var c=target.Clear;MyRender11.RC.ClearRtv(target.Texture,new RawColor4(c[0],c[1],c[2],c[3]));target.Clear=null;
        }
    }
    internal static void Flush()
    {
        if(!Ready)return;
        lock(BufferCatalog.SyncRoot)
        {
            foreach(var retired in Retired){var texture=retired;MyManagers.RwTextures.DisposeTex(ref texture);}
            Retired.Clear();
            foreach(var pair in Targets)ApplyTarget(pair.Key,pair.Value);
        }
    }
    internal static bool IsTarget(string name,ISharedBuffer buffer) => Targets.TryGetValue(name,out var target)&&ReferenceEquals(target.Published,buffer);
    internal static void Release(string owner,string name,ISharedBuffer except=null)
    {
        lock(BufferCatalog.SyncRoot)
        {
            if(!Targets.TryGetValue(name,out var target)||!string.Equals(owner,target.Owner,StringComparison.OrdinalIgnoreCase)||ReferenceEquals(except,target.Published))return;
            Targets.Remove(name);target.Active=false;
            if(target.Texture!=null){Retired.Add(target.Texture);target.Texture=null;target.LastResource=null;}
            QueueFlush();
        }
    }

    public static void PublishRgba8(PublishedBuffer target, byte[] rgba, int width, int height)
    {
        if (target == null || rgba == null || width <= 0 || height <= 0)
            return;
        var texture = CreateTexture2D(width, height, Format.R8G8B8A8_UNorm, rgba, width * 4);
        PublishUpload(target, texture, width, height, 1, 0);
    }

    public static void PublishVolumeRgba8(PublishedBuffer target, byte[] rgba, int width, int height, int depth)
    {
        if (target == null || rgba == null || width <= 0 || height <= 0 || depth <= 0)
            return;
        var texture = CreateTexture3D(width, height, depth, new[] { rgba });
        PublishUpload(target, texture, width, height, depth, 0);
    }

    public static void PublishVolumeMips(PublishedBuffer target, byte[][] mips, int dim)
    {
        if (target == null || mips == null || mips.Length == 0 || dim <= 0)
            return;
        var texture = CreateTexture3D(dim, dim, dim, mips);
        PublishUpload(target, texture, dim, dim, dim, 0);
    }

    public static void PublishStructuredFloats(PublishedBuffer target, float[] values, int stride)
    {
        if (target == null || values == null || values.Length == 0 || stride <= 0 || stride%4!=0 || (long)values.Length*4%stride!=0)
            return;
        int bytes = values.Length * sizeof(float);
        var desc = new BufferDescription
        {
            SizeInBytes = bytes,
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource,
            OptionFlags = ResourceOptionFlags.BufferStructured,
            StructureByteStride = stride,
        };
        var buffer = new SharpDX.Direct3D11.Buffer(MyRender11.DeviceInstance, desc);
        try
        {
            var handle = GCHandle.Alloc(values, GCHandleType.Pinned);
            try
            {
                var box = new DataBox(handle.AddrOfPinnedObject(), bytes, 0);
                MyRender11.DeviceInstance.ImmediateContext.UpdateSubresource(box, buffer, 0);
            }
            finally { handle.Free(); }
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
        PublishUpload(target, buffer, bytes/stride, 1, 1, stride);
    }

    static void PublishUpload(PublishedBuffer target, SharpDX.Direct3D11.Resource resource,
        int width, int height, int depth, int stride)
    {
        UploadBindable upload = null;
        try
        {
            upload = new UploadBindable(resource, new ShaderResourceView(MyRender11.DeviceInstance, resource), width, height, depth);
            target.PublishUpload(upload, resource.NativePointer, width, height, depth, stride);
        }
        catch
        {
            if (upload != null) upload.Dispose();
            else resource.Dispose();
            throw;
        }
    }

    // Keen's shader stages consume ISrvBindable, not a bare SharpDX SRV.
    // Keep the resource alive and release only uploads allocated by this host.
    sealed class UploadBindable : ISrvBindable, IDisposable
    {
        public string Name => "Anomaly.CatalogUpload";
        public SharpDX.Direct3D11.Resource Resource { get; }
        public ShaderResourceView Srv { get; }
        public Vector2I Size { get; }
        public Vector3I Size3 { get; }
        public UploadBindable(SharpDX.Direct3D11.Resource resource, ShaderResourceView srv, int width, int height, int depth)
        { Resource = resource; Srv = srv; Size = new Vector2I(width, height); Size3 = new Vector3I(width, height, depth); }
        public void Dispose() { Srv.Dispose(); Resource.Dispose(); }
    }

    public static bool EnsureColorTarget(string packId,string name,int width,int height,int format)
    {
        packId=packId??"anomaly";
        if(string.IsNullOrWhiteSpace(packId)||string.IsNullOrWhiteSpace(name)||width<1||height<1||
            !ClientPlugin.Contracts.CatalogContract.ColorFormat(format,out int dxgi,out _))return false;
        packId=packId.Trim();name=name.Trim();
        lock(BufferCatalog.SyncRoot)
        {
            if(!BufferCatalog.CanOwn(packId,name))return false;
            bool created=!Targets.TryGetValue(name,out var target);
            if(created){target=new ColorTarget{Owner=packId};target.Published=new TargetBuffer(target);}
            if(!string.Equals(target.Owner,packId,StringComparison.OrdinalIgnoreCase))return false;
            int oldWidth=target.Width,oldHeight=target.Height,oldFormat=target.Format;
            target.Width=width;target.Height=height;target.Format=dxgi;
            // Allocate before publishing so a failed conversion retains the old
            // publication and an unsuccessful resize cannot activate later.
            try{if(Ready)ApplyTarget(name,target);}
            catch{target.Width=oldWidth;target.Height=oldHeight;target.Format=oldFormat;throw;}
            if(created)Targets.Add(name,target);
            if(!BufferCatalog.Publish(packId,name,target.Published))
            {
                if(created){Targets.Remove(name);target.Active=false;if(target.Texture!=null){Retired.Add(target.Texture);target.Texture=null;}}
                QueueFlush();return false;
            }
            if(!Ready)QueueFlush();return true;
        }
    }

    public static void ClearColorTarget(string name,float r,float g,float b,float a)
    {
        if(string.IsNullOrWhiteSpace(name))return;
        foreach(float value in new[]{r,g,b,a})if(float.IsNaN(value)||float.IsInfinity(value))throw new ArgumentOutOfRangeException(nameof(value));
        lock(BufferCatalog.SyncRoot)
        {
            if(!Targets.TryGetValue(name.Trim(),out var target))return;
            target.Clear=new[]{r,g,b,a};if(Ready)ApplyTarget(name.Trim(),target);else QueueFlush();
        }
    }

    static Texture2D CreateTexture2D(int width, int height, Format format, byte[] rgba, int rowPitch)
    {
        var desc = new Texture2DDescription
        {
            Width = width,
            Height = height,
            MipLevels = 1,
            ArraySize = 1,
            Format = format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.ShaderResource,
        };
        var handle = GCHandle.Alloc(rgba, GCHandleType.Pinned);
        try
        {
            var data = new DataRectangle(handle.AddrOfPinnedObject(), rowPitch);
            return new Texture2D(MyRender11.DeviceInstance, desc, new[] { data });
        }
        finally
        {
            handle.Free();
        }
    }

    static Texture3D CreateTexture3D(int width, int height, int depth, byte[][] mips)
    {
        var desc = new Texture3DDescription
        {
            Width = width,
            Height = height,
            Depth = depth,
            MipLevels = mips.Length,
            Format = Format.R8G8B8A8_UNorm,
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.ShaderResource,
        };
        var handles = new GCHandle[mips.Length];
        var boxes = new DataBox[mips.Length];
        int sizeW = width;
        int sizeH = height;
        int sizeD = depth;
        try
        {
            for (int i = 0; i < mips.Length; i++)
            {
                handles[i] = GCHandle.Alloc(mips[i], GCHandleType.Pinned);
                boxes[i] = new DataBox(handles[i].AddrOfPinnedObject(), sizeW * 4, sizeW * sizeH * 4);
                sizeW = Math.Max(sizeW / 2, 1);
                sizeH = Math.Max(sizeH / 2, 1);
                sizeD = Math.Max(sizeD / 2, 1);
            }

            return new Texture3D(MyRender11.DeviceInstance, desc, boxes);
        }
        finally
        {
            for (int i = 0; i < handles.Length; i++)
                if (handles[i].IsAllocated)
                    handles[i].Free();
        }
    }
}
