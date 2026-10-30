# Phase 7 verification — 2026-10-31

No Unity application, Play Mode, Unity build, Unity API or file-verifier menu was executed.
Run commands from repository root.

| Check | Command | Observed result |
|---|---|---|
| Presentation and purchase contracts | dotnet run --project .omo/verification/log-phase7/LogPhase7.csproj | 100 assertions passed |
| Phase 1 | dotnet run --project .omo/verification/log-phase1/LogPhase1.csproj -- . | 3067 passed |
| Phase 2 | dotnet run --project .omo/verification/log-phase2/LogPhase2.csproj -- . | 121 passed |
| Phase 3 | dotnet run --project .omo/verification/log-phase3/LogPhase3.csproj -- . | 6060 passed |
| Phase 4 | dotnet run --project .omo/verification/log-phase4/LogPhase4.csproj -- . | 78 passed |
| Phase 5 | dotnet run --project .omo/verification/log-phase5/LogPhase5.csproj -- . | 87 passed |
| Phase 6 | dotnet run --project .omo/verification/log-phase6/LogPhase6.csproj -- . | 112 passed |
| Unified logging | dotnet run --project .omo/verification/log-merge/LogMerge.csproj | 330 passed |
| Runtime compile | dotnet build .omo/verification/log-phase7/CompileRuntime.csproj -v:q | 0 errors, 102 existing field warnings |
| Editor compile | dotnet build .omo/verification/log-phase7/CompileEditor.csproj -v:q | 0 errors, 114 existing field warnings |

Pure tests compile the logging contracts, real AmmoInventory/PlayerWallet and current ShopData/AmmoData.
Controlled observation inputs cover each purchase type, quote/actual amount differences, zero/null, repeated parts,
presentation deduplication/revisions/reopen/redraw, hidden rows, clipping math, explicit/stale/direct references,
failure classification, actual wallet refusal, free actions, cause isolation, session end, storage and record-size limits.

Static checks cover each existing GameManager wrapper, unchanged free APIs, actual selected-part/row-count collection,
viewport/alpha/mask filtering, UI reference forwarding, metadata and removal of the old purchase writer.
They do not execute GameManager/ShopInfo, actual UI layout, keyboard/gamepad navigation or physics.

The current project has no paid-heal item or TryBuyHeal API. Slot-only recovery was preserved;
no obsolete heal item, price or maximum-health purchase failure was synthesized.

Synthetic example: Docs/로그-시스템-7차.synthetic.jsonl.
Payload/coverage limits and user Unity checklist: Docs/로그-시스템-7차-계약.md.
