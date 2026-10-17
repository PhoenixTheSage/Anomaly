using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using VRageMath;

namespace ClientPlugin.Shaders;

/// <summary>
/// Well-known type. Resolve by name:
/// <c>ClientPlugin.Shaders.LocalCharacter</c>. Game-thread only for
/// <see cref="UpdateFromGameThread"/>. Local player render actor id and
/// camera-relative AABB for light-space mesh maps. Fail closed: ActorId 0
/// (spectator / dead). Atlas stamps the skinned mesh; packs must not skip
/// GBuffer contact inside this box.
/// </summary>
public static class LocalCharacter
{
    static readonly object Gate = new();
    static Snapshot current;

    public readonly struct Snapshot
    {
        public readonly uint ActorId;
        public readonly bool IsValid;
        public readonly bool IsFirstPerson;
        public readonly Vector3 CamRelCenter;
        public readonly Vector3 CamRelHalf;

        public Snapshot(uint actorId, bool isValid, bool isFirstPerson,
            Vector3 camRelCenter, Vector3 camRelHalf)
        {
            ActorId = actorId;
            IsValid = isValid;
            IsFirstPerson = isFirstPerson;
            CamRelCenter = camRelCenter;
            CamRelHalf = camRelHalf;
        }
    }

    /// <summary>
    /// Controlled <c>MyCharacter</c> while alive (first-person, third-person,
    /// cockpit). Spectator / dead leave ActorId 0.
    /// </summary>
    public static void UpdateFromGameThread()
    {
        if (MySession.Static == null)
        {
            Clear();
            return;
        }

        var character = MySession.Static.ControlledEntity as MyCharacter;
        if (character == null || character.Closed || character.IsDead)
        {
            Clear();
            return;
        }

        var ids = character.Render?.RenderObjectIDs;
        uint actorId = 0;
        if (ids != null && ids.Length > 0)
            actorId = ids[0];
        if (actorId == 0)
        {
            Clear();
            return;
        }

        var aabb = character.PositionComp.WorldAABB;
        var cam = MySector.MainCamera != null ? MySector.MainCamera.Position : aabb.Center;
        var center = (Vector3)(aabb.Center - cam);
        var half = (Vector3)(aabb.Max - aabb.Min) * 0.5f + new Vector3(0.4f);
        lock (Gate)
            current = new Snapshot(actorId, true, character.IsInFirstPersonView, center, half);
    }

    /// <summary>
    /// Camera-relative AABB of the local suit. Fail closed when
    /// spectator / dead. Atlas stamps the skinned mesh; packs must not
    /// skip GBuffer contact inside this box (that removed the only TP umbra
    /// when the atlas was invalid).
    /// </summary>
    public static bool TryGetCamRelBox(out Vector3 center, out Vector3 half)
    {
        var snap = Copy();
        center = snap.CamRelCenter;
        half = snap.CamRelHalf;
        return snap.IsValid && snap.CamRelHalf.LengthSquared() > 1e-4f;
    }

    public static void Clear()
    {
        lock (Gate)
            current = default;
    }

    public static Snapshot Copy()
    {
        lock (Gate)
            return current;
    }
}
