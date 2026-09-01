namespace ClientPlugin.Velocity;

public enum VelocitySource
{
    /// <summary>Write velocity during Keen's GBuffer pass (framework target).</summary>
    GBuffer,

    /// <summary>Fullscreen camera-from-depth reprojection; no object motion.</summary>
    CameraOnly
}
