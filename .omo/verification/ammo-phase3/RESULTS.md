# Ammo drops verification — 2026-10-29

- Runtime sources compiled outside Unity: 0 errors, 90 CS0649 warnings (serialized fields).
- Command: `dotnet run --project .omo/verification/ammo-phase3/AmmoPhase3.csproj --verbosity quiet -- .`
- Result: exit 0, `PASS: 22 pure C# data/rule/inventory checks; no Unity API execution.`
- Tests call actual AmmoInfo.Find/DropAmount/ShouldDrop and AmmoInventory methods. Cover JSON settings/mapping, rounding, deterministic probability endpoints (including Random.value's inclusive 1), invalid inputs, reserve addition/capping and independent ammo types.
- Static asset inspection: prefab internal fileIDs, external GUID presence, new meta GUID uniqueness and saved Game scene link passed.
- Code review: owned pickup membership, reservation before inventory event emission, full reserve no-op, partial pickup consumption, LOS reuse, phase/health/distance checks, duplicate enemy death prevention and cleanup ownership.
- Not executed: Unity Editor/import, JsonUtility/validation logs, Play Mode, physics LOS, actual pickup lifecycle, world text rendering, controller interaction, Editor builder or player build. These require user verification under use-orchestration restrictions.
