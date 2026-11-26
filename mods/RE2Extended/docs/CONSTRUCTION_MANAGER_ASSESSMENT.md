# Construction Manager — viability assessment

Status: **static assessment only. No gameplay code changed. Runtime probes are required.**
Baseline: Empyrion **1.20.1 build 5232**, client assembly MVID
`462483cd-2f1f-4a24-9c83-46d24b1cb3f4`.
Source idea: [`../notes/ideas.txt`](../notes/ideas.txt), "Constructor Manager".

Related:
- [`CONSTRUCTION_MANAGER_PLAN.md`](CONSTRUCTION_MANAGER_PLAN.md) — phased implementation plan
- [`DEVICE_DETECTION_PATTERN.md`](DEVICE_DETECTION_PATTERN.md)
- [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md)
- [`SERVER_DETECTION_ASSESSMENT.md`](SERVER_DETECTION_ASSESSMENT.md)

## Verdict

**Technically feasible, but not viable as a production feature in the current
RE2Extended architecture without a significant amount of new reverse
engineering.** It is much larger than the existing speed and voice features.

A narrow single-player or host-only prototype is viable:

- a custom block that is detected on the local structure;
- a simple job list and item queue UI;
- "Start Job" adds the queued item types to the native constructor queues.

The full idea — multiple named jobs, status tracking, cancellation, completion,
save/restart, multiplayer, and distribution across all constructors — has four
hard problems:

1. **Custom multi-pane NGUI UI** that is not tied to a native constructor.
2. **Persistent job state** (names, type, status, per-job item lists) where a
   class-less block has no native tile-entity data.
3. **Server-authoritative execution and client synchronization** in dedicated
   multiplayer. Existing RE2Extended features are client-local or use proven
   native state; this feature must mutate constructor queues on the playfield
   server.
4. **Job identity.** Native constructor queues store only item IDs/counts. They
   have no job ID, so start/cancel/completion cannot be tracked without an
   additional ledger and careful reconciliation with player edits.

The public Mod API makes the queue manipulation part much more feasible than
expected, but it does not solve persistence, UI, or multiplayer delivery.

## Confirmed static evidence

These are build-5232 observations from the decompiled client assembly. The
decompiler has obfuscated member names, so the C# type names below are the names
present in this build, not stable game concepts.

### Constructor UI structure

The vanilla constructor window is a prefab-driven NGUI window, not a generic
inventory screen. The controller:

- binds `ItemGridTemplates`, `ItemGridConstructionQueue`,
  `ItemGridConstructorInventory` and `ItemGridSubConstructionQueue`;
- drives an `ActivatorSettings` instance for the selected constructor;
- uses `CopyPciture`, `PrintProcess`, `RemoveItems`, `TestForm`,
  `ChangeSolution`, `EditAspect`, `OrderToolbar`, and related methods to edit
  the input and output queues;
- opens through `MenuSiteConnectionToken`/window infrastructure and relies on
  the game's native constructor replication path.

There are at least two constructor-window controller variants in the client
assembly (`LineScopeConnectionToken` and `ServerLoaderConnectionToken`), which
show the same pattern. A new mass-constructor UI would either clone one of
these prefabs and add a fourth pane, or replace it with a mod-owned UI.

### The constructor queue is a native tile entity

The constructor tile entity appears in this build as `ActivatorSettings`, which
derives from `BookmarkInvoker : NodeHelper`. `BookmarkInvoker` implements
`Eleon.Modding.IContainer` and exposes the queue through:

```text
IContainer.AddItems(int type, int count)       -> adds to queue
IContainer.RemoveItems(int type, int count)    -> removes from queue
IContainer.GetContent()                        -> List<ItemStack>
IContainer.SetContent(List<ItemStack>)         -> replaces queue
```

The queue is persisted by the native constructor tile entity through its normal
NBT/binary save path. It is not a mod-owned structure.

This is important: the public API can reach a constructor queue and mutate it
without patching `BlockConstructor` internals, if the constructor instance and
position can be resolved.

### Resolving constructor devices

`IStructure.GetDevice<T>(string blockName)` and
`IStructure.GetDevice<T>(VectorInt3 pos)` cast the native `NodeHelper` to `T`.
Since native constructors implement `IContainer`, this is statically a viable
way to get a constructor queue:

```text
IStructure.GetDevice<IContainer>(constructorBlockName)
IStructure.GetDevice<IContainer>(actualBlockPosition)
```

Open questions that must be probed:

- Does `GetDevice<IContainer>(blockName)` work for every constructor class
  (`ConstructorBig`, `ConstructorBigV2`, `ConstructorHover`,
  `ConstructorRecursive`, `ConstructorSmallV2`, `ConstructorSurvival`, etc.)?
- `GetDevice<T>(blockName)` returns one position. How do we enumerate **all**
  instances of a constructor type? `GetDevicePositions` is documented for the
  custom-device registry; it is not yet known whether constructor block names
  are registered there.
- If enumeration requires scanning the structure bounds, the existing
  `DEVICE_DETECTION_PATTERN.md` rule against whole-structure scans would have to
  be revisited or a native constructor bucket found.

### Item and event surface

`ItemStack` contains only `id`, `count`, `slotIdx`, `ammo`, and `decay`. Native
constructor queues therefore do not carry arbitrary job metadata.

`GameEventType.ItemsCrafted` exists and is raised on crafting completion. It
carries the crafted item type and related values, but no job identifier. It is a
completion signal candidate, not a job-tracking mechanism by itself.

## Hard problems in detail

### 1. Custom UI

The current mod has block injection, a narrow sprite-name fix, and local audio,
but no mod-owned NGUI window. The Job Manager UI needs:

- a job list with Name, Type, and Completed/Started status;
- selection of a job and its item queue;
- template selection on the right;
- Start Job / Cancel Job controls;
- input and output panes matching the native constructor's layout.

Options:

| Approach | Assessment |
| --- | --- |
| Clone the native constructor prefab and add a Job Manager pane | Most UI-authentic, but requires resolving prefab loading/window registration and managing duplicate native controllers. High RE cost. |
| Replace the constructor window with a fully mod-owned NGUI window | Full control, but reimplements template/inventory binding and all native constructor interactions. Very high cost. |
| Mod-owned IMGUI/UGUI overlay | Faster prototype, but visually and interaction-wise inconsistent with the game. |
| No new UI: use the existing constructor window plus a small "Start Job" control | Lowest risk, but does not satisfy the requested Job Manager design. |

The UI also needs to display state from a server-owned ledger. A one-client
prototype can keep the ledger client-local; multiplayer cannot.

### 2. Persistence

A class-less block has no native per-block tile entity. The existing
RE2Extended class-less pattern can be detected by a custom device bucket, but it
does not provide arbitrary NBT or a place to persist a job list.

Possible persistence designs:

| Design | Assessment |
| --- | --- |
| Use a native constructor-class block and native queue | The queue persists, but it stores only item IDs/counts. It cannot store job names, status, or multiple user-defined jobs. It also makes the manager an actual constructor. |
| Register a mod-defined `NodeHelper`/ECF class with custom NBT | Strongest design if the class registry can be extended, but no such registration path is documented or proven on this build. This is invasive native work. |
| Mod-owned state file per save | Feasible in principle, but must be keyed to the save/world, written only by the authoritative server process, migrated safely, and reconciled with removed/unloaded blocks. It also has to respect the project rule against writing game/save data. |
| Encode jobs in a vanilla inventory/LCD/sign block | Very hacky. Item stacks have no arbitrary metadata; job names/statuses would be limited or unreadable. Not recommended for production. |

Until one of these is proven, a "Completed" job list cannot survive
save/restart in a robust way. The first prototype should either accept
non-persistent jobs or explicitly use a native constructor queue as the only
persisted state.

### 3. Server authority and multiplayer

Constructor queues are world state. In a dedicated server they must be changed
in the playfield server (`ApplicationMode.PlayfieldServer`), where the full
world code and constructor tile entities exist. The dedicated master process is
stripped and must not execute this feature.

The client UI needs to:

1. send a Start/Cancel request to the playfield server;
2. receive job status and queue updates back;
3. avoid applying local-only changes that the server will overwrite.

The current project has no verified server-to-client custom packet route.
`INetwork.SendToPlayer` was inspected as returning `false` in the available
implementations. Client-to-playfield-server delivery exists, but the return path
is unresolved. Vanilla constructor UI works because it uses the game's own
constructor replication path; a mod implementing its own manager does not get
that path for free.

Possible routes:

- reuse the native constructor UI/queue path and let the game replicate queue
  changes, if a mod can drive it correctly;
- add a PfServer executor and client command packets, with a custom state
  delivery route (unverified);
- use a replicated signal/LCD/container as a state channel (indirect, limited,
  and fragile).

Multiplayer must remain out of scope until the command and state paths are
probed with two clients and a dedicated server.

### 4. Job identity, completion, and cancellation

The native queue is a single item list. Starting two jobs that both want
`SteelPlate` merges into one queue entry. There is no job ID attached to queue
entries.

Consequences:

- **Start** can be implemented by adding each job output item to the selected
  constructors' queues. That part is mechanically feasible.
- **Completion** cannot be read directly. It must be inferred from queue
  depletion, `ItemsCrafted` events, or a per-constructor reservation ledger.
- **Cancel after start** is unsafe by default: removing a matching item count
  from a constructor queue can remove orders the player placed manually before
  or after the job.
- **Player edits** to constructor queues must invalidate or reconcile the
  ledger.
- **Destroyed/unloaded constructors** must not silently lose a job or mark it
  complete.

A robust design needs a reservation/ledger model, not just a UI list. That is
additional state and additional failure modes.

### 5. Distribution capability

The idea requires sending items only to constructors that can process them.
The native config has separate constructor classes and the `AddinEditor`
system describes recipe/constructor capabilities. The distribution algorithm is
feasible, but it needs:

- a verified recipe -> constructor-class capability mapping;
- queue capacity/volume checks;
- handling of recursive/survival/small/big constructor differences;
- duplicate item aggregation and fair distribution;
- deterministic behavior when a constructor is destroyed or powered off.

This is engineering work, not a fundamental blocker, but it should not be
underestimated.

## Recommended probe sequence

Do these before writing feature code:

1. **Constructor enumeration probe**
   - In single-player, list every constructor on a test CV/BA.
   - Resolve each one with `GetDevice<IContainer>(position)`.
   - Confirm `GetContent`, `AddItems`, `RemoveItems`, and `SetContent` affect
     the visible constructor queue.
   - Determine whether all instances can be found without a full structure
     scan.

2. **Authority/replication probe**
   - Dedicated server + two clients.
   - Start a job by mutating queues on the client, on the PfServer, and on both.
   - Observe which changes persist, which replicate, and which are corrected.
   - Check save/restart after each case.

3. **Persistence probe**
   - Test whether a custom block can own a native `NodeHelper`/NBT class safely.
   - If not, test a mod-owned per-save ledger keyed by save identity, structure
     ID and block position, written by PfServer only.
   - Prove restore, unload/reload, block removal, and rollback.

4. **UI probe**
   - Clone/open the native constructor window from a mod.
   - Confirm a fourth pane or an added control can be bound without breaking
     native template/queue interactions.
   - If clone is not stable, fall back to a deliberately limited overlay.

5. **Completion/cancel probe**
   - Observe `ItemsCrafted` during a job.
   - Manually edit the constructor queue while a job runs.
   - Destroy a constructor and restart the server.
   - Decide whether completion/cancel can be made safe, or whether started jobs
     become immutable/untracked in the first release.

## Feasible scopes

### A. Full feature as written

Not recommended as the next implementation. It requires new UI architecture,
new persistent state, a PlayfieldServer executor, a client/server state channel,
and a job-ledger design. This is likely larger than the existing speed and voice
features combined.

### B. Single-player / host-only MVP

Viable after the constructor enumeration probe:

- one manager block;
- a job list kept in memory;
- queue templates from the native template list;
- Start Job distributes items through `IContainer.AddItems`;
- no save/restart persistence for job names/status;
- no safe cancellation after start;
- no dedicated multiplayer support.

This is useful as a proof of concept and to validate distribution.

### C. Persisted single-player / host-only version

Adds an external per-save ledger and completion tracking. Still no dedicated
multiplayer. This is a realistic first production shape if the persistence path
is acceptable under the project rules.

### D. Multiplayer production version

Needs the full authority/state-channel design. Do not start here.

## Recommendation

**Do not implement the full Construction Manager yet.** Run the five probes
above first. The most valuable early experiment is constructor enumeration plus
`IContainer` queue mutation, because it determines whether the feature has a
usable foundation at all.

If the goal is a quick in-game result, implement scope B or C and explicitly
defer the custom Job Manager UI and dedicated multiplayer. If the full idea is
required, treat it as a separate mod subsystem with its own contracts, not as an
extension of the current client-local feature set.
