# ADR-0004: Secure Plex Sync and Mutable User Data

## Status

Accepted; revised for the public-release privacy boundary.

## Context

HorrorHarbor ships a curated, non-personal recommendation catalog. A new user
starts with an empty personal taste history. Optional Plex synchronization and
explicit feedback may personalize recommendations, but all resulting state is
private to the current Windows profile.

## Decision

1. No developer or user watch-history database is bundled with source, builds,
   installers, or release payloads.
2. Mutable state is stored in
   `%LOCALAPPDATA%\HorrorHarbor\user-data.db`; current feedback is also projected
   to `%LOCALAPPDATA%\HorrorHarbor\recommendation_feedback.json`.
3. The user supplies a Plex server URL and token. The token is isolated behind
   the credential-store abstraction and is never stored in SQLite, catalog data,
   logs, URLs, command lines, or release artifacts.
4. HTTPS is required by default. Plain HTTP requires explicit opt-in and a
   private, link-local, or loopback IP address literal. HTTP hostnames and public
   destinations are rejected.
5. Plex requests send the token only in the `X-Plex-Token` header. Redirects are
   disabled; timeouts, response sizes, retries, item counts, and XML behavior are
   bounded.
6. Imported history is written transactionally and deduplicated using local
   event identity.
7. Imported history affects watched exclusions and taste only when local catalog
   matching is sufficiently confident. Ambiguous history does not train or
   exclude silently.
8. Search, ranking, generated artwork, and previously imported personalization
   remain local and usable without a provider connection.
9. A complete in-app purge workflow is not yet implemented. Manual deletion is
   documented in `PRIVACY.md`; the application must not claim otherwise.

## Mutable database schema

- `schema_version`: transactional migration ledger.
- `plex_server`: non-secret server metadata, TLS policy, machine identifier, and sync status.
- `plex_history`: normalized imported viewing events and identity keys.
- `feedback_event`: append-only explicit recommendation feedback used for undo and learning.
- `user_setting`: persisted discovery controls and layout preferences.

## Security boundaries

- Error reporting must redact `X-Plex-Token`, query parameters named `token`, and token-like values.
- Redirects are not automatically followed across hosts.
- Response size and item counts are bounded.
- XML parsing disables DTD processing and external resolution.
- Invalid certificates are never bypassed.
- Another process running as the same Windows user can read the unencrypted
  user database; this limitation is disclosed rather than hidden.

## Delivery stages

1. User database, migrations, and secure credential storage.
2. Plex connection test and incremental history import.
3. Watched exclusion and deterministic feedback-learning integration.
4. Discovery filters, sorting, details, undo, async command safety, and accessibility.
5. CI, installer, clean-machine, upgrade, signing, and generated release evidence.

## Acceptance criteria

- A valid Plex URL/token can be tested without writing the plaintext token to
  user data or diagnostics.
- Re-running sync imports no duplicate logical events.
- Cancellation or a failed page does not partially commit that page.
- Imported history affects watched exclusions and matched taste signals offline.
- Explicit feedback deterministically changes similar unseen recommendations and can be undone.
- No token appears in logs, SQLite files, command lines, diagnostics, or release
  payloads.
- Release promotion requires restore, formatting, analyzer-clean Release build,
  tests, static/privacy validation, performance checks, and publish-content
  inspection.

