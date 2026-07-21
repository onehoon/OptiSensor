# OptiScaler Shared-Memory Protocol Reference

This folder documents the shared-memory contract that OptiSensor's `ExternalOverlayPublisher`
implements. The OptiScaler-side patch stack that reads this feed lives in the
`release/0.9`/`release/0.10` branches of this repository, not here.

- `protocol/OptiExternalOverlayProtocol.h` documents the shared memory payload contract used by
  OptiSensor and the patched OptiScaler overlay reader.

External overlay lines are UTF-8, null-terminated byte strings. `MaxLineLength` is 128 bytes
including the trailing null byte, and the protocol payload size remains 544 bytes.
