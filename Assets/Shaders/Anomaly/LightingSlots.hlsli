#ifndef ANOMALY_LIGHTING_SLOTS_HLSLI
#define ANOMALY_LIGHTING_SLOTS_HLSLI

// Lighting / post extras. Geometry velocity CB is a different object at b6.
// Lighting SRV map (Common.hlsli): t0–t4 GBuffer, t10+ Keen lighting. t5–t9 free.
#define ANOMALY_LIGHTING_VELOCITY_SLOT 5
#define ANOMALY_LIGHTING_ATTACH_BASE 6
#define ANOMALY_LIGHTING_CB_SLOT 6
#define ANOMALY_POST_VELOCITY_SLOT 5
#define ANOMALY_POST_CB_SLOT 6

#endif
