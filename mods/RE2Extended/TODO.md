# RE2Extended technical debt

## Localization

Status: partially implemented for the first release. The Cruise Amplifier display
names and descriptions are injected at runtime through the game's
`Localization.Set(string,string)` method (time-of-writing English-only). Full
CSV/localization-table merging and non-English translations remain deferred.

The Cruise Amplifier has injected English entries. Future devices will still
fall back to internal identifiers until they receive entries, and non-English
languages fall back to English.

TODO:

- Consider replacing the hardcoded runtime entries with a `Localization.csv`
  fragment merged through `Localization.LoadCSV` or `Localization.Set` at a
  safe load point.
- If possible, merge `CruiseAmplifierCV`, `CruiseAmplifierSV`, and their `Info`
  keys into the in-memory localization data.
- If not possible, document the limitation and decide whether a scenario/CSV
  sidecar or a small UI patch is acceptable.
- Add proper English display names and descriptions for:
  - `CruiseAmplifierCV`
  - `CruiseAmplifierSV`
  - future devices
- Verify the icon path: `CustomIcon: CapacitorMS` versus model thumbnail.
- Verify display names in the build menu, tech tree, control panel, and
  blueprint editor.

## Display-name suffix

The game appears to use the block `Name` localization key for both the placed
block name and the inventory name. The first implementation therefore renders
both as "Cruise Amplifier". The requested inventory-only suffix
("Cruise Amplifier (CV)" / "Cruise Amplifier (SV)") likely requires either a
separate inventory item name path or a small UI/display-name patch.

TODO:

- Confirm whether any native block property provides a separate inventory label.
- If not, add a narrowly scoped display-name patch or accept the same name in all
  contexts.

## Other deferred work

- Recompute `MarketPrice` values from the pinned RE2 `ItemsConfig.ecf` and
  `Templates.ecf` in a build-time script.
- Replace the provisional craft times with final balance values.
- Add a proper block icon if the reused capacitor icon is not acceptable.
- Add automated build-time verification that block IDs `1521`/`1522` are still
  free in the active RE2 config.
- Add a multiplayer regression test for high-speed collision/docking.
