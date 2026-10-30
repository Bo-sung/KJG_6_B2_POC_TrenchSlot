# Log phase 2 verification — 2026-10-31

- `dotnet run --project .omo/verification/log-phase2/LogPhase2.csproj --verbosity quiet -- .`
  - Exit 0. PASS: 121 phase-2 non-Unity checks; no Unity API executed. Warnings are treated as errors in this pure test project.
  - Tests real LogGameLifecycle/LogSession/serializer/memory storage with the current LogData.json and an injected UTC clock; System.Text.Json independently parses each output record.
  - Checks one session_start, one normal/interrupted summary per wave, first-wave-before-exit nulls, no fabricated rewards, actual zero bonus versus unpaid null, frozen plans/start snapshots, post-reward end snapshots, early clear remaining open, actual fixed tick/shop time, counter resets, correct final/last-completed wave and total kills.
  - Checks death_snapshot -> interrupted wave_summary -> session_end, separate hand ammunition, copied pre-cleanup counts/radius, unknown last damage, future last-damage value snapshot, duplicate death/quit/destroy suppression, closed old-session callbacks and fresh-session identity/sequence/time.
  - Checks collector ownership/reset/close-once, reentrant end rejection during close, explicit failed/null collector results, collector begin/close/NaN failures, disabled output and storage-factory failure without blocking lifecycle completion. Tests loaded-data SHA256 culture independence and value sensitivity.
  - Static assertions inspect actual GameManager hook order before cleanup and after rewards, initialization before first-wave coroutine, Title preparation before LoadScene, playing/Game-scene gate, no OnDisable voluntary exit, exactly one fixed-clock call without changing Time, actual hand/capacity APIs, living-list query/dead exclusion and metadata/GUID uniqueness for all 3 new sources.
  - Writes `Docs/로그-시스템-2차.synthetic.jsonl`, a synthetic lifecycle example with test-only IDs/values. This does not execute Unity or demonstrate actual gameplay.
- `dotnet run --project .omo/verification/log-phase1/LogPhase1.csproj --verbosity quiet -- .`
  - Exit 0. PASS: 3067 non-Unity checks. Phase-1 serialization/storage/clock/ID regression remains passing after collection-boundary validation was added. Regenerates the synthetic phase-1 sample.
- `dotnet build .omo/verification/log-phase2/CompileRuntime.csproj --nologo --verbosity quiet '-consoleLoggerParameters:ErrorsOnly;Summary'`
  - Exit 0. 0 errors, 100 existing CS0649 serialized/private-field warnings, matching the pre-integration compilation count.
  - Compiles all runtime sources including the GameManager partial extension, WaveController query and GameOverPresenter route against the existing Unity assemblies; does not execute their methods or launch Unity.

Failure caught during development: a collector returning NaN originally caused the entire wave_summary to be rejected. The new failing collector case now passes after finite-number validation moved into CopyPayload as well as final-envelope serialization. Collector failure is isolated to failed/null and lifecycle records remain. A test reading a nullable double position with GetInt32 was corrected to GetDouble; no production number coercion was added.

Not executed: Unity Editor/import, Play Mode, Unity API execution, Unity player build, real scene ordering/collisions/radius comparison, actual persistentDataPath files, real timeScale scheduling, slot callbacks in play or performance. The user checklist is in `Docs/로그-시스템-2차-계약.md`. No scene/prefab regeneration, BuildAll, commit or push.
