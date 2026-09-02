#ifndef ANOMALY_FULLSCREEN_HLSLI
#define ANOMALY_FULLSCREEN_HLSLI

#include <Common.hlsli>
#include <Anomaly/FullscreenSlots.hlsli>
#define ANOMALY_EXTRAS_CB_SLOT ANOMALY_FULLSCREEN_CB_SLOT
#include <Anomaly/PassExtrasCb.hlsli>

#define ANOMALY_FULLSCREEN_STAGE

Texture2D AnomalySceneColor : register(t0);
Texture2D<float> AnomalyLinearDepth : register(t1);
Texture2D<float2> AnomalyVelocityBuffer : register(t2);
Texture2D<float> AnomalyReactiveMask : register(t3);

cbuffer AnomalyFullscreenUniforms : register(b7)
{
    float4 AnomalyPassUniform0;
    float4 AnomalyPassUniform1;
    float4 AnomalyPassUniform2;
    float4 AnomalyPassUniform3;
};

#endif
