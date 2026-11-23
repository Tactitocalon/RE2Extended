# Empyrion Game Reverse-Engineering Pipeline

Status: living document. Baseline established against Empyrion **1.20.1 build 5232**.

This document is the working method for inspecting Empyrion's managed game code and turning discoveries into small, guarded mods. It is intentionally separate from any pre-existing mod in `third-party/`; that tree is third-party work and is not the source of truth for this project.

## Goals

- Make the same discovery workflow repeatable across game updates.
- Keep large decompiled assemblies out of the agent context.
- Distinguish static guesses from runtime-verified behavior.
- Keep client and dedicated-server targets separate.
- Record enough evidence that a future agent does not need to rediscover the same symbols.

## Non-goals

- Modifying game files or shipping game assemblies.
- Committing decompiled game source, binary assemblies, saves, logs or private data.
- Treating metadata tokens or obfuscated names as permanent identities.
- Assuming a client-side observation is server-authoritative in multiplayer.

---

## 1. Environment

### Game installation

Baseline path:

```text
F:\SteamLibrary\steamapps\common\Empyrion - Galactic Survival
```

Important directories:

```text
Client managed assemblies:
F:\SteamLibrary\steamapps\common\Empyrion - Galactic Survival\Client\Empyrion_Data\Managed

Dedicated server managed assemblies:
F:\SteamLibrary\steamapps\common\Empyrion - Galactic Survival\DedicatedServer\EmpyrionDedicated_Data\Managed

Mods:
F:\SteamLibrary\steamapps\common\Empyrion - Galactic Survival\Content\Mods

Build marker:
F:\SteamLibrary\steamapps\common\Empyrion - Galactic Survival\BuildNumber.txt
```

`BuildNumber.txt` at the baseline reads:

```text
5232
v1.20.1
```

### Tools available on this machine

- PowerShell 5.1:

```text
C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe
```

- .NET SDK / runtime:

```text
C:\Program Files\dotnet\dotnet.exe
```

- ILSpy command line decompiler:

```text
%USERPROFILE%\.dotnet\tools\ilspycmd.exe
```

- Python:

```text
%LOCALAPPDATA%\Programs\Python\Python310\python.exe
```

- POSIX-style utilities through the agent shell: `grep`, `find`, `sha256sum`, `rg`.
- Game-supplied Mono.Cecil, useful for small inspection scripts:

```text
F:\SteamLibrary\steamapps\common\Empyrion - Galactic Survival\Client\Empyrion_Data\Managed\Trivial.Mono.Cecil.dll
```

Use `ilspycmd` for fast human-readable inspection. Use Mono.Cecil or `System.Reflection.Metadata` when you need exact metadata tokens, IL operands, xrefs, or a machine-readable index.

### Baseline assembly identity

These are useful for detecting a changed installation. Recalculate them rather than assuming.

| Target | File | SHA-256 | MVID |
| --- | --- | --- | --- |
| Client | `Client\Empyrion_Data\Managed\Assembly-CSharp.dll` | `28E43D961A14A636617747B8734E6EC89F47EA6E57A2EE3F31533237BF468369` | `462483cd-2f1f-4a24-9c83-46d24b1cb3f4` |
| Dedicated | `DedicatedServer\EmpyrionDedicated_Data\Managed\Assembly-CSharp.dll` | `3E88562650F63267D665CF8BE25E224D9B2D376DCF419C71E98F0464FF55C19E` | `fae3f936-680a-459b-99d0-6fe468a1f17f` |
| Client | `Client\Empyrion_Data\Managed\ModApi.dll` | `7A7372EA08EB97DF47829358B06B1502C4BCC29E164D533D6EAD0D1C25FF4C9D` | — |
| Dedicated | `DedicatedServer\EmpyrionDedicated_Data\Managed\ModApi.dll` | `7C5F24E25D0B1792EE59E9AE312F7F04BBD3540115B7890B394E835EDA86BD45` | — |

The client and dedicated server are **different code targets**. Do not reuse client tokens, type assumptions or contracts on the server without re-verification.

---

## 2. Recommended RE workspace

Create generated material outside tracked source, or keep only distilled contracts and notes in the repo:

```text
artifacts/game-re/
  <build>/<target>/
    build.json
    index/
      types.jsonl
      members.jsonl
      refs.jsonl
      strings.jsonl
    decompiled/        # optional, ignored/generated only
    probes/
    logs/

docs/NATIVE_FINDINGS.md

docs/native/
  contracts/
    client/
    dedi/
```

Track:

- this pipeline
- distilled findings
- small contract files
- probe source at most

Do not track:

- game DLLs
- full decompiled output
- large metadata dumps
- saves, databases, player logs
- raw scenario assets

The exact workspace may change, but keep the distinction between **generated/cache** and **reviewed findings**.

---

## 3. Build identification

Run this first. Every later result is tied to a build hash and target.

PowerShell:

```powershell
$Game = 'F:\SteamLibrary\steamapps\common\Empyrion - Galactic Survival'
$Client = Join-Path $Game 'Client\Empyrion_Data\Managed'
$Dedi   = Join-Path $Game 'DedicatedServer\EmpyrionDedicated_Data\Managed'

Get-Content (Join-Path $Game 'BuildNumber.txt')

Get-FileHash -Algorithm SHA256 -Path @(
  (Join-Path $Client 'Assembly-CSharp.dll'),
  (Join-Path $Client 'ModApi.dll'),
  (Join-Path $Dedi   'Assembly-CSharp.dll'),
  (Join-Path $Dedi   'ModApi.dll')
)
```

MVID:

```powershell
ilspycmd --dump-table Module --json (Join-Path $Client 'Assembly-CSharp.dll')
ilspycmd --dump-table Module --json (Join-Path $Dedi   'Assembly-CSharp.dll')
```

If a build changes, create a new target entry. Never silently overwrite an old baseline.

---

## 4. Build a searchable index

The index exists so the agent can ask targeted questions instead of decompiling the entire assembly.

An index should contain at least:

- type token, full name, namespace, base type, kind
- member token, declaring type, name, kind, full signature
- property getter/setter links
- method IL hash
- method locals, exception regions and referenced member/string operands
- xrefs: caller -> callee; method -> field read/write
- string literals and constants
- source-file path hints embedded in metadata

### Interim indexing with ILSpy

For quick work, dump metadata tables to JSON:

```powershell
$asm = Join-Path $Client 'Assembly-CSharp.dll'

ilspycmd --dump-table TypeDef   --json $asm > typedef.json
ilspycmd --dump-table Field     --json $asm > fields.json
ilspycmd --dump-table MethodDef --json $asm > methods.json
ilspycmd --dump-table Property  --json $asm > properties.json
ilspycmd --dump-table Module    --json $asm > module.json
```

Keep these dumps local. They are large and must not be pasted into the model context wholesale.

### Future indexed helper commands

The intended stable interface is:

```text
re index    --assembly <path> --target client|dedi --build <id>
re find     --name <regex>
re find     --string <literal>
re member   --token 0x06004867
re xref     --method 0x06004867
re il       --method 0x06004867
re decompile --type <full-name> [--method <name-or-token>]
re diff     --from <build> --to <build>
```

Until those scripts exist, use `ilspycmd` plus small Mono.Cecil scripts.

### ILSpy command examples

List classes, structs and interfaces separately:

```powershell
ilspycmd -l c $asm
ilspycmd -l s $asm
ilspycmd -l i $asm
```

Find candidate names:

```powershell
ilspycmd -l c $asm | Select-String -Pattern 'Ship|Vessel|Speed|Velocity'
```

Decompile a type:

```powershell
ilspycmd -t 'RbCtrlPlayerShip' $asm
```

Decompile a specific method by token:

```powershell
ilspycmd -m 0x06004867 $asm
```

Get IL for a specific method:

```powershell
ilspycmd -m 0x06004867 -il $asm
```

Prefer `-m <token>` for method inspection. In the installed ILSpy version, `-t <type> -il` can emit far more than the requested type.

### Mono.Cecil script pattern

Use Mono.Cecil to enumerate members and resolve operands without executing game code:

```powershell
Add-Type -Path 'F:\SteamLibrary\steamapps\common\Empyrion - Galactic Survival\Client\Empyrion_Data\Managed\Trivial.Mono.Cecil.dll'

$a = [Trivial.Mono.Cecil.AssemblyDefinition]::ReadAssembly($asm)
$t = $a.MainModule.GetType('Disk')
$t.Fields | ForEach-Object {
  '0x{0:X8} {1} {2}' -f $_.MetadataToken.ToInt32(), $_.Name, $_.FieldType.FullName
}
$a.Dispose()
```

For IL/xref work, inspect `MethodBody.Instructions` and resolve operands that are `FieldReference` or `MethodReference`. Record the exact operand target, not just the opcode bytes.

### Context discipline

Do not send these to the model:

- whole JSON metadata tables
- whole decompiled assemblies
- large directory listings
- repeated full method dumps

Send instead:

- a short candidate list
- one type or method
- one xref result
- the specific IL window
- a small runtime-log excerpt

This is the main way to control reverse-engineering token cost.

---

## 5. Discovery workflow

### Step 1: Identify a semantic entry point

Good starting points:

- user-facing strings and localization keys
- settings/config field names
- public or semi-public classes with meaningful names
- public Mod API methods and events
- existing runtime behavior visible in-game
- call graph around a known method

For example, for a player-ship speed change:

1. Search for `speed`, `velocity`, `thrust`, `vessel`, `ship`.
2. Find the player ship controller.
3. Find the value passed into movement/force calculations.
4. Follow callers and field reads.
5. Reject lookalikes such as debug HUD components or AI-only configuration.

### Step 2: Narrow to a candidate hook

Prefer, in order:

1. A small return-value method used by exactly one caller.
2. A postfix on a state transition method.
3. A field read/write that can be validated against a frame/instance.
4. A broad method with multiple callers only after proving the caller context.

Avoid patching shared utility methods when a narrower path exists.

### Step 3: Check client/server ownership

Before writing code, decide which target owns the behavior:

- client-only UI/physics/input
- dedicated server world simulation
- playfield server
- replicated state

Check both assemblies. A type name may exist in both but mean different things.

### Step 4: Write a probe

A probe is a temporary, opt-in Harmony mod or diagnostic build that:

- validates the exact assembly/build hash
- installs only the candidate hook
- logs instance type, caller, input values, return values and timing
- does not change gameplay state
- writes bounded logs
- can be removed without changing saves

Run the probe in the actual game state you care about. Static decompilation can tell you what a method does; only the probe tells you that this path is active for the scenario you care about.

### Step 5: Record the verified fact

Only after probe confirmation, create a contract entry. Include:

```json
{
  "name": "player-sv-max-speed",
  "target": "client",
  "build": "5232",
  "type": "Assembly-CSharp.Disk",
  "method": "PublishProject",
  "signature": "System.Single Assembly-CSharp.Disk::PublishProject()",
  "token": "0x06004867",
  "callerEvidence": "RbCtrlPlayerShip.FixedUpdate",
  "ilHash": "<sha256 of normalized IL/operands>",
  "semantics": "Return value is the player ship maximum speed input.",
  "runtimeEvidence": "probe log file/date and observed values",
  "failureMode": "disable speed patch only; do not crash the game"
}
```

Keep human notes alongside the machine contract. A JSON token alone is not enough.

---

## 6. Implementing a guarded mod

Minimum requirements for a new internal mod:

- explicit build/target check
- exact type/method resolution
- no hard dependency on an unverified token alone
- Harmony patch installed only after validation
- no exception may escape a callback into game code
- bounded logging
- config for enabling/disabling the feature
- clean unpatch at shutdown
- fail closed if the target moved or changed

For a first milestone, a small client-only mod is acceptable. Do not build a large framework before proving one behavior.

### Contract identity

Use this precedence:

1. type + member signature
2. owner/caller relationship
3. IL body/operand hash
4. build-specific metadata token

Tokens are locators, not identities. Names may be obfuscated or reused.

### Harmony patching caveat

Standard Harmony/MonoMod can generate invalid IL when patching obfuscated
`Assembly-CSharp` methods that contain same-name fields with different types.
`Disk` is one such type. The completed test shows that a locally field-type-corrected
Harmony is required for these methods.

See `docs/INTERNAL_MODDING_FINDINGS.md` for the working recipe, failure symptoms,
and the reusable patch workflow.

---

## 7. Game-update procedure

When `BuildNumber.txt` or a hash changes:

1. Create a new build record.
2. Re-index client and dedicated assemblies separately.
3. Diff the old and new indexes.
4. Re-resolve each contract:
   - exact signature still present?
   - same owner/caller relationship?
   - same IL semantics?
   - token shifted?
5. Run the existing probes against the new build.
6. Update contracts, notes and compatibility matrix.
7. Re-run mod tests.
8. Publish a version that explicitly states its supported build.

Expected effort:

- token-only shift: low
- renamed/moved method: moderate
- changed IL but same behavior: low-to-moderate
- redesigned subsystem: high
- movement/network authority changed: high

Never “fix” an update by guessing a new token from names alone. Prefer a probe plus caller/IL evidence.

---

## 8. Client vs dedicated server

The installed dedicated-server assembly is not a copy of the client assembly. It is much smaller and lacks the client player-ship controller path.

For multiplayer work:

- verify which side owns the behavior
- treat client and dedicated contracts separately
- test with one patched client and an unpatched client
- test with all clients patched
- check server logs, rubber-banding, collision, docking and playfield transitions
- do not assume a client-side speed change is server-authoritative

The official Mod API includes server-side entity/playfield/network interfaces, but it does not expose every internal physics or movement limit. Prefer legitimate API mechanisms where they exist. If the only way to enforce a rule is to fight the client's replicated movement, expect latency and rubber-banding and treat it as experimental.

### Multiplayer acceptance checklist

For any gameplay change intended for a dedicated server:

- [ ] Client and server mod versions are checked before play.
- [ ] A client without the mod cannot silently connect and desync the session.
- [ ] The server logs the effective configuration.
- [ ] One-client, two-client and all-client cases are tested.
- [ ] High-speed collision, landing, docking and playfield transition are tested.
- [ ] Server restart and client reconnect preserve expected behavior.
- [ ] The limitation is documented honestly.

---

## 9. Safety and repository hygiene

- Never edit files under the game installation unless explicitly installing a mod.
- Never commit game DLLs, decompiled output, saves, player databases, logs or private scenario data.
- Keep probes opt-in and separate from release builds.
- Do not copy code or data from `third-party/` into this project without license review; that tree is not part of this project’s implementation.
- Prefer local hashes and contracts over assumptions about “the current version.”
- If a build is unknown, leave the feature disabled and preserve diagnostic output.

---

## 10. Current baseline and known findings

See `docs/NATIVE_FINDINGS.md` for the current build-5232 notes and the first verified candidate path. That file is a living evidence log, not a guaranteed API.
