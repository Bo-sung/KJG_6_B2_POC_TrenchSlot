# Ammo shop verification — 2026-10-29

- `dotnet run --project .omo/verification/ammo-phase4/AmmoPhase4.csproj --verbosity quiet -- .`
  - Exit 0; 28 checks passed against actual AmmoShop, AmmoInventory, PlayerWallet and JSON.
  - Covers 199/200 balances, full/empty/partial stock, changed runtime capacity, all four types without ownership dependency, repeated purchase and callback reentrancy, unknown/invalid products and references.
- `dotnet build .omo/verification/ammo-phase4/CompileEditor.csproj --nologo --verbosity quiet '-consoleLoggerParameters:ErrorsOnly;Summary'`
  - Exit 0; 0 errors, 90 CS0649 warnings for serialized fields.
  - Includes runtime and Editor sources; only C# compilation, no Unity process/API execution or player build.
- Shop prefab static inspection passed: 185 unique object IDs, valid local references, component ownership, transform parent/child consistency, 11 entries in each button/label/price array and wired summary label.
- Layout geometry at reference 1920x1080: 4 columns, 3 rows; 610 required/available height; summary right edge 480 and grid left edge 540.
- Not executed: Unity import, actual rendering/text clipping, GameManager phase rejection in play, HUD refresh in play, keyboard/controller navigation, existing-item purchases in play, Editor builder execution.
