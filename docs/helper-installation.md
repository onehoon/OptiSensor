# OptiSensor installation and startup

OptiSensor is installed with its Velopack `*-Setup.exe` installer. It no longer copies a running executable over itself. Velopack installs the app for the current user under LocalAppData and keeps the launch path stable across updates through its `current` directory.

Run the installer once:

```powershell
.\OptiSensor-win-Setup.exe
```

The installer uses the framework-dependent `net10-x64-desktop` package configuration, so it can install the required .NET 10 Desktop Runtime when needed. Settings and logs remain outside the install directory:

```text
%LocalAppData%\OptiSensor\settings.json
%LocalAppData%\OptiSensor\logs\
```

Uninstall OptiSensor through Windows Installed apps. The installer-managed app files are removed; settings and logs remain unless they are manually deleted.

## Startup registration

When **Register startup task** is enabled, OptiSensor creates the current-user `OptiSensor` Task Scheduler task. Its action targets the executable under Velopack's stable `current` directory and passes `--startup`.

Policy:

```text
Trigger: At user logon, delayed by 1 minute
Run level: HighestAvailable
Restart on failure: every 1 minute, up to 3 attempts
```

The task action remains valid after a Velopack update because the app's `current` path is updated in place. Legacy `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entries named `OptiSensor` are removed when the task is registered or unregistered.

The Claw edition needs administrator rights to read MSI EC and Intel IGCL telemetry, so it elevates itself on launch (prompting for UAC when not started elevated). The `HighestAvailable` run level lets the startup task launch it elevated without a prompt.

Check the task:

```powershell
schtasks /Query /TN "OptiSensor"
schtasks /Query /TN "OptiSensor" /XML
```

## Command-line modes

`--startup` is the only supported runtime mode; it is reserved for the Task Scheduler action and suppresses the startup window. Normal app launches show the window; closing the window hides it to the tray, and only the tray/menu **Exit** action closes the helper.

## Updates

OptiSensor checks for updates automatically at startup using Velopack. There is no manual update button.
