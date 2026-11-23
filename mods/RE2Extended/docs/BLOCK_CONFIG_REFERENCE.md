# BlocksConfig property reference

Source: Empyrion `BlocksConfig.ecf` header, build 5232, plus the pinned RE2
configuration. This is the quick reference for choosing and validating new block
properties. It is intentionally a summary of the important fields, not a full
copy of the game's config documentation.

Third-party data note: the RE2-derived parts of this reference are
interoperability facts (field names, block IDs, value ranges). They are not a
copy of RE2 source or assets; review RE2's license before reusing anything
beyond these reference facts.

## 1. Block IDs

| Range | Meaning |
| --- | --- |
| `0..255` | Terrain blocks/textures. Do not use for devices or other blocks. |
| `Block Id: 256..2047` | Fixed block-ID range. Building blocks that must appear in `BlockShapesWindow.ecf` need a fixed ID below 2048. |
| `2048` | The game documentation says fixed IDs go "up to #2048", but the current configs leave 2048 unused and begin auto-assigned named blocks at 2048. Do not use 2048 for a new fixed block. |
| `Block Name: ...` | Up to 2048 additional named blocks, auto-assigned after the fixed-ID range. In the current RE2 save these occupy `2048..3563`; the theoretical top is `4095`. |

Rules:

- `Block Id` and `Block Name` are alternative definitions.
- Named IDs are assigned in config order and persisted in `blocksmap.dat`.
- Never use `0..255` for non-terrain blocks.
- Verify a fixed ID against vanilla, RE2, scenarios, and all server mods.

## 2. Mass scaling

Mass for material/building blocks is defined for a 1 m block. The game scales it
by grid size:

| Grid | Block size | Multiplier |
| --- | --- | --- |
| HV/SV | 0.5 m | `Mass * 0.125` (`0.5^3`) |
| BA/CV | 2 m | `Mass * 8` (`2^3`) |

This applies to material building blocks, not necessarily to 3D model devices
that already define their own mass.

## 3. Placement and collision

| Property | Meaning |
| --- | --- |
| `AllowPlacingAt` | Grid list, e.g. `"Base,MS,SS,GV,Terrain"`. |
| `Place: Free` | Full 3-axis rotation preview; no auto-snap/orientation. |
| `Place: Face` | Snaps to the targeted surface; rotation around X only. |
| `Place: NoRotation` | Device cannot be rotated. |
| `Collide` | Collider set such as `"movement,bullet,rocket,melee,sight"`. Removing `movement` lets players/entities pass through while still taking weapon/tool hits. |
| `RemoveOnSI` | Remove the block immediately when structural integrity collapses. |
| `IsPhysicsColliders: false` | Used for thin ramps where physics colliders cause problems. |

## 4. Power and energy

| Property | Meaning |
| --- | --- |
| `EnergyIn` | Continuous/idle power draw. Comment says the value is given in kW, but existing configs use watt-like integer values. Match comparable devices. |
| `EnergyOut` | Power production for generators. |
| `EnergyInIdle` | Separate idle draw where supported. |

## 5. Textures

`Texture` is a comma-separated face list, e.g.:

```text
Texture: "102,101,102,102,102,102"
```

Face order in the header is described as:

```text
top, bottom, north, south, west, east
```

Some block families document a different ordering. Check the closest block model
before reusing a texture list.

## 6. Explosion particle index

| Index | Effect |
| ---: | --- |
| `1` | Large explosion, air |
| `2..4` | Small explosion, ground |
| `5..6` | Small explosion, air |
| `7` | Electric explosion |
| `8..9` | Large explosion, air |
| `10` | Electric explosion |

Explosion-related fields:

```text
BlastParticleName
BlastParticleIndex
BlastRadius
BlastDamage
```

## 7. `Ref` inheritance

- `Ref` can only point to an entry defined **before** the entry that uses it.
- You cannot `Ref` an entry whose fixed ID is higher than the referring entry.
- Injected mod fragments must not rely blindly on `Ref`; the test mod learned
  that base properties may not be resolved before validation. Copy required base
  properties or fully specify them.

## 8. Display, icons, pickup, and crafting

| Property | Meaning |
| --- | --- |
| `CustomIcon` | Reuse an existing icon, e.g. `CustomIcon: DetectorSVT1`. |
| `PickupTarget` | Block/device/item always placed in the player inventory on pickup or deconstruct. Does not work on child groups; set it on the parent. |
| `TemplateRoot` | Crafting template for child groups/upgrade functionality. Usually not required on a standalone parent; parent templates use the template named after the block. Never `Ref` an entry that is also this entry's `TemplateRoot`; that can cause a BP-library stack overflow. |
| `UpgradeTo` | Upgrade target. Child groups need `TemplateRoot` or the upgrade can be free. `UpgradeTo: null` is valid to avoid loops. |
| `DowngradeTo` | Reverse upgrade target. |
| `MarketPrice` | Explicit ECF value; the game does not calculate it. |
| `StackSize` | Inventory stack size; devices commonly use 1000. |
| `ShowBlockName` | Show the block name on the placed device. |
| `ShowUser` | Whether the block is exposed to player lists/UI. |

## 9. Activation and device counting

| Property | Meaning |
| --- | --- |
| `IsActivateable` | Block can be switched on/off. |
| `IsActivateableInCP` | Block can be accessed via the control panel (turrets, fuel tanks, cargo, etc.). |
| `IsActivatedOnPlace` | Whether the device starts switched on when placed. |
| `IsAccessible` | Shows the block/device in the control panel / block list. This is essential for a controllable device to appear to the player. |
| `IsDuplicateable` | Whether the block can be used with the plane/line drawing tool. |
| `MaxCount` / `ForceMaxCount` | Per-block limit. `ForceMaxCount` enforces it even when the difficulty setting disables limits. Used for cores, warp drives, etc. |
| `IsLockable` | Whether the device can be locked in the control panel. |

For a device that must be powered, manually switchable, and signal-controlled:

- set `IsActivateable: true`;
- set `IsActivateableInCP: true` if control-panel access is wanted;
- set `IsAccessible: true` so it appears in the device/control-panel list;
- set `IsActivatedOnPlace: true` if it should start on;
- verify the runtime active state with `IBlock.Get(... active)`.

## 10. Structural integrity, voxels, and physics

| Property | Meaning |
| --- | --- |
| `BlockSizeScale` | `2` for SV/HV or `8` for CV/BA; scales mass/HP according to voxels. |
| `Voxelize` | Whether the block is voxelized into small cubes instead of occupying full block space. Default `true`. |
| `OccupySizeInBlocks` | Forces the full `SizeInBlocks` occupancy for animated/shader/other special models. |
| `SizeInBlocks` / `SizeInBlocksLocked` | Footprint and grids where the footprint is locked. |
| `SymType` | Symmetry style: `1` quarter-round, `2` corner, `3` wall-sloped, `4` wedge with rotated default. |

## 11. Combat, repair, death, reputation, and drops

| Property | Meaning |
| --- | --- |
| `IsAntiInfantryWeapon` | Weapon damage only applies to infantry/entity attack-defense, not artillery. |
| `IsKeepContainers` | Core-only. Preserves pre-placed container contents in blueprints/loot. |
| `RepairToTemplate: false` | Excludes special blocks from repair-to-template to avoid exploits. |
| `DropOnDeath` | If `False`, the item/block is not dropped on player death. Default `True`. |
| `RepFac` | Scales reputation gain/loss when the block is destroyed. |
| `DropMeshfile` | Dropped container model, e.g. `Entities/Misc/DropContainerNewPrefab`, `BagSmallNewPrefab`. |
| `DropInventoryEntity` | Box variant used when a destroyed container drops. |

## 12. Dialogue hooks

| Property | Meaning |
| --- | --- |
| `ExecuteOnActivate` | Runs a dialogue state when the player faces the block and presses F. Does not require `IsActivateable`. |
| `ExecuteOnCollide` | Runs a dialogue state on touch/collision. |
| `DialogueSingleUserAccess` | Limits dialogue use to one player at a time. |
| `DialogueState` | Hard-coded dialogue target; can make the block player-triggerable with F. |
| `OmitCone` | Hides the yellow dialogue cone. |

Skill-based property modifiers can be written as:

```text
Mod.ReloadDelay: "ReloadDelay + Player.Skill['skillname']"
```

Supported examples include damage, blast damage, bullet spread, reload delay,
recoil, range in AU, and range in LY.

## 13. Container classes

Container-like classes generally require `VolumeCapacity`:

```text
Container
ContainerPersonal
ContainerController
ContainerExtension
ArmorLocker
WarpDriveTank
RepairStation
Constructor
```

Use a container class only when the device really needs inventory/volume.

## 14. Shader modes

| Mode | Use |
| --- | --- |
| `Device` | Device blocks, old coloring setup. |
| `NoSI` | Terrain placeables/no structural integrity. |
| `NPC` | NPC material-set coloring. |
| `Cutout` | Device with cutout transparency. |
| `DeviceEmissiveOn` | Constant emissive devices such as solar panels. |
| `DeviceNewColoring` | New coloring method for device blocks. |
| `NoSINewColoring` | New coloring for terrain placeables. |

## 15. Building-block IDs for `BlockShapesWindow.ecf`

All building blocks used in the shapes window need fixed IDs below 2048.

| Grid | Parent blocks |
| --- | --- |
| BA/CV | `396` Wood, `399` Concrete, `1322` Armored Concrete, `1481` Plastic Large, `402` Steel Large, `405` Hardened Steel Large, `411` Combat Steel Large, `1395` Xeno Steel Large, `408` Alien (POI/radioactive) |
| SV/HV | `1478` Plastic Small, `380` Steel Small, `393` Hardened Steel Small, `1594` Combat Steel Small |

## 16. Exported/localization-sensitive properties

```text
## IsAccessible: shows up in Control Panel
## O2Accept: "OxygenBottleLarge,OxygenBottleSmall"
## FuelAccept: "EnergyCell,EnergyCellLarge,EnergyCellHydrogen,FusionCell,BioFuel"
```

`O2Accept` and `FuelAccept` define which items can fill tanks; the first item in
the list is filled when withdrawing.

## 17. Cruise Amplifier implications

The Cruise Amplifier is currently built as a **class-less** block with:

```text
Group: cpgSignal
IsAccessible: true
IsActivateable: true
IsActivateableInCP: true
IsActivatedOnPlace: true
```

`IsAccessible: true` is required for the device to show in the control-panel /
block list. The class-less block has no native `Class:` behaviour; the mod uses a
block-ID scan fallback if the custom-device registry does not expose it. The
speed hook uses the proven `Disk.SaveView()` postfix from
`EmpyrionExtendedTestMod`, not `PublishProject()`.
