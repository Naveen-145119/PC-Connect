# Test report — 6 November 2026

## Environment and evidence

Windows x64, build 10.0.26300.0; verified .NET SDK 10.0.403 / runtime 10.0.14. No second physical laptop or ARM64 hardware was available. Automated raw output is retained in release packages and CI artifacts; run `build.ps1` to reproduce the suite locally.

**26 automated tests passed.** Coverage includes framing, invalid inputs, bounded ordered queues, held-input cleanup, paths/quotas, mutual TLS and wrong-peer rejection, nested/empty/Unicode folders, hash corruption, transfer cancellation/disconnect cleanup, adaptive pacing, timeout, delayed heartbeat replies, drag state transitions/terminal cancellation, gesture lifetime, one-use session-bound drag permits and two-process integration.

The integration test runs two real PeerNode processes on loopback addresses 127.0.0.1 and 127.0.0.2. It exercises bidirectional files/clipboard, synthetic ordered input without desktop injection, reconnect, the drag offer/permit/transfer/ready/activate/finished exchange and unauthorized drag rejection. Each process verifies a drag transfer; ordinary inbox counters exclude drag cache transfers.

## Local native OLE handoff

The interactive `--drag-lab` fixture uses the production OleDragHost with real Windows OLE loops and an actual held mouse button. On 6 November the user completed three successful source → boundary → target gestures. The fixture reports source effect **None**, target effect **Copy**, handoff **true**, and verified file contents/Unicode and empty directories **true**. Originals remained in place. A handoff cancellation was also recorded. The initial automated pointer gesture canceled before reaching the boundary.

This proves the local source-loop cancellation and new target-loop copy can span one physical hold. It does **not** prove cross-machine transport, physical hook routing and Explorer interoperability together; that requires the two-laptop checklist.

## Loopback measurement (representative recorded run)

| Condition | Samples | Median | p95 | Maximum |
|---|---:|---:|---:|---:|
| Idle | 20 | 0.83 ms | 1.27 ms | 8.24 ms |
| During 8 MiB transfer at 1 MiB/s | 33 | 0.42 ms | 0.76 ms | 0.99 ms |

Transfer: 8.52 seconds, 0.94 MiB/s. The lower busy measurement reflects short-run scheduling variability. RTT includes TLS framing and scheduling, not physical input capture, rendering, Wi-Fi or total input latency. New runs can differ.

## Native and packaging checks

The x64 application was launched and its native redesigned interface inspected. Native self-checks cover hook install/remove, reopening the DPAPI identity and 40-byte Win32 INPUT layout. Both x64 and ARM64 are self-contained builds. The x64 installer passed actual per-user install, same-version upgrade, uninstall and reinstall checks: desktop and Start menu shortcut targets, Installed apps registration and executable presence/removal were verified. Existing identity and a retained-data fixture survived. The installed executable passed native self-check and GUI smoke (exit 0). PC Connect was left installed. ARM64 installer behavior has not been executed on ARM hardware.

Not yet qualified: two physical laptops, ARM64 execution, long real-world remote input/drag sessions, DPI/layout combinations, sleep/wake, UAC transitions, real Wi-Fi congestion, accessibility/high contrast, or an independent security audit. This is a preview, not a production-certification claim.
