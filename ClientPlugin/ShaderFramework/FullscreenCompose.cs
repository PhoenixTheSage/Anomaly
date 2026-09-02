namespace ClientPlugin.Shaders;

/// <summary>
/// How Anomaly merges a data-driven fullscreen program into the slot target.
/// Resolve by name: <c>ClientPlugin.Shaders.FullscreenCompose</c>.
/// IsolatedAdd is the Overlay/Inject "inject" analog; Replace is exclusive.
/// </summary>
public enum FullscreenCompose
{
    IsolatedAdd = 0,
    IsolatedMix = 1,
    Chain = 2,
    PublishOnly = 3,
    Replace = 4,
    DirectAdd = 5
}
