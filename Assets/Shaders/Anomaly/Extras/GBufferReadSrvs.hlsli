#ifndef ANOMALY_GBUFFER_READ_SRVS_HLSLI
#define ANOMALY_GBUFFER_READ_SRVS_HLSLI

#include <Anomaly/LightingSlots.hlsli>
#ifdef ANOMALY_VELOCITY
Texture2D<float2> AnomalyVelocityBuffer : register(MERGE(t, ANOMALY_LIGHTING_VELOCITY_SLOT));
#endif

#endif
