# Voice Interface — server-side destruction detection assessment

Status: **static assessment only. No code changed. Runtime probe required.**
Baseline: Empyrion **1.20.1 build 5232**, client MVID
`462483cd-2f1f-4a24-9c83-46d24b1cb3f4`, dedicated MVID
`fae3f936-680a-459b-99d0-6fe468a1f17f`.
Related: [`MULTIPLAYER_VOICE_PLAN.md`](MULTIPLAYER_VOICE_PLAN.md),
[`VOICE_INTERFACE_PLAN.md`](VOICE_INTERFACE_PLAN.md).

## Verdict

Server-side detection is **viable in the PfServer process**, but it is **not
viable in the Dedi master process** and it does **not by itself make multiplayer
work**.

The hard part is not detecting destruction on the server. The hard part remains
delivering the resulting event or audio to the crew clients. Build 5232 still has
no verified public server-to-client custom packet route (`SendToPlayer` returns
`false` in the inspected client and dedicated `NetworkBridge` implementations).

Recommendation: do not move detection to PfServer as the first multiplayer
change. First probe the public `IModApi.GameEvent` / `GameEventType.BlockDestroyed`
path on clients. If the native game event already reaches each crew client, the
current client-local audio design can stay and no server detector is needed. If
it does not, add a PfServer detector only after a delivery route is proven.

## What "server" means on build 5232

| Process | `Assembly-CSharp` | World/structure API | Server-side detection |
| --- | --- | --- | --- |
| `Client` | full client assembly | yes | yes, but local client only |
| `SinglePlayer` | full client assembly | yes | yes, same process |
| `PlayfieldServer` | full client assembly / bridge implementation | yes | **yes, best server target** |
| `DedicatedServer` | stripped master assembly | no `IPlayfield`/`IStructure`; null block mapping | **no** |

The dedicated master launches playfield server processes, either
`PlayfieldServer/EmpyrionPlayfieldServer.exe` or `Client/Empyrion.exe` depending
on the installation layout. Those processes use the full client-side world code,
not the stripped dedicated `Assembly-CSharp`.

Therefore, "have the server detect destruction" must mean **PfServer or
SinglePlayer**, not `ApplicationMode.DedicatedServer`.

Current code rejects both server modes in
`VoiceInterfaceFeature.TryInstall` and resolves the structure through
`LocalPlayer.CurrentStructure`. A server detector would need to be split from the
client audio presenter.

## Candidate detection paths

### A. Public `IModApi.GameEvent` / `GameEventType.BlockDestroyed` (preferred)

Build 5232 exposes:

```text
IModApi.GameEvent : GameEventDelegate
GameEventType.BlockDestroyed
GameEventType.BlockChanged
```

Static call path:

```text
ActionLayout.ReduceIcon(...)
  -> StoreQueueConnectionToken.JoinDrive(GameEventType.BlockDestroyed, ...)
  -> ModApiBridge.OnGameEvent(...)
  -> mod's IModApi.GameEvent handler
```

The `BlockDestroyed` call site supplies:

```text
arg1 = destroyer / changing entity (ReferenceHelper)
arg2 = ActionLayout (owning structure/layout)
arg3 = old ControlTable (old block type)
arg4 = related entity/structure id value
arg5 = Vector3i block position
```

This is attractive because it already carries the owning structure and the old
block value. It avoids `LocalPlayer` and avoids reverse-mapping a
`BlockPosValueList` instance back to its structure.

Static caveat: `ActionLayout.ReduceIcon` raises the event only when both an apply
flag is set and the changing-entity id is not `-1`. Damage from players, NPCs,
turrets or explosions may or may not satisfy that condition. The argument
semantics and coverage must be probed in a real dedicated session.

### B. Existing `BlockPosValueList.Remove` hook in PfServer

The current hook could technically be installed in PfServer because PfServer uses
the full client assembly. It is a worse fit on a server:

- there is no `LocalPlayer` or `CurrentStructure`;
- the hook receives only the removing list instance and position;
- the list has no reliable public owner field. `BlockPosValueList.cc` is set only
  for particular aggregate lists, not for the injected `RE2Extended` bucket;
- mapping every removing list to a structure would require a reverse index built
  from each loaded structure's `ConditionDictionary` buckets, or a per-event scan
  over playfield structures.

This is possible but more fragile than the public `BlockDestroyed` event.

### C. Dedi master

Not viable. The dedicated assembly has no world, structure or block mapping
implementation; its `ActionLayout` is a stub and `GetBlockAndItemMapping()`
returns `null`. Do not install a detector there.

## What a PfServer detector would need

1. Resolve the owning structure from `ActionLayout` / `DatabaseToken`, or from a
   cached playfield structure map.
2. Map the old block type with `IApplication.GetBlockAndItemMapping()`; this is
   available in PfServer, unlike Dedi master.
3. Apply the same component classifier and active `VoiceInterface` gate as the
   client version.
4. Target **all crew of that structure**: `structure.Pilot` and
   `structure.GetPassengers()`. The client-local design only needs the local
   player.
5. Keep queue, repeat suppression and alarm state client-local. The server should
   emit one logical event per destruction, not maintain audio state.
6. Fail closed on an unknown MVID or missing symbol; never crash the block-removal
   path.

## The remaining blocker: server-to-client delivery

A PfServer detector produces no sound by itself.

| Route | Build-5232 status |
| --- | --- |
| `INetwork.SendToPlayer` | inspected implementations return `false`; unverified and likely unavailable |
| `Client -> Dedicated`, `Dedicated -> PfServer` | statically implemented, but not a server-to-client route |
| Native `ModGameEvent` / `GameEvent` packages | exist internally; no verified mod-facing send-to-player API |
| Replicated block signal / switch state | native replication exists; could carry an event pulse, but needs a probe and encoding design |
| Native sound path with custom clips | `ISoundPlayer` needs clips in the game's sound registry; current WAVs are client-loaded files, not registered clips |

So server-authoritative detection only becomes useful after one of these is
proven. Do not build a PfServer broadcaster on an unverified internal packet
path.

## Recommended order

1. **Probe the public client `GameEvent` path first.** With a dedicated server and
   two clients, log `GameEventType.BlockDestroyed` / `BlockChanged` on:
   - the pilot's own ship;
   - a passenger's ship;
   - an observer client;
   - destruction caused by player, NPC/turret, explosion/collision.
   If every crew client already receives the event with the old block value and
   the affected structure, switch the client detector to this event and keep the
   existing client-local audio.
2. **If clients do not receive it**, implement a PfServer detector using the
   public `GameEvent` first, with the existing `BlockPosValueList.Remove` hook as
   a fallback only if the public event misses damage cases.
3. **Solve delivery before shipping server detection.** Probe `SendToPlayer`, the
   native `ModGameEvent` path, or a replicated signal/switch channel. Keep
   delivery behind a capability check and disable the feature when unavailable.
4. Keep audio client-only, 2D and per-client. Do not create audio in PfServer or
   Dedi master.

## Probe questions that decide the design

- Does `GameEventType.BlockDestroyed` fire on PfServer for real damage, not only
  for manual block removal?
- What exactly are `arg1` through `arg5` for each destruction source?
- Is `arg4` a stable structure/entity id, or only a transient value?
- Does the event fire once per multi-cell component or once per removed cell?
- Do crew clients receive `BlockDestroyed` for the ship they are aboard when the
  destruction is applied by PfServer?
- Does `SendToPlayer` actually deliver even though it returns `false`?
- Does a server-side switch/signal change replicate to clients with enough
  information and acceptable latency to act as an event channel?

Until these are answered, server-side detection should be treated as a
**technically feasible but incomplete** direction, not as a drop-in multiplayer
fix.
