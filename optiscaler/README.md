# OptiScaler Patch Stack

This folder stores the OptiScaler overlay integration as patches owned by the OptiSensor repository.

- `patches/*.patch` is applied to a selected OptiScaler repository/ref during the manual GitHub Actions build.
- `protocol/OptiExternalOverlayProtocol.h` documents the shared memory payload contract used by OptiSensor and the patched OptiScaler overlay reader.

External overlay lines are UTF-8, null-terminated byte strings. `MaxLineLength` is 128 bytes including the trailing null byte, and the protocol payload size remains 544 bytes.

The patch stack adds `7 = Just FPS (+External)` to OptiScaler's FPS overlay selector. Only this option reads the OptiSensor shared-memory feed; options `0` through `6` keep their upstream behavior.

Release builds should use the patch stack in this repository instead of building a long-lived OptiScaler feature branch directly.
