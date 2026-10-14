using System;
using System.IO;
using ClientPlugin.Backend;

static class Program
{
    static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "anomaly-guest-contract");
        if (Directory.Exists(root))
            Directory.Delete(root, true);
        string slot = Path.Combine(root, "Fullscreen", "AfterLighting");
        Directory.CreateDirectory(slot);
        File.WriteAllText(Path.Combine(slot, "Color.hlsl"), "float4 main() : SV_Target { return float4(1, 0, 0, 1); }\n");
        Directory.CreateDirectory(Path.Combine(root, "GBuffer"));
        File.WriteAllText(Path.Combine(root, "GBuffer", "objectid.hlsl"),
            "uint __pixel_shader(float4 position : SV_Position) : SV_Target0 { return 1; }\n");
        File.WriteAllText(Path.Combine(root, "anomaly.json"),
            "{\n  \"attachments\": [\n    { \"name\": \"objectid\", \"format\": \"R32_UINT\", \"file\": \"GBuffer/objectid.hlsl\" },\n    { \"name\": \"packed\", \"format\": \"GBuffer1.a\" }\n  ],\n  \"passes\": [\n    { \"id\": \"fixture.color\", \"slot\": \"AfterLighting\", \"file\": \"Fullscreen/AfterLighting/Color.hlsl\", \"compose\": \"IsolatedAdd\" }\n  ]\n}\n");

        var passes = GuestPassList.Load(root, "fixture");
        if (passes.Count != 1)
            return Fail("expected one pass, got " + passes.Count);
        var pass = passes[0];
        if (pass.Id != "fixture.color" || pass.Slot != "AfterLighting" || pass.Compose != "IsolatedAdd")
            return Fail("pass fields diverged");
        if (pass.Mark != GuestMark.AfterLighting || GuestMark.AfterAtmosphere != 2 || GuestMark.AfterTransparent != 3
            || GuestMark.BeforeTonemap != 4 || GuestMark.AfterTonemap != 5 || GuestMark.AfterUpscale != 6)
            return Fail("frame marks diverged");
        if (GuestPassList.MarkCount(passes, GuestMark.AfterLighting) != 1)
            return Fail("mark count diverged");
        var attachments = GuestPassList.LoadAttachments(root);
        if (attachments.Count != 1 || attachments[0].Name != "objectid" || attachments[0].Slot != 0 || attachments[0].NativeFormat != 4)
            return Fail("attachment parse diverged");
        if (!File.Exists(attachments[0].File))
            return Fail("attachment shader path diverged");
        if (!RendererPaths.UseSharedBus(RendererPaths.Pending) || RendererPaths.UseDx11Device(RendererPaths.Pending) || RendererPaths.UseGuestImages(RendererPaths.Pending))
            return Fail("pending route opened a device path");
        if (!RendererPaths.UseDx11Device(RendererPaths.DirectX11) || RendererPaths.UseGuestImages(RendererPaths.DirectX11))
            return Fail("DX11 route opened the guest path");
        if (!RendererPaths.UseGuestImages(RendererPaths.Vulkan) || RendererPaths.UseDx11Device(RendererPaths.Vulkan))
            return Fail("Vulkan route opened the DX11 device path");
        if (!RendererPaths.IsPathOpen(RendererPaths.Vulkan, RendererPaths.Shared) || RendererPaths.IsPathOpen(RendererPaths.Vulkan, RendererPaths.Dx11Device))
            return Fail("path table diverged");
        Console.WriteLine("guest contract pass list matches on the shared parser");
        return 0;
    }

    static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
