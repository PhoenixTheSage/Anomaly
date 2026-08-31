namespace ClientPlugin.Velocity;

/// <summary>
/// Previous-world snapshot keyed by ActorID. Implementors only; consumers use
/// <see cref="IVelocityBuffer"/>.
/// </summary>
public interface IVelocityHistory
{
    int TrackedActorCount { get; }
}
