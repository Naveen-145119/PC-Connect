# Real two-laptop validation checklist

Record both laptop models, Windows build, architecture, primary-display resolution, scale, keyboard layout, app package SHA-256, network type/link rate and selected transfer cap. Keep a local mouse/keyboard available on both. Perform tests using synthetic/non-sensitive text and disposable files.

## Pairing and control

- [ ] Start both applications as ordinary users. Check saved identities stay stable across restart.
- [ ] Pair by comparing/exchanging both complete fingerprints. Verify connected status on both.
- [ ] Change one fingerprint to a wrong value: connection must fail and no input/clipboard/files may be accepted. Restore the correct fingerprint.
- [ ] Test left and right screen arrangements. Cross at top/middle/bottom; return at the corresponding boundary; check there is no immediate bounce, drift or missing screen region.
- [ ] Repeat with differing resolutions and 100%, 125%, 150% and 200% DPI; change a display resolution while connected. Only primary displays are in scope.
- [ ] Test left/right/middle/X buttons, double-click, vertical/horizontal wheel, normal text, Shift/Ctrl/Alt/Win shortcuts, key repeat, function/navigation/numpad keys, and the layouts you use. Do not type passwords for this test.
- [ ] Hold a key for more than 3 seconds without movement; it should remain held while connected and release correctly.
- [ ] Press Ctrl+Alt+F12 from primary and secondary physical keyboards; primary control must return. Test Return control too.
- [ ] Disconnect while holding a modifier and mouse button. Confirm cleanup, no stuck input and an independently usable secondary.

## Network loss, sleep and Windows restrictions

- [ ] Disconnect Wi-Fi/Ethernet on the primary while controlling the secondary. Confirm local primary control and receiver key/button release in approximately 3 seconds. Restore network; confirm reconnect without re-pairing.
- [ ] Repeat from secondary; repeat sleep/wake on each laptop. Reconnection must remain visibly indicated.
- [ ] Close the app normally during a held key/button and during a transfer. Repeat after reconnecting.
- [ ] Open a UAC prompt or elevated app while controlling the secondary. Verify input is blocked/fails back; never bypass the prompt. Test lock/unlock manually.
- [ ] After disconnect, take the secondary away from the network and use its local applications/keyboard/mouse normally.

## Clipboard and files

- [ ] Copy ASCII, multiline, Telugu and emoji text in both directions. Disable clipboard sharing on either peer and confirm neither incoming nor outgoing text is shared there. Confirm image/rich-text data is not silently converted or synchronized.
- [ ] Transfer files and a folder containing nested folders, empty folders, zero-byte files, Unicode names and a file larger than available RAM if within the 20 GiB limit.
- [ ] Compare SHA-256 with Get-FileHash at source and destination. Resend the same selection; it must create a new destination folder without overwriting the earlier copy.
- [ ] Cancel from sender and receiver separately at early/middle/late progress. No completed-looking partial files should appear; ordinary cancellation removes staging.
- [ ] Interrupt network mid-transfer. Restore it, reconnect, and retry; canceled partial transfers must not resume silently.
- [ ] Verify full-disk, read-only source and permission failures show a useful failure status. Leave enough space for the OS; do not deliberately fill the system drive.
- [ ] Test a junction/symlink/cloud placeholder in the selection; it must be rejected rather than followed outside the tree.

## Cross-screen drag preview

- [ ] With the primary mouse, drag a small Explorer file to the edge, hold until the pointer crosses, then release in the other Explorer window. Repeat in reverse while remotely controlling the secondary. A single physical button hold must span each handoff.
- [ ] Verify destination SHA-256, nested/empty/Unicode folders and untouched originals. Test desktop drops and Explorer conflict handling.
- [ ] Leave the boundary or release during preparation; verify no destination drop. Repeat with Escape, Ctrl+Alt+F12, Disconnect, app close and network loss during preparation and after activation.
- [ ] Release immediately as the handoff starts. A canceled/released gesture must not reappear after late ready/activate packets.
- [ ] Test a large file and slow cap: the HUD must explain preparation, input must stay responsive, and the user must be able to cancel without losing originals.
- [ ] Disable the preview toggle on either peer; offers must be rejected. Reject simultaneous/replayed/unauthorized drag transfers.
- [ ] Verify a non-file virtual source and elevated destination fail safely. Verify no destructive MOVE effect is offered.
- [ ] Confirm Explorer completes its copy after accepting the drop, then clean up test cache only after copying ends.
- [ ] Confirm the independent panel/inbox transfer and outgoing inbox drag still work.

## Installer

- [ ] Install as a standard user; verify desktop/Start menu shortcuts, correct architecture and Installed apps entry.
- [ ] Launch without a separately installed .NET runtime. Re-run Setup; verify identity, settings and files survive.
- [ ] Uninstall; verify app and shortcuts are removed while received files and identity remain. Reinstall and reconnect.
- [ ] Repeat installation and runtime validation on ARM64 hardware; a successful cross-build is insufficient.

## Responsiveness

- [ ] Measure displayed network RTT for 60 seconds idle. Record median/p95/max if collecting samples externally; screenshots alone are not an input-latency measurement.
- [ ] Transfer at least 1 GiB in one direction at the default 512 KiB/s cap. During it, move the pointer, click, scroll and type continuously. Record displayed RTT and visible stutter/stuck-input events.
- [ ] Repeat reverse direction and simultaneous transfers. Try 128 KiB/s on constrained Wi-Fi; confirm lower cap and RTT backoff improve behavior.
- [ ] Repeat over Ethernet and ordinary Wi-Fi. Test normal real congestion without disrupting other users. The app cannot prioritize itself over all other network traffic.
- [ ] For end-to-end input latency, use a high-frame-rate camera viewing physical action and secondary response. Keep this result separate from network RTT and report the method/frame rate.

Do not mark the app production-qualified until these checks pass on the actual two laptops. Record failures with step number, timestamps, OS/app versions and connection state, without including keystrokes, clipboard contents, credentials or private file data.
