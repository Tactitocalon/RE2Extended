# RE2Extended technical debt

## Localization

Status: English localization uses `Configuration/Localization.csv`, merged with
`Localization.LoadCSV(byte[], merge: true)` after the base table loads. The
language-independent `Localization.Set` path remains only as a fallback when the
CSV or base table is unavailable.

Build-5232 finding: there is no native `Content/Mods` localization provider.
`Localization.LoadCSV(byte[], merge: true)` is the correct native table-merge
path; `Set(key, value)` only updates the language-independent fallback dictionary.

TODO:

- Add non-English columns to `Configuration/Localization.csv`.
- Add entries for future devices as they are introduced.
- Verify the `CapacitorMS` icon in the inventory, build menu, control-panel
  device list, tech tree, and blueprint editor.
- Verify display names in the same UI surfaces.

## Display-name suffix

Implemented without a UI patch. The amplifiers use one named block per vessel
type, with the ship-type suffix in the block localization key itself:

- `CruiseAmplifierCV` is named `Cruise Amplifier (CV)`.
- `CruiseAmplifierSV` is named `Cruise Amplifier (SV)`.
- The suffix is shown both in the inventory/build menu and when placed.

TODO:

- Verify the names in the inventory, build menu, control panel, tech tree, and
  blueprint editor.

## Block-map upgrades

Status: the runtime merges block fragments immediately before
`DirectoryConverter.CopyXmlFile` calls the game's named-block ID assignment.
Live logs confirm the early hook fires, the client now receives the block
fragments, and blueprint spawning with the new blocks works.

Observed caveat: the existing dedicated save's `blocksmap.dat` was not rewritten
after the playfield loaded. The in-memory mapping works, but the file still has
the pre-mod 1517 entries.

The runtime now also hooks the named-ID allocator by signature in both
PlayfieldServer and Client and pre-seeds RE2Extended names from `4095`
downward. This makes the IDs independent of RE2 growth and map persistence,
provided all processes have the same mod/scenario versions.

TODO:

- Verify client and server logs report the same high IDs for all injected blocks.
- Verify blueprint spawning and placed-build loading with high IDs.
- Simulate an RE2 update that adds a named block; confirm RE2Extended IDs do not
  move.
- Verify the "existing named block IDs kept" path when a map already contains
  RE2Extended names.
- Decide whether to keep `blocksmap.dat` unmodified or add a diagnostic-only
  migration check.
- Define the migration policy for worlds that already contain RE2Extended
  blocks placed under the previous low-ID assignment.

## Other deferred work

- Recompute `MarketPrice` values from the pinned RE2 `ItemsConfig.ecf` and
  `Templates.ecf` in a build-time script.
- Replace the provisional craft times with final balance values.
- Add a proper block icon if the reused capacitor icon is not acceptable.
- Add automated build-time verification that block IDs `1521`/`1522` are still
  free in the active RE2 config.
- Add a multiplayer regression test for high-speed collision/docking.
