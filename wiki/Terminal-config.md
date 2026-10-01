# Terminal config

When [Rich HUD Master](https://steamcommunity.com/sharedfiles/filedetails/?id=1965654081) is in the world, Anomaly mirrors its Pulsar settings under the main terminal overlay as **Anomaly Shaders → Anomaly**. Packs and consumer plugins add their own pages beside that folder. Master is optional — the Pulsar MyGui dialog still works without it. Do not vendor a second Rich HUD client in a pack.

```
Anomaly Shaders
  ├── Anomaly
  │     ├── Settings            Pulsar options
  │     └── Velocity Debug      Visualization / probe page
  ├── <your title>              One page per RequestPage(title)
  └── <your folder>             RequestFolderPage(folder, page)
        ├── Settings            Typical consumer page name
        ├── Status              Optional pack debug dialog (Show Status)
        └── <nested>            RequestFolderPage(page, folders)
              └── <page>        Types / deck pages under that folder
```

> **Note — Reserved titles fail closed on `RequestPage`.** `Anomaly`, `Settings`, and `Velocity Debug` belong to the framework. Pick your pack or plugin display name so it sits beside **Anomaly**. `RequestFolderPage` may use `Settings` as the page name inside a consumer folder. Folder titles cannot be reserved. The same title (or folder + page) is idempotent (append more controls). Two packs should not share a title.

## Register without referencing Anomaly.dll

Call `RequestPage` from `LoadAssets` or `Init` on the **static** type. Instantiate order is not `DependencyIds`-sorted. Do not use `Plugin.Instance`.

```csharp
public void LoadAssets(IReadOnlyDictionary<string, string> assets)
{
    Type registry = null;
    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
    {
        registry = assembly.GetType("ClientPlugin.RichHud.TerminalConfigRegistry");
        if (registry != null)
            break;
    }

    var page = registry?.GetMethod("RequestFolderPage", new[] { typeof(string), typeof(string) })
            ?.Invoke(null, new object[] { "My Shaders", "Settings" })
        ?? registry?.GetMethod("RequestPage")?.Invoke(null, new object[] { "My Shaders" });
    // Nested: RequestFolderPage(page, IReadOnlyList<string> folders)
    // e.g. ("Stratus", new[] { "My Shaders", "Low" })
    if (page == null)
        return;

    var t = page.GetType();
    t.GetMethod("Category")?.Invoke(page, new object[] { "Quality", "Runtime toggles" });
    t.GetMethod("Checkbox")?.Invoke(page, new object[]
    {
        "Enable bloom",
        (Func<bool>)(() => bloom),
        (Action<bool>)(v => { bloom = v; RequestSave(); }),
        "Soft bloom after lighting.",
    });
    t.GetMethod("Dropdown")?.Invoke(page, new object[]
    {
        "Quality",
        typeof(Quality),
        (Func<object>)(() => quality),
        (Action<object>)(v => { quality = (Quality)v; RequestSave(); }),
        "Preset for the fullscreen grade.",
    });
}
```

Use the non-generic `Dropdown(label, enumType, get, set, description)` from reflection. Assign your field in the setter and **mark the pack config dirty**. Do not `new XmlSerializer` or write a `.cfg` on that callback — Rich HUD sliders fire every mouse move on the HUD thread. Do not attach `CustomValueGetter` if you build Master controls yourself; Anomaly’s host pages omit it on every control (dropdown getters return client `EntryData` that cannot be Master’s `ListBoxEntry`; on Pulsar .NET 10 a bound getter closes the terminal). Flush the file from `Plugin.Update` / `Dispose` after the control is quiet (~400 ms); the write must run on a worker, not `File.CreateText` on Update. Anomaly writes `Anomaly.cfg` only for its reserved host pages, not for `RequestFolderPage`. Host contract: [TerminalConfig.md](../Docs/TerminalConfig.md).

If Anomaly is missing, `GetType` is null and the pack is inert. If Master is not in the world, the page stays queued until the handshake succeeds.

## HUD overlay

Well-known type: `ClientPlugin.RichHud.HudOverlayRegistry`. Corner text when Master is registered. Packs do not vendor a HUD client.

```csharp
var overlay = assembly.GetType("ClientPlugin.RichHud.HudOverlayRegistry");
overlay?.GetMethod("Register")?.Invoke(null, new object[]
{
    "my.pack",
    (Func<string>)(() => enabled ? "My pack  72 fps" : null),
});
```

| Call | Role |
|------|------|
| `Register(id, get)` | Add or replace one overlay line. Empty id fails closed. Null/empty getter hides that line for the frame. The host panel stays in the layout tree so a later non-empty getter can show again. |
| `Unregister(id)` | Drop a line. |
| `StatusLine` | `overlays=N (id, …)` |

The getter runs on the HUD draw thread. Return a cached string. Do not serialize or write a `.cfg` from it. Without Master the registration stays queued until the handshake.

## Builder

Well-known type: `ClientPlugin.RichHud.TerminalConfigRegistry`.

| Call | Role |
|------|------|
| `RequestPage(title)` | Create or return a sibling page on the **Anomaly Shaders** root. Empty / reserved titles return null (`LastError`). |
| `RequestFolderPage(folder, page)` | Create or return a page under a folder beside **Anomaly**. Folder titles cannot be reserved. Page title `Settings` is allowed. |
| `RequestFolderPage(page, folders)` | Same, with an arbitrary folder path (`IReadOnlyList<string>`). Each segment is a nested category. Empty / reserved folder names fail closed. |
| `UnregisterPage(title)` | Hide a pack page. Reserved titles fail closed. Rich HUD cannot remove a page from the tree. |
| `RegisterWindowResized(handler)` | `Action<float, float>` after a completed terminal resize (mouse-up). HUD input thread. Same handler is idempotent. |
| `UnregisterWindowResized(handler)` | Drop a global resize handler. |
| `WindowSize` / `Columns` | Live HUD-pixel size (including mid-drag) and the default **internal** column count. Zero size until the first `HandleInput` sample. |
| `RootName` | `"Anomaly Shaders"` |
| `FrameworkTitle` | `"Anomaly"` — folder that holds Settings and Velocity Debug |
| `StatusLine` | `pages=N (Settings, Velocity Debug, …) window=WxH cols=N` |

`ITerminalConfigPage`: `Category`, `Label` (`text` plus optional `Func<string> get` — applied at mount and again on `Refresh()`), `Checkbox` (optional `Func<bool> enabled` — visible but not settable when false), `Slider`, `IntSlider`, `Dropdown` / `Dropdown<T>`, `Button`, `Color` (`Func<Color>` / `Action<Color>`, Rich HUD RGB picker), `Refresh()`, `OnResized(Action<float, float>)`. Optional arguments are explicit overloads so reflection can match arity. Each category is one full-width tile. Controls flow left-to-right into **internal** columns sized around a preferred width (~300px) from the live window. Call `SeparateAt(width)` after `Category` to change that wrap width. `Columns(n)` forces a count (`1` is one full-width column). The next category starts below. Master’s page scrolls when the stack is taller than the window. Color pickers take a full row inside the tile. Prefer a short category header; put detail in control tooltips.

Host pages never set `CustomValueGetter`. After a dropdown setter returns, Anomaly pulls sibling controls from their getters once (preset dropdowns update the displayed values). `Refresh()` also pushes labels, dropdown selections, sliders, and checkboxes. A button that writes several fields should call `Refresh()` the same way. Packs that show live status (planet km, pass state) must call `Refresh()` from `Plugin.Update` when the string changes — mount happens at handshake, often before a session planet exists.

Master has no resize event. Anomaly Harmony-monitors `resizeDir` in Master’s `WindowBase.HandleInput`. Tile wrap is automatic for every `RequestPage` / `RequestFolderPage` page. Packs that need extra layout (wrapping labels, hiding chrome) can subscribe without referencing Anomaly.dll:

```csharp
registry.GetMethod("RegisterWindowResized")?.Invoke(null, new object[]
{
    (Action<float, float>)((w, h) => { /* HUD input thread — do not serialize */ }),
});
// or per page:
t.GetMethod("OnResized")?.Invoke(page, new object[]
{
    (Action<float, float>)((w, h) => { }),
});
```

`WindowSize` is the live terminal size, including while the edge is dragged. `Columns` is how many preferred-width **internal** control columns fit a section tile unless a section calls `Columns` or `SeparateAt`. Do not write a `.cfg` from those callbacks. Anomaly-owned HUD windows subclass `ClientPlugin.RichHud.ResizableWindow` instead of `WindowBase`.

## Do not

- Do not take a compile-time reference to Anomaly or RichHudFramework.
- Do not ship another Rich HUD client, or list Master as a Pulsar `DependencyId`, from a pack.
- Do not request `Anomaly`, `Settings`, or `Velocity Debug` from `RequestPage`. `RequestFolderPage` may use `Settings` as the page name.
- Do not hide the pack from Pulsar to “keep the list clean.” Packs are normal plugins that depend on Anomaly.
- Do not serialize XML or write a config file inside a slider / color / checkbox setter. Cache one `XmlSerializer`, debounce, and flush on a worker.
- Do not serialize or write a `.cfg` from `OnResized` / `RegisterWindowResized`. Those run on the HUD input thread after mouse-up.

→ [[Your-first-pack|First pack]] · [[Compatibility]] · [[Troubleshooting]]
