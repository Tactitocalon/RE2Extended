# Native Findings

Living evidence log for Empyrion managed-code reverse engineering.

Rules for this file:

- Separate **static evidence** from **runtime-verified evidence**.
- Include build hashes and target (`client` or `dedi`) for every finding.
- Do not paste large decompiled methods or game binaries.
- Treat metadata tokens as build-specific locators.
- When a finding is superseded by a new build, keep it under an old-build heading instead of silently overwriting it.

Current baseline: **Empyrion 1.20.1 build 5232**

## Grid / vessel terminology (reused by later features)

Use these mappings consistently in UI, probes and future grid-aware features. The
same concepts appear under several different native names.

| Player/UI name | Native `AllowPlacingAt` | Native `ClientScope` bit | Template `Target` token |
| --- | --- | ---: | --- |
| BA (base) | `Base` | `2` | `BaseC` |
| CV (capital vessel) | `MS` | `8` | `LargeC` |
| SV (small vessel) | `SS` | `4` | `SmallC` |
| HV (hover vessel) | `GV` | `16` | `HoverC` |
| terrain | `Terrain` | `1` | n/a |
| unknown/none | `Unknown` | `0` | n/a |

Important distinctions:

- `MS` means **capital vessel / CV**, not “medium ship”. `SS` means **small
  vessel / SV**, and `GV` means **hover vessel / HV**.
- UI `BA` corresponds to config token `Base`, not a literal `BA` token.
- `ClientScope` is a placement/grid bitmask, not a category bitmask. It is parsed
  from `AllowPlacingAt` by `EmulatorTokenConnectionToken.ClearDialog` using
  `ActionLayout.ResolveBookmark`. The decoded token table lives in
  `ActionLayout.colorInitialized`.
- Constructor **template targets** (`BaseC`, `SmallC`, `LargeC`, `AdvC`,
  `HoverC`, `SurvC`, `Furn`, `FoodP`, `Decon`, `SuitC`) are a separate namespace
  from grid/placement tokens. Do not compare them directly to `AllowPlacingAt`.
- Non-block items have `ClientScope == 0`; the native constructor template
  browser ignores grid filters for those items.
- Named blocks can permit multiple grids. Always read the full mask/list, e.g.
  `Base,MS` = BA + CV, rather than assuming one grid per block.

---

## Build 5232

### Targets

| Target | Assembly | SHA-256 | MVID |
| --- | --- | --- | --- |
| Client | `Assembly-CSharp.dll` | `28E43D961A14A636617747B8734E6EC89F47EA6E57A2EE3F31533237BF468369` | `462483cd-2f1f-4a24-9c83-46d24b1cb3f4` |
| Dedicated | `Assembly-CSharp.dll` | `3E88562650F63267D665CF8BE25E224D9B2D376DCF419C71E98F0464FF55C19E` | `fae3f936-680a-459b-99d0-6fe468a1f17f` |

The client and dedicated assemblies are different targets. `Assembly-CSharp.Disk` exists in both, but the dedicated version is an unrelated network helper.

---

## Player ship top speed

Status: **runtime-verified for the CV SaveView speed patch; 300-limit feature confirmed by user.**

### Candidate call chain

```text
RbCtrlPlayerShip.FixedUpdate()
  -> Disk.PublishProject()
       -> Disk.SplitProcess()
  -> result passed to RbCtrlPlayerShip.ViewDeployment(...)
```

### Client symbols

| Symbol | Token |
| --- | --- |
| `Assembly-CSharp.Disk` | `0x020008F3` |
| `Assembly-CSharp.Disk::PublishProject()` | `0x06004867` |
| `Assembly-CSharp.Disk::SplitProcess()` | `0x06004881` |
| `RbCtrlPlayerShip` | `0x020008BF` |
| `RbCtrlPlayerShip::FixedUpdate()` | `0x06004641` |
| `RbCtrlPlayerShip::ViewDeployment(...)` | `0x0600467C` |
| `World::get_CloseConfig()` | `0x06005392` |

### Why this is a strong candidate

- `Disk.PublishProject()` returns `System.Single`.
- In the client assembly its only call site is `RbCtrlPlayerShip.FixedUpdate()`.
- The returned float is passed into `RbCtrlPlayerShip.ViewDeployment(...)` as the maximum-speed argument.
- `Disk.PublishProject()` falls through to `Disk.SplitProcess()`.
- The value appears to be recalculated during play, so this is not a saved-stat edit.

`Disk.SplitProcess()` branches on:

- `Disk.firstMessage`, a static bool initialized to `true` at startup
- whether the playfield is space via `World.get_CloseConfig()`
- the vessel/entity implementation: `DeviceCollection`, `ActionList`, or another type

Observed values from the current method body:

| firstMessage | type | space | atmosphere |
| --- | --- | ---: | ---: |
| true | `DeviceCollection` | 3 | `valuesInstance` (60 default) |
| true | `ActionList` | `uriToken` (120 default) | `previousFont` (70 default) |
| true | other | `childMessage` (160 default) | `categoryMap` (80 default) |
| false | `DeviceCollection` | 3 | 40 |
| false | `ActionList` | 92 | 40 |
| false | other | 115 | 50 |

`Disk.PublishProject()` may also fall through to `Disk.SaveView()` when its configuration gate is not ready. `SaveView()` values are: `DeviceCollection` 3/50, `ActionList` 100/60, other 130/70, in space/atmosphere order.

`DeviceCollection` is confirmed to be the HV type because `HVEngineConfig` stores and operates on a `DeviceCollection` reference. CV/SV mapping between `ActionList` and the remaining player-vessel type is inferred, not yet runtime-verified.

Runtime evidence: the CV UI showed a limit of **60**, which matches the `SaveView()` / `ActionList` / atmosphere value. This strongly suggests the active CV path is:

```text
Disk.SplitProcess()
  -> FormatConfig() == false
  -> SaveView() == 60
```

Changing the `uriToken` / `previousFont` / `childMessage` / `categoryMap` fields does not affect that branch. The field-polling prototype therefore did not change the UI or the actual speed limit.

### Likely patch point

A Harmony postfix on `Disk.PublishProject()` that modifies `__result` is narrower than patching `Disk.SplitProcess()`.

Do not patch `SplitProcess()` first. It has multiple callers, including other game systems. `PublishProject()` is currently a single-caller path from the player ship controller.

### Runtime type-name pitfall

The native `EntityType` and `World` types are in the global namespace in this image (`Type.FullName` is `EntityType` / `World`, not `Eleon.Modding.EntityType` / `Assembly-CSharp.World`). Resolve these fields by type name/ownership rather than assuming a namespace.

### Test implementation and experiment objective

Implemented at `mods/EmpyrionExtendedTestMod`:

The current runtime uses a narrow method patch rather than the earlier
state-mutation polling prototype. The experiment objective is documented in
`mods/EmpyrionExtendedTestMod/docs/EXPERIMENT.md`.

Current evidence:

- Harmony 2.3.3 postfix on `PublishProject()` generated invalid IL on this Mono runtime
- Harmony 2.4.3 Fat produced the same invalid IL for `PublishProject()`
- standard Harmony 2.4.3 also generated invalid IL for `Disk.SaveView()`
- field polling targets the wrong active branch (`SaveView()`), so it did not change the 60 limit
- the likely cause is same-name/different-type fields in `Disk` being resolved incorrectly during Harmony/MonoMod IL copy
- a locally field-type-corrected Harmony 2.4.3 successfully installed a postfix on `Disk.SaveView()`
- the successful smoke test logged `CV SaveView: class=ActionList space=False current=60 target=70` and applied 70
- the current feature-test build sets CV `SaveView()` to **300** (increase-only)
- v0.6.0 adds a `GigaSpeedBooster` block; when detected on the local CV the
  postfix target becomes **600** instead of 300
- the intended design remains a narrow method patch with no continuous gameplay-state writes

### Hook backend clue from a third-party reference mod

A third-party InventoryLock reference mod successfully patches game UI code on this runtime using:

- **Harmony 2.4.3.0 Fat / net452** rather than 2.3.3
- its own `0Harmony.dll` resolved through `AppDomain.AssemblyResolve`
- a prefix returning `bool` to allow/suppress the original method
- `ref bool __state` plus a finalizer to restore temporary state
- `Application.Update` polling only for visual overlay work, not the core decision
- type/member discovery by name and shape, with clear failure logs
- `UnpatchAll` on shutdown

This does not prove `PublishProject()` is patchable, but it shows Harmony can patch a game method on this runtime. InventoryLock patches NGUI code, not an `Assembly-CSharp.Disk` method with duplicate field names, so it does not exercise the suspected field-resolution problem. The successful `Disk.SaveView()` test with the corrected Harmony supports the duplicate-field hypothesis and makes `PublishProject()` a good next retest target for a cleaner final patch point.

### Rejected or lower-confidence lookalikes

| Symbol/field | Why it is not the first choice |
| --- | --- |
| `MaxSpeedSystemConfig` | Debug/display component; not the authoritative player movement limit. |
| `FixedMaxSpeed` on vessel descriptors/drone setup | Primarily AI/scenario vessel configuration. |
| `CruiseModeShipController` | A different controller path; not the same as the player ship `Disk.PublishProject` route. |
| `ThrusterRCSConfig` | RCS/visual/config behavior; not the primary speed cap. |

### Final experiment result

- Standard Harmony failed on `Disk.SaveView()` with invalid IL.
- Field-type-corrected Harmony 2.4.3 installed the `SaveView()` postfix successfully.
- The successful log showed `class=ActionList`, atmosphere, `current=60`, `target=70`.
- The postfix changed the returned limit and the user confirmed the actual speed limit changed.
- The feature build now sets CV `SaveView()` to **300** and the user confirmed it works.

This validates the field-type hypothesis for this class of patch.

### Remaining questions

- [ ] Does `Disk.PublishProject()` work with the corrected Harmony and make a cleaner final target?
- [ ] Is the normal `FormatConfig() == true` branch covered, or does it need a separate patch?
- [ ] Should the patch be scoped to the local player's CV only?
- [ ] Are AI CVs, SV/HV and remote vessels unaffected in normal play?
- [ ] Does space behave the same as atmosphere?
- [ ] Does high speed cause collision, landing, docking or playfield-transition problems?
- [ ] Does the dedicated server accept/correct the movement?
- [ ] What happens with multiple clients and only some modded?

---

## Custom block/device injection (v0.6.0 prototype)

Status: **statically implemented and built; runtime verification pending.**

Goal: add a new CV device (`GigaSpeedBooster`) without editing the game or
Reforged Eden 2 files, and let it raise the CV speed decision from 300 to 600.

### Mod/config loading finding

`Content/Mods` mods are loaded by `ModManager`/`GroupLayout` as DLL-only mods.
There is no automatic provider for `Content/Mods/<mod>/Configuration/*.ecf`.
A mod folder's `Configuration` tree is not scanned.

Build 5232 does contain one native config override: `DirectoryFactory.RemoveSymbol(name, ...)`
checks for `Content/Mods/<name>.ecf` and, when there is no active scenario config
path, loads that file as the **entire** named config via
`new ConfigFile("Content/Mods", name, ...)`. All normal configs (including
`BlocksConfig`, `ItemsConfig`, `Templates`) are dispatched through this method by
`FunctionSettingsConnectionToken.CloseXmlFile`. It is not a fragment merge:
`EcfFile` has no include/append directive. When a scenario `Content` path is
active (the normal RE2 gameplay case), the scenario configuration is used and
the `Content/Mods/<name>.ecf` override is skipped. It therefore cannot extend
RE2 with a few extra blocks.

RE2Extended implementation: Harmony-postfix `ConfigFile.Load(string,string,...)`
and merge the mod's block/template fragments when the named config is
`BlocksConfig` or `Templates`. This is the single named-config boundary and it
runs after the ECF/binary loader has populated `PropertiesCollection`, but before
the game dispatches the config. A `DynamicPropertiesCollection` postfix
(`ReadEcf`, `LoadBinary`, `Read`) remains as a fallback for builds where
`ConfigFile.Load` cannot be resolved. The merge appends parsed elements and calls
`ApplyAllReferences()` so `Ref` inheritance is resolved. This happens in memory
only.

### Block ID / Reforged Eden 2 compatibility

RE2's `BlocksConfig.ecf` uses most fixed block IDs in 1..2048 and adds 1,493+
named blocks whose current auto-assigned IDs occupy 2048..3563 (verified from an
RE2 save `blocksmap.dat`). The booster therefore uses **fixed `Block Id: 1565`**.
1565 is unused by vanilla, all installed built-in scenarios, and the installed
RE2 config, and it is below the RE2 auto-assignment range.

This is a prototype reservation. It is still a gap in the vanilla ID space, so a
future vanilla/RE2 update could theoretically claim it. If that happens, change
the ECF `Block Id` and the runtime `BoosterBlockId` constant together.

Base block: `LightMS01` via `Ref`, restricted to `AllowPlacingAt: "MS"` (CV).
Choosing a Light-class device gives the prototype an available public detection
path: `IStructure.GetDevice<IDevice>(name)`, with a fallback that reads
`IStructure.GetDevices(DeviceTypeName.Light)`, checks each position with
`IBlock.Get(out type, ...)`, and compares against the fixed ID / block mapping.

### First live test failure and fix

The first v0.7.0 live load injected the blocks but failed during block validation:

```text
System.Exception: Block 1565 has missing/empty material
```

`Ref: LightMS01` / `Ref: ContainerMS01` were not resolved before the game validated
the injected elements, causing a cascade of null-reference and unknown-block errors.
The merger now applies existing references first and then copies the base element's
`DynamicProperties` directly into each injected element with `CopyFrom(..., false)`.
Block injection fails closed if the base block or `Ref` field is missing.

Second live test: the safety check skipped both blocks because the parsed fragment's
`Element.Reference` field was empty even though the ECF contained `Ref:`. The merger
now has an explicit block-name -> base-block map (`GigaSpeedBooster -> LightMS01`,
`InfiniteFuelGenerator -> ContainerMS01`) and uses the ECF `Reference` only as a
fallback. This avoids depending on parser behavior for fragment `Ref` handling.

### Inventory name vs placed block name (build 5232, static)

Vanilla thrusters do not derive the ship-type suffix from `AllowPlacingAt` at
runtime. They use two block entries:

- An invisible `...Blocks` group with `ChildBlocks: "..."`. This is the
  inventory/build-menu item and carries the long display key, e.g.
  `ThrusterMSMediumT2Blocks = "Medium Plasma Thrusters (CV)"`.
- The actual placeable child block, whose `TemplateRoot` points at the group and
  whose localization key is the short placed name, e.g.
  `ThrusterMSMediumT2 = "Medium Plasma Thruster"`.

`RegistryOptions.Init()` loads `TemplateRoot` into `generatorLength` and
`ChildBlocks` into the child list; the inventory/build UI and placement resolve
the group to its child block. No display-name UI patch is required.

RE2Extended uses the same pattern with `CruiseAmplifierCVBlocks` /
`CruiseAmplifierSVBlocks` as single-child groups. The group keys carry `(CV)` /
`(SV)`; the actual `CruiseAmplifierCV` / `CruiseAmplifierSV` keys stay
`Cruise Amplifier` for the placed block.

Icons are separate: inventory/build entries use `CustomIcon`, but the control
panel path `BuilderContextConnectionToken.AllocateEmulator()` assigns
`UISprite.spriteName = registryOptions.CloneOutline()` directly. For custom
blocks that sprite name has no atlas entry. RE2Extended therefore sets
`CustomIcon: CapacitorMS` for the Cruise Amplifiers and
`CustomIcon: TurretEnemyBlack` for the Ship Alert System, and installs a narrow
`UISprite.spriteName` setter prefix that aliases those block and group names to
the matching icon.

### Localization loading (build 5232, static)

The base localization is `Content/Extras/Localization.csv`, read by
`Localization.loadFunction` from `NGUIAssembly.Localization`. The game sets that
delegate in `GameStartup`/`GameLoader`. A scenario may supply
`Extras/Localization.csv`; `DiskEditor.FindResource()` finds it and
`FunctionSettingsConnectionToken.CloseXmlFile()` merges it with
`Localization.LoadCSV(bytes, merge: true)`.

There is no native `Content/Mods` localization provider. For a DLL mod the
closest native merge path is to ship a CSV fragment and call the public
`Localization.LoadCSV(byte[], merge: true)` after the base table has loaded.
`Localization.Set(key, value)` is a lower-level, language-independent fallback:
it writes to `mOldDictionary` and does not broadcast `OnLocalize`. It works for
English-only prototypes but is not a localization-table merge.

The dedicated `Assembly-CSharp` has the config/ECF types but no `Localization`
type; localization is a client concern.

RE2Extended now ships `Configuration/Localization.csv` and merges it through
`Localization.LoadCSV(byte[], merge: true)` after the base dictionary is ready.
`Localization.Set` remains only as a fallback for an unavailable CSV/table.

### Runtime verification still needed

- Does the game consume the ECF through a patched collection loader, or from a
  prebuilt binary/asset that bypasses these methods?
- Does fixed block ID 1565 appear in the CV build menu and remain stable?
- Does `GetDevice(name)` find a renamed `LightMS01` clone?
- Does the block remain stable across save/restart and when RE2 updates?

### Tooling/workflow issues observed

- `ilspycmd -m <token>` and `-m <doc-id>` can emit the whole decompiled assembly
  for some obfuscated `Assembly-CSharp` types/methods instead of the requested
  method. `-t <type>` plus targeted `grep`/IL inspection was more reliable.
- `ilspycmd -t <namespace.Enum>` failed for `Eleon.Modding.DeviceTypeName`;
  `-t DeviceTypeName` worked.
- The ModApi project needs an explicit `UnityEngine.CoreModule` reference for
  generic `IStructure.GetDevice<T>` calls; referencing only `ModApi.dll` fails
  with CS0012 mentioning `Vector3`.
- `Content/Mods` config loading is not documented in the included Modding Doc;
  the DLL-only behavior had to be inferred from `ModManager`, `GroupLayout`, and
  `DirectoryFactory`.

---

## Voice Interface PoC: thruster destruction audio (build 5232)

Status: **statically implemented and built; runtime verification pending.**

Goal: when a thruster is removed on the local player's structure and an active
`VoiceInterface` block is present, play the local Critical Hit WAV.

### Detection candidate

The PoC uses the device aggregate rather than a whole-structure scan. It installs
a Harmony prefix on:

```text
BlockPosValueList.Remove(Vector3i)      token 0x06004D0F
BlockPosValueList                       type  0x020009AD
BlockPosValueList.GetBlockPosValue(...) token 0x06004D17
ConditionDictionary.OpenImage(string)   token 0x0600501E
```

Before the removal, `GetBlockPosValue` supplies the old `RegistryToken`.
`ControlTable.get_JoinDrive()` gives the old block type, which is mapped to a
block name through `IApplication.GetBlockAndItemMapping()`. A name containing
`Thruster` is the PoC trigger.

The hook is limited to the local player's structure by collecting the
`BlockPosValueList` instances reachable from that structure's
`ConditionDictionary.CopyComponent()` bucket names and requiring the removing
list instance to match one of them.

### Gating

- known client assembly and client/single-player mode;
- local player and current structure present;
- structure ready and powered;
- at least one active `VoiceInterface` in the `RE2Extended` device bucket;
- removed old block type maps to a `Thruster` block.

### Audio

The PoC does not use `ISoundPlayer` for the custom clip. `ISoundPlayer` resolves
clip names through the game's `Sounds/` resource registry, which does not include
a WAV shipped only in the mod folder. Instead the runtime uses:

```text
RE2Extended/Audio/voice_critical_hit.wav
RE2Extended/Audio/voice_jet_engine_destroyed.wav
RE2Extended/Audio/voice_turret_unit_destroyed.wav
RE2Extended/Audio/voice_weapon_destroyed.wav
RE2Extended/Audio/voice_power_unit_destroyed.wav
RE2Extended/Audio/alarm_critical_damage.wav
RE2Extended/Audio/alarm_no_shield.wav
RE2Extended/Audio/alarm_no_fuel.wav
  -> minimal PCM WAV loader (per-path cache)
  -> AudioClip.Create + SetData
  -> verbal channel: VoiceEvent queue (max 4 events) + private 2D AudioSource
  -> alarm channel: immediate playback, one AudioSource per alarm id
```

A `VoiceEvent` has an event ID and an ordered list of sounds. The current
events are:

| Trigger | Event ID | Sequence |
| --- | --- | --- |
| Thruster block removed | `thruster_destroyed` | Critical Hit, Jet Engine Destroyed |
| Turret/SentryGun block removed | `turret_unit_destroyed` | Critical Hit, Turret Unit Destroyed |
| Fixed weapon block removed | `weapon_destroyed` | Critical Hit, Weapon Destroyed |
| Power generator block removed | `power_unit_destroyed` | Critical Hit, Power Unit Destroyed |

Classification is name-based for the PoC. Turret matching excludes `TurretRadar`
and `TurretAlarm`; weapon matching excludes weapon lockers/racks; generator
matching excludes shield, gravity, oxygen, water, and control-field generators.
An event is rejected if the same event ID is already pending or currently
playing. Accepted events play their sounds in sequence. The alarm channel has no
queue; the Critical Damage alarm plays immediately unless already playing.

The No Shield Alarm is separate from component destruction. One generic 333 ms
structure-alert tick reads only the local player's current structure:

```text
IsShieldActive && ShieldLevel <= 0
```

`IsShieldActive` is the "has shields and switched on" gate because the native
state only leaves `Off` when a shield generator is present and enabled. The
alarm is edge-triggered: arm on a positive shield level, fire once at zero,
then re-arm only after the level rises above zero again.

The same tick checks Pentaxid fuel:

```text
IStructure.PentaxidTank   (null when the structure has no such tank)
  -> IStructureTank.Content
```

On build 5232, `StructureBridge.PentaxidTank` returns an `ActivityService` whose
`Content` aggregates the Pentaxid tank inventory. If no tank is present the
fallback is `NetworkSetFunctionService.keywordsID`, a static field that is never
assigned in the client assembly, so the tank is null and the check fails closed.
The fuel alarm arms after observing a positive content value and fires once when
the content reaches zero. It does not fire for a tank that is already empty on
entry/load. `UsesIntegerAmounts` is true, so a small epsilon is sufficient for
the float `Content` value.

The active `VoiceInterface` bucket scan is performed only when an alarm is about
to fire, so the steady-state poll is just a few structure property reads. No
per-ship, per-device, or per-interface pollers are created, and only the local
piloted structure is monitored.

`build.ps1` copies the requested third-party WAVs from
`third-party/SoundWork/final/` into the ignored staged package. No third-party
audio is committed to source history.

### Known PoC limitations

- `BlockPosValueList.Remove` has not yet been runtime-verified for actual
  thruster destruction.
- Manual pickup/deconstruction may also remove the aggregate entry and therefore
  may also play the clip. This is accepted for the naive PoC until the probe
  distinguishes damage from deconstruction.
- `BlockPosValueList.Add` calls `Remove` internally for duplicate positions, so a
  placement/replacement edge case may produce a false trigger.
- A small per-frame queue pump checks whether the current clip has finished.
  Shield state uses one 333 ms local-structure alert tick; the same tick checks
  Pentaxid tank content. There is no block/world polling. VoiceEvent repeat
  detection and same-alarm repeat suppression are implemented; general event
  cooldowns are not.
- No dedicated-server behavior is installed; audio is client-local only.

### Files

```text
mods/RE2Extended/Configuration/Blocks/VoiceInterface.ecf
mods/RE2Extended/Configuration/Templates/VoiceInterface.ecf
mods/RE2Extended/src/RE2Extended.Runtime/VoiceInterfaceFeature.cs
mods/RE2Extended/src/RE2Extended.Runtime/VoiceAudio.cs
mods/RE2Extended/docs/VOICE_INTERFACE_PLAN.md
```

### Runtime verification still needed

- Does the `Remove` hook fire when a thruster is destroyed by damage?
- Does the old block type/name resolve to a `Thruster`?
- Does the local-structure bucket-instance mapping succeed?
- Does playback of the staged 8-bit mono WAV work?
- Does the shield alert fire once on a positive-to-zero transition and stay
  silent for shieldless ships, switched-off shields, and Voice Interface off or
  unpowered?
- Does `IsShieldActive` remain false for a ship without a shield generator?
- Is `PentaxidTank` non-null on a ship with a tank and null on a ship without
  one?
- Does `PentaxidTank.Content` decrease during warp fuel consumption and reach
  exactly zero when empty?
- Does the fuel alert fire once on the positive-to-zero transition and stay
  silent on entry/load at zero?
- Does manual pickup/deconstruction also trigger, as expected from the naive
  design?

---

## Reforged Eden 2 reference data (build 5232)

Status: **located and hashed; static reference verified.**

Third-party data note: this section records derived interoperability facts
(hashes, Workshop IDs, item/block names and IDs) from the third-party Reforged
Eden 2 mod. It is not a copy of RE2 source or assets and must not be
redistributed as RE2 content; review RE2's license before reusing anything
beyond these reference facts.

Reforged Eden 2 is installed as a Steam Workshop mod, not under the game
installation:

```text
F:\SteamLibrary\steamapps\workshop\content\383120\3143225812
```

`383120` is Empyrion's Steam app ID; `3143225812` is the RE2 Workshop mod ID.
Use this tree for RE2 item names, block definitions, templates, research data,
and localization. Re-hash before relying on it.

| File | SHA-256 |
| --- | --- |
| `Content/Configuration/BlocksConfig.ecf` | `2fa9dd399c4d17cb987cb9eceb128483860290eb523fbd272b78c0c26cd75b6e` |
| `Content/Configuration/ItemsConfig.ecf` | `15b603bcf138349ee7c6200f3b3653ab6782dace05f029ae6a683d4ac8eab39e` |
| `Content/Configuration/Templates.ecf` | `ac3982c46e98da7690648b7a8f033f14454b271b6f27b8b9c3dc1e1249ad950e` |
| `Extras/Localization.csv` | `f44f20010d08d17f62230304d23f6f732e071e341faf0835fe2a2a7e302cfea2` |

RE2's own block-ID comments currently list `1449-1452`, `1519-1526`, and
`1562-1565` as available. Ids **1566/1567 are already used** by RE2
(`BrainFungusHarvested`, `WoodLargeDestroyedBlocks`). The test mod uses
1564/1565, so the next mod should not reuse those either.

### Block-ID space

Build 5232's block-ID model:

| Range | Slots | Use |
| --- | ---: | --- |
| `0..255` | 256 | Terrain/texture reserved; do not use for devices. |
| `256..2047` | 1792 | Fixed `Block Id:` range. |
| `2048..4095` | 2048 | `Block Name:` auto-assigned in config order. |

Total addressable range: **0..4095**. RE2 currently has **1896 distinct fixed
IDs** occupied across vanilla + RE2 + installed scenarios, leaving 33 free IDs
in `256..2047`:

```text
783-794, 845-851, 873-874, 1050-1051, 1521-1526, 1562-1565
```

RE2 currently contains **1517 `Block Name:` entries**, occupying named IDs
**2048..3563** in a current save. That leaves about **532 named slots**
(`3564..4095`). Named IDs are assigned in config order and persisted in a save's
`blocksmap.dat`; new entries append rather than renumbering old ones.

### Block-map load/ID-assignment lifecycle (build 5232)

Static inspection of the PlayfieldServer `Assembly-CSharp` found the block-name
map implementation in `Assembly-CSharp.ProcessTablePageQueue`:

- `RegisterAction(dir, file)` loads the save's `blocksmap.dat`.
- `FreeAddin(start, end, DynamicPropertiesCollection)` assigns IDs for named
  elements in the supplied config range. The block path calls this with
  `start=2048`, `end=8192`.
- `SearchToolbar(...)` / `UpdateDirectory()` persist the map when it is dirty.

`Assembly-CSharp.DirectoryConverter.CopyXmlFile(DynamicPropertiesCollection,
bool)` is the point that calls `FreeAddin(2048, 8192, config)` before the map is
written. Injecting ECF fragments after this point is too late for an existing
save to receive new named IDs. RE2Extended therefore installs a Harmony prefix
on that method and merges the block fragments into the collection immediately
before ID assignment. In-memory ID assignment now works and blueprint spawning
with the injected blocks succeeds. The test utility
`mods/RE2Extended/scripts/verify-blocksmap.ps1` parses a map and reports the
record count, maximum ID, and required names.

Live test caveat (2026-11-24): the early hook fired on an existing dedicated
save, but the save's `blocksmap.dat` was not rewritten after playfield unload; it
still contained the pre-mod 1517 entries. The in-memory mapping worked.

To remove the dependency on map persistence and config order, RE2Extended also
hooks the named-ID allocator itself. The allocator has the same effective shape
in both full assemblies but different obfuscated names:

| Process | Type | Allocator |
| --- | --- | --- |
| PlayfieldServer | `Assembly-CSharp.ProcessTablePageQueue` | `FreeAddin(int,int,DynamicPropertiesCollection)` |
| Client | `Assembly-CSharp.EmulatorSetConnectionToken` | `DeployXmlFile(int,int,DynamicPropertiesCollection)` |

It is resolved by signature, not by name. The hook pre-seeds RE2Extended block
names from the highest named ID (`4095`) downward, using the map's own
name→ID/ID→name dictionaries and its `CheckPage`/`FindDockingPane` equivalent.
The native allocator then leaves those names alone. Matching client and server
mod/scenario versions therefore compute the same high IDs even when
`blocksmap.dat` is not rewritten. Existing mapped IDs are retained if present.
The risk is named-ID-space exhaustion or divergence when RE2, another mod, or a
different scenario version occupies the high range differently.

### Teleporter class

The Teleporter block class is:

```text
Class: Teleporter
```

which maps to native `BlockTeleporter : RegistryOptions, IDevice`. It is a
specific behaviour, not a generic powered-device class. Do not use it for an
unrelated device. `CapacitorMS` has no class, so a class-less clone is not
registered as a device; the Cruise Amplifier still needs a suitable neutral
class or a mod-defined class.

### MarketPrice and copied device stats

`MarketPrice` is an explicit ECF value; the game does not auto-calculate it.
RE2 normally sets it to the sum of recipe item market values. Verified examples:

| Block | Recipe material value | `MarketPrice` |
| --- | ---: | ---: |
| `TeleporterCV` | 26592 | 26592 |
| `ThrusterSVSmallT2` | 6490 | 6490 |

RE2 sometimes applies a documented `1.25x actual market value` multiplier for
blocks containing non-craftable/purchasable parts.

Medium fuel tank explosion/radiation source values:

| Block | Blast particle | Index | Radius | Damage | Radiation |
| --- | --- | ---: | ---: | ---: | ---: |
| `FuelTankMSLarge` (BA/CV) | `@rg_projectiles/RG_BlastPlasmaBlueL2` | 0 | 4 | 800 | 2.8 |
| `FuelTankSV` (SV/HV) | `@rg_projectiles/RG_BlastPlasmaBlueL2` | 0 | 4 | 525 | 2.75 |

### RE2 display-name mapping

RE2's `Extras/Localization.csv` is keyed by the internal item/block name. The
English column supplies the in-game display name. Examples relevant to the
Cruise Amplifier:

| Internal name | English display name |
| --- | --- |
| `TitanPlates` | Titanium Plates |
| `CapacitorComponent` | Advanced Capacitor |
| `AluminiumCoil` | Aluminium Foil |
| `Oscillator` | Advanced Electronics |
| `EnergyMatrix` | Energy Regulator |
| `PlasmaConverter` | Plasma Converter |
| `Graphite` | Graphite |
| `FluxCoil` | Flux Coil |
| `Electronics` | Electronics |
| `Computer` | Computer |

Small Plasma Thruster research parents in RE2:

- CV: `ThrusterMSSmallT2`
- SV: `ThrusterSVSmallT2`

### Debug block name vs model name

`CapacitorMS` is the **block definition `Name`** in `BlocksConfig.ecf`, not a
Unity prefab name and not a device class. The vanilla/RE2 entry is:

```text
Block Id: 256, Name: CapacitorMS
Model: @models/Blocks/Spaceship/CapacitorPrefab
Category: Deco Blocks
```

The game's block/debug identity is the `Name` field. To go from a model path to
a block name later, search the active `BlocksConfig.ecf` for `Model: <path>` and
read the enclosing block entry's `Name`. `CapacitorMS` has no `Class:`, so it is
a decoration block and is not automatically present in the custom-device
registry used by `IStructure.GetDevicePositions()`.

RE2 also has class-less powered/active decorative consoles:
`Eden_ConsoleWallBlueThin` (base) and `Eden_ConsoleWallBlue1x1` (variant). They
have no `Class:`, but set `IsActivateable: true`, `EnergyIn: 1`,
`IsAccessible: false`, and no interface.

**Runtime-verified build 5232:** class-less activateable blocks do not appear in
`IStructure.GetDevicePositions`. A class-less Cruise Amplifier prototype
installed the speed patch successfully, but `GetDevicePositions` returned no
positions for a placed block. `IsAccessible: true` is the property that makes the
device show in the block/control-panel list. The speed hook is the proven
`Disk.SaveView()` postfix from the test mod, not `PublishProject()`.

### Custom device detection via the structure aggregate

Status: **verified for the Cruise Amplifier on build 5232; build-specific
internal contract.**

Name-based public device APIs were not sufficient for a custom block:
`GetDevice<IDevice>(name)`, `GetDevicePositions(name)` and
`GetBlockSignals(name)` could all return empty even when the block was present
and visible in the debug device list. Iterating `GetDevices(DeviceTypeName.Light)`
and filtering by block ID worked, but is avoidable.

The game already maintains the structure aggregate incrementally in
`ConditionDictionary`. Placement adds a device, destruction removes it, and
replacement applies the old/new deltas. Thruster force, mass and device position
lists use this path. The verified lookup is:

```text
IStructure -> StructureBridge.ecc -> DatabaseToken.ActionLayout
  -> ActionLayout.ConditionDictionary
  -> ConditionDictionary.OpenImage("RE2Extended")
  -> BlockPosValueList.GetAllBlockPositonsOfType(mappedBlockId)
```

The block ID is resolved dynamically from `Application.GetBlockAndItemMapping()`
by block name; no fixed ID is needed. RE2Extended injects its own
`ConditionDictionary` bucket key, `RE2Extended`, via a Harmony postfix on the
`ConditionDictionary` constructor. ECF blocks use `IndexName: RE2Extended`, so
they do not need to borrow `Light` or any other gameplay bucket. Runtime
`IBlock.Get(... active)` filters manual/signal-disabled devices, and
`structure.IsPowered` gates the host vessel. Do not replace this with per-tick
structure/block polling or a bounding-box scan. See
`mods/RE2Extended/docs/DEVICE_DETECTION_PATTERN.md`.

---

## Teleporter shield and under-attack state (build 5232)

Status: **static contract verified; runtime probe pending.**

`BlockTeleporter` is the best reference for both gates because it is disabled
by an active shield and by recent combat.

### Shield state

```text
IStructure.IsShieldActive
  -> StructureBridge.get_IsShieldActive()
  -> ActionLayout.keywordsID.DeleteOptions()
  -> ConditionDictionary.keywordsID (EnumShieldState) > Off
```

`ConditionDictionary.EnumShieldState` is `Off=0`, `Active=1`, `WaitRecharge=2`,
`CooldownFromRecharge=3`, `CooldownFromSwitchOn=4`, `ActiveRecharging=5`,
`ActiveHitCooldown=6`. `DeleteOptions()` is `state > Off`, so it is true for
every non-`Off` state, not only a fully charged shield.

`IStructure.ShieldLevel` maps to `ConditionDictionary.currentLog` and is the
current shield points value, not a percentage. `ConditionDictionary.EnableDatabase`
consumes shield points from `currentLog`, clamps it to zero on depletion, and
publishes `GameEventType.ShieldAttacked` when damage is absorbed. Use
`IsShieldActive && ShieldLevel <= 0` for a depleted but enabled shield. Do not
use `ConditionDictionary.TestForm()` for this: it also rejects the recharge and
cooldown states that count as `IsShieldActive`.

Client tokens:

| Symbol | Token |
| --- | --- |
| `StructureBridge.ecc` | `0x0400A832` |
| `ActionLayout.keywordsID` (ConditionDictionary) | `0x04004AB9` |
| `ConditionDictionary.keywordsID` (EnumShieldState) | `0x04004BF8` |
| `ConditionDictionary.DeleteOptions()` | `0x06005069` |

`BlockTeleporter.GetActivationText()` and `OnBlockActivatedDefault()` check the
current structure's shield and, when docked, the parent structure's shield.
Use `IStructure.IsShieldActive` on the piloted structure for the speed gate;
do not substitute `ShieldLevel > 0`.

### Under-attack timer

`BlockTeleporter` uses the same timeout values exposed by the warp UI
(`igmNoWarpAttackTimeout`, "We are under attack! Warp not possible for {0}s"):

```text
currentTicks = RegistryTokenConnectionToken.keywordsID.messageCollection
lastOwnAttack = actionLayout.keywordsID.keywordsLength
lastRelatedAttack = actionLayout.keywordsID.messageCollection?.keywordsLength
elapsed = min(currentTicks - lastOwnAttack, currentTicks - lastRelatedAttack)
underAttack = elapsed < 20 * AttackTimeout
```

Client tokens:

| Symbol | Token |
| --- | --- |
| `RegistryTokenConnectionToken.keywordsID.messageCollection` (UInt64) | `0x040070A8` |
| `DatabaseToken.keywordsLength` (UInt64, last-attack timestamp) | `0x04001D58` |
| `BlockTeleporter.AttackTimeout` | `0x04001620` |
| `FormResolverConnectionToken.ChangeAssistant(UInt64,UInt64)` (min) | `0x06007E4A` |

`DatabaseToken` has four same-name `keywordsLength` fields with different types;
the exact UInt64 field token is required. Static writes to this timestamp appear
in `DatabaseToken.SaveAssembly` (`0x06001F86`) and `DatabaseToken.HideActivity`
(`0x06001F88`); the runtime meaning must be confirmed with a damage/timeout
probe before shipping gameplay behaviour.

---

## Client vs dedicated server

The dedicated server assembly is substantially smaller and does **not** contain the client player-ship controller:

- no `RbCtrlPlayerShip`
- no `RbCtrlShip`
- no client `MaxSpeedSystemConfig`
- no same client `Disk.PublishProject` player-speed path

The dedicated assembly does contain network package IDs including:

- `EntityPosAndRot`
- `EntityVelocity`
- `MovementInput`
- `Transformation`
- `PlayerInVesselTransformation`

This is consistent with client-side simulation and replicated movement, but it does **not** prove that the server accepts arbitrary client velocity. Multiplayer behavior must be tested directly.

### Mod API notes

Both client and dedicated `ModApi.dll` provide `Eleon.Modding.IModApi`, `IApplication`, `INetwork`, `IPlayfield`, `IEntity`, `IStructure` and related interfaces, but the client and dedicated `ModApi.dll` hashes differ. Validate separately.

**Important build-5232 finding:** the Dedi `Assembly-CSharp.dll` contains the Mod API interfaces but no implementation of `IPlayfield`, `IStructure`, `IEntity`, or `IContainer`. Its `ApplicationBridge.LocalPlayer` is always null and `GetBlockAndItemMapping()` returns null. A `ModTargets: Dedi` mod therefore cannot use structure/container objects through the new API. Gameplay loop code must run under `PfServer` (or `SinglePlayer`), where the full client-side `Assembly-CSharp`/bridge implementation is present.

The v0.7.0 generator prototype uses this split: config merge runs in every target, the client speed patch only in `Client`/`SinglePlayer`, and the 10-second container loop only in `SinglePlayer`/`PlayfieldServer`. It logs and stays inactive in Dedi master mode.

Lifecycle caveat: `IApplication.Mode` is not reliable during `IMod.Init`. In a single-player launch it can still report `Client`. The v0.7.0 generator therefore only arms itself during Init and re-checks the mode on `Update`/`FixedUpdate`, activating once it becomes `SinglePlayer` or `PlayfieldServer`. Deciding at Init misclassified single-player as a remote client.

`IContainer.AddItems(type, count)` ID namespace finding: passing the `ItemsConfig.ecf`
`Item Id` directly is wrong on this runtime. `BookmarkInvoker.AddItems` builds an
`OptionsSerializer(type)` and indexes `AddinEditor.ToggleProcess(type)`, where the
`AddinEditor` registry contains blocks at their block-ID indices and items at
`8192 + itemId`. Passing `325` therefore selected block ID 325
(`TurretMSFlakRetract` in RE2), not `FusionCell` item 325. The observed result was
retractable flak turrets. The internal item registry index for `FusionCell` is therefore
`8192 + 325 = 8517`. The generator now uses `FusionCell` from
`IApplication.GetBlockAndItemMapping()` when available and falls back to 8517.
It refuses a mapping value below 8192 so a block-range ID cannot repeat the turret
bug. Live verification of the produced item is still pending.

Server-side API has entity position/movement methods such as `IEntity.Position`, `IEntity.Move`, `IEntity.MoveForward`, and `IEntity.MoveStop`, but no direct “set player vessel maximum speed” property.

`INetwork` can carry custom mod packets between client/server/playfield targets. It does not automatically make a movement change authoritative.

---

### Mod network capability (build 5232, static)

Status: **static implementation inspection; runtime delivery not yet probed.**

The public `INetwork` API exposes client, playfield-server and dedicated packet
senders/receivers. Inspected build-5232 implementations show:

| Process | `SendToDedicatedServer` | `SendToPlayfieldServer` | `SendToPlayer` |
| --- | --- | --- | --- |
| Client `Eleon.ModBridge.NetworkBridge` | implemented | returns `false` | returns `false` |
| Dedicated `Eleon.ModBridge.NetworkBridge` | returns `false` | implemented | returns `false` |

So the public mod-packet route appears to be `Client -> Dedicated` and
`Dedicated -> PlayfieldServer`; there is no verified public server-to-client
route on this build. `RegisterReceiverForClientPackets` exists, but the inspected
`SendToPlayer` methods return `false` in both assemblies.

`NetworkBridge` derives the receiver key from
`Assembly.GetCallingAssembly()`. A runtime assembly that sends and registers from
the same DLL should use the same assembly-derived name; the `_Info.yaml` mod name
should not be assumed without a probe.

This affects multiplayer voice design. See
`mods/RE2Extended/docs/MULTIPLAYER_VOICE_PLAN.md`. Runtime probes must confirm:

- whether PfServer/PfClient behaves differently from the inspected static code;
- whether `SendToPlayer` returns `true` and reaches a client receiver;
- whether client block-aggregate removal events are reliable for the local
  ship and for passengers, which would avoid server-to-client packets entirely.

---

## Evidence log

| Date/context | Target | Method | Result |
| --- | --- | --- | --- |
| Baseline inspection | client | `ilspycmd`/Mono.Cecil | `Disk.PublishProject` has one caller, `RbCtrlPlayerShip.FixedUpdate`; not yet runtime-probed. |
| Baseline inspection | dedi | `ilspycmd`/Mono.Cecil | No `RbCtrlPlayerShip`; client movement controller absent from dedicated assembly. |
| Test implementation | client | .NET Framework build | `mods/EmpyrionExtendedTestMod` builds and stages a loader/runtime/Harmony package. Static target resolution validated with Mono.Cecil. |
| Experiment D smoke test | client | Harmony 2.4.3 field-type fix + `Disk.SaveView` postfix | Installed without invalid IL. CV `ActionList` atmosphere limit changed from 60 to 70; actual speed change confirmed by user. |
| Feature test | client | CV `SaveView` target = 300 | User confirmed the speed limit changes. |
| v0.6.0 implementation | client | ECF collection loader postfix + CV `SaveView` 300/600 | Builds and stages a `GigaSpeedBooster` ECF fragment using fixed block ID 1565. Runtime block injection, device detection and 600 limit still need in-game confirmation. |
| v0.7.0 assessment | dedi/pfserver | ModApi/assembly inspection | Dedi master has no `IPlayfield`/`IStructure`/`IContainer` implementation and null block mapping. Generator must run in `PfServer`/`SinglePlayer`; client-only is not authoritative. |
| v0.7.0 implementation | client/pfserver | `IContainer.AddItems` loop + ECF merge | Builds and stages `InfiniteFuelGenerator` (fixed block ID 1564, FusionCell item 325). Runtime PfServer replication/container behavior still needs live-server confirmation. |
| RE2 reference discovery | workshop | file/config inspection | Located RE2 Workshop ID `3143225812`; pinned config/localization hashes; resolved recipe item names and Small Plasma Thruster parents; found RE2 already uses block IDs 1566/1567 and comments 1519-1526 as free. |
| Shield/combat contract | client | `BlockTeleporter`/`ConditionDictionary`/`DatabaseToken` inspection | `IStructure.IsShieldActive` is native `EnumShieldState > Off`; teleporter checks own and docked-parent shield; under-attack gate uses `DatabaseToken.keywordsLength` (UInt64) with `20 * AttackTimeout`. Runtime probes still pending. |
| Device visibility/detection | client | runtime log + config | `IsAccessible: true` makes a class-less device appear in the block/control-panel list. Class-less blocks still do not appear in `GetDevicePositions`; fixed-ID scan fallback added. Speed must use the proven `Disk.SaveView()` postfix, not `PublishProject()`. |
| Class-less device prototype | client | runtime log | `PublishProject` postfix was active, but a class-less Cruise Amplifier did not appear in the ship device list and `GetDevicePositions` returned no positions. Follow-up: `IsAccessible: true` is the control-panel/block-list visibility property; class-less blocks still need a fixed-ID scan fallback. Speed must use `SaveView()`, not `PublishProject()`. |
| Block-ID / device stats | workshop + client | config and decompile inspection | Block ID space is `0..4095`: terrain `0..255`, fixed `256..2047`, named `2048..4095`. RE2 uses 1896 fixed IDs with no conflicts for 1521/1522, and 1517 named blocks mapping to 2048..3563. Teleporter class is `Teleporter`. `MarketPrice` is explicit (RE2 normally recipe material value); medium fuel tanks provide the requested blast/radiation values. |
| Multiplayer network capability | client + dedi | `NetworkBridge` inspection | Client can send to dedicated; dedicated can send to playfield server; `SendToPlayer` returns `false` in both inspected assemblies. Server-to-client mod packet delivery is unverified and likely unavailable through the public API on build 5232. |

---

## Construction Manager Phase 0 static contracts (build 5232 client)

Status: **static evidence only; runtime probe code added, not yet verified in a
test world.** Target client MVID
`462483cd-2f1f-4a24-9c83-46d24b1cb3f4`. See
`mods/RE2Extended/docs/CONSTRUCTION_MANAGER_PHASE01.md`.

### Save path

`Eleon.ModBridge.ApplicationBridgeCommon.GetPathFor(AppFolder.SaveGame)` maps to
`DiskEditor.SortMemory()` on the client. `AppFolder.Root`,
`AppFolder.Content`, `AppFolder.Mod`, `AppFolder.Cache` map through
`FormResolverConnectionToken` helpers. The runtime probe logs the returned
paths, whether they exist, and the candidate
`<save>/Mods/RE2Extended/ConstructionManager.json`, but it has not yet confirmed
whether the returned value is the active save folder or the `Saves` root.

### Constructor enumeration and queue access

Static evidence:

- `StructureBridge.GetDevicePositions(string)` reads
  `ecc.keywordsID.keywordsID.keywordsID.SortMemory(customDeviceName)` and
  converts the native `Vector3i` values to `VectorInt3`.
- `StructureBridge.GetDevice<T>(string)` uses
  `ecc.keywordsID.keywordsID.keywordsID.FindDockingPane(blockName)` and then
  casts the native `NodeHelper` at that position to `T`.
- `StructureBridge.GetDevice<T>(int,int,int)` calls
  `ecc.keywordsID.ViewConnection(new Vector3i(...))` and casts the result.
- `BookmarkInvoker : NodeHelper, IContainer, IDevice`; constructor tile entities
  derive from `ActivatorSettings : BookmarkInvoker`, so `IContainer` queue methods
  are statically reachable through `GetDevice<IContainer>(position)`.
- `ConditionDictionary.DeviceIDs` contains `Constructor = 41` (enum starts at
  `FuelTank = 1`). `ConditionDictionary.RemoveTextFile()` iterates
  `0..41`, which confirms the device buckets use those enum names and includes
  `"Constructor"`.
- `ConditionDictionary.OpenImage(string)` returns a `BlockPosValueList`, and
  `BlockPosValueList.GetAllBlockPositonsOfType(int)` filters entries by the
  registry block type. This is the intended multi-instance enumeration path:
  open `"Constructor"`, then query each whitelisted block ID.
- `BlockPosValueList.GetAt(int)` and the indexer expose the stored
  `RegistryToken`, whose native `Vector3i` position can be read by reflection.

The Phase 0 probe cross-checks the `"Constructor"` bucket against the public
`GetDevicePositions(blockName)` list and logs both counts for every whitelisted
constructor. It then reads `GetContent()` from the named device and the first
positional device. Queue mutation is opt-in and restores the original content
with `SetContent`.

### Manager block interaction

`ActionLayout.SortFunction(Vector3i)` returns a block display string, and the
native default device-use path raises:

```text
GameEventType.DeviceUsed, ActionLayout.SortFunction(position), ...
```

The Phase 1 manager block now uses native `Class: ArmorLocker` (a container with
`VolumeCapacity`) so it supports look-at interaction and device-list access.
Container interaction raises `GameEventType.InventoryOpened`; the manager checks
all event arguments for a native `Vector3i` matching the manager block, closes
the resulting foreign native window through the UI manager, then adopts its own
modal. `DeviceUsed`, `DeviceNamePowered`, `BlockChanged`, `F8` and the
Escape-menu entry remain secondary openers.

### Native modal lease for mod-owned UI

Static evidence from build 5232, also demonstrated by the third-party Trade
Ledger project:

- `IGui.ShowDialog(DialogConfig, DialogActionHandler, int)` creates a normal local
  native modal and owns cursor/input and Escape behavior.
- `Assembly-CSharp.RegistryTreeConnectionToken` owns the native UI window stack
  and registry. Its static getter returns the singleton manager.
- Window registry entries expose a root `GameObject`, a `CanvasGroup`, and a
  controller object. A dialog entry's controller is
  `Assembly-CSharp.StreamDesignerConnectionToken`, which stores the exact
  `DialogConfig` and `DialogActionHandler` used to create it.
- Setting the entry's `CanvasGroup.alpha = 0` hides the empty native dialog while
  preserving its input lease; the mod then draws its own IMGUI surface.
- The manager close method is the instance `RegistryTreeConnectionToken.CloseXmlFile()`
  returning `bool`.

RE2Extended resolves these members by type/shape and fails closed if the contract
is ambiguous. It does not copy Trade Ledger code or use its metadata tokens. The
UI is only adopted when the native window stack is empty; otherwise the open
request waits for a short handoff after the foreign/native window closes.

This proves a viable mod-owned UI path, not native constructor-window reuse.

### Item metadata and icon atlas for the manager UI

Build 5232 static contracts used by the Construction Manager UI:

- `AddinEditor.keywordsID` is the static `AddinEditor[]` definition table.
- `AddinEditor::keywordsID` is the definition's `DynamicProperties`; the private
  `AddinEditor::userDataList` string is the internal item/block name.
- `DynamicProperties.HasKey(string)` and `GetStringValue(string)` expose
  `CustomName`, `CustomIcon`, `Info`, `Category` and `AllowPlacingAt` and related
  ECF properties. The manager maps category text to its seven constructor-style
  filters and maps `AllowPlacingAt` tokens Base/BA, MS, SS and GV to BA/CV/SV/HV.
- Active names/descriptions are localized through NGUI `Localization.Exists` and
  `Localization.Get(string,bool)`.
- `MenuSiteConnectionToken::keywordsID` is a static UI manager singleton. Its
  `ProcessManagerConnectionToken` field holds the packed item-icon atlas:
  a `Texture2D` plus `List<UISpriteData>` with name/x/y/width/height.
- IMGUI can draw a sprite sub-rect with
  `GUI.DrawTextureWithTexCoords(texture, uv, true)`, where the V coordinate is
  flipped from the native atlas rectangle.

The runtime resolves these by type/shape and falls back to loaded `UIAtlas`
resources if the packed atlas is unavailable.

Native constructor template filtering is exposed by
`SymbolService.AllocateDockingPane(string target)`, which returns the
`NodeProvider` list for a constructor target abbreviation. RE2Extended calls it
for `BaseC`, `SmallC`, `LargeC` and `AdvC` and unions the output item types. This
is the same path used by `ActivatorSettings.BatchBuildDevice()`.

Native category and grid metadata:

- Item category: `AddinEditor` Category property (native `messageCollection`).
- Block category: `RegistryOptions.optionsID`, parsed from the block Category
  property, falling back to `Miscellaneous`.
- Block grid type: `RegistryOptions` `ClientScope`, parsed from
  `AllowPlacingAt`. Decoded mapping: `Base = 2`, `SS = 4`, `MS = 8`, `GV = 16`
  (`Terrain = 1`). The UI maps `Base -> BA`, `MS -> CV`, `SS -> SV`, `GV -> HV`.
- Non-block items have scope `0`. With any non-`All` grid filter selected, the
  native bitmask intersection is zero, so those items are excluded; they appear
  only under `All`. Do not treat scope `0` as "matches every grid".
- Block display metadata for template cells lives in the `RegistryOptions`
  `DynamicProperties` (`CustomName`, `CustomIcon`, `Info`), not only in
  `AddinEditor`. `AdvancedDecoDeviceBlocks` is the reference case.
- Tooltip content: `NodeProvider.CleanActivator(false)` returns the native
  constructor tooltip lines (ingredient section, output count, and displayed
  dynamic properties such as Mass, Volume, dimensions, airtightness and
  MarketPrice). Native lines use NGUI markup such as `[c][RRGGBB]`, `[-]`,
  `[b]`, `[i]`, `[u]`, `[sup]`, `[sub]`; the manager converts these to Unity
  IMGUI rich text. Craft time is `NodeProvider`'s public float field (token
  `0x04001993` in build 5232) and is added separately using the localized
  `guiCraftTime` / `utsSecond` labels.
- `NodeProvider.CleanActivator(false)` emits cumulative `|IS|` lines for recipe
  inputs (each line contains all inputs seen so far). Do not render every line as
  an input row. Read the actual inputs from the `NodeProvider` `List<PaneFactory>`
  and the `PaneFactory` options/quantity fields, or take only the final cumulative
  line.
- Native template order: `ItemGridTemplates.JoinAspect` sorts `DialogEventArgs`
  by `DialogEventArgs.DeployXmlFile`, which compares craft/unlock state and then
  the original `DialogEventArgs.colorInitialized` index. That index is assigned
  from the order returned by `SymbolService.AllocateDockingPane`, itself a
  subsequence of the global `SymbolService` recipe list. RE2Extended preserves
  that global list order, giving Graphite, Iron Ore, Copper Ore first in
  Components.

### Deferred runtime checks

- [ ] `GetPathFor(AppFolder.SaveGame)` = active save folder vs saves root.
- [ ] All seven whitelisted constructors appear in the `"Constructor"` bucket.
- [ ] `GetDevice<IContainer>(position)` returns a working queue for each
      constructor class in the whitelist.
- [ ] `AddItems`/`RemoveItems`/`SetContent` change the visible queue and the
      restore path leaves it unchanged.
- [ ] Multiple constructors with the same block name enumerate correctly.
- [ ] `DeviceUsed` fires for the manager console and carries the expected
      display string.
- [ ] The empty `IGui.ShowDialog` modal is accepted and `CanvasGroup` concealment
      leaves cursor/input under modal control.
- [ ] The Escape-menu entry appears only with a manager block and closes the
      native menu before the manager adopts its own modal.
- [ ] Manager render is suppressed when another native window becomes topmost.
- [ ] The packed `ProcessManagerConnectionToken` atlas supplies the expected
      item icons, and missing icons degrade to placeholders.
- [ ] `DynamicProperties.Info` plus localization produces readable item tooltips.
- [ ] `IStructure.Id` + manager position is stable across save/restart,
      rename, docking and playfield reload.
