# OptiSensor Build Instructions

## Branch policy

- `main` is the only ongoing development branch. Pull requests target `main`; feature and work-order branches are temporary and are removed after merge once their required archive and CI checks are verified.
- Do not restore the retired Desktop, MSI Claw, or patched OptiScaler editions in the active source tree.

## Build and test

Use Visual Studio MSBuild with the v143 C++ toolset and a Windows SDK. Build and run the native bridge/layout tests, then build the Release x64 ASI:

```powershell
msbuild .\tests\OptiSensor.Plugin.Tests\OptiSensor.Plugin.Tests.vcxproj /m /p:Configuration=Release /p:Platform=x64
& .\tests\OptiSensor.Plugin.Tests\bin\Release\OptiSensor.Plugin.Tests.exe
msbuild .\src\OptiSensor.Plugin\OptiSensor.Plugin.vcxproj /m /p:Configuration=Release /p:Platform=x64
```

The ASI output is `src/OptiSensor.Plugin/bin/Release/OptiSensor.asi`. Plugin CI runs the same native tests and build, then verifies that the ASI exports `OptiPluginInit`.

## Packaging and release

- `.github/workflows/build-optisensor-plugin.yml` is a manual `workflow_dispatch` workflow for the rolling `OptiSensor.Plugin` pre-release.
- The release asset is `OptiSensor.zip`, containing exactly one root entry: `OptiSensor.asi`.
- Preserve the workflow's `Build-Run-ID` and `Build-SHA` metadata, updater integration, and rolling asset replacement behavior.
- The plugin requires user-installed LibreHardwareMonitor v0.9.6, PawnIO, and LibreHardwareMonitor Remote Web Server on port 8085. The ASI does not install, start, elevate, or reconfigure either dependency.

## Scope

Keep plugin source, the OptiScaler SDK API, persisted setting keys, INI behavior, and UI stable during repository cleanup. Do not reintroduce .NET application builds, Velopack packaging, scheduled publishing, or OptiScaler patch-stack workflows.
