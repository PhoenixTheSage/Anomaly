using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClientPlugin.ShaderFramework;
using ClientPlugin.Shaders;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRageMath;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using Vector4 = VRageMath.Vector4;

static class Program
{
    static int assertions;
    static void Check(bool condition, string label) { if (!condition) throw new Exception(label); assertions++; }
    static void Throws(Action action, string label) { try { action(); } catch { assertions++; return; } throw new Exception(label); }
    sealed class Art : IDisposable { internal int Disposals; public void Dispose() => Disposals++; }
    static void ArtQueue()
    {
        var retired = new Queue<Art>(); var state = new QueuedArtResource<Art>(retired.Enqueue);
        var pixels = new byte[] { 1, 2, 3, 4 };
        state.Set(1, 1, 1, pixels); pixels[0] = 9;
        Check(state.Pending.Rgba[0] == 1, "Art input must be copied");
        state.Set(0, 0, 0, null);
        state.Apply(_ => throw new Exception("Cancelled art uploaded"));
        Check(state.Pending == null && state.Resource == null, "Clear cancels pending upload");
        var art = new Art(); state.Set(1, 1, 1, pixels); state.Apply(_ => art);
        Throws(() => state.Set(1, 1, 1, new byte[3]), "Invalid byte count accepted");
        Check(ReferenceEquals(state.Resource, art) && retired.Count == 0, "Invalid replacement destroyed live art");
        state.Set(1, 1, 1, pixels);
        Check(state.Resource == null && retired.Count == 1 && art.Disposals == 0, "Retirement must defer GPU disposal");
        var pending = state.Pending;
        Throws(() => state.Set(2049, 1, 1, pixels), "Oversized art accepted");
        Check(ReferenceEquals(pending, state.Pending), "Invalid replacement destroyed pending art");
        Throws(() => state.Apply(_ => throw new Exception("GPU allocation failure")), "GPU failure swallowed");
        Check(ReferenceEquals(pending, state.Pending) && state.Resource == null, "Failed upload must retain pending data");
        state.Clear(); state.Clear(); retired.Dequeue().Dispose();
        Check(art.Disposals == 1 && retired.Count == 0, "Art retired more than once");
        state.Set(1, 1, 1, pixels); state.Set(-1, 1, 1, pixels);
        Check(state.Pending == null, "Nonpositive dimensions must preserve clear behavior");
    }
    static T[] Read<T>(Device device, ISrvBuffer source, int count) where T : struct
    {
        using var staging = new Buffer(device, new BufferDescription { SizeInBytes = source.Resource.Description.SizeInBytes, Usage = ResourceUsage.Staging, CpuAccessFlags = CpuAccessFlags.Read });
        device.ImmediateContext.CopyResource(source.Resource, staging);
        DataStream stream; device.ImmediateContext.MapSubresource(staging, MapMode.Read, MapFlags.None, out stream);
        try { return stream.ReadRange<T>(count); }
        finally { device.ImmediateContext.UnmapSubresource(staging, 0); stream.Dispose(); }
    }
    static void Upload(Device device)
    {
        var rc = new MyRenderContext(device.ImmediateContext);
        var grid = new VolumetricInteriorSnapshot.Grid { Cells = new[] { new Vector3I(3, -2, 7), new Vector3I(-1, 4, 2) } };
        var snapshot = new VolumetricInteriorSnapshot.Snapshot { Grids = new[] { grid } };
        using var upload = new VolumetricInteriorUpload(); upload.Upload(snapshot, Vector3D.Zero, rc);
        var cells = Read<Vector4I>(device, upload.Cells, 2); var rows = Read<Vector4>(device, upload.Grids, 7);
        Check(cells[0].X == -1 && cells[1].X == 3, "Topology must be lexicographically sorted");
        Check(rows[5].X == -1 && rows[5].Y == -2 && rows[6].Z == 7, "Cached bounds incorrect");
        long revision = upload.Revision; var cellBuffer = upload.Cells; var gridBuffer = upload.Grids;
        int creates = MyManagers.Buffers.Created;
        grid.WorldToLocal = MatrixD.CreateRotationZ(.3) * MatrixD.CreateTranslation(2, 3, 4);
        var camera = new Vector3D(1000000, -2000000, 3000000);
        upload.Upload(snapshot, camera, rc); rows = Read<Vector4>(device, upload.Grids, 7);
        var expected = MatrixD.CreateTranslation(camera) * grid.WorldToLocal;
        Check(Math.Abs(rows[3].X - (float)expected.M41) < 1 && rows[0].X == (float)expected.M11, "Moving camera/grid transforms incorrect");
        Check(upload.Revision == revision && ReferenceEquals(cellBuffer, upload.Cells) && ReferenceEquals(gridBuffer, upload.Grids) && creates == MyManagers.Buffers.Created, "Unchanged topology recreated GPU buffers");
        grid.Cells = new[] { new Vector3I(9, 8, 7) }; upload.Upload(snapshot, camera, rc);
        Check(upload.Revision == revision + 1 && Read<Vector4I>(device, upload.Cells, 1)[0].X == 9, "Replacement topology not uploaded");
        revision = upload.Revision; grid.Generation++; upload.Upload(snapshot, camera, rc);
        Check(upload.Revision == revision + 1, "Topology generation did not invalidate cache");
        snapshot.Grids = new[] { grid, new VolumetricInteriorSnapshot.Grid { Id = 2, Cells = new[] { Vector3I.Zero } } };
        upload.Upload(snapshot, camera, rc); revision = upload.Revision;
        Array.Reverse(snapshot.Grids); upload.Upload(snapshot, camera, rc);
        Check(upload.Revision == revision + 1 && Read<Vector4>(device, upload.Grids, 14)[11].Y == 1, "Reordering grids must update cell offsets");
        grid.IsCurrent = false; upload.Upload(snapshot, camera, rc);
        Check(!upload.Complete && upload.GridCount == 1, "Invalid topology must degrade exclusion");
        snapshot.IsFresh = false; upload.Upload(snapshot, camera, rc);
        Check(!upload.Complete && upload.GridCount == 0 && Read<Vector4>(device, upload.Grids, 1)[0] == Vector4.Zero, "Stale snapshot reused old grids");
        snapshot.IsFresh = true; snapshot.Complete = false; snapshot.Grids = new[] { new VolumetricInteriorSnapshot.Grid() };
        upload.Upload(snapshot, camera, rc); Check(!upload.Complete && upload.GridCount == 0, "Empty budget-limited snapshot became complete");

        // Record two different cameras on deferred lists before executing either.
        grid.IsCurrent = true; snapshot.Grids = new[] { grid }; snapshot.Complete = true;
        using var deferred = new DeviceContext(device); var deferredRc = new MyRenderContext(deferred);
        upload.Upload(snapshot, Vector3D.Zero, deferredRc);
        using var first = new Buffer(device, new BufferDescription { SizeInBytes = upload.Grids.Resource.Description.SizeInBytes, Usage = ResourceUsage.Staging, CpuAccessFlags = CpuAccessFlags.Read });
        deferred.CopyResource(upload.Grids.Resource, first); using var firstList = deferred.FinishCommandList(false);
        upload.Upload(snapshot, new Vector3D(10, 0, 0), deferredRc);
        using var second = new Buffer(device, first.Description);
        deferred.CopyResource(upload.Grids.Resource, second); using var secondList = deferred.FinishCommandList(false);
        device.ImmediateContext.ExecuteCommandList(firstList, false); device.ImmediateContext.ExecuteCommandList(secondList, false);
        float firstX = Read<Vector4>(device, new SrvBuffer(first), 7)[3].X;
        float secondX = Read<Vector4>(device, new SrvBuffer(second), 7)[3].X;
        Check(Math.Abs(secondX - firstX - 10 * grid.WorldToLocal.M11) < .0001, "Discard mapping overwrote an earlier deferred frame");
    }
    static float Pixel(Device device, Texture2D texture)
    {
        var desc = texture.Description; desc.Usage = ResourceUsage.Staging; desc.BindFlags = BindFlags.None; desc.CpuAccessFlags = CpuAccessFlags.Read;
        using var staging = new Texture2D(device, desc); device.ImmediateContext.CopyResource(texture, staging);
        var box = device.ImmediateContext.MapSubresource(staging, 0, MapMode.Read, MapFlags.None);
        try { return Utilities.Read<float>(box.DataPointer); }
        finally { device.ImmediateContext.UnmapSubresource(staging, 0); }
    }
    static void Recovery(Device device)
    {
        using var texture = new Texture2D(device, new Texture2DDescription { Width = 4, Height = 4, MipLevels = 1, ArraySize = 1, Format = Format.R32_Float, SampleDescription = new SampleDescription(1, 0), Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget });
        using var view = new RenderTargetView(device, texture); using var checkpoint = new RenderTextureCheckpoint();
        var context = device.ImmediateContext; Action<float> fill = value => context.ClearRenderTargetView(view, new SharpDX.Mathematics.Interop.RawColor4(value, 0, 0, 0));
        fill(10); checkpoint.Capture(device, context, texture); fill(20); // legacy far interval already rendered
        bool owned = true; int replay = 0;
        var failure = new InvalidOperationException("Injected partial composite failure");
        var result = VolumeCompositeRecovery.Execute(() => { fill(99); throw failure; },
            () => checkpoint.Restore(context), () => owned = false,
            () => { Check(!owned && Pixel(device, texture) == 10, "Recovery replay did not receive clean scene/full ownership"); replay++; fill(30); }, _ => false);
        Check(ReferenceEquals(result, failure) && replay == 1 && Pixel(device, texture) == 30, "Failed composite left partial output or replayed twice");
        replay = 0; result = VolumeCompositeRecovery.Execute(() => fill(40), () => throw failure, () => throw failure, () => replay++, _ => false);
        Check(result == null && replay == 0 && Pixel(device, texture) == 40, "Successful composition triggered recovery");
        bool revoked = false;
        Throws(() => VolumeCompositeRecovery.Execute(() => throw failure, () => throw new Exception("Copy failed"), () => revoked = true, () => replay++, _ => false), "Copy failure swallowed");
        Check(revoked && replay == 0, "Copy failure must revoke ownership without replaying partial output");
        int gpuCalls = 0;
        Throws(() => VolumeCompositeRecovery.Execute(() => throw failure, () => gpuCalls++, () => gpuCalls++, () => gpuCalls++, _ => true), "Lost device swallowed");
        Check(gpuCalls == 0, "Lost-device recovery attempted GPU operations");
        checkpoint.Reset(); fill(50); checkpoint.Restore(context); Check(Pixel(device, texture) == 50, "Reset checkpoint restored stale frame");
    }
    static void Registry(string shader)
    {
        string a = "framework-a", b = "framework-b"; float intervalA = -1, intervalB = -1;
        Check(VolumetricMediumRegistry.Register(a, shader, Array.Empty<string>()), "Test provider registration failed");
        Check(VolumetricMediumRegistry.Register(b, shader, Array.Empty<string>()), "Second test provider registration failed");
        VolumetricMediumRegistry.SetEnabled(a, true); VolumetricMediumRegistry.SetEnabled(b, true);
        Check(VolumetricMediumRegistry.HasEnabledMedia && !VolumetricMediumRegistry.HasReadyMedia, "Presence check ignored parameter readiness");
        foreach (var id in new[] { a, b }) VolumetricMediumRegistry.SetParameters(id, Array.Empty<float>(), new double[] { -1, -1, -1, 1, 1, 1 }, new float[3]);
        VolumetricMediumRegistry.SetIntervalCallback(a, distance => intervalA = distance);
        VolumetricMediumRegistry.SetIntervalCallback(b, distance => { intervalB = distance; if (distance == 0) throw new Exception("Bad provider reset"); });
        var captured = VolumetricMediumRegistry.Capture(out long revision);
        Check(VolumetricMediumRegistry.HasReadyMedia && VolumetricMediumRegistry.Commit(3, revision, true, true, true, true, captured), "Ready providers failed commit");
        foreach (var medium in captured) medium.Interval(100);
        VolumetricMediumRegistry.Unregister(a);
        Throws(VolumetricMediumRegistry.ResetIntervals, "Reset error swallowed");
        Check(intervalA == 0 && intervalB == 0 && !VolumetricMediumRegistry.OwnsNearInterval(a, 3) && !VolumetricMediumRegistry.OwnsNearInterval(b, 3), "Reset did not clear every frozen/current callback and ownership");
        VolumetricMediumRegistry.Unregister(b); VolumetricMediumRegistry.ResetIntervals();
        Check(!VolumetricMediumRegistry.HasEnabledMedia && !VolumetricMediumRegistry.HasReadyMedia, "Empty registry remained ready");
    }
    static void Main(string[] args)
    {
        if (args.Length != 1 || !File.Exists(args[0])) throw new ArgumentException("Supply a production .hlsli path");
        ArtQueue(); Registry(Path.GetFullPath(args[0]));
        using (var device = new Device(DriverType.Warp, DeviceCreationFlags.None))
        { MyManagers.Buffers.Device = device; Upload(device); Recovery(device); }
        Check(MyManagers.Buffers.Created == MyManagers.Buffers.Disposed, "Upload leaked a D3D buffer");
        Console.WriteLine("PASS: " + assertions + " framework assertions, linked production code and D3D11 WARP; art cancellation/retirement, topology cache/transforms/deferred discard, volume rollback/replay and frozen provider resets.");
    }
}
