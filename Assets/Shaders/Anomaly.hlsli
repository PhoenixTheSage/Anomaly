#ifndef ANOMALY_HLSLI
#define ANOMALY_HLSLI

#include <Common.hlsli>

// Compile intercept: ANOMALY=1 on every permutation.
// ANOMALY_VELOCITY is GBuffer-only (RENDERING_PASS == 0). Depth must never see it.
//
// Prev-world SRV is packed in Stage 2 instance-buffer order (SV_InstanceID).
// Slot 15 is unused by geometry (lighting BRDF LUT is later; unbind after GBuffer).
// CB slot 6 is unused by geometry (0 frame, 1 projection, 2 object, 3 material, 4 foliage, 5 alphamask, 7 forward).

#define ANOMALY_CB_SLOT 6
#define ANOMALY_PREV_SLOT 15

#ifdef ANOMALY_VELOCITY

struct AnomalyPrevInstance
{
    float4 col0;
    float4 col1;
    float4 col2;
    float4 flags; // x = 1 when previous world is valid (not first frame / teleport / static)
};

cbuffer AnomalyVelocity : register(MERGE(b, ANOMALY_CB_SLOT))
{
    float4x4 AnomalyUnjitteredViewProj;
    float4x4 AnomalyPrevViewProj;
    float2 AnomalyRenderSize;
    float2 AnomalyInvRenderSize;
    uint AnomalyPrevCount;
    uint AnomalyHasHistory;
    uint2 AnomalyPad;
};

StructuredBuffer<AnomalyPrevInstance> AnomalyPrevWorld : register(MERGE(t, ANOMALY_PREV_SLOT));

float2 AnomalyClipToPixelDelta(float4 currClip, float4 prevClip)
{
    currClip /= max(currClip.w, 1e-6);
    prevClip /= max(prevClip.w, 1e-6);
    float2 currUv = float2(currClip.x * 0.5 + 0.5, 0.5 - currClip.y * 0.5);
    float2 prevUv = float2(prevClip.x * 0.5 + 0.5, 0.5 - prevClip.y * 0.5);
    return (currUv - prevUv) * AnomalyRenderSize;
}

// Rigid inverse of Keen construct_matrix_43 / instance VB (row-vector mul).
float3 AnomalyWorldToObject(float3 world, matrix m)
{
    float3 t = m._41_42_43;
    float3x3 r = (float3x3)m;
    return mul(world - t, transpose(r));
}

float2 AnomalyComputeVelocity(float3 positionLocal, matrix localMatrix, uint svInstanceId)
{
    float4 currClip = mul(float4(positionLocal, 1), AnomalyUnjitteredViewProj);
    float3 prevPos = positionLocal;

    [branch]
    if (AnomalyHasHistory != 0)
    {
#ifdef USE_SIMPLE_INSTANCING
        [branch]
        if (svInstanceId < AnomalyPrevCount)
        {
            AnomalyPrevInstance prev = AnomalyPrevWorld[svInstanceId];
            [branch]
            if (prev.flags.x > 0.5)
            {
                matrix prevM = construct_matrix_43(prev.col0, prev.col1, prev.col2);
                float3 objectPos = AnomalyWorldToObject(positionLocal, localMatrix);
                prevPos = mul(float4(objectPos, 1), prevM).xyz;
            }
        }
#endif
        float4 prevClip = mul(float4(prevPos, 1), AnomalyPrevViewProj);
        return AnomalyClipToPixelDelta(currClip, prevClip);
    }

    return float2(0, 0);
}

#endif // ANOMALY_VELOCITY

#endif
