# Construction Manager — Phase 0/1 implementation notes

Status: **Phase 0 probe code and a Phase 1 UI fallback are implemented and build.
Runtime verification in a test world is still required.**
Baseline: Empyrion **1.20.1 build 5232**, client MVID
`462483cd-2f1f-4a24-9c83-46d24b1cb3f4`.

Related: [`CONSTRUCTION_MANAGER_PLAN.md`](CONSTRUCTION_MANAGER_PLAN.md),
[`CONSTRUCTION_MANAGER_ASSESSMENT.md`](CONSTRUCTION_MANAGER_ASSESSMENT.md).

## What was added

Runtime (`src/RE2Extended.Runtime/ConstructionManager/`):

| File | Purpose |
| --- | --- |
| `ConstructionManagerFeature.cs` | install/dispose, event wiring, open handoff, F8 opener |
| `ConstructionManagerBlock.cs` | manager block lookup and device-use trigger matching |
| `ConstructionManagerGameAccess.cs` | shared native reflection contracts |
| `JobModel.cs` | in-memory `Pending`/`Started` job model |
| `ConstructorRegistry.cs` | the seven-name constructor whitelist |
| `ConstructionManagerWindow.cs` | Phase 1 three-pane IMGUI window |
| `ConstructionManagerNativeModalLease.cs` | native modal ownership, stacking and concealment |
| `ConstructionManagerMenuEntry.cs` | optional Escape-menu opener button |
| `ConstructionManagerItemCatalog.cs` | native item/block names, descriptions, categories, grid masks and icon-atlas access |
| `ConstructionManagerRecipeCatalog.cs` | native constructor recipe lookup via `SymbolService.AllocateDockingPane` |
| `ConstructionManagerProbe.cs` | opt-in Phase 0 save/constructor/queue probe |
| `ConstructionManagerDiagnostics.cs` | prefixed logging |

Configuration:

- `Configuration/Blocks/ConstructionManager.ecf`
- `Configuration/Templates/ConstructionManager.ecf`
- localization rows in `Configuration/Localization.csv`

The manager block uses the `ConsoleDesk_C05` model and native `Class: Switch` so
there is a normal use interaction. It is detected by block name through the
RE2Extended condition bucket, not by a hardcoded numeric ID.

## Phase 0 probe

The probe only runs when the marker file exists next to the mod folder:

```text
<Empyrion>/Content/Mods/RE2Extended/ConstructionManager.probe
```

It runs once per loaded structure and logs:

- every `IApplication.GetPathFor(...)` path and whether it exists;
- the likely co-save path `<save>/Mods/RE2Extended/ConstructionManager.json`;
- block-name -> block-ID mapping for the manager and all seven constructors;
- custom device names relevant to constructors;
- manager positions from the `RE2Extended` bucket and the custom-device list;
- per constructor: block ID, `Constructor` bucket position count,
  custom-device position count, `GetDevice<IContainer>(name)`, the first
  `GetDevice<IContainer>(position)`, and a short queue dump;
- structure ID, owner Steam ID, last-visited ticks, bounds and manager position,
  for the identity-stability check;
- the first few `DeviceUsed`/`InventoryOpened`/`WindowOpened`/`BlockChanged`
  events with argument types, to help verify the block opener.

Queue mutation is deliberately separate and opt-in. Create a second file:

```text
<Empyrion>/Content/Mods/RE2Extended/ConstructionManager.probe.mutate
```

with the first non-empty line:

```text
itemType,count
```

The probe then tests `AddItems`, `RemoveItems` and `SetContent` on the first
queue of the first matching constructor. It always attempts to restore the
original content in a `finally` block. Do not use this on a valuable save; use a
copy or a disposable test world.

## Phase 1 UI

The plan preferred cloning the native constructor window. Static inspection
showed the native window is a prefab/NGUI controller bound to a specific
`ActivatorSettings` and is not safe to clone without a runtime UI experiment.
The implementation therefore uses the fallback path from the plan, upgraded with
the native-modal lease pattern demonstrated by Trade Ledger:

> A public native dialog owns cursor/input and normal Escape handling; the
> Construction Manager draws its own IMGUI visual surface and hides the native
> dialog canvas.

This is not a native constructor-window clone. It is a safer version of plan
option 3.

The window contains:

- default window size of 90% of the game screen;
- default Unity/game GUI skin (the earlier custom color theme was reverted);
- left: job list with Name, Type (`Once`), Status (`Pending`/`Started`), New Job,
  Cancel Job and rename field;
- middle: selected job queue with `[icon] [item name] [quantity] [Remove]` rows,
  plus Clear and Start Job;
- right: constructor-style filter buttons at the top of the template pane:
  - Category: Food, Medicine, Weapons & Items, Building Blocks, Components,
    Devices, Decoration Blocks; exactly one is always selected;
  - Grid Type: All, BA, CV, SV, HV; exactly one is always selected and defaults
    to All;
  - filter buttons have tooltips;
- right: searchable 5-column grid of square template cells showing the native
  item icon; hovering a cell shows the item name and localized item description;
- template clicks merge into an existing queue row with the same item:
  - click: +1
  - Shift+click: +10
  - Ctrl+click: +100
  - Ctrl+Shift+click: +1000;
- the template catalog is built through the same native lookup the constructor
  uses: `SymbolService.AllocateDockingPane(target)` for the normal constructor
  targets `BaseC`, `SmallC`, `LargeC` and `AdvC`; decorative/technical blocks
  without a constructor recipe are excluded;
- block categories come from native `RegistryOptions.optionsID`; item categories
  come from the `AddinEditor` Category property;
- block `CustomName`, `CustomIcon` and `Info` are read from the native
  `RegistryOptions` properties, with `AddinEditor` properties as fallback; this
  covers block groups such as `AdvancedDecoDeviceBlocks`;
- grid filtering uses the native `RegistryOptions` `ClientScope` value derived
  from `AllowPlacingAt` (`Base -> BA`, `MS -> CV`, `SS -> SV`, `GV -> HV`);
- template order follows the native global recipe list order used by
  `SymbolService`, not alphabetical mapping order; this keeps the base-game order
  such as Graphite, Iron Ore, Copper Ore in Components;
- bottom: status line and warnings;
- close button and draggable window;
- an optional owned UGUI button beside the native Escape menu.

Icons are read from the game's packed `ProcessManagerConnectionToken` icon atlas
(`Texture2D` + `UISpriteData` list), with a fallback scan of loaded `UIAtlas`
resources. Item display names and descriptions come from active
`AddinEditor`/`DynamicProperties` definitions and native localization. All of
this is read-only; missing icons render as a placeholder.

The overlay is in-memory only. It does **not** distribute items to constructors,
does not persist on restart, and does not attempt completion tracking. A
`Started` job can be removed, with the same explicit warning that constructor
queues are unchanged.

### Native modal lease

`ConstructionManagerNativeModalLease` uses `IGui.ShowDialog` with an empty
`DialogConfig` to obtain the game's normal modal cursor/input lease. It then
verifies ownership of the exact native window-stack entry and hides that entry's
`CanvasGroup`. The manager window is drawn only while that owned modal is on top.

The native UI members are resolved by shape (manager getter, `Stack<string>`,
string-keyed window registry, entry root/canvas/controller, dialog config/handler
and close method). If any required member is missing or ambiguous, the UI is
disabled and logged; the Phase 0 probe can still run.

This avoids raw `Cursor.visible`/`Cursor.lockState` writes and restores normal
native Escape handling, focus-loss close, and foreign-window behavior.

## Opening the window

Three triggers are wired:

1. `GameEventType.DeviceUsed` whose argument matches `ConstructionManager` or
   `Construction Manager` and the local structure contains a manager block.
2. `F8` while the local structure contains a manager block.
3. An owned `Construction Manager` UGUI button beside the native Escape menu.

All three request an open, but the manager can only adopt its modal when the
native window stack is empty. If a block interaction has left a native window
open, the request waits for up to 2.5 seconds for that window to close. The
Escape-menu button explicitly closes the native menu first and then uses the same
short handoff.

The Phase 0 event log is still the evidence needed to decide whether trigger 1 is
reliable. If it is not, the Escape-menu entry and F8 are the primary openers.

## Phase 1 test checklist

- build and install the staged package;
- place `Construction Manager` on a CV/BA/SV test structure;
- open with the block interaction; if a native window appears first, close it and
  confirm the manager follows, or use the Escape-menu button / `F8`;
- create several jobs, select them, rename, add many templates, scroll all panes;
- confirm category filters match the native constructor categories;
- confirm grid filters show only blocks allowed on the selected grid;
- confirm item clicks merge, and Shift/Ctrl/Ctrl+Shift add the expected amounts;
- edit counts, remove rows, clear a queue;
- press Start on an empty job (disabled) and on a job with rows (becomes Started);
- press Start again (disabled), then Cancel Job and confirm the warning;
- open and close the native constructor window and confirm it still works;
- restart the game and confirm the manager UI is empty again (expected for Phase 1);
- repeat at more than one UI scale/resolution.

## Open decisions before Phase 2

- **UI gate A:** accept the modal-leased IMGUI fallback for the single-player
  spike, or spend the time to bind a native/NGUI constructor-window clone.
- **Block opener:** confirm whether `DeviceUsed` fires for the `Class: Switch`
  console block. The Escape-menu entry and `F8` are available if it does not, or
  if the block interaction leaves a native window open.
- **Save path:** confirm whether `GetPathFor(AppFolder.SaveGame)` already points
  at the active save folder or at the `Saves` root.
- **Constructor enumeration:** confirm that the `Constructor` condition bucket
  contains every whitelisted constructor and that `GetDevice<IContainer>` works
  for each represented native class.
- **Manager identity:** confirm `IStructure.Id` plus block position is stable
  across save/restart, rename, docking and playfield reload before writing the
  co-save.
- **Recipe target set:** confirm whether `ConstructorT3` needs `SmallC`,
  `AdvC`, or both. The current UI unions `BaseC`, `SmallC`, `LargeC`, `AdvC`.
