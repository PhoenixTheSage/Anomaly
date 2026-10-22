using System;
using System.Collections.Generic;
using ClientPlugin.Shaders;
using ClientPlugin.Velocity;
using VRage.Render11.Resources;
using VRageRender;

namespace ClientPlugin.Buffers;

/// <summary>
/// Well-known type for consumers. Resolve by name:
/// <c>ClientPlugin.Buffers.BufferCatalog</c> — do not take a compile-time
/// reference to Anomaly. <see cref="Active"/> looks up a named buffer
/// (<c>velocity</c>, <c>linearDepth</c>, <c>hiZ</c>, <c>historyColor</c>,
/// <c>reactiveMask</c>, <c>fullscreenIsolated</c>, <c>hdrColor</c>,
/// <c>upscaledColor</c>, <c>avgLuminance</c>, <c>bloom</c>, <c>dirt</c>,
    /// <c>litMips</c>, <c>pointLights</c>, <c>tileIndices</c>,
    /// <c>historyDepth</c>, <c>occupancy</c>, <c>pointShadowAtlas</c>,
    /// <c>volumeSunShadow</c>).
/// <c>velocity</c> aliases
/// <see cref="VelocityRegistry.Active"/>. <c>hdrColor</c> aliases Keen
/// <c>LBuffer</c>. Packs publish extras with <see cref="Publish"/>;
/// reserved names fail closed.
/// </summary>
public static class BufferCatalog
{
    public const string Velocity = ClientPlugin.Contracts.CatalogContract.Velocity;
    public const string LinearDepth = ClientPlugin.Contracts.CatalogContract.LinearDepth;
    public const string ObjectId = ClientPlugin.Contracts.CatalogContract.ObjectId;
    public const string HistoryColor = ClientPlugin.Contracts.CatalogContract.HistoryColor;
    public const string HiZ = ClientPlugin.Contracts.CatalogContract.HiZ;
    public const string ReactiveMask = ClientPlugin.Contracts.CatalogContract.ReactiveMask;
    public const string FullscreenIsolated = ClientPlugin.Contracts.CatalogContract.FullscreenIsolated;
    public const string HdrColor = ClientPlugin.Contracts.CatalogContract.HdrColor;
    public const string UpscaledColor = ClientPlugin.Contracts.CatalogContract.UpscaledColor;
    public const string AvgLuminance = ClientPlugin.Contracts.CatalogContract.AvgLuminance;
    public const string Bloom = ClientPlugin.Contracts.CatalogContract.Bloom;
    public const string Dirt = ClientPlugin.Contracts.CatalogContract.Dirt;
    public const string LitMips = ClientPlugin.Contracts.CatalogContract.LitMips;
    public const string PointLights = ClientPlugin.Contracts.CatalogContract.PointLights;
    public const string TileIndices = ClientPlugin.Contracts.CatalogContract.TileIndices;
    public const string HistoryDepth = ClientPlugin.Contracts.CatalogContract.HistoryDepth;
    public const string Occupancy = ClientPlugin.Contracts.CatalogContract.Occupancy;
    public const string PointShadowAtlas = ClientPlugin.Contracts.CatalogContract.PointShadowAtlas;
    public const string PlayerDepth = ClientPlugin.Contracts.CatalogContract.PlayerDepth;
    /// <summary>
    /// Conventional pack product: remaining sun through a volume (1 = none).
    /// Not reserved — PublishOnly <c>output</c> may use this name.
    /// </summary>
    public const string VolumeSunShadow = ClientPlugin.Contracts.CatalogContract.VolumeSunShadow;

    static object Gate = new();
    internal static object SyncRoot => Gate;
    static Func<string,string,bool> coreCanOwn;
    static Action<string,string,bool> coreRetired;

    // Host bridge: bind before installing framework hooks or replaying callbacks.
    public static void BindCoreCatalog(object gate,Func<string,string,bool> canOwn,Action<string,string,bool> retired)
    {
        if(gate==null)throw new ArgumentNullException(nameof(gate));
        lock(Gate){Gate=gate;coreCanOwn=canOwn;coreRetired=retired;}
    }
    internal static bool CanOwn(string owner,string name) => !IsReservedName(name) &&
        (!Owners.TryGetValue(name,out var previous)||string.Equals(previous,owner,StringComparison.OrdinalIgnoreCase)) &&
        (coreCanOwn==null||coreCanOwn(owner,name));
    static readonly Dictionary<string, ISharedBuffer> ByName =
        new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, string> Owners =
        new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Current producer for <paramref name="name"/>, or an unavailable
    /// placeholder. Never returns null.
    /// </summary>
    public static ISharedBuffer Active(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return UnavailableSharedBuffer.Instance;

        lock (Gate)
        {
            if (ByName.TryGetValue(name, out var published) && published != null)
                return published;
        }

        if (string.Equals(name, Velocity, StringComparison.OrdinalIgnoreCase))
            return VelocitySharedBuffer.Wrap(VelocityRegistry.Active);

        if (string.Equals(name, HdrColor, StringComparison.OrdinalIgnoreCase))
            return LBufferSharedBuffer.Instance;

        if (GBufferAttachments.TryGet(name, out var attachment))
            return new AttachmentSharedBuffer(attachment);

        return UnavailableSharedBuffer.Instance;
    }

    /// <summary>
    /// Publish or replace a named buffer. Pass null to clear.
    /// Anomaly internals use this; packs should prefer <see cref="Publish"/>.
    /// </summary>
    public static void Set(string name, ISharedBuffer buffer)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        lock (Gate)
        {
            if (buffer == null)
                ByName.Remove(name);
            else
                ByName[name] = buffer;
        }
    }

    /// <summary>
    /// Pack-owned catalog entry. Reserved names
    /// (<c>velocity</c>, <c>linearDepth</c>, <c>hiZ</c>, <c>historyColor</c>,
    /// <c>reactiveMask</c>, <c>fullscreenIsolated</c>, <c>hdrColor</c>,
    /// <c>upscaledColor</c>, <c>avgLuminance</c>, <c>bloom</c>, <c>dirt</c>,
    /// <c>litMips</c>, <c>pointLights</c>, <c>tileIndices</c>,
    /// <c>historyDepth</c>, <c>occupancy</c>, <c>pointShadowAtlas</c>) fail closed.
    /// Same name from two pack ids fails closed.
    /// </summary>
    public static bool Publish(string packId, string name, ISharedBuffer buffer)
    {
        if (string.IsNullOrWhiteSpace(packId) || string.IsNullOrWhiteSpace(name) || buffer == null)
            return false;
        packId = packId.Trim();
        name = name.Trim();
        if (IsReservedName(name))
            return false;
        lock (Gate)
        {
            if (!CanOwn(packId,name)) return false;
            CatalogImages.Release(packId,name,buffer);
            Owners[name] = packId;
            bool replaced=ByName.TryGetValue(name,out var previous)&&!ReferenceEquals(previous,buffer);
            if(replaced)(previous as PublishedBuffer)?.ReleaseUpload();
            ByName[name] = buffer;
            if(replaced)coreRetired?.Invoke(packId,name,CatalogImages.IsTarget(name,buffer));
            return true;
        }
    }

    public static void Unpublish(string packId, string name)
    {
        if (string.IsNullOrWhiteSpace(packId) || string.IsNullOrWhiteSpace(name))
            return;
        packId=packId.Trim();name=name.Trim();
        lock (Gate)
        {
            if (!Owners.TryGetValue(name, out var owner) ||
                !string.Equals(owner, packId, StringComparison.OrdinalIgnoreCase))
                return;
            Owners.Remove(name);
            if (ByName.TryGetValue(name, out var previous))
                (previous as PublishedBuffer)?.ReleaseUpload();
            ByName.Remove(name);
            CatalogImages.Release(packId,name);
            coreRetired?.Invoke(packId,name,false);
        }
    }

    public static void UnpublishAll(string packId)
    {
        if (string.IsNullOrWhiteSpace(packId))
            return;
        packId=packId.Trim();
        lock (Gate)
        {
            var drop = new List<string>();
            foreach (var kv in Owners)
            {
                if (string.Equals(kv.Value, packId, StringComparison.OrdinalIgnoreCase))
                    drop.Add(kv.Key);
            }

            for (var i = 0; i < drop.Count; i++)
            {
                Owners.Remove(drop[i]);
                if (ByName.TryGetValue(drop[i], out var previous))
                    (previous as PublishedBuffer)?.ReleaseUpload();
                ByName.Remove(drop[i]);
                CatalogImages.Release(packId,drop[i]);
                coreRetired?.Invoke(packId,drop[i],false);
            }
        }
    }

    /// <summary>
    /// DRS / device-end callbacks so a pack can drop its own
    /// <c>OnDeviceReset</c> Harmony patch.
    /// </summary>
    public static void RegisterLifetime(string packId, Action onResolutionChanged, Action onDeviceEnd)
    {
        BufferCatalogLifetime.Register(packId, onResolutionChanged, onDeviceEnd);
    }

    public static void UnregisterLifetime(string packId)
    {
        BufferCatalogLifetime.Unregister(packId);
    }

    /// <summary>
    /// Request catalog <c>litMips</c> (GenerateMips of this-frame HDR
    /// <c>LBuffer</c> at AfterLighting). <paramref name="mipLevels"/> <c>&lt;= 0</c>
    /// clears the explicit request. Json / <c>RequestSrv(..., "litMips")</c>
    /// on a live fullscreen program also generates.
    /// </summary>
    public static void RequestLitMips(int mipLevels = 5)
    {
        ClientPlugin.ShaderFramework.OwnedBuffersPass.RequestLitMips(mipLevels);
    }

    /// <summary>Last point-shadow pass selection, caster and draw diagnostics.</summary>
    public static string PointShadowStatus => ShaderFramework.PointShadowPass.Status;

    /// <summary>
    /// Request catalog <c>pointShadowAtlas</c> (light-view mesh + optional
    /// AABB depth for the closest N point lights). <paramref name="maxLights"/>
    /// <c>&lt;= 0</c> skips the cube pass. Default 4, max 64. Face default
    /// 128, max 1024 (effective light count is memory/dimension bounded). VRAM grows with the request. Mesh uses MeshDepth VS.
    /// </summary>
    public static void RequestPointShadows(int maxLights = 4, int faceResolution = 128)
    {
        ClientPlugin.ShaderFramework.PointShadowPass.RequestPointShadows(maxLights, faceResolution);
    }

    /// <summary>
    /// Stamp world AABB boxes into <c>pointShadowAtlas</c> in addition to
    /// the local character mesh when that stamp is live. Off = no AABB
    /// cubes. The local player AABB is never stamped.
    /// </summary>
    public static void RequestWorldBoxes(bool enabled = true)
    {
        ClientPlugin.ShaderFramework.PointShadowPass.RequestWorldBoxes(enabled);
    }

    /// <summary>
    /// Request catalog <c>occupancy</c> (64³ camera-relative clipmap atlas).
    /// Pass false to clear the request.
    /// </summary>
    public static void RequestOccupancy(bool enabled = true)
    {
        ClientPlugin.ShaderFramework.PointShadowPass.RequestOccupancy(enabled);
    }

    public static bool IsReservedName(string name) => ClientPlugin.Contracts.CatalogContract.IsReserved(name);

    internal static void PublishVelocity(IVelocityBuffer buffer)
    {
        Set(Velocity, VelocitySharedBuffer.Wrap(buffer));
    }

    /// <summary>
    /// Publish a Keen <c>ISrvBindable</c> under a reserved catalog name
    /// (tonemap avg luminance / bloom / dirt). Pass null to clear.
    /// </summary>
    internal static void SetKeen(string name, object srv)
    {
        if (srv == null)
        {
            Set(name, null);
            return;
        }

        Set(name, KeenColorBuffer.Wrap(srv, 1, 1));
    }

    /// <summary>
    /// Unique upscale consumer dest for this frame. Pass null to clear.
    /// <paramref name="color"/> is Keen <c>ISrvBindable</c> /
    /// <c>IRtvBindable</c> / <c>ICustomTexture</c>.
    /// </summary>
    internal static void PublishUpscaledColor(object color, int fallbackW, int fallbackH)
    {
        var wrapped = KeenColorBuffer.Wrap(color, fallbackW, fallbackH);
        Set(UpscaledColor, wrapped != null && wrapped.IsAvailable ? wrapped : null);
    }

    internal static void ClearUpscaledColor()
    {
        Set(UpscaledColor, null);
    }
}

/// <summary>
/// Pack-owned catalog texture. Pass to <see cref="BufferCatalog.Publish"/>.
/// Resolve by name: <c>ClientPlugin.Buffers.PublishedBuffer</c>.
/// </summary>
public sealed class PublishedBuffer : ISharedBuffer
{
    IDisposable ownedUpload;

    internal void PublishUpload(IDisposable upload, IntPtr native, int width, int height, int depth, int stride)
    {
        if (stride > 0) PublishStructured(upload, native, width, stride);
        else ApplyRaster(upload, native, width, height, depth, 0, null);
        ownedUpload = upload;
    }

    internal void ReleaseUpload()
    {
        if (ownedUpload != null) Clear();
    }
    public bool IsAvailable => Srv != null && Width > 0 && Height > 0 && Depth > 0 &&
        (IsStructured || NativeResource != IntPtr.Zero);
    public object Srv { get; private set; }
    public IntPtr NativeResource { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Depth { get; private set; } = 1;
    public int Format { get; private set; }
    public int Stride { get; private set; }
    public bool IsStructured { get; private set; }

    /// <summary>
    /// Optional UAV for a pack compute bake into this atlas. Anomaly does
    /// not dispatch; the pack writes, IsolatedMix reads <see cref="Srv"/>.
    /// </summary>
    public object Uav { get; private set; }

    public PublishedBuffer()
    {
    }

    public PublishedBuffer(object srv, IntPtr nativeResource, int width, int height)
    {
        Publish(srv, nativeResource, width, height);
    }

    /// <summary>
    /// 2D catalog texture. This is the only method named <c>Publish</c> —
    /// pack <c>GetMethod("Publish")</c> must stay unambiguous.
    /// Texture3D uses <see cref="Publish3D"/>; UAV bake uses
    /// <see cref="PublishBake"/>; node pools use <see cref="PublishStructured"/>.
    /// </summary>
    public void Publish(object srv, IntPtr nativeResource, int width, int height)
    {
        ApplyRaster(srv, nativeResource, width, height, 1, 0, null);
    }

    /// <summary>
    /// Texture3D atlas. Unique name so 2D pack binds do not throw
    /// <c>AmbiguousMatchException</c>. Exactly five parameters so
    /// reflection <c>Invoke</c> with width/height/depth still matches.
    /// </summary>
    public void Publish3D(object srv, IntPtr nativeResource, int width, int height, int depth)
    {
        ApplyRaster(srv, nativeResource, width, height, depth, 0, null);
    }

    /// <summary>
    /// Texture3D (or 2D) plus optional UAV for a pack compute bake.
    /// </summary>
    public void PublishBake(object srv, IntPtr nativeResource, int width, int height, int depth,
        int format, object uav)
    {
        ApplyRaster(srv, nativeResource, width, height, depth, format, uav);
    }

    void ApplyRaster(object srv, IntPtr nativeResource, int width, int height, int depth, int format,
        object uav)
    {
        ReleaseUpload();
        Srv = srv;
        NativeResource = nativeResource;
        Width = width;
        Height = height;
        Depth = depth > 0 ? depth : 1;
        Format = format;
        Uav = uav;
        IsStructured = false;
        Stride = 0;
    }

    /// <summary>
    /// Node pool / list. <see cref="Width"/> is element count.
    /// </summary>
    public void PublishStructured(object srv, IntPtr nativeResource, int elementCount, int stride,
        object uav = null)
    {
        ReleaseUpload();
        Srv = srv;
        NativeResource = nativeResource;
        Width = elementCount;
        Height = 1;
        Depth = 1;
        Format = 0;
        Stride = stride;
        Uav = uav;
        IsStructured = true;
    }

    public void Clear()
    {
        var retired = ownedUpload;
        ownedUpload = null;
        retired?.Dispose();
        Srv = null;
        NativeResource = IntPtr.Zero;
        Width = 0;
        Height = 0;
        Depth = 1;
        Format = 0;
        Stride = 0;
        IsStructured = false;
        Uav = null;
    }

    public void PublishRgba8(byte[] rgba, int width, int height) =>
        CatalogImages.PublishRgba8(this, rgba, width, height);

    public void PublishVolumeRgba8(byte[] rgba, int width, int height, int depth) =>
        CatalogImages.PublishVolumeRgba8(this, rgba, width, height, depth);

    public void PublishVolumeMips(byte[][] mips, int dim) =>
        CatalogImages.PublishVolumeMips(this, mips, dim);

    public void PublishStructuredFloats(float[] values, int stride) =>
        CatalogImages.PublishStructuredFloats(this, values, stride);
}

sealed class CatalogTexture : ISharedBuffer
{
    object srv;
    IntPtr nativeResource;
    int width;
    int height;

    public bool IsAvailable => srv != null && nativeResource != IntPtr.Zero && width > 0 && height > 0;
    public object Srv => srv;
    public IntPtr NativeResource => nativeResource;
    public int Width => width;
    public int Height => height;
    public int Depth => 1;
    public int Format => 0;

    public void Clear()
    {
        srv = null;
        nativeResource = IntPtr.Zero;
        width = 0;
        height = 0;
    }

    public void Publish(object srvBindable, IntPtr resource, int w, int h)
    {
        srv = srvBindable;
        nativeResource = resource;
        width = w;
        height = h;
    }
}

sealed class UnavailableSharedBuffer : ISharedBuffer
{
    public static readonly UnavailableSharedBuffer Instance = new();

    UnavailableSharedBuffer()
    {
    }

    public bool IsAvailable => false;
    public object Srv => null;
    public IntPtr NativeResource => IntPtr.Zero;
    public int Width => 0;
    public int Height => 0;
    public int Depth => 0;
    public int Format => 0;
}

sealed class VelocitySharedBuffer : ISharedBuffer
{
    readonly IVelocityBuffer inner;

    VelocitySharedBuffer(IVelocityBuffer inner)
    {
        this.inner = inner;
    }

    public static ISharedBuffer Wrap(IVelocityBuffer buffer)
    {
        if (buffer == null)
            return UnavailableSharedBuffer.Instance;
        return new VelocitySharedBuffer(buffer);
    }

    public bool IsAvailable => inner != null && inner.IsAvailable;
    public object Srv => inner?.Srv;
    public IntPtr NativeResource => inner != null ? inner.NativeResource : IntPtr.Zero;
    public int Width => inner != null ? inner.Width : 0;
    public int Height => inner != null ? inner.Height : 0;
    public int Depth => 1;
    public int Format => 0;
}

sealed class AttachmentSharedBuffer : ISharedBuffer
{
    readonly GBufferAttachment inner;

    public AttachmentSharedBuffer(GBufferAttachment inner)
    {
        this.inner = inner;
    }

    public bool IsAvailable => inner != null && inner.Srv != null && inner.NativeResource != IntPtr.Zero;
    public object Srv => inner?.Srv;
    public IntPtr NativeResource => inner != null ? inner.NativeResource : IntPtr.Zero;
    public int Width => 0;
    public int Height => 0;
    public int Depth => 1;
    public int Format => 0;
}

sealed class LBufferSharedBuffer : ISharedBuffer
{
    public static readonly LBufferSharedBuffer Instance = new();

    LBufferSharedBuffer()
    {
    }

    IRtvTexture Target => MyGBuffer.Main?.LBuffer;

    public bool IsAvailable => Target != null;
    public object Srv => Target;
    public IntPtr NativeResource =>
        Target?.Resource != null ? Target.Resource.NativePointer : IntPtr.Zero;
    public int Width => MyRender11.ResolutionI.X;
    public int Height => MyRender11.ResolutionI.Y;
    public int Depth => 1;
    public int Format => 0;
}

sealed class KeenColorBuffer : ISharedBuffer
{
    readonly object srv;
    readonly IntPtr nativeResource;
    readonly int width;
    readonly int height;

    KeenColorBuffer(object srv, IntPtr nativeResource, int width, int height)
    {
        this.srv = srv;
        this.nativeResource = nativeResource;
        this.width = width;
        this.height = height;
    }

    public static ISharedBuffer Wrap(object color, int fallbackW, int fallbackH)
    {
        if (color == null)
            return UnavailableSharedBuffer.Instance;
        if (color is ICustomTexture custom)
            color = custom.Linear ?? (object)custom.SRgb;
        var srv = color as ISrvBindable;
        if (srv == null)
            return UnavailableSharedBuffer.Instance;

        var native = IntPtr.Zero;
        var w = fallbackW;
        var h = fallbackH;
        var rtv = color as IRtvTexture;
        if (rtv != null)
        {
            if (rtv.Resource != null)
                native = rtv.Resource.NativePointer;
            if (rtv.Size.X > 0 && rtv.Size.Y > 0)
            {
                w = rtv.Size.X;
                h = rtv.Size.Y;
            }
        }

        return new KeenColorBuffer(srv, native, w, h);
    }

    public bool IsAvailable => srv != null && width > 0 && height > 0;
    public object Srv => srv;
    public IntPtr NativeResource => nativeResource;
    public int Width => width;
    public int Height => height;
    public int Depth => 1;
    public int Format => 0;
}
