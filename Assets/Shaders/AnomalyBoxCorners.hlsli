#ifndef ANOMALY_BOX_CORNERS_INCLUDED
#define ANOMALY_BOX_CORNERS_INCLUDED
static const float3 kCube[36] =
{
    float3(1, -1, -1), float3(1, 1, -1), float3(1, 1, 1),
    float3(1, -1, -1), float3(1, 1, 1), float3(1, -1, 1),
    float3(-1, -1, 1), float3(-1, 1, 1), float3(-1, 1, -1),
    float3(-1, -1, 1), float3(-1, 1, -1), float3(-1, -1, -1),
    float3(-1, 1, -1), float3(-1, 1, 1), float3(1, 1, 1),
    float3(-1, 1, -1), float3(1, 1, 1), float3(1, 1, -1),
    float3(-1, -1, 1), float3(-1, -1, -1), float3(1, -1, -1),
    float3(-1, -1, 1), float3(1, -1, -1), float3(1, -1, 1),
    float3(-1, -1, 1), float3(1, -1, 1), float3(1, 1, 1),
    float3(-1, -1, 1), float3(1, 1, 1), float3(-1, 1, 1),
    float3(1, -1, -1), float3(-1, -1, -1), float3(-1, 1, -1),
    float3(1, -1, -1), float3(-1, 1, -1), float3(1, 1, -1)
};
#endif
