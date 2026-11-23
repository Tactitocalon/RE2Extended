# Empyrion Extended Test Mod — Experiment Notes

Status: experiment complete. Experiment D succeeded: a locally field-type-corrected
Harmony 2.4.3 patched `Disk.SaveView()` without invalid IL. The user confirmed that
the speed limit actually changes. The current mod build sets the CV speed limit to
**300**. The earlier polling implementation was a diagnostic prototype, not the
intended final design.

Reusable findings are recorded in `docs/INTERNAL_MODDING_FINDINGS.md` at the
repository root.

## Objective

Determine how to implement a **clean, low-side-effect gameplay patch** against
Empyrion's managed internals on the current Mono runtime.

The CV speed limit is the test feature because:

- it has a visible UI result
- it has a narrow, identifiable native decision point
- it can be tested in single-player without save mutation
- it is easy to compare against SV behavior
- failure is obvious and localized

The real deliverable is the **patching mechanism and its operating rules**, not
the speed change itself.

## Desired properties

A successful implementation should:

- modify one game decision point rather than continuously rewriting state
- leave non-CV vessels on the original code path
- keep the UI and actual gameplay behavior consistent
- install and unpatch cleanly
- fail closed with a useful log message
- avoid save-file or persistent-data changes
- avoid broad patches with unrelated callers
- be understandable and small enough to maintain across game builds

## Non-goals

- permanently altering save data
- patching the entire movement system
- silently changing unrelated vessel types
- using field mutation as the final mechanism
- depending on a single metadata token without validation

## Why method patching is preferred

The native speed path is roughly:

```text
RbCtrlPlayerShip.FixedUpdate
  -> Disk.PublishProject
       -> Disk.SplitProcess
            -> SaveView / field-based speed branch
```

`Disk.PublishProject` is the narrowest final speed selector:

- its only caller in build 5232 is `RbCtrlPlayerShip.FixedUpdate`
- it covers both the normal `SplitProcess` path and the `SaveView` fallback
- it returns the float used by the ship movement calculation
- it avoids repeatedly mutating vessel objects

Patching `PublishProject` is therefore a better experiment than changing
`Disk` fields every fixed update.

## Evidence from the failed attempts

### Field polling did not work

The UI showed a CV limit of 60. That matches the `SaveView()` value for the
`ActionList` / atmosphere branch. The relevant logic is effectively:

```text
Disk.SplitProcess
  if !FormatConfig()
    return SaveView()
```

The polling prototype changed fields used only after `FormatConfig()` succeeds.
It therefore could not change the `SaveView()` result. The UI stayed at 60 and
the speed did not change.

### Harmony 2.3.3 postfix failed

The first method-level attempt patched `Disk.PublishProject` with a Harmony
postfix, but the runtime rejected the generated wrapper with invalid IL:

```text
InvalidProgramException: Invalid IL code in ... PublishProject_Patch1 ...
```

That proves the target choice was promising but the hook backend/package was not
reliable for this runtime.

## Clues from the third-party InventoryLock reference

InventoryLock is another mod, not part of this project. Its technique is useful
as evidence, not as code to copy.

Observed approach:

- uses **Harmony 2.4.3.0 Fat / net452**, not 2.3.3
- ships its own `0Harmony.dll` and resolves it with `AppDomain.AssemblyResolve`
- patches a stable game UI method (`UICamera.Notify`)
- uses a **prefix returning bool** to allow or suppress the original event
- uses `ref bool __state` plus a **finalizer** to restore temporary UI state
- uses `Application.Update` only for periodic visual work, not for the core
  gameplay decision
- resolves types/members by name and shape, with clear failure logs
- unpatches on shutdown

The important implication:

> Harmony can successfully patch a game method on this runtime when using the
> 2.4.3 Fat package. The 2.3.3 invalid-IL failure is likely package/version
> specific, or specific to how the original method is wrapped.

It does not prove that `Disk.PublishProject` will patch successfully, but it
gives a concrete next experiment.

## Experiment matrix

### Experiment A — Harmony 2.4.3 Fat postfix

Status: failed. Harmony 2.4.3 Fat produced the same invalid IL as 2.3.3 for
`Disk.PublishProject()`:

```text
InvalidProgramException: Invalid IL code in ... PublishProject_Patch1 ...
```

Target:

```text
Disk.PublishProject()
```

Postfix reads the current CV vessel and replaces `__result` with the SV-equivalent
speed. All non-CV paths return the original result.

Questions:

- does Harmony 2.4.3 generate valid IL for this target?
- does the UI reflect the changed value?
- does actual movement change?
- do HV/SV remain unaffected?

### Experiment B — Harmony 2.4.3 Fat prefix skip

Status: deprioritized. Harmony still copies the original method body into its
replacement wrapper even when a prefix returns false, so this is unlikely to avoid
the invalid-IL failure in `PublishProject()`.

### Experiment C — patch a simpler Disk method

Status: failed. Patching `Disk.SaveView()` with standard Harmony 2.4.3 produced
invalid IL as well. This showed the failure is not specific to `PublishProject()`.

Target:

```text
Disk.SaveView()
```

Why:

- the UI shows 60, matching the CV `SaveView()` fallback
- it has only two callers in the current image
- it is a much simpler method than `PublishProject()`
- it tests whether invalid IL is target-specific or affects all `Disk` methods

Postfix behavior: for CV only, return the SV branch's `SaveView()` value
(70 atmosphere / 130 space expected if CV is `ActionList`). Non-CV paths unchanged.

### Experiment D — field-type-corrected Harmony

Status: succeeded in the initial smoke test.

Observed log from the successful test:

```text
Harmony postfix installed: Assembly-CSharp.Disk.SaveView()
CV SaveView: class=ActionList space=False current=60 target=70
Applied CV SaveView target 70 in atmosphere.
```

This confirms:

- the patched Harmony installs on an `Assembly-CSharp.Disk` method
- the invalid IL was caused by same-name/different-type field resolution
- CV is the `ActionList` branch in this build
- the current CV UI/fallback path is `SaveView()` in atmosphere
- the postfix can change that branch at runtime

It does not yet confirm actual velocity, space behavior, non-CV isolation, or whether
the normal `FormatConfig() == true` branch also needs patching.

Hypothesis:

`Disk` has many fields with the same name (`keywordsID`) but different types.
MonoMod's member matching compares declaring type, name and member kind, but not
field type. During IL copy it can therefore resolve a `Disk::keywordsID : AspectEventArgs`
reference to `Disk::keywordsID : EmulatorService` (or another same-name field),
producing invalid IL.

The build now patches the pinned Harmony 2.4.3 net452 dependency locally:

- adds a FieldType comparison in `MonoMod.Utils.Extensions.Is`
- limits the extra comparison to nongeneric declaring types
- records the local modification notice and script
- keeps the Harmony source package hash/version pinned

Target remains `Disk.SaveView()` for the current feature test.

### Feature test — CV limit 300

Implemented in the current build: for CV only, `Disk.SaveView()` returns `300`
(increase-only). This is the first feature test after the successful hook experiment.

Questions to answer:

- does the UI show 300?
- does actual velocity approach/respect 300?
- does it work in space and atmosphere?
- are SV/HV unaffected?
- does the normal non-`SaveView` branch need a separate patch?

### Feature test 2 — Giga Speed Booster (CV limit 600)

Added in v0.6.0. The CV limit remains 300 by default. A placed
`GigaSpeedBooster` device raises it to 600.

Implementation observations:

- `Content/Mods` entries are loaded by `ModManager` as DLL-only mods. There is no
  config-provider path for `Content/Mods/<mod>/Configuration`; `DirectoryFactory`
  is the ECF loader used by config screens/commands, not the mod loader.
- The device is therefore added by patching `DynamicPropertiesCollection` loaders
  (`ReadEcf`, `LoadBinary`, `Read`) and merging a small ECF fragment into the
  in-memory BlocksConfig collection before block definitions are consumed.
- The fragment uses **fixed `Block Id: 1565`**. That ID was checked against
  vanilla, all installed built-in scenarios, and the installed RE2 config. It is
  outside the current RE2 named-block ID range (`2048..3563`).
- The block is a `Light`-class clone of `LightMS01`, so it can be detected through
  `IStructure.GetDevice<IDevice>(name)` or by matching the light device positions
  against the block-ID mapping.
- Detection is cached for one second per structure and falls back to the base 300
  limit if the structure API does not expose the device.

Questions to answer:

- Is the block visible/placeable in the CV building menu in creative and survival?
- Does `GetDevice(name)` or the light-position fallback detect it reliably?
- Does adding/removing the block change the UI and actual speed within ~1 second?
- Does the fixed `1565` reservation remain unused by future vanilla/RE2 updates?
- Does the block remain stable across save/restart and with Reforged Eden 2 loaded?
- Are multiple boosters still just one 600 limit (intended for this prototype)?

### Feature test 3 — Infinite Fuel Generator (server-authoritative)

Added in v0.7.0. The device is a cargo-container clone with fixed block ID
**1564**. Every 10 seconds, each generator container on a powered CV/SV/HV gets
one `FusionCell` item (localized as “Promethium Hydrocell” in RE2).

Assessment before implementation: this **requires the mod on the server-side
playfield process**. Container/inventory contents are authoritative state. A
client-only DLL cannot reliably create items in a server container; it would
only change a local/proxy view. The package now targets `PfServer,Dedi,Client`.

The Dedi master assembly on build 5232 exposes the Mod API interfaces but has no
`IPlayfield`/`IStructure`/`IContainer` implementation, and its
`ApplicationBridge.GetBlockAndItemMapping()` returns null. `ApplicationMode.DedicatedServer`
therefore cannot run the generator loop. The actual gameplay implementation must
run under `ApplicationMode.PlayfieldServer` (or `SinglePlayer`). The generator
logs this explicitly and stays inactive in Dedi master mode.

Implementation:

- runtime config merge now loads every `Configuration/*.ecf` fragment
- generator service subscribes to `Update`/`FixedUpdate` and runs every 10 seconds
- only `SinglePlayer` and `PlayfieldServer` modes activate the service; remote
  `Client` mode is deliberately skipped
- it scans `IPlayfield.Entities`, filters powered `CV`/`SV`/`HV` structures
- it finds `DeviceTypeName.Container` devices whose block type is 1564, with a
  block-name lookup fallback
- it calls `IContainer.AddItems(8517, 1)` and logs the first success/full-container
  event per structure

Questions to answer:

- Does `PfServer` receive a usable `ClientPlayfield`/`IPlayfield` on a live server?
- Does `IContainer.AddItems` replicate the new item to connected clients?
- Does the generated item survive save/restart?
- Does it work for CV, SV and HV, and skip bases/remote/AI structures?
- Does the container fill up and stop cleanly?

### Experiment E — retest PublishProject with corrected Harmony

Now that field-type-corrected Harmony works, retest `Disk.PublishProject()` as a
cleaner final patch point. It covers both `SplitProcess` and `SaveView`, so it may
be preferable to patching `SaveView()` alone.

### Experiment F — alternative hook backend

Only if the corrected-Harmony methods still cannot cover the required behavior:

- try a direct MonoMod/RuntimeDetour hook
- or patch a different narrow call site/helper
- do not fall back to field mutation as the final design

## Success criteria

The experiment succeeds when:

- the hook installs without invalid IL or runtime exceptions
- the log identifies CV and the target value clearly
- the CV UI speed limit changes
- the CV actually moves faster
- SV/HV behavior is unchanged
- atmosphere and space cases are both checked
- the original method is restored on shutdown
- the implementation does not continuously mutate vessel fields
- the custom device survives model loading and is detected on the local CV

## Recording results

For each experiment, record:

- Harmony/MonoMod package and version
- target type/method and build MVID
- patch kind: prefix, postfix, finalizer, transpiler
- whether installation succeeded
- whether UI value changed
- whether actual movement changed
- side effects observed
- whether unpatching worked
- next experiment
