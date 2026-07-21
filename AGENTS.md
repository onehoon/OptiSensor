# OptiSensor Patch Branch Instructions (release/0.10)

## Branch policy

- This branch (`release/0.10`) carries only the OptiScaler patch stack targeting upstream `optiscaler/OptiScaler` `master`, its docs, the patch import script, and the `Build OptiScaler Only` workflow. There is no `src/OptiSensor` app source here.
- OptiSensor application source work belongs on the `OptiSensorApp` branch: branch off `origin/OptiSensorApp` and open PRs against `OptiSensorApp`.
- OptiScaler `release/0.9` patch work belongs on the `release/0.9` branch.
- `main` is a frozen backup of the pre-split combined history (app + patches together). Do not develop on `main`.
- Do not copy `src/OptiSensor` changes into this branch, or this branch's patches into `OptiSensorApp`, unless the user explicitly asks for it.

## Patch stack maintenance

- Refresh patches with `scripts/import-optiscaler-patches.ps1` (see docs/build-optisensor-optiscaler.md). Never hand-edit `optiscaler/patches/*.patch` in place.
- Patch conflicts in CI are expected to fail the build; refresh the patch stack instead of auto-resolving conflicts in the workflow.
