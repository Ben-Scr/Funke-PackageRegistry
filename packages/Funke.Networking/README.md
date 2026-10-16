# Funke.Networking

Multiplayer for every platform Funke builds for. One process hosts (server plus its own player) or runs as a
dedicated server; desktop, Android and iOS players join over UDP, browser players over WebSocket, and all of them
play together on the same server. On top of the connection: typed messages, LAN discovery, networked objects that
spawn on every peer, ownership, and transforms that move smoothly on the other screens.

Everything runs on the main thread: the package polls its sockets once per frame, before the scripts' `OnUpdate`,
so message handlers and events can touch entities freely.

## Platforms

| Platform | Join | Host / server | Notes |
|---|---|---|---|
| Windows, Linux, macOS | UDP or WebSocket | yes (UDP + WebSocket) | |
| Android | UDP or WebSocket | yes | The package adds the `INTERNET` permission. Hosting on mobile data rarely works (carrier NAT). |
| iOS | UDP or WebSocket | yes | iOS asks the player for local network access. LAN discovery broadcasts need Apple's multicast entitlement; joining by address works without it. |
| Web | WebSocket only | no | Browsers can't open UDP sockets or accept connections. A page served over `https://` may only use `wss://` (see [Browsers](#browsers)). |

## Install

Package Manager → **Funke.Networking** → Install, or `funke package add Funke.Networking`. C# only; it ships
[LiteNetLib 2.1.4](https://github.com/RevenantX/LiteNetLib) (MIT) as source for the UDP side.

## Quick start

```csharp
using Funke;
using Funke.Networking;

public struct Chat : INetMessage
{
    public string Text;
    public void Serialize(NetWriter writer) => writer.Write(Text);
    public void Deserialize(NetReader reader) => Text = reader.ReadString();
}

public class Lobby : EntityScript
{
    public Entity PlayerPrefab = Entity.Invalid;   // a prefab with NetworkObject + NetworkTransform2D

    public override void OnEnable()
    {
        NetworkServer.ClientConnected += OnClientConnected;
        NetworkClient.Disconnected += OnDisconnected;
        NetworkServer.RegisterHandler<Chat>((from, chat) => NetworkServer.SendToAll(new Chat { Text = $"{from.Id}: {chat.Text}" }));
        NetworkClient.RegisterHandler<Chat>(chat => Log.Info(chat.Text));
    }

    public override void OnDisable()
    {
        NetworkServer.ClientConnected -= OnClientConnected;
        NetworkClient.Disconnected -= OnDisconnected;
    }

    public void Host() => NetworkManager.StartHost();
    public void Join(string address) => NetworkManager.StartClient(address, connectData: "Nova");

    // Runs on the server for every player, the host's own included.
    private void OnClientConnected(NetConnection client)
        => NetworkServer.Spawn(PlayerPrefab, new Vector3(0, 0, 0), 0f, ownerId: client.Id);

    private void OnDisconnected(string reason) => Log.Warn("Left the game: " + reason);
}
```

The player prefab moves only where it is owned:

```csharp
public class PlayerController : EntityScript
{
    public override void OnUpdate()
    {
        if (NetworkObject.Of(Entity) is not { IsOwner: true }) return;   // other peers just watch
        Transform!.Position += Input.GetAxis() * (5f * Time.DeltaTime);
    }
}
```

Set its `NetworkTransform2D.Authority` to **Owner**, and every other peer sees it move smoothly.

## Sessions

| Call | Does |
|---|---|
| `NetworkManager.StartHost()` | Server plus a local player. `NetworkServer.ClientConnected` and `NetworkClient.Connected` fire for that player on the next frame. |
| `NetworkManager.StartServer()` | Dedicated server without a player. |
| `NetworkManager.StartClient(address, connectData)` | Joins. `"192.168.0.5"`, `"host:7777"`, `"[::1]:7777"`, `"ws://host:7778"`, `"wss://example.com/game"`. |
| `NetworkManager.Stop(reason)` | Ends the session. The other side gets `reason`. |

Results arrive as events, never as exceptions in your frame:
- `NetworkClient.Connected`: joined, and the objects that already exist are spawned.
- `NetworkClient.Disconnected(reason)`: rejected, wrong version, server gone, timed out, or unreachable. Not raised when you call `Stop`.
- `NetworkServer.ClientConnected(client)`, `NetworkServer.ClientDisconnected(client, reason)`.
- `NetworkManager.Stopped(reason)`: the session ended for any reason.

`Start…` throws `NetworkException` only for mistakes you can fix right away: a port that is already in use, or an address that isn't one.

Client ids: the server (and the host's own player) is `NetworkManager.ServerClientId` (0); remote clients get 1, 2, ….
`NetworkManager.LocalClientId` tells a process who it is.

Reasons are plain sentences meant for players, for example:
- "Version mismatch: the server runs protocol 3, this game 2."
- "The server is full (16/16)."
- "192.168.0.5:7777 does not answer (no server on that address and port, or a firewall blocks UDP)"
- whatever your `ConnectionApproval` returned.

## Messages

A message is any struct or class implementing `INetMessage`; write and read the fields in the same order.
`NetWriter`/`NetReader` cover primitives, packed integers, strings, byte arrays, vectors and colors.

```csharp
NetworkClient.Send(new Chat { Text = "hi" });                        // client -> server
NetworkServer.Send(client, new Chat { Text = "welcome" });           // server -> one client
NetworkServer.SendToAll(new Chat { Text = "go" }, NetChannel.Reliable, exceptClientId: 3);
```

Server and client handlers are registered separately (`NetworkServer.RegisterHandler`, `NetworkClient.RegisterHandler`).
On a host, both run in the same process without being mixed up. Message types are matched by their full type name,
so both sides need the same type, in the same namespace.

| `NetChannel` | Delivery | Use for |
|---|---|---|
| `Reliable` (default) | once, in order | lobby, chat, game events, spawns |
| `ReliableUnordered` | once, any order | independent events |
| `Unreliable` | may be lost or reordered, ≤ 1000 bytes | effects, frequent state |
| `Sequenced` | may be lost, older ones dropped, ≤ 1000 bytes | inputs, positions |

Reliable messages can be up to 1 MB (split and reassembled on UDP). Browser connections run over TCP, so every
channel is reliable and ordered there. Send the same way regardless; the server picks the right path per client.

## Networked objects

- **Spawned objects:** `NetworkServer.Spawn(prefab, position, rotation, ownerId)` instantiates a prefab on the server and on
  every client, including clients that join later. `NetworkServer.Despawn(entity)` removes it everywhere. Destroying it
  any other way on the server is noticed one tick later.
- **Scene objects:** an entity placed in a scene with a `NetworkObject` script is linked across peers through its scene GUID.
  Every peer must load the same scene.
- **Ownership:** `NetworkObject.OwnerId` and `IsOwner`. The server changes it with `SetOwner(clientId)`, and every peer gets
  the `OwnerChanged` event. When a client leaves, the objects it owned are despawned; with `DestroyWithOwner` off,
  they go back to the server instead.
- **Object messages:** per-object messages through `NetworkObject`:
  - `SendToServer` (client → server, the handler sees who sent it), `SendToClients` and `SendToOwner` (server → clients).
  - Handlers: `RegisterServerHandler<T>((from, msg) => …)` and `RegisterClientHandler<T>(msg => …)`.
  - Check `from.Id == OwnerId` before trusting a request.
- **Who runs scripts:** scripts on networked entities run on every peer. Guard owner-only logic with `IsOwner` and
  server-only logic with `NetworkManager.IsServer`.

## NetworkTransform2D / NetworkTransform3D

These scripts send the entity's position, rotation and, if you want, scale at `NetworkSettings.TickRate` (30/s), and
only when something changed. Every other peer shows the object `InterpolationDelay` seconds (0.1) in the past,
blended between two received states.
- **Smooth playback:** jitter and lost packets don't make it stutter.
- **Teleports:** jumps longer than `TeleportDistance` are not blended. `Teleport()` snaps the next move on purpose, for example after a respawn.
- **`Authority = Server`:** the server moves it (enemies, doors, the ball).
- **`Authority = Owner`:** the owning client moves it (player characters). The server checks that a client only moves its own objects, then passes the update on.

Copies that don't have authority are positioned every frame. Make their rigidbodies kinematic, or physics will
fight the sync.

## LAN discovery

```csharp
NetworkDiscovery.ServerFound += server => Log.Info($"{server.Name} {server.Players}/{server.MaxPlayers} at {server.EndPoint}");
NetworkDiscovery.StartSearch();       // asks the network every second
...
NetworkManager.StartClient(NetworkDiscovery.Servers[0].EndPoint);
NetworkDiscovery.StopSearch();
```

Servers answer automatically: set `Settings.ServerName`, and optionally `Settings.DiscoveryInfo` (map, mode, …).
`IsCompatible` is false for servers with another `ProtocolVersion`. Browsers can't search (`NetworkDiscovery.IsSupported`).

## Accepting players

```csharp
NetworkServer.ConnectionApproval = request =>
    request.ConnectData == "" ? "Pick a name first." : null;   // null accepts, a string rejects with that reason
```

`request.ConnectData` is the string the client passed to `StartClient`.

## Settings

Set `NetworkManager.Settings` before starting, or pass a `NetworkSettings` to a `Start…` call.

| Field | Default | |
|---|---|---|
| `GameId` | `"FunkeGame"` | Only peers with the same id talk. **Set it to your game's name.** |
| `ProtocolVersion` | 1 | Bump when messages change; older builds are rejected with a clear reason. |
| `Port` | 7777 | UDP game port and LAN discovery. |
| `WebSocketPort` | 7778 | For browsers; 0 turns browser support off. |
| `MaxConnections` | 16 | Remote players the server accepts. |
| `TickRate` | 30 | Transform updates per second. |
| `ConnectTimeout` / `Timeout` | 5 s / 10 s | Giving up on connecting / dropping a silent connection. |
| `Discoverable`, `ServerName`, `DiscoveryInfo` | true, "Funke Game", "" | LAN discovery. |
| `SimulatedLatencyMin/Max`, `SimulatedPacketLoss` | 0 | Bad network on purpose (UDP), for testing. |

## Browsers

A server always listens for browsers on `WebSocketPort` too. A browser game joins with `StartClient("host")`, which means
`ws://host:7778`, or with a full `ws://` or `wss://` address.
- **`http://` pages:** a game served from `http://` (local tests, a LAN) can use `ws://` directly.
- **`https://` pages (itch.io, GitHub Pages, your own site):** they must use `wss://`, which needs a TLS certificate.
  The server speaks plain `ws://`, so put a TLS proxy in front of it. With [Caddy](https://caddyserver.com) on the
  server machine and a domain pointing at it:

```
game.example.com {
    reverse_proxy localhost:7778
}
```

  Browsers then join with `StartClient("wss://game.example.com")`. Desktop and mobile players keep using UDP port 7777.
- **No player-hosted browser games:** a browser can't host. Browser players always need a desktop/mobile host or a dedicated server.

## Playing over the internet

The host needs UDP port 7777 (and TCP 7778 for browsers) forwarded in its router, or a dedicated server with a public
address. Version 1.0 has no relay or NAT punch-through.

## Testing

- **Several players on one machine:** start the game several times. Give each Funke-Runtime copy its own exe name, so each
  writes its own log.
- **Bad networks:** `NetworkSettings.SimulatedLatencyMin/Max` and `SimulatedPacketLoss` work on UDP. Try 60–120 ms and 5 % loss.
- **In the editor:** stopping Play closes every connection and clears all handlers and events the game registered.

## In the editor

The package's scripts (`NetworkObject`, `NetworkTransform2D`, `NetworkTransform3D`) don't appear in the Add Component menu
yet. Until they do, you have two ways to use them:
- **Scene and prefab JSON:** add them by full class name, `"className": "Funke.Networking.NetworkTransform2D"`.
- **Editor:** derive an empty class in your game and add that one: `public class PlayerSync : NetworkTransform2D { }`.

## Limits of 1.0

- **Scenes:** no scene synchronization. Load the same scene on every peer yourself (a message works), then spawn.
- **Prediction:** no client-side prediction or rollback. Owner-authority transforms make player movement feel instant
  for the owner; server-authoritative games with fast action need their own prediction.
- **Encryption:** none. UDP and `ws://` traffic is plain, so don't send passwords or other secrets through it.
- **Ownership checks:** only for transforms; validate object messages yourself.

## Third-party code

`Source/ThirdParty/LiteNetLib` is LiteNetLib 2.1.4 by Ruslan Pyrch, MIT license (`LICENSE.txt` next to it). Its types
are made `internal` so they never clash with a LiteNetLib NuGet package in your game.
