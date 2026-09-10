#ifndef ANOMALY_FULLSCREEN_HLSLI
#define ANOMALY_FULLSCREEN_HLSLI

#include <Common.hlsli>
#include <Anomaly/FullscreenSlots.hlsli>
#define ANOMALY_EXTRAS_CB_SLOT ANOMALY_FULLSCREEN_CB_SLOT
#include <Anomaly/PassExtrasCb.hlsli>

#define ANOMALY_FULLSCREEN_STAGE

SamplerState AnomalyPointSampler : register(s0);
SamplerState AnomalyLinearSampler : register(s1);
SamplerState AnomalyWrapSampler : register(s2);

Texture2D AnomalySceneColor : register(t0);
Texture2D<float> AnomalyLinearDepth : register(t1);
Texture2D<float2> AnomalyVelocityBuffer : register(t2);
Texture2D<float> AnomalyReactiveMask : register(t3);
#if defined(ANOMALY_FULLSCREEN_SLOT_AFTERTONEMAP) || defined(ANOMALY_FULLSCREEN_SLOT_AFTERUPSCALE)
Texture2D<float2> AnomalyAvgLuminance : register(t4);
Texture2D AnomalyBloom : register(t5);
Texture2D<float> AnomalyDirt : register(t6);
#else
Texture2D AnomalyGBuffer0 : register(t4);
Texture2D AnomalyGBuffer1 : register(t5);
Texture2D AnomalyGBuffer2 : register(t6);
#endif
Texture2D AnomalyPackSrv0 : register(t7);
Texture2D AnomalyPackSrv1 : register(t8);
Texture2D AnomalyPackSrv2 : register(t9);

// Append-only. 0–7 are the original 128 B; 8–15 grow the blob to 256 B
// (same size as extras). Packs that only read 0–7 stay valid.
cbuffer AnomalyFullscreenUniforms : register(b7)
{
    float4 AnomalyPassUniform0;
    float4 AnomalyPassUniform1;
    float4 AnomalyPassUniform2;
    float4 AnomalyPassUniform3;
    float4 AnomalyPassUniform4;
    float4 AnomalyPassUniform5;
    float4 AnomalyPassUniform6;
    float4 AnomalyPassUniform7;
    float4 AnomalyPassUniform8;
    float4 AnomalyPassUniform9;
    float4 AnomalyPassUniform10;
    float4 AnomalyPassUniform11;
    float4 AnomalyPassUniform12;
    float4 AnomalyPassUniform13;
    float4 AnomalyPassUniform14;
    float4 AnomalyPassUniform15;
};

#endif
