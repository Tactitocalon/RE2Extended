# Agent Instructions

Repository root: `D:/devel/Empyrion`

This project is for building and maintaining Empyrion mods that interact with the game’s managed internals. Start with the documented pipeline instead of rediscovering the game assembly from scratch.

## Read first

| File | Purpose |
| --- | --- |
| `docs/GAME_RE_PIPELINE.md` | Required workflow: game paths, tools, indexing, discovery, probes, contracts, updates, client/dedi rules, safety. |
| `docs/NATIVE_FINDINGS.md` | Living build-specific evidence log. Read before touching a previously researched system. |
| `docs/INTERNAL_MODDING_FINDINGS.md` | Completed findings and reusable workflow for patching Empyrion managed internals. |
| `mods/EmpyrionExtendedTestMod/README.md` | Current test implementation of CV speed matching and its baseline assumptions. |
| `mods/EmpyrionExtendedTestMod/docs/EXPERIMENT.md` | Objective, success criteria, failed approaches and next hooking experiments for the test mod. |

If those files change the rules below, follow the docs.

## Project boundaries

- `third-party/` contains work not created by this project. Do not treat it as project documentation, do not modify it, and do not copy code or data from it without explicit license review. Reuse ideas at most.
- Game files live outside the repository at the installed game path. Never commit game DLLs, decompiled output, saves, logs, databases or scenario assets.
- Generated indexes/decompiled material belongs under an ignored/generated workspace such as `artifacts/game-re/`, not in tracked source.
- Reviewed contracts and short findings belong in `docs/native/` or `docs/NATIVE_FINDINGS.md`.

## Hard rules

1. **Pin the target and build.** Record build number, client-vs-dedi target, SHA-256 and MVID before trusting a symbol.
2. **Client and dedicated assemblies are different.** Never reuse client tokens or type assumptions on the dedicated server.
3. **Tokens are locators, not identities.** Prefer type + signature + caller/owner relationship + IL semantics. Tokens are build-specific.
4. **Static discovery is not proof.** Decompiling a method shows what it may do; a runtime probe must confirm the path is active for the intended gameplay case.
5. **Read minimally.** Do not dump the whole assembly, full metadata tables or large decompiled files into the model context. Use targeted searches, single methods, xrefs and small log excerpts.
6. **Fail closed.** A changed or unknown build should disable the affected feature with diagnostics, not crash or guess.
7. **Do not modify the game installation** unless the user explicitly asks for an installed mod. Probes and tests must be opt-in and isolated.
8. **Document evidence.** Update `docs/NATIVE_FINDINGS.md` when a candidate becomes verified, rejected or obsolete.

## Quick start

1. Read `docs/GAME_RE_PIPELINE.md`.
2. Check `docs/NATIVE_FINDINGS.md` for the system you are about to touch.
3. Confirm the installed build and hashes with the commands in the pipeline document.
4. For a new system, build/update a local searchable index or use targeted `ilspycmd` queries.
5. Write a small runtime probe before creating a permanent hook.
6. Record the verified contract and only then implement the mod.

Baseline game path:

```text
F:\SteamLibrary\steamapps\common\Empyrion - Galactic Survival
```

Baseline build: Empyrion **1.20.1 build 5232**.
