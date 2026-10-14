namespace ClientPlugin.RichHud;

internal static class InteractiveOverlayInputPolicy
{
    // Master reports Full for our own AllKeys+CameraRot capture. It must not close the capturing window.
    internal static bool CanUse(bool sessionReady, bool gameplayFocus, bool ownGuardFocus, bool chat,
        bool terminal, bool fullInput, bool visible) => sessionReady && (gameplayFocus || ownGuardFocus) &&
        !chat && !terminal && (visible || !fullInput);
}
