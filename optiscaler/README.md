# OptiScaler Patch Stack

This folder stores the OptiScaler overlay integration as patches owned by the OptiSensor repository.

- `patches/*.patch` is applied to a selected OptiScaler repository/ref during the manual GitHub Actions build.
- `protocol/OptiExternalOverlayProtocol.h` documents the shared memory payload contract used by OptiSensor and the patched OptiScaler overlay reader.

External overlay lines are UTF-8, null-terminated byte strings. `MaxLineLength` is 128 bytes including the trailing null byte, and the protocol payload size remains 544 bytes.

Release builds should use the patch stack in this repository instead of building a long-lived OptiScaler feature branch directly.
