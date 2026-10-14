using VRageRender;

namespace ClientPlugin.ShaderFramework;

/// <summary>
/// DX11-only camera and sun snapshot. The core <c>SceneView</c> copies these
/// fields by reflection so packs do not reference <c>VRage.Render11</c>.
/// </summary>
public static class SceneSnapshot
{
    public static int Width { get; private set; }
    public static int Height { get; private set; }
    public static double CameraX { get; private set; }
    public static double CameraY { get; private set; }
    public static double CameraZ { get; private set; }
    public static float SunX { get; private set; }
    public static float SunY { get; private set; }
    public static float SunZ { get; private set; }

    public static void Capture()
    {
        try
        {
            var size = MyRender11.ResolutionI;
            Width = size.X;
            Height = size.Y;
            var camera = MyRender11.Environment.Matrices.CameraPosition;
            CameraX = camera.X;
            CameraY = camera.Y;
            CameraZ = camera.Z;
            var sun = MyRender11.Environment.Data.EnvironmentLight.SunLightDirection;
            SunX = sun.X;
            SunY = sun.Y;
            SunZ = sun.Z;
        }
        catch
        {
            // Renderer not live yet.
        }
    }
}
