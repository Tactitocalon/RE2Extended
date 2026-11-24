# RE2Extended

A Reforged Eden 2 extension mod for Empyrion 1.20.1 build 5232.

## Current feature: Cruise Amplifier

Adds class-less, powered, switchable **Cruise Amplifier** devices for CV and SV.
The devices use the `CapacitorMS` model and a 2x1x1 hitbox, raise the ship's
maximum speed in tiers, and disable the boost while the ship is in combat or has
an active shield.

| Vessel | Environment | 1 | 2 | 3 | 4 |
| --- | --- | ---: | ---: | ---: | ---: |
| CV | Space | 115 | 130 | 145 | 160 |
| CV | Atmosphere | 65 | 70 | 75 | 80 |
| SV | Space | 160 | 200 | — | — |
| SV | Atmosphere | 80 | 100 | — | — |

The amplifier must be powered and active. Signals/control-panel switching use the
same activateable behaviour as the RE2 decorative wall console.

## Current feature: Voice Interface (PoC)

Adds a switchable `VoiceInterface` signal device using the `LCDProjector` model.
While the device is active on the local player's powered structure, destroying a
recognized component triggers its VoiceEvent:

| Component | VoiceEvent | Sounds |
| --- | --- | --- |
| Thruster | `thruster_destroyed` | Critical Hit, Jet Engine Destroyed |
| Turret / SentryGun | `turret_unit_destroyed` | Critical Hit, Turret Unit Destroyed |
| Fixed weapon | `weapon_destroyed` | Critical Hit, Weapon Destroyed |
| Power generator | `power_unit_destroyed` | Critical Hit, Power Unit Destroyed |

Each event also triggers the Critical Damage alarm, which plays immediately
unless that alarm is already playing. The voice queue holds at most 4 pending
events. The same event is rejected while it is already queued or currently
playing. The alarm channel has no queue. The PoC is intentionally naive and
client-local; see [`docs/VOICE_INTERFACE_PLAN.md`](docs/VOICE_INTERFACE_PLAN.md).

The staged package includes the five voice WAVs and
`alarm_critical_damage.wav`, copied from the local third-party SoundWork folder
by `build.ps1` into the ignored package. The WAVs are not committed to source
history.

## Install

1. Build the mod with `build.ps1`. Pass `-GameRoot` if Empyrion is installed
   somewhere other than the default path.
2. Copy the generated `package/RE2Extended` folder into:

```text
<Empyrion>\Content\Mods
```

3. Start Empyrion normally through Steam (app ID `383120`).
4. The mod targets `PfServer,Dedi,Client`; the client process owns the
   client-authoritative speed patch.

### Block-ID upgrades

Named blocks are assigned IDs by the game and persisted per save in
`blocksmap.dat`. Existing IDs must never be renumbered.

RE2Extended merges its ECF fragments before the game's named-block ID
assignment and pre-seeds its named blocks from the top of the named-ID range
(`4095` downward). This keeps RE2Extended IDs independent of RE2's low-to-high
growth and avoids relying on `blocksmap.dat` being rewritten.

The allocator is resolved by signature in both PlayfieldServer and Client, so
both sides assign the same high IDs when they run the same mod and scenario
versions. Existing mapped IDs are retained. If the high named-ID range is
occupied differently, allocation can diverge or fail closed.

Migration note: blocks placed before the high-ID scheme used low named IDs.
Those world block IDs are not migrated automatically. Treat the first high-ID
release as a save migration point for test structures, or add a world-ID
migration step before enabling it on a save with placed RE2Extended blocks.

Release checks:

- Verify the new map count and appended IDs with
  `scripts/verify-blocksmap.ps1` on a copy of the save.
- Confirm a second restart does not add duplicate entries.
- Confirm old blueprints and placed builds still load.
- If the early merge is reported unavailable in the log, do not assume an
existing save will receive new named IDs.

Localization for the Cruise Amplifier ships as
`Configuration/Localization.csv` and is merged through the game's native
`Localization.LoadCSV(..., merge: true)` path. Inventory/build-menu names use
invisible `CruiseAmplifierCVBlocks` / `CruiseAmplifierSVBlocks` groups, matching
the vanilla thruster pattern: `Cruise Amplifier (CV)` / `(SV)` in inventory,
`Cruise Amplifier` when placed. Non-English columns are tracked in
[`TODO.md`](TODO.md).

## Planned / documented

- `docs/DEVICE_DETECTION_PATTERN.md`
- `docs/IMPLEMENTATION_PLAN.md`
- `docs/VOICE_INTERFACE_PLAN.md`
- `docs/MULTIPLAYER_VOICE_PLAN.md`
- `docs/BLOCK_DEFINITION_CHECKLIST.md`
- `docs/DEVICE_CLASSES.md`
