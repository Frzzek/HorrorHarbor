# Security Policy

## Supported status

HorrorHarbor is under active development. Source review and local builds are
supported; unsigned binaries and installers are development artifacts and must
not be represented as trusted, publisher-verified public releases.

## Reporting a vulnerability

Do not open a public issue containing credentials, watch history, private server
addresses, exploit details, or personal data. Use GitHub's private vulnerability
reporting feature when enabled for the repository. If that feature is not
available, provide only a minimal non-sensitive notice through the repository
owner's GitHub profile and wait for a private channel.

Never send a real Plex token, TMDB token, database, log directory, or provider
cache as a proof of concept. Use synthetic canary values and a minimal test case.

## Implemented controls

- Local search and ranking do not require a provider or network connection.
- Provider credentials are isolated in Windows Credential Manager, with a
  current-user DPAPI fallback for Plex when required.
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
- The repository and release payload contain no bundled watch-history database.

## Known limitations

- The local user database is not encrypted and is readable by other processes
  running as the same Windows user.
- The application does not yet provide a complete in-app purge workflow; see
  `PRIVACY.md` for manual deletion steps.
- Development executables/installers are unsigned. SHA-256 checksums provide
  integrity evidence only, not publisher identity.
- CodeQL source scanning is configured, but private-repository SARIF upload
  requires GitHub Code Security to be enabled and licensed for the repository.
  The workflow is not considered a passing release gate until that setting is
  confirmed and a trusted-branch run succeeds.
- There is no automatic updater, sandbox, anti-tamper system, or defense against
  a compromised Windows account or operating system.

See `docs/quality/THREAT_MODEL.md` for the detailed engineering threat model and
`PRIVACY.md` for local data and provider boundaries.
