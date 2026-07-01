# Build OptiSensor OptiScaler Package

This repository is the build and packaging hub for the OptiSensor helper and the patched OptiScaler overlay integration.

The release package is intentionally small:

```text
OptiScaler.dll
OptiScaler.ini
OptiSensor.exe
```

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
  -BaseRef "main" `
  -OutputDir "optiscaler/patches"
```

The script:

1. Clones the selected OptiScaler repository into `.tmp/`.
2. Fetches tags and prunes remote refs.
3. Checks out `SourceRef`.
4. Runs `git format-patch BaseRef..SourceRef`.
5. Replaces `optiscaler/patches/*.patch`.
6. Prints the generated patch list.

If the source branch is not split into clean commits, create a single integration patch first and then split it later when the branch is easier to maintain.

## Manual GitHub Action

Workflow file:

```text
.github/workflows/build-optisensor-optiscaler.yml
```

Workflow name:

```text
Build OptiSensor OptiScaler Package
```

Inputs:

| Input | Purpose |
| --- | --- |
| `optiscaler_repo` | OptiScaler repository to clone, for example `onehoon/OptiScaler`. |
| `optiscaler_ref` | Branch, tag, or commit SHA to build from. |
| `package_name` | Output package base name. |
| `release_tag` | GitHub Release tag used when release publishing is enabled. |
| `prerelease` | Marks the GitHub Release as prerelease. |
| `publish_release` | When `false`, only uploads a workflow artifact. When `true`, also uploads to GitHub Releases. |
| `optiscaler_configuration` | OptiScaler MSBuild configuration. |
| `optiscaler_platform` | OptiScaler MSBuild platform. |

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
7. Publishes the OptiSensor helper as a Windows x64 self-contained single-file executable.
8. Creates the final zip.
9. Always uploads the zip as a workflow artifact.
10. Uploads the zip to a GitHub Release only when `publish_release=true`.

## Test Build vs Release Build

Test build:

```text
publish_release=false
```

This creates the zip and uploads it as an Actions artifact only.

Release build:

```text
publish_release=true
```

This creates the same artifact and uploads it to the requested GitHub Release tag.

## Package Naming

The zip filename includes the package name, sanitized OptiScaler ref, and resolved OptiScaler commit:

```text
<package_name>-<sanitized_optiscaler_ref>-<optiscaler_commit>.zip
```

Example:

```text
OptiSensor-OptiScaler-main-a1b2c3d4e5f6.zip
```

## Overlay Defaults

The patch stack is expected to make patched OptiScaler builds work with existing game folders even when new keys are missing from an old `OptiScaler.ini`.

Expected compiled defaults:

```text
ShowFps = true
FpsOverlayType = FPS Only
AppendExternalOverlayText = true
```

Expected overlay example:

```text
FPS 111 | GPU 44C | 115W | 62%
```

## Out of Scope

The workflow does not implement advanced OptiSensor features.

PawnIO installation, advanced LibreHardwareMonitor sensor selection, tray UI, autostart, and richer Helper configuration are separate follow-up work.
