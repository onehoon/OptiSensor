# Build OptiSensor Package

This repository is the build and packaging hub for the OptiSensor helper and the patched OptiScaler overlay integration.

The release package is intentionally small:

```text
OptiScaler.dll
OptiScaler.ini
OptiSensor-Setup.exe
```

`OptiSensor.exe` is published as a framework-dependent single-file executable and then packaged into a Velopack installer, `OptiSensor-Setup.exe`. The combined package zip includes that installer, not `OptiSensor.exe` directly.

No scheduled build is configured. The package workflow is manually started with `workflow_dispatch`.

## Patch Stack

OptiScaler changes are stored in this repository under:

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
.github/workflows/build-optisensor-optiscaler.yml
```

Workflow name:

```text
Build OptiSensor Package
```

Inputs:

| Input | Purpose |
| --- | --- |
| `optiscaler_ref` | Branch, tag, or commit SHA to build from. |
| `publish_release` | When `false`, the combined package zip is uploaded only as a workflow artifact. When `true`, it is also uploaded to a GitHub Release. |

Fixed workflow values:

| Value | Setting |
| --- | --- |
| OptiScaler repository | `optiscaler/OptiScaler` |
| OptiScaler configuration | `Release` |
| OptiScaler platform | `x64` |
| Package prefix | `OptiSensor` |
| Release prerelease flag | `false` |

When release publishing is enabled, the workflow assigns the next `v0.1.N` tag by scanning existing `v0.1.*` tags in this repository and incrementing the highest patch number found.

## Build Policy

The workflow builds only `OptiScaler.vcxproj`.

It intentionally does not build `OptiScaler.sln`, because the package only needs the patched `OptiScaler.dll` and the patched `OptiScaler.ini`.

The workflow:

1. Checks out this OptiSensor repository.
2. Clones the selected OptiScaler source repository/ref.
3. Applies `optiscaler/patches/*.patch` with `git am`.
4. Finds and builds `OptiScaler.vcxproj`.
5. Collects exactly one `OptiScaler.dll`.
6. Collects `OptiScaler.ini`.
7. Publishes the OptiSensor helper as a Windows x64 framework-dependent single-file executable, then packages it into `OptiSensor-Setup.exe` with Velopack.
8. Creates the combined package zip and verifies its contents.
9. Always uploads the combined package zip as a GitHub Actions artifact.
10. Uploads the same combined package zip to a GitHub Release only when `publish_release=true`.

`OptiSensor.exe` is framework-dependent. It does not bundle the .NET runtime, so target machines must install the .NET 10 Desktop Runtime separately. Native helper libraries are bundled into the single executable.

## Test Build vs Release Build

Test build:

```text
publish_release=false
```

This creates the combined package zip, verifies its contents, and uploads it as a GitHub Actions artifact. No git tag is created and no GitHub Release is uploaded.

Downloading the artifact from the Actions run gives a GitHub-generated artifact archive containing exactly one file: the combined package zip (for example, `OptiSensor-0.1.12-master-a1b2c3d4e5f6.zip`). You must unzip that archive again to reach `OptiScaler.dll`, `OptiScaler.ini`, and `OptiSensor-Setup.exe`.

Release build:

```text
publish_release=true
```

This performs the same steps as the test build, uploading the combined package zip as a workflow artifact, and additionally:

1. Creates and pushes a `v0.1.N` git tag.
2. Uploads the Velopack release feed files to a GitHub Release.
3. Uploads the same combined package zip to the GitHub Release as an asset.

## Package Naming

The zip filename includes the fixed `OptiSensor` package prefix, the assigned OptiSensor version, sanitized OptiScaler ref, and resolved OptiScaler commit:

```text
OptiSensor-<optisensor_version>-<sanitized_optiscaler_ref>-<optiscaler_commit>.zip
```

Example:

```text
OptiSensor-0.1.12-master-a1b2c3d4e5f6.zip
```

## Overlay Defaults

The patch stack is expected to make patched OptiScaler builds work with existing game folders even when new keys are missing from an old `OptiScaler.ini`.

For a new or unchanged INI file:

```ini
ShowFps=auto        ; compiled default: true
FpsOverlayType=auto ; compiled default: 7 = Just FPS (+External)
```

There is no separate `AppendExternalOverlayText` INI key or toggle in the main patch stack. Selecting overlay type `7` adds the shared-memory external text to OptiScaler's existing Just FPS output.

## Out of Scope

The workflow only builds and packages the helper plus patched OptiScaler files.

Automatic updates, PawnIO installation, game activity based publishing, OptiScaler consumer alive detection, and runtime helper behavior changes are outside the packaging workflow.
