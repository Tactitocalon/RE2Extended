# Construction Manager — phased implementation plan

Status: **Phase 0 probe code and a Phase 1 UI fallback are implemented. Runtime
verification is pending; see
[`CONSTRUCTION_MANAGER_PHASE01.md`](CONSTRUCTION_MANAGER_PHASE01.md).**
Baseline: Empyrion **1.20.1 build 5232**, client target first.
Related: [`CONSTRUCTION_MANAGER_ASSESSMENT.md`](CONSTRUCTION_MANAGER_ASSESSMENT.md).

## Grid terminology

The canonical BA/CV/SV/HV and native `Base`/`MS`/`SS`/`GV` mapping is recorded in
[`docs/NATIVE_FINDINGS.md`](../../../docs/NATIVE_FINDINGS.md#grid--vessel-terminology-reused-by-later-features).
Use that table for all future grid-aware features. Do not equate constructor
template targets such as `BaseC`/`LargeC`/`AdvC` with placement tokens.

## Product decisions

These decisions define the first implementation:

1. **UI is the primary risk.** Prove the mass-constructor UI in a single-player
   prototype before building the full feature.
2. **Persistence uses a co-save file.** Store Construction Manager data in the
   save's `Mods` tree, not in the native `global.db`, `game.dat`, or
   `blocksmap.dat`.
3. **Custom packets are a separate phase.** Investigate and implement the
   client/PfServer transport only after the single-player version works.
4. **Completion is deferred.** The Job Manager tracks only `Pending` and
   `Started`. A started job is not automatically marked completed, and the
   manager does not try to infer completion from queue state yet.
5. **Constructor discovery uses a hardcoded whitelist.** The first version only
   targets these block names:

   ```text
   ConstructorAlienT0
   ConstructorAlienT1
   ConstructorAlienT2
   ConstructorT0
   ConstructorT1V2
   ConstructorT2
   ConstructorT3
   ```

   Their active RE2 classes are:

   | Block name | ECF class |
   | --- | --- |
   | `ConstructorAlienT0` | `ConstructorT0` |
   | `ConstructorAlienT1` | `ConstructorBigV2` |
   | `ConstructorAlienT2` | `ConstructorRecursive` |
   | `ConstructorT0` | `ConstructorT0` |
   | `ConstructorT1V2` | `ConstructorBigV2` |
   | `ConstructorT2` | `ConstructorRecursive` |
   | `ConstructorT3` | `ConstructorSmallV2` |

   The whitelist lives in one code constant and is logged at startup. It can
   become config-driven later.

## Scope of the first release

In scope:

- a craftable/placeable Construction Manager block;
- a new manager window based on the native constructor UI;
- multiple user-created jobs with Name and Type (`Once` only);
- a Job Queue per job with input/recipe entries only;
- a template pane for adding items to the selected job;
- Start Job: distribute the selected job's items to known constructors;
- Cancel Job: remove the job record from the manager;
- co-save persistence for job records and manager identity;
- single-player and host-client first;
- clean diagnostics and fail-closed behavior.

Out of scope for the first release:

- automatic completion detection;
- safe cancellation of items already added to constructor queues;
- continuous/repeating job types;
- multiplayer custom packets;
- production dedicated-server support;
- migration of removed managers or deleted structures;
- configurable constructor lists beyond the hardcoded whitelist.

## Behaviour decisions to keep explicit in the UI

- A job starts as `Pending`.
- Start Job is enabled only for a `Pending` job with at least one queue item.
- After Start Job, the job becomes `Started`; Start Job is disabled.
- A `Started` job can be removed with Cancel Job, but **this only removes the
  manager record**. It does not remove items already queued in constructors.
  The UI must warn about this.
- No job is ever shown as `Completed` in the first release. That status is
  reserved for the deferred completion feature.
- Template clicks append to the selected job's queue. They do not start
  construction immediately.
- The manager does not transfer materials. Constructors still use their normal
  input containers/materials when they process their queues.

## Proposed runtime structure

```text
src/RE2Extended.Runtime/ConstructionManager/
  ConstructionManagerFeature.cs   # install/dispose, event wiring
  ConstructionManagerBlock.cs     # block identity, detection, UI opener
  JobModel.cs                     # Job, JobItem, status, schema version
  JobStore.cs                     # co-save load/save/migration/atomic write
  ConstructorRegistry.cs          # hardcoded block-name whitelist
  ConstructorService.cs           # enumerate constructors, resolve queues
  DistributionService.cs          # capability grouping and fair distribution
  ConstructionManagerWindow.cs    # UI binding and window lifecycle
  ConstructionManagerPatches.cs   # Harmony callbacks only, if needed
  ConstructionManagerDiagnostics.cs
```

The UI, store, and constructor layer stay separate. That makes the single-player
prototype testable before persistence or networking exist.

## Phase 0 — Contracts and probes

Implementation: opt-in probe and shared native contracts added. Enable with
`ConstructionManager.probe`; see
[`CONSTRUCTION_MANAGER_PHASE01.md`](CONSTRUCTION_MANAGER_PHASE01.md).

Goal: confirm the minimum native assumptions before committing to the UI.

Probes:

1. **Save path**
   - Confirm what `IApplication.GetPathFor(AppFolder.SaveGame)` returns.
   - Verify the observed precedent: saves already contain
     `<save>/Mods/<ModName>/...`, so the co-save can live at
     `<save>/Mods/RE2Extended/ConstructionManager.json`.
   - Confirm the active save folder can be identified without reading native
     save internals.

2. **Constructor API**
   - Resolve the hardcoded constructor names in a single-player test world.
   - Confirm `GetDevice<IContainer>(position)` returns each constructor queue.
   - Confirm `GetContent`, `AddItems`, `RemoveItems`, and `SetContent` affect
     the visible constructor queue.
   - Determine how to enumerate multiple constructors of the same block name.
   - Record whether `GetDevicePositions(blockName)` covers constructors or a
     fallback is required.

3. **Manager block interaction**
   - Choose how the manager block opens the UI. Candidates:
     - an interactive native class whose activation is hooked;
     - a constructor-class block whose window is replaced for this block name;
     - a mod-owned window opened from a control-panel/signal interaction.
   - Prove opening/closing does not break the native constructor window.

4. **UI spike feasibility**
   - Confirm the native constructor prefab can be cloned or extended.
   - Confirm the template grid and construction queue grid can be used by a
     second window without breaking the normal constructor.

5. **Identity stability**
   - Verify `IStructure.Id` plus block position is stable across save/restart.
   - Verify what happens on structure rename, dock/undock, and playfield reload.

Exit criteria: a small opt-in probe/log proves these assumptions, or the plan is
revised before UI implementation.

## Phase 1 — UI risk spike (single-player)

Implementation: modal-leased IMGUI fallback window, manager block, in-memory
jobs, and template/queue editing added. Native window cloning remains unproven.
See [`CONSTRUCTION_MANAGER_PHASE01.md`](CONSTRUCTION_MANAGER_PHASE01.md).

Goal: prove the highest-risk piece first. The UI can be ugly; it must be
structurally viable.

Deliverables:

- an ECF block `ConstructionManager` using `ConsoleDesk_C05` as the model;
- a working manager-window opener from the placed block;
- the window layout:
  - left: Job Manager list with columns Name, Type, Status, and a New Job button;
  - middle: selected job's Job Queue; input/recipes only, no output section;
  - right: native-style template list;
  - bottom: Start Job and Cancel Job buttons;
- placeholder/in-memory data to exercise the layout;
- template click adds a row to the selected job queue;
- scrolling, selection, add/remove job, and window close all work;
- no persistence and no constructor distribution yet.

Implementation options, in order of preference:

1. Clone/extend the native constructor window and add the Job Manager pane.
2. Instantiate a mod-owned NGUI window using the same prefabs/components.
3. Temporary IMGUI/UGUI overlay only if options 1 and 2 are blocked.

Exit gate:

- UI opens reliably from the block.
- Native constructor UI still works unchanged.
- The layout can hold enough jobs and items to be usable.
- The team accepts the chosen UI approach before deeper work.

If this gate fails, descope the UI early rather than building persistence and
constructor logic around an unusable window.

## Phase 2 — Job model and co-save persistence

Goal: make job records durable without touching native save data.

Deliverables:

- `JobModel` with a schema version.
- Manager identity: save key, structure id, block position, display name.
- `JobStore` under the active save's `Mods/RE2Extended` directory:
  - JSON, human-readable;
  - atomic write: temp file + replace;
  - previous-version backup;
  - schema version check;
  - fail closed on corrupt/unknown data;
- create/rename/delete Pending jobs, and persist on every mutation;
- load on manager open or playfield load;
- no completion logic.

Suggested shape:

```json
{
  "schemaVersion": 1,
  "managers": [
    {
      "managerId": "structureId:x,y,z",
      "structureId": 123,
      "position": { "x": 10, "y": 20, "z": 30 },
      "jobs": [
        {
          "jobId": "guid",
          "name": "Hull plates",
          "type": "Once",
          "status": "Pending",
          "items": [
            { "itemType": 1234, "count": 100 }
          ]
        }
      ]
    }
  ]
}
```

Rules:

- Only the authoritative process writes the co-save. In single-player that is
  the game process; in the later multiplayer phase it will be PfServer.
- The client never writes player-save data directly in multiplayer.
- If the save path or manager identity cannot be resolved, persistence is
  disabled with a clear log rather than writing to a guessed location.
- The co-save does not replace or modify the game's native save files.

Exit criteria:

- Jobs survive save/restart.
- A corrupt co-save is ignored safely and logged.
- Deleting the co-save has no effect on the native world save.
- Two different saves do not share manager data.

## Phase 3 — Constructor discovery and queue distribution

Goal: make Start Job work against real constructors.

Deliverables:

- `ConstructorRegistry` with the hardcoded whitelist.
- `ConstructorService` that resolves all matching constructors on the manager's
  structure.
- Per-constructor queue access through `IContainer`.
- Capability check from the native constructor recipe dictionary or a verified
  hardcoded fallback.
- Distribution algorithm:
  - flatten the selected job's items;
  - group by item type;
  - filter constructors that can process each type;
  - respect queue capacity where available;
  - distribute evenly/round-robin across eligible constructors;
  - call `AddItems` on each target queue;
  - log the exact allocation per constructor.
- Mark the job `Started` only after the distribution succeeds or partially
  succeeds with a logged warning.
- No completion tracking and no automatic removal of queue entries.

Initial compatibility policy:

- Prefer the native `ActivatorSettings` recipe dictionary to decide whether a
  constructor can process an item.
- If that lookup is not safely accessible, use a temporarily hardcoded
  class-to-recipe rule and mark it as provisional.
- Do not silently send an item to a constructor that cannot process it.

Exit criteria:

- A test structure containing every whitelisted constructor type receives the
  correct items.
- A job with items not supported by any constructor fails visibly.
- Constructor queues remain after save/restart.
- Starting a job never crashes or blocks the native constructor UI.

## Phase 4 — End-to-end single-player workflow

Goal: integrate the UI with the store and distribution layer.

Deliverables:

- New Job creates a `Pending` job and selects it.
- Template click appends to the selected job's queue.
- Selecting a job shows its queue.
- Start Job:
  - validates the job;
  - distributes through `ConstructorService`/`DistributionService`;
  - marks `Started`;
  - persists the new status;
  - disables Start Job for that job.
- Cancel Job:
  - removes a Pending job immediately;
  - for a Started job, shows a warning and removes only the manager record;
  - does not alter constructor queues.
- Error messages for:
  - no selected job;
  - empty queue;
  - no eligible constructors;
  - no manager structure;
  - co-save unavailable;
  - unsupported item.
- Status display only `Pending` and `Started`.

Exit criteria:

- Create several jobs, queue items, start them, restart, and confirm the job
  list and started statuses return.
- Cancel a pending job and a started job with the expected warnings.
- Confirm the native constructors process only the queues they normally would.

## Phase 5 — Single-player hardening and release candidate

Goal: make the single-player feature safe enough to use.

Deliverables:

- block placed/removed/replaced handling;
- manager identity validation on load;
- handling for structure unload/reload and playfield transitions;
- co-save pruning policy for managers whose block no longer exists;
- versioned co-save schema and a migration hook;
- diagnostics: resolved constructors, queue counts, distribution result, save
  path, invalid manager records;
- fail-closed behavior for missing permissions, corrupt files, or unknown game
  build;
- no exception escapes a Harmony callback;
- localization entries and icon verification;
- install/uninstall notes, including what happens to the co-save and queued
  constructor items.

Exit criteria:

- Single-player save/restart, block pickup, and re-placement tests pass.
- Removing the mod leaves native saves readable and does not corrupt native
  data.
- Co-save can be deleted to reset manager data without affecting the world.

## Phase 6 — Custom packet investigation and implementation

Goal: prove a reliable client/PfServer transport before attempting multiplayer.

This phase is deliberately separate from the single-player implementation. It
does not block Phase 1–5.

Investigation tasks:

1. Map the available `INetwork` methods on build 5232:
   - client -> PfServer;
   - PfServer -> client;
   - Dedi master relay, if any.
2. Probe with two clients and a dedicated server:
   - register send/receive handlers;
   - send a request from a client to PfServer;
   - send a response from PfServer back to the requesting client;
   - verify delivery, ordering, reconnect behavior, and payload size limits.
3. Inspect native `NetworkBridge`/`ModApiBridge` paths for a working
   server-to-client route if `INetwork.SendToPlayer` is unavailable.
4. Decide whether to reuse a native replicated channel (signals, containers,
   LCD state) as a fallback.

If a route is proven, implement:

- a versioned message envelope;
- request IDs and timeouts;
- source/authority validation;
- payload size limits and rate limiting;
- clean failure when either side is unmodded or out of date.

Exit gate:

- A reliable request/response round trip is demonstrated on a dedicated server
  with two clients and a restart.
- If no return path is proven, multiplayer remains out of scope.

## Phase 7 — Multiplayer integration

Goal: run the Construction Manager authoritatively on PfServer.

Deliverables:

- job store and distribution execute in PfServer;
- client UI sends Start/Cancel/New Job etc. through the Phase 6 protocol;
- PfServer sends job state and distribution results back to the client;
- co-save writes happen only in PfServer;
- permissions/faction ownership checks before mutating a manager;
- reconnect and playfield transition recovery;
- multiple manager blocks and multiple clients;
- no completion tracking yet.

Exit criteria:

- Two modded clients see the same manager state.
- Only the authoritative server writes the co-save.
- Restarting the server preserves Pending/Started jobs.
- Unmodded clients do not break the server or the manager.

## Phase 8 — Deferred enhancements

Only after the single-player and multiplayer foundations are stable:

- completion tracking via a reservation ledger and `ItemsCrafted`/queue
  reconciliation;
- safe cancellation of items already distributed to constructors;
- job types other than `Once`;
- configurable constructor registry/capabilities;
- factory-management integration;
- migration of old manager records.

## Test matrix by phase

### Phase 1

- open/close manager window repeatedly;
- create/select multiple placeholder jobs;
- add many queue items and scroll;
- native constructor window still opens and crafts normally;
- resolution/layout at multiple UI scales.

### Phase 2

- create/rename/delete jobs; restart;
- corrupt co-save; unknown schema;
- two saves with separate manager data;
- co-save write failure and read-only save folder.

### Phase 3

- all seven hardcoded constructor names;
- multiple constructors of the same name;
- unsupported item for a constructor;
- queue capacity boundary;
- constructor destroyed/unloaded during distribution;
- restart with queued items.

### Phase 4

- new job -> add items -> start -> restart;
- cancel pending and started jobs;
- empty job, no constructors, no structure;
- repeated Start clicks (must not duplicate distribution).

### Phase 5

- pick up/replace manager block;
- remove structure;
- playfield transition;
- old co-save version;
- uninstall with native save backup.

### Phase 6

- client -> PfServer request;
- PfServer -> client response;
- two clients, one manager;
- reconnect;
- unmodded client;
- large payload/repeated packets.

### Phase 7

- concurrent edits from two clients;
- server restart;
- manager removed while a job is started;
- client leaves/rejoins the playfield;
- permissions/faction checks.

## Decision gates

| Gate | After | Decision |
| --- | --- | --- |
| A | Phase 1 | Is the cloned/extended UI viable? If not, descope UI or choose overlay. |
| B | Phase 3 | Do constructor enumeration and queue mutation work reliably? If not, stop before workflow work. |
| C | Phase 6 | Is bidirectional custom packet delivery proven? If not, no multiplayer. |

## Recommended first implementation slice

Start with Phase 0 probes, then Phase 1. Do not begin with persistence or
networking. The UI is the largest uncertainty and the fastest way to discover
whether the full feature is worth building.

The first playable milestone should be:

```text
place manager block
  -> open manager window
  -> create a job
  -> add templates to its queue
  -> press Start
  -> items appear in the hardcoded constructors' queues
  -> restart
  -> job is still listed as Started
```

No completion, no cancellation rollback, no dedicated multiplayer.
