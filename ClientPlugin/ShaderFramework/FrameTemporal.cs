using VRageMath;
using VRageRender;

namespace ClientPlugin.Shaders;

/// <summary>
/// Well-known type. Resolve by name:
/// <c>ClientPlugin.Shaders.FrameTemporal</c>. SE-DLSS owns Halton jitter
/// (<c>Projection.M31</c> / <c>M32</c>). Anomaly reads it and republishes
/// an unjittered view-projection for owned passes and extras CBs. Do not
/// patch the projection from a pack.
/// </summary>
public static class FrameTemporal
{
    static readonly object Gate = new();
    static uint frameIndex;
    static bool snapshotted;
    static float jitterX;
    static float jitterY;
    static bool historyValid;
    static int renderWidth;
    static int renderHeight;
    static Matrix unjitteredViewProj;
    static Matrix prevViewProj;
    static Matrix cameraToWorld = Matrix.Identity;
    static Vector2 projScale = new Vector2(1f, 1f);
    static Matrix storedPrev;
    static bool hasPrev;
    static Vector3D prevCameraPos;
    static Vector3 prevForward = new Vector3(0f, 0f, -1f);
    static bool hasCameraPrev;
    static float safetyScale = 1f;

    public static uint FrameIndex
    {
        get { lock (Gate) return frameIndex; }
    }

    public static float JitterX
    {
        get { lock (Gate) return jitterX; }
    }

    public static float JitterY
    {
        get { lock (Gate) return jitterY; }
    }

    public static bool HistoryValid
    {
        get { lock (Gate) return historyValid; }
    }

    public static int RenderWidth
    {
        get { lock (Gate) return renderWidth; }
    }

    public static int RenderHeight
    {
        get { lock (Gate) return renderHeight; }
    }

    public static Matrix UnjitteredViewProj
    {
        get { lock (Gate) return unjitteredViewProj; }
    }

    public static Matrix PrevViewProj
    {
        get { lock (Gate) return prevViewProj; }
    }

    /// <summary>
    /// Camera-at-origin inverse view (<c>InvViewAt0</c>). First three rows
    /// are published on extras as <c>AnomalyCameraToWorld</c>.
    /// </summary>
    public static Matrix CameraToWorld
    {
        get { lock (Gate) return cameraToWorld; }
    }

    /// <summary>
    /// Unjittered projection <c>M11</c> / <c>M22</c> for screen-ray reconstruct.
    /// </summary>
    public static Vector2 ProjScale
    {
        get { lock (Gate) return projScale; }
    }

    /// <summary>
    /// This-frame camera translation / look, 1 = calm, 0 = cut or slam.
    /// Written to extras as <c>AnomalySafetyScale</c>. First frame is 1.
    /// </summary>
    public static float SafetyScale
    {
        get { lock (Gate) return safetyScale; }
    }

    public const float SafetyMoveStartMeters = 4f;
    public const float SafetyMoveZeroMeters = 80f;
    public const float SafetyTurnStart = 0.02f;
    public const float SafetyTurnZero = 0.45f;

    internal static Vector4 CameraToWorldRow(int row)
    {
        lock (Gate)
        {
            switch (row)
            {
                case 0:
                    return new Vector4(cameraToWorld.M11, cameraToWorld.M12, cameraToWorld.M13, cameraToWorld.M14);
                case 1:
                    return new Vector4(cameraToWorld.M21, cameraToWorld.M22, cameraToWorld.M23, cameraToWorld.M24);
                default:
                    return new Vector4(cameraToWorld.M31, cameraToWorld.M32, cameraToWorld.M33, cameraToWorld.M34);
            }
        }
    }

    internal static void BeginFrame()
    {
        lock (Gate)
        {
            frameIndex++;
            snapshotted = false;
        }
    }

    internal static void EnsureSnapshot()
    {
        lock (Gate)
        {
            if (snapshotted)
                return;
            var env = MyRender11.Environment?.Matrices;
            var size = MyRender11.ResolutionI;
            renderWidth = size.X > 0 ? size.X : 1;
            renderHeight = size.Y > 0 ? size.Y : 1;
            if (env == null)
            {
                snapshotted = true;
                return;
            }

            jitterX = env.Projection.M31;
            jitterY = env.Projection.M32;
            var unjittered = UnjitteredViewProjection(env);
            historyValid = hasPrev;
            unjitteredViewProj = unjittered;
            prevViewProj = hasPrev ? storedPrev : unjittered;
            cameraToWorld = env.InvViewAt0;
            projScale = new Vector2(env.Projection.M11, env.Projection.M22);
            safetyScale = ComputeSafetyScale(env.CameraPosition, ForwardFromInvView(cameraToWorld));
            storedPrev = unjittered;
            hasPrev = true;
            snapshotted = true;
        }
    }

    /// <summary>
    /// Call when a camera cut / resize invalidates temporal history.
    /// Does not steal SE-DLSS jitter.
    /// </summary>
    public static void InvalidateHistory()
    {
        lock (Gate)
        {
            hasPrev = false;
            historyValid = false;
            hasCameraPrev = false;
            safetyScale = 1f;
        }
    }

    internal static void Release()
    {
        lock (Gate)
        {
            snapshotted = false;
            hasPrev = false;
            historyValid = false;
            hasCameraPrev = false;
            safetyScale = 1f;
            jitterX = jitterY = 0;
        }
    }

    static float ComputeSafetyScale(Vector3D cameraPos, Vector3 forward)
    {
        if (!hasCameraPrev)
        {
            prevCameraPos = cameraPos;
            prevForward = forward;
            hasCameraPrev = true;
            return 1f;
        }

        var move = (float)Vector3D.Distance(cameraPos, prevCameraPos);
        var turn = 1f - Vector3.Dot(prevForward, forward);
        if (turn < 0f)
            turn = 0f;
        prevCameraPos = cameraPos;
        prevForward = forward;
        var moveScale = Ramp(move, SafetyMoveStartMeters, SafetyMoveZeroMeters);
        var turnScale = Ramp(turn, SafetyTurnStart, SafetyTurnZero);
        return moveScale < turnScale ? moveScale : turnScale;
    }

    static float Ramp(float value, float start, float zero)
    {
        if (value <= start)
            return 1f;
        if (value >= zero)
            return 0f;
        return 1f - (value - start) / (zero - start);
    }

    static Vector3 ForwardFromInvView(Matrix invViewAt0)
    {
        var fwd = new Vector3(-invViewAt0.M31, -invViewAt0.M32, -invViewAt0.M33);
        if (fwd.LengthSquared() < 1e-8f)
            return new Vector3(0f, 0f, -1f);
        fwd.Normalize();
        return fwd;
    }

    static Matrix UnjitteredViewProjection(MyEnvironmentMatrices env)
    {
        var proj = env.Projection;
        proj.M31 = 0f;
        proj.M32 = 0f;
        return env.ViewAt0 * proj;
    }
}
