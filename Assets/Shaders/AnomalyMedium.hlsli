#ifndef ANOMALY_MEDIUM_HLSLI
#define ANOMALY_MEDIUM_HLSLI

// Linear HDR source terms per metre. Extinction is scalar in v1; coloured
// transparent surfaces are not approximated by independent RGB opacity.
struct AnomalyMediumSample
{
    float extinction;
    float3 scattering;
    float3 emission;
    float anisotropy;
    float3 velocity;
};

AnomalyMediumSample AnomalyEmptyMedium()
{
    AnomalyMediumSample m = (AnomalyMediumSample)0;
    return m;
}

float AnomalyHenyeyGreenstein(float cosineTheta, float g)
{
    g = clamp(g, -0.95, 0.95);
    float denominator = max(1 + g*g - 2*g*clamp(cosineTheta,-1,1), 1e-5);
    return (1-g*g) / (12.5663706144 * denominator * sqrt(denominator));
}

// Add each provider's phase-weighted scattering SOURCE, not averaged g.
// This preserves mixtures of media with different phase functions.
void AnomalyAccumulateMedium(AnomalyMediumSample m, float cosineTheta,
    float sunVisibility, float3 sunRadiance, float3 ambient,
    inout float extinction, inout float3 source, inout float3 weightedVelocity)
{
    float sigma = max(m.extinction, 0);
    float3 scattering = clamp(m.scattering, 0, sigma);
    extinction += sigma;
    source += max(m.emission, 0) + scattering *
        (max(ambient,0) + max(sunRadiance,0) * saturate(sunVisibility) *
         AnomalyHenyeyGreenstein(cosineTheta, m.anisotropy));
    weightedVelocity += m.velocity * sigma;
}

void AnomalyIntegrateMedium(float extinction, float3 source, float distanceMeters,
    inout float3 radiance, inout float transmittance)
{
    float distance = max(distanceMeters, 0);
    float opticalDepth = max(extinction, 0) * distance;
    float segmentT = exp(-opticalDepth);
    // Taylor limit avoids cancellation in nearly transparent cells.
    float integral = opticalDepth < 1e-3 ? distance*(1-opticalDepth*0.5+opticalDepth*opticalDepth/6) :
        (1-segmentT)/extinction;
    radiance += transmittance * max(source, 0) * integral;
    transmittance *= segmentT;
}

float AnomalyVolumeSliceDistance(uint boundary, uint slices, float nearMeters, float farMeters)
{
    float nearDistance = max(nearMeters, 0.001);
    float base = 1 + max(farMeters, 0)/nearDistance;
    return nearDistance * (pow(abs(base), (float)boundary/max(slices,1u))-1);
}
#endif
