#ifndef ANOMALY_PASS_EXTRAS_CB_HLSLI
#define ANOMALY_PASS_EXTRAS_CB_HLSLI

// Lighting / atmosphere / post extras CB (b6). Geometry velocity CB is a
// different object at the same slot. Append-only so existing lighting injects
// that only read the first fields stay valid.
#ifndef ANOMALY_EXTRAS_CB_SLOT
#define ANOMALY_EXTRAS_CB_SLOT 6
#endif

cbuffer AnomalyLightingExtras : register(MERGE(b, ANOMALY_EXTRAS_CB_SLOT))
{
    float2 AnomalyLightingRenderSize;
    float2 AnomalyLightingInvRenderSize;
    uint AnomalyLightingHasVelocity;
    uint AnomalyLightingHistoryValid;
    uint AnomalyLightingAttachCount;
    uint AnomalyLightingFrameIndex;
    float2 AnomalyLightingJitter;
    // Host 0–1 from this-frame camera move / look. 1 = calm.
    // March packs: steps *= AnomalySafetyScale.
    float AnomalySafetyScale;
    float AnomalySafetyPad;
    row_major float4x4 AnomalyUnjitteredViewProj;
    row_major float4x4 AnomalyPrevViewProj;
    row_major float3x4 AnomalyCameraToWorld;
    float2 AnomalyProjScale;
    float2 AnomalyCameraToWorldPad;
};

#endif
