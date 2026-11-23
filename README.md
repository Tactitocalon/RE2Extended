# Empyrion Mods

Mods and reverse-engineering notes for Empyrion: Galactic Survival 1.20.1
build 5232. This repository contains source and documentation only; it never
ships game assemblies, saves, logs or scenario data.

## Layout

| Path | Purpose |
| --- | --- |
| `docs/GAME_RE_PIPELINE.md` | Required workflow: game paths, tooling, indexing, probes and build pinning. |
| `docs/NATIVE_FINDINGS.md` | Living evidence log for verified game internals. |
| `docs/INTERNAL_MODDING_FINDINGS.md` | Completed findings and reusable patching workflow. |
| `mods/RE2Extended/` | Reforged Eden 2 extension mod; see its `README.md`. |
| `mods/EmpyrionExtendedTestMod/` | Minimal test mod used to validate the hooking approach. |

Generated indexes, decompiled game code and local build outputs live in
`artifacts/`, which is ignored and must not be committed.

## Requirements

- Windows with PowerShell
- .NET SDK
- Empyrion 1.20.1 build 5232 installed locally

The pipeline document records the expected install path, assembly SHA-256
values and MVIDs. Re-check those before trusting a build-specific symbol.

## Building a mod

Each mod has its own `build.ps1` and README:

```powershell
./mods/RE2Extended/build.ps1 -GameRoot 'F:\SteamLibrary\steamapps\common\Empyrion - Galactic Survival'
```

Builds write to ignored `bin/`, `obj/` and `package/<Mod>/` directories. Copy a
staged package into `<Empyrion>\Content\Mods` to install it manually.

## Working safely

- Client and dedicated server assemblies differ; never reuse tokens across them.
- Confirm a path with a runtime probe before turning it into a permanent hook.
- Do not commit game files, decompiled output, saves or logs.
- `third-party/` is reference material outside this project's source history;
  do not modify or copy it without checking its license.
