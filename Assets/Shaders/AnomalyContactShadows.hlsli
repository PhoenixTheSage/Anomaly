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

// Local IGN so WARP probes compile this file without AnomalyFullscreen.
float AnomalyContactIgn(float2 p)
{
    return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715))));
}

// Neighbor-receiver occlusion at the same along-L as the first hit.
// Invalid / different-surface taps return occluded so they do not punch
// holes in the umbra. Only a valid same-surface neighbor that misses
// counts as lit — that is the contact silhouette.
bool AnomalyContactNeighborOccluded(Texture2D<float> depth, Texture2D<float> playerDepth, bool excludePlayer,
    float2 uvN, float2 size, uint width, uint height, float3 receiver, float3 normal,
    float3 towardLight, float along, float rayLength, float thickness,
    float3 exclusionCenter, float3 exclusionHalf, float3x3 viewToWorld)
{
    if (any(uvN <= 0) || any(uvN >= 1))
        return true;
    uint2 pixelN = min(uint2(uvN * size), uint2(width - 1, height - 1));
    float zN = depth.Load(int3(pixelN, 0));
    if (zN <= 0)
        return true;
    float3 recvN = AnomalyLightingViewPos(float2(pixelN) + 0.5, zN);
    float3 away = recvN - receiver;
    if (abs(recvN.z - receiver.z) > max(0.12, -receiver.z * 0.03) ||
        dot(away, away) > 0.25)
        return true;
    if (excludePlayer && AnomalyContactPlayerPixel(playerDepth, pixelN, recvN))
        return true;
    float3 startN = recvN + normal * 0.015 + towardLight * 0.01;
    float3 probe = startN + towardLight * along;
    if (probe.z >= -0.02)
        return true;
    float2 uvP = AnomalyViewToDepthUv(probe);
    if (any(uvP <= 0) || any(uvP >= 1))
        return false;
    uint2 pixelP = min(uint2(uvP * size), uint2(width - 1, height - 1));
    float zP = depth.Load(int3(pixelP, 0));
    if (zP <= 0)
        return false;
    float3 blocker = AnomalyLightingViewPos(float2(pixelP) + 0.5, zP);
    if (excludePlayer && AnomalyContactPlayerPixel(playerDepth, pixelP, blocker))
        return false;
    if (all(exclusionHalf > 0) &&
        all(abs(mul(blocker, viewToWorld) - exclusionCenter) <= exclusionHalf))
        return false;
    if (dot(blocker - recvN, normal) <= 0.01)
        return false;
    float alongN = dot(blocker - recvN, towardLight);
    float3 onRayN = recvN + towardLight * alongN;
    float pixelM = 2.0 * zP / min(size.x, size.y);
    float rad = thickness + pixelM;
    float3 offRay = blocker - onRayN;
    return alongN > 0.02 && alongN < rayLength && dot(offRay, offRay) < rad * rad;
}

// Short-range contact detail. Traverse contiguous projected pixel intervals,
// not isolated world-distance samples that stamp repeated depth silhouettes.
// A bounded budget truncates the ray; it never increases the pixel stride.
// softTaps 0/1 = hard first-hit. 2–8 = 1D edge filter perpendicular to the
// projected light (receiver-centered). Not a disk around the blocker.
float AnomalyContactVisibilityCore(Texture2D<float> depth, Texture2D<float> playerDepth, bool excludePlayer, float3 receiver,
    float3 normal, float3 towardLight, float rayLength, float thickness, uint budget,
    float3 exclusionCenter, float3 exclusionHalf, float3x3 viewToWorld, uint softTaps)
{
    budget = clamp(budget, 1, 192);
    softTaps = min(softTaps, 8);
    if (rayLength <= 0.02 || dot(normal, towardLight) <= 0)
        return 1;
    float3 start = receiver + normal * 0.015 + towardLight * 0.01;
    float3 finish = start + towardLight * max(rayLength - 0.025, 0);
    if (start.z >= -0.02)
        return 1;
    // Do not substitute a camera-plane endpoint. Clipping finish to z=-0.02
    // made ViewToDepthUv and the view-Z interval a ray toward the camera, so
    // contact umbras followed the view while atlas stayed on the world light.
    if (finish.z >= -0.02)
        return 1;
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
        // An atlas-owned caster must not produce a second contact umbra.
        // Zero half extents disable exclusion (missing atlas / ContactOnly).
        if (all(exclusionHalf > 0) &&
            all(abs(mul(blocker, viewToWorld) - exclusionCenter) <= exclusionHalf))
            continue;
        if (dot(blocker - receiver, normal) <= 0.01)
            continue;
        // 3D metres to the light-ray segment in this pixel, not a view-Z slab.
        // View-Z overlap accepted any closer camera-depth along the projected
        // line, so umbras followed view silhouettes like a flashlight while
        // the cube atlas compared Euclidean distance on the world ray.
        float invZa = lerp(invZ0, invZ1, a);
        float invZb = lerp(invZ0, invZ1, b);
        float wA = a * invZ1 / max(invZa, 1e-8);
        float wB = b * invZ1 / max(invZb, 1e-8);
        float3 pA = lerp(start, finish, saturate(wA));
        float3 pB = lerp(start, finish, saturate(wB));
        float3 ab = pB - pA;
        float ab2 = max(dot(ab, ab), 1e-8);
        float seg = saturate(dot(blocker - pA, ab) / ab2);
        float3 onRay = pA + ab * seg;
        float along = dot(blocker - receiver, towardLight);
        // Pixel footprint at this depth so a 1-texel interval is not tighter
        // than thickness. Do not add 0.5*|pB-pA| — that inflates along the ray
        // and re-opens camera-silhouette hits.
        float zMid = rcp(0.5 * (invZa + invZb));
        float pixelM = 2.0 * zMid / min(size.x, size.y);
        float rad = thickness + pixelM;
        float3 offRay = blocker - onRay;
        if (along > 0.02 && along < rayLength && dot(offRay, offRay) < rad * rad)
        {
            float endFade = saturate((rayLength - along) / 0.1);
            float hard = 1.0 - 0.9 * endFade;
            if (softTaps < 2)
                return hard;
            // Soften the umbra silhouette only. A Vogel disk around the
            // blocker UV used thickness as a source radius (tens of pixels)
            // and counted misses as lit — that striped the march and erased
            // interior detail. Filter neighbor receivers, perpendicular to
            // the projected light, with a ~3 cm virtual source clamped to a
            // few pixels. Invalid / other-surface taps stay occluded.
            float2 lightUv = uv1 - uv0;
            float2 perp = float2(-lightUv.y, lightUv.x);
            float perpLen = length(perp);
            perp = perpLen > 1e-8 ? perp / perpLen : float2(0, 1);
            float sourceM = 0.03;
            float penumbraM = sourceM * along / max(rayLength - along, sourceM);
            float recvPixelM = 2.0 * max(-receiver.z, 0.05) / min(size.x, size.y);
            float maxPx = softTaps >= 6 ? 7.0 : 4.5;
            float radiusPx = clamp(penumbraM / max(recvPixelM, 1e-5), 1.25, maxPx);
            float jitter = (AnomalyContactIgn(receiver.xy) - 0.5) * 0.35;
            float occluded = 1;
            float tapWeight = 1;
            uint pairs = max(softTaps / 2, 1);
            [loop] for (uint t = 0; t < 4; t++)
            {
                if (t >= pairs)
                    break;
                float x = ((t + 0.5 + jitter) / (float)pairs) * radiusPx;
                [unroll] for (int s = 0; s < 2; s++)
                {
                    float2 uvN = uv0 + perp * (x * (s == 0 ? 1.0 : -1.0)) / size;
                    occluded += AnomalyContactNeighborOccluded(depth, playerDepth, excludePlayer,
                        uvN, size, width, height, receiver, normal, towardLight, along,
                        rayLength, thickness, exclusionCenter, exclusionHalf, viewToWorld) ? 1 : 0;
                    tapWeight += 1;
                }
            }
            return 1.0 - 0.9 * endFade * (occluded / max(tapWeight, 1));
        }
    }
    return 1;
}
float AnomalyContactVisibilityExcludingBounds(Texture2D<float> depth, float3 receiver,
    float3 normal, float3 towardLight, float rayLength, float thickness, uint budget,
    float3 exclusionCenter, float3 exclusionHalf, float3x3 viewToWorld)
{
    return AnomalyContactVisibilityCore(depth, depth, false, receiver, normal,
        towardLight, rayLength, thickness, budget, exclusionCenter, exclusionHalf, viewToWorld, 0);
}
float AnomalyContactVisibilityExcludingPlayer(Texture2D<float> depth, Texture2D<float> playerDepth,
    float3 receiver, float3 normal, float3 towardLight, float rayLength, float thickness, uint budget)
{
    return AnomalyContactVisibilityCore(depth, playerDepth, true, receiver, normal,
        towardLight, rayLength, thickness, budget, 0, 0, (float3x3)0, 0);
}
float AnomalyContactVisibilityExcludingPlayer(Texture2D<float> depth, Texture2D<float> playerDepth,
    float3 receiver, float3 normal, float3 towardLight, float rayLength, float thickness, uint budget,
    uint softTaps)
{
    return AnomalyContactVisibilityCore(depth, playerDepth, true, receiver, normal,
        towardLight, rayLength, thickness, budget, 0, 0, (float3x3)0, softTaps);
}
float AnomalyContactVisibility(Texture2D<float> depth, float3 receiver,
    float3 normal, float3 towardLight, float rayLength, float thickness, uint budget)
{
    return AnomalyContactVisibilityExcludingBounds(depth, receiver, normal,
        towardLight, rayLength, thickness, budget, 0, 0, (float3x3)0);
}
#endif
