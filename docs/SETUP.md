# Two-laptop setup

## Install on both laptops

Download **PCConnect-Setup-win-x64.exe** from [GitHub Releases](https://github.com/Naveen-145119/PC-Connect/releases) for Intel/AMD laptops. Choose **PCConnect-Setup-win-arm64.exe** for Windows-on-ARM. Check Settings → System → About → System type if unsure. Both laptops need the same PC Connect version.

If you download the **Setup ZIP**, extract it and run the single Setup.exe inside. The automatic GitHub **Source code ZIP** is for developers; it is not the installer. A separately labeled Portable ZIP is available for users who do not want installation.

Run Setup, accept the MIT license, and complete the wizard. It installs for your current Windows user under `%LOCALAPPDATA%\Programs\PC Connect`, adds a desktop shortcut and a Start menu folder, and registers **PC Connect** in Windows Installed apps. .NET is bundled: no SDK, separate runtime, account or subscription is needed. Administrator rights are not requested by the installer. Launch from the desktop shortcut or the final setup screen.

Use a supported Windows 11 release. 32-bit Windows is not packaged. These preview binaries are unsigned and may trigger Windows reputation warnings. On an office laptop, get the software and peer-network connection approved by IT; follow company policy if installation is blocked.

Setup installs the application; pairing below is a one-time step on each laptop. It does not change firewall rules, install a driver/service, or enable startup at sign-in.

## Pair

1. Put both laptops on the same trusted Wi-Fi or Ethernet LAN. Guest Wi-Fi/client isolation may prevent a connection.
2. Open **Pair devices** on each laptop. Select **Primary · personal laptop** on your personal machine and **Secondary · office laptop** on your office machine.
3. Select the office laptop's position, right or left, to match your desk. Only each laptop's primary display is supported.
4. On each laptop, copy its fingerprint and enter it into the other laptop's **Device fingerprint** field. Compare the full 64 hexadecimal characters directly on the two screens. The fingerprint is public; private pairing keys are never exported by this interface.
5. On the primary, enter the office laptop's LAN IPv4 address from its pairing screen. If several addresses appear, use the address belonging to your shared Wi-Fi/Ethernet network, not a VM/VPN adapter. Hostnames also work if your LAN resolves them.
6. Keep the same port on both machines (default 45831). Start the secondary with **Connect securely**, then start the primary. Status becomes **Connected · authenticated TLS**. Both apps must remain running.
7. On the primary, move the pointer across the adjoining screen edge while no buttons or keys are held. Move back across the corresponding edge to return.

Fingerprints are saved locally. Reconnection retries automatically every two seconds while the app remains connected/listening. If the peer's identity changes, verify the new fingerprint in person and update pairing. Identities expire after three years and must then be regenerated/re-paired.

## Return control and disconnect

**Ctrl + Alt + F12** returns control to the primary on the normal desktop. **Return control** is also available on Pair devices. Disconnect stops sharing; closing the app disconnects and removes hooks. A broken session releases remotely injected held keys/buttons after the approximately three-second liveness deadline. These guarantees cannot override the Windows secure desktop or a forced termination of the receiving process.

Your office laptop continues running its own applications and remains a normal independent laptop when disconnected. No startup service or permanent keyboard/mouse driver is installed.

## Clipboard

Use **Share text clipboard** on Overview. Turn it off on either laptop to disable incoming and outgoing text sharing on that laptop. Text up to 65,536 UTF-16 code units is supported; rich text, images, file clipboard objects and clipboard history are not synchronized. Clipboard sharing is session-bound and uses a separate TLS connection.

## Files and folders

Open **File transfers**. Drop files/folders into the large drop area or choose them using the buttons. Both laptops can send. Originals remain in place. Nested and empty folders and Unicode names are supported; symlinks, junctions and reparse-point placeholders are rejected.

Files arrive under `%LOCALAPPDATA%\PCConnect\Received\Received-<timestamp>-<unique-id>`. **Open inbox** opens that folder. This default intentionally avoids OneDrive-redirected Documents. Each transfer receives a new folder, so existing files are never silently overwritten. A transfer is published only after all file hashes match. `.partial-*` folders indicate incomplete staging and must not be treated as completed transfers.

Progress reports byte counts and verification completion. **Cancel transfer** cancels active sending and receiving. A dropped network connection cancels the transfer; restart it after reconnection. Partial transfers are not resumed. If the receiver completed a transfer but its acknowledgement was lost, the sender may report failure even though a verified copy exists; check the inbox before retrying.

One outgoing and one incoming file transfer can run at a time. Limits: 10,000 entries, 20 GiB total, 64 source nesting levels, and 1,000 characters per relative path (individual names up to 255). Free disk space is checked before accepting files. Long destinations may still be restricted by the OS/filesystem.

The default transfer cap is 512 KiB/s in each sending direction, with 16 KiB chunks. RTT inflation reduces the cap to at most 128 KiB/s. Lower the cap before connecting on a slow or congested LAN. Simultaneous transfers in both directions can consume twice the selected cap. Application pacing cannot control unrelated traffic or guarantee Wi-Fi performance.

## Continuous cross-screen drag (preview)

1. Enable **Cross-screen drag (preview)** on both connected laptops. Use the primary laptop's mouse throughout, including when starting a drag in the secondary laptop's Explorer.
2. Begin an ordinary left-button drag of real files/folders in Explorer. Move to the thin amber joining edge and **keep holding**. The files are copied to a private staging folder on the other laptop and verified first. Large files require a wait at the edge.
3. When the pointer crosses to the other screen, continue holding and move into a normal Explorer folder or onto the desktop. Release once to copy there. No separate inbox drag is required. Explorer handles its own destination conflicts and final copy progress.
4. Press Escape to cancel, or Ctrl+Alt+F12 to return control. Releasing or leaving the boundary before handoff cancels preparation. Do not use Ctrl/Shift/Alt modifiers or a right-button drag.

This is **copy only**: originals are never deleted. Both directions are implemented using the primary mouse. One drag can be active at a time. If you want to reverse direction during an active handoff, cancel and start again. The bridge uses verified materialized files, so it is not an instant cross-screen transfer or network-backed streaming drop. The preparation/gesture deadline is 10 minutes.

Verified drag data is retained in `%LOCALAPPDATA%\PCConnect\DragCache` because Explorer may read it asynchronously after accepting the drop. A drag temporarily needs space for both the cache and destination copy. Once Explorer has finished copying and PC Connect is closed, you may remove old cache folders. Keep the cache until you have verified the destination copy. The normal panel inbox is separate.

The held-button OLE handoff passed a local interactive test. Full physical two-laptop validation is still pending; see [VALIDATION.md](VALIDATION.md). Virtual attachments, elevated targets and arbitrary application drop targets are not qualified.

## Network access

The app does not create or modify firewall rules. If your firewall/corporate policy requires rules, ask the administrator for narrowly scoped access:

- Secondary: inbound TCP 45831 (control) from the primary's LAN IP; inbound TCP 45832 (transfers/clipboard) from the primary.
- Primary: inbound TCP 45832 from the secondary.
- Outbound TCP to those peer ports on both laptops.
- Scope any allowance to the specific `PCConnect.exe`, the trusted/private network, and the other laptop's IP. If you change the base port, the bulk port is base + 1.

No internet endpoint is contacted by the running application. Do not expose these ports to the internet or disable the firewall. Corporate restrictions may require IT approval; the app does not bypass them.

## Diagnostics and removal

Diagnostics shows network RTT and connection events without recording input/clipboard/file contents. RTT is an application ping/pong over the encrypted control connection, including scheduling and framing overhead; it is not total input latency.

Close PC Connect, then use **Settings → Apps → Installed apps → PC Connect → Uninstall**. The application, desktop shortcut and Start menu shortcuts are removed. Pairing identity, settings, received files and drag cache remain under `%LOCALAPPDATA%\PCConnect` so an upgrade or reinstall does not lose them. Remove that folder yourself only if you want to erase the local identity and all retained data; re-pair afterward. Uninstalling does not delete files you already copied elsewhere in Explorer.

To upgrade, close PC Connect on both laptops and run the newer Setup on each. The same installation and shortcuts are updated. Protocol version 2 in the 0.2 preview cannot pair with the older 0.1 build.

## Troubleshooting

- **Already running:** close the old portable/installed window before launching another copy.
- **Cannot connect:** start the secondary first; check roles, IP, complete fingerprints, matching versions, network isolation and firewall rules below. VPNs can change routes.
- **Installer blocked:** give IT the release URL, SHA-256 manifest, source repository and requested network ports. Do not disable managed security controls.
- **Slow file preparation:** wait at the boundary or use the panel for large transfers. The default 512 KiB/s limit prioritizes input responsiveness; it is adjustable before connecting.
- **No drag handoff:** check the preview toggle on both peers, ordinary filesystem files, the correct joining edge and a continuously held left button. Inspect Diagnostics and retry with a small disposable file.
- **Office laptop away from the personal laptop:** both need direct LAN reachability. This app is not a cloud relay or remote-desktop service.
