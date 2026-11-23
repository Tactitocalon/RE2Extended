# Native Findings

Living evidence log for Empyrion managed-code reverse engineering.

Rules for this file:

- Separate **static evidence** from **runtime-verified evidence**.
- Include build hashes and target (`client` or `dedi`) for every finding.
- Do not paste large decompiled methods or game binaries.
- Treat metadata tokens as build-specific locators.
- When a finding is superseded by a new build, keep it under an old-build heading instead of silently overwriting it.

Current baseline: **Empyrion 1.20.1 build 5232**

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
`DirectoryFactory.RemoveSymbol(...)` is the ECF/config file loader used by the
config editor and console commands, not by the mod loader. A DLL mod therefore
cannot simply ship a `BlocksConfig.ecf` and expect it to be merged.

Prototype solution: Harmony-postfix the `DynamicPropertiesCollection` loaders
(`ReadEcf`, `LoadBinary`, `Read`) and append one `Block` element parsed from the
mod's own `Configuration/GigaSpeedBooster.ecf` when a block collection is
recognised. The merge then calls `ApplyAllReferences()` so `Ref` inheritance is
resolved. This happens in memory only.

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
