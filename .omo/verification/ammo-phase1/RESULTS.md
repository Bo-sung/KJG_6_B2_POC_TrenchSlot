# Ammo phase 1 verification — 2026-10-29

- `dotnet run --project .omo/verification/ammo-phase1/AmmoPhase1.csproj -- .`
  - Exit 0: `PASS: 37 checks; pure C# model/data only, no Unity execution.`
  - Covers four starting stocks/capacities, JSON weapon references, capped addition including int.MaxValue, partial consumption, invalid IDs/negative requests, event payload/no-op behavior, capacity changes with explicit discard/refusal, table immutability and fresh model initialization.
- `dotnet build .omo/verification/ammo-phase1/CompileRuntime.csproj --nologo --verbosity quiet`
  - Exit 0: 0 errors, 87 CS0649 warnings for serialized fields assigned outside C# source.
  - Compiles all runtime Scripts sources, including new files, against existing Unity assembly references using .NET SDK. Does not start Unity, execute Unity APIs, build a player, or run generators/analyzers.
  - First attempt failed because imported reference HintPaths were resolved from this verification directory. The verification project now rebases relative HintPaths to POC2; project source/reference files were not rewritten.
- Unity JsonUtility loading, log validation behavior, GameManager Awake and actual Title/Game/Retry flow: not executed. User verification required.
- Qoder task `task_b96c8a53edcf`, run `run_944934a7c81e`: both attempts failed at agent_readiness with timeout before task delivery. No Qoder-produced files or substitute worker. Failed dispatches ctx_ee46cc91f5e6 and ctx_350e177a4249 were released; reclaimable worker list empty. Main agent authored all changes.
