using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;

internal static class Program
{
    static int checks;
    static string pluginDirectory, gameDirectory;
    const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    static void Check(bool value, string message)
    { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }

    static int Main(string[] args)
    {
        try
        {
            gameDirectory = args[1];
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) => {
                foreach (string dir in new[] { pluginDirectory, gameDirectory, AppDomain.CurrentDomain.BaseDirectory })
                {
                    if (dir == null) continue;
                    string path = Path.Combine(dir, new AssemblyName(e.Name).Name + ".dll");
                    if (File.Exists(path)) return Assembly.LoadFrom(path);
                }
                return null;
            };
            if (args[0] == "host") Host(args[2], args[3] == "sources", args[4]);
            else Consumers(args[0]);
            Console.WriteLine("Status navigation: " + checks + " checks passed.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    static void Consumers(string tfm)
    {
        var repos = new[] { "SE-DLSS", "SE-FRS", "SE-FG", "HdrRender", "Prism.SSGI", "se-aurora", "VolumetricClouds", "ScreenSpaceShadows", "FinalFrontier", "SE-Vulkan", "client-plugin-template" };
        foreach (string repo in repos)
        {
            pluginDirectory = Path.Combine(@"T:\Cursor Projects", repo, "ClientPlugin", "bin", "Debug", tfm);
            string project = Path.Combine(@"T:\Cursor Projects", repo, "ClientPlugin", "ClientPlugin.csproj");
            var document = System.Xml.Linq.XDocument.Load(project);
            string name = document.Descendants().First(e => e.Name.LocalName == "AssemblyName").Value;
            var assembly = Assembly.LoadFrom(Path.Combine(pluginDirectory, name + ".dll"));
            var helper = assembly.GetType("ClientPlugin.PluginStatusPage", true);
            var install = helper.GetMethod("TryInstall");
            Action resetTimer = () => helper.GetField("nextAttempt", Static).SetValue(null, DateTime.MinValue);
            Action show = () => ClientPlugin.RichHud.TerminalConfigRegistry.Shows++;
            Func<Action<object>, bool> run = extra => (bool)install.Invoke(null, new object[] { repo, show, extra });
            var pages = ClientPlugin.RichHud.TerminalConfigRegistry.Pages;
            pages.Clear();
            ClientPlugin.RichHud.TerminalConfigRegistry.DelayStatus = true;
            ClientPlugin.RichHud.TerminalConfigRegistry.RequestFolderPage(repo, "Settings");
            Check(!run(null) && pages.Count == 1, repo + ": Settings does not prevent a delayed Status retry");
            ClientPlugin.RichHud.TerminalConfigRegistry.DelayStatus = false; resetTimer();
            Check(!run(_ => throw new InvalidOperationException("fixture")) && pages.Count == 1,
                repo + ": failed population removes only the incomplete Status page");
            resetTimer();
            Check(run(null) && pages.Count == 2, repo + ": independent Status registration succeeds");
            Check(run(null) && pages[repo + "/Status"].Buttons.Count == 1 && pages[repo + "/Settings"].Buttons.Count == 0,
                repo + ": repeated updates keep one Status button on its own page");
            int before = ClientPlugin.RichHud.TerminalConfigRegistry.Shows;
            pages[repo + "/Status"].Buttons["Show Status"]();
            Check(ClientPlugin.RichHud.TerminalConfigRegistry.Shows == before + 1, repo + ": callback reaches its owner");
            foreach (int route in new[] { 0, 1, 2 })
            {
                ClientPlugin.Backend.RendererRoute.Active = route;
                string report = (string)helper.GetProperty("RendererLine").GetValue(null);
                Check(report.Contains(ClientPlugin.Backend.RendererRoute.ActiveName), repo + ": renderer header " + report);
                if (repo == "Prism.SSGI" && route != 1)
                {
                    var text = new System.Text.StringBuilder();
                    assembly.GetType("ClientPlugin.SSGI.SSGIStatus", true).GetMethod("AppendRuntime", Static).Invoke(null, new object[] { text });
                    Check(text.ToString().Contains(route == 2 ? "Vulkan" : "waiting") && !text.ToString().Contains("Denoiser ok"),
                        "Prism reports the guest/pending runtime without claiming DX11 denoiser readiness");
                }
            }
            Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == name + ".Dx11"), repo + ": status navigation never loads a companion");
        }
    }

    static void Host(string tfm, bool sources, string route)
    {
        pluginDirectory = Path.Combine(@"T:\Cursor Projects\Anomaly\ClientPlugin\bin\Debug", tfm);
        string original = Path.Combine(pluginDirectory, "Anomaly.dll"), corePath = original;
        string scratch = Path.Combine(Path.GetTempPath(), "AnomalyStatus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var files = Assembly.LoadFrom(Path.Combine(gameDirectory, "VRage.Library.dll")).GetType("VRage.FileSystem.MyFileSystem", true);
            files.GetMethod("Init").Invoke(null, new object[] { gameDirectory, scratch, "Mods", scratch, scratch });
            using (var resolver = new DefaultAssemblyResolver())
            {
                resolver.AddSearchDirectory(pluginDirectory); resolver.AddSearchDirectory(gameDirectory);
                using (var module = ModuleDefinition.ReadModule(original, new ReaderParameters { AssemblyResolver = resolver }))
                {
                Check(!module.AssemblyReferences.Any(a => a.Name == "VRage.Render11" || a.Name.StartsWith("SharpDX")), "shared RHUD host has no DX11 dependencies");
                Check(module.GetType("ClientPlugin.RichHud.TerminalConfigRegistry") != null && module.GetType("RichHudFramework.Client.RichHudClient") != null,
                    "registry and Rich HUD client live in the shared plugin");
                if (sources)
                {
                    module.Assembly.Name.Name = "Anomaly.PulsarStatusRegression";
                    corePath = Path.Combine(scratch, "plugin.dll"); module.Write(corePath);
                }
                }
            }
            var core = Assembly.LoadFrom(corePath);
            var registry = core.GetType("ClientPlugin.RichHud.TerminalConfigRegistry", true);
            var installer = core.GetType("ClientPlugin.RichHud.AnomalyTerminalPages", true).GetMethod("Install", Static);
            var host = core.GetType("ClientPlugin.Backend.RenderHost", true);
            Action<bool, int> setRoute = (decided, choice) => {
                host.GetField("committed", Static).SetValue(null, decided);
                host.GetField("choice", Static).SetValue(null, choice);
            };
            installer.Invoke(null, null);
            Check((int)registry.GetProperty("PageCount").GetValue(null) == 1, "pending startup already has its own Status page");
            string fallback = (string)core.GetType("ClientPlugin.Config", true).GetMethod("StatusFallback", Static).Invoke(null, new object[] { "fixture diagnostic failure" });
            Check(fallback.Contains("fixture diagnostic failure") && fallback.Contains("Renderer:"), "failed diagnostics retain a readable renderer report");
            var request = registry.GetMethod("RequestFolderPage", new[] { typeof(string), typeof(string) });
            Check(request.Invoke(null, new object[] { "Probe", "Status" }) != null, "consumer Status title is allowed inside its folder");
            Check(registry.GetMethod("RequestPage").Invoke(null, new object[] { "Status" }) == null, "framework Status is reserved only at root");
            if (route == "vulkan")
            {
                setRoute(true, 1); installer.Invoke(null, null); installer.Invoke(null, null);
                Check((int)registry.GetProperty("PageCount").GetValue(null) == 4, "Vulkan installs shared Settings, Velocity Debug and Status exactly once");
                Check(((Action)registry.GetField("saveHostConfig", Static).GetValue(null)).Method.DeclaringType.Assembly == core,
                    "Vulkan host controls keep persistence in the shared settings copy");
                Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Anomaly.Dx11"), "Vulkan Rich HUD never loads Anomaly.Dx11");
            }
            else
            {
                setRoute(true, 0); installer.Invoke(null, null);
                Check((int)registry.GetProperty("PageCount").GetValue(null) == 2, "DX11 Status exists before the device companion initializes");
                var loader = core.GetType("ClientPlugin.Backend.SatelliteAssembly", true);
                var native = (Assembly)loader.GetMethod("Load", Static).Invoke(null, new object[] { Path.Combine(pluginDirectory, "Anomaly.Dx11.dll"), core });
                Check(native.GetReferencedAssemblies().Any(a => a.FullName == core.FullName), "DX11 companion binds the live shared host identity");
                Check(native.GetType("ClientPlugin.RichHud.TerminalConfigRegistry", false) == null, "DX11 companion has no second terminal registry");
                native.GetType("ClientPlugin.RichHud.AnomalyTerminalPages", true).GetMethod("Install", Static).Invoke(null, null);
                Check((int)registry.GetProperty("PageCount").GetValue(null) == 4, "DX11 settings populate the same shared registry across the assembly boundary");
                Check(((Action)registry.GetField("saveHostConfig", Static).GetValue(null)).Method.DeclaringType.Assembly == native,
                    "DX11 host controls keep persistence in the native settings copy");
                Check(!sources || !AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Anomaly"), "source mode never loads a second disk core");
            }
        }
        finally
        {
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(scratch).StartsWith(temp, StringComparison.OrdinalIgnoreCase))
                try { Directory.Delete(scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

namespace ClientPlugin.Backend
{
    public static class RendererRoute
    {
        public static int Active;
        public static string ActiveName => Active == 1 ? "DX11" : Active == 2 ? "Vulkan" : "pending";
        public static bool Decided => Active != 0;
        public static bool UseDx11Device => Active == 1;
        public static bool UseGuestImages => Active == 2;
    }
}
namespace ClientPlugin.RichHud
{
    public static class TerminalConfigRegistry
    {
        public static readonly Dictionary<string, TestPage> Pages = new Dictionary<string, TestPage>();
        public static bool DelayStatus;
        public static int Shows;
        public static object RequestFolderPage(string folder, string title)
        {
            if (DelayStatus && title == "Status") return null;
            string key = folder + "/" + title;
            if (!Pages.TryGetValue(key, out var page)) Pages[key] = page = new TestPage();
            return page;
        }
        public static void UnregisterPage(string title) => Pages.Remove(title);
    }
    public sealed class TestPage
    {
        public readonly Dictionary<string, Action> Buttons = new Dictionary<string, Action>();
        public void Category(string title) { }
        public void Button(string title, Action action, string tip) => Buttons.Add(title, action);
    }
}
