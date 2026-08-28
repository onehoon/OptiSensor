# OptiScaler External Overlay Protocol

## Overview

OptiScaler does not read hardware sensor data directly. A helper application writes one external text line to shared memory, and OptiScaler appends that line only when the FPS overlay type is `7 = Just FPS (+External)`.

```text
Helper application
  -> Local\OptiScalerExternalOverlay shared memory
  -> OptiScaler overlay type 7
  -> FPS: 123.4 | CPU 65C GPU 71C
```

Overlay types `0` through `6` retain their upstream OptiScaler behavior and do not open or read the external shared-memory mapping.

## Shared Memory Contract

| Field | Value |
| --- | --- |
| Mapping name | `Local\OptiScalerExternalOverlay` |
| Mapping size | 544 bytes |
| Text encoding | UTF-8, null-terminated |
| Displayed line | `lines[0]` only |
| Maximum text length | 127 bytes plus the null terminator |
| Freshness window | 5 seconds from the last update (`StaleAfterMs = 5000` in the `release/0.10` consumer) |
| Mapping open retry | At most once per second |

The canonical C++ definition lives in `optiscaler/protocol/OptiExternalOverlayProtocol.h` on the `release/0.9`/`release/0.10` patch branches (this branch carries only the app source, not the OptiScaler-side header):

```cpp
struct Payload
{
    uint32_t magic;              // 0x564F534F, "OSOV"
    uint32_t version;            // 1
    volatile uint32_t sequence;  // Stable read coordination
    uint64_t lastUpdateTickMs;   // GetTickCount64 value
    uint32_t lineCount;          // 1 through 4
    char lines[4][128];          // UTF-8, null-terminated
};
```

## Helper Writer Requirements

1. Create or open the `Local\OptiScalerExternalOverlay` memory mapping with read/write access and a size of 544 bytes.
2. Update the payload every 100 to 1000 milliseconds.
3. Increment `sequence` to an odd value before writing.
4. Write `lines[0]`, set `lineCount` to `1`, and set `lastUpdateTickMs` from `GetTickCount64()`.
5. Issue a memory barrier, then increment `sequence` to an even value.
6. Set `magic` to `0x564F534F` and `version` to `1` when creating the payload.

Writers must not exceed 127 UTF-8 bytes in `lines[0]`. The remaining byte is reserved for the null terminator.

## Failure Behavior

The external overlay is an optional addon.

- If the mapping does not exist, OptiScaler keeps rendering the normal Just FPS overlay.
- If the payload is invalid, stale, or being written, OptiScaler ignores the external line.
- If an exception occurs while reading the mapping, OptiScaler ignores it.
- No popup, fail state, or game-flow change is produced by an external overlay failure.
- If the helper stops updating for more than five seconds, the external text disappears automatically.

## Session Constraint

The mapping uses the `Local\` namespace. The helper application and the game process running OptiScaler must use the same Windows login session.
