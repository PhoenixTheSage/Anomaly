using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;

// Execute the built registry/prefix against installed game types. Only the
// successful GPU-draw certificate and frame clock are seeded; no copied policy.
static class Program
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static Type registry, temporal;
    static int checks;
    static object Call(Type type, string name, params object[] args) => type.GetMethod(name, All).Invoke(null, args);
    static void Put(Type type, string name, object value) => type.GetField(name, All).SetValue(null, value);
    static bool Suppressed => (bool)registry.GetProperty("SuppressNativeSunGlare", All).GetValue(null);
    static void Check(string name, bool value) { if (!value) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
    static void Frame(uint frame) => Put(temporal, "frameIndex", frame);
    static void DrawCertificate(object provider, uint frame) { Put(registry, "mainDrawnProvider", provider); Put(registry, "mainDrawnFrame", frame); }
#pragma warning disable SYSLIB0050
    static object Bare(Type type) => FormatterServices.GetUninitializedObject(type);
#pragma warning restore SYSLIB0050
    static int Main(string[] args)
    {
        string scratch = Path.Combine(Path.GetTempPath(), "Anomaly-glare-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(scratch, "Test.hlsl");
        var harmony = new Harmony("Anomaly.CelestialGlare.Tests");
        try
        {
            string gameBin = Path.GetFullPath(args[0]);
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
            {
                string file = Path.Combine(gameBin, new AssemblyName(e.Name).Name + ".dll");
                return File.Exists(file) ? Assembly.LoadFrom(file) : null;
            };
            Directory.CreateDirectory(scratch); File.WriteAllText(source, "// CPU registry test; never compiled.");
            var plugin = Assembly.LoadFrom(Path.GetFullPath(args[1]));
            registry = plugin.GetType("ClientPlugin.Shaders.CelestialBackgroundRegistry", true);
            temporal = plugin.GetType("ClientPlugin.Shaders.FrameTemporal", true);
            Check("register real celestial provider", (bool)Call(registry, "Register", "Test", source));
            Call(registry, "SetNativeSunGlare", "Test", false);
            var providers = (IDictionary)registry.GetField("Providers", All).GetValue(null);
            object provider = providers["Test"];
            Frame(100);
            Check("native glare remains until first successful main draw", !Suppressed);
            DrawCertificate(provider, 100);
            Check("same-frame successful draw owns solar glare", Suppressed);
            Frame(101);
            Check("worker before next main draw still suppresses solar glare", Suppressed);
            DrawCertificate(provider, 101);
            Check("worker after main draw also suppresses solar glare", Suppressed);
            Frame(103);
            Check("stale draw certificate restores native glare", !Suppressed);
            Frame(100);
            Check("future-frame certificate does not suppress", !Suppressed);
            DrawCertificate(provider, uint.MaxValue); Frame(0);
            Check("one-frame handoff handles frame counter wrap", Suppressed);
            DrawCertificate(provider, 100); Frame(101);
            Call(registry, "SetEnabled", "Test", false);
            Check("disabled provider restores native glare immediately", !Suppressed);
            Call(registry, "SetEnabled", "Test", true);
            provider.GetType().GetField("Failed", All).SetValue(provider, true);
            Check("failed provider restores native glare", !Suppressed);
            provider.GetType().GetField("Failed", All).SetValue(provider, false);
            Call(registry, "SetNativeSunGlare", "Test", true);
            Check("native-opt-in remains compatible for other tenants", !Suppressed);
            Call(registry, "SetNativeSunGlare", "Test", false);
            Call(registry, "Register", "Other", source);
            Check("conflicting celestial providers restore native glare", !Suppressed);
            Call(registry, "Unregister", "Other");
            Check("sole healthy provider recovers ownership", Suppressed);

            var prefixType = plugin.GetType("ClientPlugin.Patches.CelestialSunGlarePatch", true);
            var identity = plugin.GetType("ClientPlugin.Patches.CelestialSunIdentityPatch", true);
            var unload = plugin.GetType("ClientPlugin.Patches.CelestialSunUnloadPatch", true);
            var target = (MethodBase)Call(prefixType, "TargetMethod");
            Check("flare target resolves exact installed-game overload", target != null && target.GetParameters().Length == 3);
            harmony.CreateClassProcessor(prefixType).Patch();
            Check("solar flare prefix installs on installed renderer", Harmony.GetPatchInfo(target).Owners.Contains(harmony.Id));
            var game = Assembly.LoadFrom(Path.Combine(gameBin, "Sandbox.Game.dll"));
            var sector = game.GetType("Sandbox.Game.World.MySector", true);
            var sunField = sector.GetField("m_sunFlare", All);
            var sun = Bare(sunField.FieldType);
            sunField.FieldType.GetField("m_renderObjectID", All).SetValue(sun, 123u);
            sunField.SetValue(null, sun); Call(identity, "Postfix");
            Check("solar identity captures exact MySector light id", (uint)identity.GetField("SunId", All).GetValue(null) == 123u);

            Type lightType = target.GetParameters()[0].ParameterType;
            var light = Bare(lightType);
            var render = Assembly.LoadFrom(Path.Combine(gameBin, "VRage.Render.dll"));
            var actorType = render.GetType("VRage.Render.Scene.MyActor", true);
            var actor = Bare(actorType);
            var trackerField = actorType.GetField("m_ID", All);
            var tracker = Bare(trackerField.FieldType);
            trackerField.FieldType.GetField("m_ID", All).SetValue(tracker, 123u);
            trackerField.SetValue(actor, tracker);
            bool ownerAssigned = false;
            for (Type baseType = lightType; baseType != null; baseType = baseType.BaseType)
            {
                var ownerField = baseType.GetField("<Owner>k__BackingField", All);
                if (ownerField == null) continue;
                ownerField.SetValue(light, actor); ownerAssigned = true; break;
            }
            Check("test adapter seeds real actor ownership", ownerAssigned);
            Check("owned exact solar light is suppressed", !(bool)Call(prefixType, "Prefix", light));
            trackerField.FieldType.GetField("m_ID", All).SetValue(tracker, 124u);
            Check("other distant or ship light keeps native flare", (bool)Call(prefixType, "Prefix", light));
            Check("null light follows native handling", (bool)Call(prefixType, "Prefix", new object[] { null }));
            Call(unload, "Postfix");
            Check("sector unload clears solar identity", (uint)identity.GetField("SunId", All).GetValue(null) == uint.MaxValue);
            sunField.SetValue(null, null);
            Call(registry, "Release");
            Check("device release clears glare ownership", !Suppressed);
            DrawCertificate(provider, 101);
            var draw = registry.GetMethod("Draw", All);
            Type matrix = draw.GetParameters()[4].ParameterType;
            draw.Invoke(null, new object[] { null, null, null, false, Activator.CreateInstance(matrix), Activator.CreateInstance(matrix) });
            Check("unusable main draw restores native glare", !Suppressed);
            DrawCertificate(provider, 101); Call(registry, "Unregister", "Test");
            Check("unregistered provider restores native glare", !Suppressed);
            Console.WriteLine("PASS: " + checks + " real registry / solar glare checks");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            if (File.Exists(source)) File.Delete(source);
            if (Directory.Exists(scratch)) Directory.Delete(scratch); // only this empty owned directory
        }
    }
}
