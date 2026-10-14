using VRageMath;

namespace ClientPlugin.Backend;

/// <summary>
/// Backend-neutral camera and sun. DX11 fills this from <c>SceneSnapshot</c>.
/// Packs read it by reflection and do not reference <c>VRage.Render11</c>.
/// </summary>
public static class SceneView
{
    public static int Width { get; internal set; }
    public static int Height { get; internal set; }
    public static double CameraX { get; internal set; }
    public static double CameraY { get; internal set; }
    public static double CameraZ { get; internal set; }
    public static float SunX { get; internal set; }
    public static float SunY { get; internal set; }
    public static float SunZ { get; internal set; }
    public static string BackendName { get; internal set; } = "pending";

    public static Vector3D CameraPosition => new Vector3D(CameraX, CameraY, CameraZ);

    /// <summary>Keen <c>SunLightDirection</c> (from the camera toward the light).</summary>
    public static Vector3 SunLightDirection => new Vector3(SunX, SunY, SunZ);

    /// <summary>Direction from the camera toward the sun.</summary>
    public static Vector3 SunToward => new Vector3(-SunX, -SunY, -SunZ);

    internal static void Set(int width, int height, double x, double y, double z, float sx, float sy, float sz)
    {
        Width = width;
        Height = height;
        CameraX = x;
        CameraY = y;
        CameraZ = z;
        SunX = sx;
        SunY = sy;
        SunZ = sz;
    }
}

/// <summary>
/// Marker type. SE-Vulkan allows this assembly only when it does not reference
/// <c>VRage.Render11</c> or SharpDX. The presence of this type is the guest contract.
/// </summary>
public static class RenderGuestContract
{
    public const int Version = 1;
    public static string Describe() => "Anomaly guest v" + Version + " backend=" + SceneView.BackendName;
}
