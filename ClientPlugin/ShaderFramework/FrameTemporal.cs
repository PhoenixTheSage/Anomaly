using System;
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
    static Vector3 cameraDelta;
    static Vector3 sunColor;
    static Vector3 sunToward = new Vector3(0f, 1f, 0f);
    static float sunDiffuse = 1f;
    static float skyLuma;
    static Vector3 skyAmbient;

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
    /// Camera translation / look, 1 = calm, 0 = cut or slam.
    /// Written to extras as <c>AnomalySafetyScale</c>. First frame is 1.
    /// Drops match this frame; recovery is limited to
    /// <see cref="SafetyReleasePerFrame"/> so march packs do not chatter.
    /// </summary>
    public static float SafetyScale
    {
        get { lock (Gate) return safetyScale; }
    }

    /// <summary>
    /// This-frame camera translation in meters (current − previous).
    /// Zero on the first frame or after <see cref="InvalidateHistory"/>.
    /// Used to origin-correct <see cref="PrevViewProj"/> for IsolatedAdd MVs.
    /// </summary>
    public static Vector3 CameraDelta
    {
        get { lock (Gate) return cameraDelta; }
    }

    /// <summary>
    /// <c>EnvironmentLight.SunColorRaw</c>. Written to extras as
    /// <c>AnomalySunColor</c>. Zero when Environment is missing.
    /// </summary>
    public static Vector3 SunColor
    {
        get { lock (Gate) return sunColor; }
    }

    /// <summary>
    /// World direction toward the sun (<c>-SunLightDirection</c>).
    /// Written to extras as <c>AnomalySunToward</c>.
    /// </summary>
    public static Vector3 SunToward
    {
        get { lock (Gate) return sunToward; }
    }

    /// <summary>
    /// <c>max(SunDiffuseFactor, 1)</c>. Written as <c>AnomalySunDiffuse</c>.
    /// </summary>
    public static float SunDiffuse
    {
        get { lock (Gate) return sunDiffuse; }
    }

    /// <summary>
    /// Rec.709 luma of <see cref="SunColor"/> times Keen
    /// <c>AmbientDiffuseFactor</c>. Written as <c>AnomalySkyLuma</c>.
    /// Zero when Environment is missing. Daylight proxy — not night fill.
    /// </summary>
    public static float SkyLuma
    {
        get { lock (Gate) return skyLuma; }
    }

    /// <summary>
    /// Unlifted sky/ambient RGB for IsolatedMix volumes. Written as
    /// <c>AnomalySkyAmbient</c>. Keen has no night-sky colour. This is
    /// <see cref="SunColor"/> times <see cref="SkyAmbientFloor"/> only.
    /// Do not multiply <c>AmbientForwardPass</c> — Keen adds probe ambient
    /// into that field (up to AmbientMaxClamp ≈ 0.3), which paints night
    /// IsolatedMix white. Never AmbientDiffuse or HdrLift.
    /// Zero when Environment is missing.
    /// </summary>
    public static Vector3 SkyAmbient
    {
        get { lock (Gate) return skyAmbient; }
    }

    /// <summary>
    /// Floor for <see cref="SkyAmbient"/> as a fraction of unlifted sun
    /// (HZD / planet night fill ≈ 3%).
    /// </summary>
    public const float SkyAmbientFloor = 0.028f;

    public const float SafetyMoveStartMeters = 4f;
    public const float SafetyMoveZeroMeters = 80f;
    public const float SafetyTurnStart = 0.02f;
    public const float SafetyTurnZero = 0.45f;
    /// <summary>
    /// Per-frame rise toward a calm <see cref="SafetyScale"/>. Drops are
    /// applied immediately so a slam cannot keep a heavy march. Recovery
    /// is damped so spectator speed chatter cannot oscillate step counts
    /// (GPU hitch locked to undersampling grain).
    /// </summary>
    public const float SafetyReleasePerFrame = 1f / 16f;

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
            SnapshotEnvironmentLightUnlocked();
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
            safetyScale = ApplySafetyEnvelope(
                ComputeSafetyScale(env.CameraPosition, ForwardFromInvView(cameraToWorld)));
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
            cameraDelta = default;
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
            cameraDelta = default;
            jitterX = jitterY = 0;
            ClearEnvironmentLightUnlocked();
        }
    }

    static void SnapshotEnvironmentLightUnlocked()
    {
        var environment = MyRender11.Environment;
        if (environment == null)
        {
            ClearEnvironmentLightUnlocked();
            return;
        }

        var light = environment.Data.EnvironmentLight;
        sunColor = light.SunColorRaw;
        sunDiffuse = Math.Max(light.SunDiffuseFactor, 1f);
        var toward = -light.SunLightDirection;
        if (toward.LengthSquared() < 1e-8f)
            sunToward = new Vector3(0f, 1f, 0f);
        else
        {
            toward.Normalize();
            sunToward = toward;
        }

        var luma = Vector3.Dot(sunColor, new Vector3(0.2126f, 0.7152f, 0.0722f));
        if (luma < 0f)
            luma = 0f;
        skyLuma = luma * Math.Max(light.AmbientDiffuseFactor, 0f);
        skyAmbient = sunColor * SkyAmbientFloor;
    }

    static void ClearEnvironmentLightUnlocked()
    {
        sunColor = default;
        sunToward = new Vector3(0f, 1f, 0f);
        sunDiffuse = 1f;
        skyLuma = 0f;
        skyAmbient = default;
    }

    static float ComputeSafetyScale(Vector3D cameraPos, Vector3 forward)
    {
        if (!hasCameraPrev)
        {
            prevCameraPos = cameraPos;
            prevForward = forward;
            hasCameraPrev = true;
            cameraDelta = default;
            return 1f;
        }

        cameraDelta = (Vector3)(cameraPos - prevCameraPos);
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

    static float ApplySafetyEnvelope(float instant)
    {
        if (instant <= safetyScale)
            return instant;
        var next = safetyScale + SafetyReleasePerFrame;
        return next < instant ? next : instant;
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
