#ifndef ANOMALY_CONTACT_SHADOWS_INCLUDED
#define ANOMALY_CONTACT_SHADOWS_INCLUDED

// Shared by contact tracing and diagnostics: compare the rendered caster
// distance with the visible scene point, rather than its enclosing box.
bool AnomalyContactPlayerPixel(Texture2D<float> playerDepth, uint2 pixel, float3 scenePoint)
{
    float playerDistance = playerDepth.Load(int3(pixel, 0));
    return playerDistance > 0 && playerDistance < 1e4 &&
        abs(length(scenePoint) - playerDistance) <= max(0.03, -scenePoint.z * 0.001);
}

// Short-range contact detail. Traverse contiguous projected pixel intervals,
// not isolated world-distance samples that stamp repeated depth silhouettes.
// A bounded budget truncates the ray; it never increases the pixel stride.
float AnomalyContactVisibilityCore(Texture2D<float> depth, Texture2D<float> playerDepth, bool excludePlayer, float3 receiver,
    float3 normal, float3 towardLight, float rayLength, float thickness, uint budget,
    float3 exclusionCenter, float3 exclusionHalf, float3x3 viewToWorld)
{
    budget = clamp(budget, 1, 192);
    if (rayLength <= 0.02 || dot(normal, towardLight) <= 0)
        return 1;
    float3 start = receiver + normal * 0.015 + towardLight * 0.01;
    float3 finish = start + towardLight * max(rayLength - 0.025, 0);
    if (start.z >= -0.02)
        return 1;
    if (finish.z >= -0.02)
        finish = lerp(start, finish, saturate((-0.02 - start.z) / (finish.z - start.z)));
    float2 uv0 = AnomalyViewToDepthUv(start);
    float2 uv1 = AnomalyViewToDepthUv(finish);
    uint width, height;
    depth.GetDimensions(width, height);
    if (width == 0 || height == 0)
        return 1;
    float2 size = float2(width, height);
    float2 delta = (uv1 - uv0) * size;
    // Traverse actual pixel cells. Unit-length intervals anchored at uv0 can
    // straddle two cells and assign a blocker's depth to the wrong interval.
    float2 pixelStart = uv0 * size;
    float2 cell = floor(pixelStart);
    float2 nextBoundary = cell + float2(delta.x >= 0 ? 1 : 0, delta.y >= 0 ? 1 : 0);
    float2 nextCross = float2(abs(delta.x) > 1e-8 ? (nextBoundary.x - pixelStart.x) / delta.x : 1e20,
        abs(delta.y) > 1e-8 ? (nextBoundary.y - pixelStart.y) / delta.y : 1e20);
    float2 crossStep = 1.0 / max(abs(delta), 1e-20);
    float invZ0 = rcp(-start.z), invZ1 = rcp(-finish.z);
    // At most one initial cell plus each horizontal/vertical crossing.
    float reach = min(1.0, max((float)budget - 2, 1) / max(abs(delta.x) + abs(delta.y), 1));
    float nextA = 0;
    [loop] for (uint i = 0; i < 192; i++)
    {
        if (i >= budget)
            break;
        float a = nextA, b = min(min(nextCross.x, nextCross.y), reach);
        if (a >= reach)
            break;
        nextA = b;
        if (nextCross.x <= b + 1e-7) nextCross.x += crossStep.x;
        if (nextCross.y <= b + 1e-7) nextCross.y += crossStep.y;
        if (b <= a) continue;
        float mid = (a + b) * 0.5;
        float2 uv = lerp(uv0, uv1, mid);
        if (any(uv <= 0) || any(uv >= 1))
            break;
        uint2 pixel = min(uint2(uv * size), uint2(width - 1, height - 1));
        float sceneZ = depth.Load(int3(pixel, 0));
        if (sceneZ <= 0)
            continue;
        // Reject the receiver's tangent plane, not every blocker that is
        // farther from the camera. Use the actual sampled texel center.
        float3 blocker = AnomalyLightingViewPos(float2(pixel) + 0.5, sceneZ);
        if (excludePlayer && AnomalyContactPlayerPixel(playerDepth, pixel, blocker))
            continue;
        // An atlas-owned caster must not produce a second depth-slab shadow.
        // Zero half extents disable exclusion (missing atlas / ContactOnly).
        if (all(exclusionHalf > 0) &&
            all(abs(mul(blocker, viewToWorld) - exclusionCenter) <= exclusionHalf))
            continue;
        if (dot(blocker - receiver, normal) <= 0.01)
            continue;
        // Perspective-correct depth at BOTH interval boundaries. Testing
        // overlap closes the holes between samples on sloping receivers.
        float za = rcp(lerp(invZ0, invZ1, a));
        float zb = rcp(lerp(invZ0, invZ1, b));
        if (max(za, zb) > sceneZ + 0.01 && min(za, zb) < sceneZ + thickness)
        {
            // Screen interpolation is not physical distance under perspective.
            // Locate the overlap in the depth interval, then convert to a
            // fraction of the world ray. Fade only its last 10%, not 8 pixels.
            float deltaInvZ = invZ1 - invZ0;
            float hit = abs(deltaInvZ) > 1e-8
                ? clamp((rcp(sceneZ + thickness * 0.5) - invZ0) / deltaInvZ, a, b)
                : mid;
            float hitWorld = hit * invZ1 / lerp(invZ0, invZ1, hit);
            float reachWorld = reach * invZ1 / lerp(invZ0, invZ1, reach);
            float endFade = saturate((reachWorld - hitWorld) / 0.1);
            return 1.0 - 0.9 * endFade;
        }
    }
    return 1;
}
float AnomalyContactVisibilityExcludingBounds(Texture2D<float> depth, float3 receiver,
    float3 normal, float3 towardLight, float rayLength, float thickness, uint budget,
    float3 exclusionCenter, float3 exclusionHalf, float3x3 viewToWorld)
{
    return AnomalyContactVisibilityCore(depth, depth, false, receiver, normal,
        towardLight, rayLength, thickness, budget, exclusionCenter, exclusionHalf, viewToWorld);
}
float AnomalyContactVisibilityExcludingPlayer(Texture2D<float> depth, Texture2D<float> playerDepth,
    float3 receiver, float3 normal, float3 towardLight, float rayLength, float thickness, uint budget)
{
    return AnomalyContactVisibilityCore(depth, playerDepth, true, receiver, normal,
        towardLight, rayLength, thickness, budget, 0, 0, (float3x3)0);
}
float AnomalyContactVisibility(Texture2D<float> depth, float3 receiver,
    float3 normal, float3 towardLight, float rayLength, float thickness, uint budget)
{
    return AnomalyContactVisibilityExcludingBounds(depth, receiver, normal,
        towardLight, rayLength, thickness, budget, 0, 0, (float3x3)0);
}
#endif
