using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using ClientPlugin.Shaders;
using ClientPlugin.Velocity;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using VRage.Library.Collections;
using VRage.Render11.Common;
using VRage.Render11.Culling;
using VRage.Render11.GeometryStage2.Common;
using VRage.Render11.GeometryStage2.Instancing;
using VRage.Render11.GeometryStage2.Lodding;
using VRage.Render11.GeometryStage2.Model;
using VRage.Render11.GeometryStage2.PreparePass;
using VRage.Render11.GeometryStage2.Rendering;
using VRage.Render11.GeometryStage2.RenderPass;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRage.Render11.Scene.Components;
using VRage.Utils;
using VRageMath;
using VRageRender;

namespace ClientPlugin.ShaderFramework;

/// <summary>
/// Extra GBuffer color target + Stage 2 previous-world SRV + old-pipeline prev bones.
/// Bound from GBuffer begin (old <c>MyGBufferPass</c> and Stage 2
/// <c>MyGBufferRenderPass</c>). CPU history is keyed by ActorID; the GPU never
/// sees those IDs. Stage 2 packs t15 in instance-buffer order and the VS indexes
/// <c>t15[SV_InstanceID + InstanceBase]</c>. Old-pipeline cube binds VS b6
/// <c>currToPrev</c> through an Anomaly-owned CB ring per deferred context.
/// Every CB is mapped at most once per frame, so recorded draws never depend on
/// deferred-context MapDiscard aliasing. Depth stays on Keen's 3-RT SetRtvs.
/// </summary>
public static class GBufferVelocity
{
    public const int ConstantSlot = 6;
    public const int PixelProbeConstantSlot = 7;
    public const int PrevWorldSlot = 15;
    public const int PrevBoneSlot = 16;
    // The shader-visible layouts are 240 and 16 bytes, but D3D11.1 ranged
    // constant-buffer binds require both the first constant and the constant
    // count to be multiples of 16 constants (256 bytes). Keep one physical
    // 256-byte window per buffer; the unused tail is outside the HLSL layout.
    // These buffers are Anomaly-owned and never alias Keen's object-CB cache.
    const int ConstantLayoutBytes = 240;
    const int PixelProbeLayoutBytes = 16;
    const int ConstantBufferBytes = 256;
    const int PixelProbeConstantBufferBytes = 256;
    const int PrevStride = 64;
    const int BoneStride = 64;

    static readonly object Gate = new();
    static readonly object BlendGate = new();
    static readonly object VelocityCbGate = new();
    static BlendOverride[] blendOverrides = Array.Empty<BlendOverride>();
    static readonly Dictionary<MyRenderContext, VelocityCbPool> VelocityCbPools = new();
    static IConstantBuffer mrtWriteProbeCb;
    static IConstantBuffer pixelProbeOffCb;
    static IConstantBuffer mrtWritePixelProbeCb;
    static IReadBuffer mrtWriteProbeReadback;
    static IReadBuffer mrtWritePixelProbeReadback;
    static int mrtWriteProbeReadbackState;
    static int mrtWriteProbeReadbackValue = int.MinValue;
    static string mrtWritePixelProbeReadbackValue;
    static string mrtWriteProbeReadbackError;
    static int nextVelocityCbPoolId;
    static int velocityCbPoolCapacity;
    [ThreadStatic] static MyRenderContext activeGBufferContext;
    [ThreadStatic] static IDsvBindable activeGBufferDsvBind;
    [ThreadStatic] static DepthStencilView activeGBufferDsv;
    [ThreadStatic] static RenderTargetView[] activeGBufferBaseRtvs;
    [ThreadStatic] static RenderTargetView[] activeGBufferRtvs;
    [ThreadStatic] static VelocityCbPool activeVelocityCbPool;
    [ThreadStatic] static IConstantBuffer activeVelocityCb;
    [ThreadStatic] static IConstantBuffer activePixelProbeCb;
    [ThreadStatic] static VertexShader activeResolvedVertexShader;
    [ThreadStatic] static PixelShader activeResolvedPixelShader;
    [ThreadStatic] static bool drawBoundaryInspected;
    [ThreadStatic] static bool nativeBlendOverridden;
    [ThreadStatic] static BlendState activeVelocityBlendOverride;

    public static bool Enabled { get; set; } = true;
    public static bool IsLive { get; private set; }
    public static string LastError { get; private set; }

    static IRtvTexture target;
    static IRtvTexture resolved;
    static IRtvTexture auditTarget;
    static IRtvTexture auditResolved;
    static IRtvTexture checkpointTarget;
    static IRtvTexture checkpointResolved;
    static IRtvTexture auditCheckpointTarget;
    static IRtvTexture auditCheckpointResolved;
    static int targetWidth;
    static int targetHeight;
    static int targetSamples;
    static int targetSamplesQuality;
    static string targetContractStatus = "not allocated";
    static Target3Checkpoint capturedCheckpoint;
    static long checkpointCaptureFrame = -1;
    static int checkpointCaptureCount;
    static IntPtr checkpointDebugSourceNative;
    static string checkpointLastError;
    static ISrvBuffer prevWorld;
    static ISrvBuffer prevBones;
    static int prevCapacity;
    static int prevCount;
    static PrevInstance[] cpuPrev = Array.Empty<PrevInstance>();
    static readonly Matrix[] BoneScratch = new Matrix[BoneHistory.MaxBones];
    static readonly Dictionary<long, int> GroupIndex = new();
    static readonly List<PreparedLod> PreparedScratch = new();
    static int[] groupInc = Array.Empty<int>();

    struct PreparedLod
    {
        public MyLod Lod;
        public MyLodInstance LodInstance;
        public MyInstanceLodState StateId;
        public MyInstance Instance;
    }

    readonly struct BlendOverride
    {
        public readonly IntPtr Source;
        public readonly BlendState State;

        public BlendOverride(IntPtr source, BlendState state)
        {
            Source = source;
            State = state;
        }
    }

    sealed class VelocityCbPool
    {
        public readonly int Id;
        public readonly List<IConstantBuffer> Buffers = new();
        public int Cursor;

        public VelocityCbPool(int id)
        {
            Id = id;
        }
    }
    static bool loggedError;
    static bool historyValidThisFrame;
    static int resourcesReady;
    static int proxiesThisFrame;
    static int proxiesWithPrevThisFrame;
    static int hookCallsThisFrame;
    static int voxelSkipThisFrame;
    static int idZeroThisFrame;
    static int histMissThisFrame;
    static int stage2ValidThisFrame;
    static int stage2GroupBindsThisFrame;
    static int stage2PrepareThisFrame;
    static int stage2SlotsThisFrame;
    static int stage2DrewThisFrame;
    static int stage2LastInstanceBaseThisFrame;
    static int stage2MovingSlotsThisFrame;
    static int stage2MaxMotionMmThisFrame;
    static int stage2UploadsThisFrame;
    static int stage2UploadDeferredThisFrame;
    static int mrtBindsThisFrame;
    static int velocityCbBindsThisFrame;
    static int velocityCbWholeBindsThisFrame;
    static int immutableProbeCbBindsThisFrame;
    static int velocityCbPoolPeakThisFrame;
    static int lastProbeModeThisFrame;
    static int passEndClearsThisFrame;
    static int drawBoundaryDrawsThisFrame;
    static int drawBoundaryChecksThisFrame;
    static int drawBoundaryTarget3ThisFrame;
    static int drawBoundaryTrackedTarget3ThisFrame;
    static int passTargetRestoresThisFrame;
    static int drawBoundaryWritableThisFrame;
    static int drawBoundaryMaskedThisFrame;
    static int drawBoundaryBlendOverridesThisFrame;
    static int drawBoundaryBlendFailuresThisFrame;
    static int drawBoundaryRebindsThisFrame;
    static int drawBoundaryRebindTarget3ThisFrame;
    static int drawBoundaryVelocityCbRebindsThisFrame;
    static int drawBoundaryVelocityCbMissingThisFrame;
    static int drawBoundaryPixelChecksThisFrame;
    static int drawBoundaryCurrentPixelsThisFrame;
    static int drawBoundaryGBufferPixelsThisFrame;
    static int drawBoundaryVelocityPixelsThisFrame;
    static int drawBoundaryDepthPixelsThisFrame;
    static int drawBoundaryRetiredPixelsThisFrame;
    static int drawBoundaryVertexChecksThisFrame;
    static int drawBoundaryCurrentVerticesThisFrame;
    static int drawBoundaryGBufferVerticesThisFrame;
    static int drawBoundaryVelocityVerticesThisFrame;
    static int drawBoundaryDepthVerticesThisFrame;
    static int drawBoundaryRetiredVerticesThisFrame;
    static int drawBoundaryVerifiedVertexFlowThisFrame;
    static int drawBoundaryVerifiedPixelFlowThisFrame;
    static int resolvedPairsThisFrame;
    static int resolvedVerifiedPairsThisFrame;
    static int resolvedDepthPairsThisFrame;
    static int resolvedOtherPairsThisFrame;
    static int nativeDrawStateQueriesThisFrame;
    static int nativeDrawStateDeferredThisFrame;
    static int nativeDrawStateVsb6MatchThisFrame;
    static int nativeDrawStateVsb6NullThisFrame;
    static int nativeDrawStateVsb6MismatchThisFrame;
    static int nativeDrawStatePsb7MatchThisFrame;
    static int nativeDrawStatePsb7NullThisFrame;
    static int nativeDrawStatePsb7MismatchThisFrame;
    static int nativeDrawStateVsMatchThisFrame;
    static int nativeDrawStateVsNullThisFrame;
    static int nativeDrawStateVsMismatchThisFrame;
    static int nativeDrawStatePsMatchThisFrame;
    static int nativeDrawStatePsNullThisFrame;
    static int nativeDrawStatePsMismatchThisFrame;
    static int nativeDrawStateTarget3MatchThisFrame;
    static int nativeDrawStateTarget3NullThisFrame;
    static int nativeDrawStateTarget3MismatchThisFrame;
    static int nativeDrawStateTarget7MatchThisFrame;
    static int nativeDrawStateTarget7NullThisFrame;
    static int nativeDrawStateTarget7MismatchThisFrame;
    static int nativeDrawStateBlendMatchThisFrame;
    static int nativeDrawStateBlendNullThisFrame;
    static int nativeDrawStateBlendMismatchThisFrame;
    static int nativeDrawStateBlendIndependentThisFrame;
    static int nativeDrawStateBlendTarget3WritableThisFrame;
    static int nativeDrawStateBlendTarget7WritableThisFrame;
    static int nativeDrawStateErrorsThisFrame;
    static int pixelShaderLookupsThisFrame;
    static int pixelShaderLookupCurrentThisFrame;
    static int pixelShaderLookupGBufferThisFrame;
    static int pixelShaderLookupVelocityThisFrame;
    static int pixelShaderLookupDepthThisFrame;
    static int pixelShaderLookupVerifiedThisFrame;
    static int pixelShaderLookupRetiredThisFrame;
    static int vertexShaderLookupsThisFrame;
    static int vertexShaderLookupCurrentThisFrame;
    static int vertexShaderLookupGBufferThisFrame;
    static int vertexShaderLookupVelocityThisFrame;
    static int vertexShaderLookupDepthThisFrame;
    static int vertexShaderLookupVerifiedThisFrame;
    static int vertexShaderLookupRetiredThisFrame;
    static int worldGBufferSeen;
    [ThreadStatic] static bool acceptColorSlots;

    /// <summary>Old-pipeline GBuffer draws last completed frame (Status).</summary>
    internal static int OldPipelineProxiesLastFrame { get; private set; }

    /// <summary>Those draws that bound a previous world last completed frame.</summary>
    internal static int OldPipelinePrevWorldLastFrame { get; private set; }

    internal static int OldPipelineHookLastFrame { get; private set; }
    internal static int OldPipelineVoxelSkipLastFrame { get; private set; }
    internal static int OldPipelineIdZeroLastFrame { get; private set; }
    internal static int OldPipelineHistMissLastFrame { get; private set; }
    internal static int Stage2PackedLastFrame { get; private set; }
    internal static int Stage2ValidLastFrame { get; private set; }
    internal static int Stage2GroupBindsLastFrame { get; private set; }
    internal static int Stage2PrepareLastFrame { get; private set; }
    internal static int Stage2DrewLastFrame { get; private set; }
    internal static int Stage2LastInstanceBase { get; private set; }
    internal static int Stage2MovingSlotsLastFrame { get; private set; }
    internal static int Stage2MaxMotionMmLastFrame { get; private set; }
    internal static int Stage2UploadsLastFrame { get; private set; }
    internal static bool Stage2UploadWasDeferred { get; private set; }
    internal static int MrtBindsLastFrame { get; private set; }
    internal static int VelocityCbBindsLastFrame { get; private set; }
    internal static int VelocityCbWholeBindsLastFrame { get; private set; }
    internal static int ImmutableProbeCbBindsLastFrame { get; private set; }
    internal static string MrtWriteProbeReadbackStatus
    {
        get
        {
            var state = Volatile.Read(ref mrtWriteProbeReadbackState);
            if (state == 2)
                return "VS 2/" + Volatile.Read(ref mrtWriteProbeReadbackValue) +
                    " PS (8,1)/" + (Volatile.Read(ref mrtWritePixelProbeReadbackValue) ?? "?");
            if (state == -1)
                return "failed (" + (mrtWriteProbeReadbackError ?? "unknown") + ")";
            return state == 1 ? "pending" : "not read";
        }
    }
    internal static int VelocityCbPoolPeakLastFrame { get; private set; }
    internal static int VelocityCbPoolCapacity => Volatile.Read(ref velocityCbPoolCapacity);
    internal static VelocityProbe LastBoundProbe { get; private set; }
    internal static int PassEndClearsLastFrame { get; private set; }
    internal static int DrawBoundaryDrawsLastFrame { get; private set; }
    internal static int DrawBoundaryChecksLastFrame { get; private set; }
    internal static int DrawBoundaryTarget3LastFrame { get; private set; }
    internal static int DrawBoundaryTrackedTarget3LastFrame { get; private set; }
    internal static int PassTargetRestoresLastFrame { get; private set; }
    internal static int DrawBoundaryWritableLastFrame { get; private set; }
    internal static int DrawBoundaryMaskedLastFrame { get; private set; }
    internal static int DrawBoundaryBlendOverridesLastFrame { get; private set; }
    internal static int DrawBoundaryBlendFailuresLastFrame { get; private set; }
    internal static int DrawBoundaryRebindsLastFrame { get; private set; }
    internal static int DrawBoundaryRebindTarget3LastFrame { get; private set; }
    internal static int DrawBoundaryVelocityCbRebindsLastFrame { get; private set; }
    internal static int DrawBoundaryVelocityCbMissingLastFrame { get; private set; }
    internal static int DrawBoundaryPixelChecksLastFrame { get; private set; }
    internal static int DrawBoundaryCurrentPixelsLastFrame { get; private set; }
    internal static int DrawBoundaryGBufferPixelsLastFrame { get; private set; }
    internal static int DrawBoundaryVelocityPixelsLastFrame { get; private set; }
    internal static int DrawBoundaryDepthPixelsLastFrame { get; private set; }
    internal static int DrawBoundaryRetiredPixelsLastFrame { get; private set; }
    internal static int DrawBoundaryVertexChecksLastFrame { get; private set; }
    internal static int DrawBoundaryCurrentVerticesLastFrame { get; private set; }
    internal static int DrawBoundaryGBufferVerticesLastFrame { get; private set; }
    internal static int DrawBoundaryVelocityVerticesLastFrame { get; private set; }
    internal static int DrawBoundaryDepthVerticesLastFrame { get; private set; }
    internal static int DrawBoundaryRetiredVerticesLastFrame { get; private set; }
    internal static int DrawBoundaryVerifiedVertexFlowLastFrame { get; private set; }
    internal static int DrawBoundaryVerifiedPixelFlowLastFrame { get; private set; }
    internal static int ResolvedPairsLastFrame { get; private set; }
    internal static int ResolvedVerifiedPairsLastFrame { get; private set; }
    internal static int ResolvedDepthPairsLastFrame { get; private set; }
    internal static int ResolvedOtherPairsLastFrame { get; private set; }
    internal static int NativeDrawStateQueriesLastFrame { get; private set; }
    internal static int NativeDrawStateDeferredLastFrame { get; private set; }
    internal static int NativeDrawStateVsb6MatchLastFrame { get; private set; }
    internal static int NativeDrawStateVsb6NullLastFrame { get; private set; }
    internal static int NativeDrawStateVsb6MismatchLastFrame { get; private set; }
    internal static int NativeDrawStatePsb7MatchLastFrame { get; private set; }
    internal static int NativeDrawStatePsb7NullLastFrame { get; private set; }
    internal static int NativeDrawStatePsb7MismatchLastFrame { get; private set; }
    internal static int NativeDrawStateVsMatchLastFrame { get; private set; }
    internal static int NativeDrawStateVsNullLastFrame { get; private set; }
    internal static int NativeDrawStateVsMismatchLastFrame { get; private set; }
    internal static int NativeDrawStatePsMatchLastFrame { get; private set; }
    internal static int NativeDrawStatePsNullLastFrame { get; private set; }
    internal static int NativeDrawStatePsMismatchLastFrame { get; private set; }
    internal static int NativeDrawStateTarget3MatchLastFrame { get; private set; }
    internal static int NativeDrawStateTarget3NullLastFrame { get; private set; }
    internal static int NativeDrawStateTarget3MismatchLastFrame { get; private set; }
    internal static int NativeDrawStateTarget7MatchLastFrame { get; private set; }
    internal static int NativeDrawStateTarget7NullLastFrame { get; private set; }
    internal static int NativeDrawStateTarget7MismatchLastFrame { get; private set; }
    internal static int NativeDrawStateBlendMatchLastFrame { get; private set; }
    internal static int NativeDrawStateBlendNullLastFrame { get; private set; }
    internal static int NativeDrawStateBlendMismatchLastFrame { get; private set; }
    internal static int NativeDrawStateBlendIndependentLastFrame { get; private set; }
    internal static int NativeDrawStateBlendTarget3WritableLastFrame { get; private set; }
    internal static int NativeDrawStateBlendTarget7WritableLastFrame { get; private set; }
    internal static int NativeDrawStateErrorsLastFrame { get; private set; }
    internal static int PixelShaderLookupsLastFrame { get; private set; }
    internal static int PixelShaderLookupCurrentLastFrame { get; private set; }
    internal static int PixelShaderLookupGBufferLastFrame { get; private set; }
    internal static int PixelShaderLookupVelocityLastFrame { get; private set; }
    internal static int PixelShaderLookupDepthLastFrame { get; private set; }
    internal static int PixelShaderLookupVerifiedLastFrame { get; private set; }
    internal static int PixelShaderLookupRetiredLastFrame { get; private set; }
    internal static int VertexShaderLookupsLastFrame { get; private set; }
    internal static int VertexShaderLookupCurrentLastFrame { get; private set; }
    internal static int VertexShaderLookupGBufferLastFrame { get; private set; }
    internal static int VertexShaderLookupVelocityLastFrame { get; private set; }
    internal static int VertexShaderLookupDepthLastFrame { get; private set; }
    internal static int VertexShaderLookupVerifiedLastFrame { get; private set; }
    internal static int VertexShaderLookupRetiredLastFrame { get; private set; }
    internal static string TargetContractStatus => Volatile.Read(ref targetContractStatus) ?? "unknown";
    internal static string AuditTargetStatus
    {
        get
        {
            lock (Gate)
                return auditTarget == null
                    ? "not allocated"
                    : auditTarget.Size.X + "x" + auditTarget.Size.Y + "/s" + targetSamples +
                      " target7=" + NativeId(auditTarget);
        }
    }
    internal static string Target3CheckpointStatus
    {
        get
        {
            lock (Gate)
            {
                var selected = Config.Current?.Target3Checkpoint ?? Target3Checkpoint.Live;
                var live = NativeId(target);
                if (selected == Target3Checkpoint.Live)
                    return "Live target=" + live + " debug=" + NativeId(checkpointDebugSourceNative);

                var capture = capturedCheckpoint == selected
                    ? capturedCheckpoint + " frame=" + checkpointCaptureFrame + " count=" + checkpointCaptureCount
                    : "waiting (last=" + capturedCheckpoint + ")";
                if (!string.IsNullOrEmpty(checkpointLastError))
                    capture += " error=" + checkpointLastError;
                return selected + " captured=" + capture + " src=" + live +
                    " snapshot=" + NativeId(checkpointTarget) +
                    " audit-snapshot=" + NativeId(auditCheckpointTarget) +
                    " debug=" + NativeId(checkpointDebugSourceNative);
            }
        }
    }
    internal static string Target3CheckpointCompactStatus
    {
        get
        {
            lock (Gate)
            {
                var selected = Config.Current?.Target3Checkpoint ?? Target3Checkpoint.Live;
                if (selected == Target3Checkpoint.Live)
                    return "Live";
                if (capturedCheckpoint != selected)
                    return selected + " waiting (last=" + capturedCheckpoint + ")";
                return selected + " captured#" + checkpointCaptureCount + " frame=" + checkpointCaptureFrame;
            }
        }
    }

    /// <summary>
    /// True after a real GBuffer pass has begun in the current world. This is
    /// stricter than <c>MySession.Static != null</c>, which is already true
    /// during loading while it is still safe to refresh resident shaders.
    /// </summary>
    internal static bool WorldGBufferSeen => Volatile.Read(ref worldGBufferSeen) != 0;

    [StructLayout(LayoutKind.Sequential, Size = PrevStride)]
    struct PrevInstance
    {
        public Vector4 Col0;
        public Vector4 Col1;
        public Vector4 Col2;
        public Vector4 Flags;
    }

    [StructLayout(LayoutKind.Sequential, Size = ConstantLayoutBytes)]
    struct Constants
    {
        public Matrix UnjitteredViewProj;
        public Matrix PrevViewProj;
        public Vector2 RenderSize;
        public Vector2 InvRenderSize;
        public uint PrevCount;
        public uint HasHistory;
        public uint HasPrevWorld;
        public uint BoneCount;
        public Vector4 PrevRow0;
        public Vector4 PrevRow1;
        public Vector4 PrevRow2;
        public uint InstanceBase;
        public uint ProbeMode;
        public Vector2 Padding;
        public Vector3 CameraDelta;
    }

    [StructLayout(LayoutKind.Sequential, Size = PixelProbeLayoutBytes)]
    struct PixelProbeConstants
    {
        public Vector4 Value;
    }

    static bool InjectionWanted =>
        Enabled && (Config.Current?.VelocitySource ?? VelocitySource.GBuffer) == VelocitySource.GBuffer;

    static bool AuditProofWanted =>
        (Config.Current?.DebugBuffer ?? DebugBuffer.Off) == DebugBuffer.VelocityPipelineAudit;

    public static bool ShouldPublish =>
        InjectionWanted && target != null && IsLive;

    internal static void BeginFrame()
    {
        // DrawGameScene Prefix runs before prepare/render workers start. Reset
        // every context's cursor once here, not per pass, so a buffer is never
        // MapDiscarded twice while this frame's command lists are recorded.
        lock (VelocityCbGate)
        {
            foreach (var pool in VelocityCbPools.Values)
                pool.Cursor = 0;
        }

        // Counters publish on EndFrame so a later empty DrawGameScene
        // (settings / pause) cannot wipe last-drawn Stage 2 numbers.
    }

    /// <summary>
    /// Publish last-drawn counts. Empty frames (menu, no GBuffer) keep the
    /// previous non-zero snapshot so Status is readable from the overlay.
    /// </summary>
    internal static void EndFrame()
    {
        var hook = Interlocked.Exchange(ref hookCallsThisFrame, 0);
        var voxel = Interlocked.Exchange(ref voxelSkipThisFrame, 0);
        var id0 = Interlocked.Exchange(ref idZeroThisFrame, 0);
        var miss = Interlocked.Exchange(ref histMissThisFrame, 0);
        var proxies = Interlocked.Exchange(ref proxiesThisFrame, 0);
        var prevWorld = Interlocked.Exchange(ref proxiesWithPrevThisFrame, 0);
        if (hook > 0 || proxies > 0)
        {
            OldPipelineHookLastFrame = hook;
            OldPipelineVoxelSkipLastFrame = voxel;
            OldPipelineIdZeroLastFrame = id0;
            OldPipelineHistMissLastFrame = miss;
            OldPipelineProxiesLastFrame = proxies;
            OldPipelinePrevWorldLastFrame = prevWorld;
        }

        var prepare = Interlocked.Exchange(ref stage2PrepareThisFrame, 0);
        var groups = Interlocked.Exchange(ref stage2GroupBindsThisFrame, 0);
        var drew = Interlocked.Exchange(ref stage2DrewThisFrame, 0);
        var valid = Interlocked.Exchange(ref stage2ValidThisFrame, 0);
        var slots = Interlocked.Exchange(ref stage2SlotsThisFrame, 0);
        var moving = Interlocked.Exchange(ref stage2MovingSlotsThisFrame, 0);
        var maxMotionMm = Interlocked.Exchange(ref stage2MaxMotionMmThisFrame, 0);
        var uploads = Interlocked.Exchange(ref stage2UploadsThisFrame, 0);
        var uploadDeferred = Interlocked.Exchange(ref stage2UploadDeferredThisFrame, 0);
        var mrtBinds = Interlocked.Exchange(ref mrtBindsThisFrame, 0);
        var velocityCbBinds = Interlocked.Exchange(ref velocityCbBindsThisFrame, 0);
        var velocityCbWholeBinds = Interlocked.Exchange(ref velocityCbWholeBindsThisFrame, 0);
        var immutableProbeCbBinds = Interlocked.Exchange(ref immutableProbeCbBindsThisFrame, 0);
        var velocityCbPoolPeak = Interlocked.Exchange(ref velocityCbPoolPeakThisFrame, 0);
        var lastProbeMode = Interlocked.Exchange(ref lastProbeModeThisFrame, 0);
        var passEndClears = Interlocked.Exchange(ref passEndClearsThisFrame, 0);
        var boundaryDraws = Interlocked.Exchange(ref drawBoundaryDrawsThisFrame, 0);
        var boundaryChecks = Interlocked.Exchange(ref drawBoundaryChecksThisFrame, 0);
        var boundaryTarget3 = Interlocked.Exchange(ref drawBoundaryTarget3ThisFrame, 0);
        var boundaryTrackedTarget3 = Interlocked.Exchange(ref drawBoundaryTrackedTarget3ThisFrame, 0);
        var passTargetRestores = Interlocked.Exchange(ref passTargetRestoresThisFrame, 0);
        var boundaryWritable = Interlocked.Exchange(ref drawBoundaryWritableThisFrame, 0);
        var boundaryMasked = Interlocked.Exchange(ref drawBoundaryMaskedThisFrame, 0);
        var boundaryBlendOverrides = Interlocked.Exchange(ref drawBoundaryBlendOverridesThisFrame, 0);
        var boundaryBlendFailures = Interlocked.Exchange(ref drawBoundaryBlendFailuresThisFrame, 0);
        var boundaryRebinds = Interlocked.Exchange(ref drawBoundaryRebindsThisFrame, 0);
        var boundaryRebindTarget3 = Interlocked.Exchange(ref drawBoundaryRebindTarget3ThisFrame, 0);
        var boundaryVelocityCbRebinds = Interlocked.Exchange(ref drawBoundaryVelocityCbRebindsThisFrame, 0);
        var boundaryVelocityCbMissing = Interlocked.Exchange(ref drawBoundaryVelocityCbMissingThisFrame, 0);
        var boundaryPixelChecks = Interlocked.Exchange(ref drawBoundaryPixelChecksThisFrame, 0);
        var boundaryCurrentPixels = Interlocked.Exchange(ref drawBoundaryCurrentPixelsThisFrame, 0);
        var boundaryGBufferPixels = Interlocked.Exchange(ref drawBoundaryGBufferPixelsThisFrame, 0);
        var boundaryVelocityPixels = Interlocked.Exchange(ref drawBoundaryVelocityPixelsThisFrame, 0);
        var boundaryDepthPixels = Interlocked.Exchange(ref drawBoundaryDepthPixelsThisFrame, 0);
        var boundaryRetiredPixels = Interlocked.Exchange(ref drawBoundaryRetiredPixelsThisFrame, 0);
        var boundaryVertexChecks = Interlocked.Exchange(ref drawBoundaryVertexChecksThisFrame, 0);
        var boundaryCurrentVertices = Interlocked.Exchange(ref drawBoundaryCurrentVerticesThisFrame, 0);
        var boundaryGBufferVertices = Interlocked.Exchange(ref drawBoundaryGBufferVerticesThisFrame, 0);
        var boundaryVelocityVertices = Interlocked.Exchange(ref drawBoundaryVelocityVerticesThisFrame, 0);
        var boundaryDepthVertices = Interlocked.Exchange(ref drawBoundaryDepthVerticesThisFrame, 0);
        var boundaryRetiredVertices = Interlocked.Exchange(ref drawBoundaryRetiredVerticesThisFrame, 0);
        var boundaryVerifiedVertexFlow = Interlocked.Exchange(ref drawBoundaryVerifiedVertexFlowThisFrame, 0);
        var boundaryVerifiedPixelFlow = Interlocked.Exchange(ref drawBoundaryVerifiedPixelFlowThisFrame, 0);
        var resolvedPairs = Interlocked.Exchange(ref resolvedPairsThisFrame, 0);
        var resolvedVerifiedPairs = Interlocked.Exchange(ref resolvedVerifiedPairsThisFrame, 0);
        var resolvedDepthPairs = Interlocked.Exchange(ref resolvedDepthPairsThisFrame, 0);
        var resolvedOtherPairs = Interlocked.Exchange(ref resolvedOtherPairsThisFrame, 0);
        var nativeDrawStateQueries = Interlocked.Exchange(ref nativeDrawStateQueriesThisFrame, 0);
        var nativeDrawStateDeferred = Interlocked.Exchange(ref nativeDrawStateDeferredThisFrame, 0);
        var nativeDrawStateVsb6Match = Interlocked.Exchange(ref nativeDrawStateVsb6MatchThisFrame, 0);
        var nativeDrawStateVsb6Null = Interlocked.Exchange(ref nativeDrawStateVsb6NullThisFrame, 0);
        var nativeDrawStateVsb6Mismatch = Interlocked.Exchange(ref nativeDrawStateVsb6MismatchThisFrame, 0);
        var nativeDrawStatePsb7Match = Interlocked.Exchange(ref nativeDrawStatePsb7MatchThisFrame, 0);
        var nativeDrawStatePsb7Null = Interlocked.Exchange(ref nativeDrawStatePsb7NullThisFrame, 0);
        var nativeDrawStatePsb7Mismatch = Interlocked.Exchange(ref nativeDrawStatePsb7MismatchThisFrame, 0);
        var nativeDrawStateVsMatch = Interlocked.Exchange(ref nativeDrawStateVsMatchThisFrame, 0);
        var nativeDrawStateVsNull = Interlocked.Exchange(ref nativeDrawStateVsNullThisFrame, 0);
        var nativeDrawStateVsMismatch = Interlocked.Exchange(ref nativeDrawStateVsMismatchThisFrame, 0);
        var nativeDrawStatePsMatch = Interlocked.Exchange(ref nativeDrawStatePsMatchThisFrame, 0);
        var nativeDrawStatePsNull = Interlocked.Exchange(ref nativeDrawStatePsNullThisFrame, 0);
        var nativeDrawStatePsMismatch = Interlocked.Exchange(ref nativeDrawStatePsMismatchThisFrame, 0);
        var nativeDrawStateTarget3Match = Interlocked.Exchange(ref nativeDrawStateTarget3MatchThisFrame, 0);
        var nativeDrawStateTarget3Null = Interlocked.Exchange(ref nativeDrawStateTarget3NullThisFrame, 0);
        var nativeDrawStateTarget3Mismatch = Interlocked.Exchange(ref nativeDrawStateTarget3MismatchThisFrame, 0);
        var nativeDrawStateTarget7Match = Interlocked.Exchange(ref nativeDrawStateTarget7MatchThisFrame, 0);
        var nativeDrawStateTarget7Null = Interlocked.Exchange(ref nativeDrawStateTarget7NullThisFrame, 0);
        var nativeDrawStateTarget7Mismatch = Interlocked.Exchange(ref nativeDrawStateTarget7MismatchThisFrame, 0);
        var nativeDrawStateBlendMatch = Interlocked.Exchange(ref nativeDrawStateBlendMatchThisFrame, 0);
        var nativeDrawStateBlendNull = Interlocked.Exchange(ref nativeDrawStateBlendNullThisFrame, 0);
        var nativeDrawStateBlendMismatch = Interlocked.Exchange(ref nativeDrawStateBlendMismatchThisFrame, 0);
        var nativeDrawStateBlendIndependent = Interlocked.Exchange(ref nativeDrawStateBlendIndependentThisFrame, 0);
        var nativeDrawStateBlendTarget3Writable = Interlocked.Exchange(ref nativeDrawStateBlendTarget3WritableThisFrame, 0);
        var nativeDrawStateBlendTarget7Writable = Interlocked.Exchange(ref nativeDrawStateBlendTarget7WritableThisFrame, 0);
        var nativeDrawStateErrors = Interlocked.Exchange(ref nativeDrawStateErrorsThisFrame, 0);
        var pixelLookups = Interlocked.Exchange(ref pixelShaderLookupsThisFrame, 0);
        var lookupCurrent = Interlocked.Exchange(ref pixelShaderLookupCurrentThisFrame, 0);
        var lookupGBuffer = Interlocked.Exchange(ref pixelShaderLookupGBufferThisFrame, 0);
        var lookupVelocity = Interlocked.Exchange(ref pixelShaderLookupVelocityThisFrame, 0);
        var lookupDepth = Interlocked.Exchange(ref pixelShaderLookupDepthThisFrame, 0);
        var lookupVerified = Interlocked.Exchange(ref pixelShaderLookupVerifiedThisFrame, 0);
        var lookupRetired = Interlocked.Exchange(ref pixelShaderLookupRetiredThisFrame, 0);
        var vertexLookups = Interlocked.Exchange(ref vertexShaderLookupsThisFrame, 0);
        var vertexLookupCurrent = Interlocked.Exchange(ref vertexShaderLookupCurrentThisFrame, 0);
        var vertexLookupGBuffer = Interlocked.Exchange(ref vertexShaderLookupGBufferThisFrame, 0);
        var vertexLookupVelocity = Interlocked.Exchange(ref vertexShaderLookupVelocityThisFrame, 0);
        var vertexLookupDepth = Interlocked.Exchange(ref vertexShaderLookupDepthThisFrame, 0);
        var vertexLookupVerified = Interlocked.Exchange(ref vertexShaderLookupVerifiedThisFrame, 0);
        var vertexLookupRetired = Interlocked.Exchange(ref vertexShaderLookupRetiredThisFrame, 0);
        // Per-field sticky: a pause/menu Prepare still packs t15 (slots>0) and
        // used to overwrite last-drawn groups=0. That is not a dead bind.
        if (prepare > 0)
            Stage2PrepareLastFrame = prepare;
        if (groups > 0)
        {
            Stage2GroupBindsLastFrame = groups;
            Stage2LastInstanceBase = Volatile.Read(ref stage2LastInstanceBaseThisFrame);
        }
        if (drew > 0)
            Stage2DrewLastFrame = drew;
        if (valid > 0)
            Stage2ValidLastFrame = valid;
        if (slots > 0)
        {
            Stage2PackedLastFrame = slots;
            if (moving > 0)
            {
                Stage2MovingSlotsLastFrame = moving;
                Stage2MaxMotionMmLastFrame = maxMotionMm;
            }
        }
        else if (Stage2PackedLastFrame <= 0)
            Stage2PackedLastFrame = Volatile.Read(ref prevCount);
        if (uploads > 0)
        {
            Stage2UploadsLastFrame = uploads;
            Stage2UploadWasDeferred = uploadDeferred != 0;
        }
        if (mrtBinds > 0)
            MrtBindsLastFrame = mrtBinds;
        if (velocityCbBinds > 0)
        {
            VelocityCbBindsLastFrame = velocityCbBinds;
            VelocityCbWholeBindsLastFrame = velocityCbWholeBinds;
            ImmutableProbeCbBindsLastFrame = immutableProbeCbBinds;
            VelocityCbPoolPeakLastFrame = velocityCbPoolPeak;
            LastBoundProbe = (VelocityProbe)lastProbeMode;
        }
        if (passEndClears > 0)
            PassEndClearsLastFrame = passEndClears;
        if (boundaryDraws > 0)
        {
            DrawBoundaryDrawsLastFrame = boundaryDraws;
            DrawBoundaryRebindsLastFrame = boundaryRebinds;
            DrawBoundaryRebindTarget3LastFrame = boundaryRebindTarget3;
            DrawBoundaryVelocityCbRebindsLastFrame = boundaryVelocityCbRebinds;
            DrawBoundaryVelocityCbMissingLastFrame = boundaryVelocityCbMissing;
            DrawBoundaryWritableLastFrame = boundaryWritable;
            DrawBoundaryMaskedLastFrame = boundaryMasked;
            DrawBoundaryBlendOverridesLastFrame = boundaryBlendOverrides;
            DrawBoundaryBlendFailuresLastFrame = boundaryBlendFailures;
            PassTargetRestoresLastFrame = passTargetRestores;
            DrawBoundaryPixelChecksLastFrame = boundaryPixelChecks;
            DrawBoundaryCurrentPixelsLastFrame = boundaryCurrentPixels;
            DrawBoundaryGBufferPixelsLastFrame = boundaryGBufferPixels;
            DrawBoundaryVelocityPixelsLastFrame = boundaryVelocityPixels;
            DrawBoundaryDepthPixelsLastFrame = boundaryDepthPixels;
            DrawBoundaryRetiredPixelsLastFrame = boundaryRetiredPixels;
            DrawBoundaryVertexChecksLastFrame = boundaryVertexChecks;
            DrawBoundaryCurrentVerticesLastFrame = boundaryCurrentVertices;
            DrawBoundaryGBufferVerticesLastFrame = boundaryGBufferVertices;
            DrawBoundaryVelocityVerticesLastFrame = boundaryVelocityVertices;
            DrawBoundaryDepthVerticesLastFrame = boundaryDepthVertices;
            DrawBoundaryRetiredVerticesLastFrame = boundaryRetiredVertices;
            DrawBoundaryVerifiedVertexFlowLastFrame = boundaryVerifiedVertexFlow;
            DrawBoundaryVerifiedPixelFlowLastFrame = boundaryVerifiedPixelFlow;
            ResolvedPairsLastFrame = resolvedPairs;
            ResolvedVerifiedPairsLastFrame = resolvedVerifiedPairs;
            ResolvedDepthPairsLastFrame = resolvedDepthPairs;
            ResolvedOtherPairsLastFrame = resolvedOtherPairs;
            NativeDrawStateQueriesLastFrame = nativeDrawStateQueries;
            NativeDrawStateDeferredLastFrame = nativeDrawStateDeferred;
            NativeDrawStateVsb6MatchLastFrame = nativeDrawStateVsb6Match;
            NativeDrawStateVsb6NullLastFrame = nativeDrawStateVsb6Null;
            NativeDrawStateVsb6MismatchLastFrame = nativeDrawStateVsb6Mismatch;
            NativeDrawStatePsb7MatchLastFrame = nativeDrawStatePsb7Match;
            NativeDrawStatePsb7NullLastFrame = nativeDrawStatePsb7Null;
            NativeDrawStatePsb7MismatchLastFrame = nativeDrawStatePsb7Mismatch;
            NativeDrawStateVsMatchLastFrame = nativeDrawStateVsMatch;
            NativeDrawStateVsNullLastFrame = nativeDrawStateVsNull;
            NativeDrawStateVsMismatchLastFrame = nativeDrawStateVsMismatch;
            NativeDrawStatePsMatchLastFrame = nativeDrawStatePsMatch;
            NativeDrawStatePsNullLastFrame = nativeDrawStatePsNull;
            NativeDrawStatePsMismatchLastFrame = nativeDrawStatePsMismatch;
            NativeDrawStateTarget3MatchLastFrame = nativeDrawStateTarget3Match;
            NativeDrawStateTarget3NullLastFrame = nativeDrawStateTarget3Null;
            NativeDrawStateTarget3MismatchLastFrame = nativeDrawStateTarget3Mismatch;
            NativeDrawStateTarget7MatchLastFrame = nativeDrawStateTarget7Match;
            NativeDrawStateTarget7NullLastFrame = nativeDrawStateTarget7Null;
            NativeDrawStateTarget7MismatchLastFrame = nativeDrawStateTarget7Mismatch;
            NativeDrawStateBlendMatchLastFrame = nativeDrawStateBlendMatch;
            NativeDrawStateBlendNullLastFrame = nativeDrawStateBlendNull;
            NativeDrawStateBlendMismatchLastFrame = nativeDrawStateBlendMismatch;
            NativeDrawStateBlendIndependentLastFrame = nativeDrawStateBlendIndependent;
            NativeDrawStateBlendTarget3WritableLastFrame = nativeDrawStateBlendTarget3Writable;
            NativeDrawStateBlendTarget7WritableLastFrame = nativeDrawStateBlendTarget7Writable;
            NativeDrawStateErrorsLastFrame = nativeDrawStateErrors;
            PixelShaderLookupsLastFrame = pixelLookups;
            PixelShaderLookupCurrentLastFrame = lookupCurrent;
            PixelShaderLookupGBufferLastFrame = lookupGBuffer;
            PixelShaderLookupVelocityLastFrame = lookupVelocity;
            PixelShaderLookupDepthLastFrame = lookupDepth;
            PixelShaderLookupVerifiedLastFrame = lookupVerified;
            PixelShaderLookupRetiredLastFrame = lookupRetired;
            VertexShaderLookupsLastFrame = vertexLookups;
            VertexShaderLookupCurrentLastFrame = vertexLookupCurrent;
            VertexShaderLookupGBufferLastFrame = vertexLookupGBuffer;
            VertexShaderLookupVelocityLastFrame = vertexLookupVelocity;
            VertexShaderLookupDepthLastFrame = vertexLookupDepth;
            VertexShaderLookupVerifiedLastFrame = vertexLookupVerified;
            VertexShaderLookupRetiredLastFrame = vertexLookupRetired;
        }
        if (boundaryChecks > 0)
        {
            DrawBoundaryChecksLastFrame = boundaryChecks;
            DrawBoundaryTarget3LastFrame = boundaryTarget3;
            DrawBoundaryTrackedTarget3LastFrame = boundaryTrackedTarget3;
        }
    }

    /// <summary>
    /// Keen writes NonVoxel when <c>NonVoxel.IsValid</c>, else voxel when
    /// <c>Voxel.IsValid</c>. Default <c>VoxelLodSize</c> is 0, and VRage
    /// <c>float.IsValid</c> is true for 0 — skip only true voxels.
    /// </summary>
    internal static bool IsVoxelProxy(MyRenderableProxy proxy) =>
        proxy != null &&
        proxy.VoxelCommonObjectData.IsValid &&
        !proxy.NonVoxelObjectData.IsValid;

    /// <summary>
    /// GBuffer-only <c>MyPreparePass&lt;Color,Color&gt;.DoWork</c>. Env/forward
    /// uses a different closed generic, so this flag never sees those slots.
    /// </summary>
    public static void BeginGBufferColorPrepare()
    {
        acceptColorSlots = InjectionWanted;
        if (!acceptColorSlots)
            return;
        Interlocked.Increment(ref stage2PrepareThisFrame);
    }

    /// <summary>
    /// One t15 slot from Keen's GBuffer <c>AddInstanceIntoInstanceElements</c>
    /// (same offset as the instance VB).
    /// </summary>
    public static void PackColorPrepareSlot(int bufferOffset, MyInstance instance)
    {
        if (!acceptColorSlots || instance == null)
            return;
        var hasCam = CameraVelocityPass.TryGetPrevCamera(out var prevCam);
        EnsureCpuPrev(bufferOffset + 1);
        PackSlot(bufferOffset, instance, hasCam, prevCam);
        if (bufferOffset + 1 > prevCount)
            prevCount = bufferOffset + 1;
    }

    public static void EndGBufferColorPrepare()
    {
        if (!acceptColorSlots)
            return;
        acceptColorSlots = false;
        var packedValid = 0;
        var packed = Math.Max(prevCount, 0);
        for (var i = 0; i < packed && i < cpuPrev.Length; i++)
        {
            if (cpuPrev[i].Flags.X > 0.5f)
                packedValid++;
        }
        Volatile.Write(ref stage2ValidThisFrame, packedValid);
        Volatile.Write(ref stage2SlotsThisFrame, packed);
        // CPU-only on the parallel prepare worker. The GPU upload is recorded
        // later by the Stage 2 GBuffer's own deferred render context.
    }

    static void EnsureCpuPrev(int need)
    {
        need = Math.Max(need, 1);
        if (cpuPrev.Length >= need)
            return;
        var next = new PrevInstance[need];
        Array.Copy(cpuPrev, next, cpuPrev.Length);
        cpuPrev = next;
    }

    /// <summary>
    /// Pack t15 after <c>MyGeometryRenderer.Prepare</c>. UpdateMatrices already
    /// sees Main instances; the generic ColorPrepare Harmony hooks did not
    /// (<c>prepare=0</c>, <c>0/0</c> slots while <c>visible</c> and <c>groups</c>
    /// were nonzero).
    /// </summary>
    public static void NoteStage2Render(MyGeometryRenderer renderer, MyCullQuery query)
    {
        if (renderer == null || query == null || query.ViewId != 0)
            return;
        var groups = renderer.m_instanceRenderData?[0]?.InstanceLodGroups;
        var n = groups?.Count ?? 0;
        if (n > 0)
            Volatile.Write(ref stage2DrewThisFrame, n);
    }

    public static void PackAfterRendererPrepare(MyGeometryRenderer renderer, MyCullQuery query)
    {
        if (!InjectionWanted || renderer == null || query?.Results?.Instances == null)
            return;
        // GBuffer ColorPrepare / RenderPass always use pass id 0.
        if (query.ViewId != 0)
            return;

        Interlocked.Increment(ref stage2PrepareThisFrame);

        var buffers = renderer.m_instanceRenderData;
        if (buffers == null || buffers.Length == 0)
            return;
        var groups = buffers[0]?.InstanceLodGroups;
        if (groups == null || groups.Count == 0)
            return;

        renderer.GetLoddingSetting(0, out var settings);
        var custom0 = default(MyColorPreparePass0);
        var custom1 = default(MyColorPreparePass1);
        var instances = query.Results.Instances;
        var arr = instances.GetInternalArray();
        var count = instances.Count;
        PreparedScratch.Clear();
        for (var i = 0; i < count; i++)
        {
            var inst = arr[i];
            if (inst == null || !custom1.IsInstanceVisible(inst))
                continue;
            CollectPreparedLods(inst, ref settings, ref custom1);
        }

        var need = 1;
        for (var g = 0; g < groups.Count; g++)
        {
            var gr = groups[g];
            need = Math.Max(need, gr.OffsetInInstanceBuffer + gr.InstancesCount * (1 + gr.InstanceMaterialsCount));
        }

        prevCount = need;
        if (cpuPrev.Length < need)
            cpuPrev = new PrevInstance[need];
        else
            Array.Clear(cpuPrev, 0, need);

        if (groupInc.Length < groups.Count)
            groupInc = new int[groups.Count];
        Array.Clear(groupInc, 0, groups.Count);
        GroupIndex.Clear();
        for (var g = 0; g < groups.Count; g++)
        {
            var gr = groups[g];
            if (gr.LodInstance == null)
                continue;
            GroupIndex[((long)gr.LodInstance.UniqueId << 8) | (uint)(int)gr.State] = g;
        }

        var hasCam = CameraVelocityPass.TryGetPrevCamera(out var prevCam);
        for (var i = 0; i < PreparedScratch.Count; i++)
        {
            var p = PreparedScratch[i];
            if (p.Instance == null || p.LodInstance == null)
                continue;
            if (!GroupIndex.TryGetValue(((long)p.LodInstance.UniqueId << 8) | (uint)(int)p.StateId, out var gi))
                continue;
            var group = groups[gi];
            var slot = group.OffsetInInstanceBuffer + groupInc[gi];
            PackSlot(slot, p.Instance, hasCam, prevCam);
            var mats = custom0.GetInstanceMaterialsCount(p.Lod);
            for (var j = 0; j < mats; j++)
            {
                slot += group.InstancesCount;
                PackSlot(slot, p.Instance, hasCam, prevCam);
            }

            groupInc[gi]++;
        }

        var packedValid = 0;
        for (var i = 0; i < need && i < cpuPrev.Length; i++)
        {
            if (cpuPrev[i].Flags.X > 0.5f)
                packedValid++;
        }
        Volatile.Write(ref stage2ValidThisFrame, packedValid);
        Volatile.Write(ref stage2SlotsThisFrame, need);

        // CPU-only on the parallel prepare worker. The GPU upload is recorded
        // later by the Stage 2 GBuffer's own deferred render context.
    }

    static void CollectPreparedLods(MyInstance inst, ref MyPassLoddingSetting settings, ref MyColorPreparePass1 custom1)
    {
        var stateId = inst.LodStrategy.ExplicitLodState;
        var transition = custom1.IsTransitionLodUsed(inst);
        if (transition)
            stateId = MyInstanceLodState.Transition;
        var lodShift = inst.CheckGbufferVisible() ? settings.LodShiftVisible : settings.LodShift;
        var currentLod = inst.LodStrategy.CurrentLod;
        if (currentLod != -1)
        {
            currentLod = Math.Min(inst.LodStrategy.MaxLod, Math.Max(settings.MinLod, currentLod + lodShift));
            PreparedScratch.Add(new PreparedLod
            {
                Lod = inst.Model.GetLod(currentLod),
                LodInstance = inst.ModelInstance.LodInstances[currentLod],
                StateId = stateId,
                Instance = inst
            });
        }

        var transitionLod = inst.LodStrategy.TransitionLod;
        if (!transition || transitionLod == -1)
            return;
        transitionLod = Math.Min(inst.LodStrategy.MaxLod, Math.Max(settings.MinLod, transitionLod + lodShift));
        PreparedScratch.Add(new PreparedLod
        {
            Lod = inst.Model.GetLod(transitionLod),
            LodInstance = inst.ModelInstance.LodInstances[transitionLod],
            StateId = MyInstanceLodState.Transition,
            Instance = inst
        });
    }

    /// <summary>
    /// One call after GBuffer <c>PrepareInstanceableGroups</c> — not a Harmony
    /// postfix on <c>AddInstanceIntoInstanceElements</c> (that method also fills
    /// env-probe ColorPrepare and every extra material row).
    /// </summary>
    public static void PackAfterGBufferPrepare(MyPreparePass<MyColorPreparePass0, MyColorPreparePass1> pass)
    {
        if (!InjectionWanted || pass == null || pass.PassId != 0)
            return;

        var elements = Math.Max(pass.m_elementsCount, 0);
        prevCount = elements;
        var need = Math.Max(prevCount, 1);
        if (cpuPrev.Length < need)
            cpuPrev = new PrevInstance[need];
        else
            Array.Clear(cpuPrev, 0, need);

        var prepared = pass.m_preparedLodData;
        var groups = pass.m_outputRenderData?.InstanceLodGroups;
        var hasCam = CameraVelocityPass.TryGetPrevCamera(out var prevCam);
        if (prepared != null && groups != null && groups.Count > 0)
        {
            if (groupInc.Length < groups.Count)
                groupInc = new int[groups.Count];
            Array.Clear(groupInc, 0, groups.Count);
            GroupIndex.Clear();
            for (var g = 0; g < groups.Count; g++)
            {
                var gr = groups[g];
                if (gr.LodInstance == null)
                    continue;
                GroupIndex[((long)gr.LodInstance.UniqueId << 8) | (uint)(int)gr.State] = g;
            }

            var lods = prepared.GetInternalArray();
            var count = prepared.Count;
            var custom = pass.m_customPass0;
            for (var i = 0; i < count; i++)
            {
                var p = lods[i];
                var instance = p.Instance;
                if (instance == null || p.LodInstance == null)
                    continue;
                if (!GroupIndex.TryGetValue(((long)p.LodInstance.UniqueId << 8) | (uint)(int)p.StateId, out var gi))
                    continue;
                var group = groups[gi];
                var slot = group.OffsetInInstanceBuffer + groupInc[gi];
                PackSlot(slot, instance, hasCam, prevCam);
                var mats = custom.GetInstanceMaterialsCount(p.Lod);
                for (var j = 0; j < mats; j++)
                {
                    slot += group.InstancesCount;
                    PackSlot(slot, instance, hasCam, prevCam);
                }

                groupInc[gi]++;
            }
        }

        var packedValid = 0;
        var packed = Math.Max(prevCount, 0);
        for (var i = 0; i < packed && i < cpuPrev.Length; i++)
        {
            if (cpuPrev[i].Flags.X > 0.5f)
                packedValid++;
        }
        Volatile.Write(ref stage2ValidThisFrame, packedValid);
        Volatile.Write(ref stage2SlotsThisFrame, packed);

        // CPU-only on the parallel prepare worker. The GPU upload is recorded
        // later by the Stage 2 GBuffer's own deferred render context.
    }

    static void PackSlot(int bufferOffset, MyInstance instance, bool hasCam, Vector3D prevCam)
    {
        if (bufferOffset < 0 || bufferOffset >= cpuPrev.Length)
            return;
        var actorId = instance.ActorID;
        if (!hasCam ||
            ActorHistory.Instance.WasTeleported(actorId) ||
            !ActorHistory.Instance.TryGetPrevious(actorId, out var prevWorldAbs))
        {
            cpuPrev[bufferOffset] = default;
            return;
        }

        var owner = instance.Owner?.Owner;
        if (owner != null)
        {
            var current = owner.WorldMatrix.Translation;
            var millimeters = (int)Math.Min(int.MaxValue,
                Math.Round(Vector3D.Distance(current, prevWorldAbs.Translation) * 1000.0));
            if (millimeters > 1)
            {
                Interlocked.Increment(ref stage2MovingSlotsThisFrame);
                AtomicMax(ref stage2MaxMotionMmThisFrame, millimeters);
            }
        }

        cpuPrev[bufferOffset] = Pack(prevWorldAbs, prevCam);
    }

    static void AtomicMax(ref int location, int value)
    {
        var current = Volatile.Read(ref location);
        while (value > current)
        {
            var observed = Interlocked.CompareExchange(ref location, value, current);
            if (observed == current)
                return;
            current = observed;
        }
    }

    public static void Bind(MyRenderContext rc, MyGBuffer gbuffer)
    {
        BindCore(rc, gbuffer, uploadStage2History: false);
    }

    /// <summary>
    /// Stage 2 pass begin. Records the t15 upload on this pass's deferred
    /// context before recording its draws. Never map Keen's immediate context
    /// from a parallel prepare worker.
    /// </summary>
    public static void BindStage2(MyRenderContext rc, MyGBuffer gbuffer)
    {
        BindCore(rc, gbuffer, uploadStage2History: true);
    }

    static void BindCore(MyRenderContext rc, MyGBuffer gbuffer, bool uploadStage2History)
    {
        // Recover cleanly if an exceptional/missed End left this worker's
        // previous GBuffer pass active. A new Begin may otherwise skip its
        // three-target bind against the stale tracker entry.
        if (rc != null && ReferenceEquals(activeGBufferContext, rc))
        {
            RestoreNativeBlend(rc);
            RestoreBaseTargets(rc);
        }
        ClearActivePass(rc);
        if (rc == null || gbuffer == null || !rc.IsInitialized)
            return;
        if (!InjectionWanted && !GBufferAttachments.HasColorTargets)
            return;

        if (Volatile.Read(ref resourcesReady) == 0)
        {
            lock (Gate)
            {
                if (resourcesReady == 0)
                {
                    try
                    {
                        EnsureResourcesUnlocked();
                    }
                    catch (Exception e)
                    {
                        Fail("bind: " + e.Message, e);
                        return;
                    }
                }
            }
        }

        try
        {
            if (uploadStage2History)
            {
                lock (Gate)
                    UploadPrevWorldUnlocked(rc);
            }
            BindToPass(rc, gbuffer);
            Volatile.Write(ref worldGBufferSeen, 1);
        }
        catch (Exception e)
        {
            Fail("bind: " + e.Message, e);
        }
    }

    public static void Unbind(MyRenderContext rc)
    {
        if (rc == null || !rc.IsInitialized)
            return;
        try
        {
            RestoreNativeBlend(rc);
            RestoreBaseTargets(rc);
            rc.VertexShader.SetSrv(PrevWorldSlot, null);
            rc.VertexShader.SetSrv(PrevBoneSlot, null);
            rc.VertexShader.SetConstantBuffer(ConstantSlot, null);
            rc.PixelShader.SetConstantBuffer(PixelProbeConstantSlot, null);
        }
        catch (Exception e)
        {
            DebugLog.Write("GBufferVelocity unbind: " + e.Message);
        }
        finally
        {
            ClearActivePass(rc);
        }
    }

    /// <summary>
    /// Exact GBuffer draw boundary: Keen has already selected the material VS,
    /// PS, and blend state. Rebind the already-mapped velocity CB and four-RT
    /// output set here so no intervening renderer state can replace b6 or drop
    /// Target3 between pass/group setup and the native draw. The target bind
    /// must update Keen's state tracker before the native hard bind; otherwise
    /// later three-target restores can be skipped and non-GBuffer passes can
    /// overwrite velocity. MrtWrite additionally classifies the active shader
    /// objects.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void OnDrawIndexedInstanced(MyRenderContext rc)
    {
        if (rc == null || !ReferenceEquals(activeGBufferContext, rc))
            return;

        Interlocked.Increment(ref drawBoundaryDrawsThisFrame);
        var inspectBoundary = !drawBoundaryInspected;
        if (inspectBoundary)
        {
            drawBoundaryInspected = true;
            InspectDrawBoundary(rc);
        }

        // MyShaderBundle stores shader IDs. Keen resolves those IDs immediately
        // before every geometry draw, but after ReloadEffects the stage cache can
        // still hold a separately-owned wrapper. The ID lookup is authoritative:
        // bind that exact current-generation pair natively, then synchronize the
        // cache so a later pass cannot incorrectly skip its own shader bind.
        // Consume the pair once so a draw with a missing lookup can never reuse
        // the previous material's permutation.
        var resolvedVertexShader = activeResolvedVertexShader;
        var resolvedPixelShader = activeResolvedPixelShader;
        activeResolvedVertexShader = null;
        activeResolvedPixelShader = null;
        if (resolvedVertexShader != null && resolvedPixelShader != null)
        {
            Interlocked.Increment(ref resolvedPairsThisFrame);
            var verifiedPair =
                ShaderCompileIntercept.IsVerifiedGBufferVertexObject(resolvedVertexShader) &&
                ShaderCompileIntercept.IsVerifiedGBufferPixelObject(resolvedPixelShader);
            if (verifiedPair)
            {
                Interlocked.Increment(ref resolvedVerifiedPairsThisFrame);
                if (rc.DeviceContext != null)
                {
                    rc.DeviceContext.VertexShader.Set(resolvedVertexShader);
                    rc.VertexShader.m_vertexShader = resolvedVertexShader;
                    rc.DeviceContext.PixelShader.Set(resolvedPixelShader);
                    rc.PixelShader.m_pixelShader = resolvedPixelShader;
                }
            }
            else if (ShaderCompileIntercept.IsResidentDepthVertexObject(resolvedVertexShader) ||
                     ShaderCompileIntercept.IsResidentDepthPixelObject(resolvedPixelShader))
            {
                Interlocked.Increment(ref resolvedDepthPairsThisFrame);
            }
            else
            {
                Interlocked.Increment(ref resolvedOtherPairsThisFrame);
            }
        }

        // Do not MapDiscard here: one CB allocation is deliberately recorded
        // per group on Keen's deferred context. This only restores that exact
        // resource at the last possible boundary before the native draw.
        var velocityCb = activeVelocityCb;
        var pixelProbeCb = activePixelProbeCb;
        if (velocityCb?.Buffer != null && pixelProbeCb?.Buffer != null && rc.DeviceContext != null)
        {
            BindNativeVelocityConstantBuffers(rc.DeviceContext, velocityCb.Buffer, pixelProbeCb.Buffer);
            Interlocked.Increment(ref drawBoundaryVelocityCbRebindsThisFrame);
        }
        else
        {
            Interlocked.Increment(ref drawBoundaryVelocityCbMissingThisFrame);
        }

        var dsvBind = activeGBufferDsvBind;
        var dsv = activeGBufferDsv;
        var rtvs = activeGBufferRtvs;
        if (dsvBind != null && dsv != null && rtvs != null && rtvs.Length >= 4 && rc.DeviceContext != null)
        {
            if (target?.Rtv != null && rtvs[3]?.NativePointer == target.Rtv.NativePointer)
                Interlocked.Increment(ref drawBoundaryRebindTarget3ThisFrame);

            // Keep MyRenderContextState synchronized first. The native repeat is
            // still the final draw-boundary guarantee, but now a later Keen
            // SetRtvs(GBuffer) sees 4 -> 3 and really unbinds Target3.
            rc.SetRtvs(dsvBind, rtvs);
            if (inspectBoundary)
                InspectTrackedDrawBoundary(rc);
            rc.DeviceContext.OutputMerger.SetTargets(dsv, rtvs.Length, rtvs);
            Interlocked.Increment(ref drawBoundaryRebindsThisFrame);
        }

        // Do not trust the wrapper's blend-state identity here. Other renderer
        // code can bind a native state without updating MyRenderContextState,
        // leaving m_blendState apparently suitable while Target3 is masked or
        // blended to zero. Enforce the cloned Target3-replace state at the same
        // native boundary as the shaders, CBs, and RTVs for every draw.
        BindVelocityBlend(rc);

        if ((Config.Current?.VelocityProbe ?? VelocityProbe.Off) != VelocityProbe.MrtWrite)
            return;

        InspectNativeDrawState(
            rc,
            velocityCb?.Buffer,
            pixelProbeCb?.Buffer,
            rc.VertexShader.m_vertexShader,
            rc.PixelShader.m_pixelShader,
            target?.Rtv,
            AuditProofWanted ? auditTarget?.Rtv : null);

        Interlocked.Increment(ref drawBoundaryVertexChecksThisFrame);
        var vertexShader = rc.VertexShader.m_vertexShader;
        if (ShaderCompileIntercept.IsResidentVertexObject(vertexShader))
            Interlocked.Increment(ref drawBoundaryCurrentVerticesThisFrame);
        if (ShaderCompileIntercept.IsResidentGBufferVertexObject(vertexShader))
            Interlocked.Increment(ref drawBoundaryGBufferVerticesThisFrame);
        if (ShaderCompileIntercept.IsResidentVelocityVertexObject(vertexShader))
            Interlocked.Increment(ref drawBoundaryVelocityVerticesThisFrame);
        if (ShaderCompileIntercept.IsResidentDepthVertexObject(vertexShader))
            Interlocked.Increment(ref drawBoundaryDepthVerticesThisFrame);
        if (ShaderCompileIntercept.IsRetiredVertexObject(vertexShader))
            Interlocked.Increment(ref drawBoundaryRetiredVerticesThisFrame);
        if (ShaderCompileIntercept.IsVerifiedGBufferVertexObject(vertexShader))
            Interlocked.Increment(ref drawBoundaryVerifiedVertexFlowThisFrame);

        Interlocked.Increment(ref drawBoundaryPixelChecksThisFrame);
        var shader = rc.PixelShader.m_pixelShader;
        if (ShaderCompileIntercept.IsResidentPixelObject(shader))
            Interlocked.Increment(ref drawBoundaryCurrentPixelsThisFrame);
        if (ShaderCompileIntercept.IsResidentGBufferPixelObject(shader))
            Interlocked.Increment(ref drawBoundaryGBufferPixelsThisFrame);
        if (ShaderCompileIntercept.IsResidentVelocityPixelObject(shader))
            Interlocked.Increment(ref drawBoundaryVelocityPixelsThisFrame);
        if (ShaderCompileIntercept.IsResidentDepthPixelObject(shader))
            Interlocked.Increment(ref drawBoundaryDepthPixelsThisFrame);
        if (ShaderCompileIntercept.IsRetiredPixelObject(shader))
            Interlocked.Increment(ref drawBoundaryRetiredPixelsThisFrame);
        if (ShaderCompileIntercept.IsVerifiedGBufferPixelObject(shader))
            Interlocked.Increment(ref drawBoundaryVerifiedPixelFlowThisFrame);

    }

    /// <summary>
    /// Query the native D3D state at the last possible point before every
    /// MrtWrite draw. SharpDX's Get calls return AddRef'd wrappers, so every
    /// object is released immediately. This is deliberately developer-probe
    /// only: it verifies the whole draw gamut without burdening normal frames.
    /// </summary>
    static void InspectNativeDrawState(
        MyRenderContext rc,
        SharpDX.Direct3D11.Buffer expectedVsb6,
        SharpDX.Direct3D11.Buffer expectedPsb7,
        VertexShader expectedVs,
        PixelShader expectedPs,
        RenderTargetView expectedTarget3,
        RenderTargetView expectedTarget7)
    {
        SharpDX.Direct3D11.Buffer[] nativeVsb6 = null;
        SharpDX.Direct3D11.Buffer[] nativePsb7 = null;
        VertexShader nativeVs = null;
        PixelShader nativePs = null;
        RenderTargetView[] nativeRtvs = null;
        DepthStencilView nativeDsv = null;
        BlendState nativeBlend = null;

        try
        {
            var context = rc.DeviceContext;
            if (context == null)
                return;

            Interlocked.Increment(ref nativeDrawStateQueriesThisFrame);
            if (rc.IsDeferred)
                Interlocked.Increment(ref nativeDrawStateDeferredThisFrame);

            nativeVsb6 = context.VertexShader.GetConstantBuffers(ConstantSlot, 1);
            nativePsb7 = context.PixelShader.GetConstantBuffers(PixelProbeConstantSlot, 1);
            nativeVs = context.VertexShader.Get();
            nativePs = context.PixelShader.Get();
            nativeRtvs = context.OutputMerger.GetRenderTargets(expectedTarget7 != null ? 8 : 4, out nativeDsv);
            RawColor4 blendFactor;
            int blendSampleMask;
            nativeBlend = context.OutputMerger.GetBlendState(out blendFactor, out blendSampleMask);

            CountNativeBinding(
                NativePointer(nativeVsb6, 0), expectedVsb6?.NativePointer ?? IntPtr.Zero,
                ref nativeDrawStateVsb6MatchThisFrame,
                ref nativeDrawStateVsb6NullThisFrame,
                ref nativeDrawStateVsb6MismatchThisFrame);
            CountNativeBinding(
                NativePointer(nativePsb7, 0), expectedPsb7?.NativePointer ?? IntPtr.Zero,
                ref nativeDrawStatePsb7MatchThisFrame,
                ref nativeDrawStatePsb7NullThisFrame,
                ref nativeDrawStatePsb7MismatchThisFrame);
            CountNativeBinding(
                nativeVs?.NativePointer ?? IntPtr.Zero, expectedVs?.NativePointer ?? IntPtr.Zero,
                ref nativeDrawStateVsMatchThisFrame,
                ref nativeDrawStateVsNullThisFrame,
                ref nativeDrawStateVsMismatchThisFrame);
            CountNativeBinding(
                nativePs?.NativePointer ?? IntPtr.Zero, expectedPs?.NativePointer ?? IntPtr.Zero,
                ref nativeDrawStatePsMatchThisFrame,
                ref nativeDrawStatePsNullThisFrame,
                ref nativeDrawStatePsMismatchThisFrame);
            CountNativeBinding(
                NativePointer(nativeRtvs, 3), expectedTarget3?.NativePointer ?? IntPtr.Zero,
                ref nativeDrawStateTarget3MatchThisFrame,
                ref nativeDrawStateTarget3NullThisFrame,
                ref nativeDrawStateTarget3MismatchThisFrame);
            if (expectedTarget7 != null)
            {
                CountNativeBinding(
                    NativePointer(nativeRtvs, 7), expectedTarget7.NativePointer,
                    ref nativeDrawStateTarget7MatchThisFrame,
                    ref nativeDrawStateTarget7NullThisFrame,
                    ref nativeDrawStateTarget7MismatchThisFrame);
            }

            CountNativeBinding(
                nativeBlend?.NativePointer ?? IntPtr.Zero,
                activeVelocityBlendOverride?.NativePointer ?? IntPtr.Zero,
                ref nativeDrawStateBlendMatchThisFrame,
                ref nativeDrawStateBlendNullThisFrame,
                ref nativeDrawStateBlendMismatchThisFrame);
            if (nativeBlend != null)
            {
                var blendDesc = nativeBlend.Description;
                if (blendDesc.IndependentBlendEnable)
                    Interlocked.Increment(ref nativeDrawStateBlendIndependentThisFrame);
                if (IsTargetWritable(blendDesc, 3, ColorWriteMaskFlags.Red | ColorWriteMaskFlags.Green))
                    Interlocked.Increment(ref nativeDrawStateBlendTarget3WritableThisFrame);
                if (IsTargetWritable(blendDesc, 7, ColorWriteMaskFlags.All))
                    Interlocked.Increment(ref nativeDrawStateBlendTarget7WritableThisFrame);
            }
        }
        catch
        {
            Interlocked.Increment(ref nativeDrawStateErrorsThisFrame);
        }
        finally
        {
            DisposeAll(nativeVsb6);
            DisposeAll(nativePsb7);
            nativeVs?.Dispose();
            nativePs?.Dispose();
            DisposeAll(nativeRtvs);
            nativeDsv?.Dispose();
            nativeBlend?.Dispose();
        }
    }

    static bool IsTargetWritable(BlendStateDescription desc, int target, ColorWriteMaskFlags required)
    {
        var targets = desc.RenderTarget;
        var index = desc.IndependentBlendEnable ? target : 0;
        return targets != null && index >= 0 && index < targets.Length &&
               (targets[index].RenderTargetWriteMask & required) == required;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static IntPtr NativePointer<T>(T[] objects, int index) where T : SharpDX.ComObject =>
        objects != null && index >= 0 && index < objects.Length && objects[index] != null
            ? objects[index].NativePointer
            : IntPtr.Zero;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void CountNativeBinding(
        IntPtr actual,
        IntPtr expected,
        ref int match,
        ref int missing,
        ref int mismatch)
    {
        if (actual == IntPtr.Zero)
            Interlocked.Increment(ref missing);
        else if (actual == expected)
            Interlocked.Increment(ref match);
        else
            Interlocked.Increment(ref mismatch);
    }

    static void DisposeAll<T>(T[] objects) where T : IDisposable
    {
        if (objects == null)
            return;
        for (var i = 0; i < objects.Length; i++)
            objects[i]?.Dispose();
    }

    /// <summary>
    /// Called from the exact <c>MyPixelShaders.Id -&gt; PixelShader</c> lookup.
    /// A non-zero count proves Stage 2 is resolving through the same patched
    /// static manager; object classification then distinguishes current,
    /// GBuffer-overlay, and prior-generation shaders.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void OnPixelShaderLookup(PixelShader shader)
    {
        if (activeGBufferContext == null)
            return;
        activeResolvedPixelShader = shader;
        Interlocked.Increment(ref pixelShaderLookupsThisFrame);
        if (ShaderCompileIntercept.IsResidentPixelObject(shader))
            Interlocked.Increment(ref pixelShaderLookupCurrentThisFrame);
        if (ShaderCompileIntercept.IsResidentGBufferPixelObject(shader))
            Interlocked.Increment(ref pixelShaderLookupGBufferThisFrame);
        if (ShaderCompileIntercept.IsResidentVelocityPixelObject(shader))
            Interlocked.Increment(ref pixelShaderLookupVelocityThisFrame);
        if (ShaderCompileIntercept.IsResidentDepthPixelObject(shader))
            Interlocked.Increment(ref pixelShaderLookupDepthThisFrame);
        if (ShaderCompileIntercept.IsVerifiedGBufferPixelObject(shader))
            Interlocked.Increment(ref pixelShaderLookupVerifiedThisFrame);
        if (ShaderCompileIntercept.IsRetiredPixelObject(shader))
            Interlocked.Increment(ref pixelShaderLookupRetiredThisFrame);
    }

    /// <summary>
    /// Vertex half of the shader-ID handoff. A velocity draw is rebound only
    /// when both current GBuffer shaders were resolved for that exact draw.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void OnVertexShaderLookup(VertexShader shader)
    {
        if (activeGBufferContext == null)
            return;
        activeResolvedVertexShader = shader;
        Interlocked.Increment(ref vertexShaderLookupsThisFrame);
        if (ShaderCompileIntercept.IsResidentVertexObject(shader))
            Interlocked.Increment(ref vertexShaderLookupCurrentThisFrame);
        if (ShaderCompileIntercept.IsResidentGBufferVertexObject(shader))
            Interlocked.Increment(ref vertexShaderLookupGBufferThisFrame);
        if (ShaderCompileIntercept.IsResidentVelocityVertexObject(shader))
            Interlocked.Increment(ref vertexShaderLookupVelocityThisFrame);
        if (ShaderCompileIntercept.IsResidentDepthVertexObject(shader))
            Interlocked.Increment(ref vertexShaderLookupDepthThisFrame);
        if (ShaderCompileIntercept.IsVerifiedGBufferVertexObject(shader))
            Interlocked.Increment(ref vertexShaderLookupVerifiedThisFrame);
        if (ShaderCompileIntercept.IsRetiredVertexObject(shader))
            Interlocked.Increment(ref vertexShaderLookupRetiredThisFrame);
    }

    /// <summary>
    /// Keen's decal blend states use independent MRT blending and leave slots
    /// above Target2 at their zero-initialized write mask. Merely appending
    /// Target3 therefore binds the velocity surface but silently discards the
    /// pixel shader's SV_Target3 output. Clone only the active state, preserve
    /// Keen's Target0-2 behavior, make Target3 an RG replace target, and keep
    /// the reserved Target7 audit sideband RGBA-writable when it is bound.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void BindVelocityBlend(MyRenderContext rc)
    {
        var source = rc.m_state?.m_blendState;
        if (IsTarget3Writable(source))
            Interlocked.Increment(ref drawBoundaryWritableThisFrame);
        else
            Interlocked.Increment(ref drawBoundaryMaskedThisFrame);

        if (source == null)
        {
            // D3D11's null/default blend state is replace with all channels
            // writable. Rebind it even when Keen's tracker already says null:
            // the tracker can be stale after a direct native bind.
            rc.DeviceContext.OutputMerger.SetBlendState(null);
            activeVelocityBlendOverride = null;
            nativeBlendOverridden = true;
            Interlocked.Increment(ref drawBoundaryBlendOverridesThisFrame);
            return;
        }

        var replacement = GetVelocityBlendOverride(source);
        if (replacement == null)
        {
            Interlocked.Increment(ref drawBoundaryBlendFailuresThisFrame);
            return;
        }

        // Keep Keen's state cache on the original object. This native bind is
        // made at the final draw boundary and restored once when the pass ends.
        rc.DeviceContext.OutputMerger.SetBlendState(replacement);
        activeVelocityBlendOverride = replacement;
        nativeBlendOverridden = true;
        Interlocked.Increment(ref drawBoundaryBlendOverridesThisFrame);
    }

    static void RestoreNativeBlend(MyRenderContext rc)
    {
        if (!nativeBlendOverridden || rc?.DeviceContext == null)
            return;
        rc.DeviceContext.OutputMerger.SetBlendState(rc.m_state?.m_blendState);
        activeVelocityBlendOverride = null;
        nativeBlendOverridden = false;
    }

    /// <summary>
    /// Restore Keen's ordinary three-target GBuffer set through its tracker.
    /// This closes the pass without leaving a native Target3 binding hidden
    /// behind a stale three-target cache entry.
    /// </summary>
    static void RestoreBaseTargets(MyRenderContext rc)
    {
        if (!ReferenceEquals(activeGBufferContext, rc))
            return;
        var dsvBind = activeGBufferDsvBind;
        var rtvs = activeGBufferBaseRtvs;
        if (dsvBind == null || rtvs == null || rtvs.Length < 3)
            return;
        rc.SetRtvs(dsvBind, rtvs);
        Interlocked.Increment(ref passTargetRestoresThisFrame);
    }

    static bool IsTarget3Writable(BlendState state)
    {
        if (state == null)
            return true; // D3D11 default: blend disabled, RGBA write enabled.
        var desc = state.Description;
        var targets = desc.RenderTarget;
        var index = desc.IndependentBlendEnable ? 3 : 0;
        if (targets == null || targets.Length <= index)
            return false;
        const ColorWriteMaskFlags rg = ColorWriteMaskFlags.Red | ColorWriteMaskFlags.Green;
        return (targets[index].RenderTargetWriteMask & rg) == rg;
    }

    static bool IsTarget3Replace(BlendStateDescription desc)
    {
        var targets = desc.RenderTarget;
        var index = desc.IndependentBlendEnable ? 3 : 0;
        if (targets == null || targets.Length <= index)
            return false;
        var targetDesc = targets[index];
        const ColorWriteMaskFlags rg = ColorWriteMaskFlags.Red | ColorWriteMaskFlags.Green;
        if ((targetDesc.RenderTargetWriteMask & rg) != rg)
            return false;
        if (!targetDesc.IsBlendEnabled)
            return true;
        return targetDesc.SourceBlend == BlendOption.One &&
               targetDesc.DestinationBlend == BlendOption.Zero &&
               targetDesc.BlendOperation == BlendOperation.Add &&
               targetDesc.SourceAlphaBlend == BlendOption.One &&
               targetDesc.DestinationAlphaBlend == BlendOption.Zero &&
               targetDesc.AlphaBlendOperation == BlendOperation.Add;
    }

    static BlendState GetVelocityBlendOverride(BlendState source)
    {
        var pointer = source.NativePointer;
        var snapshot = Volatile.Read(ref blendOverrides);
        for (var i = 0; i < snapshot.Length; i++)
        {
            if (snapshot[i].Source == pointer)
                return snapshot[i].State;
        }

        lock (BlendGate)
        {
            snapshot = blendOverrides;
            for (var i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i].Source == pointer)
                    return snapshot[i].State;
            }

            BlendState state = null;
            try
            {
                var desc = source.Description;
                var targets = desc.RenderTarget;
                if (targets == null || targets.Length <= 7)
                    throw new InvalidOperationException("blend description has no Target3/Target7 slots");

                if (!desc.IndependentBlendEnable)
                {
                    // D3D applies Target0 to every MRT when independent blending
                    // is off. Materialize that behavior before enabling Target3.
                    var shared = targets[0];
                    for (var i = 1; i < targets.Length; i++)
                        targets[i] = shared;
                    desc.IndependentBlendEnable = true;
                }

                targets[3] = new RenderTargetBlendDescription
                {
                    IsBlendEnabled = false,
                    SourceBlend = BlendOption.One,
                    DestinationBlend = BlendOption.Zero,
                    BlendOperation = BlendOperation.Add,
                    SourceAlphaBlend = BlendOption.One,
                    DestinationAlphaBlend = BlendOption.Zero,
                    AlphaBlendOperation = BlendOperation.Add,
                    RenderTargetWriteMask = ColorWriteMaskFlags.Red | ColorWriteMaskFlags.Green
                };
                targets[7] = new RenderTargetBlendDescription
                {
                    IsBlendEnabled = false,
                    SourceBlend = BlendOption.One,
                    DestinationBlend = BlendOption.Zero,
                    BlendOperation = BlendOperation.Add,
                    SourceAlphaBlend = BlendOption.One,
                    DestinationAlphaBlend = BlendOption.Zero,
                    AlphaBlendOperation = BlendOperation.Add,
                    RenderTargetWriteMask = ColorWriteMaskFlags.Red | ColorWriteMaskFlags.Green |
                                            ColorWriteMaskFlags.Blue | ColorWriteMaskFlags.Alpha
                };

                // Own the native state directly. This makes the complete
                // eight-target descriptor the actual D3D11 contract instead
                // of relying on Keen's ordinary GBuffer resource path.
                state = new BlendState(MyRender11.DeviceInstance, desc)
                {
                    DebugName = "AnomalyVelocityBlend_" + pointer.ToInt64().ToString("X")
                };
            }
            catch (Exception e)
            {
                DebugLog.Write("GBufferVelocity blend override: " + e.GetBaseException().Message);
            }

            var next = new BlendOverride[snapshot.Length + 1];
            Array.Copy(snapshot, next, snapshot.Length);
            next[snapshot.Length] = new BlendOverride(pointer, state);
            Volatile.Write(ref blendOverrides, next);
            return state;
        }
    }

    static void InspectDrawBoundary(MyRenderContext rc)
    {
        Interlocked.Increment(ref drawBoundaryChecksThisFrame);
        try
        {
            // Read Keen's own state cache rather than issuing D3D Get* calls on
            // a deferred context. The MrtWrite rebind above proves native state.
            var state = rc.m_state;
            var rtvs = state?.m_rtvs;
            var count = state?.m_rtvsCount ?? 0;
            if (count > 3 && rtvs != null && rtvs.Length > 3 && target?.Rtv != null &&
                rtvs[3]?.NativePointer == target.Rtv.NativePointer)
                Interlocked.Increment(ref drawBoundaryTarget3ThisFrame);
        }
        catch (Exception e)
        {
            DebugLog.Write("GBufferVelocity draw-boundary inspect: " + e.Message);
        }
    }

    static void InspectTrackedDrawBoundary(MyRenderContext rc)
    {
        try
        {
            var state = rc.m_state;
            var rtvs = state?.m_rtvs;
            var count = state?.m_rtvsCount ?? 0;
            if (count > 3 && rtvs != null && rtvs.Length > 3 && target?.Rtv != null &&
                rtvs[3]?.NativePointer == target.Rtv.NativePointer)
                Interlocked.Increment(ref drawBoundaryTrackedTarget3ThisFrame);
        }
        catch (Exception e)
        {
            DebugLog.Write("GBufferVelocity tracked draw-boundary inspect: " + e.Message);
        }
    }

    static void ClearActivePass(MyRenderContext rc)
    {
        if (rc != null && activeGBufferContext != null && !ReferenceEquals(activeGBufferContext, rc))
            return;
        activeGBufferContext = null;
        activeGBufferDsvBind = null;
        activeGBufferDsv = null;
        activeGBufferBaseRtvs = null;
        activeGBufferRtvs = null;
        activeVelocityCbPool = null;
        activeVelocityCb = null;
        activePixelProbeCb = null;
        activeResolvedVertexShader = null;
        activeResolvedPixelShader = null;
        drawBoundaryInspected = false;
        nativeBlendOverridden = false;
        activeVelocityBlendOverride = null;
    }

    /// <summary>Called when Keen ends the current session/world.</summary>
    public static void ResetWorldActivity()
    {
        Volatile.Write(ref worldGBufferSeen, 0);
        ClearActivePass(null);
        lock (Gate)
        {
            capturedCheckpoint = Target3Checkpoint.Live;
            checkpointCaptureFrame = -1;
            checkpointCaptureCount = 0;
            checkpointDebugSourceNative = IntPtr.Zero;
            checkpointLastError = null;
        }
    }

    /// <summary>
    /// Copies the live velocity target at one selected scheduler boundary. All
    /// hooks are installed together, but only the selected point performs GPU
    /// work so changing checkpoints never requires another build.
    /// </summary>
    public static void CaptureTarget3Checkpoint(Target3Checkpoint checkpoint)
    {
        var cfg = Config.Current;
        if (cfg == null || cfg.Target3Checkpoint != checkpoint ||
            (cfg.DebugBuffer != DebugBuffer.GBufferVelocityRaw &&
             cfg.DebugBuffer != DebugBuffer.VelocityPipelineAudit))
            return;

        lock (Gate)
        {
            try
            {
                CaptureTarget3CheckpointUnlocked(checkpoint, MyRender11.RC);
            }
            catch (Exception e)
            {
                checkpointLastError = e.GetType().Name + ": " + e.Message;
                DebugLog.Write("GBufferVelocity Target3 checkpoint " + checkpoint + ": " + e);
            }
        }
    }

    /// <summary>
    /// Diagnostic issued once on Keen's immediate context after
    /// <c>MyRenderScheduler.Done</c> has executed every deferred geometry
    /// command list. Per-pass deferred clears are ordering-dependent: a later
    /// command list can overwrite them and make participating geometry look
    /// gray. This clear is therefore the authoritative final-resource test.
    /// </summary>
    public static void ApplySchedulerEndProbe()
    {
        var probe = Config.Current?.VelocityProbe ?? VelocityProbe.Off;
        if (probe != VelocityProbe.PassEndClear && probe != VelocityProbe.MrtWrite)
            return;
        lock (Gate)
        {
            var rc = MyRender11.RC;
            if (rc == null || !rc.IsInitialized)
                return;
            if (probe == VelocityProbe.MrtWrite)
            {
                ReadBackMrtWriteProbe(rc);
                return;
            }
            if (target != null)
            {
                rc.ClearRtv(target, new RawColor4(8f, 0f, 0f, 0f));
                Interlocked.Increment(ref passEndClearsThisFrame);
            }
        }
    }

    /// <summary>
    /// Reads the immutable VS b6 and PS b7 probes once on Keen's immediate
    /// context. Unlike CPU-side struct dumps, this verifies the bytes in the
    /// actual D3D buffers bound to the two shader stages.
    /// </summary>
    static void ReadBackMrtWriteProbe(MyRenderContext rc)
    {
        if (Volatile.Read(ref mrtWriteProbeReadbackState) != 0 || mrtWriteProbeCb == null ||
            mrtWritePixelProbeCb == null)
            return;
        Volatile.Write(ref mrtWriteProbeReadbackState, 1);
        try
        {
            mrtWriteProbeReadback ??= MyManagers.Buffers.CreateRead(
                "Anomaly.VelocityCB.MrtWriteReadback", ConstantBufferBytes / sizeof(uint), sizeof(uint), false);
            rc.CopyResource(mrtWriteProbeCb, mrtWriteProbeReadback);
            var mapped = rc.MapSubresource(mrtWriteProbeReadback, 0, MapMode.Read,
                SharpDX.Direct3D11.MapFlags.None);
            try
            {
                Volatile.Write(ref mrtWriteProbeReadbackValue,
                    Marshal.ReadInt32(mapped.DataPointer, Marshal.OffsetOf<Constants>(nameof(Constants.ProbeMode)).ToInt32()));
            }
            finally
            {
                rc.UnmapSubresource(mrtWriteProbeReadback, 0);
            }

            mrtWritePixelProbeReadback ??= MyManagers.Buffers.CreateRead(
                "Anomaly.VelocityPixelProbe.MrtWriteReadback",
                PixelProbeConstantBufferBytes / sizeof(uint), sizeof(uint), false);
            rc.CopyResource(mrtWritePixelProbeCb, mrtWritePixelProbeReadback);
            mapped = rc.MapSubresource(mrtWritePixelProbeReadback, 0, MapMode.Read,
                SharpDX.Direct3D11.MapFlags.None);
            try
            {
                var value = Marshal.PtrToStructure<Vector4>(mapped.DataPointer);
                Volatile.Write(ref mrtWritePixelProbeReadbackValue,
                    "(" + value.X.ToString("0.###") + "," + value.Z.ToString("0.###") + ")");
            }
            finally
            {
                rc.UnmapSubresource(mrtWritePixelProbeReadback, 0);
            }
            mrtWriteProbeReadbackError = null;
            Volatile.Write(ref mrtWriteProbeReadbackState, 2);
        }
        catch (Exception e)
        {
            mrtWriteProbeReadbackError = e.GetType().Name + ": " + e.Message;
            Volatile.Write(ref mrtWriteProbeReadbackState, -1);
            DebugLog.Write("GBufferVelocity immutable probe readback: " + e);
        }
    }

    public static void ClearTarget(MyRenderContext rc)
    {
        if (rc == null)
            return;
        lock (Gate)
        {
            if (AuditProofWanted && auditTarget != null)
                rc.ClearRtv(auditTarget, default(RawColor4));
            if (target != null)
            {
                var probe = Config.Current?.VelocityProbe ?? VelocityProbe.Off;
                var clear = probe == VelocityProbe.TargetClear
                    ? new RawColor4(8f, 0f, 0f, 0f)
                    // A negative sentinel makes MrtWrite tri-state: cyan means
                    // geometry never reached Target3, gray means it wrote zero,
                    // and pink means the expected +X shader value arrived.
                    : probe == VelocityProbe.MrtWrite
                        ? new RawColor4(-8f, 0f, 0f, 0f)
                        : default(RawColor4);
                rc.ClearRtv(target, clear);
                CaptureTarget3CheckpointUnlocked(Target3Checkpoint.AfterClear, rc);
            }
            GBufferAttachments.ClearTargets(rc);
        }
    }

    public static ISrvBindable PrepareCompositeSource()
    {
        lock (Gate)
        {
            try
            {
                return PrepareCompositeSourceUnlocked();
            }
            catch (Exception e)
            {
                Fail("composite source: " + e.Message, e);
                return null;
            }
        }
    }

    /// <summary>Raw-debug source; never used by the camera composite or publication.</summary>
    public static ISrvBindable PrepareDebugSource()
    {
        lock (Gate)
        {
            try
            {
                var selected = Config.Current?.Target3Checkpoint ?? Target3Checkpoint.Live;
                if (selected == Target3Checkpoint.Live)
                {
                    var live = PrepareCompositeSourceUnlocked();
                    checkpointDebugSourceNative = ResourceNative(live);
                    return live;
                }

                // Never silently substitute Live for an uncaptured checkpoint.
                // That made a late PassEndClear look as though it had already
                // existed at a boundary whose status still said "waiting".
                if (capturedCheckpoint != selected || checkpointTarget == null)
                {
                    checkpointDebugSourceNative = IntPtr.Zero;
                    return null;
                }

                ISrvBindable source = checkpointTarget;
                if (targetSamples > 1)
                {
                    EnsureCheckpointResolvedUnlocked();
                    var rc = MyRender11.RC;
                    if (checkpointResolved == null || rc?.DeviceContext == null)
                        return null;
                    rc.DeviceContext.ResolveSubresource(
                        checkpointTarget.Resource, 0, checkpointResolved.Resource, 0, Format.R16G16_Float);
                    source = checkpointResolved;
                }

                checkpointDebugSourceNative = ResourceNative(source);
                return source;
            }
            catch (Exception e)
            {
                checkpointLastError = e.GetType().Name + ": " + e.Message;
                Fail("debug checkpoint source: " + e.Message, e);
                return null;
            }
        }
    }

    /// <summary>
    /// Sideband written by the same GBuffer pixel invocation as Target3.
    /// RGBA = pixel-executed, PS-b7-enabled, incoming velocity X/Y.
    /// </summary>
    public static ISrvBindable PrepareAuditProofSource()
    {
        lock (Gate)
        {
            try
            {
                var selected = Config.Current?.Target3Checkpoint ?? Target3Checkpoint.Live;
                IRtvTexture source;
                if (selected == Target3Checkpoint.Live)
                {
                    source = auditTarget;
                }
                else
                {
                    if (capturedCheckpoint != selected || auditCheckpointTarget == null)
                        return null;
                    source = auditCheckpointTarget;
                }

                if (source == null)
                    return null;
                if (targetSamples <= 1)
                    return source;

                if (selected == Target3Checkpoint.Live)
                    EnsureAuditResolvedUnlocked(targetWidth, targetHeight);
                else
                    EnsureAuditCheckpointResolvedUnlocked();
                var rc = MyRender11.RC;
                var resolvedSource = selected == Target3Checkpoint.Live
                    ? auditResolved
                    : auditCheckpointResolved;
                if (resolvedSource == null || rc?.DeviceContext == null)
                    return null;
                rc.DeviceContext.ResolveSubresource(
                    source.Resource, 0, resolvedSource.Resource, 0, Format.R16G16B16A16_Float);
                return resolvedSource;
            }
            catch (Exception e)
            {
                Fail("audit proof source: " + e.Message, e);
                return null;
            }
        }
    }

    /// <summary>
    /// Old-pipeline GBuffer draw. Called from a transpiler on
    /// <c>MyGBufferPass.RecordCommandsInternal</c> (not a Harmony Prefix —
    /// Prefix on every voxel proxy was Thread CPU Load).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void OnGBufferProxy(MyGBufferPass pass, MyRenderableProxy proxy)
    {
        Interlocked.Increment(ref hookCallsThisFrame);
        if (IsVoxelProxy(proxy))
        {
            Interlocked.Increment(ref voxelSkipThisFrame);
            return;
        }

        var rc = pass?.RC;
        if (rc == null || proxy == null)
            return;
        OnProxyDraw(rc, proxy);
    }

    /// <summary>
    /// Stage 2 GBuffer group. Keen's <c>StartInstanceLocation</c> offsets the
    /// instance VB only; <c>SV_InstanceID</c> stays 0-based, so t15 must add
    /// <paramref name="instanceBase"/> (<c>OffsetInInstanceBuffer</c>).
    /// </summary>
    /// <summary>
    /// <c>MyRenderPass.ProcessRenderData</c> calls this immediately before
    /// <c>DrawInstanceLodGroup</c> (every pass type). GBuffer-only bind.
    /// </summary>
    public static void BindStage2BeforeDraw(MyRenderPass pass, MyRenderContext rc, MyInstanceLodGroup group)
    {
        if (!(pass is MyGBufferRenderPass))
            return;
        BindStage2Group(rc, group.OffsetInInstanceBuffer);
    }

    public static void BindStage2Group(MyRenderContext rc, int instanceBase)
    {
        Interlocked.Increment(ref stage2GroupBindsThisFrame);
        Volatile.Write(ref stage2LastInstanceBaseThisFrame, instanceBase);
        if (!InjectionWanted || rc == null || !rc.IsInitialized)
            return;
        try
        {
            BindVelocityCb(rc, hasPrevWorld: false, default, default, default, boneCount: 0,
                instanceBase: (uint)Math.Max(instanceBase, 0));
        }
        catch (Exception e)
        {
            Fail("stage2 group: " + e.Message, e);
        }
    }

    public static void OnProxyDraw(MyRenderContext rc, MyRenderableProxy proxy)
    {
        if (!InjectionWanted || rc == null || !rc.IsInitialized)
            return;

        var actor = proxy.Parent?.Owner;
        var actorId = actor != null ? actor.ID : 0;
        if (actorId == 0)
        {
            Interlocked.Increment(ref idZeroThisFrame);
            return;
        }

        var common = proxy.CommonObjectData;
        ActorHistory.Instance.RecordGBufferLocal(actorId, common.m_row0, common.m_row1, common.m_row2);

        var wantsBones = proxy.SkinningMatrices != null;
        var hasPrevWorld = TryPackOldPipelineDelta(actorId, common,
            out var row0, out var row1, out var row2);

        Interlocked.Increment(ref proxiesThisFrame);
        if (hasPrevWorld)
            Interlocked.Increment(ref proxiesWithPrevThisFrame);
        else
            Interlocked.Increment(ref histMissThisFrame);

        try
        {
            var boneCount = 0;
            if (wantsBones)
            {
                lock (Gate)
                    boneCount = PackPrevBones(rc, actorId, proxy.SkinningMatrices, proxy.DrawSubmesh.BonesMapping);
            }

            BindVelocityCb(rc, hasPrevWorld, row0, row1, row2, (uint)boneCount, instanceBase: 0);
        }
        catch (Exception e)
        {
            Fail("proxy draw: " + e.Message, e);
        }
    }

    public static void PublishAndAdvanceHistory()
    {
        lock (Gate)
        {
            try
            {
                PublishUnlocked();
            }
            catch (Exception e)
            {
                Fail("publish: " + e.Message, e);
                VelocityRegistry.SetActive(UnavailableVelocityBuffer.Instance);
            }
        }
    }

    public static void OnResolutionChanged()
    {
        lock (Gate)
        {
            GBufferAttachments.OnResolutionChanged();
            Volatile.Write(ref resourcesReady, 0);
            if (!InjectionWanted && !GBufferAttachments.HasColorTargets)
                return;
            try
            {
                EnsureTargetUnlocked();
            }
            catch (Exception e)
            {
                Fail("resize: " + e.Message, e);
            }
        }
    }

    public static void Release()
    {
        lock (Gate)
        {
            IsLive = false;
            Volatile.Write(ref resourcesReady, 0);
            CameraVelocityBuffer.Instance.Clear();
            DisposeTarget();
            DisposePrevAndCb();
            DisposeVelocityCbPools();
            DisposeBlendOverrides();
            GBufferAttachments.Release();
        }
    }

    static void EnsureResourcesUnlocked()
    {
        EnsureTargetUnlocked();
        EnsurePrevBufferUnlocked(Math.Max(prevCount, 1));
        EnsureBoneBufferUnlocked();
        if (target != null && prevWorld != null)
            Volatile.Write(ref resourcesReady, 1);
    }

    static void BindToPass(MyRenderContext rc, MyGBuffer gbuffer)
    {
        if (target == null || prevWorld == null)
            return;
        if (gbuffer.GbufferRtvs == null || gbuffer.GbufferRtvs.Length < 3 || gbuffer.DepthStencil?.Dsv == null)
            return;

        var rtvs = BuildPassRtvs(gbuffer);
        rc.SetRtvs(gbuffer.DepthStencil.Dsv, rtvs);
        activeGBufferContext = rc;
        activeGBufferDsvBind = gbuffer.DepthStencil.Dsv;
        activeGBufferDsv = gbuffer.DepthStencil.Dsv.Dsv;
        activeGBufferBaseRtvs = gbuffer.GbufferRtvs;
        activeGBufferRtvs = rtvs;
        activeVelocityCbPool = GetVelocityCbPool(rc);
        activeVelocityCb = null;
        activePixelProbeCb = null;
        drawBoundaryInspected = false;
        Interlocked.Increment(ref mrtBindsThisFrame);
        BindVelocityCb(rc, hasPrevWorld: false, default, default, default, boneCount: 0, instanceBase: 0);
        rc.VertexShader.SetSrv(PrevWorldSlot, prevWorld);
        if (prevBones != null)
            rc.VertexShader.SetSrv(PrevBoneSlot, prevBones);

        IsLive = ShaderCompileIntercept.GBufferOverlayPresent;
        LastError = ShaderCompileIntercept.GBufferOverlayPresent
            ? null
            : (KeenShaderGuard.LastError ?? "Keen GBuffer patch not applied");
        loggedError = false;
    }

    static RenderTargetView[] BuildPassRtvs(MyGBuffer gbuffer)
    {
        var hasExtras = GBufferAttachments.HasColorTargets;
        var extraMax = hasExtras ? GBufferAttachments.MaxBoundTarget : 3;
        if (hasExtras)
        {
            var extrasSize = gbuffer.GBuffer0?.Size ?? MyRender11.ResolutionI;
            var samples = Math.Max(gbuffer.SamplesCount, 1);
            GBufferAttachments.EnsureTargets(extrasSize.X, extrasSize.Y, samples, gbuffer.SamplesQuality);
        }

        var audit = AuditProofWanted && auditTarget?.Rtv != null;
        var n = Math.Max(audit ? 8 : 4, extraMax + 1);
        // This array stays active until the pass ends. A fresh array avoids a
        // later pass on the same worker mutating the native RTV set retained by
        // an already-recording deferred context.
        var rtvs = new RenderTargetView[n];
        rtvs[0] = gbuffer.GbufferRtvs[0];
        rtvs[1] = gbuffer.GbufferRtvs[1];
        rtvs[2] = gbuffer.GbufferRtvs[2];
        rtvs[3] = target.Rtv;
        if (hasExtras)
            GBufferAttachments.CopyRtvs(rtvs);
        if (audit)
            rtvs[7] = auditTarget.Rtv;

        var gbufferSize = gbuffer.GBuffer0?.Size ?? MyRender11.ResolutionI;
        var samplesMatch = targetSamples == Math.Max(gbuffer.SamplesCount, 1) &&
                           targetSamplesQuality == gbuffer.SamplesQuality;
        var sizeMatch = targetWidth == gbufferSize.X && targetHeight == gbufferSize.Y;
        Volatile.Write(ref targetContractStatus,
            "velocity=" + targetWidth + "x" + targetHeight + "/s" + targetSamples + "q" + targetSamplesQuality +
            " gbuffer=" + gbufferSize.X + "x" + gbufferSize.Y + "/s" + Math.Max(gbuffer.SamplesCount, 1) +
            "q" + gbuffer.SamplesQuality + " match=" + (sizeMatch && samplesMatch ? "yes" : "NO"));
        return rtvs;
    }

    static void PublishUnlocked()
    {
        if (target == null)
            return;

        IRtvTexture publish = target;
        if (targetSamples > 1)
        {
            EnsureResolvedUnlocked(targetWidth, targetHeight);
            var rc = MyRender11.RC;
            if (resolved == null || rc?.DeviceContext == null)
                return;
            rc.DeviceContext.ResolveSubresource(target.Resource, 0, resolved.Resource, 0, Format.R16G16_Float);
            publish = resolved;
        }

        var native = publish.Resource != null ? publish.Resource.NativePointer : IntPtr.Zero;
        CameraVelocityBuffer.Instance.Publish(publish, native, targetWidth, targetHeight, historyValidThisFrame);
        VelocityRegistry.SetActive(CameraVelocityBuffer.Instance);
        CameraVelocityPass.AdvanceHistory();
        LastError = null;
    }

    static void UploadPrevWorldUnlocked(MyRenderContext rc)
    {
        var count = Math.Max(prevCount, 1);
        EnsurePrevBufferUnlocked(count);
        if (prevWorld == null)
            return;
        if (rc == null || !rc.IsInitialized)
            return;
        if (cpuPrev.Length < count)
        {
            var next = new PrevInstance[count];
            Array.Copy(cpuPrev, next, cpuPrev.Length);
            cpuPrev = next;
        }

        var mapping = MyMapping.MapDiscard(rc, prevWorld);
        mapping.WriteAndPosition(cpuPrev, count, 0);
        mapping.Unmap();
        Interlocked.Increment(ref stage2UploadsThisFrame);
        if (rc.IsDeferred)
            Volatile.Write(ref stage2UploadDeferredThisFrame, 1);
    }

    static void EnsureTargetUnlocked()
    {
        var gbuffer = MyGBuffer.Main;
        var size = gbuffer?.GBuffer0?.Size ?? MyRender11.ResolutionI;
        if (size.X <= 0 || size.Y <= 0)
            return;

        var samples = Math.Max(gbuffer?.SamplesCount ?? 1, 1);
        var quality = gbuffer?.SamplesQuality ?? 0;
        if ((Config.Current?.Target3Checkpoint ?? Target3Checkpoint.Live) == Target3Checkpoint.Live &&
            checkpointTarget != null)
            DisposeCheckpointTarget();
        var auditReady = AuditProofWanted ? auditTarget != null : auditTarget == null;
        if (target != null && auditReady && targetWidth == size.X && targetHeight == size.Y &&
            targetSamples == samples && targetSamplesQuality == quality)
            return;

        DisposeTarget();
        target = MyManagers.RwTextures.CreateRtv("Anomaly.GBufferVelocity", size.X, size.Y, Format.R16G16_Float,
            samples, quality);
        if (AuditProofWanted)
            auditTarget = MyManagers.RwTextures.CreateRtv("Anomaly.VelocityPipelineAudit", size.X, size.Y,
                Format.R16G16B16A16_Float, samples, quality);
        targetWidth = size.X;
        targetHeight = size.Y;
        targetSamples = samples;
        targetSamplesQuality = quality;
        if (samples <= 1)
        {
            if (resolved != null)
                MyManagers.RwTextures.DisposeTex(ref resolved);
            resolved = null;
        }

        Volatile.Write(ref targetContractStatus,
            "velocity=" + size.X + "x" + size.Y + "/s" + samples + "q" + quality + " gbuffer=pending");
        DebugLog.Write("GBufferVelocity RT " + size.X + "x" + size.Y + " samples=" + samples + " quality=" + quality);
    }

    static void EnsureResolvedUnlocked(int width, int height)
    {
        if (resolved != null && resolved.Size.X == width && resolved.Size.Y == height)
            return;
        if (resolved != null)
            MyManagers.RwTextures.DisposeTex(ref resolved);
        resolved = MyManagers.RwTextures.CreateRtv("Anomaly.GBufferVelocity.Resolved", width, height, Format.R16G16_Float);
    }

    static void EnsureAuditResolvedUnlocked(int width, int height)
    {
        if (auditResolved != null && auditResolved.Size.X == width && auditResolved.Size.Y == height)
            return;
        if (auditResolved != null)
            MyManagers.RwTextures.DisposeTex(ref auditResolved);
        auditResolved = MyManagers.RwTextures.CreateRtv(
            "Anomaly.VelocityPipelineAudit.Resolved", width, height, Format.R16G16B16A16_Float);
    }

    static void CaptureTarget3CheckpointUnlocked(Target3Checkpoint checkpoint, MyRenderContext rc)
    {
        var cfg = Config.Current;
        if (cfg == null || cfg.Target3Checkpoint != checkpoint ||
            (cfg.DebugBuffer != DebugBuffer.GBufferVelocityRaw &&
             cfg.DebugBuffer != DebugBuffer.VelocityPipelineAudit) || target?.Resource == null ||
            rc?.DeviceContext == null)
            return;

        EnsureCheckpointTargetUnlocked();
        var audit = cfg.DebugBuffer == DebugBuffer.VelocityPipelineAudit;
        if (checkpointTarget?.Resource == null ||
            (audit && auditCheckpointTarget?.Resource == null))
            return;

        rc.DeviceContext.CopyResource(target.Resource, checkpointTarget.Resource);
        if (audit)
            rc.DeviceContext.CopyResource(auditTarget.Resource, auditCheckpointTarget.Resource);
        capturedCheckpoint = checkpoint;
        checkpointCaptureFrame = FrameTemporal.FrameIndex;
        checkpointCaptureCount++;
        checkpointLastError = null;
    }

    static void EnsureCheckpointTargetUnlocked()
    {
        var audit = Config.Current?.DebugBuffer == DebugBuffer.VelocityPipelineAudit;
        if (checkpointTarget != null && checkpointTarget.Size.X == targetWidth &&
            checkpointTarget.Size.Y == targetHeight &&
            (!audit || (auditCheckpointTarget != null &&
                        auditCheckpointTarget.Size.X == targetWidth &&
                        auditCheckpointTarget.Size.Y == targetHeight)))
            return;

        DisposeCheckpointTarget();
        checkpointTarget = MyManagers.RwTextures.CreateRtv(
            "Anomaly.GBufferVelocity.Checkpoint", targetWidth, targetHeight, Format.R16G16_Float,
            targetSamples, targetSamplesQuality);
        if (audit)
        {
            auditCheckpointTarget = MyManagers.RwTextures.CreateRtv(
                "Anomaly.VelocityPipelineAudit.Checkpoint", targetWidth, targetHeight,
                Format.R16G16B16A16_Float, targetSamples, targetSamplesQuality);
        }
    }

    static void EnsureCheckpointResolvedUnlocked()
    {
        if (checkpointResolved != null && checkpointResolved.Size.X == targetWidth &&
            checkpointResolved.Size.Y == targetHeight)
            return;
        if (checkpointResolved != null)
            MyManagers.RwTextures.DisposeTex(ref checkpointResolved);
        checkpointResolved = MyManagers.RwTextures.CreateRtv(
            "Anomaly.GBufferVelocity.Checkpoint.Resolved", targetWidth, targetHeight, Format.R16G16_Float);
    }

    static void EnsureAuditCheckpointResolvedUnlocked()
    {
        if (auditCheckpointResolved != null && auditCheckpointResolved.Size.X == targetWidth &&
            auditCheckpointResolved.Size.Y == targetHeight)
            return;
        if (auditCheckpointResolved != null)
            MyManagers.RwTextures.DisposeTex(ref auditCheckpointResolved);
        auditCheckpointResolved = MyManagers.RwTextures.CreateRtv(
            "Anomaly.VelocityPipelineAudit.Checkpoint.Resolved", targetWidth, targetHeight,
            Format.R16G16B16A16_Float);
    }

    static void EnsurePrevBufferUnlocked(int elements)
    {
        elements = Math.Max(elements, 1);
        if (prevWorld != null && prevCapacity >= elements)
            return;
        if (prevWorld == null)
        {
            prevWorld = MyManagers.Buffers.CreateSrv("Anomaly.PrevWorld", elements, PrevStride,
                usage: ResourceUsage.Dynamic);
            prevCapacity = elements;
            return;
        }

        MyManagers.Buffers.Resize(prevWorld, elements, PrevStride, null);
        prevCapacity = elements;
    }

    static void EnsureBoneBufferUnlocked()
    {
        if (prevBones != null)
            return;
        prevBones = MyManagers.Buffers.CreateSrv("Anomaly.PrevBones", BoneHistory.MaxBones, BoneStride,
            usage: ResourceUsage.Dynamic);
    }

    static ISrvBindable PrepareCompositeSourceUnlocked()
    {
        if (target == null)
            return null;
        if (targetSamples > 1)
        {
            EnsureResolvedUnlocked(targetWidth, targetHeight);
            var rc = MyRender11.RC;
            if (resolved == null || rc?.DeviceContext == null)
                return null;
            rc.DeviceContext.ResolveSubresource(target.Resource, 0, resolved.Resource, 0, Format.R16G16_Float);
            return resolved;
        }

        return target;
    }

    static IntPtr ResourceNative(ISrvBindable resource)
    {
        if (resource is IRtvTexture texture && texture.Resource != null)
            return texture.Resource.NativePointer;
        return IntPtr.Zero;
    }

    static string NativeId(IRtvTexture texture) =>
        texture?.Resource == null ? "-" : NativeId(texture.Resource.NativePointer);

    static string NativeId(IntPtr pointer) =>
        pointer == IntPtr.Zero ? "-" : "0x" + pointer.ToInt64().ToString("X");

    static int PackPrevBones(MyRenderContext rc, uint actorId, Matrix[] current, int[] mapping)
    {
        if (current == null || actorId == 0 || prevBones == null)
            return 0;
        if (!BoneHistory.Instance.TryGetPrevious(actorId, current.Length, out var previous) ||
            previous == null)
            return 0;

        Array.Clear(BoneScratch, 0, BoneScratch.Length);
        var count = 0;
        if (mapping == null)
        {
            count = Math.Min(BoneHistory.MaxBones, previous.Length);
            Array.Copy(previous, BoneScratch, count);
        }
        else
        {
            count = Math.Min(BoneHistory.MaxBones, mapping.Length);
            for (var i = 0; i < count; i++)
            {
                var idx = mapping[i];
                if (idx >= 0 && idx < previous.Length)
                    BoneScratch[i] = previous[idx];
            }
        }

        if (count == 0)
            return 0;

        var mappingGpu = MyMapping.MapDiscard(rc, prevBones);
        mappingGpu.WriteAndPosition(BoneScratch, count, 0);
        mappingGpu.Unmap();
        return count;
    }

    /// <summary>
    /// Per-draw CB from an Anomaly-owned ring scoped to this
    /// <see cref="MyRenderContext"/>. A CB is mapped only once per frame. This
    /// avoids both Keen's size-keyed object-CB cache and repeated MapDiscard
    /// renaming while deferred command lists are being recorded.
    /// </summary>
    static bool BindVelocityCb(MyRenderContext rc, bool hasPrevWorld, Vector4 row0, Vector4 row1, Vector4 row2,
        uint boneCount, uint instanceBase)
    {
        if (!CameraVelocityPass.TryGetMotionFrame(out var unjittered, out var prevVp, out _,
                out var historyValid, out var size))
            return false;
        historyValidThisFrame = historyValid;
        var probeMode = (uint)(Config.Current?.VelocityProbe ?? VelocityProbe.Off);
        // MrtWrite is the lowest-level shader/binding probe. Give both stages
        // immutable, stage-specific bytes so the result cannot be affected by
        // deferred-context mapping, CB-ring lifetime, or partial/shared cbuffer
        // layout. b6 tests the VS path; the dedicated 16-byte b7 tests the final
        // PS Target3 write independently.
        var immutableProbe = probeMode == (uint)VelocityProbe.MrtWrite;
        var cb = immutableProbe ? GetMrtWriteProbeCb() : AcquireVelocityCb(rc);
        var pixelProbeCb = GetPixelProbeCb(immutableProbe);
        if (cb == null || pixelProbeCb == null)
            return false;
        if (!immutableProbe)
        {
            WriteConstants(rc, cb, unjittered, prevVp, size, historyValid,
                (uint)Math.Max(prevCount, 1), hasPrevWorld, row0, row1, row2, boneCount, instanceBase, probeMode);
        }
        // Set through Keen's tracker, then issue the native bind as a hard
        // boundary. This remains correct if another subsystem touched b6
        // without updating MyVertexStage's object-identity cache.
        rc.VertexShader.SetConstantBuffer(ConstantSlot, cb);
        rc.DeviceContext.VertexShader.SetConstantBuffer(ConstantSlot, cb.Buffer);
        rc.PixelShader.SetConstantBuffer(PixelProbeConstantSlot, pixelProbeCb);
        BindNativeVelocityConstantBuffers(rc.DeviceContext, cb.Buffer, pixelProbeCb.Buffer);
        if (ReferenceEquals(activeGBufferContext, rc))
        {
            activeVelocityCb = cb;
            activePixelProbeCb = pixelProbeCb;
        }
        Interlocked.Increment(ref velocityCbBindsThisFrame);
        if (immutableProbe)
            Interlocked.Increment(ref immutableProbeCbBindsThisFrame);
        Volatile.Write(ref lastProbeModeThisFrame, (int)probeMode);
        return true;
    }

    /// <summary>
    /// Bind the complete Anomaly-owned constant buffers at the native draw
    /// boundary. These buffers are dedicated 256-byte allocations, so ranged
    /// D3D11.1 binding provides no benefit. Whole-buffer binds also clear any
    /// inherited first/count window before the deferred command list records
    /// the draw.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void BindNativeVelocityConstantBuffers(DeviceContext1 context, SharpDX.Direct3D11.Buffer velocity,
        SharpDX.Direct3D11.Buffer pixelProbe)
    {
        context.VertexShader.SetConstantBuffer(ConstantSlot, velocity);
        context.PixelShader.SetConstantBuffer(PixelProbeConstantSlot, pixelProbe);
        Interlocked.Increment(ref velocityCbWholeBindsThisFrame);
    }

    static IConstantBuffer GetMrtWriteProbeCb()
    {
        lock (VelocityCbGate)
        {
            if (mrtWriteProbeCb != null)
                return mrtWriteProbeCb;

            var constants = new Constants
            {
                ProbeMode = (uint)VelocityProbe.MrtWrite
            };
            var data = Marshal.AllocHGlobal(ConstantBufferBytes);
            try
            {
                Marshal.Copy(new byte[ConstantBufferBytes], 0, data, ConstantBufferBytes);
                Marshal.StructureToPtr(constants, data, false);
                mrtWriteProbeCb = MyManagers.Buffers.CreateConstantBuffer(
                    "Anomaly.VelocityCB.MrtWriteProbe", ConstantBufferBytes, data, ResourceUsage.Immutable);
            }
            finally
            {
                Marshal.FreeHGlobal(data);
            }
            return mrtWriteProbeCb;
        }
    }

    static IConstantBuffer GetPixelProbeCb(bool mrtWrite)
    {
        lock (VelocityCbGate)
        {
            var existing = mrtWrite ? mrtWritePixelProbeCb : pixelProbeOffCb;
            if (existing != null)
                return existing;

            var constants = new PixelProbeConstants
            {
                Value = mrtWrite ? new Vector4(8f, 0f, 1f, 0f) : Vector4.Zero
            };
            var data = Marshal.AllocHGlobal(PixelProbeConstantBufferBytes);
            try
            {
                Marshal.Copy(new byte[PixelProbeConstantBufferBytes], 0, data, PixelProbeConstantBufferBytes);
                Marshal.StructureToPtr(constants, data, false);
                var created = MyManagers.Buffers.CreateConstantBuffer(
                    mrtWrite ? "Anomaly.VelocityPixelProbe.MrtWrite" : "Anomaly.VelocityPixelProbe.Off",
                    PixelProbeConstantBufferBytes, data, ResourceUsage.Immutable);
                if (mrtWrite)
                    mrtWritePixelProbeCb = created;
                else
                    pixelProbeOffCb = created;
                return created;
            }
            finally
            {
                Marshal.FreeHGlobal(data);
            }
        }
    }

    static VelocityCbPool GetVelocityCbPool(MyRenderContext rc)
    {
        if (rc == null)
            return null;
        lock (VelocityCbGate)
        {
            if (VelocityCbPools.TryGetValue(rc, out var pool))
                return pool;
            pool = new VelocityCbPool(++nextVelocityCbPoolId);
            VelocityCbPools.Add(rc, pool);
            return pool;
        }
    }

    static IConstantBuffer AcquireVelocityCb(MyRenderContext rc)
    {
        var pool = ReferenceEquals(activeGBufferContext, rc) ? activeVelocityCbPool : null;
        pool ??= GetVelocityCbPool(rc);
        if (pool == null)
            return null;

        // A render context records on one worker at a time. Lock only this
        // context's pool so independent Stage 2 workers remain parallel.
        lock (pool)
        {
            var index = pool.Cursor++;
            while (pool.Buffers.Count <= index)
            {
                var bufferIndex = pool.Buffers.Count;
                var buffer = MyManagers.Buffers.CreateConstantBuffer(
                    "Anomaly.VelocityCB." + pool.Id + "." + bufferIndex,
                    ConstantBufferBytes, usage: ResourceUsage.Dynamic);
                pool.Buffers.Add(buffer);
                Interlocked.Increment(ref velocityCbPoolCapacity);
            }

            AtomicMax(ref velocityCbPoolPeakThisFrame, index + 1);
            return pool.Buffers[index];
        }
    }

    static void WriteConstants(MyRenderContext rc, IConstantBuffer dest, Matrix unjittered, Matrix prevVp, Vector2I size,
        bool historyValid, uint packedCount, bool hasPrevWorld, Vector4 row0, Vector4 row1, Vector4 row2, uint boneCount,
        uint instanceBase, uint probeMode)
    {
        if (dest == null || size.X <= 0 || size.Y <= 0)
            return;
        var cb = new Constants
        {
            UnjitteredViewProj = unjittered,
            PrevViewProj = prevVp,
            RenderSize = new Vector2(size.X, size.Y),
            InvRenderSize = new Vector2(1f / size.X, 1f / size.Y),
            PrevCount = packedCount,
            HasHistory = historyValid ? 1u : 0u,
            HasPrevWorld = hasPrevWorld ? 1u : 0u,
            BoneCount = boneCount,
            PrevRow0 = row0,
            PrevRow1 = row1,
            PrevRow2 = row2,
            InstanceBase = instanceBase,
            ProbeMode = probeMode,
            CameraDelta = historyValid && CameraVelocityPass.TryGetPrevCamera(out var previousCamera)
                ? (Vector3)(MyRender11.Environment.Matrices.CameraPosition - previousCamera)
                : Vector3.Zero
        };
        var mapping = MyMapping.MapDiscard(rc, dest);
        mapping.WriteAndPosition(ref cb);
        mapping.Unmap();
    }

    /// <summary>
    /// Old-pipeline CB rows are <c>currToPrev</c> (row-vector):
    /// <c>prevPos = positionLocal * Invert(curr) * prev</c>, same packing as
    /// <c>MyObjectDataCommon.LocalMatrix</c>. The VS multiplies only; it must
    /// not invert <c>local_matrix</c> again.
    /// </summary>
    static bool TryPackOldPipelineDelta(uint actorId, MyObjectDataCommon common,
        out Vector4 row0, out Vector4 row1, out Vector4 row2)
    {
        row0 = default;
        row1 = default;
        row2 = default;
        if (ActorHistory.Instance.WasTeleported(actorId))
            return false;

        Matrix prevM;
        if (ActorHistory.Instance.TryGetPreviousLocal(actorId, out var p0, out var p1, out var p2))
            prevM = UnpackRows(p0, p1, p2);
        else if (CameraVelocityPass.TryGetPrevCamera(out var prevCam) &&
                 ActorHistory.Instance.TryGetPrevious(actorId, out var world))
        {
            var packed = Pack(world, prevCam);
            prevM = UnpackRows(packed.Col0, packed.Col1, packed.Col2);
        }
        else
            return false;

        var currM = UnpackRows(common.m_row0, common.m_row1, common.m_row2);
        Matrix.Invert(ref currM, out var inv);
        PackRows(inv * prevM, out row0, out row1, out row2);
        return true;
    }

    static Matrix UnpackRows(Vector4 r0, Vector4 r1, Vector4 r2)
    {
        return new Matrix(
            r0.X, r1.X, r2.X, 0f,
            r0.Y, r1.Y, r2.Y, 0f,
            r0.Z, r1.Z, r2.Z, 0f,
            r0.W, r1.W, r2.W, 1f);
    }

    static void PackRows(Matrix m, out Vector4 r0, out Vector4 r1, out Vector4 r2)
    {
        r0 = new Vector4(m.M11, m.M21, m.M31, m.M41);
        r1 = new Vector4(m.M12, m.M22, m.M32, m.M42);
        r2 = new Vector4(m.M13, m.M23, m.M33, m.M43);
    }

    static PrevInstance Pack(MatrixD world, Vector3D camera)
    {
        var t = world.Translation - camera;
        return new PrevInstance
        {
            Col0 = new Vector4((float)world.M11, (float)world.M21, (float)world.M31, (float)t.X),
            Col1 = new Vector4((float)world.M12, (float)world.M22, (float)world.M32, (float)t.Y),
            Col2 = new Vector4((float)world.M13, (float)world.M23, (float)world.M33, (float)t.Z),
            Flags = new Vector4(1f, 0f, 0f, 0f)
        };
    }

    static void DisposeTarget()
    {
        DisposeCheckpointTarget();
        if (target != null)
            MyManagers.RwTextures.DisposeTex(ref target);
        if (resolved != null)
            MyManagers.RwTextures.DisposeTex(ref resolved);
        if (auditTarget != null)
            MyManagers.RwTextures.DisposeTex(ref auditTarget);
        if (auditResolved != null)
            MyManagers.RwTextures.DisposeTex(ref auditResolved);
        target = null;
        resolved = null;
        auditTarget = null;
        auditResolved = null;
        targetWidth = 0;
        targetHeight = 0;
        targetSamples = 0;
        targetSamplesQuality = 0;
        Volatile.Write(ref targetContractStatus, "not allocated");
    }

    static void DisposeCheckpointTarget()
    {
        if (checkpointTarget != null)
            MyManagers.RwTextures.DisposeTex(ref checkpointTarget);
        if (checkpointResolved != null)
            MyManagers.RwTextures.DisposeTex(ref checkpointResolved);
        if (auditCheckpointTarget != null)
            MyManagers.RwTextures.DisposeTex(ref auditCheckpointTarget);
        if (auditCheckpointResolved != null)
            MyManagers.RwTextures.DisposeTex(ref auditCheckpointResolved);
        checkpointTarget = null;
        checkpointResolved = null;
        auditCheckpointTarget = null;
        auditCheckpointResolved = null;
        capturedCheckpoint = Target3Checkpoint.Live;
        checkpointCaptureFrame = -1;
        checkpointCaptureCount = 0;
        checkpointDebugSourceNative = IntPtr.Zero;
        checkpointLastError = null;
    }

    static void DisposePrevAndCb()
    {
        if (prevWorld != null)
        {
            MyManagers.Buffers.Dispose(new ISrvBindable[] { prevWorld });
            prevWorld = null;
        }

        if (prevBones != null)
        {
            MyManagers.Buffers.Dispose(new ISrvBindable[] { prevBones });
            prevBones = null;
        }

        prevCapacity = 0;
        prevCount = 0;
    }

    static void DisposeVelocityCbPools()
    {
        lock (VelocityCbGate)
        {
            if (mrtWriteProbeCb != null)
            {
                MyManagers.Buffers.Dispose(new[] { mrtWriteProbeCb });
                mrtWriteProbeCb = null;
            }
            if (pixelProbeOffCb != null)
            {
                MyManagers.Buffers.Dispose(new[] { pixelProbeOffCb });
                pixelProbeOffCb = null;
            }
            if (mrtWritePixelProbeCb != null)
            {
                MyManagers.Buffers.Dispose(new[] { mrtWritePixelProbeCb });
                mrtWritePixelProbeCb = null;
            }
            if (mrtWriteProbeReadback != null)
            {
                MyManagers.Buffers.Dispose(new[] { mrtWriteProbeReadback });
                mrtWriteProbeReadback = null;
            }
            if (mrtWritePixelProbeReadback != null)
            {
                MyManagers.Buffers.Dispose(new[] { mrtWritePixelProbeReadback });
                mrtWritePixelProbeReadback = null;
            }
            mrtWriteProbeReadbackError = null;
            Volatile.Write(ref mrtWritePixelProbeReadbackValue, null);
            Volatile.Write(ref mrtWriteProbeReadbackValue, int.MinValue);
            Volatile.Write(ref mrtWriteProbeReadbackState, 0);

            foreach (var pool in VelocityCbPools.Values)
            {
                lock (pool)
                {
                    if (pool.Buffers.Count > 0)
                        MyManagers.Buffers.Dispose(pool.Buffers.ToArray());
                    pool.Buffers.Clear();
                    pool.Cursor = 0;
                }
            }

            VelocityCbPools.Clear();
            nextVelocityCbPoolId = 0;
            Volatile.Write(ref velocityCbPoolCapacity, 0);
            activeVelocityCbPool = null;
            activeVelocityCb = null;
            activePixelProbeCb = null;
        }
    }

    static void DisposeBlendOverrides()
    {
        lock (BlendGate)
        {
            var snapshot = blendOverrides;
            blendOverrides = Array.Empty<BlendOverride>();
            for (var i = 0; i < snapshot.Length; i++)
                snapshot[i].State?.Dispose();
        }
    }

    static void Fail(string message, Exception e)
    {
        LastError = message;
        IsLive = false;
        if (loggedError)
            return;
        loggedError = true;
        MyLog.Default.WriteLine("Anomaly GBuffer velocity: " + message);
        DebugLog.Write("GBufferVelocity " + message + (e != null ? "\n" + e : ""));
    }
}
