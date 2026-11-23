# Custom device detection pattern

Status: implemented for the Cruise Amplifier on Empyrion 1.20.1 build 5232.

## Rule

Do **not** detect custom devices by:

- polling the whole structure every tick,
- scanning `IStructure.MinPos..MaxPos`,
- iterating every Light/Container/device bucket,
- relying on a hardcoded numeric block ID.

Use the game's existing per-structure device aggregate instead.

## Why

Empyrion already maintains the structure's `ConditionDictionary` incrementally.
The same aggregate is used by thrusters, power, mass, device counts and the
control-panel/device lists.

For thrusters, for example:

- placement adds the thruster force to a directional aggregate;
- destruction subtracts it;
- replacement subtracts the old contribution and adds the new one;
- movement reads the updated aggregate on the next physics tick.

The game therefore already knows when blocks are placed, removed or replaced.
We should read or hook that state instead of rediscovering it by scanning.

## Implemented lookup

```text
IStructure
  -> StructureBridge.ecc
  -> DatabaseToken.ActionLayout
  -> ActionLayout.ConditionDictionary
  -> ConditionDictionary.OpenImage("RE2Extended")
  -> BlockPosValueList.GetAllBlockPositonsOfType(mappedBlockId)
```

The runtime resolves the block ID dynamically from
`Application.GetBlockAndItemMapping()` using the ECF block `Name`. No numeric ID
is hardcoded.

## Dedicated mod bucket

RE2Extended injects its own `ConditionDictionary` bucket named `RE2Extended`.
All RE2Extended devices should use:

```text
Group: cpgTransportation
IndexName: RE2Extended
```

This avoids borrowing an existing gameplay bucket such as `Light`, `Thruster`,
`Teleporter` or `RCS`. The block does not need `Class: Light` or
`Ref: LightMS01` for detection.

The bucket is added through a Harmony postfix on the `ConditionDictionary`
constructor and is also ensured lazily before lookup. If the injection fails,
detection fails closed rather than falling back to scanning or another bucket.

`Name` remains the stable block identity. `IndexName` is the device-bucket key,
not a display name.

## Caching and polling

Detection results are cached per structure and block name. `SaveView()` may run
every frame, but the structure aggregate is only queried when the cache expires,
not on every frame.

The cache interval is deliberately short (currently 500 ms) because device
activation can change through signals or manual toggling.

The placement/removal path is already incremental inside the game; no full block
scan is needed there.

## Power and activation

There are two separate gates:

1. Host structure power:
   ```text
   structure.IsPowered
   ```
   If the vessel has no power, no amplifier boost is applied.

2. Individual device activation:
   ```text
   IBlock.Get(out type, out shape, out rotation, out active)
   ```
   Only `active == true` amplifiers count.

This covers:

- manual control-panel on/off,
- signal-driven activation/deactivation,
- devices that are present but switched off,
- destroyed/blocks removed from the aggregate.

`IStructure.IsShieldActive` and the under-attack gate are checked before the
amplifier count, so an active shield or recent combat also disables the boost.

## Contract stability

The `ConditionDictionary` path is internal and build-specific. Keep the
following pinned/validated:

- game build and `Assembly-CSharp` MVID,
- `StructureBridge` field of type `DatabaseToken`,
- `DatabaseToken` field of type `ActionLayout`,
- `ActionLayout` field of type `ConditionDictionary`,
- `ConditionDictionary.OpenImage(string)`,
- `BlockPosValueList.GetAllBlockPositonsOfType(int)`.

If any part is missing, fail closed and log the incomplete contract. Do not fall
back to whole-structure scanning.

## Future work

A stronger version should invalidate the cache from the same incremental hooks
that update `BlockPosValueList` (`Add`, `Remove`, `Replace`, `Update`,
`SetBlockValue`, `Clear`). That would reduce activation/placement latency further
and eliminate the remaining short polling interval.
