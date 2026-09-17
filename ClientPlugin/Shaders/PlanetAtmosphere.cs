using System;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Planet;
using Sandbox.Game.World;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Shaders;

/// <summary>
/// Well-known type. Resolve by name:
/// <c>ClientPlugin.Shaders.PlanetAtmosphere</c> — do not take a
/// compile-time reference. Game-thread only for
/// <see cref="UpdateFromGameThread"/>. Radii are meters from the
/// planet center (not camera-relative). Fail closed to 0 when no
/// eligible planet is in session.
/// </summary>
public static class PlanetAtmosphere
{
    public const float MatchMeters = 64f;

    static readonly object Gate = new();
    static Snapshot current;
    static long loggedPlanetId;

    public readonly struct Snapshot
    {
        public readonly Vector3D Center;
        public readonly float AirTop;
        public readonly float VisualCeil;
        public readonly float TerrainRadius;
        public readonly float HillRadius;
        public readonly float AtmosphereRadius;
        public readonly bool IsValid;

        public Snapshot(Vector3D center, float airTop, float visualCeil,
            float terrainRadius, float hillRadius, float atmosphereRadius, bool isValid)
        {
            Center = center;
            AirTop = airTop;
            VisualCeil = visualCeil;
            TerrainRadius = terrainRadius;
            HillRadius = hillRadius;
            AtmosphereRadius = atmosphereRadius;
            IsValid = isValid;
        }
    }

    /// <summary>
    /// Nearest <see cref="MyPlanet"/> with <c>HasAtmosphere</c> or
    /// CloudLayers. Call from <c>Plugin.Update</c> only.
    /// </summary>
    public static void UpdateFromGameThread()
    {
        if (MySession.Static == null || MySector.MainCamera == null || MyPlanets.Static == null)
        {
            Clear();
            return;
        }

        var camera = MySector.MainCamera.Position;
        MyPlanet planet = null;
        var best = double.MaxValue;
        foreach (var candidate in MyPlanets.GetPlanets())
        {
            if (candidate == null || candidate.Closed || !IsEligible(candidate))
                continue;
            var d = (camera - candidate.PositionComp.GetPosition()).LengthSquared();
            if (d >= best)
                continue;
            best = d;
            planet = candidate;
        }

        if (planet == null ||
            !TryComputeRadii(planet, out var airTop, out var visualCeil, out var terrain, out var hill))
        {
            Clear();
            return;
        }

        var center = planet.PositionComp.GetPosition();
        var mesh = planet.AtmosphereRadius;
        var snap = new Snapshot(center, airTop, visualCeil, terrain, hill, mesh, true);
        lock (Gate)
            current = snap;
        LogOnce(planet, airTop, visualCeil);
    }

    public static void Clear()
    {
        lock (Gate)
        {
            current = default;
            loggedPlanetId = 0;
        }
    }

    public static Snapshot Copy()
    {
        lock (Gate)
            return current;
    }

    /// <summary>
    /// Pack-facing: extras for this world-space planet center, or false
    /// when the snapshot is empty / a different planet.
    /// </summary>
    public static bool TryGetRadii(Vector3D worldCenter, float matchMeters, out float airTop,
        out float visualCeil)
    {
        airTop = 0f;
        visualCeil = 0f;
        var limit = matchMeters > 1f ? matchMeters : MatchMeters;
        Snapshot snap;
        lock (Gate)
            snap = current;
        if (!snap.IsValid || snap.VisualCeil <= 1f)
            return false;
        if ((snap.Center - worldCenter).LengthSquared() > (double)limit * limit)
            return false;
        airTop = snap.AirTop;
        visualCeil = snap.VisualCeil;
        return true;
    }

    /// <summary>
    /// Anomaly-owned column. <paramref name="airTop"/> is
    /// <c>AverageRadius + AtmosphereAltitude</c>.
    /// <paramref name="visualCeil"/> equals air top today (optical
    /// IsolatedMix cap). Never <c>0.90 × AtmosphereRadius</c>.
    /// </summary>
    public static bool TryComputeRadii(MyPlanet planet, out float airTop, out float visualCeil,
        out float terrain, out float hill)
    {
        airTop = 0f;
        visualCeil = 0f;
        terrain = 0f;
        hill = 0f;
        if (planet == null || planet.Closed)
            return false;

        var avg = planet.AverageRadius;
        terrain = planet.MinimumRadius > avg * 0.5f ? planet.MinimumRadius : avg;
        if (terrain < 1f)
            return false;
        hill = planet.MaximumRadius > terrain ? planet.MaximumRadius : terrain;
        if (avg < terrain * 0.5f)
            avg = terrain;

        var airColumn = planet.AtmosphereAltitude;
        if (airColumn < 200f)
            airColumn = Math.Max(hill - avg, 200f);

        airTop = avg + airColumn;
        visualCeil = airTop;
        return visualCeil > terrain + 80f;
    }

    static bool IsEligible(MyPlanet planet)
    {
        if (planet.HasAtmosphere)
            return true;
        var layers = planet.Generator?.CloudLayers;
        return layers != null && layers.Count > 0;
    }

    static void LogOnce(MyPlanet planet, float airTop, float visualCeil)
    {
        if (planet.EntityId == loggedPlanetId)
            return;
        loggedPlanetId = planet.EntityId;
        var name = planet.Generator?.Id.SubtypeName ?? planet.EntityId.ToString();
        var line = "PlanetAtmosphere '" + name + "': airTop "
            + (airTop / 1000f).ToString("0.#") + " km, visualCeil "
            + (visualCeil / 1000f).ToString("0.#") + " km";
        MyLog.Default.WriteLine("Anomaly " + line);
        ClientPlugin.DebugLog.Write(line);
    }
}
