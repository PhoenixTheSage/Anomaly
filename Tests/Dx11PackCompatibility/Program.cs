using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.D3DCompiler;

static class Program
{
    const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    static int checks;
    static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
    static object Call(Type type, string name, params object[] args) => type.GetMethod(name, Static).Invoke(null, args);

    static int Main(string[] args)
    {
        string tfm = args[0], mode = args.Length > 1 ? args[1] : "uploads";
        string game = @"T:\SteamLibrary\steamapps\common\SpaceEngineers\Bin64";
        string configuration=args.Length>2?args[2]:"Debug";
        string build = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../../ClientPlugin/bin",configuration,tfm));
        Assembly keen = null;
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) => {
            if (keen != null && new AssemblyName(e.Name).Name == "VRage.Render11") return keen;
            foreach (string dir in new[] { build, game }) {
                string file = Path.Combine(dir, new AssemblyName(e.Name).Name + ".dll");
                if (File.Exists(file)) return Assembly.LoadFrom(file);
            }
            return null;
        };
        try {
            // Only suppress Keen's game/platform bootstrap; keep its real resource
            // interfaces and render-context shader binding implementation.
            using var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(game);
            using (var module = ModuleDefinition.ReadModule(Path.Combine(game, "VRage.Render11.dll"), new ReaderParameters { AssemblyResolver = resolver }))
            using (var bytes = new MemoryStream()) {
                var startup = module.GetType("VRageRender.MyRender11").Methods.First(m => m.Name == ".cctor");
                startup.Body.Instructions.Clear(); startup.Body.ExceptionHandlers.Clear();
                startup.Body.Instructions.Add(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ret));
                module.Write(bytes);
#if NET10_0
                bytes.Position = 0;
                keen = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromStream(bytes);
#else
                keen = Assembly.Load(bytes.ToArray());
#endif
            }
            var core = Assembly.LoadFrom(Path.Combine(build, "Anomaly.dll"));
            if(mode=="snapshot") {
                var native=Assembly.LoadFrom(Path.Combine(build,"Anomaly.Dx11.dll"));
                var registry=native.GetType("ClientPlugin.Shaders.ShaderPackRegistry");
                string root=Path.Combine(Path.GetTempPath(),"AnomalySnapshot-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                try {
                    File.WriteAllText(Path.Combine(root,"anomaly.json"),"{\"id\":\"snapshot.fixture\",\"passes\":[{\"id\":\"snapshot.color\",\"slot\":\"AfterLighting\",\"file\":\"color.hlsl\"}]}");
                    string file=Path.Combine(root,"color.hlsl");File.WriteAllText(file,"float4 main():SV_Target{return 1;}\n");
                    Directory.CreateDirectory(Path.Combine(root,"Overlay/Post.FXAA"));Directory.CreateDirectory(Path.Combine(root,"Inject/Lighting"));
                    File.WriteAllText(Path.Combine(root,"Overlay/Post.FXAA/Fxaa.hlsl"),"// shared overlay fixture");File.WriteAllText(Path.Combine(root,"Inject/Lighting/fixture.h"),"// shared injection fixture");
                    object[] request={"snapshot.fixture",root,"snapshot.color",null};
                    Check((bool)registry.GetMethod("TryRegister",Static).Invoke(null,request),"DX11 accepts registration before assets/device availability: "+request[3]);
                    var pending=(System.Collections.IDictionary)registry.GetField("Pending",Static).GetValue(null);
                    object pack=pending["snapshot.fixture"];string frozen=(string)pack.GetType().GetField("Root").GetValue(pack);
                    Check(frozen!=root && File.Exists(Path.Combine(frozen,"color.hlsl")),"DX11 compiles from captured pack source paths");
                    var guest=core.GetType("ClientPlugin.Backend.GuestPassList");
                    var host=core.GetType("ClientPlugin.Backend.RenderHost");
                    host.GetMethod("QueueRegister",Static,null,new[]{typeof(string),typeof(string),typeof(string)},null).Invoke(null,new object[]{"snapshot.fixture",root,"snapshot.color"});
                    var sources=(System.Collections.IEnumerable)Call(guest,"SourceSnapshot");object captured=sources.Cast<object>().Single();
                    Check((string)captured.GetType().GetProperty("Root").GetValue(captured)==frozen,"DX11 and Vulkan consume the same shared source generation");
                    Check(((System.Collections.IList)pack.GetType().GetField("Overlays").GetValue(pack)).Count==1 && ((System.Collections.IList)pack.GetType().GetField("InjectFiles").GetValue(pack)).Count==1,"DX11 consumes the shared overlay and .h injection model");
                    File.AppendAllText(file,"// next generation\n");
                    Check(!File.ReadAllText(Path.Combine(frozen,"color.hlsl")).Contains("next generation"),"editing pack files does not mutate a registered DX11 generation");
                    var held=(System.Collections.IList)host.GetField("Pending",Static).GetValue(null);
                    Check(((string[])held[0])[1]==frozen,"pending renderer selection retains the captured generation rather than mutable source paths");
                    Check((bool)registry.GetMethod("TryRegister",Static).Invoke(null,request),"DX11 replacement registration accepts changed source bytes");
                    string replacement=(string)pending["snapshot.fixture"].GetType().GetField("Root").GetValue(pending["snapshot.fixture"]);
                    Check(replacement!=frozen,"changed source bytes receive a distinct generation path");
                    Call(guest,"UnregisterRoot","snapshot.fixture");
                } finally {if(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true);}
            } else if(mode=="portable") {
                var native=Assembly.LoadFrom(Path.Combine(build,"Anomaly.Dx11.dll"));
                Call(core.GetType("ClientPlugin.Backend.Dx11Call"),"Bind",native);
                var owned=core.GetType("ClientPlugin.Shaders.OwnedPassRegistry");object received=null;
                var packApi=core.GetType("ClientPlugin.Shaders.ShaderPackRegistry");
                object capabilities=Call(packApi,"GetCapabilities");
                Check((bool)capabilities.GetType().GetProperty("PackRegistrationAvailable").GetValue(capabilities) && !(bool)capabilities.GetType().GetProperty("FullscreenExecutionSupported").GetValue(capabilities),"pending capabilities distinguish registration from execution support");
                Call(owned,"RegisterPortable","portable.fixture","AfterAtmosphere",0,0,(Action<object>)(context=>received=context),0);
                var host=core.GetType("ClientPlugin.Backend.RenderHost");host.GetField("committed",Static).SetValue(null,true);host.GetField("choice",Static).SetValue(null,0);
                Call(core.GetType("ClientPlugin.Backend.PortablePassRegistry"),"BindDx11",new object[]{null});
                capabilities=Call(packApi,"GetCapabilities");
                Check((bool)capabilities.GetType().GetProperty("PortableCommandsSupported").GetValue(capabilities),"DX11 capabilities identify the portable executor");
                var nativeOwned=native.GetType("ClientPlugin.Shaders.OwnedPassRegistry");
                var registrations=(System.Collections.IList)nativeOwned.GetField("Passes",Static).GetValue(null);
                Check(registrations.Count==1,"pending portable registration binds once to DX11");
                var slot=native.GetType("ClientPlugin.Shaders.OwnedPassSlot");var policy=native.GetType("ClientPlugin.Shaders.TemporalPolicy");
                var contextType=native.GetType("ClientPlugin.Shaders.OwnedPassContext");
                object context=Activator.CreateInstance(contextType,BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{Enum.Parse(slot,"AfterAtmosphere"),null,Enum.ToObject(policy,0),(object)false},null);
                var draw=(Delegate)registrations[0].GetType().GetField("Draw").GetValue(registrations[0]);draw.DynamicInvoke(context);
                Check(received!=null && received.GetType().FullName=="ClientPlugin.Shaders.PortablePassContext" && received.GetType().GetProperty("Rc")==null,"DX11 callback receives the device-free portable context");
                Check((string)received.GetType().GetProperty("Slot").GetValue(received)=="AfterAtmosphere","portable slot survives the DX11 adapter");
                bool expired=false;try{received.GetType().GetMethod("Copy").Invoke(received,new object[]{"a","b"});}catch(TargetInvocationException e){expired=e.InnerException is InvalidOperationException;}
                Check(expired,"DX11 portable context expires after callback");
                Call(owned,"Unregister","portable.fixture");Check(registrations.Count==0,"portable unregister removes the DX11 wrapper");
                host.GetField("choice",Static).SetValue(null,1);capabilities=Call(packApi,"GetCapabilities");
                Check(!(bool)capabilities.GetType().GetProperty("NamedStageExecutionSupported").GetValue(capabilities) && !(bool)capabilities.GetType().GetProperty("SameDrawAttachmentsSupported").GetValue(capabilities),"Vulkan capabilities do not advertise unimplemented named-stage activation or same-draw attachments");
                host.GetField("choice",Static).SetValue(null,0);
            } else if (mode == "lookup") {
                string scratch = Path.Combine(Path.GetTempPath(), "SsgiLookup-" + Guid.NewGuid().ToString("N"));
                string edition = Path.Combine(scratch, tfm == "net48" ? "Legacy" : "Interim");
                string path = Path.Combine(edition, "DevFolder", "StarCpt-Prism.SSGI-fixture", "Bin", "plugin.dll");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                Directory.CreateDirectory(Path.Combine(edition, "Sources"));
                File.WriteAllText(Path.Combine(edition, "Sources", "sources.xml"), "<SourcesConfig><LocalPlugin><Folder>T:\\Cursor Projects\\Prism.SSGI</Folder><File>plugin.xml</File></LocalPlugin></SourcesConfig>");
                try {
                using (var module = ModuleDefinition.ReadModule(Path.Combine(@"T:\Cursor Projects\Prism.SSGI\ClientPlugin\bin\Debug", tfm, "Prism.SSGI.dll"), new ReaderParameters { AssemblyResolver = resolver })) {
                    module.Assembly.Name.Name = "SSGI.PulsarDx11Regression";
                    module.Write(path);
                }
                var caller = Assembly.LoadFrom(path);
                string found = (string)Call(core.GetType("ClientPlugin.Backend.SatelliteLookup"), "Find", "Prism.SSGI.Dx11.dll", caller);
                Check(File.Exists(found) && found.Contains(@"Prism.SSGI\ClientPlugin\bin"), "source-mode SSGI companion lookup: " + found);
                var loader = caller.GetType("ClientPlugin.SatelliteAssembly");
                var companion = (Assembly)loader.GetMethod("Load", Static).Invoke(null, new object[] { found, caller });
                Check(companion.GetReferencedAssemblies().Any(a => a.FullName == caller.FullName), "SSGI companion references the live Pulsar core identity");
                Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Prism.SSGI"), "source-mode lookup/load creates no second settings core");
                var lookup = core.GetType("ClientPlugin.Backend.SatelliteLookup");
                Check((bool)Call(lookup, "IdMatches", @"T:\Cursor Projects\SE-DLSS\SpaceEngineersDLSS.xml", "B6469FEE-83BF-44A9-93F9-B4A252527C35-fixture"), "GUID lookup remains compatible with working DLSS");
                Check(!(bool)Call(lookup, "IdMatches", @"T:\Cursor Projects\Prism.SSGI\plugin.xml", "StarCpt-Prism.SSGIExtra-fixture"), "lookup rejects a different pack with a common prefix");
                } finally {
                    // The sole deletion target is the unique test directory we created.
                    if (Path.GetFullPath(scratch).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                        try { Directory.Delete(scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            } else {
                var host = core.GetType("ClientPlugin.Backend.RenderHost");
                var facade = core.GetType("ClientPlugin.Buffers.BufferCatalog");
                int resolution = 0, end = 0;
                // Registration during LoadAssets must survive the pending route.
                if (mode != "baseline") Call(facade, "RegisterLifetime", "probe", (Action)(() => resolution++), (Action)(() => end++));
                var native = Assembly.LoadFrom(Path.Combine(build, "Anomaly.Dx11.dll"));
                Call(core.GetType("ClientPlugin.Backend.Dx11Call"), "Bind", native);
                host.GetField("committed", Static).SetValue(null, true);
                host.GetField("choice", Static).SetValue(null, 0);
                Call(facade, "ApplyDeferred");
                var ssgi = Assembly.LoadFrom(Path.Combine(@"T:\Cursor Projects\Prism.SSGI\ClientPlugin\bin\Debug", tfm, "Prism.SSGI.dll"));
                Call(ssgi.GetType("ClientPlugin.SSGI.SsgiGuest"), "DisableForDx11");
                var enabled = (System.Collections.IDictionary)native.GetType("ClientPlugin.Shaders.FullscreenPassRegistry").GetField("PackEnabledById", Static).GetValue(null);
                foreach (string id in new[] { "temporal", "moments", "composite", "prevDepth", "prevNormal", "atrous0", "atrous1", "atrous2", "atrous3" })
                    Check(enabled["prism.ssgi." + id] is false, "DX11 suppresses duplicate guest stage " + id);
                Check(!enabled.Contains("prism.ssgi.trace"), "DX11 suppression preserves the shared trace pass");
                using (var device = new Device(DriverType.Hardware, DeviceCreationFlags.None, FeatureLevel.Level_11_0))
                using (var device1 = device.QueryInterface<Device1>()) {
                    keen.GetType("VRageRender.MyRender11").GetProperty("DeviceInstance", Static).SetValue(null, device1);
                    var published = core.GetType("ClientPlugin.Buffers.PublishedBuffer");
                    object buffer = Activator.CreateInstance(published);
                    var upload = published.GetMethod("PublishRgba8");
                    upload.Invoke(buffer, new object[] { new byte[] { 32, 96, 160, 255 }, 1, 1 });
                    Check((bool)Call(facade, "Publish", "probe", "probe.color", buffer), "core forwards uploaded image into DX11 catalog");
                    object active = Call(facade, "Active", "probe.color");
                    object bindable = active.GetType().GetProperty("Srv").GetValue(active);
                    Check(keen.GetType("VRage.Render11.Resources.ISrvBindable").IsInstanceOfType(bindable), "uploaded image implements Keen ISrvBindable (fullscreen t7/t8 contract)");
                    var srv = (ShaderResourceView)bindable.GetType().GetProperty("Srv").GetValue(bindable);
                    ReadPixel(device, srv);
                    var borrowed = Activator.CreateInstance(active.GetType());
                    active.GetType().GetMethod("Publish").Invoke(borrowed, new object[] { bindable, active.GetType().GetProperty("NativeResource").GetValue(active), 1, 1 });
                    active.GetType().GetMethod("Clear").Invoke(borrowed, null);
                    Check(!srv.IsDisposed, "clearing borrowed resources preserves their owner's SRV");
                    upload.Invoke(buffer, new object[] { new byte[] { 1, 2, 3, 255 }, 1, 1 });
                    Check(srv.IsDisposed, "replacing uploaded pixels retires the old owned SRV");
                    active = Call(facade, "Active", "probe.color");
                    bindable = active.GetType().GetProperty("Srv").GetValue(active);
                    srv = (ShaderResourceView)bindable.GetType().GetProperty("Srv").GetValue(bindable);
                    if (mode != "baseline") {
                        var lifetime = native.GetType("ClientPlugin.Buffers.BufferCatalogLifetime");
                        Call(lifetime, "NotifyResolutionChanged");
                        Check(resolution == 1 && !srv.IsDisposed, "pending lifetime callback reaches DX11; DRS preserves fixed upload");
                        Call(lifetime, "NotifyDeviceEnd");
                        Check(end == 1, "device-end reaches the pack callback");
                        Call(facade, "UnregisterLifetime", "probe");
                        Call(lifetime, "NotifyResolutionChanged");
                        Check(resolution == 1, "unregistered pack lifetime is no longer called");
                    }
                    Call(facade, "Unpublish", "probe", "probe.color");
                    Check(srv.IsDisposed, "unpublish releases the host-owned upload");
                }
            }
            Console.WriteLine("PASS: " + checks + " DX11 pack compatibility checks; " + Environment.Version);
            return 0;
        } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    static void ReadPixel(Device device, ShaderResourceView srv)
    {
        const string source = "Texture2D<float4> Image : register(t7); RWStructuredBuffer<float4> Result : register(u0); [numthreads(1,1,1)] void main(){Result[0]=Image.Load(int3(0,0,0));}";
        using (var code = ShaderBytecode.Compile(source, "main", "cs_5_0"))
        using (var shader = new ComputeShader(device, code))
        using (var output = new SharpDX.Direct3D11.Buffer(device, new BufferDescription(16, ResourceUsage.Default, BindFlags.UnorderedAccess, CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, 16)))
        using (var uav = new UnorderedAccessView(device, output))
        using (var staging = new SharpDX.Direct3D11.Buffer(device, new BufferDescription(16, ResourceUsage.Staging, BindFlags.None, CpuAccessFlags.Read, ResourceOptionFlags.None, 0))) {
            var rc = device.ImmediateContext;
            rc.ComputeShader.Set(shader); rc.ComputeShader.SetShaderResource(7, srv); rc.ComputeShader.SetUnorderedAccessView(0, uav); rc.Dispatch(1, 1, 1);
            rc.ComputeShader.SetShaderResource(7, null); rc.ComputeShader.SetUnorderedAccessView(0, null);
            rc.CopyResource(output, staging);
            SharpDX.DataStream stream;
            rc.MapSubresource(staging, MapMode.Read, MapFlags.None, out stream);
            try { var pixel = stream.ReadRange<float>(4); Check(Math.Abs(pixel[0] - 32f / 255f) < 0.0001f && Math.Abs(pixel[1] - 96f / 255f) < 0.0001f && Math.Abs(pixel[2] - 160f / 255f) < 0.0001f && pixel[3] == 1f, "hardware shader samples uploaded RGBA through t7"); }
            finally { rc.UnmapSubresource(staging, 0); stream.Dispose(); }
        }
    }
}
