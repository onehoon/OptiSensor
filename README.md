# OptiSensor

OptiSensor is a lightweight Windows hardware sensor helper for OptiScaler's external overlay.

It reads local hardware sensor values with LibreHardwareMonitor and publishes a compact overlay line through the `Local\OptiScalerExternalOverlay` shared-memory mapping. A patched OptiScaler build appends the first UTF-8 line to its FPS overlay. The shared-memory payload format is fixed; only the text content is updated.

Example overlay:

```text
FPS: 111.0 | GPU 44C | 115W | 62%
```

## Current Scope

This branch (`main`) carries only the OptiSensor application source and its own CI:

- `OptiSensor.exe`: a WPF tray helper with LibreHardwareMonitor sensor discovery, selected overlay sensor editing, and shared memory publishing.
- Velopack packaging: each manual OptiSensor CI build creates a current-user installer and automatically assigns the next `0.1.x` version and matching `v0.1.x` tag.

The OptiScaler patch stack that reads this app's shared-memory feed, and the combined-package build that pairs a patched `OptiScaler.dll` with this app's installer, live on the version-specific `release/0.9`/`release/0.10` branches instead — see [Branch Layout](#branch-layout).

Follow-up work includes richer Fluent UI styling, automatic sensor recommendation, presets, private GitHub update-feed activation, and game or OptiScaler activity based publishing.

The helper source is organized under `src/OptiSensor` by role:

```text
App/         WPF startup coordination and single-instance lifetime
Cli/         --once and --watch diagnostic commands
Install/     LocalAppData data paths and Task Scheduler startup registration
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

The local publish output expects the .NET 10 Desktop Runtime to be installed separately. Native helper libraries are still bundled into `OptiSensor.exe` so the output can keep a single helper executable. CI then turns this publish output into a Velopack installer.

## Startup

OptiSensor registers a current-user Windows Task Scheduler task named `OptiSensor` when `startWithWindows` is enabled.
The task launches the Velopack `current` helper with `--startup` at user logon after a 5-minute delay using the current user's normal permissions, and is configured to restart on failure up to 3 times at 1-minute intervals.

Tray `Exit` and the main window `Exit` button perform a normal exit with code `0`, so Task Scheduler does not restart the helper after an intentional user exit.
Legacy HKCU Run startup entries are removed during task registration/unregistration to avoid duplicate launches.

## Shared-Memory Protocol

[docs/optiscaler-external-overlay-protocol.md](docs/optiscaler-external-overlay-protocol.md) documents the shared-memory payload contract
that `ExternalOverlayPublisher` implements. External overlay lines are UTF-8, null-terminated byte
strings; the maximum line length is 128 bytes including the trailing null byte, and the protocol payload
size is 544 bytes. The canonical C++ struct definition and the OptiScaler-side patch stack that reads
this feed live on the `release/0.9`/`release/0.10` branches instead — see [Branch Layout](#branch-layout).

## Branch Layout

The OptiSensor app source and the OptiScaler patch stacks that consume it are split across branches:

| Branch | Contents |
| --- | --- |
| `main` | This branch. `src/OptiSensor` app source and its own CI (`build-optisensor-only.yml`) only — no OptiScaler patches. |
| `release/0.9` | OptiScaler `release/0.9` patch stack and its packaging/build workflows only — no app source. |
| `release/0.10` | OptiScaler `master` patch stack and its packaging/build workflows only — no app source. |
| `backup/pre-split` | Frozen backup of the pre-split combined history. Not actively developed. |

App source changes belong on `main`. OptiScaler patch changes belong on the relevant
`release/0.x` branch. A combined OptiSensor+OptiScaler package build reads the app from
`main` and patches from the target `release/0.x` branch.

## License

OptiSensor is licensed under the GNU General Public License version 3.0. See [LICENSE](LICENSE).
