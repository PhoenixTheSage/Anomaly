using System;

namespace ClientPlugin.ShaderFramework;

/// <summary>Undo partial shared output before replaying the full legacy interval, exactly once.</summary>
internal static class VolumeCompositeRecovery
{
    internal static Exception Execute(Action composite, Action restoreFrame, Action restoreLegacy,
        Action replayLegacy, Func<Exception, bool> isLostDevice)
    {
        try { composite(); return null; }
        catch (Exception error)
        {
            if (isLostDevice(error)) throw;
            // Ownership must be revoked even if restoring the GPU checkpoint fails.
            try { restoreFrame(); }
            finally { restoreLegacy(); }
            replayLegacy();
            return error;
        }
    }
}
