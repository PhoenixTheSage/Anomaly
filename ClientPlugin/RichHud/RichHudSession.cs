using VRage.Game;
using VRage.Game.Components;

namespace ClientPlugin.RichHud;

/// <summary>
/// Session hook for optional Rich HUD Master. Safe when Master is absent.
/// </summary>
[MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
public sealed class RichHudSession : MySessionComponentBase
{
    public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
    {
        RichHudSupport.TryInitialize();
    }

    // Lobbies: pause does not tick BeforeSimulation in SP; Draw still runs.
    // F5 / quick-load can leave the client Registered without re-firing Init.
    public override void Draw()
    {
        RichHudSupport.TryInitialize();
        base.Draw();
    }

    protected override void UnloadData()
    {
        RichHudSupport.Shutdown();
    }
}
