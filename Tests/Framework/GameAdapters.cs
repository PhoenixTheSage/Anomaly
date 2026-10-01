using System;
using SharpDX;
using SharpDX.Direct3D11;
using VRageMath;
using Buffer = SharpDX.Direct3D11.Buffer;

// Only game plumbing is substituted. Production upload, recovery, registry and
// art queue code are linked unchanged; resource creation/mapping uses real D3D11.
namespace VRage.Render11.Common { }
namespace VRage.Render11.Resources
{
    internal interface ISrvBuffer { Buffer Resource { get; } }
    internal sealed class SrvBuffer : ISrvBuffer, IDisposable
    {
        public Buffer Resource { get; }
        readonly ShaderResourceView srv;
        internal SrvBuffer(Buffer resource, Device device = null) { Resource = resource; if (device != null) srv = new ShaderResourceView(device, resource); }
        public void Dispose() { srv?.Dispose(); Resource.Dispose(); }
    }
}
namespace VRage.Render11.RenderContext
{
    internal sealed class MyRenderContext
    {
        internal DeviceContext DeviceContext;
        internal MyRenderContext(DeviceContext context) => DeviceContext = context;
    }
}
namespace VRageRender
{
    internal sealed class MyMapping
    {
        readonly DeviceContext context;
        readonly Buffer buffer;
        readonly DataBox box;
        MyMapping(VRage.Render11.RenderContext.MyRenderContext rc, VRage.Render11.Resources.ISrvBuffer resource)
        {
            context = rc.DeviceContext; buffer = resource.Resource;
            box = context.MapSubresource(buffer, 0, MapMode.WriteDiscard, MapFlags.None);
        }
        internal static MyMapping MapDiscard(VRage.Render11.RenderContext.MyRenderContext rc, VRage.Render11.Resources.ISrvBuffer buffer) => new MyMapping(rc, buffer);
        internal void WriteAndPosition<T>(T[] values, int count) where T : struct => Utilities.Write(box.DataPointer, values, 0, count);
        internal void Unmap() => context.UnmapSubresource(buffer, 0);
    }
}
namespace ClientPlugin.ShaderFramework
{
    internal static class MyManagers
    {
        internal static class Buffers
        {
            internal static Device Device;
            internal static int Created, Disposed;
            internal static VRage.Render11.Resources.ISrvBuffer CreateSrv(string name, int elements, int byteStride, IntPtr? initData = null, ResourceUsage usage = ResourceUsage.Default)
            {
                var description = new BufferDescription { SizeInBytes = elements * byteStride, StructureByteStride = byteStride,
                    Usage = usage, BindFlags = BindFlags.ShaderResource, OptionFlags = ResourceOptionFlags.BufferStructured,
                    CpuAccessFlags = usage == ResourceUsage.Dynamic ? CpuAccessFlags.Write : CpuAccessFlags.None };
                var buffer = initData.HasValue ? new Buffer(Device, initData.Value, description) : new Buffer(Device, description);
                Created++; return new VRage.Render11.Resources.SrvBuffer(buffer, Device);
            }
            internal static void Dispose(VRage.Render11.Resources.ISrvBuffer buffer) { ((IDisposable)buffer).Dispose(); Disposed++; }
        }
    }
    internal static class VolumetricInteriorSnapshot
    {
        internal sealed class Snapshot
        {
            internal bool IsFresh = true, Complete = true;
            internal string Status = "Test snapshot";
            internal Grid[] Grids = Array.Empty<Grid>();
        }
        internal sealed class Grid
        {
            internal long Id = 1, Generation = 1;
            internal bool IsCurrent = true;
            internal MatrixD WorldToLocal = MatrixD.Identity;
            internal float CellSize = 2.5f;
            internal Vector3I[] Cells = Array.Empty<Vector3I>();
        }
    }
}
