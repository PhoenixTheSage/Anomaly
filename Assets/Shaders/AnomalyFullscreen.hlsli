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

// Compatibility name: the returned point is in camera view coordinates.
// The depth belongs to a jittered raster pixel, so reconstruction MUST use
// that pixel's live projection inverse. Dropping M31/M32 here displaces the
// surface and breaks the ViewToDepthUv round trip by the Halton offset.
float3 AnomalyLightingViewPosUnjittered(float2 pixel, float linearDepth)
{
    return AnomalyLightingViewPos(pixel, linearDepth);
}

// Inverse of compute_screen_ray (same live projection as LightPoint / AnomalyLightingViewPos).
// BRDF / dest match. Also used for contact depth projection; invert the same
// raster projection that produced the sampled depth.
float2 AnomalyViewToLightingUv(float3 viewPos)
{
    float z = max(-viewPos.z, 1e-4);
    float3 ray = viewPos / z;
    float ray_x = rcp(max(frame_.Environment.projection_matrix._11, 1e-6));
    float ray_y = rcp(max(frame_.Environment.projection_matrix._22, 1e-6));
    float2 projOffset = float2(
        frame_.Environment.projection_matrix._31 * ray_x,
        frame_.Environment.projection_matrix._32 * ray_y);
    return float2(
        ((ray.x - projOffset.x) / ray_x + 1) * 0.5,
        (1 - (ray.y - projOffset.y) / ray_y) * 0.5);
}

float2 AnomalyViewToUnjitteredUv(float3 viewPos)
{
    float z = max(-viewPos.z, 1e-4);
    float3 ray = viewPos / z;
    float ray_x = rcp(max(frame_.Environment.projection_matrix._11, 1e-6));
    float ray_y = rcp(max(frame_.Environment.projection_matrix._22, 1e-6));
    return float2(
        (ray.x / ray_x + 1) * 0.5,
        (1 - ray.y / ray_y) * 0.5);
}

// AnomalyLightingJitter is Projection M31/M32 (SE-DLSS Halton).
// unjittered UV + this = this-frame GBuffer / linearDepth texel.
float2 AnomalyLightingJitterUv()
{
    return float2(-AnomalyLightingJitter.x, AnomalyLightingJitter.y) * 0.5;
}

// View-space point -> this-frame depth texel, using the live raster projection.
// Do not use AnomalyUnjitteredViewProj as a substitute.
float2 AnomalyViewToDepthUv(float3 viewPos)
{
    return AnomalyScreenUvToTexel(AnomalyViewToLightingUv(viewPos));
}

// Camera-relative world AABB. Contact packs skip the local suit so
// GBuffer copies do not stack on pointShadowAtlas.
bool AnomalyInsideAabb(float3 p, float3 center, float3 halfExt)
{
    return all(abs(p - center) <= max(halfExt, 1e-4));
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

float2 AnomalyRaySphere(float3 origin, float3 dir, float3 center, float radius)
{
    float3 oc = origin - center;
    float b = dot(oc, dir);
    float c = dot(oc, oc) - radius * radius;
    float disc = b * b - c;
    if (disc < 0.0)
        return float2(-1.0, -1.0);
    float s = sqrt(disc);
    return float2(-b - s, -b + s);
}

// Night IsolatedMix inscatter as a fraction of AJ in-cloud day fill.
// 1.0 × VolumeAmbient over dest≈0 is headlights. 0.30 was a grey deck
// on a black night disk from orbit. Match Keen night ambient.
// Dest luma is not an illuminant. Extras stay 320 B.
float AnomalyVolumeNightScale()
{
    return 0.05;
}

float3 AnomalyVolumeNight(float3 albedo, float sunVis)
{
    float vis = saturate(sunVis);
    float3 dayFill = AnomalyVolumeAmbient() * albedo;
    return lerp(dayFill * AnomalyVolumeNightScale(), dayFill, vis);
}

// destRgb kept so existing packs compile. Ignored — do not pass dest.
float3 AnomalyVolumeNight(float3 albedo, float3 destRgb, float sunVis)
{
    return AnomalyVolumeNight(albedo, sunVis);
}

float AnomalyVolumeCeil();

// Optical sun transmittance. Deep night is solid 0. Twilight is a
// monotonic limb: sample-height horizon dip sqrt(2h/r), symmetric
// smoothstep, then squared so IsolatedMix HDR does not turn the S-curve
// into a white wall. Do not return raw geo when μ≤0 and geo*exp(-OD)
// when μ>0 — that peaked vis on the night side of μ=0 (bright band)
// and zeroed vis on the grazing day side (hard cut). Do not use
// twilight*0.45 as the day edge (vis=1 ~5° into day while Keen is still
// yellow twilight). Do not sphere-hit tNear>1 (HashIgn fireflies).
// Daytime 6-step OD only after geo is ~1. Do not bind Keen CSM.
// Fixed 6 steps — not AnomalySafetyScale. 12% Lambert wrap is not used.
float AnomalySunTransmittance(float3 posCamRel, float3 centerCamRel,
    float planetR, float airTop)
{
    float rPlanet = max(planetR, 1.0);
    float3 oc = posCamRel - centerCamRel;
    float occ2 = dot(oc, oc);
    if (occ2 < rPlanet * rPlanet)
        return 0.0;

    float3 sun = AnomalySunToward;
    float sun2 = dot(sun, sun);
    if (sun2 < 1e-8)
        return 0.0;
    sun *= rsqrt(sun2);

    float rSample = sqrt(max(occ2, 1e-6));
    float mu = dot(oc, sun) / rSample;

    float air = airTop;
    if (air < rPlanet + 40.0)
        air = AnomalyVolumeCeil();
    float column = air > rPlanet + 40.0 ? (air - rPlanet) : (rPlanet * 0.026);
    column = max(column, 80.0);
    float h = max(rSample - rPlanet, 80.0);
    // Pertam h/r is large; 0.18 capped inside the geometric sunset.
    // 0.40 still cannot Lambert-wrap the night cap.
    float twilight = sqrt(saturate(2.0 * max(h, column) / rPlanet));
    twilight = min(max(twilight, 0.08), 0.40);
    float geo = smoothstep(-twilight, twilight, mu);
    if (geo <= 1e-4)
        return 0.0;

    float vis = geo * geo;
    if (air < rPlanet + 40.0 || mu < twilight)
        return vis;

    float2 aHit = AnomalyRaySphere(posCamRel, sun, centerCamRel, air);
    if (aHit.y < 0.0)
        return vis;

    float t0 = max(aHit.x, 0.0);
    float t1 = aHit.y;
    if (t1 <= t0 + 1.0)
        return vis;

    float sigma = 0.25 / column;
    float dtOd = (t1 - t0) / 6.0;
    float od = 0.0;
    [unroll]
    for (int i = 0; i < 6; i++)
    {
        float3 p = posCamRel + sun * (t0 + (float(i) + 0.5) * dtOd);
        float rad = length(p - centerCamRel);
        float h01 = saturate((rad - rPlanet) / column);
        float rho = exp(-h01 * 4.0);
        if (rad > air || rad < rPlanet)
            rho = 0.0;
        od += rho * dtOd * sigma;
    }
    float day = smoothstep(twilight, twilight + 0.08, mu);
    return saturate(vis * lerp(1.0, exp(-od), day));
}

float AnomalySunTransmittance(float3 posCamRel, float3 centerCamRel, float planetR)
{
    return AnomalySunTransmittance(posCamRel, centerCamRel, planetR, AnomalyVolumeCeil());
}

// Radii from the planet center, meters. 0 = fail closed (no eligible
// planet this frame). Not camera-relative. Packs compare extras to
// their own planet; Anomaly publishes the nearest HasAtmosphere /
// CloudLayers body.
float AnomalyVolumeCeil()
{
    float v = AnomalyVisualAtmoCeil;
    if (v > 1.0)
        return v;
    float a = AnomalyPlanetAirTop;
    if (a > 1.0)
        return a;
    return 0.0;
}

float AnomalyClampRadialToCeil(float radialFromCenter)
{
    float ceil = AnomalyVolumeCeil();
    if (ceil < 1.0)
        return radialFromCenter;
    return min(radialFromCenter, ceil);
}

// 1 inside the ceiling, 0 at/above it. Fail closed (no extras)
// returns 1 so a missing snapshot does not erase pack uniforms.
float AnomalyVolumeCeilFade(float radialFromCenter)
{
    float ceil = AnomalyVolumeCeil();
    if (ceil < 1.0)
        return 1.0;
    float soft = max(ceil * 0.02, 80.0);
    return 1.0 - saturate((radialFromCenter - (ceil - soft)) / soft);
}

// Interleaved gradient noise in pixel coordinates. Stable under SafetyScale
// (do not xor with frame index). Contact / SSGI dither.
float AnomalyIgn(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

// Same IGN on a world-stable 2D seed so a contact / SSGI step phase does
// not crawl when the camera moves and the light has not. Pass
// InvViewAt0(receiver - light).xz (camera look and translate cancel).
// Do not scale the seed (high-frequency IGN flips when viewPos jitters 1 cm).
// pixel IGN follows the raster.
float AnomalyIgnWorld(float2 worldXz)
{
    return AnomalyIgn(worldXz);
}

// Slice AN. Catalog volumeSunShadow.r is remaining sun (1 = none).
// .a < 0.5 means the stamp is missing or unbound — fail closed to 1
// (null SRV samples 0). Do not bind Keen CSM. Extras stay 320 B.
float AnomalyVolumeSunShadow(float remaining, float alpha)
{
    if (alpha < 0.5)
        return 1.0;
    return saturate(remaining);
}

float AnomalyVolumeSunShadow(Texture2D tex, SamplerState samp, float2 uv)
{
    float4 s = tex.SampleLevel(samp, uv, 0);
    return AnomalyVolumeSunShadow(s.r, s.a);
}

// IsolatedSub src is a 0–1 dest fraction (per channel). Anomaly merges
// dest*(1-src). .a is occlusion for Reactive. AfterLighting t0 is a dest
// copy when dest aliases LBuffer; blit runs before the pack PixelShader.
// Packs do not composite dest. Prefer AnomalyIsolatedSubEnergy so occ is
// photometric shadowed energy / dest — minVis OR of many lights, or a
// longer-range BRDF tail at the falloff, punches dest to black.
float4 AnomalyIsolatedSub(float occ)
{
    occ = saturate(occ);
    return float4(occ, occ, occ, occ);
}

float4 AnomalyIsolatedSub(float3 occ)
{
    occ = saturate(occ);
    return float4(occ, max(occ.x, max(occ.y, occ.z)));
}

float4 AnomalyIsolatedSubEnergy(float3 removed, float3 dest)
{
    float destLuma = dot(dest, 1.0 / 3.0);
    if (destLuma < 1e-5)
        return AnomalyIsolatedSub(0);
    dest = max(dest, 1e-4);
    // Clamp to dest so a hotter reconstruct cannot dest-punch. Soft knee
    // so occ=1 at the photometric falloff cannot zero dest (duplicate
    // hard silhouettes that do not appear in the bright center).
    removed = clamp(removed, 0, dest);
    float3 occ = removed / dest;
    occ = occ / (1.0 + occ * 0.12);
    return AnomalyIsolatedSub(occ);
}

float4 AnomalyIsolatedSubEnergy(float3 removed, float3 dest, float3 unshadowed)
{
    return AnomalyIsolatedSubEnergy(min(max(removed, 0), max(unshadowed, 0)), dest);
}

// Append-only. 0–7 are the original 128 B; 8–15 grow the blob to 256 B.
// Packs that only read 0–7 stay valid. Extras on b6 are 320 B.
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
