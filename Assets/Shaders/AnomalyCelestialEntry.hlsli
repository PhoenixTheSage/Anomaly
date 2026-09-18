// Include AFTER defining float3 AnomalyEvaluateCelestial(AnomalyCelestialInput).
float4 __pixel_shader(float4 position : SV_Position, float2 uv : TEXCOORD0) : SV_Target0
{
    AnomalyCelestialInput input;
#if ANOMALY_CELESTIAL_PROBE
    float2 ndc = uv * float2(2, -2) + float2(-1, 1);
    float3 localRay = float3(ndc / CelestialView.zw, -1);
    input.direction = normalize(float3(dot(localRay, CelestialViewR0.xyz),
        dot(localRay, CelestialViewR1.xyz), dot(localRay, CelestialViewR2.xyz)));
    input.isProbe = 1;
#else
    // Scene projection deliberately shared by stars, sun and guides, including zoom/jitter.
    input.direction = normalize(view_to_world(compute_screen_ray(screen_to_uv(uint2(position.xy)))));
    input.isProbe = 0;
#endif
    input.angularPixel = AnomalyCelestialFootprint(input.direction);
    input.directionDx = ddx(input.direction);
    input.directionDy = ddy(input.direction);
    input.sunDirection = normalize(-frame_.Light.directionalLightVec);
    input.sunRadiance = frame_.Light.SunDiscColor * frame_.Light.directionalLightColor * frame_.Light.SunDiscIntensity;
    input.dataCount = (uint)CelestialDataInfo.x;
    // Derivatives above must be evaluated before the non-uniform depth discard.
    if (AnomalyCelestialDepth.Load(int3(uint2(position.xy), 0)) > 0) discard;
    float3 color = AnomalyEvaluateCelestial(input);
    if (!all(isfinite(color))) discard; // preserve Keen pixel for invalid provider output
#if !ANOMALY_CELESTIAL_PROBE
    color = lerp(max(color, 0), frame_.Fog.color, frame_.Fog.sky);
#endif
    return float4(max(color, 0), 1);
}
