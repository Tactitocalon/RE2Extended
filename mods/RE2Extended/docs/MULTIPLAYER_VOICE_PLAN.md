# Voice Interface — multiplayer plan

Status: **draft. No multiplayer code written yet.**
Baseline: Empyrion **1.20.1 build 5232**, client and dedicated assemblies.
Related: [`VOICE_INTERFACE_PLAN.md`](VOICE_INTERFACE_PLAN.md).

The current Voice Interface is a client-local feature:

- the Harmony hook is installed only in `Client` / `SinglePlayer`;
- it checks `LocalPlayer.CurrentStructure`;
- it plays audio locally through Unity `AudioSource`;
- it sends no network messages and receives no server events.

That works for the local player’s current ship, but multiplayer semantics and
the server/client ownership of block destruction still need to be proven.

---

## 1. Definition of done

Multiplayer support should mean:

- a modded player aboard a ship with an active Voice Interface hears that ship’s
  alerts, whether they are piloting or a passenger;
- each modded player aboard hears their own local copy exactly once per event;
- players not aboard the affected ship hear nothing in the first multiplayer
  version;
- unmodded clients simply hear nothing and are not rejected;
- the dedicated/server process never creates audio;
- the voice queue, repeat detection, and alarm suppression remain per-client;
- a changed build or missing native symbol disables multiplayer voice, not the
  game;
- the behavior is verified with at least two clients and a dedicated server.

Not in the first multiplayer version:

- positional/3D audio for nearby non-crew players;
- server-authoritative event delivery if the public packet path is unavailable;
- mod-version handshakes or rejection of unmodded clients;
- syncing queue state between clients.

---

## 2. Current player/structure model

Useful public API on both client and PfServer:

```text
IApplication.LocalPlayer
IPlayer.CurrentStructure
IPlayer.DrivingEntity
IPlayer.IsPilot
IStructure.Pilot
IStructure.GetPassengers()
IPlayfield.Players
IPlayfield.Entities
IEntity.Structure
```

This is enough to decide, on a client or PfServer, which players are aboard a
given structure without scanning blocks.

Current client scope:

```text
BlockPosValueList.Remove
  -> only if the removing bucket belongs to LocalPlayer.CurrentStructure
  -> only if that structure has an active VoiceInterface
  -> play locally
```

The first multiplayer question is whether this already works for:

- the pilot’s own ship;
- a passenger on another player’s ship;
- a remote client observing the same ship.

That must be answered by a runtime probe, not by decompilation alone.

---

## 3. Build-5232 network findings (static)

The public `INetwork` API exposes:

```text
SendToDedicatedServer(receiver, data, playfieldName)
SendToPlayfieldServer(receiver, playfieldName, data)
SendToPlayer(receiver, playerEntityId, data)

RegisterReceiverForDediPackets(...)
RegisterReceiverForPlayfieldPackets(...)
RegisterReceiverForClientPackets(...)
```

Inspected implementations:

| Process | `SendToDedicatedServer` | `SendToPlayfieldServer` | `SendToPlayer` |
| --- | --- | --- | --- |
| Client `NetworkBridge` | works | returns `false` | returns `false` |
| Dedicated `NetworkBridge` | returns `false` | works | returns `false` |

So on build 5232 the public mod-network route appears to be:

```text
Client -> Dedicated
Dedicated -> PlayfieldServer
```

but there is **no verified public route from a server process back to a client**.
`RegisterReceiverForClientPackets` exists, but the inspected `SendToPlayer`
implementations return `false`.

This is a major planning constraint. The actual PfServer process still needs a
runtime probe; the static result above is not enough to assume the final answer
for every process/mode.

Also note: `NetworkBridge` derives the receiver key from
`Assembly.GetCallingAssembly()`. A module that sends from
`RE2Extended.Runtime.dll` and registers from the same assembly should use the
same assembly-derived receiver name. Do not assume it is the `_Info.yaml` mod
name without probing.

---

## 4. Recommended strategy

### Recommendation: make client-crew-local detection the first multiplayer target

The lowest-risk multiplayer implementation is:

```text
Each modded client detects removals for the structure the local player is
currently aboard, and plays the alert locally.

No server packets.
No network authority.
No cross-client queue sync.
```

This is an extension of the current behavior, not a rewrite. It works with each
client’s own audio and avoids the missing server->client packet route.

It is sufficient if the runtime probe shows that clients receive the relevant
device-aggregate removals for their current structure, including when they are
passengers.

### Use server-authoritative delivery only if the probe requires it

If clients do not receive reliable aggregate removals for passenger/local ships,
or if the design must include remote observers, a PfServer event producer is
needed. That requires solving the server->client transport separately.

Do **not** start by writing a PfServer detector until the client probe shows that
client-side detection is insufficient.

---

## 5. Phase 0 — multiplayer probe

Write a diagnostic build (or a verbose mode in the existing feature) that logs
the following per process/client:

```text
process mode (Client/SinglePlayer/PlayfieldServer/DedicatedServer)
local player entity id, pilot flag, current structure id/name
structure pilot and passenger list
removal list instance, bucket name if resolvable, position, old block type/name
VoiceInterface present/active on that structure
whether the event passed the local-structure gate
```

Probe matrix:

| Case | Expected first-pass result |
| --- | --- |
| Single-player pilot destroys own thruster | alert |
| Dedicated, Client A pilot destroys own thruster | Client A alert |
| Dedicated, Client B observes A’s ship | no audio |
| Dedicated, Client C is a passenger on A’s ship | C alert, if client aggregates are sufficient |
| Damage caused by NPC/player/turret instead of self | same as above |
| Voice Interface off/unpowered | no alert |
| Unmodded client aboard | no alert, no error |
| Player leaves the ship | no further alerts for that ship |
| Player boards another ship | alerts follow the new current structure |

Also probe the network API in the real processes:

1. From a client, call `SendToDedicatedServer` and confirm the dedicated receiver
   callback fires.
2. From the dedicated process, call `SendToPlayfieldServer` and confirm the
   PfServer receiver fires.
3. From PfServer/dedi, call `SendToPlayer` for a real player and confirm whether
   it returns `true` and whether the client callback fires.
4. Register a client receiver before the first send.

Record the exact process/mode, build hash/MVID, result, and whether the return
value matched actual delivery.

---

## 6. Option A — client-crew-local detection (recommended first release)

### Behavior

- Each modded client detects block removals for its own current structure.
- The local player may be piloting or a passenger.
- The client checks the active Voice Interface on that structure.
- The local queue/alarm system plays the alert.
- Other clients independently do the same for their own structures.
- No server packets are sent.

### Changes from the current code

- Replace the assumption “local player is piloting” with “local player’s
  `CurrentStructure` is the affected structure.”
- Prefer `LocalPlayer.CurrentStructure`; if it is not reliable for passengers,
  fall back to:
  - `LocalPlayer.DrivingEntity?.Structure`, or
  - a structure whose `Pilot`/`GetPassengers()` contains the local player.
- Keep the bucket-instance-to-structure association. The current
  `BelongsToStructure` logic already compares against the local structure’s
  bucket instances.
- Make the active Voice Interface check use the same selected structure.
- Keep queue, event dedupe, and alarm suppression local to the client.

### Advantages

- no missing network transport;
- no server authority needed for audio;
- low bandwidth;
- works with the existing Harmony/detection path;
- naturally gives each crew member their own correctly-timed local alert.

### Limitations

- only works if the client receives the aggregate removal;
- remote observers hear nothing;
- a client that does not load the ship/playfield hears nothing;
- no anti-cheat guarantee, but audio-only spoofing is low impact;
- each client can have slightly different timing or queue state.

### Acceptance criteria

- pilot and passenger cases both pass;
- a client only reacts to its current structure;
- no duplicate local player in `CurrentStructure`/passenger resolution;
- unmodded clients are unaffected.

---

## 7. Option B — PfServer-authoritative events plus a client delivery route

Use this only if Option A cannot see the required removals or if the design
requires remote/observer audio, server-side anti-spoofing, or centralized event
generation.

### Detection on PfServer

- Install the detector in `ApplicationMode.PlayfieldServer` (and optionally
  single-player), not in client mode.
- The PfServer must map the removing `BlockPosValueList` instance to a structure.
  Options:
  - resolve the owning `ActionLayout`/`DBC` if available;
  - map bucket instances across `IPlayfield.Entities`/structures;
  - hook a server-side damage/removal path that already carries structure
    identity.
- Determine the target players:
  - `structure.Pilot`;
  - `structure.GetPassengers()`;
  - optionally `IPlayfield.Players` whose `CurrentStructure.Id` matches.
- Apply the component classifier and active-device gate on the server.
- Build one logical `VoiceEvent` per destruction, with a stable event ID and
  sequence/timestamp.

### Delivery

Preferred if available:

```text
INetwork.SendToPlayer("RE2Extended.Runtime", playerEntityId, packet)
```

Required runtime proof: `SendToPlayer` must return `true` and the client
`RegisterReceiverForClientPackets` callback must fire. The inspected build-5232
client and dedicated implementations both return `false`, so this is currently
unproven and likely unavailable through the public API.

If `SendToPlayer` is unavailable, evaluate these in order:

1. **Game sound path** — `ISoundPlayer.PlaySound(sourceEntityId, clipName, OneShot)`
   or another native sound replication path. This likely requires registering the
   custom clip on each client and proving the server call reaches clients. It is
   build-specific and may not support arbitrary WAV clips.
2. **Supported future API** — keep the server detector behind a capability check
   and enable delivery only when a later build implements `SendToPlayer`.
3. **Internal Harmony network hook** — only after a probe identifies a stable
   internal packet path. Treat as experimental and fail closed.

Do not silently use an unverified internal packet path in a release.

### Protocol sketch

If delivery becomes available:

```text
magic:        "RE2VI"
version:      byte
eventType:    byte
structureId:  int
eventId:      string/hash
gameTicks:    ulong
blockType:    int
position:     optional
```

Client processing:

```text
validate version/build
-> check feature enabled
-> check local player is a target of this structure/packet
-> apply local VoiceEvent queue/alarm rules
-> ignore duplicate or stale events
```

Rate-limit and dedupe on the server per structure and event type.

### Advantages

- authoritative, consistent event generation;
- can target all crew members;
- can later support nearby observers and 3D alarm audio;
- avoids relying on each client’s block aggregate.

### Limitations

- blocked by the current server->client packet gap;
- more code and more failure modes;
- server/client versioning and duplicate suppression become necessary;
- potential audio timing differences still exist.

---

## 8. Option C — hybrid

Use client detection by default, and add server events only when the probe shows
a missing case.

Example:

- client-crew detection handles the pilot’s and passenger’s own ship;
- PfServer sends additional events only for remote/observer audio later;
- a client ignores a server event if it already generated the same event locally.

This can avoid duplicate playback only if the client receives both and can
dedupe by structure + event ID + timestamp. It is more complex than either
single mode and should not be the first implementation.

---

## 9. Recommended implementation phases

### Phase M0 — Probe and contract

- Add temporary verbose logging around mode, local player, structure, pilot,
  passengers, bucket association, and network send/receive.
- Run the probe matrix with two clients and a dedicated server.
- Record results in `docs/NATIVE_FINDINGS.md`.
- Decide: Option A is sufficient, or Option B is required.

### Phase M1 — Client-crew hardening

Assuming Option A passes:

- introduce a single `LocalStructureContext` helper:
  - current structure;
  - pilot/passenger membership;
  - active Voice Interface gate;
  - health/power gate.
- use it from the existing removal hook;
- keep per-client queues and alarms;
- add logs that identify the local player and structure;
- ensure a player leaving the ship clears any queued event/alarm state if needed.

### Phase M2 — Multiplayer test

- dedicated server, two modded clients, one unmodded client;
- pilot, passenger, observer, ship change, playfield change;
- simultaneous destruction bursts;
- queue max 4 and event dedupe;
- alarm repeat suppression;
- server restart and reconnect.

### Phase M3 — Server-authoritative only if needed

Only after M0 shows a concrete gap:

- add PfServer detector;
- implement/verify `SendToPlayer` or a proven alternative;
- add protocol versioning and duplicate suppression;
- add remote-observer targeting if desired;
- keep client mode fallback.

### Phase M4 — Optional observer/3D audio

- decide whether non-crew players should hear alarms;
- send target/position data;
- use 3D AudioSource at the structure/device position;
- apply distance attenuation and an explicit max range.

---

## 10. Test matrix

### Authority and targeting

- Client A piloting Ship A: A hears alerts.
- Client B piloting Ship B: B does not hear A’s alerts.
- Client C passenger on Ship A: C hears A’s alerts if client aggregates are
  sufficient.
- Client D on foot away from ships: no alerts.
- Unmodded passenger: no errors, no audio.
- Dedicated master: no audio objects.
- PfServer-only mode: no audio unless explicitly a client.

### Detection

- Thruster, turret, weapon, and generator removed by:
  - local player weapon;
  - another player;
  - NPC/turret;
  - explosion/collision.
- Manual pickup/deconstruction remains accepted per the current PoC design.
- Structure unloaded, playfield transition, player leaves ship.
- Multiple components destroyed in one burst.
- Voice Interface off, unpowered, or missing.
- More than one active Voice Interface on the ship.

### Queue/alarm

- queue max 4 events;
- same event already queued is rejected;
- same event currently playing is rejected;
- different events queue and play in order;
- alarm plays immediately and skips only while the same alarm is active;
- two clients on the same ship have independent queues and alarms.

### Network (if Option B)

- `SendToPlayer` return value and actual delivery;
- receiver registration before first send;
- packet version mismatch fails closed;
- client receiving an event for a different structure ignores it;
- stale/duplicate packet suppression;
- latency and packet loss behavior;
- server restart/reconnect.

---

## 11. Open questions and proposed defaults

| # | Question | Proposed default |
| --- | --- | --- |
| 1 | Who should hear a ship’s alerts? | All modded players aboard that structure: pilot and passengers. |
| 2 | Should nearby non-crew players hear the alarm? | Not in the first multiplayer version; optional later with 3D audio. |
| 3 | Should every crew member get their own queue? | Yes. Queue and alarm state remain client-local. |
| 4 | Should unmodded clients be rejected/kicked? | No. They simply do not hear the audio. |
| 5 | Is server-authoritative event generation required? | Only if the client probe shows missing removals or if remote observers are required. |
| 6 | What if the client does not receive the aggregate removal? | Probe; fall back to PfServer detection only if a delivery route is verified. |
| 7 | What audio mode for crew? | 2D non-spatial for the first version. |
| 8 | What audio mode for nearby observers later? | 3D at the structure/device position with a configurable max range. |
| 9 | How to resolve `CurrentStructure` for passengers? | `LocalPlayer.CurrentStructure`, with `DrivingEntity.Structure` and pilot/passenger membership as fallbacks. |
| 10 | What if the ship has multiple active Voice Interfaces? | Any one active device enables the alert; duplicates do not multiply sounds. |
| 11 | What if the style/mod versions differ between client and server? | Audio is client-local in Option A; if Option B is added, include a protocol version and fail closed on mismatch. |

---

## 12. Recommendation summary

1. **Probe first.** Determine which clients see `BlockPosValueList.Remove` for
   their local/passenger structure in a real dedicated-server session.
2. **Ship Option A** if the probe passes: each modded crew client detects and
   plays its own alerts. This is the simplest multiplayer-compatible model and
   needs no server->client packet route.
3. **Do not build a PfServer broadcaster yet.** Build 5232’s inspected
   `SendToPlayer` returns `false` in both client and dedicated assemblies. Only
   move to Option B after verifying a working delivery path.
4. Keep audio client-local, 2D, and independent per client. Add observer/3D audio
   and server authority only as a later feature.
