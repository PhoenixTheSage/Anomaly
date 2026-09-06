#define ANOMALY_VELOCITY
#define USE_SIMPLE_INSTANCING
#include <Anomaly.hlsli>

float4 main(float3 position : POSITION, uint instanceId : SV_InstanceID) : SV_Position
{
    matrix transform = matrix(0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
    float2 velocity = AnomalyComputeVelocity(position, transform, instanceId);
    return float4(velocity, 0, 1);
}
