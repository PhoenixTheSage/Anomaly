using VRageMath;

namespace ClientPlugin.Velocity;

/// <summary>
/// Previous-world snapshot keyed by ActorID. Implementors only; consumers use
/// <see cref="IVelocityBuffer"/>.
/// </summary>
public interface IVelocityHistory
{
    int TrackedActorCount { get; }

    bool TryGetPrevious(uint actorId, out MatrixD world);

    bool WasTeleported(uint actorId);
}
