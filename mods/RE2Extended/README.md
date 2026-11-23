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
- `docs/BLOCK_DEFINITION_CHECKLIST.md`
- `docs/DEVICE_CLASSES.md`
