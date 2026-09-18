using System;
using System.IO;
using System.Runtime.InteropServices;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using VRage.Render11.Common;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRageRender;

namespace ClientPlugin.ShaderFramework;

/// <summary>Integrates already-lit shared coefficients. Caller owns stage ordering and publication.</summary>
internal static class VolumetricIntegrator
{
    static ComputeShader shader;
    static IConstantBuffer constants;
    static readonly VolumetricGpuTimer Timer = new();
    internal static double GpuMilliseconds => Timer.LastMilliseconds;
    internal static void PollTimings(DeviceContext immediateContext) => Timer.Poll(immediateContext);
    [StructLayout(LayoutKind.Sequential,Size=32)]
    struct Parameters
    {
        public uint Width,Height,Slices;
        public float Distance,Near;
    }
    internal static void Execute(MyRenderContext rc,VolumetricFrameResources resources,float distance)
    {
        if(rc == null || !rc.IsInitialized || resources?.Coefficients == null)
            throw new InvalidOperationException("Volume integration resources unavailable");
        if(float.IsNaN(distance) || float.IsInfinity(distance) || distance <= 0 || distance > 8000)
            throw new ArgumentOutOfRangeException(nameof(distance));
        if(shader == null)
        {
            var code=MyShaderCompiler.Compile(Path.Combine(ShaderCompileIntercept.IncludeDirectory,"AnomalyVolumeIntegrate.hlsl"),
                Array.Empty<ShaderMacro>(),MyShaderProfile.cs_5_0,"Anomaly.SharedVolume.Integrate",false);
            if(code == null || code.Length == 0) throw new InvalidOperationException("Shared integration shader failed");
            try
            {
                shader=new ComputeShader(MyRender11.DeviceInstance,code);
                constants=MyManagers.Buffers.CreateConstantBuffer("Anomaly.VolumeIntegrate",32,usage:ResourceUsage.Dynamic);
            }
            catch { Release(); throw; }
        }
        var parameters=new Parameters{Width=(uint)resources.Width,Height=(uint)resources.Height,
            Slices=(uint)resources.Slices,Distance=distance,Near=1};
        var mapping=MyMapping.MapDiscard(rc,constants);
        try { mapping.WriteAndPosition(ref parameters); }
        finally { mapping.Unmap(); }
        var context=rc.DeviceContext;
        Timer.Begin(context);
        try
        {
            rc.ComputeShader.Set(shader);
            rc.ComputeShader.SetConstantBuffer(0,constants);
            context.ComputeShader.SetShaderResources(0,resources.Coefficients.Srv,resources.Source.Srv);
            context.ComputeShader.SetUnorderedAccessView(0,resources.ScatteringTransmittance.Uav);
            context.Dispatch((resources.Width+7)/8,(resources.Height+7)/8,1);
        }
        finally
        {
            context.ComputeShader.SetUnorderedAccessView(0,null);
            context.ComputeShader.SetShaderResources(0,new ShaderResourceView[2]);
            Timer.End(context);
            rc.ClearState();
        }
        // Deliberately does not Publish: temporal/reactive products and the
        // eventual composite must also succeed before interval ownership changes.
    }
    internal static void Release()
    {
        Timer.Dispose(); shader?.Dispose(); shader=null;
        if(constants!=null) MyManagers.Buffers.Dispose(constants);
        constants=null;
    }
}
