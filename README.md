# OptiSensor — OptiScaler release/0.9 Patch Branch

This branch carries only the OptiScaler patch stack targeting the upstream
`optiscaler/OptiScaler` `release/0.9` branch. There is no OptiSensor
application source here.

## Contents

```text
optiscaler/patches/     Patch stack applied on top of upstream OptiScaler release/0.9
optiscaler/protocol/    Shared-memory payload contract header
scripts/                Patch import helper (import-optiscaler-patches.ps1)
docs/                   Patch-branch build notes and protocol documentation
.github/workflows/      Build OptiScaler Only (patched OptiScaler.dll artifact)
```

## Branch Layout

| Branch | Contents |
| --- | --- |
| `main` | OptiSensor app source (`src/OptiSensor`) and its own CI only — no OptiScaler patches. |
| `release/0.9` | This branch. OptiScaler `release/0.9` patch stack and build workflow only — no app source. |
| `release/0.10` | OptiScaler `master` patch stack and build workflow only — no app source. |
| `backup/pre-split` | Frozen backup of the pre-split combined history. Not actively developed. |

Users install the OptiSensor helper from the `main` release (Velopack
`OptiSensor-Setup.exe`) and pair it with the patched `OptiScaler.dll` built from
this branch's `Build OptiScaler Only` workflow.

## Overlay Behavior

- Upstream target: `optiscaler/OptiScaler` `release/0.9`
- Overlay type: `7 = Just FPS (+External)`
- The patch appends the shared-memory text to OptiScaler's existing Just FPS output.
- There is no `AppendExternalOverlayText` INI key or separate toggle.

For a new or unchanged INI file, the compiled defaults are:

```ini
ShowFps=auto        ; compiled default: true
FpsOverlayType=auto ; compiled default: 7 = Just FPS (+External)
```

When OptiSensor stops publishing, the external sensor text expires after about 5 seconds while the FPS portion remains visible.

## Importing OptiScaler Patches

Use the patch import helper when the OptiScaler integration branch changes:

```powershell
.\scripts\import-optiscaler-patches.ps1 `
  -SourceRepo "onehoon/OptiScaler" `
  -SourceRef "optisensor-overlay" `
  -BaseRef "release/0.9" `
  -OutputDir "optiscaler/patches"
```

Patch conflicts during CI are expected to fail the build. Refresh the patch stack instead of auto-resolving conflicts in the workflow.

Detailed build notes are in [docs/build-optisensor-optiscaler.md](docs/build-optisensor-optiscaler.md).

## License

OptiSensor is licensed under the GNU General Public License version 3.0. See [LICENSE](LICENSE).
