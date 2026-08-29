# OptiSensor

OptiSensor extends OptiScaler's FPS overlay with additional hardware telemetry. It publishes one
plain-text line to a shared-memory feed that a patched OptiScaler build appends to its on-screen FPS
counter.

Two editions are available:

- **Desktop Edition** — for general supported Windows PCs, using either HWiNFO or LibreHardwareMonitor
  as its sensor source.
- **Claw Edition** — for supported MSI Claw handhelds, using a fully native Windows / MSI EC / Intel
  IGCL telemetry stack. **The Claw edition does not use HWiNFO or LibreHardwareMonitor.**

Both editions require the OptiSensor-compatible patched `OptiScaler.dll` —
see [Install the OptiSensor-Compatible OptiScaler Build](#install-the-optisensor-compatible-optiscaler-build).

## Editions

|  | Desktop Edition | Claw Edition |
| --- | --- | --- |
| Source branch | `main` | `claw` |
| Target | General supported Windows PCs | Supported MSI Claw models only |
| Sensor backend | HWiNFO **or** LibreHardwareMonitor | Windows + MSI EC + Intel IGCL |
| HWiNFO | Optional; required only when the HWiNFO source is selected | **Not used** |
| LibreHardwareMonitor | Built-in alternative sensor source | **Not used** |
| Windows native telemetry | Not the Desktop telemetry path | Used |
| MSI EC | Not a Desktop telemetry path | Used |
| Intel IGCL | Not a Desktop telemetry path | Used |
| Velopack update channel | `win` | `claw` |
| Installer | `OptiSensor-win-Setup.exe` | `OptiSensor-claw-Setup.exe` |

The two editions share the OptiSensor external-overlay contract and the same patched OptiScaler
consumer, but their telemetry backends are entirely different and are described separately below.

```text
Desktop Edition                         Claw Edition
HWiNFO or LibreHardwareMonitor           Windows + MSI EC + Intel IGCL
            │                                        │
            ▼                                        ▼
        OptiSensor                               OptiSensor
            │                                        │
            ▼                                        ▼
       Shared memory                            Shared memory
 Local\OptiScalerExternalOverlay         Local\OptiScalerExternalOverlay
            │                                        │
            ▼                                        ▼
     patched OptiScaler.dll                   patched OptiScaler.dll
```

## Desktop Edition

The Desktop edition targets general supported Windows PCs and reads hardware sensors from one
selectable sensor source.

### Requirements

- Windows 11 (Windows 10 and older are not supported)
- If **HWiNFO** is selected as the Desktop sensor source, a separately installed copy of HWiNFO is
  required. HWiNFO is not bundled with or licensed by OptiSensor; users are responsible for complying
  with the applicable HWiNFO license terms.
- **LibreHardwareMonitor** is the alternative built-in sensor source and does not require HWiNFO to be
  installed.

### Telemetry

The Desktop edition lets you choose either **HWiNFO** or **LibreHardwareMonitor** as the active sensor
source. OptiSensor discovers sensors from the selected source, lets you choose which values appear in
the overlay line, and publishes the formatted line to shared memory.

### Installation

Install `OptiSensor-win-Setup.exe` (Velopack `win` channel). It installs per-user under LocalAppData
and updates in place. See [docs/helper-installation.md](docs/helper-installation.md) for the shared
installer / startup-task details.

## Claw Edition

The Claw edition is a distinct implementation of OptiSensor for MSI Claw handhelds. It samples
telemetry natively from Windows, the MSI embedded controller, and the Intel graphics control library —
**it does not use HWiNFO or LibreHardwareMonitor**, and it does not require any third-party sensor
tool to be installed.

### Supported devices

The OptiSensor Claw edition supports only:

| Device | Board ID |
| --- | --- |
| **MSI Claw 8 AI+ A2VM** | `MS-1T52` |
| **MSI Claw 8 EX AI+ CG3EM** | `MS-1T91` |

Other MSI Claw models — including the **MSI Claw 7 AI+ A2VM** (`MS-1T42`) and the original MSI Claw
A1M — are **not** currently supported by the OptiSensor Claw edition. Device names follow the
Steam Addon for Claw project's naming.

### Telemetry backend

| Metric | Source |
| --- | --- |
| CPU Usage | Windows |
| CPU Temperature | MSI EC |
| GPU Usage | Intel IGCL |
| GPU Clock | Intel IGCL |
| TDP | MSI EC (CPU package power) |
| RAM | Windows |
| VRAM | Windows (Intel GPU memory) |
| FAN | MSI EC |
| Battery | Windows |

FPS is provided by OptiScaler itself. OptiSensor does not sample FPS. GPU power and GPU temperature
are not published by the Claw edition.

### Overlay example

```text
CPU 36% 67°C | GPU 98% 2300MHz | TDP 18W | RAM 20.0GB | VRAM 9.4GB | FAN 3540RPM | BAT 72% 2.5h
```

OptiScaler prepends its own FPS reading; OptiSensor supplies everything after it. Segments are omitted
when their source is unavailable.

### Installation

Install `OptiSensor-claw-Setup.exe` (Velopack `claw` channel). It installs per-user under LocalAppData
and updates in place.

The Claw edition runs **elevated** — reading the MSI embedded controller requires administrator
privileges, so OptiSensor requests UAC on launch (or is started elevated by its startup task). Do not
install HWiNFO or LibreHardwareMonitor for the Claw edition; they are not used.

See [docs/helper-installation.md](docs/helper-installation.md) for installer, startup-task, and
lifecycle details.

### Tray and UI behavior

```text
Normal launch          → MainWindow shown
Start with Windows / --startup
                       → tray-only startup; telemetry publishing active, no window
Minimize               → UI retires to the tray; telemetry continues
X / Close              → UI retires to the tray; telemetry continues
Tray → Show            → a new UI session opens
Tray → Exit            → application exits
MainWindow Exit        → application exits
```

Telemetry publishing is owned by the application host and continues whenever the window is hidden or
closed; only **Tray Exit** or the **MainWindow Exit** button ends the process.

The single-page window contains:

- **Current Overlay Feed** — the live line currently in shared memory
- **Intel VRR Range Fix** — restores the native VRR range on the affected MSI Claw 8 internal panel
- **Start with Windows** — registers/removes the per-user startup task

## Install the OptiSensor-Compatible OptiScaler Build

**OptiSensor telemetry does not appear with a standard OptiScaler build.** Upstream OptiScaler does
not contain the external-overlay patch that reads `Local\OptiScalerExternalOverlay`. You must replace
your `OptiScaler.dll` with the patched build produced by this repository's GitHub Actions.

### Download the patched artifact

1. Open this repository's **Actions** tab on GitHub.
2. Select the workflow matching the OptiScaler version you run:
   - **Build OptiScaler 0.9** — for OptiScaler `release/0.9`
   - **Build OptiScaler 0.10** — for current OptiScaler `master`
3. Open a successful run of that workflow.
4. Download its artifact from the run's **Artifacts** section:
   - `OptiScaler-0.9.dll` or `OptiScaler-0.10.dll`
5. Extract `OptiScaler.dll` from the downloaded archive.
6. Back up your existing `OptiScaler.dll` if you want to keep it.
7. Replace your existing `OptiScaler.dll` with the patched one.
8. Run OptiSensor alongside OptiScaler, and set the OptiScaler FPS overlay type to
   `Just FPS (+External)`.

### Match the OptiScaler version

The patched DLL is built against a specific OptiScaler branch and only works with that version:

```text
OptiScaler 0.9   → use the 0.9 patched artifact (OptiScaler-0.9.dll)
OptiScaler 0.10  → use the 0.10 patched artifact (OptiScaler-0.10.dll)
```

> Always use the patched artifact built for the OptiScaler version you are running. Do not mix DLLs
> built against different OptiScaler branches.

## Updates

Both editions check for updates automatically at startup and have no manual update button.

```text
Desktop Edition → Velopack `win` channel
Claw Edition    → Velopack `claw` channel
```

The repository-wide release workflow on `main` builds both editions from their own source branches and
publishes them to their respective channels in one GitHub Release.

## Technical Integration

OptiSensor writes a single UTF-8 line to the `Local\OptiScalerExternalOverlay` shared-memory mapping,
and the patched OptiScaler reads `lines[0]` and appends it to the FPS overlay. The payload layout,
sequence-lock algorithm, freshness window, and canonical C++ struct are documented in
[docs/optiscaler-external-overlay-protocol.md](docs/optiscaler-external-overlay-protocol.md), which is
the technical authority for the contract.

## Development

```powershell
dotnet build -c Release
dotnet test tests/OptiSensor.Tests -c Release
```

The app targets `net10.0-windows` / `win-x64` and needs the .NET 10 SDK. CI (`app-ci.yml`) runs the
build, the tests, and a single-file publish smoke on every pull request.

## Branches

| Branch | Purpose |
| --- | --- |
| `main` | Desktop OptiSensor edition; repository-wide release authority (builds and publishes both editions) |
| `claw` | MSI Claw OptiSensor edition |
| `release/0.9` | OptiScaler 0.9 patch stack and its build workflow |
| `release/0.10` | OptiScaler 0.10 patch stack and its build workflow |

## License

OptiSensor is licensed under the GNU General Public License version 3.0. See [LICENSE](LICENSE).
