# OptiSensor Build Instructions

## Branch policy

- `main` is the default branch for all work: branch off `origin/main` and open PRs against `main` unless the user explicitly asks for another branch.
- Do not copy changes to `release/0.9` or any other branch unless the user explicitly requests it.
- When the user does request another branch, cherry-pick the relevant commit(s) onto that branch instead of re-targeting the `main` PR or rebasing `main` work onto it.
- Keep `release/0.9` limited to its independent OptiScaler patch-stack maintenance unless explicitly directed otherwise.

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
