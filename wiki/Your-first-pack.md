# Your first pack

A pack is a Pulsar plugin whose job is `LoadAssets` + `ShaderPackRegistry.Register`. Prefer hashed HLSL and no extra Harmony.

## 1. Plugin XML

Depend on Anomaly so enabling the pack auto-enables the framework. Instantiate order is the enabled-plugin list, not a topological sort — register on the static type, never `Plugin.Instance`.

```xml
<DependencyIds>
  <Id>A9C29274-E447-49EE-881B-C980E6D190FD</Id>
</DependencyIds>
<Asset Name="AnomalyPack" Path="pack.zip" Extract="true" />
```

## 2. Register without referencing Anomaly.dll

```csharp
public void LoadAssets(IReadOnlyDictionary<string, string> assets)
{
    if (!assets.TryGetValue("AnomalyPack", out var root))
        return;
    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
    {
        var t = assembly.GetType("ClientPlugin.Shaders.ShaderPackRegistry");
        t?.GetMethod("Register")?.Invoke(null, new object[] { "my.pack.id", root });
        break;
    }
}
```

If Anomaly is missing, `GetType` is null and the pack is inert. Do not set `Reference="true"` on the HLSL zip — that flag is for managed DLLs.

## 3. Pack folder

```
anomaly.json          required: id, name; optional priority, exclusive, defines, attachments, passes
Overlay/              replace Keen or Anomaly sources
  GBuffer/PixelStage.hlsli
Inject/               additive includes Anomaly concatenates
  GBuffer.hlsli
  Lighting.hlsli
  Atmosphere.hlsli
Fullscreen/           Anomaly-drawn programs (not Keen overlays)
  AfterAtmosphere/Curtain.hlsl
```

## 4. anomaly.json

```json
{
  "id": "example.gbuffer-tweaks",
  "name": "Example GBuffer tweaks",
  "priority": 0,
  "defines": ["ANOMALY_OBJECTID"]
}
```

> **Warning — Local drop is developer-only.** Unsigned experiments can live in Pulsar `Data/Anomaly/Packs` (each subdirectory or zip). That path is not for PluginHub and has no SHA-256 unless you hash it yourself.

## 5. Prove it

Load a world. Anomaly → Show Status: shader packs listed, Depth still compiles, `stages=` names any live overlays, `Fullscreen:` lists data-driven programs. A pack that breaks a sentinel is rolled back and named in the log as `pack=id`.

→ [[Overlay-vs-inject|Choose inject or overlay]] · [[Fullscreen-programs|Fullscreen programs]] · [[Troubleshooting|If Status looks wrong]]
