namespace ClientPlugin.Velocity;

public enum VelocitySource
{
    /// <summary>Write velocity during Keen's GBuffer pass (framework target).</summary>
    GBuffer,

    /// <summary>Fullscreen camera-from-depth reprojection; no object motion.</summary>
    CameraOnly

    // OwnedRaster is not shipped. GBuffer piggyback is the only object-MV path.
}
