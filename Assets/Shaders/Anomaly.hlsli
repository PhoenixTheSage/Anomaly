#ifndef ANOMALY_HLSLI
#define ANOMALY_HLSLI

#include <Common.hlsli>
#include <Anomaly/PackFingerprint.hlsli>

// Compile intercept: ANOMALY=1 on every permutation.
// ANOMALY_VELOCITY is GBuffer-only (RENDERING_PASS == 0). Depth must never see it.
//
// Prev-world SRV is packed in Stage 2 instance-buffer order (SV_InstanceID).
// Slot 15 is unused by geometry (lighting BRDF LUT is later; unbind after GBuffer).
// Slot 16 is previous bones for the current GBuffer draw (old pipeline skinning).
// CB slot 6 is unused by geometry (0 frame, 1 projection, 2 object, 3 material, 4 foliage, 5 alphamask, 7 forward).

#define ANOMALY_CB_SLOT 6
#define ANOMALY_PREV_SLOT 15
#define ANOMALY_BONE_SLOT 16

#ifdef ANOMALY_VELOCITY

struct AnomalyPrevInstance
{
    float4 col0;
    float4 col1;
    float4 col2;
    float4 flags; // x = 1 when previous world is valid (not first frame / teleport / static / clipmap)
};

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
};

StructuredBuffer<AnomalyPrevInstance> AnomalyPrevWorld : register(MERGE(t, ANOMALY_PREV_SLOT));
StructuredBuffer<float4x4> AnomalyPrevBones : register(MERGE(t, ANOMALY_BONE_SLOT));

float2 AnomalyClipToPixelDelta(float4 currClip, float4 prevClip)
{
    currClip /= max(currClip.w, 1e-6);
    prevClip /= max(prevClip.w, 1e-6);
    float2 currUv = float2(currClip.x * 0.5 + 0.5, 0.5 - currClip.y * 0.5);
    float2 prevUv = float2(prevClip.x * 0.5 + 0.5, 0.5 - prevClip.y * 0.5);
    return (currUv - prevUv) * AnomalyRenderSize;
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
    float3x3 invR = adj / max(abs(det), 1e-8);
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
    float4 currClip = mul(float4(positionLocal, 1), AnomalyUnjitteredViewProj);
    float3 prevPos = positionLocal;

    [branch]
    if (AnomalyHasHistory != 0)
    {
        matrix prevM = localMatrix;
        bool hasPrevWorld = false;

#ifdef USE_SIMPLE_INSTANCING
        [branch]
        if (svInstanceId < AnomalyPrevCount)
        {
            AnomalyPrevInstance prev = AnomalyPrevWorld[svInstanceId];
            [branch]
            if (prev.flags.x > 0.5)
            {
                prevM = construct_matrix_43(prev.col0, prev.col1, prev.col2);
                hasPrevWorld = true;
            }
        }
#else
        [branch]
        if (AnomalyHasPrevWorld != 0)
        {
            prevM = construct_matrix_43(AnomalyPrevRow0, AnomalyPrevRow1, AnomalyPrevRow2);
            hasPrevWorld = true;
        }
#endif

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
        float4 prevClip = mul(float4(prevPos, 1), AnomalyPrevViewProj);
        return AnomalyClipToPixelDelta(currClip, prevClip);
    }

    return float2(0, 0);
}

float2 AnomalyComputeVelocity(float3 positionLocal, matrix localMatrix, uint svInstanceId)
{
    return AnomalyComputeVelocity(positionLocal, localMatrix, svInstanceId, uint4(0, 0, 0, 0), float4(0, 0, 0, 0));
}

#endif // ANOMALY_VELOCITY

#include <Anomaly/GBufferExtras.hlsli>

#endif
