# Phase 5 verification — 2026-10-31

No Unity application or Unity API was executed.

| Check | Command | Observed result |
|---|---|---|
| Ammo contracts and actual inventory | dotnet run --project .omo/verification/log-phase5/LogPhase5.csproj | 87 assertions passed |
| Phase 1 regression | dotnet run --project .omo/verification/log-phase1/LogPhase1.csproj -- . | 3067 passed |
| Phase 2 regression | dotnet run --project .omo/verification/log-phase2/LogPhase2.csproj -- . | 121 passed |
| Phase 3 regression | dotnet run --project .omo/verification/log-phase3/LogPhase3.csproj -- . | 6060 passed |
| Phase 4 regression | dotnet run --project .omo/verification/log-phase4/LogPhase4.csproj -- . | 78 passed |
| Unified logging regression | dotnet run --project .omo/verification/log-merge/LogMerge.csproj | 314 passed |
| Runtime compile | dotnet build .omo/verification/log-phase5/CompileRuntime.csproj -v:q | 0 errors, 103 CS0649 warnings |
| Editor compile | dotnet build .omo/verification/log-phase5/CompileEditor.csproj -v:q | 0 errors, 115 existing field warnings |

Run from repository root. Compiles reference installed/cached Unity assemblies and do not execute them.
The current generated project references cached LogKit.Runtime.dll; no duplicate package source is included.

The pure tests compile the real AmmoInventory and logging sources with no UnityEngine dependency.
PlayerWeapon reload/slot/fire inputs are reproduced as explicit state transitions plus static source-hook checks.
They are not a PlayerLoop test and do not prove actual Unity ordering or presentation.

Covered: table-driven types and caps, actual partial/zero grants, 0/1 shot consumption, shotgun count,
both hands, partial reload cancellation, slot fill, world/new/initial loaded rounds, stable floor identity,
return/discard conservation, maximum changes, shortages, closed-wave attribution, death, disable/storage/snapshot failures.

The phase-1 ID test initially failed because it assumed exactly five ID kinds. It now checks uniqueness for all registered kinds.
The phase-5 sample is Docs/로그-시스템-5차.synthetic.jsonl and is synthetic input, not a Unity play log.

User Unity checks are in Docs/로그-시스템-5차-계약.md.
