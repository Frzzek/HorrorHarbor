# Security Policy

## Supported status

HorrorHarbor is under active development. Source review and local builds are
supported; unsigned binaries and installers are development artifacts and must
not be represented as trusted, publisher-verified public releases.

## Reporting a vulnerability

Do not open a public issue containing credentials, watch history, private server
addresses, exploit details, or personal data. Use the repository's enabled
GitHub private vulnerability reporting feature.

Never send a real Plex token, TMDB token, database, log directory, or provider
cache as a proof of concept. Use synthetic canary values and a minimal test case.

## Implemented controls

- Local search and ranking do not require a provider or network connection.
- Provider credentials are isolated in Windows Credential Manager, with a
  current-user DPAPI fallback for Plex when required.
- The per-user SQLite database and its journal pages use SQLite3 Multiple
  Ciphers with explicit ChaCha20-Poly1305 selection and a zero-byte plaintext
  header. A random 256-bit key is protected with current-user DPAPI.
- Database connections are private and non-pooled, keys are applied through the
  native binary key API before schema access, and mutable key buffers are zeroed
  where practical.
- Valid version-1 plaintext databases migrate through an independently verified
  encrypted replacement. Missing/wrong keys and unknown schemas fail closed
  without silently replacing personal data.
- Plex tokens are sent only in the `X-Plex-Token` header; redirects are disabled.
- Insecure Plex HTTP is explicit and restricted to private, link-local, or
  loopback IP address literals. HTTP hostnames and public destinations are
  blocked.
- TMDB authorization is sent only to `api.themoviedb.org`; image requests to
  `image.tmdb.org` do not contain the token.
- Plex XML and TMDB JSON/image responses are bounded and validated.
- Local error reports are bounded and redacted.
- Builds treat compiler/analyzer warnings as errors and run transitive NuGet
  vulnerability auditing, unit tests, integration tests, and security tests.
- CI actions are pinned to immutable commit SHAs and checkout credentials are
  not persisted.
- GitHub secret scanning and push protection are enabled. Dependabot
  vulnerability alerts and automated security updates are enabled.
- The protected `main` branch requires the quality gate and CodeQL analysis;
  force-pushes and branch deletion are disabled.
- The repository and release payload contain no bundled watch-history database.

## Known limitations

- Database encryption does not protect against malware running as the same
  Windows user, a compromised signed-in session, an administrator, a
  compromised operating system, or process-memory inspection.
- The feedback JSON projection, poster caches, and local diagnostic files are
  separate per-user artifacts and are not encrypted by the database cipher.
- Loss of the DPAPI-protected database-key file or its Windows profile makes the
  encrypted database unrecoverable; there is no recovery key or back door.
- The application does not yet provide a complete in-app purge workflow; see
  `PRIVACY.md` for manual deletion steps.
- Development executables/installers are unsigned. SHA-256 checksums provide
  integrity evidence only, not publisher identity.
- There is no automatic updater, sandbox, anti-tamper system, or defense against
  a compromised Windows account or operating system.

See `docs/quality/THREAT_MODEL.md` for the detailed engineering threat model and
`PRIVACY.md` for local data and provider boundaries.
