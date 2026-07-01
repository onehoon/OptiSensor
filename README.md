# OptiSensor

OptiSensor is a lightweight Windows hardware sensor helper for OptiScaler's external overlay.

It reads local GPU sensor values with LibreHardwareMonitor and publishes a compact overlay line through shared memory, so a patched OptiScaler build can append the values to its FPS-only overlay.

Example overlay:

```text
FPS 111 | GPU 44C | 115W | 62%
```

## Current Scope

This repository currently serves two purposes:

- `OptiSensor.exe`: a minimal helper that reads GPU temperature, power, and load.
- OptiScaler package hub: a manual GitHub Actions workflow that applies the OptiScaler patch stack, builds `OptiScaler.dll`, publishes `OptiSensor.exe`, and creates a small release zip.

Advanced helper features such as tray UI, sensor selection, autostart, PawnIO integration, and richer configuration are follow-up work.

## Requirements

- Windows 11
- .NET 10 SDK for local development
- Visual Studio/MSBuild for local OptiScaler builds

The helper targets:

```text
net10.0-windows
win-x64
```

Windows 10 and older Windows versions are not supported by this project.

## Local Helper Usage

Run once:

```powershell
dotnet run -- --once
```

Watch sensor output in the console:

```powershell
dotnet run -- --watch
```

Publish a self-contained Windows x64 executable:

```powershell
dotnet publish .\OptiSensor.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishReadyToRun=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\publish\win-x64
```

## OptiScaler Integration

OptiScaler changes are stored as a patch stack under:

```text
optiscaler/patches/
```

The manual package workflow:

1. Checks out this OptiSensor repository.
2. Clones the selected OptiScaler repository/ref.
3. Applies `optiscaler/patches/*.patch` with `git am`.
4. Builds only `OptiScaler.vcxproj`.
5. Collects `OptiScaler.dll` and `OptiScaler.ini`.
6. Publishes `OptiSensor.exe`.
7. Creates a zip containing only:

```text
OptiScaler.dll
OptiScaler.ini
OptiSensor.exe
```

Detailed build and release notes are in [docs/build-optisensor-optiscaler.md](docs/build-optisensor-optiscaler.md).

## Importing OptiScaler Patches

Use the patch import helper when the OptiScaler integration branch changes:

```powershell
.\scripts\import-optiscaler-patches.ps1 `
  -SourceRepo "onehoon/OptiScaler" `
  -SourceRef "optisensor-overlay" `
  -BaseRef "main" `
  -OutputDir "optiscaler/patches"
```

Patch conflicts during CI are expected to fail the build. Refresh the patch stack instead of auto-resolving conflicts in the workflow.

## License

OptiSensor is licensed under the GNU General Public License version 3.0. See [LICENSE](LICENSE).
