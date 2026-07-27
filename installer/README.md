# Installer gate

> **Development only:** current installer output is unsigned and must not be
> promoted as a trusted public release.

The Inno Setup definition in `installer/NocturneHarbor.iss` packages only a
fresh, verified self-contained `win-x64` publish. It installs per user under
`%LOCALAPPDATA%\Programs\Nocturne Harbor`, creates a Start Menu shortcut, and
can optionally create a desktop shortcut.

The installer must not contain a watch-history database, provider credential,
official TMDB cache, generated user cache, local log, or other private artifact.
User-specific state remains under `%LOCALAPPDATA%` and Windows Credential
Manager; uninstall currently preserves it so reinstall is non-destructive. See
[`PRIVACY.md`](../PRIVACY.md) for manual purge steps.

Before any public installer release:

1. Build from a clean checkout and an empty publish/staging directory.
2. Run the complete restore, formatting, analyzer, unit, integration, security,
   self-test, performance, and publish-content gates.
3. Produce a file inventory, SHA-256 checksums, dependency inventory, source
   revision, and test evidence.
4. Scan source and final payload for secrets, private databases, caches, logs,
   local paths, and malware.
5. Sign the application and installer with a publisher-controlled Authenticode
   certificate and RFC 3161 timestamp.
6. Verify signatures and checksums after signing.
7. Test clean install, upgrade, repair, uninstall, rollback, and offline launch
   on supported Windows 11 systems.

Without those gates, installer output is an internal development artifact only.
