# OptiScaler release/0.10 Patch Branch

This branch carries only the OptiScaler patch stack targeting the upstream
`optiscaler/OptiScaler` `master` branch (the future 0.10 line), plus the build
workflow that produces a patched `OptiScaler.dll`.

The OptiSensor application source lives on the `main` branch and has
its own CI (`Build OptiSensor Only`). This branch contains no app source. Users
install the OptiSensor helper from the `main` release and pair it with
the `OptiScaler.dll` built here.

## Patch Stack

OptiScaler changes are stored in this branch under:

```text
optiscaler/patches/*.patch
```

The GitHub Actions workflow clones the selected OptiScaler repository/ref, applies these patches with `git am`, and then builds the patched `OptiScaler.vcxproj`.

Patch conflicts are expected to fail the workflow. Do not auto-resolve conflicts in CI; refresh the patch stack instead.

## Importing Patches From an OptiScaler Branch

Use the helper script when the OptiScaler integration branch has new commits:

```powershell
.\scripts\import-optiscaler-patches.ps1 `
  -SourceRepo "onehoon/OptiScaler" `
  -SourceRef "optisensor-overlay" `
  -BaseRef "master" `
  -OutputDir "optiscaler/patches"
```

The script:

1. Validates that `OutputDir` resolves to a path inside this repository (and is not the repository root itself) before doing anything else.
2. Clones the selected OptiScaler repository into a unique temporary run directory under `.tmp/` and resolves `SourceRef` and `BaseRef` to commits.
3. Verifies that `BaseRef` is an ancestor of `SourceRef` and that the `BaseRef..SourceRef` range contains at least one commit.
4. Runs `git format-patch BaseRef..SourceRef` into a temporary staging directory (the real `optiscaler/patches` is not touched yet).
5. Smoke-tests the generated patches with plain `git am` against a clean checkout of `BaseRef`, then confirms the resulting tree matches `SourceRef`'s tree.
6. Only after all of the above succeed, replaces `*.patch` files in `OutputDir` using a backup-and-rename swap, preserving any non-`*.patch` files already there.
7. Prints the generated patch list and a final success summary.

If any validation or smoke-apply step fails, the existing patch stack in `OutputDir` is left unmodified, and the temporary run directory is cleaned up.

If the source branch is not split into clean commits, create a single integration patch first and then split it later when the branch is easier to maintain.

## Manual GitHub Action

Workflow file:

```text
.github/workflows/build-optiscaler-only.yml
```

Workflow name:

```text
Build OptiScaler Only
```

Inputs:

| Input | Purpose |
| --- | --- |
| `optiscaler_ref` | Branch, tag, or commit SHA to build from. Default `master`. |

Fixed workflow values:

| Value | Setting |
| --- | --- |
| OptiScaler repository | `optiscaler/OptiScaler` |
| OptiScaler configuration | `Release` |
| OptiScaler platform | `x64` |

The workflow:

1. Checks out this branch (patches only).
2. Clones the selected OptiScaler source repository/ref.
3. Applies `optiscaler/patches/*.patch` with `git am`.
4. Builds `OptiScaler.vcxproj`.
5. Uploads the patched `OptiScaler.dll` as a GitHub Actions artifact (14-day retention).

It intentionally does not build `OptiScaler.sln`; only the patched `OptiScaler.dll` is needed.

## Overlay Defaults

The patch stack is expected to make patched OptiScaler builds work with existing game folders even when new keys are missing from an old `OptiScaler.ini`.

For a new or unchanged INI file:

```ini
ShowFps=auto        ; compiled default: true
FpsOverlayType=auto ; compiled default: 7 = Just FPS (+External)
Scale=auto          ; compiled default: 1.3 at 1080p and above, still auto-scales lower below 900p
```

There is no separate `AppendExternalOverlayText` INI key or toggle in this patch stack. Selecting overlay type `7` adds the shared-memory external text to OptiScaler's existing Just FPS output.
