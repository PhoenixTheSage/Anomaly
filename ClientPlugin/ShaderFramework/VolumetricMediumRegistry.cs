using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ClientPlugin.Shaders;

/// <summary>
/// CPU registration contract for shared participating media. Registration does
/// not suppress a legacy renderer. Only a complete, current-frame transaction
/// may grant ownership of a provider's near interval to the shared renderer.
/// </summary>
public static class VolumetricMediumRegistry
{
    public const int ContractVersion = 1;
    public const int MaximumProviders = 4;
    public const int UniformFloatCount = 64;
    public const int HighColumnPixels = 8;
    public const int HighDepthSlices = 96;
    public const float MaximumDistance = 8000;
    static readonly object Gate = new();
    static readonly Dictionary<string, Medium> Media = new(StringComparer.OrdinalIgnoreCase);
    static long revision, historyRevision;
    static readonly HashSet<string> Requests = new(StringComparer.OrdinalIgnoreCase);
    internal static bool Requested { get { lock(Gate) return Requests.Count > 0; } }
    internal static bool HasEnabledMedia
    {
        get { lock (Gate) { foreach (var m in Media.Values) if (m.Enabled) return true; return false; } }
    }
    internal static bool HasReadyMedia
    {
        get
        {
            lock (Gate)
            {
                bool any = false;
                foreach (var m in Media.Values) if (m.Enabled) { any = true; if (!m.ParametersSet) return false; }
                return any;
            }
        }
    }
    internal static int Quality { get; private set; } = 1;
    internal static int DebugView { get; private set; }
    internal static float Distance { get; private set; } = 8000;
    internal static long HistoryRevision { get { lock(Gate) return historyRevision; } }
    public static void RequestRenderer(string id,bool enabled)
    {
        if(string.IsNullOrWhiteSpace(id)) return;
        lock(Gate) { bool changed=enabled?Requests.Add(id):Requests.Remove(id); if(changed) Invalidate("Renderer request changed"); }
    }
    public static void Configure(int quality,float distance,int debugView)
    {
        if(!Finite(distance)) throw new ArgumentException("Distance must be finite");
        lock(Gate) {
            quality=Math.Max(0,Math.Min(2,quality)); distance=Math.Max(100,Math.Min(MaximumDistance,distance));
            if(Quality!=quality || Distance!=distance) { Quality=quality; Distance=distance; Invalidate("Volume quality changed"); }
            DebugView=Math.Max(0,Math.Min(6,debugView));
        }
    }
    public static void SetOrigin(string id,double x,double y,double z)
    {
        if(!Finite(x)||!Finite(y)||!Finite(z)) throw new ArgumentException("Origin must be finite");
        lock(Gate) if(Media.TryGetValue(id,out var medium) && (medium.Origin[0]!=x || medium.Origin[1]!=y || medium.Origin[2]!=z))
        { medium.Origin=new[]{x,y,z}; Invalidate("Medium origin changed"); }
    }
    public static void InvalidateHistory(string id)
    { lock(Gate) if(Media.ContainsKey(id)) historyRevision++; }
    public static void SetIntervalCallback(string id,Action<float> callback)
    { lock(Gate) if(Media.TryGetValue(id,out var medium)) medium.Interval=callback; }
    internal static void SetStatus(string value) { lock(Gate) status=value; }
    internal static void ResetIntervals()
    {
        var callbacks = new HashSet<Action<float>>();
        lock(Gate)
        {
            activeRevision=-1; activeProviders=null;
            if (activeMedia != null) foreach (var medium in activeMedia) if (medium.Interval != null) callbacks.Add(medium.Interval);
            foreach (var medium in Media.Values) if (medium.Interval != null) callbacks.Add(medium.Interval);
            activeMedia = null;
        }
        List<Exception> errors = null;
        foreach(var callback in callbacks)
        {
            try { callback(0); }
            catch (Exception e) { (errors ??= new List<Exception>()).Add(e); }
        }
        if (errors != null) throw new AggregateException("Volume interval reset failed", errors);
    }

    static long activeRevision = -1;
    static uint activeFrame;
    static HashSet<string> activeProviders;
    static Medium[] activeMedia;
    static string status = "Shared volume backend not ready; legacy rendering retained";

    public static string StatusLine { get { lock (Gate) return status; } }
    public static long Revision { get { lock (Gate) return revision; } }

    /// <summary>shaderFile exports EvaluateMedium; bindings are immutable catalog names.</summary>
    public static bool Register(string id, string shaderFile, string[] catalogBindings)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(shaderFile)) return false;
        string path;
        try { path = Path.GetFullPath(shaderFile); }
        catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) { return false; }
        if (!File.Exists(path) || !path.EndsWith(".hlsli", StringComparison.OrdinalIgnoreCase)) return false;
        if (catalogBindings == null || catalogBindings.Length > 3 || catalogBindings.Any(string.IsNullOrWhiteSpace)) return false;
        lock (Gate)
        {
            if (Media.TryGetValue(id, out var existing))
                return existing.ShaderFile == path && existing.CatalogBindings.SequenceEqual(catalogBindings);
            if (Media.Count == MaximumProviders) return false;
            Media.Add(id, new Medium(id, path, (string[])catalogBindings.Clone()));
            Invalidate("Provider membership changed");
            return true;
        }
    }

    public static void Unregister(string id)
    {
        if (id == null) return;
        lock (Gate) if (Media.Remove(id)) Invalidate("Provider removed");
    }

    public static void SetEnabled(string id, bool enabled)
    {
        if (id == null) return;
        lock (Gate) if (Media.TryGetValue(id, out var medium) && medium.Enabled != enabled)
        { medium.Enabled = enabled; Invalidate("Provider enable state changed"); }
    }

    /// <summary>motion is world-space m/s; bounds are world-space meters, never camera-relative.</summary>
    public static void SetParameters(string id, float[] uniforms, double[] bounds, float[] motion)
    {
        if (uniforms == null || uniforms.Length > UniformFloatCount || uniforms.Any(v => !Finite(v)))
            throw new ArgumentException("Expected at most 64 finite uniform floats", nameof(uniforms));
        if (bounds == null || bounds.Length != 6 || bounds.Any(v => !Finite(v)) ||
            bounds[0] > bounds[3] || bounds[1] > bounds[4] || bounds[2] > bounds[5])
            throw new ArgumentException("Expected ordered, finite world AABB", nameof(bounds));
        if (motion == null || motion.Length != 3 || motion.Any(v => !Finite(v)))
            throw new ArgumentException("Expected finite world velocity", nameof(motion));
        var padded = new float[UniformFloatCount];
        Array.Copy(uniforms, padded, uniforms.Length);
        lock (Gate)
        {
            if (!Media.TryGetValue(id, out var medium)) return;
            if (medium.ParametersSet && medium.Uniforms.SequenceEqual(padded) &&
                medium.Bounds.SequenceEqual(bounds) && medium.Motion.SequenceEqual(motion)) return;
            medium.Uniforms = padded;
            medium.Bounds = (double[])bounds.Clone();
            medium.Motion = (float[])motion.Clone();
            medium.ParametersSet = true;
            revision++; activeRevision=-1; // Motion/advection changes are rejected per voxel, not a global history cut.
        }
    }

    /// <summary>No previous-frame activation: consumers must supply their exact render frame.</summary>
    public static bool OwnsNearInterval(string id, uint frame)
    {
        if (id == null) return false;
        lock (Gate) return activeProviders != null && activeFrame == frame && activeProviders.Contains(id);
    }

    internal static Medium[] Capture(out long capturedRevision)
        => Capture(out capturedRevision,out _,out _,out _,out _);

    internal static Medium[] Capture(out long capturedRevision,out long capturedHistoryRevision,
        out int quality,out float distance,out int debugView)
    {
        lock (Gate)
        {
            capturedRevision = revision;
            capturedHistoryRevision=historyRevision; quality=Quality; distance=Distance; debugView=DebugView;
            return Media.Values.Where(m => m.Enabled).OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                .Select(m => m.Copy()).ToArray();
        }
    }

    // Must be called only after all resources and all provider shaders succeed.
    // A failed/stale shadow, room or cloud field cannot acquire legacy ownership.
    internal static bool Commit(uint frame, long capturedRevision, bool resourcesReady,
        bool shadowsReady, bool interiorsReady, bool temporalReady, Medium[] frameMedia = null)
    {
        lock (Gate)
        {
            activeRevision = -1; activeProviders=null;
            var committed = frameMedia ?? Media.Values.Where(m=>m.Enabled).ToArray();
            if ((frameMedia == null && capturedRevision != revision) || !resourcesReady || !shadowsReady || !interiorsReady ||
                !temporalReady || committed.Length == 0 ||
                committed.Any(m => !m.ParametersSet))
            { status = "Shared volume prerequisites incomplete; legacy rendering retained"; return false; }
            activeProviders=new HashSet<string>(committed.Select(m=>m.Id),StringComparer.OrdinalIgnoreCase);
            activeMedia = committed;
            activeFrame = frame;
            activeRevision = revision;
            status = "Shared volume frame ready";
            return true;
        }
    }

    internal static void Release()
    { lock (Gate) { activeProviders=null; Invalidate("Volume resources released; legacy rendering retained"); } }

    static void Invalidate(string reason) { revision++; historyRevision++; activeRevision = -1; status = reason; }
    static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    internal sealed class Medium
    {
        internal readonly string Id, ShaderFile;
        internal readonly string[] CatalogBindings;
        internal bool Enabled, ParametersSet;
        internal Action<float> Interval;
        internal double[] Origin = new double[3];
        internal float[] Uniforms = new float[UniformFloatCount], Motion = new float[3];
        internal double[] Bounds = new double[6];
        internal Medium(string id, string file, string[] bindings) { Id = id; ShaderFile = file; CatalogBindings = bindings; }
        internal Medium Copy() => new Medium(Id, ShaderFile, (string[])CatalogBindings.Clone())
        {
            Enabled = Enabled, ParametersSet = ParametersSet, Interval=Interval, Origin=(double[])Origin.Clone(),
            Uniforms = (float[])Uniforms.Clone(), Motion = (float[])Motion.Clone(), Bounds = (double[])Bounds.Clone()
        };
    }
}
