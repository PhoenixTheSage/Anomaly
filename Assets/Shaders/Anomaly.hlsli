#ifndef ANOMALY_HLSLI
#define ANOMALY_HLSLI

#include <Common.hlsli>
#include <Anomaly/PackFingerprint.hlsli>

// Compile intercept: ANOMALY=1 on every permutation.
// ANOMALY_VELOCITY is GBuffer-only (RENDERING_PASS == 0). Depth must never see it.
// Velocity reconstruct is VS-only (PixelStage defines ANOMALY_PIXEL_STAGE).
//
// t15 is packed in Stage 2 instance-buffer order (same slots as Keen's VB).
// The VS indexes t15[SV_InstanceID + AnomalyInstanceBase]. SV_InstanceID is
// 0-based per draw; InstanceBase is the group's OffsetInInstanceBuffer
// (Keen StartInstanceLocation only offsets the instance VB, not SV_InstanceID).
// Stage 2: previous world as camera-relative 4x3; VS inverts local_matrix.
// Old cube: PrevRow is CPU currToPrev — do not invert again.
// Slot 16 is previous bones for the current GBuffer draw (old pipeline skinning).
// Slot 6: Keen old-pipeline Begin binds voxel materials here; Anomaly
// overwrites VS b6 with an Anomaly-owned velocity CB (per deferred context,
// one ring entry per update). Developer probes are runtime modes in this CB.
// Geometry VS: 0 frame, 1 projection, 2 object, 3 material, 4 foliage,
// 5 alphamask, 7 forward. MrtWrite emits a known value in the active VS and
// again at the final GBuffer PS output through pixel b7, isolating Target3
// writes from shader-cache identity and VS interpolation.
#define ANOMALY_CB_SLOT 6
#define ANOMALY_PREV_SLOT 15
#define ANOMALY_BONE_SLOT 16

#ifdef ANOMALY_VELOCITY
#ifdef ANOMALY_PIXEL_STAGE
// Final Target3 wire probe.  Pixel b7 is unused by Keen's GBuffer material
// shaders (voxel material constants occupy pixel b6). A dedicated 16-byte
// payload selects MrtWrite independently of the vertex constants.
cbuffer AnomalyVelocityPixelProbe : register(b7)
{
    // Keep the pixel-output diagnostic independent from the 240-byte VS b6
    // layout.  A dedicated float4 at byte zero avoids partial-cbuffer and
    // cross-stage buffer-layout ambiguity while resident GBuffer shaders are
    // being tested.
    float4 AnomalyPixelProbe;
};
#else
cbuffer AnomalyVelocity : register(MERGE(b, ANOMALY_CB_SLOT))
{
    float4x4 AnomalyUnjitteredViewProj;
    float4x4 AnomalyPrevViewProj;
    float2 AnomalyRenderSize;
    float2 AnomalyInvRenderSize;
    uint AnomalyPrevCount;
    uint AnomalyHasHistory;
    uint AnomalyHasPrevWorld;
    uint AnomalyBoneCount;
    float4 AnomalyPrevRow0;
    float4 AnomalyPrevRow1;
    float4 AnomalyPrevRow2;
    uint AnomalyInstanceBase;
    uint AnomalyProbeMode;
    float2 AnomalyPadding;
    float3 AnomalyCameraDelta;
};

// Keen's construct_matrix_43 lives in Geometry/VertexTemplateBase.hlsli (VS only).
// GBuffer PixelStage includes this file and must not depend on that helper.
matrix AnomalyConstructMatrix43(float4 a, float4 b, float4 c)
{
    return transpose(matrix(a, b, c, float4(0, 0, 0, 1)));
}

struct AnomalyPrevInstance
{
    float4 col0;
    float4 col1;
    float4 col2;
    float4 flags; // x = 1 when previous world is valid (not first frame / teleport / static / clipmap)
};

StructuredBuffer<AnomalyPrevInstance> AnomalyPrevWorld : register(MERGE(t, ANOMALY_PREV_SLOT));
StructuredBuffer<float4x4> AnomalyPrevBones : register(MERGE(t, ANOMALY_BONE_SLOT));

float2 AnomalyClipToPixelDelta(float4 currClip, float4 prevClip)
{
    currClip /= max(currClip.w, 1e-6);
    prevClip /= max(prevClip.w, 1e-6);
    float2 currUv = float2(currClip.x * 0.5 + 0.5, 0.5 - currClip.y * 0.5);
    float2 prevUv = float2(prevClip.x * 0.5 + 0.5, 0.5 - prevClip.y * 0.5);
    // Backward reprojection: previousPixel = currentPixel + motion.
    return (prevUv - currUv) * AnomalyRenderSize;
}

float3 AnomalyWorldToObject(float3 world, matrix m)
{
    float3 t = m._41_42_43;
    float3x3 r = (float3x3)m;
    float3 c0 = r._11_12_13;
    float3 c1 = r._21_22_23;
    float3 c2 = r._31_32_33;
    float3x3 adj = float3x3(cross(c1, c2), cross(c2, c0), cross(c0, c1));
    float det = dot(c0, adj._11_12_13);
    float safeDet = abs(det) < 1e-8 ? (det < 0 ? -1e-8 : 1e-8) : det;
    float3x3 invR = transpose(adj) / safeDet;
    return mul(world - t, invR);
}

#ifdef USE_SKINNING
matrix AnomalyBlendBones(uint4 indices, float4 weights, bool previous)
{
    matrix s = 0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        if (previous)
            s += AnomalyPrevBones[indices[i]] * weights[i];
        else
            s += object_.bone_matrix[indices[i]] * weights[i];
    }
    return s;
}
#endif

float2 AnomalyComputeVelocity(float3 positionLocal, matrix localMatrix, uint svInstanceId, uint4 blendIndices, float4 blendWeights)
{
    // GPU boundary probe: if this does not appear in raw Target3, the active
    // permutation/MRT/state is wrong; history and matrix math are irrelevant.
    if (AnomalyProbeMode == 2)
        return float2(8, 0);

    // C# writes VRageMath.Matrix row-major. CameraVelocity.hlsl uses
    // pack_matrix(row_major). Geometry VS is Keen column-major, so load
    // the 4x4s transposed. t15 / PrevRow 4x3 uses Keen construct_matrix_43.
    float4x4 currVp = transpose(AnomalyUnjitteredViewProj);
    float4x4 prevVp = transpose(AnomalyPrevViewProj);
    float4 currClip = mul(float4(positionLocal, 1), currVp);
    // Positions and history transforms use different camera-relative origins.
    float3 prevPos = positionLocal + AnomalyCameraDelta;

    matrix prevM = localMatrix;
    bool hasPrevWorld = false;

    [branch]
    if (AnomalyHasHistory != 0)
    {
#ifdef USE_SIMPLE_INSTANCING
        uint prevIdx = svInstanceId + AnomalyInstanceBase;
        [branch]
        if (prevIdx < AnomalyPrevCount)
        {
            AnomalyPrevInstance prev = AnomalyPrevWorld[prevIdx];
            [branch]
            if (prev.flags.x > 0.5)
            {
                prevM = AnomalyConstructMatrix43(prev.col0, prev.col1, prev.col2);
                hasPrevWorld = true;
            }
        }
#else
        [branch]
        if (AnomalyHasPrevWorld != 0)
        {
            prevM = AnomalyConstructMatrix43(AnomalyPrevRow0, AnomalyPrevRow1, AnomalyPrevRow2);
            hasPrevWorld = true;
        }
#endif

        // Direct proof that t15 / b6 reached this active VS. Pink (+X) is a
        // hit; cyan (+Y) is a miss in CatalogDebug's velocity map.
        if (AnomalyProbeMode == 3)
            return hasPrevWorld ? float2(8, 0) : float2(0, 8);

#ifdef USE_SIMPLE_INSTANCING
#ifdef USE_SKINNING
        [branch]
        if (hasPrevWorld && AnomalyBoneCount != 0)
        {
            float3 skinnedObj = AnomalyWorldToObject(positionLocal, localMatrix);
            matrix currSkin = AnomalyBlendBones(blendIndices, blendWeights, false);
            float3 mesh = AnomalyWorldToObject(skinnedObj, currSkin);
            matrix prevSkin = AnomalyBlendBones(blendIndices, blendWeights, true);
            float3 prevSkinned = mul(float4(mesh, 1), prevSkin).xyz;
            prevPos = mul(float4(prevSkinned, 1), prevM).xyz;
        }
        else if (hasPrevWorld)
        {
            float3 objectPos = AnomalyWorldToObject(positionLocal, localMatrix);
            prevPos = mul(float4(objectPos, 1), prevM).xyz;
        }
#else
        [branch]
        if (hasPrevWorld)
        {
            float3 objectPos = AnomalyWorldToObject(positionLocal, localMatrix);
            prevPos = mul(float4(objectPos, 1), prevM).xyz;
        }
#endif
#else
        // Old pipeline: PrevRow is CPU currToPrev (Invert(curr)*prev), not a world matrix.
        [branch]
        if (hasPrevWorld)
            prevPos = mul(float4(positionLocal, 1), prevM).xyz;
#endif
        float4 prevClip = mul(float4(prevPos, 1), prevVp);
        return AnomalyClipToPixelDelta(currClip, prevClip);
    }

    if (AnomalyProbeMode == 3)
        return float2(0, 8);

    return float2(0, 0);
}

float2 AnomalyComputeVelocity(float3 positionLocal, matrix localMatrix, uint svInstanceId)
{
    return AnomalyComputeVelocity(positionLocal, localMatrix, svInstanceId, uint4(0, 0, 0, 0), float4(0, 0, 0, 0));
}

#endif // ANOMALY_PIXEL_STAGE
#endif // ANOMALY_VELOCITY

#include <Anomaly/GBufferExtras.hlsli>

#endif
