#ifndef ANOMALY_FULLSCREEN_HLSLI
#define ANOMALY_FULLSCREEN_HLSLI

#include <Common.hlsli>
#if !defined(ANOMALY_FULLSCREEN_SLOT_AFTERTONEMAP) && !defined(ANOMALY_FULLSCREEN_SLOT_AFTERUPSCALE)
#include <Frame.hlsli>
#endif
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
#ifndef ANOMALY_PACK_SRV0_TYPE
#define ANOMALY_PACK_SRV0_TYPE Texture2D
#endif
#ifndef ANOMALY_PACK_SRV1_TYPE
#define ANOMALY_PACK_SRV1_TYPE Texture2D
#endif
#ifndef ANOMALY_PACK_SRV2_TYPE
#define ANOMALY_PACK_SRV2_TYPE Texture2D
#endif
ANOMALY_PACK_SRV0_TYPE AnomalyPackSrv0 : register(t7);
ANOMALY_PACK_SRV1_TYPE AnomalyPackSrv1 : register(t8);
ANOMALY_PACK_SRV2_TYPE AnomalyPackSrv2 : register(t9);
#if !defined(ANOMALY_FULLSCREEN_SLOT_AFTERTONEMAP) && !defined(ANOMALY_FULLSCREEN_SLOT_AFTERUPSCALE)
struct AnomalyPointLight
{
    float3 positionView;
    float range;
    float3 color;
    float fallOff;
    float glossFactor;
    float diffuseFactor;
    float _pad1;
    float _pad2;
};
StructuredBuffer<AnomalyPointLight> AnomalyPointLights : register(t10);
StructuredBuffer<uint> AnomalyTileIndices : register(t11);
#endif

#ifdef ANOMALY_FULLSCREEN_COMPUTE
RWTexture2D<float4> AnomalyComputeDest : register(u0);
#endif

#define AnomalyPassSize AnomalyLightingRenderSize
#define AnomalyInvPassSize AnomalyLightingInvRenderSize

uint2 AnomalyScenePixel(float2 uv)
{
    return uint2(saturate(uv) * AnomalySceneSize);
}

// Full-res pixel delta → UV. Scaled passes: AnomalyPassSize is the RT;
// GBuffer / linearDepth / velocity stay scene-sized. Do not use
// AnomalyInvPassSize for a screen-space radius in pixels.
float2 AnomalySceneUvOffset(float2 pixelDelta)
{
    return pixelDelta * AnomalyInvSceneSize;
}

#if !defined(ANOMALY_FULLSCREEN_SLOT_AFTERTONEMAP) && !defined(ANOMALY_FULLSCREEN_SLOT_AFTERUPSCALE)
// LightPoint reconstruct. Interpolator TEXCOORD ignores Frame.Screen.offset (DRS jitter).
float2 AnomalyLightingUv(float2 pixel)
{
    return screen_to_uv(pixel);
}

// screen_to_uv space → 0–1 of the depth / GBuffer texture (offset is pixels).
float2 AnomalyScreenUvToTexel(float2 screenUv)
{
    float2 res = max(frame_.Screen.resolution, 1);
    return screenUv + (float2)frame_.Screen.offset / res;
}

float3 AnomalyLightingViewPos(float2 pixel, float linearDepth)
{
    return linearDepth * compute_screen_ray(AnomalyLightingUv(pixel));
}

// LightPoint N: world_to_view(view_to_world(NView)). Do not skip NdotL ≤ 0;
// MaterialRadiance already saturates ln.
float3 AnomalyLightingN(float3 nView)
{
    return world_to_view(view_to_world(nView));
}
#endif

// camToVolumeMeters is a uniform (camera-to-shell / camera-to-volume). Never per-ray tMin.
// nearMeters → minSteps; farMeters → budget; then * saturate(AnomalySafetyScale).
// Do not floor AnomalySafetyScale.
int AnomalyMarchSteps(float budget, int minSteps, int maxSteps,
    float camToVolumeMeters, float nearMeters, float farMeters)
{
    int lo = max(minSteps, 1);
    int hi = max(maxSteps, lo);
    float safety = saturate(AnomalySafetyScale);
    float span = max(farMeters - nearMeters, 1e-3);
    float away = saturate((camToVolumeMeters - nearMeters) / span);
    away *= away;
    float scaled = lerp((float)lo, max(budget, (float)lo), away * safety);
    return clamp((int)(scaled + 0.5), lo, hi);
}

// 0 = local night, 1 = full sun.
// pos / center are camera-relative (origin-0). Keen CSM is camera-local and
// does not cover a planet disk from orbit — do not bind shadow maps here.
// lightWrapMeters is atmosphere thickness (AtmosphereRadius - hill radius).
// A hard sphere umbra with a 150 m penumbra cuts inside Keen's twilight limb.
float AnomalySunVisibility(float3 posCamRel, float3 occluderCenterCamRel,
    float occluderRadius, float lightWrapMeters)
{
    float3 sun = AnomalySunToward;
    float sun2 = dot(sun, sun);
    if (sun2 < 1e-8)
        return 0.0;
    sun *= rsqrt(sun2);

    float r = max(occluderRadius, 1.0);
    float3 oc = posCamRel - occluderCenterCamRel;
    float occ2 = dot(oc, oc);
    if (occ2 < 1e-8)
        return 0.0;

    float3 n = oc * rsqrt(occ2);
    // Geometric AtmosphereRadius sits outside Keen's visual limb. Thickness/r
    // can be O(1) and then IsolatedMix night stays sunlit. Cap at the 3-arg
    // 12% so wrap is twilight, not the whole night disk.
    float wrap = saturate(max(lightWrapMeters, r * 0.05) / r);
    wrap = min(wrap, 0.12);
    return saturate(smoothstep(-wrap, wrap * 0.4, dot(n, sun)));
}

float AnomalySunVisibility(float3 posCamRel, float3 occluderCenterCamRel, float occluderRadius)
{
    float r = max(occluderRadius, 1.0);
    return AnomalySunVisibility(posCamRel, occluderCenterCamRel, r, r * 0.12);
}

// IsolatedMix night / ambient fill. Independent of AnomalySunVisibility.
// Do not hdr-lift AnomalySkyLuma. Do not use AmbientForwardPass (Keen probe
// ambient). Cap at ~3% unlifted sun so a stale extras tail cannot equal sun.
float3 AnomalyVolumeAmbient()
{
    float3 cap = AnomalySunColor * 0.028;
    float3 a = AnomalySkyAmbient;
    float a2 = dot(a, a);
    float c2 = dot(cap, cap);
    if (a2 < 1e-12)
        return cap;
    return (a2 > c2 && c2 > 1e-12) ? cap : a;
}

// IsolatedSub src is a 0–1 dest fraction. Anomaly merges dest*(1-src).
// .a is occlusion for Reactive. AfterLighting t0 is a dest copy; blit
// runs before the pack PixelShader. Packs do not composite dest.
float4 AnomalyIsolatedSub(float occ)
{
    occ = saturate(occ);
    return float4(occ, occ, occ, occ);
}

// Append-only. 0–7 are the original 128 B; 8–15 grow the blob to 256 B.
// Packs that only read 0–7 stay valid. Extras on b6 are 304 B.
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
