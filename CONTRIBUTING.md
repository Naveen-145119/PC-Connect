# Contributing

Thanks for helping improve PC Connect. Open an issue describing the problem, expected behavior and environment before a large redesign or protocol change. Small fixes can go directly to a pull request.

1. Fork and create a focused branch. Read [development](docs/DEVELOPMENT.md) and [limitations](docs/LIMITATIONS.md).
2. Use the pinned SDK and keep changes compatible with Windows x64/ARM64. Keep disk/network work outside input hooks. Preserve bounded queues and cancellation.
3. Add meaningful regression coverage for changed protocol/security/state behavior. Run `build.ps1 -Installer` and report which physical tests you actually performed.
4. Explain behavior, validation and remaining limitations in the pull request. Do not include pairing keys, clipboard content, real keystrokes, private documents or environment credentials in logs.

Protocol changes must consider both peers and reject unsupported versions cleanly. Never claim hardware validation from a loopback test. Contributions are submitted under the repository's MIT license. Be respectful and discuss technical disagreements with evidence.
