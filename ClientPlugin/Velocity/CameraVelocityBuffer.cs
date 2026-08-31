using System;

namespace ClientPlugin.Velocity;

/// <summary>
/// Camera-from-depth producer. Filled by <see cref="ShaderFramework.CameraVelocityPass"/>.
/// </summary>
internal sealed class CameraVelocityBuffer : IVelocityBuffer
{
    public static readonly CameraVelocityBuffer Instance = new();

    object srv;
    IntPtr nativeResource;
    int width;
    int height;
    bool historyValid;

    private CameraVelocityBuffer()
    {
    }

    public bool IsAvailable => srv != null && nativeResource != IntPtr.Zero && width > 0 && height > 0;

    public object Srv => srv;

    public IntPtr NativeResource => nativeResource;

    public int Width => width;

    public int Height => height;

    public VelocityConvention Convention =>
        VelocityConvention.Unjittered | VelocityConvention.PixelSpace |
        VelocityConvention.MatchesRenderResolution;

    public bool HistoryValid => historyValid;

    internal void Clear()
    {
        srv = null;
        nativeResource = IntPtr.Zero;
        width = 0;
        height = 0;
        historyValid = false;
    }

    internal void Publish(object srvBindable, IntPtr resource, int w, int h, bool history)
    {
        srv = srvBindable;
        nativeResource = resource;
        width = w;
        height = h;
        historyValid = history;
    }
}
