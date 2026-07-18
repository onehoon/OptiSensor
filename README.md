# OptiSensor

OptiSensor is a lightweight Windows hardware sensor helper for OptiScaler's external overlay.

It reads local hardware sensor values with LibreHardwareMonitor and publishes a compact overlay line through the `Local\OptiScalerExternalOverlay` shared-memory mapping. A patched OptiScaler build appends the first UTF-8 line to its FPS overlay. The shared-memory payload format is fixed; only the text content is updated.

Example overlay:

```text
FPS: 111.0 | GPU 44C | 115W | 62%
```

## Current Scope

This repository currently serves two purposes:

- `OptiSensor.exe`: a WPF tray helper with LibreHardwareMonitor sensor discovery, selected overlay sensor editing, and shared memory publishing.
- OptiScaler package hub: a manual GitHub Actions workflow that applies the OptiScaler patch stack, builds `OptiScaler.dll`, publishes `OptiSensor.exe`, and creates a small release zip.

Follow-up work includes richer Fluent UI styling, automatic sensor recommendation, presets, PawnIO handling, automatic helper updates, and game or OptiScaler activity based publishing.

The helper source is organized under `src/OptiSensor` by role:

```text
App/         WPF startup coordination and single-instance lifetime
Cli/         --once and --watch diagnostic commands
Install/     LocalAppData install paths and Task Scheduler startup registration
Libre/       LibreHardwareMonitor reading and sensor classification
Models/      Detected and selected sensor models
Overlay/     Overlay line formatting and shared memory publishing
Publishing/  Shared publish runner and background service
Settings/    settings.json model and store
UI/          Main window and tray icon lifecycle
```

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
dotnet run --project .\src\OptiSensor\OptiSensor.csproj -- --once
```

Watch sensor output in the console:

```powershell
dotnet run --project .\src\OptiSensor\OptiSensor.csproj -- --watch
```

Publish a framework-dependent Windows x64 executable:

```powershell
dotnet publish .\src\OptiSensor\OptiSensor.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -p:PublishSingleFile=true `
  -p:PublishReadyToRun=false `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\publish\win-x64
```

The packaged helper expects the .NET 10 Desktop Runtime to be installed separately. Native helper libraries are still bundled into `OptiSensor.exe` so the package can keep a single helper executable.

## Startup

OptiSensor registers a current-user Windows Task Scheduler task named `OptiSensor` when `startWithWindows` is enabled.
The task launches the installed helper with `--startup` at user logon and is configured to restart on failure up to 3 times at 1 minute intervals.

Tray `Exit` and the main window `Exit` button perform a normal exit with code `0`, so Task Scheduler does not restart the helper after an intentional user exit.
Legacy HKCU Run startup entries are removed during install/uninstall to avoid duplicate launches.

## OptiScaler Integration

OptiScaler changes are stored as a patch stack under:

```text
optiscaler/patches/
```

This is an unofficial OptiScaler integration. It builds OptiScaler from the upstream `master` branch and applies this repository's main-specific patch stack; it is not an upstream OptiScaler release.

### main Overlay Behavior

- Upstream target: `optiscaler/OptiScaler` `master`
- Overlay type: `7 = Just FPS (+External)`
- The main patch appends the shared-memory text to the Just FPS output only when type 7 is selected.
- There is no `AppendExternalOverlayText` INI key or separate toggle.

For a new or unchanged INI file, the compiled defaults are:

```ini
ShowFps=auto        ; compiled default: true
FpsOverlayType=auto ; compiled default: 7 = Just FPS (+External)
```

Users can explicitly set `ShowFps=true` and `FpsOverlayType=7` if they do not want to rely on `auto`. When OptiSensor stops publishing, the external sensor text expires after about 5 seconds while the FPS portion remains visible.

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
  -BaseRef "master" `
  -OutputDir "optiscaler/patches"
```

Patch conflicts during CI are expected to fail the build. Refresh the patch stack instead of auto-resolving conflicts in the workflow.

## License

OptiSensor is licensed under the GNU General Public License version 3.0. See [LICENSE](LICENSE).
