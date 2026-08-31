using System;

namespace ClientPlugin.Velocity;

/// <summary>
/// Placeholder until camera blit or GBuffer injection produces a real RT.
/// </summary>
internal sealed class UnavailableVelocityBuffer : IVelocityBuffer
{
    public static readonly UnavailableVelocityBuffer Instance = new();

    private UnavailableVelocityBuffer()
    {
    }

    public bool IsAvailable => false;
    public object Srv => null;
    public IntPtr NativeResource => IntPtr.Zero;
    public int Width => 0;
    public int Height => 0;

    public VelocityConvention Convention =>
        VelocityConvention.Unjittered | VelocityConvention.PixelSpace |
        VelocityConvention.MatchesRenderResolution;

    public bool HistoryValid => false;
}
