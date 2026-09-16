using System;
using System.Reflection;
using ClientPlugin.Buffers;
using HarmonyLib;
using SharpDX.Direct3D11;
using VRage.Render11.Common;
using VRage.Render11.LightingStage;
using VRage.Render11.Resources;
using VRage.Utils;
using VRageRender;

namespace ClientPlugin.ShaderFramework;

/// <summary>
/// Wraps Keen’s tiled point-light GPU buffers into catalog
/// <c>pointLights</c> / <c>tileIndices</c>. No copy.
/// </summary>
public static class PointLightCatalog
{
    const int PointLightStride = 48;
    const int TileIndexStride = 4;

    static readonly object Gate = new();
    static readonly StructuredSharedBuffer LightsPublished = new();
    static readonly StructuredSharedBuffer TilesPublished = new();

    static FieldInfo lightsField;
    static FieldInfo tilesField;
    static FieldInfo visibleField;
    static bool fieldsResolved;
    static ISrvBuffer dummyLights;
    static ISrvBuffer dummyTiles;

    internal static void PublishFromKeen()
    {
        EnsureFields();
        ISrvBuffer lights = null;
        ISrvUavBuffer tiles = null;
        var visible = false;
        try
        {
            lights = lightsField?.GetValue(null) as ISrvBuffer;
            tiles = tilesField?.GetValue(null) as ISrvUavBuffer;
            visible = visibleField != null && visibleField.GetValue(null) is true;
        }
        catch (Exception e)
        {
            MyLog.Default.Warning("Anomaly: point-light catalog field read failed: " + e.Message);
        }

        if (!MyRender11.DebugOverrides.PointLights || !visible || lights == null || tiles == null)
        {
            Clear();
            return;
        }

        LightsPublished.Publish(lights, Count(lights));
        TilesPublished.Publish(tiles, Count(tiles));
        BufferCatalog.Set(BufferCatalog.PointLights, LightsPublished);
        BufferCatalog.Set(BufferCatalog.TileIndices, TilesPublished);
    }

    internal static void Clear()
    {
        LightsPublished.Clear();
        TilesPublished.Clear();
        BufferCatalog.Set(BufferCatalog.PointLights, null);
        BufferCatalog.Set(BufferCatalog.TileIndices, null);
    }

    internal static void Release()
    {
        Clear();
        lock (Gate)
        {
            if (dummyLights != null)
            {
                MyManagers.Buffers.Dispose(dummyLights);
                dummyLights = null;
            }

            if (dummyTiles != null)
            {
                MyManagers.Buffers.Dispose(dummyTiles);
                dummyTiles = null;
            }
        }
    }

    /// <summary>
    /// Live catalog SRV, or a 1-element dummy so t10/t11 are never unbound.
    /// </summary>
    public static ISrvBindable SrvOrDummy(string catalogName)
    {
        var buf = BufferCatalog.Active(catalogName);
        if (buf != null && buf.IsAvailable && buf.Srv is ISrvBindable live)
            return live;
        EnsureDummy();
        if (string.Equals(catalogName, BufferCatalog.PointLights, StringComparison.OrdinalIgnoreCase))
            return dummyLights;
        return dummyTiles;
    }

    static void EnsureDummy()
    {
        lock (Gate)
        {
            if (dummyLights != null && dummyTiles != null)
                return;
            dummyLights ??= MyManagers.Buffers.CreateSrv("Anomaly.DummyPointLights", 1,
                PointLightStride, null, ResourceUsage.Dynamic, isGlobal: true);
            dummyTiles ??= MyManagers.Buffers.CreateSrv("Anomaly.DummyTileIndices", 1,
                TileIndexStride, null, ResourceUsage.Dynamic, isGlobal: true);
        }
    }

    static void EnsureFields()
    {
        if (fieldsResolved)
            return;
        lock (Gate)
        {
            if (fieldsResolved)
                return;
            var type = typeof(MyLightsRendering);
            lightsField = AccessTools.Field(type, "m_pointlightCullHwBuffer");
            tilesField = AccessTools.Field(type, "m_tileIndices");
            visibleField = AccessTools.Field(type, "m_lastFrameVisiblePointlights");
            fieldsResolved = true;
        }
    }

    static int Count(IBuffer buffer)
    {
        if (buffer == null)
            return 0;
        try
        {
            return Math.Max(buffer.ElementCount, 0);
        }
        catch
        {
            return 0;
        }
    }
}

sealed class StructuredSharedBuffer : ISharedBuffer
{
    ISrvBindable srv;
    IntPtr native;
    int count;

    public bool IsAvailable => srv != null && count > 0;
    public object Srv => srv;
    public IntPtr NativeResource => native;
    public int Width => count;
    public int Height => 1;

    public void Publish(ISrvBindable bindable, int elementCount)
    {
        srv = bindable;
        count = elementCount;
        native = IntPtr.Zero;
        if (bindable is IResource resource && resource.Resource != null)
            native = resource.Resource.NativePointer;
    }

    public void Clear()
    {
        srv = null;
        native = IntPtr.Zero;
        count = 0;
    }
}
