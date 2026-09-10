using System;
using System.Collections.Generic;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using VRage.Render11.Common;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace ClientPlugin.ShaderFramework;

/// <summary>
/// Display-tenant dest for <c>DrawGameScene</c>. Keen's
/// <c>BorrowCustom</c> is <c>R8G8B8A8</c> UNORM unless another plugin
/// upgraded <c>MyCustomTexture</c>. BT.2390 writes scRGB above 1; UNORM
/// clips on store. When the dest cannot store HDR, wrap a borrowed
/// <c>R16G16B16A16_Float</c> UAV that still exposes
/// <c>IBorrowedCustomTexture.Linear</c> / <c>.SRgb</c> / <c>.Release</c>.
/// </summary>
static class DisplayDest
{
    public const Format HdrFormat = Format.R16G16B16A16_Float;

    public const string TonemappedName = "DrawGameScene.Tonemapped";
    public const string FxaaName = "MyRender11.FXAA.Rgb8";
    public const string ChromaticName = "DrawGameScene.ChromaticAberration";

    static readonly object Gate = new();
    static readonly HashSet<string> LoggedKeys = new();
    static string statusLine = "none";

    public static string StatusLine
    {
        get
        {
            lock (Gate)
                return statusLine;
        }
    }

    public static bool IsPostTonemapName(string debugName) =>
        debugName == TonemappedName ||
        debugName == FxaaName ||
        debugName == ChromaticName;

    public static bool CanStoreHdr(Format format) =>
        format == Format.R16G16B16A16_Float ||
        format == Format.R32G32B32A32_Float;

    /// <summary>
    /// Turn a notify dest (<c>IBorrowedCustomTexture</c> / <c>ICustomTexture</c>)
    /// into something <c>DrawGameScene</c> can copy. Persistent custom
    /// textures get a no-op <c>Release</c> so Keen's end-of-scene release
    /// does not dispose the upscaler's RT.
    /// </summary>
    public static IBorrowedCustomTexture Adopt(object color)
    {
        if (color == null)
            return null;
        if (color is IBorrowedCustomTexture borrowed)
            return EnsureHdr(TonemappedName, borrowed);
        if (color is ICustomTexture custom)
            return EnsureHdr(TonemappedName, new DisplayPersistentCustom(custom));
        return null;
    }

    public static IBorrowedCustomTexture BorrowTonemapped()
    {
        var dest = MyManagers.RwTexturesPool.BorrowCustom(TonemappedName);
        return EnsureHdr(TonemappedName, dest);
    }

    public static IBorrowedCustomTexture EnsureHdr(string debugName, IBorrowedCustomTexture dest)
    {
        if (dest == null)
            return null;
        if (dest is DisplayBorrowedCustom || dest is DisplayPersistentCustom)
            return dest;

        Format linear;
        Format srgb;
        Vector2I size;
        try
        {
            linear = dest.Linear != null ? dest.Linear.Format : Format.Unknown;
            srgb = dest.SRgb != null ? dest.SRgb.Format : Format.Unknown;
            size = dest.Size;
        }
        catch (Exception e)
        {
            Note(debugName, "inspect failed: " + e.Message);
            return dest;
        }

        if (CanStoreHdr(linear))
        {
            Note(debugName, "keep " + linear + " (srgb " + srgb + ") " + size.X + "x" + size.Y);
            return dest;
        }

        IBorrowedUavTexture hdr;
        try
        {
            if (size.X <= 0 || size.Y <= 0)
            {
                Note(debugName, "skip wrap " + linear + " (bad size " + size.X + "x" + size.Y + ")");
                return dest;
            }

            hdr = MyManagers.RwTexturesPool.BorrowUav(
                "Anomaly.DisplayDest", size.X, size.Y, HdrFormat);
        }
        catch (Exception e)
        {
            Note(debugName, "wrap failed " + linear + ": " + e.Message);
            return dest;
        }

        if (hdr == null)
        {
            Note(debugName, "wrap failed " + linear + " (null UAV)");
            return dest;
        }

        try
        {
            dest.Release();
        }
        catch (Exception e)
        {
            try
            {
                hdr.Release();
            }
            catch
            {
                // Best-effort: do not leak both if the Keen dest release threw.
            }

            Note(debugName, "release failed " + linear + ": " + e.Message);
            return dest;
        }

        Note(debugName, "wrap " + linear + "/" + srgb + " -> " + HdrFormat + " " +
                        size.X + "x" + size.Y);
        return new DisplayBorrowedCustom(hdr);
    }

    /// <summary>
    /// Persistent <c>ICustomTexture</c> (upscaler <c>CreateTexture</c>) as
    /// the borrowed dest <c>DrawGameScene</c> expects. <c>Release</c> is a
    /// no-op — Keen always releases the tonemap result.
    /// </summary>
    sealed class DisplayPersistentCustom : IBorrowedCustomTexture
    {
        readonly ICustomTexture inner;

        public DisplayPersistentCustom(ICustomTexture inner)
        {
            this.inner = inner;
        }

        public void AddRef()
        {
        }

        public void Release()
        {
        }

        public string Name => inner.Name;

        public SharpDX.Direct3D11.Resource Resource => inner.Resource;

        public Vector3I Size3 => inner.Size3;

        public Vector2I Size => inner.Size;

        public Format Format => inner.Linear != null ? inner.Linear.Format : Format.Unknown;

        public int MipLevels => inner.Linear != null ? inner.Linear.MipLevels : 0;

        public ShaderResourceView Srv => inner.Linear != null ? inner.Linear.Srv : null;

        public UnorderedAccessView Uav => inner.Uav;

        public IRtvTexture Linear => inner.Linear;

        public IRtvTexture SRgb => inner.SRgb;

        public event Action<ITexture> OnFormatChanged
        {
            add { }
            remove { }
        }
    }

    static void Note(string debugName, string detail)
    {
        var key = debugName + "|" + detail;
        lock (Gate)
        {
            statusLine = debugName + " " + detail;
            if (!LoggedKeys.Add(key))
                return;
        }

        var line = "Anomaly display dest " + debugName + ": " + detail;
        MyLog.Default.WriteLine(line);
        DebugLog.Write(line);
        RenderTrace.Note("DisplayDest");
    }

    sealed class DisplayBorrowedCustom : IBorrowedCustomTexture
    {
        readonly IBorrowedUavTexture inner;

        public DisplayBorrowedCustom(IBorrowedUavTexture inner)
        {
            this.inner = inner;
        }

        public void AddRef() => inner.AddRef();

        public void Release() => inner.Release();

        public string Name => inner.Name;

        public SharpDX.Direct3D11.Resource Resource => inner.Resource;

        public Vector3I Size3 => inner.Size3;

        public Vector2I Size => inner.Size;

        public Format Format => inner.Format;

        public int MipLevels => inner.MipLevels;

        public ShaderResourceView Srv => inner.Srv;

        public UnorderedAccessView Uav => inner.Uav;

        public IRtvTexture Linear => inner;

        public IRtvTexture SRgb => inner;

        public event Action<ITexture> OnFormatChanged
        {
            add { }
            remove { }
        }
    }
}
