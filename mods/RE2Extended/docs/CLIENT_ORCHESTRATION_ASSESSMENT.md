# Voice Interface — client-orchestrated multiplayer assessment

Status: **static assessment only. No code changed. Runtime probe required.**
Baseline: Empyrion **1.20.1 build 5232**, client MVID
`462483cd-2f1f-4a24-9c83-46d24b1cb3f4`.
Related: [`MULTIPLAYER_VOICE_PLAN.md`](MULTIPLAYER_VOICE_PLAN.md),
[`SERVER_DETECTION_ASSESSMENT.md`](SERVER_DETECTION_ASSESSMENT.md).

## Verdict

Client-side orchestration is **viable and is the lowest-risk multiplayer design**
for the Voice Interface. It avoids the unresolved server-to-client transport
problem entirely and keeps all audio, queueing and alarm suppression local to
each client.

The current single-player design is already close to this model. The remaining
question is not whether the architecture is sound, but whether each crew client
receives the block-removal updates needed by the existing detector for the
structure it is aboard.

## Why this is a good fit

The existing feature already separates detection from audio:

```text
BlockPosValueList.Remove
  -> classify removed block type
  -> require local player's current structure
  -> require active VoiceInterface on that structure
  -> play locally through Unity AudioSource
```

Nothing in the audio path is server-specific. Each client loads the mod package,
has its own WAV files, and has its own `VoicePlayer` queue and alarm state. There
is no state that needs to be shared between clients.

This matches the design recommendation in `MULTIPLAYER_VOICE_PLAN.md`:
each modded crew client detects removals for its own current structure and plays
its own local copy exactly once.

## Detection viability

### Existing `BlockPosValueList.Remove` hook

The hook is a general block-aggregate update path, not a local-player-only path.
It fires when the client's `ConditionDictionary` removes a device entry. In
single-player that happens when the world applies a block removal.

In multiplayer the same client-side world must apply replicated block changes for
a loaded ship, including the ship the local player is aboard. If that application
path updates the aggregate, the existing hook should fire without changes.

This is plausible but **not proven**. The critical probe is whether the client
receives an aggregate removal for:

- the ship the local player is piloting;
- a ship the local player is only a passenger on;
- damage caused by another player, NPC, turret, explosion or collision.

If the client only receives structure health/damage updates and not the block
removal/aggregate update, the current hook will not fire and the feature stays
single-player-only.

### Alternative client detector: `IModApi.GameEvent`

Build 5232 exposes `GameEventType.BlockDestroyed` through `IModApi.GameEvent`.
That event carries the owning `ActionLayout`, the old block value and the block
position, so it is a simpler detector than `BlockPosValueList.Remove`.

It is worth probing on a client in a dedicated-server session. If clients receive
`BlockDestroyed` for their own or their passenger ship, the detector can move to
the public event and avoid the aggregate-instance matching problem entirely.

Do not assume the public event covers every damage source; its call site has
conditions that need a runtime check.

## Passenger structure resolution

The current code uses:

```text
IApplication.LocalPlayer.CurrentStructure
```

`PlayerBridge.CurrentStructure` resolves the player's current vessel reference,
which is likely populated for passengers as well as pilots. That is a static
expectation, not a verified multiplayer fact.

If `CurrentStructure` is null or stale for passengers, the fallback chain should
be:

```text
LocalPlayer.CurrentStructure
  -> LocalPlayer.DrivingEntity?.Structure
  -> a structure whose Pilot or GetPassengers() contains LocalPlayer
```

The fallback should be resolved once per event or cached by player/structure
change; it should not become a per-frame structure scan.

## Main risks and limitations

- **Replicated aggregate coverage** — the decisive unknown.
- **Prediction/rollback** — the local client may predict a destruction before the
  server confirms it. Audio-only early playback is low impact, but a later
  server echo can replay the same alert after the queue has drained.
- **Duplicate aggregate removals** — a multi-cell component may produce multiple
  `Remove` calls; current behavior is intentionally naive.
- **False positives from placement/replacement** — `BlockPosValueList.Add` calls
  `Remove` internally for duplicate positions, which is already a known PoC
  limitation and may be more visible on a busy multiplayer server.
- **Deconstruction/pickup** — the current hook cannot distinguish damage from
  manual removal. This remains accepted unless a later detector adds a source
  check.
- **Ship change** — queued events are global to the client, not tied to a
  structure. Leaving or boarding a ship can leave old alerts playing or queued.
- **Remote observers** — clients not aboard the affected ship hear nothing. This
  is the intended first-release behavior.
- **Unloaded playfield** — no detection if the client no longer has the ship
  loaded.

## Recommended implementation if the probe passes

1. Add a small `LocalStructureContext` helper:
   - current structure;
   - pilot/passenger membership;
   - active Voice Interface gate;
   - ready/powered gate.
2. Use it from the existing removal hook instead of calling
   `LocalPlayer.CurrentStructure` directly.
3. Cache the resolved structure and active-device result by structure id; avoid
   re-resolving for every removed cell.
4. Add a short per-structure/position/event dedupe window if prediction plus
   server application produces duplicate alerts.
5. Optionally clear queued voice/alarm state when the local structure changes.
6. Keep the existing client-only mode gate. No PfServer detector and no network
   protocol are required.
7. Fail closed on an unknown assembly MVID or missing native types.

## Probe matrix

| Case | Expected result |
| --- | --- |
| Single-player pilot destroys own component | existing alert |
| Dedicated, Client A pilots Ship A and destroys a component | A hears alert |
| Dedicated, Client B observes Ship A | B hears nothing |
| Dedicated, Client C is passenger on Ship A | C hears alert if client aggregates are sufficient |
| Damage by another player | same as above |
| Damage by NPC/turret/explosion/collision | same as above |
| Voice Interface off/unpowered | no alert |
| Unmodded passenger | no audio, no error |
| Player leaves Ship A | no further alerts for A |
| Player boards Ship B | alerts follow B |
| Multiple components destroyed in one burst | queue behaves as designed |
| Placement/replacement near the ship | no unexpected alert, or accepted false positive |

Also log, per client:

```text
local player entity id, pilot flag
local player CurrentStructure id/name
DrivingEntity structure id
structure Pilot and GetPassengers()
removal list instance, bucket name, position, old block type/name
whether BelongsToStructure passed
VoiceInterface active count
```

## Recommendation

Probe the current client detector in a real dedicated-server session before
changing architecture. If the client receives the aggregate removals for its own
or passenger structure, keep the client-orchestrated design and harden only
structure resolution and duplicate handling. If it does not, try the public
`GameEventType.BlockDestroyed` client event before considering a PfServer
detector. Server detection should remain a fallback, not the default.
