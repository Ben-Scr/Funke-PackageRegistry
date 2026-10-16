using Funke.Networking.Internal;

namespace Funke.Networking;

public enum NetAuthority
{
    /// <summary>The server moves the object; clients follow.</summary>
    Server,
    /// <summary>The owning client moves the object (player characters); the server relays it to the others.</summary>
    Owner,
}

/// <summary>Shared part of <see cref="NetworkTransform2D"/> and <see cref="NetworkTransform3D"/>.</summary>
public abstract class NetworkTransformBase : EntityScript
{
    [ToolTip("Who moves this object: the server, or the client that owns it.")]
    public NetAuthority Authority = NetAuthority.Server;

    public bool SyncPosition = true;
    public bool SyncRotation = true;
    public bool SyncScale;

    [ToolTip("Seconds the copies on other peers lag behind, so there are always two states to blend. Lower is snappier, higher hides more jitter and loss.")]
    [ClampValue(0f, 1f)]
    public float InterpolationDelay = 0.1f;

    [ToolTip("Jumps further than this are not blended (respawns, teleports). 0 blends everything.")]
    public float TeleportDistance;

    internal readonly SnapshotBuffer Snapshots = new();
    internal TransformState LastSent;
    internal double LastSentTime = double.NegativeInfinity;
    internal bool HasSent;

    internal abstract bool Is3D { get; }
    internal abstract TransformState Read();
    internal abstract void Apply(in TransformState state);

    /// <summary>True on the peer that moves this object right now.</summary>
    public bool HasAuthority => NetworkObjects.HasAuthority(this);

    /// <summary>Snaps the copies on other peers instead of blending the next move (after a respawn).</summary>
    public void Teleport() => NetworkObjects.Teleport(this);

    public override void OnAwake() => NetworkObjects.AttachTransform(this);
    public override void OnDestroy() => NetworkObjects.DetachTransform(this);
}

/// <summary>Sends the entity's Transform2D to the other peers and blends their copies smoothly.</summary>
public class NetworkTransform2D : NetworkTransformBase
{
    internal override bool Is3D => false;

    internal override TransformState Read()
    {
        Transform2D? transform = Entity.Transform;
        if (transform == null) return LastSent;
        Vector2 position = transform.Position;
        Vector2 scale = transform.Scale;
        return new TransformState
        {
            Position = new Vector3(position.X, position.Y, 0),
            Rotation = new Vector3(0, 0, transform.Rotation),
            Scale = new Vector3(scale.X, scale.Y, 1),
        };
    }

    internal override void Apply(in TransformState state)
    {
        Transform2D? transform = Entity.Transform;
        if (transform == null) return;
        if (SyncPosition) transform.Position = new Vector2(state.Position.X, state.Position.Y);
        if (SyncRotation) transform.Rotation = state.Rotation.Z;
        if (SyncScale) transform.Scale = new Vector2(state.Scale.X, state.Scale.Y);
    }
}

/// <summary>Sends the entity's Transform3D to the other peers and blends their copies smoothly.</summary>
public class NetworkTransform3D : NetworkTransformBase
{
    internal override bool Is3D => true;

    internal override TransformState Read()
    {
        Transform3D? transform = Entity.GetComponent<Transform3D>();
        if (transform == null) return LastSent;
        return new TransformState { Position = transform.Position, Rotation = transform.Rotation, Scale = transform.Scale };
    }

    internal override void Apply(in TransformState state)
    {
        Transform3D? transform = Entity.GetComponent<Transform3D>();
        if (transform == null) return;
        if (SyncPosition) transform.Position = state.Position;
        if (SyncRotation) transform.Rotation = state.Rotation;
        if (SyncScale) transform.Scale = state.Scale;
    }
}
