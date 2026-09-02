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
    float2 AnomalyLightingPad1;
    row_major float4x4 AnomalyUnjitteredViewProj;
    row_major float4x4 AnomalyPrevViewProj;
};

#endif
