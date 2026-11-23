# Empyrion Extended Test Mod

A small client-side test mod for Empyrion 1.20.1 build 5232.

## Purpose

This mod is an experiment to determine how to apply a **clean, low-side-effect
gameplay patch** to Empyrion's managed internals on the current Mono runtime.
The CV speed limit is only the test feature; the real goal is the patching method.

See [`docs/EXPERIMENT.md`](docs/EXPERIMENT.md) for the objective, success criteria,
evidence from failed attempts, and the experiment matrix.

This build is based on the successful **Experiment D** result: a field-type-corrected
Harmony 2.4.3 Fat postfix on `Disk.SaveView()`. Standard Harmony 2.3.3 and 2.4.3
both generated invalid IL on `Disk` methods. The likely cause was `Disk`'s many
same-name fields with different types, which MonoMod could resolve to the wrong
field during IL copy.

Current mod behavior:

- builds a local field-type-corrected Harmony 2.4.3 from the pinned NuGet input
- loads that Harmony from the mod's runtime folder
- patches `Disk.SaveView()` with a postfix
- detects CV through the game's vessel `EntityType`
- leaves HV alone
- sets the CV speed limit to **300** in both space and atmosphere (increase-only)
- adds the custom CV device **Giga Speed Booster** through a runtime config merge
- raises the CV limit to **600** while a Giga Speed Booster is placed on the CV
- adds the custom container device **Infinite Fuel Generator**
- on a powered CV/SV/HV, adds one `FusionCell` every 10 seconds to each generator
  container (RE2 localizes `FusionCell` as “Promethium Hydrocell”)
- runs the generator loop only in server-authoritative modes
  (`SinglePlayer` and `PlayfieldServer`), never on a remote multiplayer client
- logs installation, device detection and generation under `[ExtendedTestMod]`

### Giga Speed Booster and Reforged Eden 2 IDs

The device uses a **fixed block ID: 1565** (`Block Id: 1565`). It is not
auto-assigned.

Why 1565 was chosen:

- it is inside Empyrion's documented fixed block ID range (`0..2048`)
- it is unused by vanilla, all installed built-in scenarios, and the installed
  Reforged Eden 2 `BlocksConfig.ecf` (`3143225812`)
- it is outside the range currently used by RE2's named-block auto-assignment;
  the current RE2 save maps those named blocks to `2048..3563`
- it is a gap left by older/removed block definitions rather than an ID in an
  active contiguous vanilla/RE2 block family

This is a prototype reservation. If a future vanilla/RE2 update starts using
1565, change the one value in `Configuration/GigaSpeedBooster.ecf` and the
`BoosterBlockId` constant in the runtime.

The block reuses the vanilla CV light model/properties and is a `Light`-class
device so it can be found through the public structure API. It is intended for
prototyping, not balance.

Because ordinary `Content/Mods` DLL mods do not get a `BlocksConfig.ecf` loaded by
the game, the mod merges every `Configuration/*.ecf` fragment into the game's
in-memory `DynamicPropertiesCollection` at the moment BlocksConfig is loaded. It
does not edit game files or Reforged Eden 2 files.

### Infinite Fuel Generator and dedicated-server requirement

The Infinite Fuel Generator is a cargo-container clone with fixed block ID
**1564**. It is intended to answer the question: can a DLL mod make authoritative
gameplay changes on a dedicated server?

Assessment: **yes, but it must run in the server-side playfield process.**
Container contents are authoritative state. A client-only mod that calls
`IContainer.AddItems` would modify a local/proxy view and would not reliably
replicate to the real server container. The generator loop therefore runs only
when `IApplication.Mode` is `SinglePlayer` or `PlayfieldServer`.

The Dedi master process is a special case on this build: its `Assembly-CSharp`
contains the Mod API interfaces but no `IPlayfield`/`IStructure`/`IContainer`
implementation. `ApplicationBridge.GetBlockAndItemMapping()` returns null and
`ApplicationMode.DedicatedServer` has no playfield object. The generator logs a
clear message and stays inactive in Dedi mode. The actual gameplay loop needs the
`PfServer` target. The package therefore declares:

```yaml
ModTargets: PfServer,Dedi,Client
```

The Client target still provides the speed patch and rendering-side block config;
the PfServer target owns the container generation. The Dedi target is included so
the block config is available to the server-side config pipeline where possible,
but the master process cannot run the generator itself.

The generator scans the current playfield's entities, accepts only powered
`CV`/`SV`/`HV` structures, finds `Container` devices whose block type is `1564`,
and calls `IContainer.AddItems(8517, 1)` (`FusionCell` item-registry id) every 10 seconds. It logs
the first success per structure and the first full/rejected container per
structure.

Previous attempts:

- Harmony 2.3.3 and standard 2.4.3 postfixes on `Disk.PublishProject()` generated invalid IL
- standard 2.4.3 postfix on `Disk.SaveView()` also generated invalid IL
- field polling did not work because the active CV branch was `SaveView()` (UI showed 60)

The InventoryLock reference patches a method in the NGUI assembly, so it does not
exercise duplicate fields inside `Assembly-CSharp.Disk`. This build tests whether
the field-type fix is sufficient for `Disk` patches.

Status: the client speed hook is proven. The Giga Speed Booster and Infinite Fuel
Generator are prototype gameplay features. The generator intentionally tests the
server-authoritative path and is expected to be inactive in the Dedi master
process, active in `PfServer`/single-player, and inactive on remote clients.

Remaining follow-ups are about hardening, not proving the hook:

- local-player versus all-CV scoping
- space and atmosphere parity
- SV/HV, AI CVs and remote vessels remaining unchanged
- the normal (`FormatConfig() == true`) branch
- `Disk.PublishProject()` as a possibly cleaner final target
- docking, landing, collision and multiplayer behavior
- production-quality block item/template/icon and crafting integration
- verify `PfServer` actually receives the playfield/container API on a live server
- verify container writes replicate to connected clients and survive save/restart

See `docs/INTERNAL_MODDING_FINDINGS.md` in the repository root for the reusable
findings and workflow.

## Install

1. Build with `build.ps1`, or use the staged `package/EmpyrionExtendedTestMod` folder.
2. Close Empyrion.
3. Copy `EmpyrionExtendedTestMod` into:

```text
<Empyrion>\Content\Mods
```

4. Start the game and load a save, or start the dedicated server.

For multiplayer/dedicated testing the mod must be present on the server-side
playfield process as well as clients:

- Client install: gives the speed patch and renders the custom blocks.
- `PfServer` install: owns the Infinite Fuel Generator container loop; this is
  required for authoritative item creation.
- Dedi install: optional for the master process; it cannot run the generator on
  this build because that assembly has no `IPlayfield`/`IStructure` implementation.

The package declares `ModTargets: PfServer,Dedi,Client`. Copy the same mod folder
into the `Content/Mods` directory used by each process/install.

Do not install only on a remote client and expect the generator to work; container
state is server-authoritative.

## Uninstall

Remove the mod folder. The mod stores no save data.

## Baseline

- Game: Empyrion 1.20.1 build 5232
- Client `Assembly-CSharp.dll` MVID: `462483cd-2f1f-4a24-9c83-46d24b1cb3f4`
- Related native path: `Disk.PublishProject()` / `Disk.SplitProcess()`
- CV/SV branch fields: `uriToken`, `previousFont`, `childMessage`, `categoryMap`

A changed or unsupported build should be treated as untested.

## Debug checklist

Use the Empyrion log to check:

```text
[ExtendedTestMod] Empyrion Extended Test Mod starting (v0.7.0.0).
[ExtendedTestMod] Loaded bundled Harmony: 0Harmony, Version=2.4.3.0...
[ExtendedTestMod] Harmony postfix installed: ...Disk.SaveView()...
[ExtendedTestMod] Runtime mode=... assemblyMvid=...
[ExtendedTestMod] Device config merge installed on ... collection loader(s) for ... fragment(s).
[ExtendedTestMod] Injected block 'GigaSpeedBooster' into BlocksConfig from GigaSpeedBooster.ecf.
[ExtendedTestMod] Injected block 'InfiniteFuelGenerator' into BlocksConfig from InfiniteFuelGenerator.ecf.
[ExtendedTestMod] Booster probe: structure=... blocks=... devices=..., GetDevice(name)... blockMapping=...
[ExtendedTestMod] CV SaveView: class=... space=... booster=False (...) current=... target=300
[ExtendedTestMod] Applied CV SaveView target 300 ...
[ExtendedTestMod] CV SaveView: class=... space=... booster=True (...) current=... target=600
[ExtendedTestMod] Applied CV SaveView target 600 ...
[ExtendedTestMod] InfiniteFuelGenerator armed; initial mode=...
[ExtendedTestMod] InfiniteFuelGenerator active in mode SinglePlayer|PlayfieldServer.
[ExtendedTestMod] InfiniteFuelGenerator produced 1 FusionCell on structure ... (CV).
```

On a Dedi master process the expected diagnostic is:

```text
[ExtendedTestMod] InfiniteFuelGenerator inactive in DedicatedServer master mode: this assembly has no IPlayfield/IStructure implementation. Install/run the PfServer target.
```

If the log says no CV is detected, verify that the craft is actually CV and not HV.

If a device is not injected, check that the package contains the expected
`Configuration/*.ecf` fragments and that the `Config merge hook` lines appear.

If the booster is injected but `booster=False` while one is placed, report the
`Booster probe` line. That line records the structure API path that was tested and
the block-ID mapping. The device lookup is currently name-based, with a light-device
plus block-ID fallback.

If the log shows the CV but also says the wrapper did not expose `EmulatorService`,
the public Mod API bridge did not expose the expected native object.
