# Internal Modding Findings

Status: completed experiment, baseline Empyrion 1.20.1 build 5232.

This document records what we learned from the Empyrion Extended Test Mod. The
test feature was a CV speed reduction/limit change, but the real result is a
repeatable method for building small, guarded mods that patch Empyrion's managed
internals.

## Executive summary

The working recipe is:

```text
field-type-corrected Harmony 2.4.3 Fat/net452
  + exact game-build resolution
  + narrow postfix around the native speed decision point
  + CV/entity gating
  + versioned logging
  + runtime verification of both UI and actual behavior
```

The most important discovery was the reason standard Harmony failed:

> `Disk` contains many fields with the same name but different types. MonoMod's
> member matcher compared declaring type, name and member kind, but not field type,
> so IL copy could resolve the wrong field and generate invalid IL.

A local field-type correction in `MonoMod.Utils.Extensions.Is` fixed the problem.
With that correction, a postfix on `Disk.SaveView()` installed and changed the
CV speed limit; the user later confirmed the actual speed limit changed.

## What worked

- **Harmony 2.4.3 Fat/net452** as the base runtime.
- A locally patched `0Harmony.dll` with FieldType comparison added to the
  nongeneric field-matching branch.
- Loading Harmony through the mod loader before loading the runtime adapter.
- Resolving `Assembly-CSharp.Disk` and its method by exact build token, with a
  signature fallback.
- A postfix on `Disk.SaveView()`:
  - `object __instance`
  - `ref float __result`
  - type-gated to CV
- `EntityType.CV` verification through the vessel's native `EntityType` field.
- Increase-only behavior for the test.
- A startup log containing the actual loaded mod assembly version.
- Runtime evidence showing:
  - patched Harmony installed successfully
  - `Disk.SaveView()` postfix executed
  - CV class was `ActionList`
  - atmosphere current value was `60`
  - the postfix changed the returned limit
- A final test build setting the CV limit to `300`; user confirmed the speed
  limit changes in-game.

## What did not work

### Direct field mutation from `FixedUpdate`

The prototype read the local player's vessel and wrote `Disk` speed fields every
fixed update. It did not work because the active CV path was not the field-based
`SplitProcess()` branch:

```text
Disk.SplitProcess()
  -> FormatConfig() == false
  -> SaveView() == 60
```

The UI showing `60` was the clue. Writing `uriToken`, `previousFont`,
`childMessage`, or `categoryMap` cannot affect `SaveView()`.

This is a diagnostic technique at best. It is not the preferred final design.

### Standard Harmony 2.3.3

A postfix on `Disk.PublishProject()` installed through standard Harmony 2.3.3
produced invalid IL in the generated wrapper.

### Standard Harmony 2.4.3

The same invalid IL occurred on:

- `Disk.PublishProject()`
- `Disk.SaveView()`

This ruled out the idea that the failure was specific to one target. It also
showed that "InventoryLock uses Harmony 2.4.3 successfully" was not sufficient
evidence: InventoryLock patches NGUI code, not an `Assembly-CSharp.Disk` method
with duplicate field names.

### Prefix-skip as an immediate fix

A prefix that skips the original method is still worth knowing, but it was not
the right next step here because Harmony still copies the original method body
into its replacement wrapper. The invalid IL was in that copied body, not in the
decision to execute it.

### Shipping ECF blocks from a Content/Mods DLL

A `Content/Mods` mod is loaded by `ModManager`/`GroupLayout` as a DLL-only mod.
There is no automatic config provider for `Content/Mods/<mod>/Configuration`.
The game's ECF loader (`DirectoryFactory`) is used for config screens, console
commands and scenario/save config paths, not the mod loader.

For the v0.6.0 prototype the block is therefore added by patching
`DynamicPropertiesCollection` loaders (`ReadEcf`, `LoadBinary`, `Read`) and
merging a small ECF fragment into the in-memory BlocksConfig collection. This is
an experiment; the long-term supported mechanism (if any) is still unknown.

Do not blindly reuse a fixed block ID for an RE2-compatible block. RE2 uses
most fixed IDs in 1..2048 and its named blocks currently occupy 2048..3563. For
the prototype we reserved fixed ID 1565 after checking vanilla, all installed
built-in scenarios, and the installed RE2 config. If choosing another ID, verify
it against the full union of configs and keep the ECF ID and runtime constant in
sync. The v0.7.0 Infinite Fuel Generator uses 1564 by the same check.

### Dedicated-server gameplay authority

Container/inventory mutations are server-authoritative. A client-only DLL mod
cannot reliably create items in a real server container.

On build 5232 the **Dedi master** `Assembly-CSharp` exposes the Mod API interfaces
but does not implement `IPlayfield`, `IStructure`, `IEntity`, or `IContainer`.
`ApplicationBridge.LocalPlayer` is null and `GetBlockAndItemMapping()` returns
null. The gameplay loop therefore has to run in the **PfServer** process (or
`SinglePlayer`), which uses the full client-side assembly/bridge implementation.

Use this targeting rule:

- `Client`: client UI/physics and rendering-side config.
- `PfServer`: authoritative gameplay loops, structure/container access, item
  creation.
- `Dedi`: master-process features only; do not expect structure/container API.

A mod folder can declare `ModTargets: PfServer,Dedi,Client` and branch on
`IApplication.Mode` at runtime. Do not run authoritative item generation on a
remote `Client` target.

## Why standard Harmony failed

Symptom examples:

```text
InvalidProgramException: Invalid IL code in ... PublishProject_Patch1 ...
InvalidProgramException: Invalid IL code in ... SaveView_Patch1 ...
```

The failure was consistent with wrong field resolution during IL copy.

`Disk` has many fields named `keywordsID`, including fields of different types:

- `EmulatorService`
- `AspectEventArgs`
- `ConditionDictionary`
- other native types

MonoMod's matcher in `MonoMod.Utils.Extensions.Is(MemberReference, MemberInfo)`
checked:

- declaring type
- member name
- field/method/property/event kind

but not `FieldType` for fields. A field reference could therefore match the first
same-named field rather than the intended field with the correct type.

The local correction:

- adds a FieldType comparison for fields
- applies it only when the declaring type is nongeneric
- preserves upstream generic behavior

This is a dependency correction, not a game-file modification. The patched
Harmony is built from a pinned NuGet input and accompanied by its MIT license and
a local modification notice.

## Target-selection guidance

Do not choose a patch target only because its name sounds correct. Establish:

1. **Which branch is actually active at runtime.**
   - `PublishProject` vs `SplitProcess` vs `SaveView`
   - `FormatConfig()` true/false
   - space vs atmosphere
2. **Who calls the method.**
   - `PublishProject` had one caller.
   - `SaveView` had multiple speed/UI-related callers.
   - `SplitProcess` had many callers.
3. **Whether the method is a final selector or an intermediate helper.**
   - A final selector is attractive semantically.
   - A narrower helper may be easier to patch and still produce the desired
     visible/gameplay result.
4. **Whether the patch is safe for all callers.**
   - `SaveView()` is narrower than `SplitProcess()` in this context, but it still
     has more than one caller.
   - The postfix must gate on CV and any required local-player/ownership scope.

## Reusable workflow for future internal mods

1. Pin the game build, target assembly, MVID and hashes.
2. Decompile and index the relevant types/methods.
3. Follow callers and field reads to find the decision point.
4. Use runtime logs/UI to confirm the active native branch.
5. Build a field-type-corrected Harmony when patching obfuscated `Assembly-CSharp`
   methods with duplicate field names.
6. Resolve the target by exact build token plus signature/owner validation.
7. Prefer a narrow prefix/postfix over continuous state mutation.
8. Gate by type, entity, local player/ownership and playfield where relevant.
9. Fail closed and log the reason.
10. Verify:
    - patch installation
    - UI result
    - actual gameplay result
    - non-target behavior
    - shutdown/unpatch
11. Put the assembly version in the startup log so testers can prove which build
    loaded.
12. Record the verified contract and the runtime evidence.

## Practical patch pattern

The working shape is intentionally small:

```csharp
var harmony = new Harmony("ModName.feature");
harmony.Patch(
    target,
    postfix: new HarmonyMethod(typeof(Patch).GetMethod(nameof(Patch.Postfix))));

public static void Postfix(object __instance, ref float __result)
{
    if (!TargetIdentityMatches(__instance)) return;
    __result = NewValue;
}
```

Use `object __instance` or a resolved native type. Keep the patch callback
exception-safe and log only state changes.

## Scoping and side-effect checklist

Before calling an internal mod complete, test:

- [ ] Local player's target vessel only, unless all vessels are intended.
- [ ] AI/remote/non-owned entities are unchanged.
- [ ] Other vessel types are unchanged.
- [ ] Space and atmosphere are both checked.
- [ ] UI value matches actual behavior.
- [ ] Normal and fallback native branches are understood.
- [ ] Save/load and restart behavior is acceptable.
- [ ] Patch removal/unpatch is clean.
- [ ] No continuously repeated writes when a one-time result patch is sufficient.
- [ ] Multiplayer behavior is explicitly tested or explicitly unsupported.

## Open follow-ups

These are not required for the completed speed experiment, but are useful next
questions:

- Retest `Disk.PublishProject()` with the corrected Harmony. It covers both the
  normal and fallback branches and may be a cleaner final patch point.
- Determine whether the normal `FormatConfig() == true` branch needs a separate
  patch for other gameplay states.
- Decide whether CV speed changes should be scoped to the local player only.
- Test space behavior explicitly.
- Test SV/HV, AI CVs and multiplayer behavior.

## Reference implementation

Working test mod:

```text
mods/EmpyrionExtendedTestMod
```

Key pieces:

- `src/EmpyrionExtendedTestMod.Loader`
- `src/EmpyrionExtendedTestMod.Runtime`
- `scripts/patch-harmony-fieldtype.ps1`
- `docs/EXPERIMENT.md`

The patched Harmony dependency is generated during the build; it is not a game
file and no game assembly is modified.
