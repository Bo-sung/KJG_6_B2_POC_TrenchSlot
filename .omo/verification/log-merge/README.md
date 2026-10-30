# Unified logging verification

Run from the repository root. Requires dotnet and the project's cached Unity/Newtonsoft assemblies.

Pure contracts:

```sh
dotnet run --project .omo/verification/log-merge/LogMerge.csproj
```

Compile-only checks use the exact LogKit commit pinned by packages-lock.json.
No Unity application or Unity API is executed.

```sh
git clone --depth 1 --branch v0.3.0 https://github.com/Bo-sung/logkit.git .omo/verification/log-merge/logkit-source
git -C .omo/verification/log-merge/logkit-source rev-parse HEAD
dotnet build .omo/verification/log-merge/CompileRuntime.csproj
dotnet build .omo/verification/log-merge/CompileEditor.csproj
```

Expected package commit: 172a28918ebc44592a7b19e3e9f88a058264e81d.
If the source directory already exists, verify its commit instead of cloning again.

Observed on 2026-10-31:

- Unified logging: 312 passing assertions, including a source scan for conflict markers and legacy gameplay writers.
- Existing phases 1/2/3/4: 3067 / 121 / 6060 / 78 passing assertions.
- Runtime compile: zero errors, 103 warnings.
- Editor and runtime compile: zero errors, 115 warnings.
- Warnings concern existing inspector fields and editor authoring data.
- Actual Unity gameplay, console capture and network upload were not run.
