# OptiSensor Build Instructions

## Branch policy

- `main` is the default branch for all work: branch off `origin/main` and open PRs against `main` unless the user explicitly asks for another branch.
- The OptiSensor application source (`src/OptiSensor`) must stay identical between `main` and `release/0.9`. When it changes on `main`, sync it to `release/0.9` too — cherry-pick the commit(s), or if history has drifted enough that cherry-picking conflicts, replace `src/OptiSensor` wholesale from `main` (`git checkout origin/main -- src/OptiSensor`) rather than hand-adapting the change to an older shape.
- `release/0.9` additionally carries its own OptiScaler patch stack and packaging (`optiscaler/patches`, `docs/build-optisensor-optiscaler.md`, workflow defaults like `optiscaler_ref`). Those are release/0.9-specific and are not synced back to `main`.
- When the user requests a change on a branch other than `main` or `release/0.9`, cherry-pick the relevant commit(s) onto that branch instead of re-targeting the `main` PR or rebasing `main` work onto it.

## Default local build

When the user asks to build OptiSensor without specifying another format, produce the following output:

- Use the `Release` configuration.
- Use `dotnet publish`, not only `dotnet build`.
- Target `win-x64`.
- Produce a runnable Windows executable at `artifacts/release/OptiSensor.exe`.
- Use a framework-dependent publish: `--self-contained false`.
- Bundle application dependencies into one executable: `-p:PublishSingleFile=true`.
- Do not generate PDB files: `-p:DebugType=None -p:DebugSymbols=false`.
- Do not create a ZIP or any other archive.
- In the completion report, provide the EXE path and state whether the publish succeeded.
