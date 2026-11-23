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

Implemented without a UI patch. Vanilla thrusters use an invisible `*Blocks`
group block with `ChildBlocks`; the placed child block points back with
`TemplateRoot`. The group's localization key carries the ship-type suffix, while
the child block's key is the short placed name.

The Cruise Amplifiers now follow the same pattern:

- `CruiseAmplifierCVBlocks` / `CruiseAmplifierSVBlocks` are the inventory groups.
- Their names are `Cruise Amplifier (CV)` / `Cruise Amplifier (SV)`.
- `CruiseAmplifierCV` / `CruiseAmplifierSV` remain `Cruise Amplifier` when placed.

TODO:

- Verify in-game that the single-child groups appear as direct inventory entries
  rather than requiring a variant-selection step.

## Other deferred work

- Recompute `MarketPrice` values from the pinned RE2 `ItemsConfig.ecf` and
  `Templates.ecf` in a build-time script.
- Replace the provisional craft times with final balance values.
- Add a proper block icon if the reused capacitor icon is not acceptable.
- Add automated build-time verification that block IDs `1521`/`1522` are still
  free in the active RE2 config.
- Add a multiplayer regression test for high-speed collision/docking.
