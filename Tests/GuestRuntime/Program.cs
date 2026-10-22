using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using ClientPlugin.Backend;
using ClientPlugin.Rendering;

static partial class Program
{
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Main(string[] args)
    {
        if (Array.IndexOf(args, "--stage-binding-contracts") >= 0) { StageBindingContracts(); return; }
        if (Array.IndexOf(args, "--native-stage-contracts") >= 0) { NativeStageContracts(); return; }
        if (Array.IndexOf(args, "--parity-contracts") >= 0) { ParityContracts(); return; }
        if (Array.IndexOf(args, "--frame-contracts") >= 0) { FrameContracts(); return; }
        if (Array.IndexOf(args, "--portable-contracts") >= 0) { PortableContracts(); return; }
        string root = Path.GetFullPath(args.Length > 0 ? args[0] : "../..");
        string assets = Path.Combine(root, "Assets");
        if(Array.IndexOf(args,"--core-only")>=0) {PlanetSnapshot(root);return;}
        Environment.SetEnvironmentVariable("SE_VULKAN_ASSET_DIR", Path.GetFullPath(Path.Combine(root, "../SE-Vulkan/Assets")));
        GuestOwned.Register(assets);
        GuestCompiler.CompilePending(assets);
        foreach (var p in GuestPassList.Snapshot())
        {
            Console.WriteLine(p.Id + " " + p.Status + " " + p.Error);
            Require(p.PixelSpirv != null, "real compiler failed: " + p.Id + " " + GuestCompiler.LastError);
            Require(p.Status == "renderer-pending", "renderer absence must remain retryable");
        }
        var binaries = GuestPassList.Snapshot().Select(p => p.PixelSpirv).ToArray();
        if (Array.IndexOf(args, "--gpu") >= 0) { NativeProbe.Open(Path.GetFullPath(Path.Combine(root, "../SE-Vulkan"))); NativeProbe.TargetContracts(); }
        GuestBridge.Ready = true;
        GuestCompiler.CompilePending(assets);
        int i = 0;
        foreach (var p in GuestPassList.Snapshot())
        {
            Require(p.Pipeline != 0, "pipeline retry failed: " + p.Id);
            Require(ReferenceEquals(p.PixelSpirv, binaries[i++]), "retry unnecessarily recompiled HLSL");
        }
        Require(!GuestCompiler.HasPending, "ready programs should not trigger repeated compilation");
        if(Array.IndexOf(args,"--owned-stage-gpu")>=0){NativeProbe.OwnedStageGpu(assets);NativeProbe.Close();return;}
        if(Array.IndexOf(args,"--portable-gpu")>=0){PortableContracts();NativeProbe.PortableGpuContracts();NativeProbe.CapturedPackGpu(assets);NativeProbe.Close();return;}
        ResourceContracts();
        EnvironmentGpu(assets);
        NativeProbe.CompositionContracts();
        NativeProbe.TemporalContracts();
        PortableContracts();
        NativeProbe.PortableGpuContracts();
        Publication();
        LitMips();
        Occupancy();
        PointShadows(assets);
        ExtendedVolume(assets,Array.IndexOf(args,"--packs")>=0);
        string broken = Path.Combine(Path.GetTempPath(), "anomaly-guest-sources", "broken.hlsl");
        File.WriteAllText(broken, "#error guest-runtime-diagnostic\n");
        GuestPassList.AddProgram("fixture.broken", "AfterLighting", broken, "PublishOnly");
        GuestCompiler.CompilePending(assets);
        var failure = GuestPassList.Snapshot().Single(p => p.Id == "fixture.broken");
        Require(failure.Status == "compile-failed" && failure.Error.Contains("guest-runtime-diagnostic"), "lost compiler diagnostic");
        Require(!GuestCompiler.HasPending, "broken HLSL must not recompile every frame");
        Console.WriteLine("PASS: real owned shaders, delayed renderer, cached retry, terminal HLSL diagnostics");
        if (NativeProbe.Enabled)
        {
            string probe = Path.Combine(Path.GetTempPath(), "anomaly-guest-sources", "frame-probe.hlsl");
            File.WriteAllText(probe, "cbuffer Frame : register(b0) { float4 data[80]; }; float4 __pixel_shader(float4 p : SV_Position, float2 uv : TEXCOORD0) : SV_Target0 { return float4(uv.x + data[12].x, uv.y + data[79].x, 0, 1); }");
            GuestPassList.AddProgram("fixture.frame", "AfterLighting", probe, "PublishOnly");
            GuestCompiler.CompilePending(assets);
            var frame = GuestPassList.Snapshot().Single(p => p.Id == "fixture.frame");
            Require(frame.Pipeline != 0, "stock frame pipeline was rejected");
            NativeProbe.DrawFrameProbe(frame.Pipeline);
            NativeProbe.LiveGuestResources(frame.VertexSpirv,frame.PixelSpirv,frame.PixelEntry,
                GuestPassList.Snapshot().Single(p=>p.Id=="fixture.volume.inject"));
            string mipProbe = Path.Combine(Path.GetTempPath(), "anomaly-guest-sources", "mip-probe.hlsl");
            File.WriteAllText(mipProbe, "Texture2D<float4> Source : register(t0); SamplerState Linear : register(s1); cbuffer Params : register(b7) { float4 Params; }; float4 __pixel_shader(float4 p : SV_Position, float2 uv : TEXCOORD0) : SV_Target0 { return float4(Source.SampleLevel(Linear, uv, Params.x).rgb * 0.25, 1); }");
            GuestPassList.AddProgram("fixture.mips", "AfterLighting", mipProbe, "PublishOnly");
            GuestCompiler.CompilePending(assets);
            NativeProbe.DrawMipProbe(GuestPassList.Snapshot().Single(p => p.Id == "fixture.mips").Pipeline);
            string boxProbe = Path.Combine(Path.GetTempPath(), "anomaly-guest-sources", "box-probe.hlsl");
            File.WriteAllText(boxProbe, File.ReadAllText(Path.Combine(assets, "Shaders/OccupancyStamp.hlsl")).Replace("void __compute_shader(", "void OriginalStamp(") +
                "\n[numthreads(64,1,1)] void __compute_shader(uint3 id : SV_DispatchThreadID) { if(id.x==0) { Occupancy[uint2(0,0)]=BoxCount; Occupancy[uint2(1,0)]=Dim; Occupancy[uint2(2,0)]=Boxes[0].center.x; Occupancy[uint2(3,0)]=Boxes[0].halfExt.x; Occupancy[uint2(4,0)]=Boxes[64].center.x; } }\n");
            GuestPassList.AddProgram("fixture.boxread", "AfterLighting", boxProbe, "PublishOnly");
            GuestPassList.SetFlags("fixture.boxread", 50, 2); GuestCompiler.CompilePending(assets);
            NativeProbe.DrawOccupancyProbe(GuestPassList.Snapshot().Single(p => p.Id == GuestOccupancy.SplatId).Pipeline,
                GuestPassList.Snapshot().Single(p => p.Id == GuestOccupancy.StampId).Pipeline, GuestPassList.Snapshot().Single(p => p.Id == "fixture.boxread").Pipeline);
        }
        if (Array.IndexOf(args, "--packs") >= 0)
        {
            foreach (string repo in new[] { "Prism.SSGI", "VolumetricClouds", "se-aurora", "ScreenSpaceShadows" })
            {
                string pack = Path.GetFullPath(Path.Combine(root, "..", repo, repo == "Prism.SSGI" ? "Assets/AnomalyPack" : "Pack"));
                Require(GuestPassList.RegisterRoot(repo, pack) > 0, "missing pack fixture: " + pack);
            }
            Require(GuestPassList.Snapshot().Single(p => p.Id == "prism.ssgi.trace").Srvs.Single().Slot == 8, "manifest bind was lost");
            string celestial = Path.GetFullPath(Path.Combine(root, "../FinalFrontier/Assets/Celestial"));
            GuestPassList.AddProgram("fixture.celestial", "AfterLighting", Path.Combine(celestial, "Background.hlsl"), "Replace");
            GuestPassList.SetFlags("fixture.celestial", GuestBridgeClient.DepthTest, 0);
            GuestPassList.AddProgram("fixture.medium", "AfterAtmosphere", Path.Combine(celestial, "Atmosphere/Fog.hlsli"), "PublishOnly");
            GuestPassList.SetFlags("fixture.medium", GuestBridgeClient.Compute | GuestBridgeClient.Volume, 1);
            GuestCompiler.CompilePending(assets);
            int failed = 0;
            foreach (var p in GuestPassList.Snapshot().Where(p => !p.Id.StartsWith("anomaly.") && p.Id != "fixture.broken"))
            {
                Console.WriteLine(p.Id + " " + p.Status);
                if (p.PixelSpirv == null || p.Pipeline == 0) failed++;
            }
            Require(failed == 0, "real pack shaders failed: " + failed);
            Console.WriteLine("PASS: actual SSGI, cloud, aurora, contact-shadow and Final Frontier pipelines");
        }
        NativeProbe.Close();
        VolumeRoute(assets);
        if(Array.IndexOf(args,"--core")>=0)PlanetSnapshot(root);
    }

    static void Occupancy()
    {
        var programs = GuestPassList.Snapshot();
        var splat = programs.Single(p => p.Id == GuestOccupancy.SplatId);
        GuestOccupancy.Request(false);
        GuestBridgeClient.Submit(programs);
        Require(ClientPlugin.Buffers.BufferCatalog.Active("occupancy") == null, "unrequested occupancy published");
        GuestOccupancy.Request(true);
        GuestBridge.SupportedPacketFlags = 15;
        GuestBridgeClient.Submit(programs);
        Require(ClientPlugin.Buffers.BufferCatalog.Active("occupancy") == null && splat.Status == "capability-missing", "old native falsely advertised occupancy");
        GuestBridge.SupportedPacketFlags = 63;
        GuestBridgeClient.Submit(programs);
        var packet = GuestBridge.Packets.Single(p => p.Pipeline == splat.Pipeline);
        Require(packet.Width == SceneView.Width && packet.Height == SceneView.Height, "occupancy dispatch used atlas dimensions");
        Require(((ClientPlugin.Buffers.GuestRendererImage)ClientPlugin.Buffers.BufferCatalog.Active("occupancy")).Width == 512, "occupancy catalog shape changed");
        Require(packet.Sampled[0] == ((ClientPlugin.Buffers.GuestRendererImage)ClientPlugin.Buffers.BufferCatalog.Active("linearDepth")).ImageId, "occupancy missed current depth");
        Require(packet.Flags == 50 && BitConverter.ToUInt32(packet.Uniforms, 24) == 64 && BitConverter.ToSingle(packet.Uniforms, 44) == 2, "occupancy clear/custom constants contract lost");
        var depthProgram = programs.Single(p => p.Id == "anomaly.owned.LinearDepth");
        depthProgram.Enabled = false; GuestBridgeClient.Submit(programs);
        Require(ClientPlugin.Buffers.BufferCatalog.Active("occupancy") == null, "occupancy reused stale depth after producer disable");
        depthProgram.Enabled = true;
        double world = 1e12;
        var inputs = new object[] { new double[] { world, 0, 0, 20 }, new uint[] { 10 },
            new double[] { world + 3, 0, 0, .5, .5, .5, world + 4, 0, 0, 1, 1, 1, world, 0, 0, 1, 1, 1, world + 100, 0, 0, 1, 1, 1 },
            new uint[] { 20, 30, 10, 40 }, new double[] { world + 3, 0, 0, world + 1, 0, 0 } };
        var boxes = GuestOccupancy.SelectBoxes(inputs, 1, 20);
        Require(boxes.Length == 8 && boxes[0] == 3, "world-box exclusion/range/double subtraction changed");
        var emptyBounds = new object[] { new double[] { 10,0,0,30, 9.1,0,0,30 }, new uint[] { 10,11 }, Array.Empty<double>(), Array.Empty<uint>(), new double[6] };
        GuestOccupancy.SelectBoxes(emptyBounds, 1, 0);
        Require(GuestOccupancy.SelectedLights.Single() == 10, "incumbent light retention changed");
        ((double[])emptyBounds[0])[4] = 8.8;
        GuestOccupancy.SelectBoxes(emptyBounds, 1, 0);
        Require(GuestOccupancy.SelectedLights.Single() == 11, "materially closer light could not replace incumbent");
        var manyBounds = new double[100 * 6]; var manyIds = new uint[100];
        for (int i = 0; i < 100; i++) { manyIds[i] = (uint)(100+i); manyBounds[i*6+3] = manyBounds[i*6+4] = manyBounds[i*6+5] = 1; }
        var manyLights = new double[8*4]; var manyLightIds = new uint[8];
        for (int i = 0; i < 8; i++) { manyLightIds[i] = (uint)(10+i); manyLights[i*4+3] = 30; }
        Require(GuestOccupancy.SelectBoxes(new object[] { manyLights, manyLightIds, manyBounds, manyIds, new double[6] }, 8, 0).Length == 256*8, "world-box work cap changed");
        GuestBridge.ShadowInputs = inputs; GuestOccupancy.LocalActorId = 20;
        GuestOccupancy.RequestPointShadows(1, 128);
        GuestBridgeClient.Submit(programs);
        var stamp = programs.Single(p => p.Id == GuestOccupancy.StampId);
        Require(GuestOccupancy.LastBoxCount == 1 && GuestBridge.Packets.Any(p => p.Pipeline == stamp.Pipeline), "box stamp not submitted");
        GuestBridge.ShadowInputs = null;
        GuestBridgeClient.Submit(programs);
        Require(ClientPlugin.Buffers.BufferCatalog.Active("occupancy") == null, "missing scene snapshot advertised partial product");
        GuestOccupancy.RequestPointShadows(0, 128); GuestOccupancy.Request(false);
        GuestBridgeClient.Submit(programs);
        Require(ClientPlugin.Buffers.BufferCatalog.Active("occupancy") == null, "clear request retained stale occupancy");
        Console.WriteLine("PASS: occupancy demand/capability/scene gating, dispatch/catalog sizes, depth input and world-box policy");
    }

    static void Publication()
    {
        SceneView.Width = 1920; SceneView.Height = 1080;
        GuestBridgeClient.Submit(GuestPassList.Snapshot());
        var depth = GuestPassList.Snapshot().Single(p => p.Id.EndsWith("LinearDepth"));
        ulong first = ((ClientPlugin.Buffers.GuestRendererImage)ClientPlugin.Buffers.BufferCatalog.Active("linearDepth")).ImageId;
        Require(ClientPlugin.Buffers.BufferCatalog.Active("historyDepth") == null, "first frame published undefined history");
        Require(((ClientPlugin.Buffers.GuestRendererImage)ClientPlugin.Buffers.BufferCatalog.Active("hiZ")).Width == 960, "HiZ size/name diverged");
        Require(GuestBridge.Packets.Single(p => p.Pipeline == depth.Pipeline).Width == 1920, "owned images must use scene resolution");
        GuestBridgeClient.Submit(GuestPassList.Snapshot());
        Require(((ClientPlugin.Buffers.GuestRendererImage)ClientPlugin.Buffers.BufferCatalog.Active("historyDepth")).ImageId == first, "previous depth missing");
        Require(((ClientPlugin.Buffers.GuestRendererImage)ClientPlugin.Buffers.BufferCatalog.Active("linearDepth")).ImageId != first, "current depth catalog did not follow ping-pong");
        SceneView.Width = 1280; SceneView.Height = 720;
        GuestBridgeClient.Submit(GuestPassList.Snapshot());
        Require(ClientPlugin.Buffers.BufferCatalog.Active("historyDepth") == null, "resize retained invalid history");
        var consumer = new GuestProgram { Id = "consumer", Mark = GuestMark.AfterLighting, Pipeline = depth.Pipeline + 10000,
            Priority = -100, Compose = "IsolatedAdd", Srvs = new List<SrvBind> { new SrvBind { Slot = 8, Name = "fixture.texture" } } };
        ClientPlugin.Buffers.BufferCatalog.Set("fixture.texture", new ClientPlugin.Buffers.GuestRendererImage(777, 1, 1, 1));
        var list = new List<GuestProgram> { consumer }; list.AddRange(GuestPassList.Snapshot());
        GuestBridgeClient.Submit(list);
        var packet = GuestBridge.Packets.Single(p => p.Pipeline == consumer.Pipeline);
        Require(GuestBridge.Packets.IndexOf(packet) > GuestBridge.Packets.FindIndex(p => p.Pipeline == depth.Pipeline), "early consumer ran before owned producers");
        Require(packet.Sampled[1] == ((ClientPlugin.Buffers.GuestRendererImage)ClientPlugin.Buffers.BufferCatalog.Active("linearDepth")).ImageId, "consumer missed current linear depth");
        Require(packet.Sampled[4] == GuestBridgeClient.GBuffer0 && packet.Sampled[5] == GuestBridgeClient.GBuffer1 && packet.Sampled[6] == GuestBridgeClient.GBuffer2, "automatic GBuffer inputs missing");
        Require(packet.Sampled[8] == 777, "explicit pack texture binding missing");
        Console.WriteLine("PASS: production submission, scene dimensions, HiZ, depth ping-pong and resize invalidation");
    }

    static void LitMips()
    {
        var programs = GuestPassList.Snapshot();
        var lit = programs.Single(p => p.Id == "anomaly.owned.LitMips");
        GuestOwned.RequestLitMips(0);
        GuestBridgeClient.Submit(programs);
        Require(ClientPlugin.Buffers.BufferCatalog.Active("litMips") == null, "unrequested lit mips allocated");
        GuestOwned.RequestLitMips(5);
        GuestBridge.SupportedPacketFlags = 7;
        GuestBridgeClient.Submit(programs);
        Require(ClientPlugin.Buffers.BufferCatalog.Active("litMips") == null && lit.Status == "capability-missing", "older renderer falsely published ungenerated mips");
        GuestBridge.SupportedPacketFlags = 15;
        GuestBridgeClient.Submit(programs);
        Require(lit.Status == "pipeline" && lit.Error == null, "capability recovery left stale failure");
        Require((lit.ImageFormat >> 16) == 5, "explicit lit mip count missing");
        Require(((ClientPlugin.Buffers.GuestRendererImage)ClientPlugin.Buffers.BufferCatalog.Active("litMips")).ImageId == lit.StorageImage, "lit catalog not published");
        GuestOwned.RequestLitMips(3);
        Require(GuestOwned.LitMipLevels(programs, 1280, 720) == 5, "smaller request reduced sticky maximum");
        GuestOwned.RequestLitMips(0);
        var consumer = new GuestProgram { Id = "mip.consumer", Enabled = true, Pipeline = lit.Pipeline + 20000, Mark = GuestMark.AfterLighting,
            Srvs = new List<SrvBind> { new SrvBind { Name = "litMips", Slot = 8 } } };
        var list = new List<GuestProgram> { consumer }; list.AddRange(programs);
        GuestBridgeClient.Submit(list);
        var packet = GuestBridge.Packets.Single(p => p.Pipeline == consumer.Pipeline);
        Require(packet.Sampled[8] == lit.StorageImage, "automatic mip request did not bind pyramid");
        Require(GuestBridge.Packets.FindIndex(p => p.Pipeline == lit.Pipeline) < GuestBridge.Packets.IndexOf(packet), "lit mips produced after consumer");
        SceneView.Width = 3; SceneView.Height = 1;
        GuestBridgeClient.Submit(list);
        Require((lit.ImageFormat >> 16) == 2 && lit.ImageWidth == 3, "small resize did not clamp mip count");
        consumer.Enabled = false;
        GuestBridgeClient.Submit(list);
        Require(ClientPlugin.Buffers.BufferCatalog.Active("litMips") == null, "disabled consumer left stale pyramid");
        GuestOwned.DebugLitMips = true;
        GuestBridgeClient.Submit(list);
        Require(ClientPlugin.Buffers.BufferCatalog.Active("litMips") != null, "debug view did not request lit mips");
        GuestOwned.DebugLitMips = false;
        SceneView.Width = 1280; SceneView.Height = 720;
        Console.WriteLine("PASS: lit mip explicit/automatic demand, sticky maximum, producer order, resize and disable");
    }
}

namespace ClientPlugin.Rendering
{
    static class GuestBridge
    {
        public static bool Ready;
        public static long TestResourceEpoch;
        public static long ResourceEpoch => TestResourceEpoch;
        public static readonly List<(ulong Id, int Format)> CreatedImages = new();
        public static readonly List<byte[]> ResourceCommands = new();
        public static int SupportedPacketFlags { get; set; } = 8207;
        public static bool MirrorGpu;
        public static bool FailReset;
        public static bool AcceptSceneDepth=true;
        public static double[] SceneDepthMatrices;
        public static bool ConfigureSceneDepthViews(ulong[] images,double[] matrices) {SceneDepthMatrices=(double[])matrices.Clone();return AcceptSceneDepth;}
        public static byte[] FrameOverride;
        public static object[] FrameInputs;
        public static byte[] EnvironmentPacket;
        public static int FrameReads;
        public static object[] CaptureFrameInputs() => FrameInputs;
        public static bool SetFrameEnvironment(byte[] packet) { EnvironmentPacket=(byte[])packet.Clone(); return true; }
        public static object[] Character;
        public static object[] CaptureCharacter(uint actor) => Character;
        static ulong next;
        public static readonly List<(int Mark, ulong Pipeline, int Width, int Height, ulong Storage, byte[] Uniforms, ulong[] Sampled, int Flags)> Packets = new();
        public static Action AfterPipeline;
        public static ulong CreatePipeline(byte[] vs, byte[] ps, string ve, string pe)
        {
            ulong pipeline=!Ready?0:NativeProbe.Enabled?NativeProbe.Pipeline(vs,ps,ve,pe):++next;
            if(pipeline!=0)AfterPipeline?.Invoke();return pipeline;
        }
        public static ulong CreateExtendedPipeline(byte[] vs, byte[] ps, string ve, string pe, int contract) => !Ready ? 0 : NativeProbe.Enabled ? NativeProbe.Pipeline2(vs, ps, ve, pe, contract) : ++next;
        public static void Reset() { if(FailReset) throw new InvalidOperationException("controlled bridge reset failure"); Packets.Clear(); if (MirrorGpu) NativeProbe.ResetQueue(); }
        public static void CreateImage(ulong id, int w, int h, int d, int f) { CreatedImages.Add((id,f)); if (MirrorGpu) NativeProbe.ImageCreate(id,w,h,d,f); }
        public static void Submit(int mark, int compose, int width, int height, ulong pipe, ulong storage, byte[] uniforms, ulong[] sampled, int flags)
        { Packets.Add((mark, pipe, width, height, storage, uniforms, sampled, flags)); if (MirrorGpu) NativeProbe.Queue(mark,compose,width,height,pipe,storage,uniforms,sampled,flags); }
        public static object[] ShadowInputs;
        public static object[] CaptureShadowInputs(uint id) => ShadowInputs;
        public static void UpdateImage(ulong id, byte[] data) { if (MirrorGpu) NativeProbe.ImageUpdate(id,data); }
        public static bool ResourceCommand(byte[] command) { ResourceCommands.Add((byte[])command.Clone()); return !MirrorGpu || NativeProbe.Resource(command); }
        public static void SetNativeSunGlare(bool value) { }
        public static bool TryGetFrame(byte[] frame)
        {
            ++FrameReads;
            if (FrameOverride != null) { Buffer.BlockCopy(FrameOverride,0,frame,0,frame.Length); return Ready; }
            void F(int o, float v) => Buffer.BlockCopy(BitConverter.GetBytes(v), 0, frame, o, 4);
            void U(int o, uint v) => Buffer.BlockCopy(BitConverter.GetBytes(v), 0, frame, o, 4);
            U(8, (uint)SceneView.Width); U(12, (uint)SceneView.Height);
            F(60, 1); F(64, 1); F(264, 1); F(284, 1); F(304, 1); F(324, 1);
            return Ready;
        }
        public static bool InitGBufferAttachment(int slot, int format, byte[] pixel) => Ready;
    }
}
namespace ClientPlugin.Backend
{
    static class RenderHost { public static bool IsDx11=>false; public static bool IsVulkan=>true; }
    static class RendererRoute { public static bool UseDx11Device; public static bool UseGuestImages = true; }
    static class SceneView { public static int Width, Height; }
    static class GuestColor
    {
        public static bool NativeGlare(string id) => true;
        public static class Celestial
        {
            public static readonly Dictionary<string, ulong> Data = new(), Art = new();
        }
    }
}
namespace ClientPlugin.Buffers
{
    sealed class GuestRendererImage
    {
        public readonly ulong ImageId;
        public readonly int Width, Height, Depth;
        readonly Func<bool> available;
        public bool RequiresCurrentFrame {get;}=true;
        public bool IsAvailable=>ImageId!=0 && (available==null || available());
        public GuestRendererImage(ulong id,int width,int height,int depth,Func<bool> available):this(id,width,height,depth){this.available=available;}
        public GuestRendererImage(ulong id, int width, int height, int depth) { ImageId = id; Width = width; Height = height; Depth = depth; }
        public GuestRendererImage(ulong id,int width,int height,int depth,bool current):this(id,width,height,depth){RequiresCurrentFrame=current;}
    }
    sealed class PublishedBuffer { public ulong ImageId { get; set; } public int Width {get;set;} public int Height {get;set;} }
    static class BufferCatalog
    {
        static readonly Dictionary<string, object> Items = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string,string> Owners = new(StringComparer.OrdinalIgnoreCase);
        internal static object SyncRoot => Items;
        internal static bool Reserve(string owner,string name) { if(Owners.TryGetValue(name,out var old) && !string.Equals(old,owner,StringComparison.OrdinalIgnoreCase))return false;Owners[name]=owner;return true; }
        internal static void Forget(string owner,string name) { if(Owners.TryGetValue(name,out var old) && string.Equals(old,owner,StringComparison.OrdinalIgnoreCase)){Owners.Remove(name);Items.Remove(name);} }
        public static void Set(string name, object image) => Items[name] = image;
        public static object Active(string name) => Items.TryGetValue(name, out var image) ? image : null;
    }
}
namespace ClientPlugin.Shaders
{
    static class FrameTemporal { public static uint FrameIndex=>GuestFrame.Open?GuestFrame.U32(16):0; }
}
namespace VRage.Utils
{
    sealed class MyLog
    {
        public static readonly MyLog Default = new MyLog();
        public void WriteLine(string message) => Console.WriteLine(message);
    }
}
namespace VRage.FileSystem
{
    static class MyFileSystem
    {
        public static string ContentPath => Environment.GetEnvironmentVariable("SE_GAME_CONTENT")
            ?? @"T:\SteamLibrary\steamapps\common\SpaceEngineers\Content";
    }
}
namespace ClientPlugin.Rendering.Shaders
{
    public sealed class VulkanShaderArtifact
    {
        public string SpirvPath { get; set; }
        public ShaderMetadata Layout { get; set; }
    }
}
