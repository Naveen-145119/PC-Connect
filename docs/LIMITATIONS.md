# Status and limitations

PC Connect 0.2 is an **experimental Windows preview**. The source is MIT licensed. Read the [test report](TEST-REPORT.md) and complete the [two-laptop checklist](VALIDATION.md) before relying on it.

## Implemented

- Native dark Windows Forms interface, visual device arrangement, pairing, files and diagnostics pages.
- Dedicated Win32 hook thread and input injection worker; bounded queues preserve key/button order and coalesce consecutive motion. Ctrl+Alt+F12 returns control. Session loss triggers input release after approximately three seconds while the receiver is running.
- Mutually authenticated TLS, direct SHA-256 certificate pinning, DPAPI-protected per-user identity, separate control/bulk channels and automatic reconnect.
- Bidirectional text clipboard and verified file/folder copies, Unicode/empty/nested folders, unique inbox destinations, cancellation and paced transfers.
- Continuous held-button cross-screen **copy** bridge: capture ordinary Explorer CF_HDROP data at the edge, transfer and verify, cancel the source OLE drag, then start a native target OLE drag driven by the held gesture. No extra receiver inbox drag is required.
- Per-user self-contained installer, application icon, desktop and Start menu shortcuts, Installed apps registration, upgrade and uninstall support. Optional portable packages.

## Important boundaries

1. **Two physical laptops have not been qualified.** Automated protocol tests and the local held-button OLE fixture pass, but they do not establish end-to-end remote input/drag behavior, sleep/wake reliability or real LAN latency. ARM64 is cross-built only.
2. Drag preparation waits at the edge until all files arrive and verify. It is not instantaneous or streaming. Only unmodified left-button filesystem copy drags are supported, using the primary mouse in either direction. No MOVE/delete, virtual Outlook attachments, elevated windows or arbitrary third-party targets. Cancel/restart to reverse a handoff. Ten-minute gesture deadline.
3. Explorer owns the final destination copy after accepting CF_HDROP; an accepted drop is not a claim that Explorer has finished copying. Verified cache is retained until manually cleaned. Destination conflicts are handled by Explorer. Budget disk space for cache plus destination.
4. Only two laptops and one primary display per laptop, arranged horizontally. No video streaming, vertical/multi-monitor topology, touch/pen/game controllers or multiple peers. The primary pointer is parked centrally while remotely controlling the secondary.
5. Ordinary SendInput cannot control login, Ctrl+Alt+Delete, UAC secure desktop, elevated applications or protected games. No service, driver, uiAccess or elevation bypass is installed. Injection failure disconnects.
6. Target keyboard layout determines scan-code output. Match layouts; IMEs, dead keys, unusual keys, international layouts and high-frequency gaming input need hardware testing.
7. Files are copied, never moved. Maximum 20 GiB/10,000 entries/64 source nesting levels. No resume, compression, ACL/owner/ADS/timestamp preservation, or whole-directory snapshot. Symlinks/junctions/reparse placeholders are rejected. OS/path restrictions still apply.
8. Clipboard is plain text only, limited to 65,536 UTF-16 code units. No images, rich text, file objects or history. A trusted peer receives shared clipboard text; disable sharing for sensitive content.
9. Forced termination of the receiver during injected held input can leave Windows input state held; no separate guardian service exists. Use the local keyboard/mouse to release keys, or sign out if necessary. Abrupt power/process loss can leave `.partial-*` staging folders.
10. Lost transfer acknowledgements can produce a sender error after a verified copy arrived. Check the inbox before retrying. SHA-256 detects corrupt content; it does not guarantee power-loss durability or scan files for malware.
11. Default cap 512 KiB/s per sender, adjustable 128–4096 KiB/s; RTT backoff can reduce it. No OS/router QoS guarantee. Loopback RTT excludes physical capture, injection, rendering and actual LAN delays.
12. Unsigned preview binaries, no automatic update/startup/tray mode, no certificate-rotation UI or independent security/accessibility audit. Install and run under your organization's policy. No internet relay or firewall modifications.

## Native drag design references

The bridge follows Windows OLE's local source/data-object/drop-target model: [DoDragDrop](https://learn.microsoft.com/en-us/windows/win32/api/ole2/nf-ole2-dodragdrop), [IDropSource::QueryContinueDrag](https://learn.microsoft.com/en-us/windows/win32/api/oleidl/nf-oleidl-idropsource-querycontinuedrag) and [Shell clipboard formats](https://learn.microsoft.com/en-us/windows/win32/shell/clipboard). The app uses materialized CF_HDROP paths instead of blocking network reads inside COM data streams. Target release is signaled to the owned OLE capture loop, without injecting a new destination click.
