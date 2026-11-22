# OptiSensor native plugin third-party notices

`OptiSensor.dll` is a Native AOT shared library built for Windows x64. It compiles the unmodified official `LibreHardwareMonitorLib` package and its selected NuGet dependencies into the native module. The package and dependency versions below are from the locked Release `win-x64` restore. MPL-covered package source remains available at the linked upstream repositories; no LibreHardwareMonitor source was modified or vendored.

| Package | Version | License | Source |
| --- | --- | --- | --- |
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0 | https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/3d331e3370efb858411f19511373eff65a218701 |
| BlackSharp.Core | 1.0.7 | MPL-2.0 | https://github.com/Blacktempel/BlackSharp |
| DiskInfoToolkit | 1.1.2 | MPL-2.0 | https://github.com/Blacktempel/DiskInfoToolkit |
| HidSharp | 2.6.4 | Apache-2.0 | https://software.seekye.com/hidsharp |
| Mono.Posix.NETStandard | 1.0.0 | MIT | https://www.nuget.org/packages/Mono.Posix.NETStandard/1.0.0 |
| RAMSPDToolkit-NDD | 1.4.2 | MPL-2.0 | https://github.com/Blacktempel/RAMSPDToolkit |
| System.CodeDom | 10.0.2 | MIT | https://www.nuget.org/packages/System.CodeDom/10.0.2 |
| System.IO.FileSystem.AccessControl | 5.0.0 | MIT | https://www.nuget.org/packages/System.IO.FileSystem.AccessControl/5.0.0 |
| System.IO.Ports | 10.0.3 | MIT | https://www.nuget.org/packages/System.IO.Ports/10.0.3 |
| System.Management | 10.0.2 | MIT | https://www.nuget.org/packages/System.Management/10.0.2 |
| .NET Native AOT runtime | 10.0.15 | MIT; bundled notices | https://github.com/dotnet/runtime |

The Native AOT compiler and IL linker packages are build-time dependencies and are not included in the delivered files. PE import inspection found only Windows system DLLs, which are operating-system components and are not redistributed here.

License texts and notices are included in `licenses/`:

- `MPL-2.0.txt` for the MPL-licensed packages above.
- `Apache-2.0-HidSharp.txt` for HidSharp.
- `MIT-dotnet.txt` and `DotNet-ThirdPartyNotices.txt` for the .NET runtime and Microsoft MIT-licensed packages.
