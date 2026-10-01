using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRageRender;

namespace ClientPlugin.ShaderFramework;

/// <summary>Bindings Keen establishes once before atmospheres and inherits in billboards.</summary>
internal static class TransparentStageBindings
{
    internal static void RestoreAfterAtmosphere(MyRenderContext rc)
    {
        // MyTransparentRendering.Render binds PS b0 before RenderGBuffer.
        // Billboard BindResourcesCommon rebinds VS b0, but never PS b0 or PS
        // standard samplers. Temporal contribution uses/clears b0 and fullscreen
        // draws replace s0-s2. Restore through Keen's wrappers so its caches and
        // deferred command list agree; native immediate-context state is unrelated.
        rc.VertexShader.SetConstantBuffer(0, MyCommon.FrameConstants);
        rc.PixelShader.SetConstantBuffer(0, MyCommon.FrameConstants);
        rc.PixelShader.SetSamplers(0, MySamplerStateManager.StandardSamplers);
        rc.PixelShader.SetSampler(15, MySamplerStateManager.Shadowmap);
    }
}
