# OptiSensor Patch Branch Instructions (release/0.9)

## Branch policy

- This branch (`release/0.9`) carries only the OptiScaler patch stack targeting upstream `optiscaler/OptiScaler` `release/0.9`, its docs, the patch import script, and the `Build OptiScaler Only` workflow. There is no `src/OptiSensor` app source here.
- OptiSensor application source work belongs on the `main` branch: branch off `origin/main` and open PRs against `main`.
- OptiScaler `master` (0.10 line) patch work belongs on the `release/0.10` branch.
- `backup/pre-split` is a frozen backup of the pre-split combined history (app + patches together). Do not develop on it.
- Do not copy `src/OptiSensor` changes into this branch, or this branch's patches into `main`, unless the user explicitly asks for it.

## Patch stack maintenance

- Refresh patches with `scripts/import-optiscaler-patches.ps1` (see docs/build-optisensor-optiscaler.md). Never hand-edit `optiscaler/patches/*.patch` in place.
- Patch conflicts in CI are expected to fail the build; refresh the patch stack instead of auto-resolving conflicts in the workflow.

## Local upstream cache

- `.work/OptiScaler-0.9` and `.work/OptiScaler-0.10` are ignored local upstream
  clone caches used only by the DLL build scripts. Their checked-out
  `OptiScaler.ini` files are not schema inputs and must not be cited as a
  durable source of truth.
- The build scripts fetch, detach at, and clean the requested upstream revision,
  so deleting an individual file in `.work` is not persistent: the next build
  restores it from upstream. For schema work, fetch the matching upstream
  branch's root `OptiScaler.ini` and source code directly instead.
