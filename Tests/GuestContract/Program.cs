using System;
using System.IO;
using ClientPlugin.Backend;

static class Program
{
    static int Main()
    {
        StageSourceContracts.Run();
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
        var parsed = PackDefinition.Parse("{\"id\":\"pack\",\"priority\":7,\"name\":\"quoted \\\"[{}]\\\"\",\"passes\":[{\"id\":\"pack.test\",\"slot\":\"AfterLighting\",\"file\":\"test.hlsl\",\"binds\":[{\"catalog\":\"history[{}]\",\"slot\":7}],\"temporal\":[\"Reactive\"]}]}");
        if (parsed.Id != "pack" || parsed.Priority != 7 || parsed.Passes[0].Binds[0].Catalog != "history[{}]"
            || parsed.Passes[0].Temporal[0] != "Reactive" || parsed.Name != "quoted \"[{}]\"")
            return Fail("structured manifest parsing lost escaped or nested fields");
        foreach (string invalid in new[] { "{", "{}", "{\"id\":\"a\",\"id\":\"b\"}", "{\"id\":\"a\",\"passes\":[{\"slot\":3}]}" })
        {
            try { PackDefinition.Parse(invalid); return Fail("accepted malformed manifest: " + invalid); }
            catch (System.IO.InvalidDataException) { }
        }
        string validManifest = File.ReadAllText(Path.Combine(root, "anomaly.json")).Replace("{\n", "{\n\"id\":\"fixture\",\n");
        File.WriteAllText(Path.Combine(root, "anomaly.json"), validManifest);
        GuestPassList.RegisterRoot("fixture", root, "fixture.color");
        var before = GuestPassList.Snapshot()[0];
        var captured=GuestPassList.SourceSnapshot()[0];
        string sourceColor=Path.Combine(slot,"Color.hlsl");
        var capturedBytes=captured.Bytes("Fullscreen/AfterLighting/Color.hlsl");
        capturedBytes[0]=0;
        if(captured.Bytes("Fullscreen/AfterLighting/Color.hlsl")[0]==0)return Fail("captured shader bytes were exposed as mutable arrays");
        File.AppendAllText(sourceColor,"// registration source changed\n");
        if(File.ReadAllText(before.File).Contains("source changed"))return Fail("active generation follows mutable pack paths");
        var nextCapture=PackSourceSnapshot.Capture(root,"fixture");
        if(nextCapture.Fingerprint==captured.Fingerprint || nextCapture.Root==captured.Root)return Fail("changed pack bytes reused the previous generation");
        try { GuestPassList.RegisterRoot("fixture", root, "fixture.absent"); return Fail("missing required program accepted"); }
        catch (System.IO.InvalidDataException) { }
        try { GuestPassList.RegisterRoot("other-owner", root); return Fail("duplicate program ownership accepted"); }
        catch (System.IO.InvalidDataException) { }
        if (!object.ReferenceEquals(before, GuestPassList.Snapshot()[0])) return Fail("rejected registration changed current generation");
        File.WriteAllText(Path.Combine(root, "anomaly.json"), "{broken");
        try { GuestPassList.RegisterRoot("fixture", root); return Fail("invalid reload succeeded"); }
        catch (System.IO.InvalidDataException) { }
        if (!object.ReferenceEquals(before, GuestPassList.Snapshot()[0])) return Fail("invalid reload changed active registration");
        GuestPassList.UnregisterRoot("fixture");
        if (GuestPassList.Snapshot().Count != 0) return Fail("unregister retained owned programs");
        Console.WriteLine("PASS: shared parser, escaped/nested fields, malformed input, required program, transactional reload and pending renderer");
        return 0;
    }

    static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
