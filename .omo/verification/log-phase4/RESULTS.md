# Log phase 4 verification — 2026-10-31

- `dotnet run --project .omo/verification/log-phase4/LogPhase4.csproj --verbosity quiet -- .`
  - Exit 0. PASS: 78 phase-4 non-Unity checks; no Unity API executed. Pure test compilation treats warnings as errors.
  - Uses production pure combat collector/context/source/lifecycle/session classes, actual LogData.json, memory output and independent System.Text.Json parsing.
  - Covers unique single/shotgun shot IDs, per-projectile namespaces, actual launch origin snapshot, requested/created/failed counts, one shot event, duplicate impact/kill rejection, multi-target pellets, miss/contact/zero-damage distinction, in-flight weapon swaps, separate akimbo instances/hands, same-equipment refresh dedup and exact per-instance/type active duration, shop exclusion and interruption closure.
  - Covers actual before/after health loss (armor-applied input), overkill capped to remaining health, immutable attack source position, unknown legacy source, zero-health-loss suppression, fatal last source and damage included before death_snapshot/wave_summary/session_end.
  - Injects late results in shop and a new wave. Confirms full replacement corrections with original keys/revisions 1 and 2, original record unchanged, occurrence envelope distinct from origin, no current-wave attribution, retained summary release after last projectile, post-session logging rejection and fresh-session isolation.
  - Covers failed projectile creation, cancellation without hit/kill, logging disabled and failed store. A failing test reproduced retained summary state after session end; cleanup was made independent of recording eligibility, and the same test now passes without emitting new logs.
  - Static assertions inspect actual success-path order (TakeShot/Fired/HandFired/fallback origin/context/Launch/result/finalization), unchanged pelletGroup meaning, no duplicate legacy subscriptions, actual PlayerHealth calculation and observer-before-UI/death ordering, preserved overloads, melee/ranged source capture, immutable projectile propagation, pre/post enemy health observation, termination/cancel paths, equipment registration/detachment and new source metadata/GUID uniqueness.
  - Writes `Docs/로그-시스템-4차.synthetic.jsonl`, a synthetic combat and fatal lifecycle example; checks all lines and sequences. It does not execute gameplay.
- `dotnet run --project .omo/verification/log-phase3/LogPhase3.csproj --verbosity quiet -- .`
  - Exit 0, 6060 phase-3 checks passed. Regenerates phase-3 synthetic sample.
- `dotnet run --project .omo/verification/log-phase2/LogPhase2.csproj --verbosity quiet -- .`
  - Exit 0, 121 phase-2 checks passed. Regenerates phase-2 synthetic sample.
- `dotnet run --project .omo/verification/log-phase1/LogPhase1.csproj --verbosity quiet -- .`
  - Exit 0, 3067 phase-1 checks passed. Regenerates phase-1 synthetic sample.
- `dotnet build .omo/verification/log-phase4/CompileRuntime.csproj --nologo --verbosity quiet '-consoleLoggerParameters:ErrorsOnly;Summary'`
  - Exit 0, 0 errors, 100 existing CS0649 serialized/private-field warnings, matching prior runtime compilation.
  - Includes all runtime sources and API consumers through the existing glob/reference project. Unity assemblies are compile references only; no Unity API is executed.

Not executed: Unity import/Editor/Play Mode/player build, real collision/headshot/armor inputs, actual projectile spawn failure, death feedback, input/HUD/audio behavior, scene/prefab rendering, real Application.persistentDataPath or FPS impact. Current WaveController.Stop still cancels projectiles; cross-boundary correction tests use synthetic late callbacks, not a changed gameplay lifetime. The user checklist is in `Docs/로그-시스템-4차-계약.md`. No ammo-economy implementation, scene/prefab regeneration, BuildAll, commit or push.
