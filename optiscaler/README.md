# OptiScaler Patch Stack

This folder stores the OptiScaler overlay integration as patches owned by the OptiSensor repository.

- `patches/*.patch` is applied to a selected OptiScaler repository/ref during the manual GitHub Actions build.
- `protocol/OptiExternalOverlayProtocol.h` documents the shared memory payload contract used by OptiSensor and the patched OptiScaler overlay reader.

Release builds should use the patch stack in this repository instead of building a long-lived OptiScaler feature branch directly.
