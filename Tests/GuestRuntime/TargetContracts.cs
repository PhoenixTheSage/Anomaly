using System;
using System.Runtime.InteropServices;
using System.Linq;
using ClientPlugin.Backend;
using ClientPlugin.Rendering;

static partial class Program
{
    static void ResourceContracts()
    {
        int features = GuestBridge.SupportedPacketFlags;
        GuestBridge.SupportedPacketFlags &= ~8192;
        try { GuestBridgeClient.ResourceCommand(3, 1); throw new Exception("Older host was accepted"); }
        catch (NotSupportedException error) { Require(error.Message.Contains("Update SE-Vulkan"), "Older-host refusal is not actionable"); }
        finally { GuestBridge.SupportedPacketFlags = features; }
        GuestBridge.TestResourceEpoch = 1;
        GuestBridge.CreatedImages.Clear(); GuestBridge.ResourceCommands.Clear();
        Require(GuestCatalogTargets.Ensure("parity", "parity.fp16", 7, 5, 10), "FP16 target request refused");
        GuestCatalogTargets.Clear("parity.fp16", 2.5f, .125f, .75f, -1f);
        GuestCatalogTargets.Flush(); GuestCatalogTargets.Flush();
        Require(GuestBridge.CreatedImages.Count == 1 && GuestBridge.CreatedImages[0].Format == 1, "Catalog format or idempotent ensure failed");
        ulong image = GuestBridge.CreatedImages[0].Id;
        Require(GuestBridge.ResourceCommands.Count == 1 && BitConverter.ToSingle(GuestBridge.ResourceCommands[0], 44) == -1, "Queued clear lost negative alpha");
        Require(!GuestCatalogTargets.Ensure("other", "parity.fp16", 7, 5, 10), "Duplicate target ownership accepted");
        GuestCatalogTargets.Ensure("parity", "parity.fp16", 9, 3, 10); GuestCatalogTargets.Flush();
        Require(GuestBridge.CreatedImages.Count == 2 && GuestBridge.CreatedImages[1].Id == image, "Resize abandoned the stable image ID");
        GuestCatalogTargets.Release("parity"); GuestCatalogTargets.Flush();
        Require(GuestBridge.ResourceCommands.Any(c => BitConverter.ToInt32(c, 8) == 1 && BitConverter.ToUInt64(c, 16) == image), "Unpublish did not retire target");
        GuestBridge.TestResourceEpoch = 0;
        Console.WriteLine("PASS: format-aware catalog targets, queued negative-alpha clears, stable resize IDs, ownership, retirement and older-host refusal");
    }
}

static partial class NativeProbe
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ResourceFn(IntPtr context, IntPtr command);
    public static byte[] Execution(ulong pipeline)
    {
        var packet = new byte[48]; Buffer.BlockCopy(new[] { 48, 1 }, 0, packet, 0, 8);
        Buffer.BlockCopy(BitConverter.GetBytes(pipeline), 0, packet, 8, 8);
        var pin = GCHandle.Alloc(packet, GCHandleType.Pinned);
        try { Check(Fn<ResourceFn>("sevk_guest_execution_status")(context, pin.AddrOfPinnedObject())); return packet; }
        finally { pin.Free(); }
    }
    public static bool Resource(byte[] command)
    {
        if (!Enabled) return true;
        var pin = GCHandle.Alloc(command, GCHandleType.Pinned);
        try { Check(Fn<ResourceFn>("sevk_guest_resource_command")(context, pin.AddrOfPinnedObject())); return true; }
        finally { pin.Free(); }
    }
    public static void TargetContracts()
    {
        if (!Enabled) return;
        const ulong image = 83021;
        ImageCreate(image, 7, 5, 1, 1);
        var command = new byte[48];
        Buffer.BlockCopy(new[] { 48, 1, 3, 1 }, 0, command, 0, 16);
        Buffer.BlockCopy(BitConverter.GetBytes(image), 0, command, 16, 8);
        var values = new[] { 2.5f, .125f, .75f, -1f };
        Buffer.BlockCopy(values, 0, command, 32, 16);
        ResetQueue(); Resource(command); Replay(1);
        void CheckPixels()
        {
            var bytes = ReadRaw(image, 7 * 5 * 8);
            for (int pixel = 0; pixel < 35; pixel++) for (int channel = 0; channel < 4; channel++)
                if (Math.Abs((float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(bytes, pixel * 8 + channel * 2)) - values[channel]) > .001f)
                    throw new Exception("FP16 clear/HDR/negative-alpha contract failed");
        }
        CheckPixels();
        ImageCreate(image, 7, 5, 1, 1); // Identical ensure must preserve contents.
        CheckPixels();
        Buffer.BlockCopy(BitConverter.GetBytes(1), 0, command, 8, 4);
        Resource(command); Resource(command); // Release is idempotent.
        Console.WriteLine("GPU PASS: queued FP16 clear, HDR/negative alpha, idempotent create and release");
    }
}
