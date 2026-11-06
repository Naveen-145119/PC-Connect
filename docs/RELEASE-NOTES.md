# PC Connect Windows preview

Download **PCConnect-Setup-win-x64.exe** for Intel/AMD, or **PCConnect-Setup-win-arm64.exe** for Windows on ARM. Setup includes .NET, installs the app for your user, creates desktop and Start menu shortcuts, and registers an uninstaller in Installed apps. Setup ZIPs contain one installer. Portable ZIPs are optional; GitHub Source code ZIPs are for developers.

Install the same version on both laptops, then follow the [pairing and office setup guide](https://github.com/Naveen-145119/PC-Connect/blob/main/docs/SETUP.md). Setup does not configure fingerprints, network policy or firewall rules automatically.

Includes native keyboard/mouse sharing, text clipboard, verified file/folder copies and a preview continuous held-button cross-screen **copy** bridge. Hold at the joining edge while files are transferred and verified, then continue dragging into the destination Explorer window. Originals are retained. Ctrl+Alt+F12 returns local control.

Validation: 26 automated tests, real two-process TLS/drag protocol integration and successful interactive local Windows OLE handoff. Two physical laptops and ARM64 execution still need qualification. The preview is unsigned and may require approval on managed office laptops. See [limits and evidence](https://github.com/Naveen-145119/PC-Connect/blob/main/docs/TEST-REPORT.md).

Check downloads against SHA256SUMS.txt using PowerShell `Get-FileHash <download> -Algorithm SHA256`. Source is MIT licensed. Both peers must run protocol version 2 (this 0.2 preview).
