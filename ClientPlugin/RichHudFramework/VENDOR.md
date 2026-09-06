# Vendored Rich HUD Framework client

Source: https://github.com/ZachHembree/RichHudFramework.Client  
Release: [1.3.0.0](https://github.com/ZachHembree/RichHudFramework.Client/releases/tag/1.3.0.0)  
Commit: `9eb595ec1f0d1f48a19fff39c68d4eef7ded2724`  
License: MIT (see `LICENSE`)

`Client/` and `Shared/` are the official install layout. Do not edit them except when syncing this pin.

Runtime Master is the Steam Workshop mod [Rich HUD Master](https://steamcommunity.com/sharedfiles/filedetails/?id=1965654081) (`1965654081`). It is **optional**. Anomaly does not list it in Pulsar `DependencyIds`. Without Master, `RichHudClient.Registered` stays false and the Pulsar MyGui dialog remains the settings UI.

Handshake: `ClientPlugin.RichHud.RichHudSupport`.
