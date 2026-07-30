# HorrorHarbor Project Record

## Current public-source boundary

HorrorHarbor is a local-first Windows horror discovery application. The shared
source and release design contain a curated recommendation catalog but no
developer or user watch history, Plex GUID collection, credential, provider
cache, or local diagnostic data.

New users start with an empty personal profile. Optional Plex imports and
explicit feedback personalize recommendations only for the current Windows
profile. Optional TMDB artwork requires a user-supplied authorized credential
and remains local cache data.

## Implemented engineering controls

- Deterministic local search and explainable recommendation ranking.
- Strict catalog parsing and identity validation.
- Per-user full-page encrypted SQLite persistence using reviewed SQLite3MC
  ChaCha20-Poly1305 with a zero-byte plaintext header.
- A random 256-bit database key protected for the current Windows user through
  DPAPI; keys are never placed in SQLite connection strings.
- Private, non-pooled database connections, atomic supported plaintext-v1
  migration, and fail-closed wrong-key, missing-key, malformed-file, and
  unknown-schema behavior.
- Credential isolation through Windows credential abstractions.
- Bounded Plex and TMDB clients with redirect restrictions and parser limits.
- Private-IP-literal enforcement for explicitly enabled Plex HTTP.
- Generated artwork fallback and explicit official-artwork provenance.
- Analyzer warnings as errors, layered tests, static validation, performance
  checks, Windows publish automation, provider inventory enforcement, and a
  disposable exact-payload encrypted-storage runtime probe.

## Public-source status

The public repository was created as a fresh GitHub object from an audited,
privacy-safe parentless root. Historical personal-history inputs, derived
artifacts, export tools, hashes, statistics, and obsolete runtime dependencies
are not part of the public repository or any of its current refs.

The exact published source revision passed clean restore, formatting, compiler
and analyzer checks, static/privacy validation, dependency auditing, layered
tests, performance budgets, Windows publication, payload inventory, and CodeQL.
GitHub secret scanning reports no alerts, and `main` is protected by the quality
and CodeQL checks.

Encrypted-storage work is promoted only through that protected pull-request
path. Local candidate evidence, including synthetic migration and DPAPI runtime
results, does not become public-release evidence until the candidate revision
passes the required GitHub quality and CodeQL checks and is merged to `main`.

Source publication is accepted. No public binary is approved: unsigned
executables and installers remain development artifacts and must not be
represented as publisher-verified releases.

## Provider representation

HorrorHarbor is an independent community project and is not reviewed, approved,
or endorsed by Plex or TMDB. Provider credentials are supplied by each user and
remain subject to current provider terms. See `THIRD_PARTY_NOTICES.md`.

## Evidence policy

Verification results are recorded only after execution against the exact
candidate revision. Generated evidence must not be committed if it contains
local paths, credentials, user history, caches, or other private state. Stale
counts and manifests are not release evidence. Storage runtime verification
uses only disposable synthetic database and key paths under ignored validation
artifacts and never opens the default per-user HorrorHarbor storage location.
