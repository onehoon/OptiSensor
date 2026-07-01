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

OptiSensor uses a current-user Windows Task Scheduler task instead of the HKCU Run registry key.

Task name:

```text
OptiSensor
```

Action:

```text
"%LocalAppData%\Programs\OptiSensor\OptiSensor.exe" --startup
```

Policy:

```text
Trigger: At user logon
Run level: Least privilege
Restart on failure: every 1 minute, up to 3 attempts
```

No administrator rights are required. Legacy `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entries named `OptiSensor` are removed during install and uninstall to avoid duplicate launches.

Tray `Exit` and the main window `Exit` button exit normally with code `0`, so Task Scheduler does not restart OptiSensor after an intentional user exit. A crash or non-zero process exit can be restarted by the task failure policy.

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

Removes the Task Scheduler startup task, removes any legacy HKCU Run startup entry, and tries to delete the installed executable. Settings and logs are not deleted.

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

## Sensor Editing

The main window can discover supported LibreHardwareMonitor sensors and add them to the selected overlay list.
Users can edit display name, format, enabled state, and order.

Changes apply immediately for the current running session. Click `Save` to persist them to `settings.json`.

Selected sensors can show `Possible match (not used)` when a saved sensor is no longer found by exact id but a similar sensor exists. Possible matches are shown for manual repair only and are not automatically used for publishing yet.

## settings.json

Default settings:

```json
{
  "startWithWindows": true,
  "startMinimized": true,
  "publishIntervalMs": 1000,
  "selectedSensors": []
}
```

`publishIntervalMs` is clamped to the range `100` to `10000`.

`startMinimized` is used by the tray lifecycle policy. Startup mode runs without showing the main window; normal interactive launch still shows the main window.

If the file contains invalid JSON, OptiSensor backs it up as `settings.json.bad.<timestamp>` and recreates defaults.

## Task Scheduler Verification

Check the startup task:

```powershell
schtasks /Query /TN "OptiSensor"
schtasks /Query /TN "OptiSensor" /XML
```

Expected task details:

```text
LogonTrigger
Command: %LocalAppData%\Programs\OptiSensor\OptiSensor.exe
Arguments: --startup
RestartOnFailure Interval: PT1M
RestartOnFailure Count: 3
RunLevel: LeastPrivilege
```

Check that the legacy HKCU Run value is absent:

```powershell
reg query HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v OptiSensor
```
