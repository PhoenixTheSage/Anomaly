extern alias AnomalyShared;

// Device settings remain private to the device assembly. Only the renderer-neutral
// HUD services are imported from the shared core, avoiding duplicate Config types.
global using TerminalConfigRegistry = AnomalyShared::ClientPlugin.RichHud.TerminalConfigRegistry;
global using HudOverlayRegistry = AnomalyShared::ClientPlugin.RichHud.HudOverlayRegistry;
global using InteractiveOverlayRegistry = AnomalyShared::ClientPlugin.RichHud.InteractiveOverlayRegistry;
global using RichHudSupport = AnomalyShared::ClientPlugin.RichHud.RichHudSupport;
global using DebugLog = AnomalyShared::ClientPlugin.DebugLog;
