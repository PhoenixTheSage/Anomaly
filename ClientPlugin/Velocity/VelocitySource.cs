namespace ClientPlugin.Velocity;

public enum VelocitySource
{
    /// <summary>Write velocity during Keen's GBuffer pass (framework target).</summary>
    GBuffer,

    /// <summary>Plugin-owned depth-tested raster of movers (bootstrap only).</summary>
    OwnedRaster,

    /// <summary>Fullscreen camera-from-depth reprojection; no object motion.</summary>
    CameraOnly
}
