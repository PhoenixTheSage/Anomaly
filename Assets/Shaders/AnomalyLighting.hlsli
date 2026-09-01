#ifndef ANOMALY_LIGHTING_HLSLI
#define ANOMALY_LIGHTING_HLSLI

#include <Anomaly/LightingSlots.hlsli>

cbuffer AnomalyLightingExtras : register(MERGE(b, ANOMALY_LIGHTING_CB_SLOT))
{
    float2 AnomalyLightingRenderSize;
    float2 AnomalyLightingInvRenderSize;
    uint AnomalyLightingHasVelocity;
    uint AnomalyLightingHistoryValid;
    uint AnomalyLightingAttachCount;
    uint AnomalyLightingPad;
};

#endif
