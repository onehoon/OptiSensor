# OptiSensor Helper Installation

OptiSensor is a WPF tray helper packaged as a single `OptiSensor.exe`. When it is run normally from a downloaded zip or any temporary folder, it installs itself into the current user's LocalAppData folder and relaunches from the installed path.

The app uses the default Windows WPF theme and standard controls. There is no custom theme, dark theme, or third-party UI framework.

## Paths

Program install path:

```text
%LocalAppData%\Programs\OptiSensor\OptiSensor.exe
```

Data path:

```text
%LocalAppData%\OptiSensor\
```

Data files:

```text
%LocalAppData%\OptiSensor\settings.json
%LocalAppData%\OptiSensor\logs\
```

Uninstall keeps `settings.json` and `logs\` so user data is preserved.

## Startup Registration

OptiSensor uses the current user's Run registry key:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
```

Value name:

```text
OptiSensor
```

Value data:

```text
"%LocalAppData%\Programs\OptiSensor\OptiSensor.exe" --startup
```

No administrator rights are required.

## Commands

Normal run:

```powershell
.\OptiSensor.exe
```

Installs to LocalAppData when needed, launches the installed executable, then exits the original process. If already running from the installed path, it starts the background sensor publish service, shows the tray icon, and displays the main window.

Closing the main window with the X button does not exit OptiSensor. It hides the window to the tray while sensor publishing continues. Minimizing the window also hides it to the tray.

To exit the app, use the tray menu `Exit` command or the main window `Exit` button. The tray menu also includes `Show`, which restores and activates the main window.

Install:

```powershell
.\OptiSensor.exe --install
```

Copies the current executable to the install path, creates the data and logs folders, creates `settings.json` if needed, registers startup when `startWithWindows` is true, then exits.

Uninstall:

```powershell
.\OptiSensor.exe --uninstall
```

Removes the HKCU Run startup entry and tries to delete the installed executable. Settings and logs are not deleted.

Startup:

```powershell
.\OptiSensor.exe --startup
```

Used by the Windows startup entry. If launched from another path, it redirects to the installed executable. It then starts the tray icon and background sensor publish service without showing the main window. If another instance is already running, startup mode exits quietly and writes to the log.

Once:

```powershell
.\OptiSensor.exe --once
```

Diagnostic command. Runs one sensor read/publish cycle from the current path and exits. This command does not self-install and does not show the WPF window or tray icon.

Watch:

```powershell
.\OptiSensor.exe --watch
```

Diagnostic command. Runs the sensor publish loop from the current path and prints the current overlay line to the console when a console is available. This command does not self-install and does not show the WPF window or tray icon. It may create or load `%LocalAppData%\OptiSensor\settings.json` so it can use `publishIntervalMs`.

## settings.json

Default settings:

```json
{
  "startWithWindows": true,
  "startMinimized": true,
  "publishIntervalMs": 1000
}
```

`publishIntervalMs` is clamped to the range `100` to `10000`.

`startMinimized` is used by the tray lifecycle policy. Startup mode runs without showing the main window; normal interactive launch still shows the main window.

There is no settings UI yet. Users may edit `settings.json` manually. If the file contains invalid JSON, OptiSensor backs it up as `settings.json.bad.<timestamp>` and recreates defaults.
