# RE2Extended — Voice Interface implementation plan

Status: **PoC implemented; runtime verification pending.**
Target baseline: Empyrion **1.20.1 build 5232**, client target for audio.
Scenario: Reforged Eden 2, Steam Workshop ID `3143225812`.

PoC scope: add a new signal device, the **Voice Interface**, and play a
**Critical Hit** voice clip when a ship component is destroyed.

## PoC decisions (locked)

- Recognized component destruction triggers a component-specific VoiceEvent:
  thruster, turret/SentryGun, fixed weapon, and power generator.
- Implementation is deliberately naive: every observed removal on the local
  structure attempts to queue the matching VoiceEvent.
- Model and block properties follow `LCDProjector` / `LCDNoFrame1x1`.
- The requested third-party voice WAVs and `alarm_critical_damage.wav` are used.
  `build.ps1` copies them from `third-party/SoundWork/final/` into the ignored
  staged package only.
- Each `VoiceEvent` contains Critical Hit followed by its specific alert:
  Jet Engine Destroyed, Turret Unit Destroyed, Weapon Destroyed, or Power Unit
  Destroyed.
- The voice queue holds a maximum of **4 events**. Adding an event is rejected
  if the same event is already queued or currently playing.
- The alarm channel is separate: no queue, immediate playback, and the same
  alarm is skipped while it is already playing.
- The Critical Damage Alarm currently accompanies every recognized component
  destruction event.
- The detection hook is `BlockPosValueList.Remove(Vector3i)`; this still needs
  in-game confirmation.

This document is deliberately conservative: the block and audio parts are
mostly known work; the **destruction event** is the part that must be probed
before a permanent hook is written.

---

## 1. Definition of done for the PoC

- A new `VoiceInterface` block exists in RE2Extended and can be placed on the
  intended vessel type(s).
- The block is switchable in the control panel / by signal. When it is off,
  unpowered, or absent, no sound is played.
- With an active Voice Interface on the local player's piloted ship, destroying
  a component plays `voice_critical_hit.wav`.
- The sound does not play for:
  - unmodded/unsupported builds;
  - other ships or structures;
  - hull-only blocks, unless deliberately included later;
  - manual pickup/deconstruction, if the chosen detection path can distinguish
    it (see gaps).
- A missing/invalid audio file, missing game symbol, or unknown build disables
  only the voice feature and logs a clear diagnostic. It must not crash or
  affect the rest of RE2Extended.
- The destruction contract is recorded in `docs/NATIVE_FINDINGS.md` with build,
  target, signature, caller/IL evidence, runtime evidence, and failure mode.

Not in the PoC:

- the full verbal-alert set (turret/weapon/thruster/etc.);
- the alarm-sound channel;
- combat/fuel/heat warnings;
- server-authoritative event replication;
- a user-facing in-game configuration UI.

---

## 2. What to reuse from RE2Extended

| Existing piece | Reuse |
| --- | --- |
| Loader/runtime split, bundled field-type-corrected Harmony | Reuse unchanged. |
| ECF block/template injection and localization merge | Reuse for the new block. |
| `ConditionDictionary` `RE2Extended` bucket injection | Reuse for device detection. |
| Device detection/caching pattern in `CruiseSpeedContext` | Reuse shape for Voice Interface presence/active count. |
| Runtime mode gate (`Client`, `SinglePlayer`, `PfServer`, `Dedi`) | Audio is client-only; do not install audio in dedi master. |
| Fail-once logging and exception containment | Reuse. |
| Build staging script | Extend to support optional local audio input. |

Do **not** attach the feature to cruise-speed code or assume the same patch
lifetime. Keep a separate feature class and separate Harmony patches.

---

## 3. Proposed block

### 3.1 Identity and placement

Named block: no fixed `Block Id`. The existing ECF files correctly rely on named
blocks and runtime name lookup; the older fixed-ID notes are obsolete for this
mod's blocks.

| Property | PoC default |
| --- | --- |
| Internal name | `VoiceInterface` |
| Inventory group | `VoiceInterfaceBlocks` (same `*Blocks` pattern as Cruise Amplifier) |
| Display name | `Voice Interface`; inventory group `Voice Interface (CV)` / `(SV)` if split |
| Allowed grids | `MS,SS` for a single block; otherwise separate CV/SV entries |
| Model | `@models/Blocks/Spaceship/CapacitorPrefab` for the first safe PoC |
| Category / Group / IndexName | `Devices` / `cpgSignal` / `RE2Extended` |
| Class | **none** (class-less activateable device, same pattern as Cruise Amplifier) |
| Activation | `IsAccessible`, `IsActivateable`, `IsActivateableInCP`, `IsActivatedOnPlace` |
| Power | `EnergyIn` > 0; feature also gates on `IStructure.IsPowered` |
| Size | `2,1,1` initially, or match the final model |
| Localization | English only initially |

The final model should be a console/computer model if the user wants a better
fit. The Capacitor model is only chosen to minimize PoC model/validation risk.

### 3.2 Stats, recipe, research

These are **not specified** by the requirement. Initial defaults can be
provisional and balanced later:

- mass: ~500 kg;
- volume: ~100 L;
- hit points: ~2000;
- `EnergyIn`: ~50 W;
- `CPUIn`: ~200;
- `MarketPrice`: computed from the final recipe by the existing RE2 material
  value method;
- recipe: a small electronics/computer/capacitor recipe;
- research: deferred until the user chooses the tech-tree parent.

For the first in-game PoC it is acceptable to place the block in creative mode
and defer recipe/research polish. Do not invent a `TechTreeParent` that has not
been verified against the active RE2 config.

### 3.3 Signal behaviour

The requirement says “signal device, can be turned on or off”. The existing
class-less switchable-device pattern already provides:

- control-panel on/off;
- signal-driven activation/deactivation;
- `IBlock.Get(... out active)` as the authoritative active flag.

No additional signal output or native device class is required for the PoC.
“Off” means the active flag is false; a signal-disabled device is also off.

---

## 4. Runtime architecture

Suggested files:

```text
src/RE2Extended.Runtime/
  Audio/
    VoiceClipLoader.cs       # WAV -> float[] -> AudioClip, cached
    VoicePlayer.cs           # AudioSource pool, queue, 2D/3D routing
    VoiceTuning.cs           # paths, volume, queue size, dedupe windows
  Features/VoiceInterface/
    VoiceInterfaceFeature.cs # enable/disable, lifecycle, local structure gate
    VoiceInterfaceGate.cs    # active device detection on local structure
    ComponentClassifier.cs   # block type/name -> component category
    DestructionEvent.cs      # immutable event record
    DestructionDetector.cs   # Harmony callbacks / probe hook
```

Keep Harmony callbacks thin. They should only capture a candidate event and
hand it to the feature. No audio loading or block scanning inside a callback that
can run for every damage/remove operation.

### 4.1 Voice Interface gate

Use the existing `ConditionDictionary` pattern:

```text
IStructure -> StructureBridge.ecc -> DatabaseToken.ActionLayout
  -> ActionLayout.ConditionDictionary
  -> ConditionDictionary.OpenImage("RE2Extended")
  -> BlockPosValueList.GetAllBlockPositonsOfType(mappedBlockId)
```

For each position, call `IStructure.GetBlock(pos).Get(... out active)` and count
only active `VoiceInterface` blocks. The host structure must be ready and
powered. Cache the result for roughly 250–500 ms per structure, as the cruise
feature already does.

Default: any one active Voice Interface enables the feature; duplicates do not
stack. A second device is harmless.

### 4.2 Local-ship scope

The PoC should only play audio for the **local player's current piloted
structure**. This is a client-side audio feature; there is no reason to make it
server-authoritative.

Default gates:

```text
feature enabled
+ known client build
+ local player exists
+ player.CurrentStructure is ready and powered
+ at least one active VoiceInterface on that structure
+ destruction event belongs to that same structure
```

Do not play for AI ships, remote player ships, unloaded structures, or the
dedicated master process.

### 4.3 Event → queue → playback

For the PoC there are four component event types:

```text
ThrusterDestroyed    -> thruster_destroyed    [Critical Hit, Jet Engine Destroyed]
TurretDestroyed      -> turret_unit_destroyed [Critical Hit, Turret Unit Destroyed]
WeaponDestroyed      -> weapon_destroyed      [Critical Hit, Weapon Destroyed]
GeneratorDestroyed   -> power_unit_destroyed  [Critical Hit, Power Unit Destroyed]
  -> verbal event queue (max 4 events)
  -> alarm channel: Critical Damage Alarm (skip if already playing)
```

Channel rules:

- **VoiceEvent**: an event ID plus an ordered list of sound paths. The event is
  the unit of queueing and repeat detection.
- **Verbal event queue**: FIFO with a maximum of 4 pending events.
- **Repeat detection**: reject an incoming event if the same event ID is already
  pending in the queue, or if the currently playing event has that ID.
- **Sequence playback**: all sounds in the current event play in order before the
  next event starts.
- **Alarm channel**: no queue. Play immediately, unless that alarm's AudioSource
  is already playing. Different alarm IDs use separate AudioSources so they can
  coexist later.

---

## 5. Destruction detection — the blocking research item

There is no public `IStructure` block-destroyed event on this build. The
destruction event must be found in managed internals and confirmed with a runtime
probe.

### 5.1 Static evidence gathered on build 5232 (client)

Useful types/methods:

| Candidate | Build-5232 client token | Why it matters |
| --- | --- | --- |
| `BlockPosValueList` | `0x020009AD` | The per-structure device aggregate. |
| `BlockPosValueList.Remove(Vector3i)` | `0x06004D0F` | Called when a block entry leaves an aggregate bucket. |
| `BlockPosValueList.Add(Vector3i, ControlTable)` | `0x06004D0E` | Also calls `Remove` internally for duplicate positions. |
| `BlockPosValueList.GetBlockPosValue(Vector3i)` | `0x06004D17` | Reads the old `RegistryToken`/`ControlTable` before removal. |
| `ConditionDictionary.OpenImage(string)` | `0x0600501E` | Opens a named device bucket. |
| `ConditionDictionary.CopyComponent()` | — | Returns all bucket names; useful for mapping list instances to a structure. |
| `ReferenceProvider.DisconnectBitmap(...)` | `0x0600344F` | Central damage/removal path; contains damage source. |
| `MemoryHelper.EnableSelection(DriveEventArgs,int,bool)` | `0x06001D66` | Damage application path; has `EnumDamageSourceType`. |
| `ReferenceHelper.SaveAssembly(float,int)` | `0x06001CBE` | Virtual damage/removal handoff. |

Tokens are locators only. The permanent implementation should resolve by type +
method name + exact signature, and use the token only as an additional build
check.

### 5.2 Candidate approaches

#### Approach A — `BlockPosValueList.Remove` prefix (recommended for PoC probe)

- Harmony prefix on `BlockPosValueList.Remove(Vector3i)`.
- Before the original runs, call `GetBlockPosValue(pos)` to read the old block
  type.
- Map block type id → block name with the reverse of
  `IApplication.GetBlockAndItemMapping()`.
- Classify the old block as a component.
- Confirm the removal belongs to the local player's structure.
- Emit a `ComponentDestroyed` event.
- Ignore removals invoked from `BlockPosValueList.Add` by setting a thread-local
  or scoped “in Add” flag with an `Add` prefix.
- Ignore `Clear`/`Read` paths; they do not call `Remove`, so loading a save
  should not produce a burst of false alerts.

Advantages: event-driven, cheap, uses the aggregate the game already maintains.

Caveats to prove with the probe:

- Does `Remove` run for turrets, weapons, thrusters, generators, fuel tanks,
  warp drives, radar, power converters, and ammo controllers?
- Does it run once per component, or once per occupied cell for large blocks?
- Is it also used for manual pickup/deconstruction? If yes, can the event be
  filtered by damage source?
- Can a `BlockPosValueList` instance be mapped reliably to a structure?

#### Approach B — damage-path hook

Hook `ReferenceProvider.DisconnectBitmap(...)` and/or
`MemoryHelper.EnableSelection(...)`, capture the affected position/damage source,
and after the original check whether the block is gone.

Advantages: directly tied to damage, so manual deconstruction can potentially be
excluded by `EnumDamageSourceType` and caller.

Caveats: large, widely used methods; higher Harmony/IL risk; harder to prove that
the final block removal happened inside the callback; likely needs a paired
prefix/finalizer or a postfix on a lower-level setter. Keep as a fallback or a
second-stage improvement.

#### Approach C — local snapshot diff

Periodically snapshot positions/types for the local structure's component buckets
and diff on an update tick.

Advantages: no fragile game hook; easy to reason about; naturally structure-
scoped.

Caveats: adds latency and per-tick cost; needs a reliable list of component
buckets; misses short-lived changes if snapshots are too sparse. Use only as a
fallback if Approach A cannot be made reliable.

### 5.3 Structure association for Approach A

`BlockPosValueList` has a private `cc : ActionLayout` field, but static inspection
shows `SetCC` is only called for one condition list, so `cc` may be null for the
lists we care about.

Recommended probe strategy:

1. Each time the local structure or playfield changes, read the local structure's
   `ConditionDictionary.CopyComponent()` bucket names.
2. For each name, call `OpenImage(name)` and store
   `BlockPosValueList instance -> local structure identity`.
3. Refresh this map occasionally (for example every 1–2 s) and on playfield /
   structure change.
4. In the `Remove` callback, ignore the event if the list instance is not in the
   map.

This avoids scanning every structure and gives a reliable local-ship scope. The
probe must confirm that the list instance that removes turrets/thrusters is in
the local structure's bucket set.

### 5.4 Component classification

The full requirement lists specific component categories. For the PoC, the
simplest defensible rule is:

> A component is a block that participates in one of the ship's device
> aggregates and matches a component-name/class rule.

Initial classifier rules (to be verified against the active RE2 config):

| Category | Default match |
| --- | --- |
| Turret | block name contains `Turret`, or class `TurretAuto` / `TurretManual` |
| Fixed weapon | class `Weapon`, or name contains `Weapon` |
| Generator | class `Generator`, name contains `Generator` |
| Fuel tank | class `FuelTank`, name contains `FuelTank` |
| Warp drive | class `WarpDrive`, name contains `WarpDrive` |
| Thruster | class `Thruster`, name contains `Thruster` |
| Radar / sensors | name contains `Radar` or `Sensor` (RE2 has `RadarTracker` with `IndexName: Radar`) |
| Power unit / converter | name/class containing `Power`, `Converter`, or `Regulator` — needs resolution |
| Ammo controller | name contains `AmmoController`, or `IndexName: AmmoCntr` |

For the very first PoC, “any classified component” is enough; the category only
decides which future voice clip to play. The classifier must be data-driven and
easy to adjust after the probe.

### 5.5 Probe log contract

The probe should log, once per candidate removal:

```text
structureId, structureName, listInstance, bucketName (if resolved),
position, oldBlockType, oldBlockName, category,
voiceInterfacePresent, voiceInterfaceActiveCount,
eventSource (remove / add-remove / damage), damageSource if available
```

It should also log:

- `ConditionDictionary.CopyComponent()` bucket names for the local structure;
- whether `Remove` is called during manual pickup, deconstruction, replacement,
  save load, and playfield transition;
- how many `Remove` calls a single destroyed multi-cell component produces;
- whether `Add` invokes `Remove` on a normal placement/update.

Run at least these destruction cases:

1. turret;
2. fixed weapon;
3. thruster;
4. generator;
5. fuel tank;
6. warp drive;
7. radar;
8. ammo controller;
9. ordinary hull/armor block (should not be a component);
10. manual pickup/deconstruct of a component (should ideally be excluded);
11. block replacement/upgrade at the same position;
12. save/load and playfield transition.

Only after this probe is stable should the permanent hook be written.

---

## 6. Audio playback

### 6.1 Why not just use `ISoundPlayer`

`ISoundPlayer.PlaySound(...)` takes a clip name and the client bridge resolves it
through the game's sound registry. The resolver looks under `Sounds/` and a small
set of prefixes (`Misc`, `Step`, `ImpactSurface`, `DestroyBlock`, `ThrowImpact`).
A WAV that only exists in the mod folder is not automatically in that registry.

Two viable options:

1. **Own AudioSource + WAV loader** — recommended for the PoC. No dependency on
   the game's sound registry, and it works with the supplied custom WAVs.
2. Register the custom clip into the game's internal sound cache and use
   `ISoundPlayer`. More brittle and internal; keep as a fallback only.

The runtime will need a reference to `UnityEngine.AudioModule.dll` in the
runtime `.csproj` in addition to `UnityEngine.CoreModule.dll`.

### 6.2 WAV loader

Implement a small PCM WAV parser:

- RIFF/WAVE header validation;
- PCM format only;
- 8-bit unsigned and 16-bit signed samples;
- mono and stereo;
- honor sample rate and channel count;
- convert to interleaved `float[]` in −1..1;
- create `AudioClip.Create(name, sampleCount, channels, sampleRate, false)`;
- set data with `AudioClip.SetData`;
- cache by absolute file path and file last-write time;
- reject unsupported encodings with one clear log entry.

The supplied `voice_critical_hit.wav` is PCM, 8-bit, mono, 11025 Hz, so it should
be covered by the first implementation.

### 6.3 Player

- One persistent `AudioSource` for the voice channel, created on the Unity main
  thread, `spatialBlend = 0` (2D cockpit alert) by default.
- Optional future alarm channel with 3D position at the Voice Interface or ship.
- `PlayOneShot` for the PoC; a queue only decides which clip is next.
- Volume default ~0.8; configurable later.
- Stop/cleanup on feature disable, playfield unload, mod shutdown, and audio
  device/scene reset.
- Never load/parse a clip inside a per-damage Harmony callback; enqueue a logical
  event and do the work from a main-thread pump.

### 6.4 Audio asset location and licensing

The requested file currently lives under:

```text
third-party/SoundWork/final/voice_critical_hit.wav
```

`third-party/` is reference material and is excluded from source history. The
directory names also indicate MechWarrior 2 audio. **Do not commit or package
those files without explicit license review and user confirmation.**

Recommended PoC default:

- Runtime looks for `RE2Extended/Audio/voice_critical_hit.wav` in the installed
  mod folder.
- The file is not committed. The user supplies it locally, or `build.ps1` copies
  it from a user-supplied `-VoiceAudioSource` path into the ignored staged
  package only.
- If the file is missing, log once and leave the block functional but silent.
- If the user confirms redistribution rights, add an attribution/license notice
  and then consider committing a reviewed asset.

This keeps the implementation usable now without violating the repository's
third-party rules.

---

## 7. Configuration defaults

No in-game UI for the PoC. A small runtime config can be added later. Suggested
defaults:

| Setting | Default |
| --- | --- |
| Feature enabled | `true` |
| Require powered structure | `true` |
| Require active Voice Interface | `true` |
| Local piloted structure only | `true` |
| Voice event queue size | `4` pending VoiceEvents |
| Voice repeat detection | Same event already queued or currently playing |
| Alarm repeat suppression | Skip while the same alarm is already playing |
| Per-position dedupe window | `250 ms` |
| Device detection cache | `500 ms` |
| Voice volume | `0.8` |
| Spatial voice | `false` (2D) |
| Missing clip behaviour | log once, silent |
| Unknown build behaviour | disable voice feature |

---

## 8. Gaps in the requirement and proposed defaults

These are the main ambiguities. The right-hand column is what the PoC will do
unless the user chooses otherwise.

| # | Gap / question | Proposed PoC default |
| --- | --- | --- |
| 1 | What is a “component”? Every block, every device, or the listed functional categories? | Curated component categories (turrets, weapons, thrusters, generators, fuel tanks, warp drives, radar/sensors, power converters, ammo controllers). Hull/armor excluded. |
| 2 | Which vessels? CV, SV, BA, HV? | CV and SV first. BA optional later. HV excluded. |
| 3 | Whose ship triggers the sound? | Local player's current piloted structure only. |
| 4 | Multiplayer: does every client hear every ship, or only their own? | Client-local audio only. Each client hears its own piloted ship. No server replication. |
| 5 | Multiple Voice Interface blocks on one ship? | Any one active device enables the feature; duplicates do not stack. |
| 6 | Must the device be powered? | Yes: structure `IsPowered`, device active, and feature enabled are all required. |
| 7 | “Destroyed” vs picked up/deconstructed/replaced? | Prefer destruction only. If the chosen hook cannot distinguish, document the false-positive limitation and filter later. |
| 8 | Do large multi-cell components fire one event or several? | Deduplicate by structure + position + block type over ~250 ms. |
| 9 | Should the same warning repeat for many simultaneous components? | No. Reject a VoiceEvent if the same event is already queued or currently playing. |
| 10 | Queue limit / drop policy | Maximum 4 pending events; reject the incoming event when full. |
| 11 | Does “Critical Hit” always precede the specific message? | Yes. The thruster event contains Critical Hit, then Jet Engine Destroyed. |
| 12 | Which clips are in the PoC? | `voice_critical_hit.wav`, `voice_jet_engine_destroyed.wav`, and `alarm_critical_damage.wav`. |
| 13 | Are the MW2-derived WAVs licensed for redistribution? | User approved using them. They are staged only into the ignored package, not committed. |
| 14 | 2D cockpit voice or 3D positional sound? | 2D non-spatial voice and alarm for the PoC. |
| 15 | Audio format support | PCM WAV, 8/16-bit, mono/stereo. Reject others with a log message. |
| 16 | What happens if the clip is missing? | Feature stays installed; log once; play nothing. |
| 17 | Block model and aesthetics | `CapacitorMS` for the first safe PoC; replace with a console/computer model after user choice. |
| 18 | Block stats, recipe, market price, research parent | Provisional low-tier electronics recipe and stats; creative placement first. Research parent requires user/RE2 choice. |
| 19 | One block for CV+SV or separate blocks? | One `VoiceInterface` with `MS,SS`; split later only if inventory suffix or balance requires it. |
| 20 | Does the block need signal output as well as on/off input? | No for PoC. It is switchable/active; no signal emitter. |
| 21 | What happens on an unknown/changed game build? | Disable voice feature and log the build/MVID; existing mod features keep their own gates. |
| 22 | Does the dedicated server need any part of this? | No audio. Do not install the audio feature in dedi master mode. |
| 23 | Should the feature work when the ship is not loaded? | No. Local structure must be ready. |
| 24 | Should the feature respect pause/menus? | Audio pauses with the game by default; do not bypass the listener pause in the PoC. |
| 25 | Should users be able to remap sounds/volume? | Not in PoC. Add a small config file after the event path is stable. |
| 26 | What happens if the mod is removed after placing the block? | Same save-compatibility warning as other RE2Extended blocks: back up saves; unknown block may remain. |

---

## 9. Implementation phases

### Phase 0 — Probe and contract (blocking)

- Pin build/MVID/hashes again.
- Add a temporary diagnostic feature flag (or separate probe mod) that installs
  only the candidate `BlockPosValueList.Remove` hook.
- Log the fields listed in section 5.5.
- Run the destruction/manual-removal/replacement/load matrix.
- Decide between Approach A, B, or C.
- Record the verified contract in `docs/NATIVE_FINDINGS.md`.

### Phase 1 — Block and framing

- Add `Configuration/Blocks/VoiceInterface.ecf` and template/localization rows.
- Use the named-block pattern; no fixed block ID.
- Add icon alias/localization entries if the reused model needs them.
- Add a feature registry entry and clean shutdown/unpatch.

### Phase 2 — Audio subsystem

- Add `UnityEngine.AudioModule` reference.
- Implement and test the WAV parser with the local 8-bit mono file.
- Implement the 2D voice player and queue.
- Add missing-file/unsupported-format diagnostics.
- Keep the runtime functional if audio init fails.

### Phase 3 — Destruction hook

- Implement the chosen detection path with exact type/signature resolution.
- Add local-structure association and active-device gate.
- Add dedupe/suppression and a main-thread event pump.
- Ensure no exception escapes a Harmony callback.
- Do not run the hook in dedi master.

### Phase 4 — PoC verification

- Creative-mode placement and control-panel toggle.
- Destroy each target component category; confirm exactly one Critical Hit.
- Confirm no sound when device is off/unpowered/missing.
- Confirm no sound for remote/AI ships or hull-only blocks.
- Confirm manual pickup/deconstruction behaviour is known and documented.
- Confirm save/load, playfield transition, and mod shutdown do not produce
  spurious sounds or leaked audio objects.

### Phase 5 — Full voice/alarm suite (later)

- Add the queue-backed verbal clips for turret/weapon/internal explosion/jet
  engine/sensors/power/ammo.
- Add the alarm channel with same-alarm suppression.
- Add fuel/heat/shield-lost warnings.
- Add category-specific priority and queue tuning.

---

## 10. Test plan

### Functional

- Place block on CV and SV; toggle in control panel; toggle by signal.
- Active + powered: Critical Hit plays.
- Off / unpowered / structure not ready: no sound.
- No block: no sound.
- Two blocks: no double playback.
- Remove block while sound is queued: stop/clear the queue as configured.

### Destruction matrix

- Turret, weapon, thruster, generator, fuel tank, warp drive, radar, ammo
  controller, power converter.
- Projectile, explosion, collision, and player weapon damage where feasible.
- Manual pickup and manual deconstruction.
- Replacement/upgrade at the same position.
- Multi-cell component.
- Several components destroyed by one explosion.
- Hull/armor block destruction (should not trigger unless deliberately added).
- AI/remote ship destruction (should not trigger).

### Lifecycle

- Save/load with the block placed.
- Playfield change.
- Vessel change/cockpit exit.
- Mod disable/uninstall (backup save first).
- Repeated enable/disable does not leak AudioSources or queue entries.

### Multiplayer

- Single-player first.
- Dedicated server + one client: client-local sound only.
- Two clients with mod: each hears only their own ship.
- One modded and one unmodded client: no requirement for a handshake in the PoC;
  document that unmodded clients simply do not hear the feature.

---

## 11. Risks

| Risk | Mitigation |
| --- | --- |
| `BlockPosValueList.Remove` is not the destruction path for all components | Probe first; fall back to damage-path hook or snapshot diff. |
| Hook fires for deconstruction/pickup | Probe and document; filter by damage source if possible; otherwise explicitly accept for PoC. |
| Duplicate events from multi-cell blocks/explosions | Per-position and per-clip dedupe windows. |
| Audio files are third-party/copyrighted | Do not commit/package without license review; load user-supplied files. |
| Harmony IL problems on obfuscated methods | Use the existing field-type-corrected Harmony and prefer a small method signature. |
| Audio init on non-main thread | Do all Unity object/audio work from the main-thread pump. |
| Block model/config validation failure | Use the proven ECF merge and explicit properties; start from a known model. |
| Performance from per-damage callbacks | Event-driven hook, cached device state, no asset I/O inside callbacks. |
| Dedicated server accidentally loads audio code | Mode gate and feature gate; no audio objects in dedi master. |

---

## 12. Decisions needed from the user

1. **Audio licensing**: may the MW2-derived WAVs be redistributed, or should the
   mod only load a user-supplied local file?
2. **PoC sound set**: Critical Hit only, or all six supplied clips?
3. **Vessel scope**: CV/SV only, or also BA (and explicitly not HV)?
4. **Block model**: keep `CapacitorMS` for the PoC, or choose a console/computer
   model now?
5. **Manual removal**: should picking up or deconstructing a component play the
   alert? Default is no, if detectable.
6. **Audio style**: cockpit 2D voice, or positional 3D at the Voice Interface?
7. **Block progression**: creative-only PoC, or should the recipe/research entry
   be finished in the first pass?
8. **Queue behaviour**: maximum 4 events and reject duplicate event IDs while playing or queued? Default yes.
