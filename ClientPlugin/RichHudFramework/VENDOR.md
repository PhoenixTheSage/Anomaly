# Vendored Rich HUD Framework client

Source: https://github.com/ZachHembree/RichHudFramework.Client  
Release: [1.3.0.0](https://github.com/ZachHembree/RichHudFramework.Client/releases/tag/1.3.0.0)  
Commit: `9eb595ec1f0d1f48a19fff39c68d4eef7ded2724`  
License: MIT (see `LICENSE`)

`Client/` and `Shared/` are the official install layout. Do not edit them except when syncing this pin or applying the Pulsar .NET 10 handshake patch below.

Local pin patch (`Client/RichHudClient.cs`): Master talks through `SendModMessage` and `is` checks on nested `VRage.MyTuple` types. On Pulsar Interim (net10.0 / plugin ALC) those closed generics can fail `is ServerData` / `is ExtendedClientData` even though the delegates are valid. The client duck-types the success payload, also sends flat `ClientData`, does not latch `regFail` on "already registered", and `Pulse()` retries every 2s. `RichHudSupport` calls `Init` again after `Close` so a Master reload does not require a world reload.

Also patched: `ListBoxData` reads `SelectionIndex` without a hard `(int)` cast (net10 can box a different integer). `ExceptionHandler.ExceptionReported` lets Anomaly copy the stack to `Anomaly.debug.log` before a reload. Host pages omit `CustomValueGetter` on every control (not only sliders) so Master does not invoke plugin `Func`s every tick while an Anomaly page is visible.

Runtime Master is the Steam Workshop mod [Rich HUD Master](https://steamcommunity.com/sharedfiles/filedetails/?id=1965654081) (`1965654081`). It is **optional**. Anomaly does not list it in Pulsar `DependencyIds`. Without Master, `RichHudClient.Registered` stays false and the Pulsar MyGui dialog remains the settings UI.

Handshake: `ClientPlugin.RichHud.RichHudSupport`. Init name / terminal root: **Anomaly Shaders**. Packs add pages through `ClientPlugin.RichHud.TerminalConfigRegistry` and corner status through `ClientPlugin.RichHud.HudOverlayRegistry` — do not vendor a second client in a pack plugin.

Resize events are not in upstream WindowBase. Anomaly does **not** edit Shared `WindowBase.cs`. Host HUD windows subclass `ClientPlugin.RichHud.ResizableWindow` and watch `resizeDir` after `HandleInput`. The shared terminal window is Master’s — `TerminalWindowMonitor` Harmony-postfixes Master’s `WindowBase.HandleInput` and `TerminalConfigRegistry` reflows tile columns from the live size.
