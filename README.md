# OptiSensor

OptiSensor is a Windows x64 hardware-monitoring plugin for the OptiScaler V10 Plugin API. It reads LibreHardwareMonitor sensor data and displays it in the OptiScaler plugin tab.

## Requirements

- Windows x64 supported by the installed OptiScaler and LibreHardwareMonitor versions.
- OptiScaler V10 with a compatible ASI Plugin API.
- LibreHardwareMonitor v0.9.6 installed and running.
- PawnIO installed and available to LibreHardwareMonitor.
- LibreHardwareMonitor Remote Web Server enabled on port 8085.

## Install

1. Install and run LibreHardwareMonitor v0.9.6 with PawnIO installed.
2. Enable **Remote Web Server** in LibreHardwareMonitor and verify that [http://127.0.0.1:8085/data.json](http://127.0.0.1:8085/data.json) returns sensor data.
3. Download `OptiSensor.zip` from the [OptiSensor.Plugin rolling pre-release](https://github.com/onehoon/OptiSensor/releases/tag/OptiSensor.Plugin).
4. Extract `OptiSensor.asi` into OptiScaler's plugins folder.
5. Set `LoadAsiPlugins=true` in `OptiScaler.ini`.

Keep LibreHardwareMonitor running while using OptiSensor. The plugin reads the loopback endpoint. It never installs, starts, elevates, or reconfigures LibreHardwareMonitor or PawnIO. Review LibreHardwareMonitor remote access settings and Windows Firewall rules because its web server may listen beyond loopback.

## Current capabilities

- Discover LibreHardwareMonitor sensors and show them in collapsible hardware groups.
- Select sensors, reorder them, and edit overlay groups; save selections in OptiScaler's existing plugin settings.
- Suggest default CPU, GPU, VRAM, and RAM sensors.
- Preview composed overlay text in the plugin tab. The current plugin does not publish this preview to the OptiScaler FPS overlay.
- Show the existing update-available indication.

## Build and test

Build and run the native bridge and overlay-layout tests, then build the Release x64 ASI with MSBuild:

```powershell
msbuild .\tests\OptiSensor.Plugin.Tests\OptiSensor.Plugin.Tests.vcxproj /m /p:Configuration=Release /p:Platform=x64
& .\tests\OptiSensor.Plugin.Tests\bin\Release\OptiSensor.Plugin.Tests.exe
msbuild .\src\OptiSensor.Plugin\OptiSensor.Plugin.vcxproj /m /p:Configuration=Release /p:Platform=x64
```

The ASI output is `src/OptiSensor.Plugin/bin/Release/OptiSensor.asi`. [Plugin CI](https://github.com/onehoon/OptiSensor/blob/main/.github/workflows/optisensor-plugin-ci.yml) builds and tests the native projects and verifies the `OptiPluginInit` export. The [rolling release workflow](https://github.com/onehoon/OptiSensor/blob/main/.github/workflows/build-optisensor-plugin.yml) is started manually and publishes one `OptiSensor.zip` asset containing `OptiSensor.asi`.

## Legacy history

The Desktop, MSI Claw, and patched OptiScaler editions are discontinued. Their source snapshots are preserved in Git:

- [Legacy Desktop](https://github.com/onehoon/OptiSensor/tree/archive/legacy-desktop)
- [Legacy MSI Claw](https://github.com/onehoon/OptiSensor/tree/archive/legacy-claw)
- [OptiScaler 0.9 edition](https://github.com/onehoon/OptiSensor/tree/archive/optiscaler-0.9)
- [OptiScaler 0.10 edition](https://github.com/onehoon/OptiSensor/tree/archive/optiscaler-0.10)

## License

The repository root [LICENSE](LICENSE) is GNU GPL version 3. Files in `src/OptiSensor.Plugin` carrying an SPDX MIT identifier are covered by that directory's [LICENSE](src/OptiSensor.Plugin/LICENSE). The vendored yyjson dependency retains its upstream [MIT license](third_party/yyjson/LICENSE) and [provenance](third_party/yyjson/README.md).
