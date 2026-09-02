#ifndef ANOMALY_ATMOSPHERE_HLSLI
#define ANOMALY_ATMOSPHERE_HLSLI

#include <Anomaly/AtmosphereSlots.hlsli>
#define ANOMALY_EXTRAS_CB_SLOT ANOMALY_ATMOSPHERE_CB_SLOT
#include <Anomaly/PassExtrasCb.hlsli>

Texture2D<float2> AnomalyVelocityBuffer : register(MERGE(t, ANOMALY_ATMOSPHERE_VELOCITY_SLOT));

#endif
