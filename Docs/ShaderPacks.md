# Shader packs (Pulsar assets)

How third-party HLSL reaches Anomaly. Architecture layers: [ShaderAPI.md](ShaderAPI.md). Implementation order: [ROADMAP.md](ROADMAP.md).

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

---

## Pack layout (on disk after Pulsar extract)

Root = the path Pulsar put in `assets["AnomalyPack"]`.

```
anomaly.json                 # required: id, name, optional priority
Overlay/                     # optional: Keen-relative replacements
  Geometry/Passes/GBuffer/PixelStage.hlsli
Inject/                      # optional: snippets Anomaly #includes
  GBufferExtras.hlsli
```

`anomaly.json` (v1, keep small):

```json
{
  "id": "example.gbuffer-tweaks",
  "name": "Example GBuffer tweaks",
  "priority": 0,
  "exclusive": ["GBuffer"]
}
```

- **Overlay** files replace Keen sources at the same relative path (layer 2). One owner per path; on conflict log both pack ids and keep the higher `priority` (or fail closed — pick one rule and stick to it; prefer **fail closed** on Hub).
- **Inject** files are additive includes (layer 1). Anomaly concatenates them into `Anomaly/GBufferExtras.hlsli`.
- Missing Overlay file → Keen original (Iris-style fallback).

Do not allow packs to replace Depth with a fourth MRT. Validate after compile.

---

## Optional local drop folder (not Hub)

For unsigned local experiments only, Anomaly may also scan:

`Plugin.GetConfigPath("Packs")` → `%AppData%/Pulsar/Data/Anomaly/Packs/` (Pulsar injects `GetConfigPath` as a static `Func<string, string, string>` on the plugin type).

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

HLSL itself can still DoS the compile (infinite macros) or break Depth. Anomaly should:

- Restrict Overlay paths to `Content/Shaders`-relative, no `..`
- Compile Depth after applying packs; roll back that pack on failure
- Log pack id on every compile error

---

## Timing vs velocity work

Pack loading is **slice F** ([ROADMAP.md](ROADMAP.md)): after the compile intercept exists. Slice A only needs Anomaly’s own `Shaders` include dir. Do not block camera velocity or GBuffer piggyback on third-party packs.
