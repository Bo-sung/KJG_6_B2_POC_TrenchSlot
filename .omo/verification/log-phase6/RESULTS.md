# Phase 6 verification — 2026-10-31

No Unity application, Play Mode, Unity build or Unity API was executed.
Run the commands below from the repository root.

| Check | Command | Observed result |
|---|---|---|
| Controlled spin contracts | dotnet run --project .omo/verification/log-phase6/LogPhase6.csproj | 112 assertions passed |
| Phase 1 | dotnet run --project .omo/verification/log-phase1/LogPhase1.csproj -- . | 3067 passed |
| Phase 2 | dotnet run --project .omo/verification/log-phase2/LogPhase2.csproj -- . | 121 passed |
| Phase 3 | dotnet run --project .omo/verification/log-phase3/LogPhase3.csproj -- . | 6060 passed |
| Phase 4 | dotnet run --project .omo/verification/log-phase4/LogPhase4.csproj -- . | 78 passed |
| Phase 5 | dotnet run --project .omo/verification/log-phase5/LogPhase5.csproj -- . | 87 passed |
| Unified logging | dotnet run --project .omo/verification/log-merge/LogMerge.csproj | 320 passed |
| Runtime compile | dotnet build .omo/verification/log-phase6/CompileRuntime.csproj -v:q | 0 errors, 102 existing field warnings |
| Editor compile | dotnet build .omo/verification/log-phase6/CompileEditor.csproj -v:q | 0 errors, 114 existing field warnings |

The pure project compiles real logging sources, AmmoInventory and PlayerWallet with no UnityEngine references.
It reads current SlotMachineData/AmmoData/LogData JSON and supplies controlled faces, outcomes, state and payout.
Actual reel scheduling, input, visuals and gameplay reward evaluation remain user Unity checks.

Coverage:

- Successful spin IDs, pre-payment state, discount/base cost, copied original faces and symbols.
- Refund with no successful start or consumed spin ID; duplicate input/result and stale contexts.
- Miss, cash, ammo, combined cash/ammo inputs; actual zero, capped reserve and per-hand loaded grants.
- Weapon swap/akimbo during spin, canonical cause and transaction IDs without duplicate ammo acquisition.
- Shop/next-wave envelopes versus original wave, shared clock and closed-wave attribution.
- Death, return, quit, unknown exit; incomplete before session_end, restart isolation, multiple machines.
- Invalid/non-finite payloads, unknown/null state, disabled/storage-failed logs and failed observers.
- Static presenter checks for success/refund/identity/legacy event/input semantics and no legacy duplicate writer.

Synthetic JSONL: Docs/로그-시스템-6차.synthetic.jsonl.
User checks and payload interpretation: Docs/로그-시스템-6차-계약.md.
