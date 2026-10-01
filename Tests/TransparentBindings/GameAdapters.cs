using SharpDX.Direct3D11;
using Buffer = SharpDX.Direct3D11.Buffer;

// Cache-aware engine plumbing around native D3D11. Production restoration is
// linked unchanged; adapters intentionally model Keen's reference equality skip.
namespace VRage.Render11.Resources
{
    static class MySamplerStateManager
    {
        internal static SamplerState[] StandardSamplers;
        internal static SamplerState Shadowmap;
    }
}
namespace VRageRender { static class MyCommon { internal static Buffer FrameConstants; } }
namespace VRage.Render11.RenderContext
{
    sealed class Stage
    {
        readonly CommonShaderStage native;
        internal readonly Buffer[] Constants = new Buffer[8];
        internal readonly SamplerState[] Samplers = new SamplerState[16];
        internal Stage(CommonShaderStage stage) => native = stage;
        internal void SetConstantBuffer(int slot, Buffer value)
        {
            if (ReferenceEquals(Constants[slot], value)) return;
            Constants[slot] = value; native.SetConstantBuffer(slot, value);
        }
        internal void SetSampler(int slot, SamplerState value)
        {
            if (ReferenceEquals(Samplers[slot], value)) return;
            Samplers[slot] = value; native.SetSampler(slot, value);
        }
        internal void SetSamplers(int first, SamplerState[] values)
        {
            for (int i = 0; i < values.Length; i++) Samplers[first + i] = values[i];
            native.SetSamplers(first, values);
        }
    }
    sealed class MyRenderContext
    {
        internal readonly Stage VertexShader, PixelShader;
        internal MyRenderContext(DeviceContext context)
        { VertexShader = new Stage(context.VertexShader); PixelShader = new Stage(context.PixelShader); }
    }
}
