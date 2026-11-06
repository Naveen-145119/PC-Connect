# Development

## Requirements and build

Windows x64, PowerShell, Git and .NET SDK 10.0.403. `bootstrap.ps1` installs a verified SDK into `work/tools/dotnet` without changing the system SDK. `build.ps1 -Installer` runs the tests, publishes self-contained x64/ARM64 packages, installs the verified Inno Setup 7.1.1 x64 compiler locally if needed, compiles both installers and writes SHA256SUMS.txt. The compiler requires an x64 build host; target ARM64 binaries are cross-published.

```powershell
.\bootstrap.ps1
.\build.ps1 -Installer
# Focused regression run (DOTNET_ROOT is needed by the two-process fixture):
$env:DOTNET_ROOT = "$PWD\work\tools\dotnet"
& "$env:DOTNET_ROOT\dotnet.exe" run --project tests\PCConnect.Tests -c Release -- --report outputs\test-results.txt
# Interactive local OLE handoff fixture:
.\outputs\packages\PCConnect-win-x64\PCConnect.exe --drag-lab "$PWD\work\ole-lab"
```

For the fixture, hold the left button from SOURCE, move slowly through BOUNDARY and release over TARGET. PASS requires a source OLE cancel, a second OLE Copy result and verified fixture contents. Test Escape/release cancellation too. This is not a substitute for two physical laptops.

## Architecture

- **Core:** length-prefixed JSON protocol version 2, pinned mutual TLS, heartbeat/liveness, bounded input mailbox, file manifest/path validation, chunk pacing, SHA-256 staging and atomic publication.
- **App:** a UI STA for WinForms/OLE, a dedicated hook STA/message pump, and an independent input injection worker. File/network work never runs in low-level input hooks. UI progress is latest-only and bounded.
- **Channels:** TCP base port (45831) for authenticated input/control; base + 1 for individually authenticated, session-bound file/clipboard connections. No discovery broadcast, cloud relay or runtime internet request.
- **Drag:** offer → accepted → verified file transfer → ready → activate → finished/cancel. One-use permits authorize only the current session/drag ID. Input and activation remain ordered. Terminal IDs cannot revive a canceled gesture. The source drag is canceled without MOVE; the receiver supplies verified local CF_HDROP paths to a real DoDragDrop loop. Remote button lifetime determines target release.
- **Storage:** `%LOCALAPPDATA%/PCConnect` contains DPAPI identity/settings, Received inbox and retained DragCache. Installed binaries live separately in `%LOCALAPPDATA%/Programs/PC Connect`. Uninstall preserves user data.

## Release process

1. Update the app csproj version, CHANGELOG and validation evidence. Keep both peers on the same protocol version.
2. Run `build.ps1 -Installer` and the applicable hardware checks. Do not present cross-built ARM64 or loopback tests as physical-device validation.
3. Push source and require the Windows build workflow to pass.
4. Run **Publish preview release** in GitHub Actions with the exact csproj version. The workflow builds the selected commit, creates a `vVERSION` tag/release and uploads installers, Setup ZIPs, Portable ZIPs and checksums. It refuses a mismatched version or existing release. It needs repository Contents write permission and is manually dispatched, never run on fork pull requests.
5. Download an asset, verify its SHA-256 against the manifest, and confirm release notes distinguish measured results from pending checks. The initial preview may also be uploaded with the authenticated GitHub CLI after the same checks.

The Windows CI workflow compiles both installers and runs the same regression suite on every main push and pull request. Artifacts expire after 14 days; public release assets are the download channel. Actions use pinned commit SHAs. Executables are currently unsigned; a future release process should sign artifacts using a securely managed certificate before checksum generation. Never commit signing keys, pairing identities, tokens or user files.
