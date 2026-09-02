#ifndef ANOMALY_ATMOSPHERE_SLOTS_HLSLI
#define ANOMALY_ATMOSPHERE_SLOTS_HLSLI

// Keen AtmosphereCommon binds DensityLut at t5 and atmosphere CB at b1.
// Do not reuse t5. Velocity / pack extras start at t6; extras CB at b6.
#define ANOMALY_ATMOSPHERE_VELOCITY_SLOT 6
#define ANOMALY_ATMOSPHERE_CB_SLOT 6
#define ANOMALY_ATMOSPHERE_EXTRA_BASE 7

#endif
