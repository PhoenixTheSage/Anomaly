#if defined(ANOMALY_FAR_SUN)
#define __pixel_shader __anomaly_native_depth_pixel
#include <Keen/Geometry/Passes/Depth/PixelStage.hlsli>
#undef __pixel_shader
#include <FarSunClip.hlsli>
void __pixel_shader(PixelStageInput input) {
    AnomalyClipResident(input.position);
    __anomaly_native_depth_pixel(input);
}
#else
#include <Keen/Geometry/Passes/Depth/PixelStage.hlsli>
#endif
