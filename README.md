# OptiSensor

OptiSensor is a Windows tray utility that publishes native MSI Claw telemetry to OptiScaler's `Local\OptiScalerExternalOverlay` shared-memory feed.

The Claw edition reads telemetry directly from Windows, MSI EC, and Intel IGCL. HWiNFO is not required. It publishes a compact overlay line through the `Local\OptiScalerExternalOverlay` shared-memory mapping; a patched OptiScaler build appends the first UTF-8 line to its FPS overlay. The shared-memory payload format is fixed; only the text content is updated.

Example overlay:

```text
CPU 36% 67°C | GPU 98% 2300MHz | TDP 18W | RAM 20.0GB | VRAM 9.4GB | FAN 3540RPM | BAT 72% 2.5h
```

## UI

The single-page window shows the current shared-memory overlay feed and provides Intel VRR Range Fix and Start with Windows controls. Closing or hiding the window leaves background telemetry publishing active.

## Current Scope

This branch (`claw`) carries only the OptiSensor application source and its own CI:

- `OptiSensor.exe`: a WPF tray helper that samples native Windows/MSI EC/Intel IGCL telemetry and publishes the overlay line.
- Velopack packaging: each manual OptiSensor CI build creates a current-user installer and automatically assigns the next `0.1.x` version and matching `v0.1.x` tag.

The OptiScaler patch stack that reads this app's shared-memory feed, and the combined-package build that pairs a patched `OptiScaler.dll` with this app's installer, live on the version-specific `release/0.9`/`release/0.10` branches instead — see [Branch Layout](#branch-layout).

The helper source is organized under `src/OptiSensor` by role:

```text
App/         WPF startup coordination and single-instance lifetime
Claw/        Native telemetry sampler and overlay line formatter
Install/     LocalAppData data paths and Task Scheduler startup registration
Overlay/     Shared-memory overlay publisher, reader, and protocol
Publishing/  Background publish service
Settings/    settings.json model and store
Tweaks/      Intel VRR Range Fix coordinator
Updates/     Velopack background update check
UI/          Single-page main window and tray icon lifecycle
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

When **Start with Windows** is enabled, OptiSensor registers the current-user `OptiSensor` Task Scheduler task whose action targets the Velopack `current` helper:

```text
Trigger: At user logon, delayed by 1 minute
Run level: HighestAvailable
Restart on failure: every 1 minute, up to 3 attempts
Argument: --startup
```

`--startup` is the only supported runtime command-line mode. Tray `Exit` and the main window `Exit` button perform a normal exit with code `0`, so Task Scheduler does not restart the helper after an intentional user exit. Legacy HKCU Run startup entries are removed during task registration/unregistration to avoid duplicate launches.

## Updates

OptiSensor checks for updates automatically when the application starts. There is no manual update button.

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
