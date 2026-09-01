using ClientPlugin.Buffers;

namespace ClientPlugin.Velocity;

/// <summary>
/// Process-wide velocity producer. Consumers resolve this type by well-known name
/// (reflection) — do not add a compile-time project reference to Anomaly.
/// </summary>
public static class VelocityRegistry
{
    public static IVelocityBuffer Active { get; private set; } = UnavailableVelocityBuffer.Instance;

    internal static void SetActive(IVelocityBuffer buffer)
    {
        Active = buffer ?? UnavailableVelocityBuffer.Instance;
        BufferCatalog.PublishVelocity(Active);
    }
}
