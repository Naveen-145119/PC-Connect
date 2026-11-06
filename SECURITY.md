# Security

PC Connect is an experimental preview without an independent security audit. Only the latest preview is maintained; there is no production support or response-time guarantee.

## Report privately

Use [GitHub private vulnerability reporting](https://github.com/Naveen-145119/PC-Connect/security/advisories/new) for suspected security problems. Include affected version, steps using synthetic data and expected/actual behavior. Do not post exploit details, private keys, clipboard content or sensitive files in a public issue. If private reporting is unavailable, open an issue requesting a private contact without vulnerability details.

## Trust model

Both users compare and exchange full SHA-256 device certificate fingerprints, then every connection uses mutually authenticated TLS with pin/validity checks. Local identity is DPAPI-protected for the current Windows user. Pinning is direct trust, not public-CA hostname verification. A paired device is trusted to receive input, shared clipboard text and offered files; do not pair with an unknown machine.

The app does not authenticate a human independently from the Windows account, isolate a compromised peer, scan transferred files for malware or bypass Windows integrity levels. It cannot protect data from malware already running as the same user. Use it only on approved, trusted networks/devices, keep Windows updated and disable clipboard sharing when appropriate.

Uninstall preserves `%LOCALAPPDATA%\PCConnect`, including identity and received/cache files. To revoke a peer, stop sharing and remove/replace its configured fingerprint; re-pair with a newly generated identity if compromise is suspected. Existing identities expire after three years. Close the app before manually archiving/removing identity data. No private identity is included in source or release packages.

See [limitations](docs/LIMITATIONS.md) for forced-termination input cleanup, staging/durability and pending physical validation. Network ports should be restricted to the specific trusted peer; never expose them to the public internet.
