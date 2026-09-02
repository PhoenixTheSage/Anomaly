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
    static Matrix storedPrev;
    static bool hasPrev;

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
        }
    }

    internal static void Release()
    {
        lock (Gate)
        {
            snapshotted = false;
            hasPrev = false;
            historyValid = false;
            jitterX = jitterY = 0;
        }
    }

    static Matrix UnjitteredViewProjection(MyEnvironmentMatrices env)
    {
        var proj = env.Projection;
        proj.M31 = 0f;
        proj.M32 = 0f;
        return env.ViewAt0 * proj;
    }
}
