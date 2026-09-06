// Keep pass dispatch on the system-include search path. Anomaly's include root
// wins for GBuffer/VertexStage.hlsli; every other stage falls through to Keen.
// This avoids relying on nested local-include redirection for resident material
// shaders while preserving Keen's renderer and pass implementations.
#include <Geometry/Passes/PassesDefines.hlsli>

#ifndef RENDERING_PASS
#include <Geometry/Passes/GBuffer/VertexStage.hlsli>

#elif RENDERING_PASS == RENDERING_PASS_GBUFFER
#include <Geometry/Passes/GBuffer/VertexStage.hlsli>

#elif RENDERING_PASS == RENDERING_PASS_DEPTH
#include <Geometry/Passes/Depth/VertexStage.hlsli>

#elif RENDERING_PASS == RENDERING_PASS_FORWARD
#include <Geometry/Passes/Forward/VertexStage.hlsli>

#elif RENDERING_PASS == RENDERING_PASS_HIGHLIGHT
#include <Geometry/Passes/Highlight/VertexStage.hlsli>

#elif RENDERING_PASS == RENDERING_PASS_FOLIAGE_STREAMING
#include <Geometry/Passes/FoliageStreaming/VertexStage.hlsli>

#elif RENDERING_PASS == RENDERING_PASS_TRANSPARENT
#include <Geometry/Passes/Transparent/VertexStage.hlsli>

#elif RENDERING_PASS == RENDERING_PASS_TRANSPARENT_FOR_DECALS
#include <Geometry/Passes/TransparentForDecals/VertexStage.hlsli>

#elif RENDERING_PASS == RENDERING_PASS_TEST
#include <Geometry/Passes/Test/VertexStage.hlsli>

#else
#error "Undefined RENDERING_PASS"
#endif
