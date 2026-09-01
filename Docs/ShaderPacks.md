# Shader packs (Pulsar assets)

How third-party HLSL reaches Anomaly. Architecture layers: [ShaderAPI.md](ShaderAPI.md). Implementation order: [ROADMAP.md](ROADMAP.md). After velocity: [Extensibility.md](Extensibility.md) (stage-scoped inject, pack defines, attachment slots).

**Yes — use Pulsar named assets.** Do **not** invent a second unzip/hash pipeline, and do **not** assume Pulsar dumps every pack into one folder Anomaly can scan.

---

## What Pulsar actually does

From [SpaceGT/Pulsar](https://github.com/SpaceGT/Pulsar) (`Shared/Assets`, `Legacy/Loader/PluginInstance.cs`):

- Each plugin declares **named** assets in XML. Sources: repo path, dev folder, local plugin directory, or URL.
- Pulsar copies / extracts / hash-checks them **before** the plugin runs. `Extract="true"` unpacks zip/7z/etc. into a cache directory.
- Hub assets with `Url` **must** have `Sha256`. Missing hash logs a warning; mismatch throws and the plugin fails to load.
- `LoadAssets` is called at **instantiate** (before `Init`):

  | Method | When |
  |--------|------|
  | `LoadAssets(string)` | If a reserved asset named `AssetFolder` exists (old `<AssetFolder>` compat) |
  | `LoadAssets(IReadOnlyDictionary<string, string>)` | Always, when the plugin has any named assets. Values are **resolved filesystem paths** (folder or file). |

- `Reference="true"` adds managed DLLs as compiler references. **Do not** use this for HLSL packs.

`<DependencyIds>`: enabling a plugin **auto-enables its dependencies**. There is **no** in-game API that lists dependants’ asset maps. `PluginData.GetNamedAssets()` is Pulsar-internal. Anomaly cannot see another plugin’s dictionary unless that plugin **hands Anomaly the path**.

Instantiate order is the enabled-plugin list, **not** a topological sort of `DependencyIds`. Packs must register onto a **static** Anomaly type (assembly already loaded). Do not use `Plugin.Instance` from a pack’s `LoadAssets`.

---

## Mods vs plugins

| Kind | Can ship an Anomaly pack? |
|------|---------------------------|
| **Pulsar plugin** (PluginHub / Local / dev folder) | Yes. This is the supported path. |
| **Steam Workshop / `.sbc` mod** | No. Workshop content is not Pulsar `Asset` XML and is not SHA-256 gated the same way. |

“Shader mods” in this design are **Pulsar plugins** that depend on Anomaly. They can be `Hidden` so they do not clutter the plugin list (same pattern as `linux-compat`).

---

## Recommended model

```
Pack plugin XML
  DependencyIds → Anomaly GUID
  Asset Name="AnomalyPack" Path="pack.zip" Extract="true"   (or a folder)

Pack LoadAssets(dict)
  ShaderPackRegistry.Register(pluginId, dict["AnomalyPack"])  // well-known type name

Anomaly Init
  consume static registry → overlay include dirs + replace table
```

Anomaly’s **own** shaders stay Anomaly assets (`Shaders`). Packs never write into Anomaly’s folder.

### Pack plugin XML (Hub)

```xml
<Id>…pack guid…</Id>
<Hidden>true</Hidden>
<DependencyIds>
  <Id>A9C29274-E447-49EE-881B-C980E6D190FD</Id>
</DependencyIds>
<Asset Name="AnomalyPack" Path="pack.zip" Extract="true" />
```

For a URL blob on the hub:

```xml
<Asset Name="AnomalyPack" Url="https://…" Extract="true" Sha256="…" />
```

### Pack `LoadAssets` (no compile-time reference to Anomaly)

```csharp
public void LoadAssets(IReadOnlyDictionary<string, string> assets)
{
    if (!assets.TryGetValue("AnomalyPack", out var root))
        return;
    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
    {
        var t = assembly.GetType("ClientPlugin.Shaders.ShaderPackRegistry");
        t?.GetMethod("Register")?.Invoke(null, new object[] { "PackFriendlyId", root });
        break;
    }
}
```

If Anomaly is missing, `GetType` is null: pack is inert (dependency should have pulled Anomaly in).

### Anomaly XML (this repo)

Migrate off a single `<AssetFolder>` when implementing packs:

```xml
<Asset Name="AssetFolder" Path="Assets" />
<Asset Name="Shaders" Path="Assets/Shaders" />
```

Keep `LoadAssets(string)` for the reserved `AssetFolder` name **and** add `LoadAssets(IReadOnlyDictionary<string, string>)`. Use `assets["Shaders"]` as the first include directory for the compile hook.

Implemented: `ClientPlugin.Shaders.ShaderPackRegistry.Register`. Local drop is `{GetConfigPath("Anomaly")}/Packs` (developer-only). Overlay conflicts fail closed. Inject files concatenate into `Anomaly/Extras/<Stage>.hlsli` (`GBufferExtras.hlsli` aliases GBuffer; Lighting extras are included from `Lighting/Light.hlsli`). Pack `defines` merge onto GBuffer and lighting compiles. `GBufferAttachments.Request` (or json `attachments`) allocates extra GBuffer targets. `ShaderBindRegistry.RequestSrv` binds catalog textures on lighting/post. `BufferCatalog.Active("velocity")` aliases `VelocityRegistry`. Pack fingerprints are `#include`d from `Anomaly.hlsli` so Keen’s preprocess cache misses when packs change.

---

## Pack layout (on disk after Pulsar extract)

Root = the path Pulsar put in `assets["AnomalyPack"]`.

```
anomaly.json                 # required: id, name, optional priority
Overlay/                     # optional: named stages and/or Keen-relative replacements
  GBuffer/PixelStage.hlsli   # → Geometry/Passes/GBuffer/PixelStage.hlsli
  Post.HBAO/CoarseAO.hlsl    # → Postprocess/HBAO/CoarseAO.hlsl
  Geometry/Materials/Standard/Pixel.hlsl   # escape hatch
Inject/                      # optional: snippets Anomaly #includes (additive)
  GBuffer.hlsli              # → Anomaly/Extras/GBuffer.hlsli (also unscoped *.hlsli)
  GBuffer/PsHelpers.hlsli    # same stage, concatenated
  Lighting.hlsli             # → Anomaly/Extras/Lighting.hlsli (included from Lighting/Light.hlsli wrap)
  Post.HBAO/Hint.hlsli
```

`anomaly.json` (v1, keep small):

```json
{
  "id": "example.gbuffer-tweaks",
  "name": "Example GBuffer tweaks",
  "priority": 0,
  "exclusive": ["GBuffer"],
  "defines": ["ANOMALY_OBJECTID"],
  "attachments": [
    { "name": "objectid", "format": "R32_UINT" }
  ]
}
```

- **Named stages** (`ClientPlugin.Shaders.ShaderStages`): put files under `Overlay/<Stage>/`. Basename or path suffix maps to the Keen (or Anomaly) file for that stage. Unknown files under a stage name are skipped. Stages: `GBuffer`, `Depth`, `Forward`, `Highlight`, `Transparent`, `TransparentForDecals`, `Lighting.Dir` / `.Point` / `.Spot`, `Post.Tonemap` / `.HBAO` / `.SSAO` / `.Bloom` / `.FXAA` / `.EyeAdaptation` / `.Luminance` / `.ChromaticAberration`, `Anomaly.CameraVelocity` / `.LinearDepth` / `.HistoryColor`, `Shadows`, `Atmosphere`, `Decals`, `GPUParticles`, `EnvProbe`, `Foliage`.
- **Overlay** files replace Keen sources at the same relative path (layer 2) when the path is not a named stage. One owner per path; on conflict log both pack ids and **fail closed** (Keen/Anomaly default kept).
- Overlay of Anomaly-owned GBuffer **write** stages (`Geometry/Passes/GBuffer/VertexStage.hlsli`, `Geometry/Passes/GBuffer/PixelStage.hlsli`, `GBuffer/GBufferWrite.hlsli`) is rejected unless `exclusive` contains `"GBuffer"`. That claim opts out of Anomaly velocity extras for those files.
- Overlay of Anomaly **read wraps** (`GBuffer/GBuffer.hlsli`, `GBuffer/Surface.hlsli`) needs `exclusive: ["GBuffer"]` **or** `["Lighting"]` so a lighting pack does not have to kill velocity writes.
- Overlay of `Lighting/Light.hlsli` needs `exclusive: ["Lighting"]` (not `Lighting.Dir` / `.Point` / `.Spot`). That wrap is Keen-relative (`Overlay/Lighting/Light.hlsli`); `Lighting` is inject-only as a folder name.
- **Inject** files are additive includes (layer 1). Stage-scoped: `Inject/GBuffer.hlsli` or `Inject/GBuffer/*.hlsli` concatenates into `Anomaly/Extras/GBuffer.hlsli`. `Inject/Lighting.hlsli` is included from the Light wrap (`ANOMALY_LIGHTING_STAGE`). Unscoped `Inject/*.hlsli` still goes to GBuffer (v1). Unknown folders fail closed. Depth inject is rejected. Pixel-only helpers wrap in `#ifdef ANOMALY_PIXEL_STAGE` (GBuffer PS defines it; VS does not).
- **Defines** (`anomaly.json` `"defines": ["ANOMALY_OBJECTID"]`) are merged onto GBuffer permutations and lighting / OIT-resolve compiles (never Depth). Reserved: `ANOMALY`, `ANOMALY_VELOCITY`, `RENDERING_PASS`, `DEPTH_ONLY`, `CUSTOM_DEPTH`, `ANOMALY_ATTACH_*`.
- **Attachments** (`GBufferAttachments.Request` or `"attachments"` in json): named extra MRT (`R32_UINT`, `R16G16_Float`, …) or packed `GBuffer1.a`. Velocity keeps `SV_Target3`. Same name + format shares the slot; mismatched formats fail closed. Lighting samples extras as `AnomalyAttach_<name>` at t6+ (`ANOMALY_ATTACH_<NAME>_SRV`).
- **Binds** (`ClientPlugin.Shaders.ShaderBindRegistry.RequestSrv(stage, catalogName)`): Anomaly binds catalog textures at reserved slots (Lighting/post t5 = `"velocity"`). Packs do not Harmony-patch lighting. Unbind after each pass.
- **Catalog** (`ClientPlugin.Buffers.BufferCatalog.Active("velocity"|"linearDepth"|"hiZ"|"historyColor")`) aliases `VelocityRegistry` for velocity. Live GBuffer attachments also resolve by name.
- Missing Overlay file → Keen original (Iris-style fallback).

Do not allow packs to replace Depth with a fourth MRT. After apply, Anomaly compiles a sentinel for each live named stage and rolls back the offending pack. Every compile error logs `pack=<id>`.

---

## Optional local drop folder (not Hub)

For unsigned local experiments only, Anomaly may also scan:

`Plugin.GetConfigPath("Anomaly")` then `Packs\` → `{PulsarDir}/Data/Anomaly/Packs/` (Pulsar injects `GetConfigPath` as a static `Func<string, string, string>` on the plugin type: `(name, extension)`).

Each subdirectory or `.zip` is treated like `AnomalyPack`. **Not** for PluginHub. No SHA-256 unless we hash ourselves. Document it as developer-only so players are not told to unzip random packs there.

---

## What not to do

| Idea | Why not |
|------|---------|
| Anomaly enumerates Pulsar’s plugin list and reads other plugins’ caches | Couples to Pulsar internals; breaks when cache layout changes. |
| One world-writable `Content/Shaders` overlay | Fights Keen’s cache, no hub hash, path traversal. |
| `Reference="true"` on a shader zip | That flag is for **managed DLLs** at compile time. |
| Steam Workshop folders as packs | Different loader, no `DependencyIds`, no asset SHA-256. |
| Pack Harmony-patches `MyShader` | Two compile hooks; Anomaly owns the intercept. |

---

## Security

PluginHub already requires hashed, reviewed assets. A pack plugin is still **full-trust C#** if it ships scripts — prefer **Hidden plugins whose only job is `LoadAssets` + Register**, with HLSL in the hashed zip and **no** extra Harmony.

HLSL itself can still DoS the compile (infinite macros) or break Depth. Anomaly:

- Restricts Overlay paths to `Content/Shaders`-relative, no `..`
- Maps named stages (`Overlay/GBuffer/…`) to a fixed Keen path table; unknown files under a stage name fail closed
- Maps `Inject/<Stage>/` the same way; unknown inject folders fail closed; Depth inject is rejected
- Merges pack `defines` onto GBuffer only; reserved Keen/Anomaly macros are rejected
- Allocates extra GBuffer attachments (`GBufferAttachments.Request`); velocity keeps `SV_Target3`; Depth never sees extra targets
- Compiles a sentinel for each live named stage after applying packs; rolls back that pack on failure
- Logs `pack=<id>` on every compile error; in-game overlay failures roll back that pack
- Rejects Overlay of Anomaly-owned GBuffer write stages unless `exclusive: ["GBuffer"]`
- Rejects Overlay of GBuffer read wraps unless `exclusive: ["GBuffer"]` or `["Lighting"]`
- Rejects Overlay of `Lighting/Light.hlsli` unless `exclusive: ["Lighting"]`

---

## Timing vs velocity work

Pack loading is **slice F** ([ROADMAP.md](ROADMAP.md)): after the compile intercept exists. Slice A only needs Anomaly’s own `Shaders` include dir. Do not block camera velocity or GBuffer piggyback on third-party packs.
