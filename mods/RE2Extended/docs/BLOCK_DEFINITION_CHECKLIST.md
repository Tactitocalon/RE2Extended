# New device block definition checklist

Purpose: use this before implementing any new Empyrion/RE2Extended device. Fill
every item below, even when the value is inherited from a base block. The test
mod proved that injected blocks fail validation when required base properties
(such as material) are missing, so "the base block probably provides it" is not
good enough.

See [`BLOCK_CONFIG_REFERENCE.md`](BLOCK_CONFIG_REFERENCE.md) for the full
property reference derived from the game's `BlocksConfig.ecf` header.

Pin references before filling this in:

- game build + MVID;
- RE2 Workshop ID/path + `BlocksConfig.ecf` / `ItemsConfig.ecf` /
  `Templates.ecf` / `Localization.csv` hashes.

## 1. Identity and placement

| Property | Required? | Notes |
| --- | --- | --- |
| `Name` | Yes | Unique internal name. Used as the localization key, device name, template name, and block lookup name. |
| `Block Id` or `Block Name` | Yes | Fixed IDs: `256..2047`. Auto named IDs: `2048..4095`. Never use `0..255` for devices. Verify the ID is free across vanilla, RE2, scenarios, and other mods. |
| `Class` | Yes | Selects native device behaviour. The class must be probed; see the class-selection section. |
| `Ref` / base block | Recommended | Use an existing block as the visual/validation base, but still verify every required property. `Ref` is not guaranteed to resolve for injected fragments. |
| `Category` | Yes | e.g. `Devices`. |
| `Group` / `IndexName` / `EssentialCategory` | Usually | Controls build-menu grouping and device index category. Match the closest comparable device. |
| `Info` | Yes | Localization key for the device description. |
| `CustomIcon` | Recommended | Existing icon name if reusing another device's icon. |
| `ShowBlockName` / `ShowUser` | Recommended | Normal devices use `ShowBlockName: true`; `ShowUser` controls visibility in lists. |
| `Place` | Yes | `Free`, `NoRotation`, or `Face`, depending on the model. |
| `AllowPlacingAt` | Yes | Exact grid list, e.g. `MS` for CV, `SS` for SV, `Base,MS`, `SS,GV`. |
| `SizeInBlocks` | Yes | Footprint, e.g. `2,1,1`. |
| `SizeInBlocksLocked` | Recommended | Grids where the block size is fixed. |
| `OccupySizeInBlocks`, `Voxelize`, `Collide` | Conditional | Required for models that must not be voxelized or need custom collision. |

## 2. Model and rendering

| Property | Required? | Notes |
| --- | --- | --- |
| `Material` | Yes | Missing/empty material is a known validation failure. |
| `Shape` | Yes | Usually `ModelEntity`. |
| `Model` | Yes | Prefab path, e.g. `@models/Blocks/Spaceship/CapacitorPrefab`. |
| `Mesh-Damage-1..4` | Recommended | Damage-stage meshes, normally copied from the model's base block. |
| `DropMeshfile` | Recommended | Drop container/bag prefab. |
| `DropInventoryEntity` | Conditional | Needed for devices with inventory drops. |
| `BlockColor` | Recommended | Standard device tint. |
| `Texture`, `IsTextureable`, `IsGPUInstance` | Conditional | Usually only for textureable/instanced block families. |
| `ModelOffset`, `SymType` | Conditional | Model alignment and symmetry behaviour. |

## 3. Gameplay stats

| Property | Required? | Notes |
| --- | --- | --- |
| `Mass` | Yes | kg. |
| `Volume` | Yes | Litres. |
| `HitPoints` | Yes | Device durability. |
| `StackSize` | Yes | Default for devices: **1000**, unless a documented reason exists. |
| `EnergyIn` | Yes | Idle/continuous power draw in watts. |
| `EnergyInIdle` | Conditional | Only when the device has a separate idle draw. |
| `EnergyOut` | Conditional | Only for generators/power sources. |
| `CPUIn` | Yes | CPU cost. |
| `MarketPrice` | Yes | **Not auto-calculated by the game.** See section 6. |
| `BlastParticleName` | Yes for explosive devices | Copy from the closest fuel tank/explosive. |
| `BlastParticleIndex` | Yes for explosive devices | Integer index. |
| `BlastRadius` | Yes for explosive devices | Explosion radius. |
| `BlastDamage` | Yes for explosive devices | Explosion damage. |
| `Radiation` | Conditional | Use `display: RadiationLevel`. Fuel tanks use this. |
| `Temperature` | Conditional | Heat signature where applicable. |
| `RepFac` | Recommended | Reputation factor for destruction, often `20` for devices. |
| `IsActivateable` | Yes for switchable devices | Allows on/off state. |
| `IsActivateableInCP` | Recommended | Control-panel access. |
| `IsActivatedOnPlace` | Recommended | Start active when placed. |
| `IsLockable` | Recommended | Standard for devices. |
| `IsAccessible` | Yes for visible devices | Shows the block in the control panel / block list. It does not by itself add an F-button interface for a class-less block. |
| `IsOxygenTight` | Yes | Usually `false` for devices. |
| `IsDuplicateable` | Recommended | Normally `false` for functional devices. |
| `MaxCount` / `ForceMaxCount` | Conditional | Only for limited devices. |
| `UpgradeTo` / `DowngradeTo` | Conditional | Only for upgrade families. |
| `PickupTarget` | Conditional | Item/block returned on pickup/deconstruct. |
| `TemplateRoot` | Conditional | Needed for child groups/upgrade templates; do not self-reference. |

### Active/powered devices

For any device whose feature depends on being switched on:

- set `IsActivateable: true`;
- normally set `IsActivatedOnPlace: true`;
- verify the runtime active flag with `IBlock.Get(out type, out shape, out
  rotation, out active)`;
- verify the structure itself is powered (`IStructure.IsPowered`);
- document whether "installed", "powered", and "active" are separate gates.

## 4. Class selection

`Class:` is a real behaviour in this game, not a cosmetic tag. Examples:

- Teleporter: `Class: Teleporter` -> native `BlockTeleporter : RegistryOptions, IDevice`;
- fuel tank: `Class: FuelTank`;
- light: `Class: Light`;
- shield generator: `Class: ShieldGenerator`.

Before implementing a new device:

1. Identify the closest native class whose behaviour is acceptable.
2. Probe whether it registers in the custom-device registry:
   `GetDevicePositions(name)`, `GetAllCustomDeviceNames()`,
   `GetDevice<IDevice>(name)`.
3. Confirm it has no unacceptable inventory/UI/signal/teleport/light side effect.
4. If no existing class is suitable, determine whether a mod-defined class can be
   registered on the pinned build. Do not silently use an unrelated class.
5. Record the chosen class and the probe evidence.

A class-less block is not automatically in the custom-device registry. RE2's
`Eden_ConsoleWallBlueThin`/`Eden_ConsoleWallBlue1x1` can be `IsActivateable` and
powered. `IsAccessible: true` is what makes a device show in the block/control-
panel list. A class-less device can be counted with a cached fixed-ID block scan
when `GetDevicePositions` returns nothing. Use `Class: Switch` only if you
explicitly want switch-native behaviour.

## 5. Crafting, research, localization

| Item | Required? | Notes |
| --- | --- | --- |
| `Templates.ecf` entry | Yes | Template `Name` must equal the block `Name`. |
| `CraftTime` | Yes | Use comparable device timings. |
| `Target` | Yes | Constructor classes, e.g. `SmallC,AdvC` / `AdvC`. |
| `Child Inputs` | Yes | Exact item names from the active `ItemsConfig.ecf`. |
| `UnlockCost` | Yes | Research point cost. |
| `UnlockLevel` | Yes | Player-level requirement. |
| `TechTreeParent` | Yes | Existing block name, not a display string. |
| `TechTreeNames` | Yes | e.g. `Capital Vessel`, `Small Vessel`. |
| Localization display name | Yes | `Localization.csv` row keyed by block `Name`. |
| Localization `Info` key | Yes | Description row keyed by `Info`. |
| Icon/`CustomIcon` | Recommended | Existing icon or a real new asset. |
| Market price | Yes | See section 6. |

## 6. MarketPrice calculation

`MarketPrice` is an explicit ECF value. The game does not calculate it from the
recipe.

RE2's normal method is the sum of the recipe's item market values:

```text
MarketPrice = sum(item.MarketPrice * recipeQuantity)
```

Verified examples from the pinned RE2 config:

| Block | Recipe material value | ECF `MarketPrice` |
| --- | ---: | ---: |
| `TeleporterCV` | 26592 | 26592 |
| `ThrusterSVSmallT2` | 6490 | 6490 |

RE2 sometimes applies a deliberate multiplier, e.g. `1.25x actual market value`
for blocks containing non-craftable/purchasable parts. That is a balance
decision and must be recorded explicitly.

Cruise Amplifier initial values using the normal material-value method:

| Block | Material value |
| --- | ---: |
| `CruiseAmplifierCV` | **48584** |
| `CruiseAmplifierSV` | **10629** |

Recompute these from the pinned RE2 `ItemsConfig.ecf` and `Templates.ecf` in a
build-time script if possible. Do not hand-edit them after an RE2 update without
rerunning that check.

## 7. Explosion/radiation reference values

| Source block | BlastParticleName | Index | Radius | Damage | Radiation |
| --- | --- | ---: | ---: | ---: | ---: |
| `FuelTankMSLarge` (BA/CV medium) | `@rg_projectiles/RG_BlastPlasmaBlueL2` | 0 | 4 | 800 | 2.8 |
| `FuelTankSV` (SV/HV medium) | `@rg_projectiles/RG_BlastPlasmaBlueL2` | 0 | 4 | 525 | 2.75 |

Use the radiation value from the matching tank family. The user's requested
2.8 (CV) and 2.75 (SV) values match these blocks exactly.

## 8. Pre-implementation sign-off

- [ ] Target build/MVID and RE2 version/hashes recorded.
- [ ] Block name and ID verified free; startup collision check planned.
- [ ] Base block and all copied properties identified.
- [ ] Device class chosen and probed; no unintended behaviour.
- [ ] All identity, model, placement, and gameplay fields filled in.
- [ ] Active/powered semantics defined and testable.
- [ ] Explosion and radiation values copied from the named source block.
- [ ] Recipe items resolved against the active `ItemsConfig.ecf`.
- [ ] `MarketPrice` calculated from the recipe and recorded.
- [ ] Research parent/level/cost verified against the active RE2 config.
- [ ] Display name, description, and icon/localization path resolved.
- [ ] Save/restart, blueprint, pickup/deconstruct, and deconstruction behavior
      planned.
- [ ] Multiplayer and dedicated-server behavior included in the test plan.

## 9. Fill-in template

```text
Internal name:
Block ID:                       (fixed 256..2047 or named 2048..4095)
Base/Ref:
Class:
Category / Group / IndexName / EssentialCategory:
Display name key / Info key / CustomIcon:
Allowed grids:
SizeInBlocks / SizeInBlocksLocked:
Model / Material / Shape:
Mass (kg) / Volume (L):
HitPoints:
EnergyIn / CPUIn:
StackSize:                      1000 by default
MarketPrice:                    (calculated from recipe)
Explosion source block:
BlastParticleName / Index / Radius / Damage:
Radiation:
Active/powered semantics:
PickupTarget / TemplateRoot:
Recipe items + quantities:
CraftTime / Target:
UnlockCost / UnlockLevel / TechTreeParent / TechTreeNames:
Required runtime probes:
Known side effects:
```
