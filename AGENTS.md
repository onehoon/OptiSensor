# OptiSensor Build Instructions

## Branch policy

- `OptiSensorApp` is the default branch for OptiSensor application source work: branch off `origin/OptiSensorApp` and open PRs against `OptiSensorApp` unless the user explicitly asks for another branch. This branch has no `optiscaler/patches` and no OptiScaler build workflow — only `src/OptiSensor` and `build-optisensor-only.yml`.
- OptiScaler patch-stack work belongs on the relevant `release/0.9` or `release/0.10` branch instead. Those branches carry no `src/OptiSensor` app source — only `optiscaler/patches`, their packaging docs, and OptiScaler build workflows.
- `main` is a frozen backup of the pre-split combined history (app + patches together). Do not develop on `main`; it is kept only as a fallback reference.
- Do not copy `src/OptiSensor` changes into a `release/0.x` branch, or `optiscaler/patches` changes into `OptiSensorApp`, unless the user explicitly asks for it.
- When the user requests a change on a branch other than the one implied above, cherry-pick the relevant commit(s) onto that branch instead of re-targeting the PR or rebasing work onto it.

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
