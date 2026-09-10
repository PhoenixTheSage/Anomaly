# Terminal config persistence

Rich HUD sliders fire `ControlChanged` on the HUD thread, often **every mouse move**. A pack that serializes XML and writes a `.cfg` in that setter can stall Present long enough for a TDR. Space Engineers then reports `DXGI_ERROR_DEVICE_REMOVED` (`0x887A0005`); `GetDeviceRemovedReason` is often `DEVICE_HUNG` (`0x887A0006`). That Present failure is the wrapper — see `.cursor/rules/se-device-removed.mdc`.

Live uniforms (`FullscreenPassRegistry.SetUniforms` / `SetEnabled`) may update on the next owned-pass tick. **Disk I/O must not.**

Master’s `TerminalValue<T>.Update` also assigns `CustomValueGetter()` every `HandleInput` tick. On float sliders the min/max percent round-trip makes `Value != lastValue` after mouse-up, so `ControlChanged` (and `ValueText` → `SetText`) keeps firing until Present hangs. **Do not attach `CustomValueGetter` to any host control.** Dropdown getters return client `EntryData<T>` while Master’s `TValue` is `ListBoxEntry<T>` — if Pulsar .NET 10 binds that `Func`, `Update` `InvalidCastException`s every tick and Master’s `ExceptionHandler` closes the terminal. Checkboxes and color pickers use CoreLib/`VRageMath` `Func`s that *do* bind, so Master would call into the plugin ALC every tick while the page is visible. Host pages are push-only (`ControlChanged` + `Refresh()`). Pulsar MyGui has no getter round-trip.

## Host (this repo)

`ClientPlugin.Settings.ConfigStorage`:

- One cached `XmlSerializer(typeof(Config))`. Do not `new XmlSerializer` per save.
- `Save` only marks the instance dirty and records `Environment.TickCount`.
- `FlushPending()` waits ~400 ms of quiet, then serializes on a worker. Call it from `Plugin.Update` (cheap; do not `File.CreateText` there).
- `FlushPending(true)` waits for an in-flight worker, then writes immediately. Call it from `Plugin.Dispose`.

`TerminalConfigRegistry` writes Anomaly.cfg only for reserved host pages (`Anomaly` folder → `Settings` / `Velocity Debug`). `RequestPage` / `RequestFolderPage` pack controls persist nothing for Anomaly. The pack setter owns the pack file. Host `Slider` / `IntSlider` / `Checkbox` / `Dropdown` / `Color` never set `CustomValueGetter`. After a dropdown on that page changes, Anomaly pulls sibling controls from their getters once so preset widgets catch up without a per-tick getter. Packs that apply several fields from a button can call `Refresh()`.

## Packs

Resolve `TerminalConfigRegistry` by name. In the setter, assign the field and mark **your** config dirty. Flush from your `Plugin.Update` / `Dispose`.

```csharp
// BAD — HUD thread, every slider tick
(Action<float>)(v =>
{
    Config.Current.Intensity = v;
    using (var text = File.CreateText(path))
        new XmlSerializer(typeof(Config)).Serialize(text, Config.Current);
});

// GOOD — apply now, write later off the game thread
(Action<float>)(v => Config.Current.Intensity = v);
ConfigStorage.Save(Config.Current);       // dirty flag
// Plugin.Update: ConfigStorage.FlushPending();          // worker after 400 ms quiet
// Plugin.Dispose: ConfigStorage.FlushPending(true);     // wait + sync
```

Do not vendor a second Rich HUD client. Do not list Master as a Pulsar `DependencyId`. Player-facing layout: [wiki/Terminal-config.md](../wiki/Terminal-config.md).
