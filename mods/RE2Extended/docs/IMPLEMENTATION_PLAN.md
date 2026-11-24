# RE2Extended — Cruise Amplifier implementation plan

Status: **draft plan**. No gameplay code has been written yet.

Target game baseline: Empyrion **1.20.1 build 5232** client and dedicated
server. Target scenario/configuration: **Reforged Eden 2**, Steam Workshop mod ID
**3143225812**. The installed copy is pinned in
[Pinned RE2 reference data](#pinned-re2-reference-data) below.

Third-party data note: the Reforged Eden 2 material referenced by this plan
(block/item names, IDs, config fields, hashes) is recorded as derived
interoperability facts only. It is not a copy of RE2 source or assets and must
not be redistributed as RE2 content; review RE2's license before reusing
anything beyond these reference facts.

## 1. Goal and definition of done

Add a production-quality **Cruise Amplifier** for CV and SV:

- new craftable/researchable CV and SV devices using the `CapacitorMS` model;
- a passive speed increase with tiered limits based on the number of installed
  amplifiers;
- no boost while the vessel is in combat or has an active shield;
- no change for HV, bases, AI/remote vessels, or ships without amplifiers;
- safe behaviour on an unsupported game build (fail closed, clear diagnostics);
- verified on a dedicated server with more than one client;
- no editing of the game installation or RE2 configuration files.

Definition of done for the first production release:

- speed limit changes in the UI **and** actual movement changes;
- CV and SV space/atmosphere tables are verified in-game;
- combat and shield gates are verified with real gameplay events;
- blocks are placeable, craftable, researchable, localized, and survive
  save/restart;
- uninstall/rollback behaviour is documented;
- contracts and runtime evidence are recorded in `docs/NATIVE_FINDINGS.md`.

## Pinned RE2 reference data

Reforged Eden 2 lives outside the Empyrion installation in the Steam Workshop
tree:

```text
F:\SteamLibrary\steamapps\workshop\content\383120\3143225812
```

`383120` is Empyrion's Steam app ID; `3143225812` is the RE2 Workshop mod ID.
This is the authoritative location for RE2 item names, block definitions,
templates, research tech data, and localization when RE2 is selected as a
scenario. Always re-hash before trusting the values below.

| File | SHA-256 |
| --- | --- |
| `Content/Configuration/BlocksConfig.ecf` | `2fa9dd399c4d17cb987cb9eceb128483860290eb523fbd272b78c0c26cd75b6e` |
| `Content/Configuration/ItemsConfig.ecf` | `15b603bcf138349ee7c6200f3b3653ab6782dace05f029ae6a683d4ac8eab39e` |
| `Content/Configuration/Templates.ecf` | `ac3982c46e98da7690648b7a8f033f14454b271b6f27b8b9c3dc1e1249ad950e` |
| `Extras/Localization.csv` | `f44f20010d08d17f62230304d23f6f732e071e341faf0835fe2a2a7e302cfea2` |

Other useful reference locations:

```text
Vanilla configs:
  <Empyrion>\Content\Configuration\
Scenario overrides:
  <Empyrion>\Content\Scenarios\<scenario>\Content\Configuration\
RE2 localization:
  F:\SteamLibrary\steamapps\workshop\content\383120\3143225812\Extras\Localization.csv
```

RE2 reports free fixed block IDs in comments. Relevant current comments:

```text
# available free 1449 - 1452
# available free 1519 - 1526
# available free 1562 - 1565
```

### Block ID space

The game's block-ID model on build 5232 is:

| Range | Slots | Use |
| --- | ---: | --- |
| `0..255` | 256 | Terrain/texture-reserved. Do not use for devices. |
| `256..2047` | 1792 | Fixed `Block Id:` range for blocks/devices. |
| `2048..4095` | 2048 | `Block Name:` entries, auto-assigned in config order. |

So the total addressable range is **0..4095**. The usable fixed-ID range for
new devices is **256..2047**. Named blocks can use up to 2048 automatic slots.

RE2 uses the same fixed IDs as vanilla plus its own overrides: **1896 distinct
fixed IDs** are occupied across vanilla, RE2, and installed scenarios. In
`256..2047` that leaves 33 free IDs, in these gaps:

```text
783-794, 845-851, 873-874, 1050-1051, 1521-1526, 1562-1565
```

RE2 currently contains **1517 `Block Name:` entries**, which occupy the named
range **2048..3563** in a current save. That leaves roughly **532 named slots**
(`3564..4095`) before the 2048-slot named range is exhausted. Named IDs are
assigned by config order and are saved in `blocksmap.dat`, so existing saves keep
their old mapping when new named blocks are appended.

**1566 and 1567 are already used by RE2** (`BrainFungusHarvested` and
`WoodLargeDestroyedBlocks`), so the earlier draft reservation was wrong. The
test mod uses 1564/1565, so those are also out for coexistence. The plan reserves
**1521/1522**, which are inside the RE2-commented `1519-1526` gap and free across
the checked configs.

### Resolved RE2 names for the Cruise Amplifier recipe

The human-readable names in `notes/ideas.txt` map to these RE2 item IDs/names:

| Idea name | RE2 item name | Item ID |
| --- | --- | ---: |
| Titanium Plates | `TitanPlates` | 265 |
| Advanced Capacitor | `CapacitorComponent` | 209 |
| Electronics | `Electronics` | 212 |
| Computer | `Computer` | 210 |
| Aluminium Foil | `AluminiumCoil` | 268 |
| Advanced Electronics | `Oscillator` | 234 |
| Flux Coil | `FluxCoil` | 213 |
| Energy Regulator | `EnergyMatrix` | 211 |
| Plasma Converter | `PlasmaConverter` | 289 |
| Graphite | `Graphite` | 196 |

Resolved research parents (block names from RE2 `BlocksConfig.ecf`):

- CV "Small Plasma Thruster (CV)" = `ThrusterMSSmallT2`
- SV "Small Plasma Thruster (SV)" = `ThrusterSVSmallT2`

### Device class

The in-game Teleporter's ECF class is:

```text
Class: Teleporter
```

which maps to the native `BlockTeleporter : RegistryOptions, IDevice` type.
It is **not** a generic powered device class; using it for the Cruise Amplifier
would give the block teleporter behaviour and its range/UI logic. Do not use
`Class: Teleporter` for an unrelated device.

`CapacitorMS` has no class. A class-less clone may not appear in the custom
device registry used by `GetDevicePositions(name)`.

RE2's `Eden_ConsoleWallBlueThin`/`Eden_ConsoleWallBlue1x1` show the desired
class-less powered/active behaviour. Runtime testing showed that class-less
blocks are not in the custom-device registry, but the **`IsAccessible` property
is what makes a device show in the block/control-panel list**.

The implemented shape is therefore:

```text
Group: cpgSignal
IsAccessible: true
IsActivateable: true
IsActivateableInCP: true
IsActivatedOnPlace: true
(no Class)
```

A class-less block has no native behaviour beyond the activation state. The mod
uses a cached block-ID scan fallback if `GetDevicePositions` does not expose the
placed amplifier. This keeps the switch/signal presentation without giving the
block an unrelated native class.

`Class: Light` is proven to register and be countable, but it is not the right
semantic fit. `Class: Teleporter` is definitely wrong. See
[`DEVICE_CLASSES.md`](DEVICE_CLASSES.md) for all ECF classes.

### What `CapacitorMS` actually is

`CapacitorMS` is the **block definition `Name`** in
`BlocksConfig.ecf`, not the Unity prefab/model name and not a device class.
In both vanilla and RE2 the entry is:

```text
Block Id: 256, Name: CapacitorMS
Model: @models/Blocks/Spaceship/CapacitorPrefab
Category: Deco Blocks
```

The in-game debug information that shows "CapacitorMS" is therefore showing the
block's runtime identity, which is the same key used by `GetBlock`, device
position lookups, localization rows, and templates. The model file itself is
only `CapacitorPrefab` under `@models/Blocks/Spaceship/`.

To find a block name from a model in the future:

1. Search the active `BlocksConfig.ecf` for `Model: <model path>`.
2. Walk back to the enclosing `{ Block ... Name: ... }` entry.
3. Use that `Name` as the runtime identifier.
4. Use `Extras/Localization.csv` with that same key for the display name.

`CapacitorMS` is a `Deco Blocks` entry with **no `Class:`**. That means it is
not automatically a device. The amplifier must either add a device class or use
a detection path that works for non-device blocks; see section 4.7.
## 2. What can be reused from `EmpyrionExtendedTestMod`

Reuse the proven mechanics, not the test mod's hard-coded feature logic:

| Proven item | Reuse decision |
| --- | --- |
| Loader/runtime split and bundled Harmony loading | Reuse shape. |
| Locally field-type-corrected Harmony 2.4.3 | Reuse, pinned and regenerated by script. |
| `Disk.SaveView()` postfix and corrected-Harmony IL fix | Primary speed hook; reuse exactly. |
| ECF merge via `DynamicPropertiesCollection` loader postfixes | Generalize for Blocks and Templates; localization deferred. |
| Runtime mode gating (`Client` vs `SinglePlayer` vs `PfServer`) | Reuse and extend. |
| Fail-once logging and exception containment | Reuse. |
| Fixed block-ID reservation workflow | Reuse with new IDs and a stricter collision check. |

Do **not** reuse the test mod's prototype "any CV gets 300/600" logic, its
name-based one-device detection, or its fixed `1564`/`1565` IDs.

## 3. Proposed repository layout

```text
mods/RE2Extended/
  README.md
  build.ps1
  package/RE2Extended_Info.yaml
  scripts/patch-harmony-fieldtype.ps1
  Configuration/
    Blocks/CruiseAmplifier.ecf
    Templates/CruiseAmplifier.ecf
    Localization/CruiseAmplifier.csv      # only if runtime localization merge works
  docs/
    IMPLEMENTATION_PLAN.md                # this file
    CONTRACTS.md                          # verified native contracts
    TEST_PLAN.md
  src/
    RE2Extended.Loader/
      RE2ExtendedMod.cs
      RE2Extended.Loader.csproj
    RE2Extended.Runtime/
      RE2Extended.Runtime.csproj
      RE2ExtendedRuntime.cs
      Game/
        GameImage.cs                      # build/MVID/hash gate
        GameSymbols.cs                    # resolved types/methods/fields
        Log.cs
      Ecf/
        BlockConfigMerger.cs
        TemplateConfigMerger.cs
        LocalizationMerger.cs             # optional, if discovery succeeds
      Features/CruiseAmplifier/
        CruiseAmplifierFeature.cs
        CruiseAmplifierTuning.cs          # speed tables and limits
        CruiseAmplifierPatch.cs           # Harmony callbacks only
        VesselSnapshot.cs                 # CV/SV, space/atmo, powered
        AmplifierCounter.cs               # count + cache/invalidation
        CombatGate.cs                     # native "under attack" state
        ShieldGate.cs                     # public API shield state
```

Keep feature logic separate from config injection and from Harmony callbacks so
the speed table can be unit-tested or exercised by a diagnostic command.

## 4. Runtime behaviour

### 4.1 Patch point

Final target: **`Assembly-CSharp.Disk.SaveView()`**.

This is the proven target from `EmpyrionExtendedTestMod`: the corrected Harmony
successfully patched `Disk.SaveView()` and the user confirmed that the speed limit
changed in-game. Use `SaveView()` for the Cruise Amplifier.

The postfix changes only `ref float __result`. It must never mutate vessel
fields or persistent state.

### 4.2 Speed decision

Pseudo-code:

```text
if patch not installed or feature disabled: return
if __instance is not the expected player-ship Disk: return
if vessel is not CV or SV: return
if vessel is not the local player's piloted vessel: return
if vessel is not powered: return
if combat gate says under attack: return
if shield gate says shield active: return

count = amplifier count for this vessel type, capped at the table maximum
if count == 0: return

target = table[vesselType][spaceOrAtmosphere][count - 1]
if target > current result: result = target
```

Rules:

- **Absolute limit, increase-only application.** RE2 does not adjust base ship
  speeds; the idea values are the new maximum speed. The game's active path
  currently yields lower base limits (CV about 100/60 and SV about 130/70 in the
  `SaveView` path), so tier 1 is still an increase on that path. Still
  implement `if target > current result` so another mod or a future game change
  cannot be silently reduced by this feature. Runtime verification must confirm
  the exact base limit for each vessel/environment.
- **Boost loss.** When combat or a shield starts, returning the original value
  makes the engine decelerate from the boosted speed. This is preferable to an
  instant position/velocity write. Confirm it does not produce unacceptable
  physics behaviour.
- **Additional capacitors.** Counts above the maximum use the maximum tier and
  provide no additional benefit. They must not disable the amplifier or create
  a penalty.

Proposed table, exactly from `notes/ideas.txt`:

| Vessel | Environment | 1 | 2 | 3 | 4 |
| --- | --- | ---: | ---: | ---: | ---: |
| CV | Space | 115 | 130 | 145 | 160 |
| CV | Atmosphere | 65 | 70 | 75 | 80 |
| SV | Space | 160 | 200 | — | — |
| SV | Atmosphere | 80 | 100 | — | — |

### 4.3 Space vs atmosphere

Use the same `World.get_CloseConfig()` signal already used by the verified
speed patch. Record its semantics in the contract file; do not infer space from
playfield type strings.

### 4.4 Vessel type

Use the native `EntityType` from `EmulatorService`, not the UI title or structure
name. CV and SV are distinct; HV (`DeviceCollection`) must be excluded. The test
mod proved CV maps to `ActionList`; the remaining player-vessel type is presumed
SV but must be runtime-verified.

### 4.5 Combat gate

The requirement is the same under-attack timer used by warp. Static inspection
of `BlockTeleporter` already shows the native implementation. The game checks:

```text
currentTicks = RegistryTokenConnectionToken.keywordsID.messageCollection
lastOwnAttack = actionLayout.keywordsID.keywordsLength                   // ulong
lastRelatedAttack = actionLayout.keywordsID.messageCollection?.keywordsLength

elapsed = min(currentTicks - lastOwnAttack, currentTicks - lastRelatedAttack)
underAttack = elapsed < 20 * AttackTimeout
```

If the structure is docked, the related parent structure is also considered;
this matches the teleporter's "we are under attack" behaviour.

Verified client build-5232 contract pieces:

| Symbol | Token | Meaning |
| --- | --- | --- |
| `RegistryTokenConnectionToken.keywordsID.messageCollection` | `0x040070A8` | current game ticks (`UInt64`) |
| `DatabaseToken.keywordsLength` (UInt64) | `0x04001D58` | last attack timestamp used by the under-attack gate |
| `BlockTeleporter.AttackTimeout` | `0x04001620` | timeout in seconds (default 20) |
| `FormResolverConnectionToken.ChangeAssistant(UInt64,UInt64)` | `0x06007E4A` | returns `min(a, b)` |

Access path from the public structure wrapper:

```text
IStructure (StructureBridge)
  -> private ecc : DatabaseToken                 (0x0400A832)
  -> DatabaseToken.keywordsID : ActionLayout     (0x04001CEB)
  -> ActionLayout.keywordsID : DatabaseToken     (0x04004AB1)
  -> DatabaseToken.keywordsLength : UInt64       (0x04001D58)
```

The exact field token is essential because `DatabaseToken` has four fields named
`keywordsLength` with different types. Resolve by declaring type + name + type,
with the build token as the exact fallback.

Still required before shipping:

1. Runtime-probe the timestamp while taking damage and after the timeout; confirm
   it changes in the same way as the teleporter/warp result.
2. Confirm whether `hideActivity`/`SaveAssembly` is the write path for this
   timestamp on a damaged vessel.
3. Record the runtime evidence in `NATIVE_FINDINGS.md`.

If the native combat state cannot be resolved for the pinned build, disable the
feature rather than allowing combat speed boosts. A proximity heuristic is not
acceptable as the production gate.

### 4.6 Shield gate

Use `IStructure.IsShieldActive`. This is not a heuristic; on this build it maps
exactly to the native shield state used by teleporters:

```text
StructureBridge.IsShieldActive
  -> ActionLayout.keywordsID.DeleteOptions()
  -> ConditionDictionary.keywordsID (EnumShieldState) > Off
```

Verified contract pieces:

| Symbol | Token | Meaning |
| --- | --- | --- |
| `StructureBridge.ecc` | `0x0400A832` | native `DatabaseToken` behind `IStructure` |
| `ActionLayout.keywordsID` (ConditionDictionary) | `0x04004AB9` | vessel condition/shield state |
| `ConditionDictionary.keywordsID` (EnumShieldState) | `0x04004BF8` | Off/Active/Recharging/Cooldown state |
| `ConditionDictionary.DeleteOptions()` | `0x06005069` | returns `EnumShieldState > Off` |

`EnumShieldState` values are `Off=0`, `Active=1`, `WaitRecharge=2`,
`CooldownFromRecharge=3`, `CooldownFromSwitchOn=4`, `ActiveRecharging=5`, and
`ActiveHitCooldown=6`. `IsShieldActive` is true for every state except `Off`.

`BlockTeleporter` uses this exact check twice: once for the current structure and,
if docked, once for the parent structure. Our speed gate should use the same
semantics for the piloted vessel. This settles the previous open question:
"active shield" means any non-`Off` native shield state, including recharging
and cooldown states.

Teleporter evidence from build 5232:

```text
BlockTeleporter.GetActivationText()      -> checks own + docked-parent shield
BlockTeleporter.OnBlockActivatedDefault -> checks own + docked-parent shield
```

Do not substitute `ShieldLevel > 0`; a fully depleted shield can still be in a
non-`Off` state and must still block the boost.

### 4.7 Amplifier detection and count

Preferred detection: `IStructure.GetDevicePositions(blockName)`, which returns
all positions for a custom device name. Count CV and SV blocks separately and
cap to the table maximum.

What this means in practice: `GetDevicePositions` reads the game's
`ConditionDictionary -> DatabaseStream` custom-device registry. Class-less blocks
are not registered there. The implemented fallback scans the piloted structure's
bounding box by fixed block ID (`1521`/`1522`) and caches the result. The scan is
limited by a maximum volume and logs when it is skipped.

`IsAccessible: true` is what makes the device show in the block/control-panel
list. It is separate from custom-device position registration.

For each position, read `IBlock.Get(... out active)` and count only active
amplifiers. Cache the count briefly and invalidate on playfield/structure change.

Cache the count per structure for a short interval (for example 250–500 ms) and
invalidate on playfield load/unload, structure change if an event is available,
and local-player vessel change. Log only state changes, not every frame.

## 5. Block, recipe, research, and localization data

### 5.1 Block IDs

Reserve **1521** for `CruiseAmplifierCV` and **1522** for
`CruiseAmplifierSV`. These are inside RE2's commented free gap `1519-1526` and
are free across vanilla, RE2, and installed scenarios. See the ID-space table
above for the full range and headroom.

### 5.2 Block definitions

Two blocks are cleaner than one because CV and SV have different stats,
recipes, research parents, and speed tables.

Both blocks are **class-less** with `Group: cpgSignal`, `IndexName:
CruiseAmplifier`, `IsActivateable: true`, `IsActivateableInCP: true`,
`IsActivatedOnPlace: true`, `IsAccessible: true`. They use the `CapacitorMS`
model and `SizeInBlocks: "2,1,1"` as the actual model/hitbox. Do not inherit the
Eden decorative mass, volume, max-count, template-root, or collision values;
specify all stats explicitly. `IsAccessible: true` is required for the device to
appear in the block/control-panel list. Device position lookup falls back to a
cached block-ID scan because class-less blocks are not in the custom registry.

CV block (`CruiseAmplifierCV`):

- model: `CapacitorMS` (`@models/Blocks/Spaceship/CapacitorPrefab`);
- size: `2,1,1` (same footprint as `CapacitorMS`);
- hit points: 4500;
- mass: 4500 kg;
- volume: 800 L;
- energy in: 750;
- CPU: 5000;
- stack size: 1000;
- airtight: no;
- explosion: copy `FuelTankMSLarge` (`BlastParticleName:
  @rg_projectiles/RG_BlastPlasmaBlueL2`, `BlastParticleIndex: 0`,
  `BlastRadius: 4`, `BlastDamage: 800`);
- radiation: 2.8, `display: RadiationLevel`;
- market price: 48584 by the RE2 material-value method below;
- research: 12 points, level 15, Capital Vessel, parent `ThrusterMSSmallT2`;
- recipe: `TitanPlates` x16, `CapacitorComponent` x24, `Electronics` x36,
  `Computer` x10, `AluminiumCoil` x24, `Oscillator` x24, `FluxCoil` x8,
  `EnergyMatrix` x4, `PlasmaConverter` x4, `Graphite` x12.

SV block (`CruiseAmplifierSV`):

- same model;
- hit points: 1750;
- mass: 120 kg;
- volume: 31.3 L;
- energy in: 200;
- CPU: 300;
- stack size: 1000;
- airtight: no;
- explosion: copy `FuelTankSV` (`BlastParticleName:
  @rg_projectiles/RG_BlastPlasmaBlueL2`, `BlastParticleIndex: 0`,
  `BlastRadius: 4`, `BlastDamage: 525`);
- radiation: 2.75, `display: RadiationLevel`;
- market price: 10629 by the RE2 material-value method below;
- research: 11 points, level 10, Small Vessel, parent `ThrusterSVSmallT2`;
- recipe: `TitanPlates` x4, `CapacitorComponent` x4, `Electronics` x16,
  `Computer` x2, `AluminiumCoil` x8, `Oscillator` x4, `FluxCoil` x1,
  `EnergyMatrix` x2, `PlasmaConverter` x1, `Graphite` x4.

The remaining unspecified values (pickup target, exact category/group choices,
and any icon override) must follow the closest comparable vanilla/RE2 device and
be recorded in the ECF file. Fill in only values that are proven to validate.
The test mod learned that `Ref:` alone is not enough for injected blocks; the
base element's properties must be copied before validation. Generalize that
existing merger rather than depending on `Ref` resolving later.

### 5.3 Recipes, market price, research, and block-stat checklist

Add `Templates` entries named exactly after each block:

```text
{ +Template Name: CruiseAmplifierCV ... }
{ +Template Name: CruiseAmplifierSV ... }
```

**MarketPrice is not auto-calculated by the game.** It is an explicit ECF
property. RE2 blocks generally set it from the crafting recipe's item market
values. Examples checked:

- `TeleporterCV`: recipe material value 26592 = `MarketPrice: 26592`;
- `ThrusterSVSmallT2`: recipe material value 6490 = `MarketPrice: 6490`.

Some RE2 blocks explicitly use a `1.25x actual market value` multiplier when
they contain non-craftable/purchasable parts. That is a deliberate balance
choice, not automatic.

Using the same material-value method:

- CV recipe material value = **48584**;
- SV recipe material value = **10629**.

Implement these as the initial `MarketPrice`, and add a build-time script that
recomputes them from the pinned RE2 `ItemsConfig.ecf` + `Templates.ecf` so a
future RE2 update cannot silently desynchronise the values.

Research is defined on the block via `UnlockCost`, `UnlockLevel`,
`TechTreeParent`, and `TechTreeNames`, matching vanilla blocks. The RE2
identifiers are resolved above: use `ThrusterMSSmallT2` for the CV parent and
`ThrusterSVSmallT2` for the SV parent, with `TechTreeNames: "Capital Vessel"`
and `TechTreeNames: "Small Vessel"` respectively.

Both blocks must be `IsActivateable: true` and should start active on placement
(`IsActivatedOnPlace: true`) because the feature requires an active device. The
speed gate must still read the actual block `active` flag from `IBlock.Get`, so
a switched-off amplifier gives no boost.

See [`BLOCK_DEFINITION_CHECKLIST.md`](BLOCK_DEFINITION_CHECKLIST.md) for the
full list of properties to provide before implementing any future device, and
[`BLOCK_CONFIG_REFERENCE.md`](BLOCK_CONFIG_REFERENCE.md) for the reformatted
BlocksConfig property notes.

### 5.4 Localization and icons

Status: **implemented.** English entries ship in
`Configuration/Localization.csv` and are merged through the native CSV path. The
runtime fallback sets the same entries through `Localization.Set(string,string)`:

- `CruiseAmplifierCV` → "Cruise Amplifier (CV)";
- `CruiseAmplifierSV` → "Cruise Amplifier (SV)";
- descriptions: `bkiCruiseAmplifierCV` / `bkiCruiseAmplifierSV`.

Each vessel type uses a single named block; there is no separate invisible
inventory group. The suffix therefore appears both in the inventory/build menu
and when placed. Non-English translations are tracked in [`TODO.md`](../TODO.md).

The icon path `CustomIcon: CapacitorMS` still needs in-game verification. If the
reused icon is invalid, the model thumbnail should be used until a proper icon is
available.

## 6. Required discovery and contracts before coding the feature

These are blocking research items, in order:

1. ~~RE2 baseline~~ — done; see the pinned reference data above.
2. **Current CV/SV speed baselines** in space and atmosphere under RE2. RE2 does
   not change base speeds, so use the build-5232 native values and confirm the
   active path at runtime.
3. **`Disk.SaveView()` patchability** with the corrected Harmony (proven in the test mod).
4. **SV branch mapping** (`ActionList` vs the remaining type) and SV speed path.
5. **Combat state runtime probe** using the `DatabaseToken` timestamp contract.
6. ~~Shield state semantics~~ — done; `IStructure.IsShieldActive` is native
   non-`Off` shield state, matching the teleporter.
7. **Device detection class/count method** for the active Capacitor-based block.
8. **Config merge targets**: Blocks, Templates, and localization; confirm which
   `DynamicPropertiesCollection` instances receive each and in which process.
9. **Multiplayer smoke test**: verify single-player and multiplayer; ship speed
   is treated as client-authoritative and no server-side enforcement is added.

Each completed item gets a contract entry with target, build MVID, type/member
signature, token, caller/IL evidence, runtime evidence, and failure mode.

## 7. Implementation phases

### Phase A — Skeleton and safety

- Copy the loader/runtime/build/packaging shape from the test mod.
- Rename assembly, namespaces, Harmony ID, log prefix, package metadata.
- Pin the expected client/dedi MVIDs and hashes.
- Implement `GameImage` validation. On mismatch: do not install gameplay
  patches; log exact build identity; optionally still merge blocks only if safe.
- Add a single feature registry and clean `Dispose`/`UnpatchAll`.

### Phase B — Config/data pipeline

- Generalize the ECF merger into explicit collections with explicit base-block
  maps. No silent parse failure.
- Add block fragments and template fragments. Localization is deferred.
- Add startup collision and validation checks.
- Expose a diagnostic log showing injected names, IDs, base blocks, and
  collection names.

### Phase C — Speed patch

- Resolve and validate the final patch target.
- Implement the postfix and the pure speed-decision function.
- Add vessel/space/atmosphere/powered gates and local-player scope.
- Add the combat and shield gates behind resolved contracts.
- Add caching and fail-closed diagnostics.

### Phase D — In-game verification

- Single-player creative and survival matrix.
- Dedicated server with one, two, and all clients modded.
- Crafting, research, save/restart, blueprint, and deconstruction checks.
- Collision, landing, docking, warp, and playfield-transition checks.

### Phase E — Production hardening

- Add user-facing configuration for enable/disable, optional speed overrides,
  detection cache, and diagnostic logging.
- Document server/client version requirements and rollback.
- Record contracts and evidence in `docs/NATIVE_FINDINGS.md`.
- Produce a release package and a short operations checklist.

## 8. Test matrix

### Speed and gating

- CV with 0, 1, 2, 3, 4, 5 amplifiers, space and atmosphere.
- SV with 0, 1, 2, 3 amplifiers, space and atmosphere.
- HV, base, AI CV, remote-player ship: no change.
- Vessel unpowered: no boost.
- Shield off, charging, active, and fully charged: expected gate behaviour.
- Combat starts while boosting: speed returns to base; after timeout the boost
  returns.
- Damage from different sources (NPC, player, turret) and firing without being
  hit, to match the warp semantics.
- Docked, landing, warp, and playfield transition: no stuck boosted state.

### Blocks and progression

- Creative menu visibility and correct icon/name.
- Survival tech tree unlock at the specified level and parent.
- Recipe consumes exactly the specified ingredients.
- Place, pickup, deconstruct, blueprint save/load, and save/restart.
- Additional amplifiers above the cap are allowed and harmless.
- Removal of an amplifier lowers the limit promptly.

### Multiplayer

- Server only, client only, and both patched.
- One modded and one unmodded client in the same playfield.
- High-speed collision and docking.
- Server restart and client reconnect.
- Server logs show effective configuration and mod version.

## 9. Production safety rules

- No exception may escape a Harmony callback.
- A missing or changed native symbol disables the boost, not the game.
- If the combat or shield gate cannot be resolved, the boost is disabled.
- If block injection fails, the speed patch must not activate for blocks that
  do not exist.
- Never write game files, RE2 files, saves, or player databases.
- Keep the locally patched Harmony reproducible and accompanied by its license
  and modification notice.
- Warn that removing the mod after players have built the devices may leave
  unknown blocks in saves; require backups before install/uninstall.

## 10. Open questions and decisions needed

### Gameplay / balance

1. ~~Absolute limits or increases?~~ Answered: RE2 does not adjust base speeds;
   the table is the new maximum. Keep increase-only application for safety.
2. ~~What counts as in combat?~~ Answered: identical to the warp under-attack
   timer. Use the `BlockTeleporter`/`DatabaseToken` contract above, including the
   configured `AttackTimeout` (default 20 seconds).
3. ~~What is an active shield?~~ Answered: powered/native non-`Off` shield state,
   matching the teleporter. Use `IStructure.IsShieldActive`.
4. ~~Powered/active/placed?~~ Answered: the block must be powered and active.
   It has a manual/control-panel power toggle and signal control; count only
   positions whose `IBlock.Get` active flag is true.
5. Should the boost cut off instantly when combat/shield begins, or decay over
   a short interval? The planned implementation returns the base limit and lets
   the engine decelerate.
6. Are CV and SV allowed to use the same block, or are two separately
   craftable blocks preferred? This plan assumes two blocks.
7. ~~What mass, volume, market price, stack size, explosion behaviour, and
   pickup target should the two blocks use?~~ Answered: stats are specified in
   section 5.2; `MarketPrice` is calculated from recipe material value. Pickup
   target follows the base/device defaults and should be recorded in the ECF.

### RE2 / data

8. ~~Where is the exact RE2 config?~~ Answered: Steam Workshop mod ID
   `3143225812`, pinned above.
9. ~~RE2 item identifiers?~~ Answered: see the resolved recipe table above.
10. ~~Small Plasma Thruster parent block names?~~ Answered:
    `ThrusterMSSmallT2` (CV) and `ThrusterSVSmallT2` (SV).
11. Which block class gives reliable counting with the least side effects?
    `Class: Light` is proven but probably inappropriate; `Class: Teleporter` is
    the wrong behaviour. Need a neutral existing class or a mod-defined device
    class.
12. ~~Localization?~~ Deferred for v1; tracked in `TODO.md`.
13. ~~Are block IDs 1566/1567 free?~~ Answered: no, RE2 uses them. Reserved
    1521/1522 instead; verify on the final production mod set.

### Native / multiplayer

14. Does `Disk.SaveView()` patch cleanly with the corrected Harmony?
    This is the proven test-mod target; failure disables the feature.
15. Is SV the non-`ActionList` player-vessel type, and does its normal speed
    path require a separate patch?
16. ~~Which native state does warp read for its under-attack timeout?~~
    Answered by the `BlockTeleporter`/`DatabaseToken` contract above. Still
    probe whether it is safe to read from the postfix every frame and cache it.
17. ~~Server enforcement?~~ Answered: ship speed is client-authoritative on this
    server; no `PfServer` enforcement is added. Multiplayer is smoke-tested.
18. ~~Unmodded clients?~~ A slower unmodded client is acceptable; no handshake
    or rejection is required for v1.

## 11. Main risks

- **Wrong speed semantics** produce a feature that is weaker than base speed at
  tier 1 or unexpectedly strong in RE2.
- **Combat-gate mismatch** lets players escape combat or disables boost in
  harmless situations.
- **Config injection fragility** across game/RE2 updates, especially templates
  and localization.
- **Client/server movement disagreement** causing rubber-banding at high speed.
- **Block-ID collision** with a future RE2 update or another server mod.
- **Save compatibility** if the mod is removed after devices are placed.
