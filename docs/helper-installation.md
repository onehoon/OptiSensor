# OptiSensor installation and startup

OptiSensor is installed with its Velopack `*-Setup.exe` installer. It no longer copies a running executable over itself. Velopack installs the app for the current user under LocalAppData and keeps the launch path stable across updates through its `current` directory.

The Claw edition is packaged separately in the `claw` Velopack channel. Run its installer once:

```powershell
.\OptiSensor-claw-Setup.exe
```

The installer uses the framework-dependent `net10-x64-desktop` package configuration, so it can install the required .NET 10 Desktop Runtime when needed. Settings and logs remain outside the install directory:

```text
%LocalAppData%\OptiSensor\settings.json
%LocalAppData%\OptiSensor\logs\
```

Uninstall OptiSensor through Windows Installed apps. The installer-managed app files are removed; settings and logs remain unless they are manually deleted.

## Startup registration

When **Start with Windows** is enabled, OptiSensor creates the current-user `OptiSensor` Task Scheduler task. Its action targets the executable under Velopack's stable `current` directory and passes `--startup`.

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

## Window and tray lifecycle

`--startup` is the only supported runtime command-line mode; it is reserved for the Task Scheduler action and starts OptiSensor without a window.

```text
Normal launch      -> MainWindow shown
--startup          -> no MainWindow; tray icon + native publisher remain active
Minimize           -> window retires to the tray
X / Close          -> window retires to the tray
Tray -> Show       -> creates/shows a new UI session
Tray -> Exit       -> application exits
MainWindow Exit    -> application exits
```

Minimize and X do not stop telemetry. Native telemetry publishing is owned by `ApplicationHost` and continues whether or not a MainWindow exists; only **Tray Exit** or the **MainWindow Exit** button ends the process.

## Updates

The installed Claw edition checks for updates automatically at startup using its Velopack `claw` channel. There is no manual update button.
