# OptiSensor Build Instructions

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
