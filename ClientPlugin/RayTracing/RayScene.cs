using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Runtime.CompilerServices;
using ClientPlugin.Buffers;
using ClientPlugin.ShaderFramework;
using ClientPlugin.Shaders;
using HarmonyLib;
using SharpDX.Direct3D;
using VRage.Import;
using VRage.Render.Scene;
using VRage.Render11.Common;
using VRage.Render11.GeometryStage2.Instancing;
using VRage.Render11.GeometryStage2.Model;
using VRage.Render11.Resources;
using VRage.Render11.Resources.Textures;
using VRage.Render11.Scene;
using VRage.Render11.Scene.Components;
using VRage.Utils;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender;
using VRageRender.Messages;
using VRage.Render.Particles;

namespace ClientPlugin.Shaders;

// ABI v1 is documented in Docs/RayTracing.md. Arrays cross Pulsar assembly boundaries.
public static class RayScene
{
    public const int ApiVersion = 1;
    const long CaptureLimit = 512L * 1024 * 1024;
    static readonly object Gate = new();
    static bool deviceEnding;
    static readonly Dictionary<object, object[]> Meshes = new();
    static readonly Dictionary<object, long> Sizes = new();
    static readonly Dictionary<string, Action<object[]>> Consumers = new(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> FarSunRequested = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, bool> FrameEnabled = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<string, Action<object>> Completed = new(StringComparer.OrdinalIgnoreCase);
    static readonly Dictionary<uint, object[]> Emitters = new();
    static long bytes;
    static int revision, epoch = 1;
    static int requested;
    internal static SharpDX.Direct3D11.ShaderResourceView SunShadows;
    public static string Status { get; private set; } = "idle";
    internal static bool HasEnabledFrames { get { lock (Gate) { if (Meshes.Count == 0) return false; foreach (var id in FarSunRequested) if (FrameEnabled.TryGetValue(id, out var enabled) && enabled) return true; return false; } } }
    public static bool CaptureRequested => Volatile.Read(ref requested) != 0;

    public static bool Register(string id, int apiVersion, Action<object[]> frame)
    {
        if (apiVersion != ApiVersion || string.IsNullOrWhiteSpace(id) || frame == null) return false;
        lock (Gate) { Consumers[id] = frame; FrameEnabled[id] = true; Volatile.Write(ref requested, Consumers.Count); }
        OwnedPassRegistry.Register("ray.scene." + id, OwnedPassSlot.AfterLighting, -100,
            TemporalPolicy.InColor, ctx => Invoke(id, ctx), OwnedPassPhase.BeforeFullscreen);
        return true;
    }

    public static void Unregister(string id)
    {
        lock (Gate) { Consumers.Remove(id); FrameEnabled.Remove(id); FarSunRequested.Remove(id); Completed.Remove(id); Volatile.Write(ref requested, Consumers.Count); }
        OwnedPassRegistry.Unregister("ray.scene." + id);
        OpaqueLightingOwnership.Release(id);
    }

    public static void RegisterCompleted(string id, Action<object> completed) { lock (Gate) Completed[id] = completed; }
    // Upload capture remains armed; disabled consumers avoid frame enumeration and texture lookup.
    public static void SetFarSunEnabled(string id, bool enabled) { lock (Gate) { if (enabled && Consumers.ContainsKey(id)) FarSunRequested.Add(id); else FarSunRequested.Remove(id); } }
    public static void SetFrameEnabled(string id, bool enabled) { lock (Gate) if (Consumers.ContainsKey(id)) FrameEnabled[id] = enabled; }
    // Explicit source ownership is the only deduplication criterion. Positions never identify a light.
    public static void SetEmitter(uint id, float[] cameraRelativeDisc, object texture, uint nativeLightOwner, uint sourceRevision)
    {
        if (id == 0 || cameraRelativeDisc == null || cameraRelativeDisc.Length != 16) throw new ArgumentException("Emitter ABI");
        lock (Gate) Emitters[id] = new object[] { id, (float[])cameraRelativeDisc.Clone(), texture, nativeLightOwner, sourceRevision };
    }
    public static void RemoveEmitter(uint id) { lock (Gate) Emitters.Remove(id); }
    internal static void NotifyCompleted()
    {
        Action<object>[] callbacks; lock (Gate) { callbacks = new Action<object>[Completed.Count]; Completed.Values.CopyTo(callbacks, 0); }
        foreach (var callback in callbacks) try { callback(MyRender11.RC.DeviceContext); } catch (Exception e) { Warn("completed callback", e); }
    }

    public static byte[] CompileShader(string path, int profile)
    {
        if (profile < 0 || profile > 3) throw new ArgumentOutOfRangeException(nameof(profile));
        var code = MyShaderCompiler.Compile(path, Array.Empty<ShaderMacro>(), (MyShaderProfile)profile,
            "Anomaly.RayTenant." + System.IO.Path.GetFileName(path), optimize: true, invalidateCache: false,
            out _, out string log, out _, savePdb: false, savePreprocessed: false);
        if (code == null || code.Length == 0) throw new InvalidOperationException("Ray tenant shader compilation failed: " + path + "\n" + log);
        return code;
    }

    internal static void CaptureModern(MyLod lod, ref MyMwmData data)
    {
        if (!CaptureRequested || data.Positions == null || data.Indices == null) return;
        try
        {
            int n = data.Positions.Length;
            if (n > CaptureLimit / 64) return;
            var vertices = new float[n * 16];
            for (int i = 0; i < n; i++)
            {
                var packed = data.Positions[i]; var p = PositionPacker.UnpackPosition(ref packed);
                var normal = data.Normals != null && i < data.Normals.Length ? VF_Packer.UnpackNormal(data.Normals[i].PackedValue) : Vector3.Up;
                var uv = data.Texcoords != null && i < data.Texcoords.Length ? data.Texcoords[i].ToVector2() : Vector2.Zero;
                WriteVertex(vertices, i, p, normal, uv);
                if (data.BoneIndices != null && data.BoneWeights != null && i < data.BoneIndices.Length && i < data.BoneWeights.Length)
                {
                    var b = data.BoneIndices[i]; var w = data.BoneWeights[i]; int j = i * 16;
                    vertices[j + 8] = b.X; vertices[j + 9] = b.Y; vertices[j + 10] = b.Z; vertices[j + 11] = b.W;
                    vertices[j + 12] = w.X; vertices[j + 13] = w.Y; vertices[j + 14] = w.Z; vertices[j + 15] = w.W;
                }
            }
            var parts = new List<object[]>();
            foreach (var part in data.Parts)
                parts.Add(Part(part.IndexOffset, part.IndicesCount, 0, part.MaterialName,
                    part.ColorMetalFilepath, part.NormalGlossFilepath, part.ExtensionFilepath, part.AlphamaskFilepath, part.Technique.ToString()));
            Store(lod, vertices, data.Indices.ToArray(), parts.ToArray(), false);
        }
        catch (Exception e) { Warn("modern capture", e); }
    }

    internal static void CaptureLegacy(LodMeshId lod)
    {
        if (!CaptureRequested || lod == LodMeshId.NULL) return;
        try
        {
            var info = lod.Info; var raw = info.Data;
            if (raw.VertexStream0 == null || raw.Indices == null || raw.VertexStream0.IsDisposed || raw.Indices.IsDisposed) return;
            int n = info.VerticesNum;
            if (n <= 0 || n > CaptureLimit / 64 || raw.Stride0 < 8) return;
            bool voxel = info.PartOffsets != null;
            var vertices = new float[n * 16];
            for (int i = 0; i < n; i++)
            {
                IntPtr p0 = IntPtr.Add(raw.VertexStream0.Ptr, checked(i * raw.Stride0));
                Vector3 p;
                if (voxel) p = Marshal.PtrToStructure<Vector3>(p0);
                else { var packed = Marshal.PtrToStructure<HalfVector4>(p0); p = PositionPacker.UnpackPosition(ref packed); }
                var normal = Vector3.Up; var uv = new Vector2(p.X, p.Z);
                if (raw.VertexStream1 != null && !raw.VertexStream1.IsDisposed && raw.Stride1 >= 4)
                {
                    IntPtr p1 = IntPtr.Add(raw.VertexStream1.Ptr, checked(i * raw.Stride1));
                    normal = VF_Packer.UnpackNormal(unchecked((uint)Marshal.ReadInt32(p1)));
                    if (!voxel && raw.Stride1 >= 12) uv = Marshal.PtrToStructure<HalfVector2>(IntPtr.Add(p1, 8)).ToVector2();
                }
                if (voxel && raw.Stride0 >= 16)
                {
                    int selector = Marshal.ReadByte(p0, 15);
                    uv = new Vector2(selector == 0 ? 1 : 0, selector == 1 ? 1 : 0);
                }
                WriteVertex(vertices, i, p, normal, uv);
                if (!voxel && raw.Stride0 == 20)
                {
                    var weights = Marshal.PtrToStructure<HalfVector4>(IntPtr.Add(p0, 8)).ToVector4(); int j = i * 16;
                    for (int b = 0; b < 4; b++) vertices[j + 8 + b] = Marshal.ReadByte(p0, 16 + b);
                    vertices[j + 12] = weights.X; vertices[j + 13] = weights.Y; vertices[j + 14] = weights.Z; vertices[j + 15] = weights.W;
                }
            }
            var indices = new int[info.IndicesNum];
            for (int i = 0; i < indices.Length; i++) indices[i] = raw.IndicesFmt == MyIndexBufferFormat.UInt
                ? Marshal.ReadInt32(raw.Indices.Ptr, i * 4) : (ushort)Marshal.ReadInt16(raw.Indices.Ptr, i * 2);
            var voxelParts = new List<object[]>();
            if (voxel) foreach (var p in info.PartOffsets)
            {
                var ids = new[] { p.Materials.I0, p.Materials.I1, p.Materials.I2 };
                var paths = new string[24]; var scales = new float[3];
                for (int m = 0; m < 3; ++m)
                {
                    int material = ids[m] == 255 ? ids[0] : ids[m];
                    if (material >= MyVoxelMaterials.Table.Length) continue;
                    var entry = MyVoxelMaterials.Table[material]; var r = entry.Resource;
                    scales[m] = 1 / Math.Max(entry.StandardTilingSetup.InitialScale, .001f);
                    for (int axis = 0; axis < 2; ++axis)
                    {
                        int b = m * 8 + axis * 4;
                        paths[b] = First(axis == 0 ? r.ColorMetalXZnY_Filepaths : r.ColorMetalY_Filepaths);
                        paths[b + 1] = First(axis == 0 ? r.NormalGlossXZnY_Filepaths : r.NormalGlossY_Filepaths);
                        paths[b + 2] = First(axis == 0 ? r.ExtXZnY_Filepaths : r.ExtY_Filepaths);
                    }
                }
                voxelParts.Add(new object[] { p.StartIndex, p.IndexCount, 0, "voxel:" + string.Join(":", ids), paths, "VOXEL", scales });
            }
            Store("legacy:" + lod.Index, vertices, indices, voxelParts.ToArray(), voxel);
        }
        catch (Exception e) { Warn("legacy capture", e); }
    }
    static string First(string[] paths) => paths != null && paths.Length > 0 ? paths[0] : null;

    static void Store(object key, float[] vertices, int[] indices, object[][] parts, bool voxel)
    {
        long size = vertices.LongLength * 4 + indices.LongLength * 4;
        lock (Gate)
        {
            Sizes.TryGetValue(key, out long old);
            if (bytes - old + size > CaptureLimit) { Status = "capture memory limit; uncovered meshes use native lighting"; return; }
            var payload = new object[] { Interlocked.Increment(ref revision), vertices, indices, parts, voxel };
            Meshes[key] = payload; Sizes[key] = size; bytes += size - old;
        }
    }

    static void WriteVertex(float[] v, int i, Vector3 p, Vector3 n, Vector2 uv)
    {
        int j = i * 16; v[j] = p.X; v[j + 1] = p.Y; v[j + 2] = p.Z;
        v[j + 3] = n.X; v[j + 4] = n.Y; v[j + 5] = n.Z; v[j + 6] = uv.X; v[j + 7] = uv.Y;
    }

    static object[] Part(int start, int count, int vertex, string name, string cm, string ng, string ext, string alpha, string technique)
        => new object[] { start, count, vertex, name ?? "", new[] { cm, ng, ext, alpha }, technique ?? "MESH" };

    internal static void Remove(object key)
    {
        lock (Gate) { if (deviceEnding) return; if (Sizes.TryGetValue(key, out var size)) bytes -= size; Sizes.Remove(key); Meshes.Remove(key); }
    }

    public static void Clear()
    {
        lock (Gate) { Meshes.Clear(); Sizes.Clear(); Emitters.Clear(); bytes = 0; epoch++; Status = "world/device reset"; }
        FarSunShadows.Release();
        NotifyReset();
    }
    internal static void ResetDevice()
    {
        lock (Gate) { epoch++; Status = "device reset; CPU captures retained"; }
        FarSunShadows.Release();
        NotifyReset();
    }
    internal static void BeginDeviceEnd() { lock (Gate) deviceEnding = true; ResetDevice(); }
    internal static void FinishDeviceEnd() { lock (Gate) deviceEnding = false; }
    static void NotifyReset()
    {
        SunShadows = null;
        Action<object>[] callbacks; lock (Gate) { callbacks = new Action<object>[Completed.Count]; Completed.Values.CopyTo(callbacks, 0); }
        foreach (var callback in callbacks) try { callback(null); } catch (Exception e) { Warn("reset callback", e); }
    }

    static void Invoke(string id, OwnedPassContext context)
    {
        Action<object[]> callback;
        lock (Gate) if (!Consumers.TryGetValue(id, out callback) || !FrameEnabled[id]) return;
        var rc = context.Rc; var env = MyRender11.Environment?.Matrices;
        if (env == null || MyGBuffer.Main == null || MyRender11.MultisamplingEnabled) { Status = "main view unavailable or MSAA enabled"; return; }
        try
        {
            var meshes = new List<object[]>(); var meshIndices = new Dictionary<object, int>(); var instances = new List<object[]>();
            var center = env.CameraPosition;
            foreach (var component in MyComponentFactory<MyInstanceComponent>.GetAll())
            {
                var actor = component.Owner;
                if (!InRange(actor, center)) continue;
                var lod = component.GetHighlightLod();
                object[] data = GetMesh(lod);
                if (data == null) { AddUnknown(actor, instances, center); continue; }
                int index = AddMesh(lod, data, meshIndices, meshes);
                var parts = (object[][])data[3]; var overrides = new object[parts.Length];
                for (int p = 0; p < parts.Length; p++)
                {
                    var part = parts[p]; var material = component.GetInstanceMaterial(MyStringId.GetOrCompute((string)part[3]));
                    var changes = component.GetTextureChanges();
                    var paths = (string[])part[4];
                    if (changes != null && changes.TryGetValue(MyStringId.GetOrCompute((string)part[3]), out var change)) paths = ChangePaths(paths, change);
                    var packed = material.PackedColorMultEmissivity.ToVector4();
                    overrides[p] = new object[] { part, new[] { packed.X, packed.Y, packed.Z, packed.W }, ResolveTextures(paths), Array.Empty<float>(), RayMaterialOptics.Capture(part) };
                }
                AddInstance(actor, index, overrides, instances, center);
            }
            foreach (var component in MyComponentFactory<MyRenderableComponent>.GetAll())
            {
                var actor = component.Owner;
                if (!InRange(actor, center)) continue;
                var lods = component.Lods;
                if (lods == null || lods.Length == 0) { AddUnknown(actor, instances, center); continue; }
                int lodIndex = Math.Max(0, Math.Min(component.CurrentLod, lods.Length - 1));
                var proxies = lods[lodIndex]?.RenderableProxies;
                if (proxies == null) { AddUnknown(actor, instances, center); continue; }
                var grouped = new Dictionary<int, List<object>>();
                foreach (var proxy in proxies)
                {
                    if (proxy == null || proxy.Mesh == LodMeshId.NULL) continue;
                    if (proxy.InstancingEnabled) { AddUnknown(actor, instances, center); continue; }
                    object key = "legacy:" + proxy.Mesh.Index; var data = GetMesh(key);
                    if (data == null) { AddUnknown(actor, instances, center); continue; }
                    int meshIndex = AddMesh(key, data, meshIndices, meshes);
                    if (!grouped.TryGetValue(meshIndex, out var overrides)) grouped[meshIndex] = overrides = new List<object>();
                    string[] paths; string name;
                    if (proxy.Material != MyMeshMaterialId.NULL)
                    {
                        var mat = proxy.Material.Info; name = mat.Name.String;
                        paths = new[] { mat.ColorMetal_Texture, mat.NormalGloss_Texture, mat.Extensions_Texture, mat.Alphamask_Texture };
                    }
                    else { name = "voxel"; paths = new string[4]; }
                    var textureChange = component.GetTextureChange(MyStringId.GetOrCompute(name));
                    if (textureChange.HasValue) paths = ChangePaths(paths, textureChange.Value);
                    var sub = proxy.DrawSubmesh;
                    object[] part = Part(sub.StartIndex, sub.IndexCount, sub.BaseVertex, name, paths[0], paths[1], paths[2], paths[3], proxy.Technique.ToString());
                    if ((bool)data[4])
                    {
                        part = null;
                        foreach (var candidate in (object[][])data[3]) if ((int)candidate[0] == sub.StartIndex && (int)candidate[1] == sub.IndexCount) { part = candidate; break; }
                        if (part == null) { AddUnknown(actor, instances, center); continue; }
                        paths = (string[])part[4];
                    }
                    var multiplier = new[] { 1f, 1f, 1f, 1f };
                    if (component.ModelProperties != null)
                        foreach (var property in component.ModelProperties)
                            if (property.Key.Material == MyStringId.GetOrCompute(name))
                            {
                                var m = property.Value; multiplier = new[] { m.ColorMul.X, m.ColorMul.Y, m.ColorMul.Z, m.Emissivity };
                                break;
                            }
                    var bones = BoneArray(proxy.SkinningMatrices, sub.BonesMapping);
                    overrides.Add(new object[] { part, multiplier, ResolveTextures(paths), bones, RayMaterialOptics.Capture(part) });
                }
                foreach (var group in grouped) AddInstance(actor, group.Key, group.Value.ToArray(), instances, center);
            }
            var lights = new List<float>();
            var physicalOrigins = new List<float>();
            var cookies = new List<object[]>();
            foreach (var light in MyComponentFactory<MyLightComponent>.GetAll())
            {
                var actor = light.Owner;
                if (actor == null || actor.IsDestroyed) continue;
                var data = light.Data;
                if (Vector3D.DistanceSquared(actor.WorldMatrix.Translation, center) > Math.Pow(256 + Math.Max(data.PointLight.Range, data.SpotLight.Light.Range), 2)) continue;
                if (data.PointLightOn && data.PointIntensity > 0.001f)
                {
                    var physical = actor.WorldMatrix.Translation - center;
                    physicalOrigins.Add((float)physical.X); physicalOrigins.Add((float)physical.Y); physicalOrigins.Add((float)physical.Z);
                    AddLight(lights, actor.WorldMatrix.Translation + actor.WorldMatrix.Forward * data.PointOffset - center,
                        data.PointLight.Color, data.PointLight.Range, actor.WorldMatrix.Forward, -1, 0, actor.ID, data.PointLight.Falloff, data.PointLight.DiffuseFactor, data.PointLight.GlossFactor);
                    cookies.Add(new object[] { null, new[] { (float)actor.WorldMatrix.Up.X, (float)actor.WorldMatrix.Up.Y, (float)actor.WorldMatrix.Up.Z } });
                }
                if (data.SpotLightOn && data.SpotIntensity > 0.001f)
                {
                    var physical = actor.WorldMatrix.Translation - center;
                    physicalOrigins.Add((float)physical.X); physicalOrigins.Add((float)physical.Y); physicalOrigins.Add((float)physical.Z);
                    AddLight(lights, actor.WorldMatrix.Translation - center, data.SpotLight.Light.Color,
                        data.SpotLight.Light.Range, actor.WorldMatrix.Forward, data.SpotLight.ApertureCos, 1, actor.ID, data.SpotLight.Light.Falloff, data.SpotLight.Light.DiffuseFactor, data.SpotLight.Light.GlossFactor);
                    object cookie = null;
                    if (!string.IsNullOrEmpty(data.ReflectorTexture)) try { cookie = MyManagers.Textures.GetTexture(data.ReflectorTexture, MyFileTextureEnum.CUSTOM).Texture?.Srv; } catch { }
                    cookies.Add(new object[] { cookie, new[] { (float)actor.WorldMatrix.Up.X, (float)actor.WorldMatrix.Up.Y, (float)actor.WorldMatrix.Up.Z } });
                }
            }
            var gb = MyGBuffer.Main;
            var velocity = BufferCatalog.Active(BufferCatalog.Velocity);
            var frame = new object[] { ApiVersion, epoch, FrameTemporal.FrameIndex, new[] { center.X, center.Y, center.Z },
                meshes.ToArray(), instances.ToArray(), lights.ToArray(),
                new object[] { MyRender11.DeviceInstance, rc.DeviceContext, gb.LBuffer.Rtv, gb.ResolvedDepthStencil.SrvDepth.Srv,
                    gb.GBuffer0.Srv, gb.GBuffer1.Srv, gb.GBuffer2.Srv, velocity?.Srv is ISrvBindable v ? v.Srv : null, MyCommon.FrameConstants.Buffer,
                    SunShadows, MyManagers.EnvironmentProbe.CloseCubemapFinal?.Srv, MyManagers.EnvironmentProbe.FarCubemapFinal?.Srv,
                    MyCommon.GetAmbientBrdfLut()?.Srv },
                new[] { context.Width, context.Height },
                new[] { FrameTemporal.SunToward.X, FrameTemporal.SunToward.Y, FrameTemporal.SunToward.Z, FrameTemporal.SunDiffuse,
                    FrameTemporal.SunColor.X, FrameTemporal.SunColor.Y, FrameTemporal.SunColor.Z, FrameTemporal.HistoryValid ? 1f : 0f,
                    FrameTemporal.SkyAmbient.X, FrameTemporal.SkyAmbient.Y, FrameTemporal.SkyAmbient.Z, MyCommon.FrameConstantsData.Postprocess.BloomEmissiveness },
                MatrixArray(FrameTemporal.UnjitteredViewProj), MatrixArray(FrameTemporal.PrevViewProj), CaptureEmitters(center), cookies.ToArray(), physicalOrigins.ToArray(), FarSunShadows.Product(FrameTemporal.FrameIndex, center) };
            rc.ClearState();
            callback(frame);
            Status = meshes.Count + " meshes, " + instances.Count + " instances, " + lights.Count / 16 + " native lights";
        }
        finally { rc.ClearState(); TransparentStageBindings.RestoreAfterAtmosphere(rc); }
    }

    static object[] GetMesh(object key) { lock (Gate) return key != null && Meshes.TryGetValue(key, out var data) ? data : null; }
    static int AddMesh(object key, object[] data, Dictionary<object, int> map, List<object[]> meshes)
    { if (map.TryGetValue(key, out int index)) return index; index = meshes.Count; map[key] = index; meshes.Add(data); return index; }
    static bool InRange(IMyActor actor, Vector3D center)
        // IsVisible is explicit actor visibility, not frustum culling. Off-screen casters remain resident.
        => actor != null && !actor.IsDestroyed && (actor.IsVisible || actor.ID == LocalCharacter.Copy().ActorId) && actor.WorldAabb.DistanceSquared(center) <= 256 * 256;
    static void AddUnknown(IMyActor actor, List<object[]> list, Vector3D center) => AddInstance(actor, -1, Array.Empty<object>(), list, center);
    static void AddInstance(IMyActor actor, int mesh, object[] parts, List<object[]> list, Vector3D center)
    {
        var world = actor.WorldMatrix; world.Translation -= center;
        var bounds = actor.WorldAabb;
        list.Add(new object[] { actor.ID, mesh, MatrixArray((Matrix)world), parts,
            new[] { (float)(bounds.Min.X - center.X), (float)(bounds.Min.Y - center.Y), (float)(bounds.Min.Z - center.Z),
                (float)(bounds.Max.X - center.X), (float)(bounds.Max.Y - center.Y), (float)(bounds.Max.Z - center.Z) } });
    }
    static object[] ResolveTextures(string[] paths)
    {
        var result = new object[paths.Length];
        var types = new[] { MyFileTextureEnum.COLOR_METAL, MyFileTextureEnum.NORMALMAP_GLOSS, MyFileTextureEnum.EXTENSIONS, MyFileTextureEnum.ALPHAMASK };
        for (int i = 0; i < paths.Length; i++)
            if (!string.IsNullOrEmpty(paths[i]))
                try { result[i] = MyManagers.Textures.GetTexture(paths[i], types[i % 4]).Texture?.Srv; } catch { }
        return result;
    }
    static string[] ChangePaths(string[] original, MyTextureChange change)
    {
        var paths = (string[])original.Clone();
        if (change.ColorMetalFileName != null) paths[0] = change.ColorMetalFileName;
        if (change.NormalGlossFileName != null) paths[1] = change.NormalGlossFileName;
        if (change.ExtensionsFileName != null) paths[2] = change.ExtensionsFileName;
        if (change.AlphamaskFileName != null) paths[3] = change.AlphamaskFileName;
        return paths;
    }
    static object[][] CaptureEmitters(Vector3D camera)
    {
        var result = new List<object[]>(); lock (Gate) result.AddRange(Emitters.Values);
        void Add(MyBillboard b)
        {
            if (b.CustomViewProjection != -1 || b.BlendType == MyBillboard.BlendTypeEnum.LDR || b.BlendType == MyBillboard.BlendTypeEnum.PostPP) return;
            var material = MyTransparentMaterials.GetMaterial(b.Material);
            if (material == null || (material.CanBeAffectedByOtherLights && b.BlendType == MyBillboard.BlendTypeEnum.Standard)) return;
            if (b.ColorIntensity <= 0 || b.Color.W <= 0) return;
            Vector3D center = b.LocalType == MyBillboard.LocalTypeEnum.Custom ? (b.Position0 + b.Position1 + b.Position2 + b.Position3) * .25 : b.Position0;
            Vector3D normal = b.LocalType == MyBillboard.LocalTypeEnum.Custom ? Vector3D.Cross(b.Position1 - b.Position0, b.Position2 - b.Position0) : Vector3D.Forward;
            double radius = b.LocalType == MyBillboard.LocalTypeEnum.Custom ? Math.Sqrt(Vector3D.DistanceSquared(b.Position0, center)) : Math.Abs(b.Position3.X);
            if (b.ParentID != uint.MaxValue)
            {
                var parent = MyIDTracker<MyActor>.FindByID(b.ParentID); if (parent == null) return;
                center = Vector3D.Transform(center, parent.WorldMatrix); normal = Vector3D.TransformNormal(normal, parent.WorldMatrix);
            }
            if (b.LocalType == MyBillboard.LocalTypeEnum.Line) center += b.Position1 * b.Position3.X * .5;
            if (Vector3D.DistanceSquared(center, camera) > 256 * 256 || radius <= .001) return;
            if (b.LocalType != MyBillboard.LocalTypeEnum.Custom) normal = camera - center;
            normal.Normalize();center -= camera;
            object texture = null;
            if (material.TextureType == MyTransparentMaterialTextureType.FileTexture && material.Texture != null)
                try { texture = MyManagers.Textures.GetTexture(material.Texture, MyFileTextureEnum.GUI).Texture?.Srv; } catch { }
            uint id = unchecked((uint)RuntimeHelpers.GetHashCode(b) ^ (uint)b.Material.Id * 16777619u) | 1u;
            var c = b.Color * b.ColorIntensity;
            uint rev = unchecked((uint)c.GetHashCode() ^ (uint)b.Material.Id);
            result.Add(new object[] { id, new[] { (float)center.X, (float)center.Y, (float)center.Z, (float)radius,
                c.X, c.Y, c.Z, 0, (float)normal.X, (float)normal.Y, (float)normal.Z, (float)(Math.PI * radius * radius), 0,0,0,0 }, texture, b.ParentID, rev });
        }
        foreach (var billboard in MyRenderProxy.BillboardsRead) Add(billboard);
        for (int i = 0; i < MyBillboardRenderer.m_billboardsOncePool.GetAllocatedCount(); ++i) Add(MyBillboardRenderer.m_billboardsOncePool.GetAllocatedItem(i));
        MyRenderProxy.ApplyActionOnPersistentBillboards(Add);
        foreach (var emitter in MyGPUEmitters.m_emitters)
        {
            if (emitter.BufferIndex < 0) continue;
            var source = emitter.EmitterData; var d = source.Data;
            if ((d.Flags & GPUEmitterFlags.Dead) != 0) continue;
            Vector3 color = Vector3.Zero;
            color += new Vector3(d.Color0.X, d.Color0.Y, d.Color0.Z) * d.Intensity0 * d.Emissivity0;
            color += new Vector3(d.Color1.X, d.Color1.Y, d.Color1.Z) * d.Intensity1 * d.Emissivity1;
            color += new Vector3(d.Color2.X, d.Color2.Y, d.Color2.Z) * d.Intensity2 * d.Emissivity2;
            color += new Vector3(d.Color3.X, d.Color3.Y, d.Color3.Z) * d.Intensity3 * d.Emissivity3;
            color *= .25f * d.UserColorIntensityMultiplier;
            if (color.LengthSquared() <= 1e-8) continue;
            Vector3D position = source.WorldPosition, direction = d.Direction;
            if (source.ParentID != uint.MaxValue)
            { var parent = MyIDTracker<MyActor>.FindByID(source.ParentID); if (parent == null) continue; position = Vector3D.Transform(position, parent.WorldMatrix); direction = Vector3D.TransformNormal(direction, parent.WorldMatrix); }
            position += direction * d.Velocity * d.UserVelocityMultiplier * d.ParticleLifeSpan * .25;
            double radius = Math.Max(d.EmitterSize.Length(), Math.Max((d.ParticleSize0 + d.ParticleSize1 + d.ParticleSize2 + d.ParticleSize3) * .25 * d.Scale * d.UserRadiusMultiplier, Math.Abs(d.Velocity * d.UserVelocityMultiplier * d.ParticleLifeSpan) * .25));
            if (radius <= .001 || Vector3D.DistanceSquared(position, camera) > 256 * 256) continue;
            var normal = camera - position; normal.Normalize(); position -= camera;
            object texture = null; if (!string.IsNullOrEmpty(source.AtlasTexture)) try { texture = MyManagers.Textures.GetTexture(source.AtlasTexture, MyFileTextureEnum.GUI).Texture?.Srv; } catch { }
            uint id = unchecked((uint)RuntimeHelpers.GetHashCode(emitter) * 16777619u) | 1u;
            result.Add(new object[] { id, new[] { (float)position.X, (float)position.Y, (float)position.Z, (float)radius, color.X, color.Y, color.Z, 0,
                (float)normal.X, (float)normal.Y, (float)normal.Z, (float)(Math.PI * radius * radius), 0,0,0,0 }, texture, source.ParentID, unchecked((uint)color.GetHashCode()) });
        }
        return result.ToArray();
    }
    static float[] BoneArray(Matrix[] bones, int[] remap)
    {
        if (bones == null || bones.Length == 0) return Array.Empty<float>();
        int count = remap == null ? bones.Length : remap.Length; var result = new float[count * 16];
        for (int i = 0; i < count; i++)
        { int source = remap == null ? i : remap[i]; if (source >= 0 && source < bones.Length) Array.Copy(MatrixArray(bones[source]), 0, result, i * 16, 16); }
        return result;
    }
    internal static float[] MatrixArray(Matrix m) => new[] { m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44 };
    static void AddLight(List<float> list, Vector3D position, Vector3 color, float range, Vector3D direction, float cone, int kind, uint id, float falloff, float diffuse, float gloss)
    { list.AddRange(new[] { (float)position.X, (float)position.Y, (float)position.Z, range, color.X, color.Y, color.Z, falloff,
        (float)direction.X, (float)direction.Y, (float)direction.Z, cone, kind, BitConverter.ToSingle(BitConverter.GetBytes(id), 0), diffuse, gloss }); }
    static void Warn(string operation, Exception e) { Status = operation + ": " + e.Message; MyLog.Default.Warning("Anomaly RayScene " + Status); }
}
