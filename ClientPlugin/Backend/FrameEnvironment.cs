using System;
using VRageMath;

namespace ClientPlugin.Backend;

// CPU calculations shared by the DX11 frame owner and Vulkan guest adapter.
internal static class FrameEnvironment
{
    internal readonly struct Lighting
    {
        internal readonly Vector3 SunColor, SunToward, SkyAmbient;
        internal readonly float SunDiffuse, SkyLuma;
        internal Lighting(Vector3 color, Vector3 direction, float diffuse, float ambient)
        {
            SunColor = color;
            SunDiffuse = Math.Max(diffuse, 1f);
            var toward = -direction;
            if (!toward.IsValid() || toward.LengthSquared() < 1e-8f) toward = Vector3.Up;
            else toward.Normalize();
            SunToward = toward;
            SkyLuma = Math.Max(0f, Vector3.Dot(color, new Vector3(.2126f, .7152f, .0722f))) * Math.Max(ambient, 0f);
            SkyAmbient = color * .028f;
        }
    }

    internal sealed class Motion
    {
        Vector3D previousPosition;
        Vector3 previousForward;
        bool havePrevious;
        float safety = 1f;
        internal Vector3 Delta { get; private set; }
        internal void Reset() { havePrevious = false; safety = 1f; Delta = default; }
        internal float Capture(Vector3D position, Matrix cameraToWorld)
        {
            var forward = new Vector3(-cameraToWorld.M31, -cameraToWorld.M32, -cameraToWorld.M33);
            if (!forward.IsValid() || forward.LengthSquared() < 1e-8f) forward = Vector3.Forward;
            else forward.Normalize();
            float instant = 1f;
            if (havePrevious)
            {
                Delta = (Vector3)(position - previousPosition);
                instant = Math.Min(Ramp((float)Vector3D.Distance(position, previousPosition), 4f, 80f),
                    Ramp(Math.Max(0f, 1f - Vector3.Dot(previousForward, forward)), .02f, .45f));
            }
            else Delta = default;
            previousPosition = position; previousForward = forward; havePrevious = true;
            safety = instant <= safety ? instant : Math.Min(instant, safety + 1f / 16f);
            return safety;
        }
        static float Ramp(float value, float start, float zero) => value <= start ? 1f : value >= zero ? 0f : 1f - (value - start) / (zero - start);
    }
}
