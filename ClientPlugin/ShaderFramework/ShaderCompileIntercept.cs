// Slice A: Keen compile entry (VRage.Render11).
//
//   MyShaderCompiler.Compile(...)
//   - Source path: Path.Combine(ShadersPath, info.File) where ShadersPath is
//     MyFileSystem.ShadersBasePath + "Shaders" (Content/Shaders).
//   - Defines: per-permutation ShaderMacro[] plus GlobalShaderMacros (PC: empty).
//   - Includes: private List m_includes, default { ShadersPath }.
//     MyIncludeProcessor uses this list for #include <...> (system includes).
//   - Cache key: MyShaderCache.GetShaderHash(preprocessedSource, profile).
//     Unused macros (ANOMALY with no #ifdef) do not change the preprocess text,
//     so the Keen cache still hits. Overlay files that #include Anomaly.hlsli will miss.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using ClientPlugin.Shaders;
using HarmonyLib;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using VRage.Utils;
using VRageRender;

namespace ClientPlugin.ShaderFramework;

public static class ShaderCompileIntercept
{
    public const string MacroName = "ANOMALY";
    public const string MacroValue = "1";
    public const string VelocityMacroName = "ANOMALY_VELOCITY";
    public const string VelocityMacroValue = "1";
    public const string RenderingPassMacro = "RENDERING_PASS";
#if DEBUG
    public const bool BytecodeAuditEnabled = true;
#else
    public const bool BytecodeAuditEnabled = false;
#endif

    public static bool IsLive { get; private set; }
    public static bool GBufferOverlayPresent { get; private set; }
    public static string IncludeDirectory { get; private set; }
    public static string LastError { get; private set; }
    public static int CompileCount => Volatile.Read(ref compileCount);
    public static int FailureCount => Volatile.Read(ref failureCount);
    public static string OverlayOpenStatus =>
        "dispatcher-vs=" + Volatile.Read(ref dispatcherVertexOpenCount) +
        " ps=" + Volatile.Read(ref dispatcherPixelOpenCount) +
        " gbuffer-vs=" + Volatile.Read(ref gbufferVertexOpenCount) +
        " ps=" + Volatile.Read(ref gbufferPixelOpenCount);
    public static string ResidentRefreshStatus
    {
        get
        {
            if (Volatile.Read(ref residentRefreshRunning) != 0)
                return "running";
            if (Volatile.Read(ref residentRefreshPending) != 0)
                return "queued on Keen render thread";
            if (Volatile.Read(ref residentRefreshGeneration) >
                Volatile.Read(ref residentRefreshRequestedGeneration))
                return "awaiting frontend refresh";
            var count = Volatile.Read(ref residentRefreshCount);
            if (!string.IsNullOrEmpty(residentRefreshError))
                return "failed (" + residentRefreshError + ")";
            return count > 0 ? "done#" + count : "not requested";
        }
    }
    public static string ResidentGBufferCompileStatus
    {
        get
        {
            var gbuffer = Volatile.Read(ref residentGBufferPixelCompiles);
            if (Volatile.Read(ref residentRefreshRunning) != 0)
                return "compiling (" + gbuffer + ")";
            return gbuffer.ToString();
        }
    }
    public static string GBufferBytecodeContractStatus =>
        "VS b6=" + Volatile.Read(ref reflectedVertexB6Count) + "/" + Volatile.Read(ref reflectedVertexCount) +
        " out12=" + Volatile.Read(ref reflectedVertexTexcoord12Count) + "/" + Volatile.Read(ref reflectedVertexCount) +
        " PS in12=" + Volatile.Read(ref reflectedPixelTexcoord12Count) + "/" + Volatile.Read(ref reflectedPixelCount) +
        " target3=" + Volatile.Read(ref reflectedPixelTarget3Count) + "/" + Volatile.Read(ref reflectedPixelCount) +
        " target7=" + Volatile.Read(ref reflectedPixelTarget7Count) + "/" + Volatile.Read(ref reflectedPixelCount) +
        " PS b7=" + Volatile.Read(ref reflectedPixelProbeB7Count) + "/" + Volatile.Read(ref reflectedPixelCount) +
        " VS cb=" + Volatile.Read(ref reflectedVelocityCbSize) +
        " probe@" + Volatile.Read(ref reflectedProbeOffset) +
        " PS cb=" + Volatile.Read(ref reflectedPixelProbeCbSize) +
        " probe@" + Volatile.Read(ref reflectedPixelProbeOffset) +
        " errors=" + Volatile.Read(ref reflectionFailureCount);
    public static string GBufferBytecodeFlowStatus =>
        "VS cb6-read=" + Volatile.Read(ref flowVertexB6ReadCount) + "/" + Volatile.Read(ref flowVertexCount) +
        " out12-write=" + Volatile.Read(ref flowVertexTexcoord12WriteCount) + "/" + Volatile.Read(ref flowVertexCount) +
        " PS cb7-read=" + Volatile.Read(ref flowPixelB7ReadCount) + "/" + Volatile.Read(ref flowPixelCount) +
        " t0-write=" + Volatile.Read(ref flowPixelTarget0WriteCount) + "/" + Volatile.Read(ref flowPixelCount) +
        " t3-write=" + Volatile.Read(ref flowPixelTarget3WriteCount) + "/" + Volatile.Read(ref flowPixelCount) +
        " t7-write=" + Volatile.Read(ref flowPixelTarget7WriteCount) + "/" + Volatile.Read(ref flowPixelCount) +
        " errors=" + Volatile.Read(ref flowFailureCount);
    public static string ShaderCreationStatus =>
        "create-key VS=" + Volatile.Read(ref vertexCreateVelocityCount) + "/" + Volatile.Read(ref vertexCreateCount) +
        " PS=" + Volatile.Read(ref pixelCreateVelocityCount) + "/" + Volatile.Read(ref pixelCreateCount) +
        " init VS/PS=" + Volatile.Read(ref vertexVelocityInitCount) + "/" +
        Volatile.Read(ref pixelVelocityInitCount) +
        " object-compile VS/PS=" + Volatile.Read(ref vertexObjectCompileCount) + "/" +
        Volatile.Read(ref pixelObjectCompileCount) +
        " fallback-only VS/PS=" + Volatile.Read(ref vertexFiveArgFallbackCount) + "/" +
        Volatile.Read(ref pixelFiveArgFallbackCount) +
        " evidence-miss VS/PS=" + Volatile.Read(ref vertexEvidenceMissingCount) + "/" +
        Volatile.Read(ref pixelEvidenceMissingCount);
    public static int VertexVelocityInitCount => Volatile.Read(ref vertexVelocityInitCount);
    public static int PixelVelocityInitCount => Volatile.Read(ref pixelVelocityInitCount);
    public static int VertexObjectCompileCount => Volatile.Read(ref vertexObjectCompileCount);
    public static int PixelObjectCompileCount => Volatile.Read(ref pixelObjectCompileCount);
    public static int VertexFiveArgFallbackCount => Volatile.Read(ref vertexFiveArgFallbackCount);
    public static int PixelFiveArgFallbackCount => Volatile.Read(ref pixelFiveArgFallbackCount);
    public static int VertexInitBytecodeCaptureCount => Volatile.Read(ref vertexInitBytecodeCaptureCount);
    public static int PixelInitBytecodeCaptureCount => Volatile.Read(ref pixelInitBytecodeCaptureCount);
    public static int VertexEvidenceMissingCount => Volatile.Read(ref vertexEvidenceMissingCount);
    public static int PixelEvidenceMissingCount => Volatile.Read(ref pixelEvidenceMissingCount);
    public static int VerifiedVertexObjectCount =>
        CountFullFlow(Volatile.Read(ref residentVertexEvidence), VertexFlowRequired);
    public static int VerifiedPixelObjectCount =>
        CountFullFlow(Volatile.Read(ref residentPixelEvidence), PixelFlowRequired);
    public static string IncludeResolutionStatus =>
        "order=" + Volatile.Read(ref includeOrderStatus) +
        " dispatcher VS/PS=" + Volatile.Read(ref lastDispatcherVertexOrigin) + "/" +
        Volatile.Read(ref lastDispatcherPixelOrigin) +
        " gbuffer VS/PS=" + Volatile.Read(ref lastGBufferVertexOrigin) + "/" +
        Volatile.Read(ref lastGBufferPixelOrigin);

    private static int compileCount;
    private static int failureCount;
    private static int dispatcherVertexOpenCount;
    private static int dispatcherPixelOpenCount;
    private static int gbufferVertexOpenCount;
    private static int gbufferPixelOpenCount;
    private static int residentRefreshPending;
    private static int residentRefreshRunning;
    private static int residentRefreshCount;
    private static int residentRefreshGeneration;
    private static int residentRefreshRequestedGeneration;
    private static int residentGBufferPixelCompiles;
    private static int reflectedVertexCount;
    private static int reflectedVertexB6Count;
    private static int reflectedVertexTexcoord12Count;
    private static int reflectedPixelCount;
    private static int reflectedPixelTexcoord12Count;
    private static int reflectedPixelTarget3Count;
    private static int reflectedPixelTarget7Count;
    private static int reflectedPixelProbeB7Count;
    private static int reflectedVelocityCbSize = -1;
    private static int reflectedProbeOffset = -1;
    private static int reflectedPixelProbeCbSize = -1;
    private static int reflectedPixelProbeOffset = -1;
    private static int reflectionFailureCount;
    private static int flowVertexCount;
    private static int flowVertexB6ReadCount;
    private static int flowVertexTexcoord12WriteCount;
    private static int flowPixelCount;
    private static int flowPixelB7ReadCount;
    private static int flowPixelTarget0WriteCount;
    private static int flowPixelTarget3WriteCount;
    private static int flowPixelTarget7WriteCount;
    private static int flowFailureCount;
    private static int vertexCreateCount;
    private static int vertexCreateVelocityCount;
    private static int pixelCreateCount;
    private static int pixelCreateVelocityCount;
    private static int vertexVelocityInitCount;
    private static int pixelVelocityInitCount;
    private static int vertexObjectCompileCount;
    private static int pixelObjectCompileCount;
    private static int vertexObjectBytecodeCaptureCount;
    private static int pixelObjectBytecodeCaptureCount;
    private static int vertexFiveArgFallbackCount;
    private static int pixelFiveArgFallbackCount;
    private static int vertexInitBytecodeCaptureCount;
    private static int pixelInitBytecodeCaptureCount;
    private static int vertexOutputBytecodeCaptureCount;
    private static int vertexInitTranspilerSites;
    private static int pixelInitTranspilerSites;
    private static int vertexCacheHitCount;
    private static int vertexCacheMissCount;
    private static int pixelCacheHitCount;
    private static int pixelCacheMissCount;
    private static int vertexEvidenceMissingCount;
    private static int pixelEvidenceMissingCount;
    private static int deepCompileHookAvailable;
    private static int deepVertexCompileCount;
    private static int deepPixelCompileCount;
    private static int deepVertexCacheHitCount;
    private static int deepVertexFreshCount;
    private static int deepPixelCacheHitCount;
    private static int deepPixelFreshCount;
    private static int deepVertexFullFlowCount;
    private static int deepPixelFullFlowCount;
    private static int deepVertexInvalidatedCount;
    private static int deepPixelInvalidatedCount;
    private static int deepVertexOrphanCount;
    private static int deepPixelOrphanCount;
    private static int objectRepairAttempts;
    private static int objectRepairSuccesses;
    private static int objectRepairFailures;
    public static string ObjectRepairStatus => "attempt=" + Volatile.Read(ref objectRepairAttempts) +
        " replaced=" + Volatile.Read(ref objectRepairSuccesses) +
        " failed=" + Volatile.Read(ref objectRepairFailures);
    public static bool ObjectRepairHealthy => Volatile.Read(ref objectRepairFailures) == 0;
    private static string includeOrderStatus = "not inspected";
    private static string lastDispatcherVertexOrigin = "unseen";
    private static string lastDispatcherPixelOrigin = "unseen";
    private static string lastGBufferVertexOrigin = "unseen";
    private static string lastGBufferPixelOrigin = "unseen";
    private const int VertexFlowRequired = 0x3;
    private const int PixelFlowRequired = 0xF;
    private const int DeepMissingRouteLimit = 6;

    internal readonly struct ShaderBytecodeEvidence
    {
        public readonly ulong Hash;
        public readonly int FlowMask;

        public ShaderBytecodeEvidence(ulong hash, int flowMask)
        {
            Hash = hash;
            FlowMask = flowMask;
        }
    }

    readonly struct ShaderObjectEvidence
    {
        public readonly IntPtr Pointer;
        public readonly ulong Hash;
        public readonly int FlowMask;

        public ShaderObjectEvidence(IntPtr pointer, ShaderBytecodeEvidence evidence)
        {
            Pointer = pointer;
            Hash = evidence.Hash;
            FlowMask = evidence.FlowMask;
        }
    }

    public sealed class ShaderObjectCreationState
    {
        internal readonly bool IsGBuffer;
        internal readonly bool IsDepth;
        internal readonly bool HasVelocity;
        internal readonly MyShaderProfile Profile;
        internal readonly string File;
        internal readonly string Permutation;
        internal ShaderBytecodeEvidence Evidence;
        internal bool HasEvidence;
        internal bool ObjectCompileObserved;
        internal bool ObjectBytecodeObserved;
        internal bool FiveArgumentCompileObserved;
        internal bool DeepCompileObserved;
        internal bool InitBytecodeObserved;
        internal bool VertexOutputBytecodeObserved;
        internal bool CacheObserved;

        internal bool IsVelocityGBuffer => IsGBuffer && !IsDepth && HasVelocity;

        internal ShaderObjectCreationState(bool isGBuffer, bool isDepth, bool hasVelocity,
            MyShaderProfile profile, string file, string permutation)
        {
            IsGBuffer = isGBuffer;
            IsDepth = isDepth;
            HasVelocity = hasVelocity;
            Profile = profile;
            File = file;
            Permutation = permutation;
        }
    }

    public readonly struct DeepCompileState
    {
        internal readonly ShaderObjectCreationState Owner;
        internal readonly string File;
        internal readonly string Permutation;
        internal readonly MyShaderProfile Profile;
        internal readonly bool IsVelocityGBuffer;
        internal readonly bool InvalidateCache;

        internal DeepCompileState(ShaderObjectCreationState owner, string file, string permutation,
            MyShaderProfile profile, bool isVelocityGBuffer, bool invalidateCache)
        {
            Owner = owner;
            File = file;
            Permutation = permutation;
            Profile = profile;
            IsVelocityGBuffer = isVelocityGBuffer;
            InvalidateCache = invalidateCache;
        }
    }

    [ThreadStatic] private static Stack<ShaderObjectCreationState> activePixelObjects;
    [ThreadStatic] private static Stack<ShaderObjectCreationState> activeVertexObjects;
    private static IntPtr[] residentPixelObjects = Array.Empty<IntPtr>();
    private static IntPtr[] residentGBufferPixelObjects = Array.Empty<IntPtr>();
    private static IntPtr[] residentVelocityPixelObjects = Array.Empty<IntPtr>();
    private static IntPtr[] residentDepthPixelObjects = Array.Empty<IntPtr>();
    private static IntPtr[] retiredPixelObjects = Array.Empty<IntPtr>();
    private static IntPtr[] residentVertexObjects = Array.Empty<IntPtr>();
    private static IntPtr[] residentGBufferVertexObjects = Array.Empty<IntPtr>();
    private static IntPtr[] residentVelocityVertexObjects = Array.Empty<IntPtr>();
    private static IntPtr[] residentDepthVertexObjects = Array.Empty<IntPtr>();
    private static IntPtr[] retiredVertexObjects = Array.Empty<IntPtr>();
    private static ShaderObjectEvidence[] residentPixelEvidence = Array.Empty<ShaderObjectEvidence>();
    private static ShaderObjectEvidence[] residentVertexEvidence = Array.Empty<ShaderObjectEvidence>();
    private static string residentRefreshError;
    private static string lastArmedRefreshKey;
    private static string lastCompletedRefreshKey;
    private static string assetFolder;
    private static string includeDirectoryOverride;
    private static readonly List<string> PackIncludes = new();
    private static readonly object Gate = new();
    private static readonly object ShaderObjectGate = new();
    private static readonly List<string> DeepVertexMissingRoutes = new();
    private static readonly List<string> DeepPixelMissingRoutes = new();

    public static int ResidentPixelObjectCount =>
        Volatile.Read(ref residentPixelObjects).Length;
    public static int ResidentGBufferPixelObjectCount =>
        Volatile.Read(ref residentGBufferPixelObjects).Length;
    public static int ResidentVelocityPixelObjectCount =>
        Volatile.Read(ref residentVelocityPixelObjects).Length;
    public static int ResidentDepthPixelObjectCount =>
        Volatile.Read(ref residentDepthPixelObjects).Length;
    public static int RetiredPixelObjectCount =>
        Volatile.Read(ref retiredPixelObjects).Length;
    public static int ResidentVertexObjectCount =>
        Volatile.Read(ref residentVertexObjects).Length;
    public static int ResidentGBufferVertexObjectCount =>
        Volatile.Read(ref residentGBufferVertexObjects).Length;
    public static int ResidentVelocityVertexObjectCount =>
        Volatile.Read(ref residentVelocityVertexObjects).Length;
    public static int ResidentDepthVertexObjectCount =>
        Volatile.Read(ref residentDepthVertexObjects).Length;
    public static int RetiredVertexObjectCount =>
        Volatile.Read(ref retiredVertexObjects).Length;
    public static string ExactObjectEvidenceStatus
    {
        get
        {
            var vs = Volatile.Read(ref residentVertexEvidence);
            var ps = Volatile.Read(ref residentPixelEvidence);
            return "VS flow=" + CountFullFlow(vs, VertexFlowRequired) + "/" + vs.Length +
                   " objects(all/g/w/depth)=" + ResidentVertexObjectCount + "/" +
                   ResidentGBufferVertexObjectCount + "/" + ResidentVelocityVertexObjectCount + "/" +
                   ResidentDepthVertexObjectCount +
                   " PS flow=" + CountFullFlow(ps, PixelFlowRequired) + "/" + ps.Length +
                   " objects(all/g/w/depth)=" + ResidentPixelObjectCount + "/" +
                   ResidentGBufferPixelObjectCount + "/" + ResidentVelocityPixelObjectCount + "/" +
                   ResidentDepthPixelObjectCount;
        }
    }
    public static bool BytecodeFlowContractValid =>
        Volatile.Read(ref flowFailureCount) == 0 &&
        Volatile.Read(ref flowVertexCount) > 0 &&
        Volatile.Read(ref flowVertexB6ReadCount) == Volatile.Read(ref flowVertexCount) &&
        Volatile.Read(ref flowVertexTexcoord12WriteCount) == Volatile.Read(ref flowVertexCount) &&
        Volatile.Read(ref flowPixelCount) > 0 &&
        Volatile.Read(ref flowPixelB7ReadCount) == Volatile.Read(ref flowPixelCount) &&
        Volatile.Read(ref flowPixelTarget0WriteCount) == Volatile.Read(ref flowPixelCount) &&
        Volatile.Read(ref flowPixelTarget3WriteCount) == Volatile.Read(ref flowPixelCount) &&
        Volatile.Read(ref flowPixelTarget7WriteCount) == Volatile.Read(ref flowPixelCount);
    public static bool ExactObjectProofValid =>
        ResidentVelocityVertexObjectCount > 0 && ResidentVelocityPixelObjectCount > 0 &&
        VerifiedVertexObjectCount == ResidentVelocityVertexObjectCount &&
        VerifiedPixelObjectCount == ResidentVelocityPixelObjectCount &&
        VertexEvidenceMissingCount == 0 && PixelEvidenceMissingCount == 0;
    public static bool DeepCompileProofValid =>
        Volatile.Read(ref deepCompileHookAvailable) != 0 &&
        Volatile.Read(ref deepVertexCompileCount) > 0 &&
        Volatile.Read(ref deepPixelCompileCount) > 0 &&
        Volatile.Read(ref deepVertexFullFlowCount) == Volatile.Read(ref deepVertexCompileCount) &&
        Volatile.Read(ref deepPixelFullFlowCount) == Volatile.Read(ref deepPixelCompileCount);
    public static string VertexBytecodeProofStatus =>
        "flow=" + Volatile.Read(ref flowVertexCount) +
        " b6=" + Volatile.Read(ref flowVertexB6ReadCount) +
        " o12=" + Volatile.Read(ref flowVertexTexcoord12WriteCount) +
        " cb=" + Volatile.Read(ref reflectedVelocityCbSize) +
        " probe@" + Volatile.Read(ref reflectedProbeOffset);
    public static string PixelBytecodeProofStatus =>
        "flow=" + Volatile.Read(ref flowPixelCount) +
        " b7=" + Volatile.Read(ref flowPixelB7ReadCount) +
        " t0/3/7=" + Volatile.Read(ref flowPixelTarget0WriteCount) + "/" +
        Volatile.Read(ref flowPixelTarget3WriteCount) + "/" +
        Volatile.Read(ref flowPixelTarget7WriteCount) +
        " cb=" + Volatile.Read(ref reflectedPixelProbeCbSize);
    public static string ShaderObjectRouteStatus =>
        "init V/P=" + VertexVelocityInitCount + "/" + PixelVelocityInitCount +
        " return=" + VertexInitBytecodeCaptureCount + "/" + PixelInitBytecodeCaptureCount +
        " sites=" + Volatile.Read(ref vertexInitTranspilerSites) + "/" +
        Volatile.Read(ref pixelInitTranspilerSites) +
        " wrapper pre/result=" + VertexObjectCompileCount + "/" + PixelObjectCompileCount + ":" +
        Volatile.Read(ref vertexObjectBytecodeCaptureCount) + "/" +
        Volatile.Read(ref pixelObjectBytecodeCaptureCount) +
        " v-out=" + Volatile.Read(ref vertexOutputBytecodeCaptureCount) +
        " cache H/M=" + Volatile.Read(ref vertexCacheHitCount) + "/" +
        Volatile.Read(ref vertexCacheMissCount) + ":" + Volatile.Read(ref pixelCacheHitCount) + "/" +
        Volatile.Read(ref pixelCacheMissCount) +
        " miss=" + VertexEvidenceMissingCount + "/" + PixelEvidenceMissingCount;
    public static string DeepCompileStatus
    {
        get
        {
            string vertexMissing;
            string pixelMissing;
            lock (ShaderObjectGate)
            {
                vertexMissing = FormatMissingRoutes(DeepVertexMissingRoutes);
                pixelMissing = FormatMissingRoutes(DeepPixelMissingRoutes);
            }

            return "hook=" + (Volatile.Read(ref deepCompileHookAvailable) != 0 ? "yes" : "no") +
                   " V/P=" + Volatile.Read(ref deepVertexCompileCount) + "/" +
                   Volatile.Read(ref deepPixelCompileCount) +
                   " full=" + Volatile.Read(ref deepVertexFullFlowCount) + "/" +
                   Volatile.Read(ref deepPixelFullFlowCount) +
                   " cache=" + Volatile.Read(ref deepVertexCacheHitCount) + "/" +
                   Volatile.Read(ref deepPixelCacheHitCount) +
                   " fresh=" + Volatile.Read(ref deepVertexFreshCount) + "/" +
                   Volatile.Read(ref deepPixelFreshCount) +
                   " inv=" + Volatile.Read(ref deepVertexInvalidatedCount) + "/" +
                   Volatile.Read(ref deepPixelInvalidatedCount) +
                   " orphan=" + Volatile.Read(ref deepVertexOrphanCount) + "/" +
                   Volatile.Read(ref deepPixelOrphanCount) +
                   " missing routes=" + (vertexMissing == "-" ? "0" : "see log") + "/" +
                   (pixelMissing == "-" ? "0" : "see log");
        }
    }
    public static string ExactObjectProofStatus =>
        "verified V/P=" + VerifiedVertexObjectCount + "/" + ResidentVelocityVertexObjectCount +
        " " + VerifiedPixelObjectCount + "/" + ResidentVelocityPixelObjectCount;

    public static void SetAssetFolder(string folder)
    {
        assetFolder = folder;
    }

    public static void SetIncludeDirectory(string directory)
    {
        includeDirectoryOverride = directory;
    }

    public static void SetPackIncludeDirectories(IReadOnlyList<string> directories)
    {
        lock (Gate)
        {
            PackIncludes.Clear();
            if (directories != null)
            {
                foreach (var dir in directories)
                {
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        PackIncludes.Add(Path.GetFullPath(dir));
                }
            }

            if (IsLive)
            {
                try
                {
                    EnsureIncludePath();
                }
                catch (Exception e)
                {
                    DebugLog.Write("ShaderCompileIntercept pack includes: " + e.Message);
                }
            }
        }
    }

    public static void TryRemapSource(ref string filepath)
    {
        ShaderPackRegistry.TryRemapCompilePath(ref filepath);
    }

    public static void Activate()
    {
        lock (Gate)
        {
            LastError = null;
            IncludeDirectory = ResolveIncludeDirectory();
            if (string.IsNullOrEmpty(IncludeDirectory))
            {
                IsLive = false;
                LastError = "shader include directory not found";
                DebugLog.Write("ShaderCompileIntercept: " + LastError);
                MyLog.Default.WriteLine("Anomaly: " + LastError);
                return;
            }

            try
            {
                EnsureGlobalMacro();
                EnsureIncludePath();
                KeenShaderGuard.Prepare();
                GBufferOverlayPresent = KeenShaderGuard.GBufferPatchesReady;
                if (!GBufferOverlayPresent && !string.IsNullOrEmpty(KeenShaderGuard.LastError))
                    LastError = KeenShaderGuard.LastError;
                IsLive = true;
                MyLog.Default.WriteLine("Anomaly compile intercept live. Include: " + IncludeDirectory
                    + " GBuffer overlay=" + GBufferOverlayPresent
                    + (GBufferOverlayPresent ? "" : " (" + KeenShaderGuard.StatusLine + ")"));
                DebugLog.Write("ShaderCompileIntercept live include=" + IncludeDirectory
                    + " keen=" + KeenShaderGuard.StatusLine);
            }
            catch (Exception e)
            {
                IsLive = false;
                LastError = e.GetType().Name + ": " + e.Message;
                MyLog.Default.WriteLine("Anomaly compile intercept failed: " + LastError);
                DebugLog.Write("ShaderCompileIntercept failed: " + e);
            }
        }
    }

    /// <summary>
    /// Marks the current shader inputs as needing one renderer-owned refresh.
    /// The request is submitted in the frontend, before world loading can race
    /// ReloadEffects against Stage 2 model and GPU-resource creation.
    /// </summary>
    public static void ArmResidentShaderRefresh()
    {
        if (!IsLive)
            return;

        var key = CurrentRefreshInputKey();
        if (string.Equals(key, lastCompletedRefreshKey, StringComparison.Ordinal) ||
            string.Equals(key, lastArmedRefreshKey, StringComparison.Ordinal))
            return;

        lastArmedRefreshKey = key;
        Interlocked.Increment(ref residentRefreshGeneration);
        residentRefreshError = null;
        DebugLog.Write("ShaderCompileIntercept armed resident refresh generation " +
            Volatile.Read(ref residentRefreshGeneration) + " key=" + key);
    }

    static string CurrentRefreshInputKey()
    {
        return (ShaderPackRegistry.Fingerprint ?? "0") + "|" +
               (KeenShaderGuard.GBufferPatchesReady ? "1" : "0") + "|" +
               (KeenShaderGuard.StatusLine ?? "");
    }

    /// <summary>
    /// Asks Keen to rebuild both shader owners through its native ReloadEffects
    /// message. Each armed generation is submitted at most once. The renderer
    /// consumes that message before scene drawing, so no deferred command list
    /// can retain a shader object that was just disposed.
    /// </summary>
    public static void RequestResidentShaderRefresh()
    {
        if (!IsLive)
            return;
        var generation = Volatile.Read(ref residentRefreshGeneration);
        if (generation == 0 ||
            generation <= Volatile.Read(ref residentRefreshRequestedGeneration))
            return;
        if (Volatile.Read(ref residentRefreshRunning) != 0 ||
            Interlocked.CompareExchange(ref residentRefreshPending, 1, 0) != 0)
            return;

        // Re-read after owning the pending slot. If shader inputs changed while
        // another caller was checking, this request covers the newest version.
        generation = Volatile.Read(ref residentRefreshGeneration);
        Volatile.Write(ref residentRefreshRequestedGeneration, generation);

        residentRefreshError = null;
        try
        {
            MyRenderProxy.ReloadEffects();
            DebugLog.Write("ShaderCompileIntercept queued Keen ReloadEffects generation " + generation);
        }
        catch (Exception e)
        {
            Interlocked.Exchange(ref residentRefreshPending, 0);
            residentRefreshError = e.GetType().Name + ": " + e.Message;
            MyLog.Default.WriteLine("Anomaly resident shader refresh enqueue failed: " + residentRefreshError);
            DebugLog.Write("ShaderCompileIntercept ReloadEffects enqueue failed: " + e);
        }
    }

    /// <summary>Called around Keen's ReloadEffects render message.</summary>
    public static void BeginResidentShaderRefresh()
    {
        if (!IsLive || Interlocked.Exchange(ref residentRefreshRunning, 1) != 0)
            return;

        Interlocked.Exchange(ref residentRefreshPending, 0);
        Interlocked.Exchange(ref residentGBufferPixelCompiles, 0);
        Interlocked.Exchange(ref reflectedVertexCount, 0);
        Interlocked.Exchange(ref reflectedVertexB6Count, 0);
        Interlocked.Exchange(ref reflectedVertexTexcoord12Count, 0);
        Interlocked.Exchange(ref reflectedPixelCount, 0);
        Interlocked.Exchange(ref reflectedPixelTexcoord12Count, 0);
        Interlocked.Exchange(ref reflectedPixelTarget3Count, 0);
        Interlocked.Exchange(ref reflectedPixelTarget7Count, 0);
        Interlocked.Exchange(ref reflectedPixelProbeB7Count, 0);
        Interlocked.Exchange(ref reflectedVelocityCbSize, -1);
        Interlocked.Exchange(ref reflectedProbeOffset, -1);
        Interlocked.Exchange(ref reflectedPixelProbeCbSize, -1);
        Interlocked.Exchange(ref reflectedPixelProbeOffset, -1);
        Interlocked.Exchange(ref reflectionFailureCount, 0);
        Interlocked.Exchange(ref flowVertexCount, 0);
        Interlocked.Exchange(ref flowVertexB6ReadCount, 0);
        Interlocked.Exchange(ref flowVertexTexcoord12WriteCount, 0);
        Interlocked.Exchange(ref flowPixelCount, 0);
        Interlocked.Exchange(ref flowPixelB7ReadCount, 0);
        Interlocked.Exchange(ref flowPixelTarget0WriteCount, 0);
        Interlocked.Exchange(ref flowPixelTarget3WriteCount, 0);
        Interlocked.Exchange(ref flowPixelTarget7WriteCount, 0);
        Interlocked.Exchange(ref flowFailureCount, 0);
        Interlocked.Exchange(ref vertexCreateCount, 0);
        Interlocked.Exchange(ref vertexCreateVelocityCount, 0);
        Interlocked.Exchange(ref pixelCreateCount, 0);
        Interlocked.Exchange(ref pixelCreateVelocityCount, 0);
        Interlocked.Exchange(ref vertexVelocityInitCount, 0);
        Interlocked.Exchange(ref pixelVelocityInitCount, 0);
        Interlocked.Exchange(ref vertexObjectCompileCount, 0);
        Interlocked.Exchange(ref pixelObjectCompileCount, 0);
        Interlocked.Exchange(ref vertexFiveArgFallbackCount, 0);
        Interlocked.Exchange(ref pixelFiveArgFallbackCount, 0);
        Interlocked.Exchange(ref vertexEvidenceMissingCount, 0);
        Interlocked.Exchange(ref pixelEvidenceMissingCount, 0);
        Interlocked.Exchange(ref deepVertexCompileCount, 0);
        Interlocked.Exchange(ref deepPixelCompileCount, 0);
        Interlocked.Exchange(ref deepVertexCacheHitCount, 0);
        Interlocked.Exchange(ref deepVertexFreshCount, 0);
        Interlocked.Exchange(ref deepPixelCacheHitCount, 0);
        Interlocked.Exchange(ref deepPixelFreshCount, 0);
        Interlocked.Exchange(ref deepVertexFullFlowCount, 0);
        Interlocked.Exchange(ref deepPixelFullFlowCount, 0);
        Interlocked.Exchange(ref deepVertexInvalidatedCount, 0);
        Interlocked.Exchange(ref deepPixelInvalidatedCount, 0);
        Interlocked.Exchange(ref deepVertexOrphanCount, 0);
        Interlocked.Exchange(ref deepPixelOrphanCount, 0);
        Interlocked.Exchange(ref objectRepairAttempts, 0);
        Interlocked.Exchange(ref objectRepairSuccesses, 0);
        Interlocked.Exchange(ref objectRepairFailures, 0);
        lock (ShaderObjectGate)
        {
            DeepVertexMissingRoutes.Clear();
            DeepPixelMissingRoutes.Clear();
        }
        Volatile.Write(ref lastDispatcherVertexOrigin, "unseen");
        Volatile.Write(ref lastDispatcherPixelOrigin, "unseen");
        Volatile.Write(ref lastGBufferVertexOrigin, "unseen");
        Volatile.Write(ref lastGBufferPixelOrigin, "unseen");
        residentRefreshError = null;
    }

    /// <summary>Completes tracking for Keen's renderer-owned refresh.</summary>
    public static void CompleteResidentShaderRefresh(Exception error)
    {
        if (Interlocked.Exchange(ref residentRefreshRunning, 0) == 0)
            return;

        if (error == null)
        {
            residentRefreshError = null;
            lastCompletedRefreshKey = lastArmedRefreshKey ?? CurrentRefreshInputKey();
            Interlocked.Increment(ref residentRefreshCount);
            var message = "Anomaly resident shaders refreshed by Keen ReloadEffects. Opens: "
                + OverlayOpenStatus + " GBuffer PS=" + ResidentGBufferCompileStatus;
            MyLog.Default.WriteLine(message);
            DebugLog.Write(message);
        }
        else
        {
            residentRefreshError = error.GetType().Name + ": " + error.Message;
            MyLog.Default.WriteLine("Anomaly resident shader refresh failed: " + residentRefreshError);
            DebugLog.Write("ShaderCompileIntercept resident refresh failed: " + error);
        }
    }

    public static void NoteCompile(string filepath, ShaderMacro[] macros, MyShaderProfile profile, string sourceDescriptor, byte[] bytecode)
    {
        Interlocked.Increment(ref compileCount);
        var isGBuffer = IsVelocityGBufferCompile(filepath, macros);
        if (Volatile.Read(ref residentRefreshRunning) != 0 &&
            profile == MyShaderProfile.ps_5_0 && isGBuffer)
            Interlocked.Increment(ref residentGBufferPixelCompiles);
        if (ShaderRecompileCacheFill.IsRunning)
            return;

        if (bytecode != null && bytecode.Length != 0)
        {
            if (ShaderPackRegistry.StageProbePending && !ShaderPackRegistry.StageProbeInProgress)
                ShaderPackRegistry.ValidateStages();
            return;
        }

        if (ShaderPackRegistry.StageProbeInProgress)
            return;

        Interlocked.Increment(ref failureCount);
        var desc = sourceDescriptor ?? filepath ?? "(unknown)";
        var defines = MacrosToString(macros);
        var owner = ShaderPackRegistry.DescribeCompileOwners(filepath);
        var live = ShaderPackRegistry.DescribeLivePackIds();
        var msg = "Anomaly: shader compile failed " + desc + " profile=" + profile + " defines=[" + defines + "]"
            + " pack=" + owner + " live=" + live + " packs=" + ShaderPackRegistry.Fingerprint;
        MyLog.Default.WriteLine(msg);
        DebugLog.Write(msg);

        ShaderPackRegistry.OnCompileFailed(filepath, macros);
    }

    public static void NoteDeepCompileHook(bool available)
    {
        Volatile.Write(ref deepCompileHookAvailable, available ? 1 : 0);
    }

    /// <summary>
    /// Last common compiler boundary before preprocessing, cache lookup, or a
    /// fresh FXC invocation. Every object path converges here, including routes
    /// whose small manager/compiler wrappers have been inlined by the CLR.
    /// </summary>
    public static DeepCompileState PrepareDeepCompile(string filepath, ref ShaderMacro[] macros,
        MyShaderProfile profile, bool invalidateCache)
    {
        EnsureGBufferMacros(filepath, ref macros);
        var isVelocity = (profile == MyShaderProfile.vs_5_0 || profile == MyShaderProfile.ps_5_0) &&
                         IsVelocityGBufferCompile(filepath, macros);
        var owner = isVelocity ? PeekActiveShaderObject(profile) : null;
        if (owner != null)
            owner.DeepCompileObserved = true;
        return new DeepCompileState(owner, CompactShaderPath(filepath), MacroFingerprint(macros),
            profile, isVelocity, invalidateCache);
    }

    /// <summary>
    /// Records the actual cache/fresh result and associates the exact common-
    /// endpoint bytecode with the active native-object Init call. Missing flow
    /// identities are bounded on-screen and fully emitted to the debug log.
    /// </summary>
    public static void NoteDeepCompile(bool wasCached, string compilerHash, byte[] bytecode,
        DeepCompileState state)
    {
        if (!state.IsVelocityGBuffer)
            return;

        var vertex = state.Profile == MyShaderProfile.vs_5_0;
        var owner = state.Owner;
        if (owner == null || !owner.IsVelocityGBuffer)
        {
            if (vertex)
                Interlocked.Increment(ref deepVertexOrphanCount);
            else
                Interlocked.Increment(ref deepPixelOrphanCount);
            // A compile outside Init is not itself a bytecode failure. Reflect
            // it independently; never assign it to an unrelated native object.
            owner = new ShaderObjectCreationState(true, false, true, state.Profile,
                state.File, state.Permutation);
            var count = vertex ? Volatile.Read(ref deepVertexOrphanCount) : Volatile.Read(ref deepPixelOrphanCount);
            if (count <= DeepMissingRouteLimit)
                DebugLog.Write("unowned compile " + state.Profile + " " + state.File +
                    "#" + state.Permutation + " thread=" + Thread.CurrentThread.ManagedThreadId);
        }

        if (vertex)
        {
            Interlocked.Increment(ref deepVertexCompileCount);
            Interlocked.Increment(ref wasCached ? ref deepVertexCacheHitCount : ref deepVertexFreshCount);
            if (state.InvalidateCache)
                Interlocked.Increment(ref deepVertexInvalidatedCount);
        }
        else
        {
            Interlocked.Increment(ref deepPixelCompileCount);
            Interlocked.Increment(ref wasCached ? ref deepPixelCacheHitCount : ref deepPixelFreshCount);
            if (state.InvalidateCache)
                Interlocked.Increment(ref deepPixelInvalidatedCount);
        }

        if (!BytecodeAuditEnabled)
            return;

        var compiledEvidence = ReflectGBufferBytecode(state.Profile, bytecode);
        if (!owner.HasEvidence)
        {
            owner.Evidence = compiledEvidence;
            owner.HasEvidence = compiledEvidence.Hash != 0;
        }
        var required = vertex ? VertexFlowRequired : PixelFlowRequired;
        var full = compiledEvidence.Hash != 0 && (compiledEvidence.FlowMask & required) == required;
        if (full)
        {
            if (vertex)
                Interlocked.Increment(ref deepVertexFullFlowCount);
            else
                Interlocked.Increment(ref deepPixelFullFlowCount);
            return;
        }

        var route = state.File + "#" + state.Permutation;
        AddMissingDeepRoute(vertex, route);
        var cache = wasCached ? "cache" : "fresh";
        DebugLog.Write("Anomaly velocity deep compile missing flow profile=" + state.Profile +
            " route=" + route + " sourceHash=" + (compilerHash ?? "?") +
            " resultHash=0x" + compiledEvidence.Hash.ToString("X16") +
            " mask=0x" + compiledEvidence.FlowMask.ToString("X") + " " + cache +
            " invalidate=" + state.InvalidateCache);
    }

    /// <summary>
    /// Captures bytecode at Keen's non-inline compiler boundary and attaches it
    /// to the exact active Pixel/Vertex Init call. Harmony can miss the tiny
    /// Compile(ref info) wrapper after it has been inlined, but this boundary is
    /// still crossed for cache hits and fresh compiles alike.
    /// </summary>
    public static void NoteCompiledShaderBytecode(ShaderMacro[] macros, MyShaderProfile profile, byte[] bytecode)
    {
        if (bytecode == null || bytecode.Length == 0 ||
            (profile != MyShaderProfile.vs_5_0 && profile != MyShaderProfile.ps_5_0) ||
            !IsGBufferPermutation(macros) || !ContainsNamed(macros, VelocityMacroName))
            return;

        var state = PeekActiveShaderObject(profile);
        if (state == null || !state.IsVelocityGBuffer)
            return;

        state.FiveArgumentCompileObserved = true;
        AttachShaderObjectEvidence(state, profile, bytecode);
    }

    /// <summary>
    /// Associates the exact bytecode returned by Compile(ref info) with the
    /// native shader object that Keen constructs immediately afterward. This
    /// overload is above MyShaderCache, so cache hits cannot bypass evidence.
    /// </summary>
    public static void NoteShaderObjectBytecode(ref MyShaderCompilationInfo info, byte[] bytecode)
    {
        if (bytecode == null || bytecode.Length == 0)
            return;

        var profile = info.Profile;
        if (profile != MyShaderProfile.vs_5_0 && profile != MyShaderProfile.ps_5_0)
            return;

        var state = PeekActiveShaderObject(profile);
        if (state == null || !state.IsVelocityGBuffer)
            return;

        state.ObjectCompileObserved = true;
        if (!state.ObjectBytecodeObserved)
        {
            state.ObjectBytecodeObserved = true;
            if (profile == MyShaderProfile.vs_5_0)
                Interlocked.Increment(ref vertexObjectBytecodeCaptureCount);
            else
                Interlocked.Increment(ref pixelObjectBytecodeCaptureCount);
        }
        AttachShaderObjectEvidence(state, profile, bytecode);
    }

    /// <summary>
    /// Called from IL injected immediately after the object-facing Compile call
    /// inside Keen's manager Init methods. The duplicated array is the exact
    /// result Keen stores for VertexShader/PixelShader construction, including
    /// cache-hit paths hidden from the five-argument compiler boundary.
    /// </summary>
    public static void NotePixelShaderInitBytecode(byte[] bytecode) =>
        NoteShaderInitBytecode(MyShaderProfile.ps_5_0, bytecode);

    public static void NoteVertexShaderInitBytecode(byte[] bytecode) =>
        NoteShaderInitBytecode(MyShaderProfile.vs_5_0, bytecode);

    /// <summary>
    /// Independent proof from MyVertexShaders.Init's byteCode out parameter.
    /// Unlike the transpiler capture this needs no IL-shape assumption.
    /// </summary>
    public static void NoteVertexShaderOutputBytecode(byte[] bytecode)
    {
        if (bytecode == null || bytecode.Length == 0)
            return;
        var state = PeekActiveShaderObject(MyShaderProfile.vs_5_0);
        if (state == null || !state.IsVelocityGBuffer)
            return;
        if (!state.VertexOutputBytecodeObserved)
        {
            state.VertexOutputBytecodeObserved = true;
            Interlocked.Increment(ref vertexOutputBytecodeCaptureCount);
        }
        AttachShaderObjectEvidence(state, MyShaderProfile.vs_5_0, bytecode);
    }

    static void NoteShaderInitBytecode(MyShaderProfile profile, byte[] bytecode)
    {
        if (bytecode == null || bytecode.Length == 0)
            return;
        var state = PeekActiveShaderObject(profile);
        if (state == null || !state.IsVelocityGBuffer)
            return;
        if (!state.InitBytecodeObserved)
        {
            state.InitBytecodeObserved = true;
            if (profile == MyShaderProfile.vs_5_0)
                Interlocked.Increment(ref vertexInitBytecodeCaptureCount);
            else
                Interlocked.Increment(ref pixelInitBytecodeCaptureCount);
        }
        AttachShaderObjectEvidence(state, profile, bytecode);
    }

    public static void NoteShaderInitTranspiler(MyShaderProfile profile, int sites)
    {
        if (profile == MyShaderProfile.vs_5_0)
            Volatile.Write(ref vertexInitTranspilerSites, sites);
        else if (profile == MyShaderProfile.ps_5_0)
            Volatile.Write(ref pixelInitTranspilerSites, sites);
    }

    public static void NoteShaderCacheResult(MyShaderProfile profile, bool hit)
    {
        if (profile != MyShaderProfile.vs_5_0 && profile != MyShaderProfile.ps_5_0)
            return;
        var state = PeekActiveShaderObject(profile);
        if (state == null || !state.IsVelocityGBuffer || state.CacheObserved)
            return;
        state.CacheObserved = true;
        if (profile == MyShaderProfile.vs_5_0)
        {
            if (hit)
                Interlocked.Increment(ref vertexCacheHitCount);
            else
                Interlocked.Increment(ref vertexCacheMissCount);
        }
        else
        {
            if (hit)
                Interlocked.Increment(ref pixelCacheHitCount);
            else
                Interlocked.Increment(ref pixelCacheMissCount);
        }
    }

    static void AttachShaderObjectEvidence(ShaderObjectCreationState state,
        MyShaderProfile profile, byte[] bytecode)
    {
        if (!BytecodeAuditEnabled || state == null || state.HasEvidence || state.Profile != profile)
            return;
        var evidence = ReflectGBufferBytecode(profile, bytecode);
        state.Evidence = evidence;
        state.HasEvidence = evidence.Hash != 0;
    }

    // Init has not published the object to Keen's manager yet. Repair only this
    // object, and only with a fully verified replacement; never edit live draws.
    static byte[] CompileVerifiedReplacement(ref MyShaderCompilationInfo info,
        out ShaderBytecodeEvidence evidence)
    {
        Interlocked.Increment(ref objectRepairAttempts);
        var macros = info.Macros ?? Array.Empty<ShaderMacro>();
        EnsureGBufferMacros(info.File.ToString(), ref macros);
        var path = Path.Combine(MyShaderCompiler.ShadersPath, info.File.ToString());
        var bytes = MyShaderCompiler.Compile(path, macros, info.Profile, info.File.ToString(),
            false, false, out var cached, out var compileLog, out var hash, false, false);
        evidence = ReflectGBufferBytecode(info.Profile, bytes);
        var required = info.Profile == MyShaderProfile.vs_5_0 ? VertexFlowRequired : PixelFlowRequired;
        if (evidence.Hash == 0 || (evidence.FlowMask & required) != required)
            throw new InvalidOperationException("replacement lacks velocity flow: " + info.File +
                " mask=" + evidence.FlowMask + " cache=" + cached + " hash=" + hash + " " + compileLog);
        return bytes;
    }

    public static void RepairVertexShader(ref MyShaderCompilationInfo info, ref byte[] bytes,
        ref VertexShader shader, ShaderObjectCreationState state)
    {
        if (!BytecodeAuditEnabled || state == null || !state.IsVelocityGBuffer || shader == null) return;
        // The out array outranks earlier wrapper observations.
        state.Evidence = ReflectGBufferBytecode(info.Profile, bytes);
        state.HasEvidence = state.Evidence.Hash != 0;
        if (state.HasEvidence && (state.Evidence.FlowMask & VertexFlowRequired) == VertexFlowRequired) return;
        var original = state.Evidence;
        try
        {
            var replacementBytes = CompileVerifiedReplacement(ref info, out var evidence);
            var replacement = new VertexShader(MyRender11.DeviceInstance, replacementBytes);
            var previous = shader;
            shader = replacement;
            bytes = replacementBytes; // Keep Keen's input-layout bytecode consistent.
            state.Evidence = evidence;
            state.HasEvidence = true;
            Interlocked.Increment(ref objectRepairSuccesses);
            DisposeReplacedShader(previous);
        }
        catch (Exception e)
        {
            state.Evidence = original;
            state.HasEvidence = original.Hash != 0;
            Interlocked.Increment(ref objectRepairFailures);
            MyLog.Default.WriteLine("Anomaly VS repair failed: " + e);
        }
    }

    public static void RepairPixelShader(ref MyShaderCompilationInfo info, ref PixelShader shader,
        ShaderObjectCreationState state)
    {
        if (!BytecodeAuditEnabled || state == null || !state.IsVelocityGBuffer || shader == null ||
            (state.HasEvidence && (state.Evidence.FlowMask & PixelFlowRequired) == PixelFlowRequired)) return;
        var original = state.Evidence;
        try
        {
            var bytes = CompileVerifiedReplacement(ref info, out var evidence);
            var replacement = new PixelShader(MyRender11.DeviceInstance, bytes);
            var previous = shader;
            shader = replacement;
            state.Evidence = evidence;
            state.HasEvidence = true;
            Interlocked.Increment(ref objectRepairSuccesses);
            DisposeReplacedShader(previous);
        }
        catch (Exception e)
        {
            state.Evidence = original;
            state.HasEvidence = original.Hash != 0;
            Interlocked.Increment(ref objectRepairFailures);
            MyLog.Default.WriteLine("Anomaly PS repair failed: " + e);
        }
    }

    static void DisposeReplacedShader(IDisposable previous)
    {
        try { previous.Dispose(); }
        catch (Exception e) { MyLog.Default.WriteLine("Anomaly replaced shader disposal: " + e); }
    }

    /// <summary>
    /// Inspect the exact GBuffer bytecode returned to Keen.
    /// Macro/object tracking alone cannot prove that FXC retained the b6 read,
    /// TEXCOORD12 link, and SV_Target3 write which form the velocity contract.
    /// </summary>
    static ShaderBytecodeEvidence ReflectGBufferBytecode(MyShaderProfile profile, byte[] bytecode)
    {
        if (!BytecodeAuditEnabled)
            return default;
        try
        {
            using var reflection = new ShaderReflection(bytecode);
            var desc = reflection.Description;
            var hasVelocityB6 = false;
            var hasPixelProbeB7 = false;
            var velocityCbSize = -1;
            var probeOffset = -1;
            var pixelProbeCbSize = -1;
            var pixelProbeOffset = -1;

            for (var i = 0; i < desc.BoundResources; i++)
            {
                var binding = reflection.GetResourceBindingDescription(i);
                if (!string.Equals(binding.Name, "AnomalyVelocity", StringComparison.Ordinal) ||
                    binding.BindPoint != GBufferVelocity.ConstantSlot)
                {
                    if (string.Equals(binding.Name, "AnomalyVelocityPixelProbe", StringComparison.Ordinal) &&
                        binding.BindPoint == GBufferVelocity.PixelProbeConstantSlot)
                        hasPixelProbeB7 = true;
                    continue;
                }
                hasVelocityB6 = true;
            }

            for (var i = 0; i < desc.ConstantBuffers; i++)
            {
                var cb = reflection.GetConstantBuffer(i);
                if (cb == null)
                    continue;
                var cbDesc = cb.Description;
                var isVelocity = string.Equals(cbDesc.Name, "AnomalyVelocity", StringComparison.Ordinal);
                var isPixelProbe = string.Equals(cbDesc.Name, "AnomalyVelocityPixelProbe", StringComparison.Ordinal);
                if (!isVelocity && !isPixelProbe)
                    continue;
                if (isVelocity)
                    velocityCbSize = cbDesc.Size;
                else
                    pixelProbeCbSize = cbDesc.Size;
                for (var j = 0; j < cbDesc.VariableCount; j++)
                {
                    var variable = cb.GetVariable(j);
                    if (variable == null)
                        continue;
                    var variableDesc = variable.Description;
                    if (isVelocity && string.Equals(variableDesc.Name, "AnomalyProbeMode", StringComparison.Ordinal))
                    {
                        probeOffset = variableDesc.StartOffset;
                        break;
                    }
                    if (isPixelProbe && string.Equals(variableDesc.Name, "AnomalyPixelProbe", StringComparison.Ordinal))
                    {
                        pixelProbeOffset = variableDesc.StartOffset;
                        break;
                    }
                }
            }

            if (velocityCbSize >= 0)
                Interlocked.Exchange(ref reflectedVelocityCbSize, velocityCbSize);
            if (probeOffset >= 0)
                Interlocked.Exchange(ref reflectedProbeOffset, probeOffset);
            if (pixelProbeCbSize >= 0)
                Interlocked.Exchange(ref reflectedPixelProbeCbSize, pixelProbeCbSize);
            if (pixelProbeOffset >= 0)
                Interlocked.Exchange(ref reflectedPixelProbeOffset, pixelProbeOffset);

            var flowMask = 0;
            if (profile == MyShaderProfile.vs_5_0)
            {
                Interlocked.Increment(ref reflectedVertexCount);
                if (hasVelocityB6)
                    Interlocked.Increment(ref reflectedVertexB6Count);
                var texcoord12Register = FindSemanticRegister(reflection, desc.OutputParameters,
                    input: false, "TEXCOORD", 12);
                if (texcoord12Register >= 0)
                    Interlocked.Increment(ref reflectedVertexTexcoord12Count);
                flowMask = InspectExecutableFlow(profile, bytecode, texcoord12Register, -1, -1, -1);
            }
            else
            {
                Interlocked.Increment(ref reflectedPixelCount);
                if (HasSemantic(reflection, desc.InputParameters, input: true, "TEXCOORD", 12))
                    Interlocked.Increment(ref reflectedPixelTexcoord12Count);
                var target0Register = FindSemanticRegister(reflection, desc.OutputParameters,
                    input: false, "SV_Target", 0);
                var target3Register = FindSemanticRegister(reflection, desc.OutputParameters,
                    input: false, "SV_Target", 3);
                var target7Register = FindSemanticRegister(reflection, desc.OutputParameters,
                    input: false, "SV_Target", 7);
                if (target3Register >= 0)
                    Interlocked.Increment(ref reflectedPixelTarget3Count);
                if (target7Register >= 0)
                    Interlocked.Increment(ref reflectedPixelTarget7Count);
                if (hasPixelProbeB7)
                    Interlocked.Increment(ref reflectedPixelProbeB7Count);
                flowMask = InspectExecutableFlow(profile, bytecode, -1, target0Register, target3Register, target7Register);
            }
            return new ShaderBytecodeEvidence(HashBytecode(bytecode), flowMask);
        }
        catch (Exception e)
        {
            Interlocked.Increment(ref reflectionFailureCount);
            DebugLog.Write("GBuffer bytecode reflection failed: " + e.Message);
            return default;
        }
    }

    static bool HasSemantic(ShaderReflection reflection, int count, bool input, string name, int index)
    {
        return FindSemanticRegister(reflection, count, input, name, index) >= 0;
    }

    static int FindSemanticRegister(ShaderReflection reflection, int count, bool input, string name, int index)
    {
        for (var i = 0; i < count; i++)
        {
            var parameter = input
                ? reflection.GetInputParameterDescription(i)
                : reflection.GetOutputParameterDescription(i);
            if (parameter.SemanticIndex == index &&
                string.Equals(parameter.SemanticName, name, StringComparison.OrdinalIgnoreCase))
                return parameter.Register;
        }
        return -1;
    }

    /// <summary>
    /// Reflection proves only that a resource and semantic were declared. Read
    /// the resident DXBC instruction stream as well, so the audit can prove that
    /// FXC retained the cbuffer reads and executable output writes.
    /// </summary>
    static int InspectExecutableFlow(MyShaderProfile profile, byte[] bytecode,
        int texcoord12Register, int target0Register, int target3Register, int target7Register)
    {
        if (!BytecodeAuditEnabled)
            return 0;
        try
        {
            using var shader = new ShaderBytecode(bytecode);
            var assembly = shader.Disassemble();
            if (profile == MyShaderProfile.vs_5_0)
            {
                var mask = 0;
                Interlocked.Increment(ref flowVertexCount);
                if (HasExecutableReference(assembly, "cb" + GBufferVelocity.ConstantSlot + "["))
                {
                    Interlocked.Increment(ref flowVertexB6ReadCount);
                    mask |= 0x1;
                }
                if (texcoord12Register >= 0 && HasExecutableOutputWrite(assembly, texcoord12Register))
                {
                    Interlocked.Increment(ref flowVertexTexcoord12WriteCount);
                    mask |= 0x2;
                }
                return mask;
            }

            var pixelMask = 0;
            Interlocked.Increment(ref flowPixelCount);
            if (HasExecutableReference(assembly, "cb" + GBufferVelocity.PixelProbeConstantSlot + "["))
            {
                Interlocked.Increment(ref flowPixelB7ReadCount);
                pixelMask |= 0x1;
            }
            if (target0Register >= 0 && HasExecutableOutputWrite(assembly, target0Register))
            {
                Interlocked.Increment(ref flowPixelTarget0WriteCount);
                pixelMask |= 0x2;
            }
            if (target3Register >= 0 && HasExecutableOutputWrite(assembly, target3Register))
            {
                Interlocked.Increment(ref flowPixelTarget3WriteCount);
                pixelMask |= 0x4;
            }
            if (target7Register >= 0 && HasExecutableOutputWrite(assembly, target7Register))
            {
                Interlocked.Increment(ref flowPixelTarget7WriteCount);
                pixelMask |= 0x8;
            }
            return pixelMask;
        }
        catch (Exception e)
        {
            Interlocked.Increment(ref flowFailureCount);
            DebugLog.Write("GBuffer bytecode disassembly failed: " + e.Message);
            return 0;
        }
    }

    static ulong HashBytecode(byte[] bytecode)
    {
        if (bytecode == null || bytecode.Length == 0)
            return 0;
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        for (var i = 0; i < bytecode.Length; i++)
        {
            hash ^= bytecode[i];
            hash *= prime;
        }
        return hash;
    }

    static bool HasExecutableReference(string assembly, string token)
    {
        if (string.IsNullOrEmpty(assembly) || string.IsNullOrEmpty(token))
            return false;
        foreach (var rawLine in assembly.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal) ||
                line.StartsWith("dcl_", StringComparison.OrdinalIgnoreCase))
                continue;
            if (line.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    static bool HasExecutableOutputWrite(string assembly, int register)
    {
        if (string.IsNullOrEmpty(assembly) || register < 0)
            return false;
        var token = "o" + register;
        foreach (var rawLine in assembly.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal) ||
                line.StartsWith("dcl_", StringComparison.OrdinalIgnoreCase))
                continue;
            var firstSpace = line.IndexOfAny(new[] { ' ', '\t' });
            if (firstSpace < 0)
                continue;
            var operands = line.Substring(firstSpace + 1).TrimStart();
            var comma = operands.IndexOf(',');
            var destination = (comma >= 0 ? operands.Substring(0, comma) : operands).Trim();
            if (destination.Equals(token, StringComparison.OrdinalIgnoreCase) ||
                destination.StartsWith(token + ".", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Inject into the shader owner's compilation info, not only the downstream
    /// five-argument compiler call. This makes lazy Stage 2 objects and native
    /// ReloadEffects generations carry the same GBuffer contract, and gives the
    /// Init patch stable per-call state even when compilation nests.
    /// </summary>
    static ShaderObjectCreationState PrepareShaderObject(ref MyShaderCompilationInfo info)
    {
        var file = info.File.ToString();
        var macros = info.Macros;
        EnsureGBufferMacros(file, ref macros);
        info.Macros = macros;
        return new ShaderObjectCreationState(
            IsGBufferPermutation(macros) || IsGeometryWithoutPass(file, macros),
            IsDepthPermutation(macros),
            ContainsNamed(macros, VelocityMacroName),
            info.Profile,
            CompactShaderPath(file),
            MacroFingerprint(macros));
    }

    /// <summary>
    /// Runs before Keen builds its manager cache key. The key must describe the
    /// same velocity-enabled permutation later compiled by Init; adding the
    /// macro only inside Init leaves the key and native object out of sync.
    /// </summary>
    public static void PrepareVertexShaderCreate(string file, ref ShaderMacro[] macros)
    {
        Interlocked.Increment(ref vertexCreateCount);
        EnsureGBufferMacros(file, ref macros);
        if (IsVelocityGBufferCompile(file, macros))
            Interlocked.Increment(ref vertexCreateVelocityCount);
    }

    public static void PreparePixelShaderCreate(string file, ref ShaderMacro[] macros)
    {
        Interlocked.Increment(ref pixelCreateCount);
        EnsureGBufferMacros(file, ref macros);
        if (IsVelocityGBufferCompile(file, macros))
            Interlocked.Increment(ref pixelCreateVelocityCount);
    }

    /// <summary>
    /// Runs at the object-facing compiler overload, above MyShaderCache. It
    /// preserves Keen's normal cache and in-progress synchronization; the deep
    /// compiler endpoint supplies authoritative cache-hit/fresh evidence.
    /// </summary>
    public static void PrepareObjectCompile(ref MyShaderCompilationInfo info, ref bool invalidateCache)
    {
        var macros = info.Macros;
        EnsureGBufferMacros(info.File.ToString(), ref macros);
        info.Macros = macros;
        if (!IsVelocityGBufferCompile(info.File.ToString(), macros))
            return;

        var state = PeekActiveShaderObject(info.Profile);
        if (state != null)
            state.ObjectCompileObserved = true;
        if (info.Profile == MyShaderProfile.vs_5_0)
            Interlocked.Increment(ref vertexObjectCompileCount);
        else if (info.Profile == MyShaderProfile.ps_5_0)
            Interlocked.Increment(ref pixelObjectCompileCount);
        _ = invalidateCache;
    }

    public static ShaderObjectCreationState PreparePixelShaderObject(ref MyShaderCompilationInfo info)
    {
        var state = PrepareShaderObject(ref info);
        (activePixelObjects ??= new Stack<ShaderObjectCreationState>()).Push(state);
        if (state.IsVelocityGBuffer)
            Interlocked.Increment(ref pixelVelocityInitCount);
        return state;
    }

    public static ShaderObjectCreationState PrepareVertexShaderObject(ref MyShaderCompilationInfo info)
    {
        var state = PrepareShaderObject(ref info);
        (activeVertexObjects ??= new Stack<ShaderObjectCreationState>()).Push(state);
        if (state.IsVelocityGBuffer)
            Interlocked.Increment(ref vertexVelocityInitCount);
        return state;
    }

    public static void NotePixelShaderObject(PixelShader shader, ShaderObjectCreationState state)
    {
        RemoveActiveShaderObject(activePixelObjects, state);
        CountShaderObjectRoute(state, false);
        if (shader == null || shader.NativePointer == IntPtr.Zero)
            return;

        lock (ShaderObjectGate)
        {
            var pointer = shader.NativePointer;
            Volatile.Write(ref residentPixelObjects, AddPointer(residentPixelObjects, pointer));
            if (state.IsGBuffer)
            {
                Volatile.Write(ref residentGBufferPixelObjects, AddPointer(residentGBufferPixelObjects, pointer));
                if (BytecodeAuditEnabled)
                {
                    if (state.HasEvidence)
                        Volatile.Write(ref residentPixelEvidence, AddEvidence(residentPixelEvidence, pointer, state.Evidence));
                    else
                        Interlocked.Increment(ref pixelEvidenceMissingCount);
                }
            }
            if (state.IsVelocityGBuffer)
                Volatile.Write(ref residentVelocityPixelObjects, AddPointer(residentVelocityPixelObjects, pointer));
            if (state.IsDepth)
                Volatile.Write(ref residentDepthPixelObjects, AddPointer(residentDepthPixelObjects, pointer));
        }
    }

    public static void NoteVertexShaderObject(VertexShader shader, ShaderObjectCreationState state)
    {
        RemoveActiveShaderObject(activeVertexObjects, state);
        CountShaderObjectRoute(state, true);
        if (shader == null || shader.NativePointer == IntPtr.Zero)
            return;

        lock (ShaderObjectGate)
        {
            var pointer = shader.NativePointer;
            Volatile.Write(ref residentVertexObjects, AddPointer(residentVertexObjects, pointer));
            if (state.IsGBuffer)
            {
                Volatile.Write(ref residentGBufferVertexObjects, AddPointer(residentGBufferVertexObjects, pointer));
                if (BytecodeAuditEnabled)
                {
                    if (state.HasEvidence)
                        Volatile.Write(ref residentVertexEvidence, AddEvidence(residentVertexEvidence, pointer, state.Evidence));
                    else
                        Interlocked.Increment(ref vertexEvidenceMissingCount);
                }
            }
            if (state.IsVelocityGBuffer)
                Volatile.Write(ref residentVelocityVertexObjects, AddPointer(residentVelocityVertexObjects, pointer));
            if (state.IsDepth)
                Volatile.Write(ref residentDepthVertexObjects, AddPointer(residentDepthVertexObjects, pointer));
        }
    }

    static ShaderObjectCreationState PeekActiveShaderObject(MyShaderProfile profile)
    {
        var stack = profile == MyShaderProfile.vs_5_0 ? activeVertexObjects : activePixelObjects;
        return stack != null && stack.Count != 0 ? stack.Peek() : null;
    }

    static void RemoveActiveShaderObject(Stack<ShaderObjectCreationState> stack,
        ShaderObjectCreationState state)
    {
        if (stack == null || stack.Count == 0 || state == null)
            return;
        if (ReferenceEquals(stack.Peek(), state))
        {
            stack.Pop();
            return;
        }

        // Error-retry recursion can complete an outer Init after an inner one.
        // Preserve newer frames while removing only this exact call.
        var newer = new Stack<ShaderObjectCreationState>();
        while (stack.Count != 0 && !ReferenceEquals(stack.Peek(), state))
            newer.Push(stack.Pop());
        if (stack.Count != 0)
            stack.Pop();
        while (newer.Count != 0)
            stack.Push(newer.Pop());
    }

    static void CountShaderObjectRoute(ShaderObjectCreationState state, bool vertex)
    {
        if (state == null || !state.IsVelocityGBuffer || state.ObjectCompileObserved ||
            !state.FiveArgumentCompileObserved)
            return;
        if (vertex)
            Interlocked.Increment(ref vertexFiveArgFallbackCount);
        else
            Interlocked.Increment(ref pixelFiveArgFallbackCount);
    }

    public static void ClearPixelShaderObjects()
    {
        lock (ShaderObjectGate)
        {
            // Keep the immediately previous MyPixelShaders generation. A draw
            // matching this set is genuinely stale; matching neither current
            // nor retired means another owner or an unobserved creation path.
            Volatile.Write(ref retiredPixelObjects, residentPixelObjects);
            Volatile.Write(ref residentPixelObjects, Array.Empty<IntPtr>());
            Volatile.Write(ref residentGBufferPixelObjects, Array.Empty<IntPtr>());
            Volatile.Write(ref residentVelocityPixelObjects, Array.Empty<IntPtr>());
            Volatile.Write(ref residentDepthPixelObjects, Array.Empty<IntPtr>());
            Volatile.Write(ref residentPixelEvidence, Array.Empty<ShaderObjectEvidence>());
            Interlocked.Exchange(ref pixelEvidenceMissingCount, 0);
            Interlocked.Exchange(ref pixelVelocityInitCount, 0);
            Interlocked.Exchange(ref pixelObjectCompileCount, 0);
            Interlocked.Exchange(ref pixelObjectBytecodeCaptureCount, 0);
            Interlocked.Exchange(ref pixelFiveArgFallbackCount, 0);
            Interlocked.Exchange(ref pixelInitBytecodeCaptureCount, 0);
            Interlocked.Exchange(ref pixelCacheHitCount, 0);
            Interlocked.Exchange(ref pixelCacheMissCount, 0);
            Interlocked.Exchange(ref deepPixelCompileCount, 0);
            Interlocked.Exchange(ref deepPixelCacheHitCount, 0);
            Interlocked.Exchange(ref deepPixelFreshCount, 0);
            Interlocked.Exchange(ref deepPixelFullFlowCount, 0);
            Interlocked.Exchange(ref deepPixelInvalidatedCount, 0);
            Interlocked.Exchange(ref deepPixelOrphanCount, 0);
            DeepPixelMissingRoutes.Clear();
        }
    }

    public static void ClearVertexShaderObjects()
    {
        lock (ShaderObjectGate)
        {
            Volatile.Write(ref retiredVertexObjects, residentVertexObjects);
            Volatile.Write(ref residentVertexObjects, Array.Empty<IntPtr>());
            Volatile.Write(ref residentGBufferVertexObjects, Array.Empty<IntPtr>());
            Volatile.Write(ref residentVelocityVertexObjects, Array.Empty<IntPtr>());
            Volatile.Write(ref residentDepthVertexObjects, Array.Empty<IntPtr>());
            Volatile.Write(ref residentVertexEvidence, Array.Empty<ShaderObjectEvidence>());
            Interlocked.Exchange(ref vertexEvidenceMissingCount, 0);
            Interlocked.Exchange(ref vertexVelocityInitCount, 0);
            Interlocked.Exchange(ref vertexObjectCompileCount, 0);
            Interlocked.Exchange(ref vertexObjectBytecodeCaptureCount, 0);
            Interlocked.Exchange(ref vertexFiveArgFallbackCount, 0);
            Interlocked.Exchange(ref vertexInitBytecodeCaptureCount, 0);
            Interlocked.Exchange(ref vertexOutputBytecodeCaptureCount, 0);
            Interlocked.Exchange(ref vertexCacheHitCount, 0);
            Interlocked.Exchange(ref vertexCacheMissCount, 0);
            Interlocked.Exchange(ref deepVertexCompileCount, 0);
            Interlocked.Exchange(ref deepVertexCacheHitCount, 0);
            Interlocked.Exchange(ref deepVertexFreshCount, 0);
            Interlocked.Exchange(ref deepVertexFullFlowCount, 0);
            Interlocked.Exchange(ref deepVertexInvalidatedCount, 0);
            Interlocked.Exchange(ref deepVertexOrphanCount, 0);
            DeepVertexMissingRoutes.Clear();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsResidentPixelObject(PixelShader shader) =>
        ContainsPointer(Volatile.Read(ref residentPixelObjects), shader);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsResidentGBufferPixelObject(PixelShader shader) =>
        ContainsPointer(Volatile.Read(ref residentGBufferPixelObjects), shader);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsResidentVelocityPixelObject(PixelShader shader) =>
        ContainsPointer(Volatile.Read(ref residentVelocityPixelObjects), shader);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsResidentDepthPixelObject(PixelShader shader) =>
        ContainsPointer(Volatile.Read(ref residentDepthPixelObjects), shader);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsVerifiedGBufferPixelObject(PixelShader shader) =>
        ContainsFullFlow(Volatile.Read(ref residentPixelEvidence), shader?.NativePointer ?? IntPtr.Zero, PixelFlowRequired);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsRetiredPixelObject(PixelShader shader) =>
        ContainsPointer(Volatile.Read(ref retiredPixelObjects), shader?.NativePointer ?? IntPtr.Zero);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsResidentVertexObject(VertexShader shader) =>
        ContainsPointer(Volatile.Read(ref residentVertexObjects), shader?.NativePointer ?? IntPtr.Zero);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsResidentGBufferVertexObject(VertexShader shader) =>
        ContainsPointer(Volatile.Read(ref residentGBufferVertexObjects), shader?.NativePointer ?? IntPtr.Zero);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsResidentVelocityVertexObject(VertexShader shader) =>
        ContainsPointer(Volatile.Read(ref residentVelocityVertexObjects), shader?.NativePointer ?? IntPtr.Zero);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsResidentDepthVertexObject(VertexShader shader) =>
        ContainsPointer(Volatile.Read(ref residentDepthVertexObjects), shader?.NativePointer ?? IntPtr.Zero);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsVerifiedGBufferVertexObject(VertexShader shader) =>
        ContainsFullFlow(Volatile.Read(ref residentVertexEvidence), shader?.NativePointer ?? IntPtr.Zero, VertexFlowRequired);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsRetiredVertexObject(VertexShader shader) =>
        ContainsPointer(Volatile.Read(ref retiredVertexObjects), shader?.NativePointer ?? IntPtr.Zero);

    static IntPtr[] AddPointer(IntPtr[] current, IntPtr pointer)
    {
        for (var i = 0; i < current.Length; i++)
        {
            if (current[i] == pointer)
                return current;
        }
        var next = new IntPtr[current.Length + 1];
        Array.Copy(current, next, current.Length);
        next[current.Length] = pointer;
        return next;
    }

    static ShaderObjectEvidence[] AddEvidence(ShaderObjectEvidence[] current, IntPtr pointer,
        ShaderBytecodeEvidence evidence)
    {
        for (var i = 0; i < current.Length; i++)
        {
            if (current[i].Pointer != pointer)
                continue;
            if (current[i].Hash == evidence.Hash && current[i].FlowMask == evidence.FlowMask)
                return current;
            var replaced = (ShaderObjectEvidence[])current.Clone();
            replaced[i] = new ShaderObjectEvidence(pointer, evidence);
            return replaced;
        }
        var next = new ShaderObjectEvidence[current.Length + 1];
        Array.Copy(current, next, current.Length);
        next[current.Length] = new ShaderObjectEvidence(pointer, evidence);
        return next;
    }

    static int CountFullFlow(ShaderObjectEvidence[] current, int required)
    {
        var count = 0;
        for (var i = 0; i < current.Length; i++)
        {
            if ((current[i].FlowMask & required) == required)
                count++;
        }
        return count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool ContainsFullFlow(ShaderObjectEvidence[] current, IntPtr pointer, int required)
    {
        if (pointer == IntPtr.Zero)
            return false;
        for (var i = 0; i < current.Length; i++)
        {
            if (current[i].Pointer == pointer)
                return (current[i].FlowMask & required) == required;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool ContainsPointer(IntPtr[] objects, PixelShader shader)
    {
        return ContainsPointer(objects, shader?.NativePointer ?? IntPtr.Zero);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool ContainsPointer(IntPtr[] objects, IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
            return false;
        for (var i = 0; i < objects.Length; i++)
        {
            if (objects[i] == pointer)
                return true;
        }
        return false;
    }

    public static void EnsureIncludes(IReadOnlyList<string> includes)
    {
        if (includes is not List<string> list)
            return;
        lock (Gate)
        {
            ArrangeIncludeSearchOrder(list);
        }
    }

    public static void EnsureGBufferMacros(string filepath, ref ShaderMacro[] macros)
    {
        if (IsDeferredReadCompile(filepath))
        {
            EnsureReadPassMacros(ref macros);
            return;
        }

        EnsureVelocityMacro(filepath, ref macros);
        AppendGBufferExtras(filepath, ref macros);
    }

    public static void EnsureGBufferMacros(List<ShaderMacro> macros)
    {
        EnsureVelocityMacro(macros);
        AppendGBufferExtras(macros);
    }

    static void AppendGBufferExtras(string filepath, ref ShaderMacro[] macros)
    {
        if (IsDepthPermutation(macros))
            return;
        if (!IsGBufferPermutation(macros) && !IsGeometryWithoutPass(filepath, macros))
            return;
        AppendMissing(ref macros, ShaderPackRegistry.LiveDefineMacros);
        AppendMissing(ref macros, GBufferAttachments.LiveDefineMacros);
    }

    static void AppendGBufferExtras(List<ShaderMacro> macros)
    {
        if (macros == null || !IsGBufferPermutation(macros))
            return;
        AppendMissing(macros, ShaderPackRegistry.LiveDefineMacros);
        AppendMissing(macros, GBufferAttachments.LiveDefineMacros);
    }

    static void EnsureReadPassMacros(ref ShaderMacro[] macros)
    {
        if (IsDepthPermutation(macros))
            return;
        if (!ContainsNamed(macros, VelocityMacroName))
            macros = AppendMacro(macros, VelocityMacroName, VelocityMacroValue);
        AppendMissing(ref macros, ShaderPackRegistry.LiveDefineMacros);
        AppendMissing(ref macros, GBufferAttachments.LiveDefineMacros);
    }

    static bool IsDeferredReadCompile(string filepath)
    {
        if (string.IsNullOrEmpty(filepath))
            return false;
        var n = filepath.Replace('\\', '/');
        return n.IndexOf("/Lighting/", StringComparison.OrdinalIgnoreCase) >= 0 ||
               n.IndexOf("/Transparent/OIT/Resolve", StringComparison.OrdinalIgnoreCase) >= 0 ||
               n.IndexOf("/Transparent/Atmosphere/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static void AppendMissing(ref ShaderMacro[] macros, ShaderMacro[] extra)
    {
        if (extra == null || extra.Length == 0)
            return;
        for (var i = 0; i < extra.Length; i++)
        {
            if (ContainsNamed(macros, extra[i].Name))
                continue;
            macros = AppendMacro(macros, extra[i].Name, extra[i].Definition);
        }
    }

    static void AppendMissing(List<ShaderMacro> macros, ShaderMacro[] extra)
    {
        if (macros == null || extra == null)
            return;
        for (var i = 0; i < extra.Length; i++)
        {
            if (ContainsNamed(macros, extra[i].Name))
                continue;
            macros.Add(extra[i]);
        }
    }

    /// <summary>
    /// GBuffer only (<c>RENDERING_PASS=0</c>). Depth / forward / highlight stay 3-attachment.
    /// </summary>
    public static void EnsureVelocityMacro(ref ShaderMacro[] macros)
    {
        EnsureVelocityMacro(null, ref macros);
    }

    public static void EnsureVelocityMacro(string filepath, ref ShaderMacro[] macros)
    {
        if (IsDepthPermutation(macros))
            return;
        if (!IsGBufferPermutation(macros) && !IsGeometryWithoutPass(filepath, macros))
            return;
        if (!ContainsNamed(macros, VelocityMacroName))
            macros = AppendMacro(macros, VelocityMacroName, VelocityMacroValue);
    }

    public static void EnsureVelocityMacro(List<ShaderMacro> macros)
    {
        if (macros == null || !IsGBufferPermutation(macros))
            return;
        if (!ContainsNamed(macros, VelocityMacroName))
            macros.Add(new ShaderMacro(VelocityMacroName, VelocityMacroValue));
    }

    /// <summary>
    /// Local Keen includes do not search <c>m_includes</c>. Prefix
    /// <c>MyIncludeProcessor.Open</c> so arbitrary pack overlays,
    /// hashed Keen patches, and <c>Keen/</c> escape-hatch includes still resolve.
    /// </summary>
    public static bool TryOpenOverlay(IncludeType includeType, string fileName, Stream parentStream, out Stream stream)
    {
        stream = null;
        if (string.IsNullOrEmpty(fileName))
            return false;
        if (fileName.IndexOf("..", StringComparison.Ordinal) >= 0)
            return false;

        if (TryOpenKeenPrefixed(fileName, out stream))
            return true;

        string relativeKey = null;
        if (includeType == IncludeType.System)
        {
            relativeKey = fileName;
        }
        else
        {
            string parentDir = null;
            if (parentStream is FileStream parentFile && !string.IsNullOrEmpty(parentFile.Name))
                parentDir = Path.GetDirectoryName(parentFile.Name);
            else if (parentStream is KeenPatchedIncludeStream patchedParent &&
                     !string.IsNullOrEmpty(patchedParent.VirtualPath))
            {
                var virt = patchedParent.VirtualPath.Replace('\\', '/');
                var slash = virt.LastIndexOf('/');
                var parentVirt = slash < 0 ? "" : virt.Substring(0, slash);
                relativeKey = string.IsNullOrEmpty(parentVirt)
                    ? fileName.Replace('\\', '/')
                    : parentVirt + "/" + fileName.Replace('\\', '/');
            }
            if (!string.IsNullOrEmpty(parentDir))
            {
                var resolved = Path.GetFullPath(Path.Combine(parentDir, fileName));
                try
                {
                    var shadersRoot = Path.GetFullPath(MyShaderCompiler.ShadersPath);
                    if (TryRelativize(shadersRoot, resolved, out var rel))
                        relativeKey = rel;
                }
                catch
                {
                    // ShadersPath not ready yet.
                }

                if (relativeKey == null && !string.IsNullOrEmpty(IncludeDirectory) &&
                    TryRelativize(Path.GetFullPath(IncludeDirectory), resolved, out var relInclude))
                    relativeKey = relInclude;
            }
        }

        if (!string.IsNullOrEmpty(relativeKey) && ShaderPackRegistry.TryOpenGenerated(relativeKey, out stream))
            return true;
        if (!string.IsNullOrEmpty(relativeKey) && ShaderPackRegistry.TryResolveOverlay(relativeKey, out var packFile) &&
            File.Exists(packFile))
        {
            stream = new FileStream(packFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }

        if (KeenShaderGuard.TryOpen(!string.IsNullOrEmpty(relativeKey) ? relativeKey : fileName, out stream))
            return true;

        if (string.IsNullOrEmpty(IncludeDirectory) || string.IsNullOrEmpty(fileName))
            return false;

        var includeRoot = Path.GetFullPath(IncludeDirectory);
        string overlayPath = null;

        if (includeType == IncludeType.System)
        {
            overlayPath = Path.GetFullPath(Path.Combine(includeRoot, fileName));
        }
        else if (!string.IsNullOrEmpty(relativeKey))
        {
            overlayPath = Path.GetFullPath(Path.Combine(includeRoot, relativeKey));
        }

        if (string.IsNullOrEmpty(overlayPath) || !File.Exists(overlayPath))
        {
            if (TryOpenKeenLocal(includeType, relativeKey, out stream))
                return true;
            return false;
        }
        if (!IsUnderRoot(includeRoot, overlayPath))
            return false;

        stream = new FileStream(overlayPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return true;
    }

    /// <summary>Records the actual file returned by Keen's include processor.</summary>
    public static void NoteOpenedInclude(Stream stream)
    {
        string path;
        string origin;
        if (stream is KeenPatchedIncludeStream patched && !string.IsNullOrEmpty(patched.VirtualPath))
        {
            path = patched.VirtualPath.Replace('\\', '/');
            origin = "anomaly";
        }
        else if (stream is FileStream file && !string.IsNullOrEmpty(file.Name))
        {
            path = file.Name.Replace('\\', '/');
            origin = DescribeIncludeOrigin(file.Name);
        }
        else
            return;
        var key = path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
        if (key.EndsWith("/Geometry/Passes/VertexStage.hlsli", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref dispatcherVertexOpenCount);
            Volatile.Write(ref lastDispatcherVertexOrigin, origin);
        }
        else if (key.EndsWith("/Geometry/Passes/PixelStage.hlsli", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref dispatcherPixelOpenCount);
            Volatile.Write(ref lastDispatcherPixelOrigin, origin);
        }
        else if (key.EndsWith("/Geometry/Passes/GBuffer/VertexStage.hlsli", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref gbufferVertexOpenCount);
            Volatile.Write(ref lastGBufferVertexOrigin, origin);
        }
        else if (key.EndsWith("/Geometry/Passes/GBuffer/PixelStage.hlsli", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref gbufferPixelOpenCount);
            Volatile.Write(ref lastGBufferPixelOrigin, origin);
        }
    }

    /// <summary>
    /// <c>#include &lt;Keen/relative.hlsli&gt;</c> opens the game file and
    /// skips overlay remap so Anomaly wraps can include the original.
    /// </summary>
    static bool TryOpenKeenPrefixed(string fileName, out Stream stream)
    {
        stream = null;
        var n = fileName.Replace('\\', '/');
        const string prefix = "Keen/";
        if (!n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            var rest = n.Substring(prefix.Length);
            if (rest.Length == 0 || rest.IndexOf("..", StringComparison.Ordinal) >= 0)
                return false;
            var shadersRoot = Path.GetFullPath(MyShaderCompiler.ShadersPath);
            var keenPath = Path.GetFullPath(Path.Combine(shadersRoot, rest.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(keenPath) || !IsUnderRoot(shadersRoot, keenPath))
                return false;
            stream = new FileStream(keenPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static bool TryOpenKeenLocal(IncludeType includeType, string relativeKey, out Stream stream)
    {
        stream = null;
        if (includeType != IncludeType.Local || string.IsNullOrEmpty(relativeKey))
            return false;
        try
        {
            var shadersRoot = Path.GetFullPath(MyShaderCompiler.ShadersPath);
            var keenPath = Path.GetFullPath(Path.Combine(shadersRoot, relativeKey));
            if (!File.Exists(keenPath) || !IsUnderRoot(shadersRoot, keenPath))
                return false;
            stream = new FileStream(keenPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void EnsureGlobalMacros(ref ShaderMacro[] macros)
    {
        if (ContainsNamed(macros, MacroName))
            return;

        lock (Gate)
        {
            var field = AccessTools.Field(typeof(MyShaderCompiler), "m_globalShaderMacros");
            var current = field?.GetValue(null) as ShaderMacro[] ?? macros ?? Array.Empty<ShaderMacro>();
            if (ContainsNamed(current, MacroName))
            {
                macros = current;
                return;
            }

            var next = AppendMacro(current, MacroName, MacroValue);
            field?.SetValue(null, next);
            macros = next;
        }
    }

    private static string ResolveIncludeDirectory()
    {
        foreach (var candidate in IncludeCandidates())
        {
            if (!string.IsNullOrEmpty(candidate) && Directory.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    private static IEnumerable<string> IncludeCandidates()
    {
        if (!string.IsNullOrEmpty(includeDirectoryOverride))
            yield return includeDirectoryOverride;

        if (!string.IsNullOrEmpty(assetFolder))
        {
            yield return Path.Combine(assetFolder, "Shaders");
            yield return assetFolder;
        }

        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!string.IsNullOrEmpty(asmDir))
            yield return Path.Combine(asmDir, "Shaders");
    }

    private static void EnsureGlobalMacro()
    {
        var field = AccessTools.Field(typeof(MyShaderCompiler), "m_globalShaderMacros");
        if (field == null)
            throw new MissingFieldException(typeof(MyShaderCompiler).FullName, "m_globalShaderMacros");

        var current = field.GetValue(null) as ShaderMacro[] ?? Array.Empty<ShaderMacro>();
        if (ContainsNamed(current, MacroName))
            return;
        field.SetValue(null, AppendMacro(current, MacroName, MacroValue));
    }

    internal static bool IsDepthPermutation(ShaderMacro[] macros)
    {
        if (macros == null)
            return false;
        for (var i = 0; i < macros.Length; i++)
        {
            if (string.Equals(macros[i].Name, "DEPTH_ONLY", StringComparison.Ordinal))
                return true;
            if (string.Equals(macros[i].Name, RenderingPassMacro, StringComparison.Ordinal) &&
                macros[i].Definition == "1")
                return true;
        }

        return false;
    }

    private static bool IsGeometryWithoutPass(string filepath, ShaderMacro[] macros)
    {
        if (string.IsNullOrEmpty(filepath) || HasNamed(macros, RenderingPassMacro))
            return false;
        return filepath.IndexOf("Geometry", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsVelocityGBufferCompile(string filepath, ShaderMacro[] macros)
    {
        return !IsDepthPermutation(macros) &&
               ContainsNamed(macros, VelocityMacroName) &&
               (IsGBufferPermutation(macros) || IsGeometryWithoutPass(filepath, macros));
    }

    private static bool HasNamed(ShaderMacro[] macros, string name)
    {
        return ContainsNamed(macros, name);
    }

    private static bool IsGBufferPermutation(ShaderMacro[] macros)
    {
        if (macros == null)
            return false;
        for (var i = 0; i < macros.Length; i++)
        {
            if (string.Equals(macros[i].Name, RenderingPassMacro, StringComparison.Ordinal))
                return IsGBufferPassValue(macros[i].Definition);
        }

        return false;
    }

    private static bool IsGBufferPermutation(List<ShaderMacro> macros)
    {
        for (var i = 0; i < macros.Count; i++)
        {
            if (string.Equals(macros[i].Name, RenderingPassMacro, StringComparison.Ordinal))
                return IsGBufferPassValue(macros[i].Definition);
        }

        return false;
    }

    private static bool IsGBufferPassValue(string definition)
    {
        return definition == "0";
    }

    private static bool ContainsNamed(ShaderMacro[] macros, string name)
    {
        if (macros == null)
            return false;
        for (var i = 0; i < macros.Length; i++)
        {
            if (string.Equals(macros[i].Name, name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool ContainsNamed(List<ShaderMacro> macros, string name)
    {
        for (var i = 0; i < macros.Count; i++)
        {
            if (string.Equals(macros[i].Name, name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static ShaderMacro[] AppendMacro(ShaderMacro[] current, string name, string value)
    {
        current = current ?? Array.Empty<ShaderMacro>();
        var next = new ShaderMacro[current.Length + 1];
        Array.Copy(current, next, current.Length);
        next[current.Length] = new ShaderMacro(name, value);
        return next;
    }

    private static string CompactShaderPath(string filepath)
    {
        if (string.IsNullOrEmpty(filepath))
            return "?";
        var value = filepath.Replace('\\', '/');
        var marker = value.LastIndexOf("/Shaders/", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
            value = value.Substring(marker + 9);
        const int maxLength = 52;
        return value.Length <= maxLength ? value : "..." + value.Substring(value.Length - maxLength + 3);
    }

    private static string MacroFingerprint(ShaderMacro[] macros)
    {
        unchecked
        {
            uint hash = 2166136261;
            if (macros != null)
            {
                for (var i = 0; i < macros.Length; i++)
                {
                    HashFingerprintPart(ref hash, macros[i].Name);
                    hash = (hash ^ '=') * 16777619;
                    HashFingerprintPart(ref hash, macros[i].Definition);
                    hash = (hash ^ ';') * 16777619;
                }
            }
            return hash.ToString("X8");
        }
    }

    private static void HashFingerprintPart(ref uint hash, string value)
    {
        if (value == null)
            return;
        unchecked
        {
            for (var i = 0; i < value.Length; i++)
                hash = (hash ^ value[i]) * 16777619;
        }
    }

    private static void AddMissingDeepRoute(bool vertex, string route)
    {
        lock (ShaderObjectGate)
        {
            var routes = vertex ? DeepVertexMissingRoutes : DeepPixelMissingRoutes;
            if (routes.Contains(route))
                return;
            if (routes.Count < DeepMissingRouteLimit)
                routes.Add(route);
        }
    }

    private static string FormatMissingRoutes(List<string> routes)
    {
        return routes.Count == 0 ? "-" : string.Join(",", routes);
    }

    private static bool TryRelativize(string root, string fullPath, out string relative)
    {
        relative = null;
        if (!IsUnderRoot(root, fullPath))
            return false;
        relative = fullPath.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return relative.Length > 0;
    }

    private static bool IsUnderRoot(string root, string fullPath)
    {
        var prefix = root;
        if (!prefix.EndsWith(Path.DirectorySeparatorChar.ToString()) &&
            !prefix.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            prefix += Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureIncludePath()
    {
        var field = AccessTools.Field(typeof(MyShaderCompiler), "m_includes");
        if (field == null)
            throw new MissingFieldException(typeof(MyShaderCompiler).FullName, "m_includes");

        var list = field.GetValue(null) as List<string>;
        if (list == null)
        {
            list = new List<string> { MyShaderCompiler.ShadersPath };
            field.SetValue(null, list);
        }

        ArrangeIncludeSearchOrder(list);
    }

    /// <summary>
    /// MyIncludeProcessor searches system roots from the tail toward index 0.
    /// Keep pack roots ahead of Keen while reserving the final (winning) slot
    /// for Anomaly's stable geometry dispatchers.
    /// </summary>
    private static void ArrangeIncludeSearchOrder(List<string> list)
    {
        foreach (var packDir in PackIncludes)
            MoveIncludeToTail(list, packDir);
        MoveIncludeToTail(list, IncludeDirectory);

        var winner = list.Count > 0 ? DescribeIncludeOrigin(list[list.Count - 1]) : "none";
        Volatile.Write(ref includeOrderStatus, "reverse winner=" + winner + " roots=" + list.Count);
    }

    private static void MoveIncludeToTail(List<string> list, string directory)
    {
        if (string.IsNullOrEmpty(directory))
            return;

        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (string.Equals(list[i], directory, StringComparison.OrdinalIgnoreCase))
                list.RemoveAt(i);
        }
        list.Add(directory);
    }

    private static string DescribeIncludeOrigin(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "unknown";
        try
        {
            var full = Path.GetFullPath(path);
            if (!string.IsNullOrEmpty(IncludeDirectory) &&
                IsUnderRoot(Path.GetFullPath(IncludeDirectory), full))
                return "anomaly";
            for (var i = 0; i < PackIncludes.Count; i++)
            {
                if (IsUnderRoot(Path.GetFullPath(PackIncludes[i]), full))
                    return "pack" + i;
            }
            if (IsUnderRoot(Path.GetFullPath(MyShaderCompiler.ShadersPath), full))
                return "keen";
        }
        catch
        {
            return "invalid";
        }
        return "other";
    }

    private static void AddIncludeIfMissing(List<string> list, string directory)
    {
        if (string.IsNullOrEmpty(directory))
            return;

        foreach (var existing in list)
        {
            if (string.Equals(existing, directory, StringComparison.OrdinalIgnoreCase))
                return;
        }

        list.Add(directory);
    }

    private static string MacrosToString(ShaderMacro[] macros)
    {
        if (macros == null || macros.Length == 0)
            return "";
        var parts = new string[macros.Length];
        for (var i = 0; i < macros.Length; i++)
            parts[i] = macros[i].Name + "=" + macros[i].Definition;
        return string.Join("; ", parts);
    }
}
