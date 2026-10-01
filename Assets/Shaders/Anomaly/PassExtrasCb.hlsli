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
    // Host 0–1 from camera move / look. 1 = calm. Drops this frame;
    // recovery is damped so march step counts do not chatter.
    // March packs: scale steps by AnomalySafetyScale (do not globally floor).
    float AnomalySafetyScale;
    float AnomalySafetyPad;
    row_major float4x4 AnomalyUnjitteredViewProj;
    row_major float4x4 AnomalyPrevViewProj;
    row_major float3x4 AnomalyCameraToWorld;
    float2 AnomalyProjScale;
    float2 AnomalyCameraToWorldPad;
    // Full-res scene (ResolutionI / ViewportResolution). RenderSize above is
    // the pass RT — equal to SceneSize unless passes[].scale / SetScale.
    float2 AnomalySceneSize;
    float2 AnomalyInvSceneSize;
    // Slice AI — EnvironmentLight. Append-only; 256 B shaders ignore the tail.
    float3 AnomalySunColor;
    float AnomalySunDiffuse;
    float3 AnomalySunToward;
    float AnomalySkyLuma;
    // Dim unlifted sky/ambient RGB. Independent of AnomalySunVisibility and
    // pack HdrLift. Not AnomalySkyLuma (sun luma × AmbientDiffuse).
    float3 AnomalySkyAmbient;
    float AnomalySkyAmbientPad;
    // Slice AK — radii from planet center, meters. 0 = fail closed.
    // Not camera-relative. VisualCeil equals AirTop today (optical
    // IsolatedMix cap). Never 0.90 × AtmosphereRadius.
    float AnomalyPlanetAirTop;
    float AnomalyVisualAtmoCeil;
    // Slice AS — AfterAtmosphere empty skip. Occupancy below Floor
    // takes Mul × dt. 0 / ≤1 fail closed to helper defaults (0.38 / 6).
    // Named tail of the 320 B extras; do not grow the CB.
    float AnomalyVolumeSkipFloor;
    float AnomalyVolumeSkipMul;
};

#endif
