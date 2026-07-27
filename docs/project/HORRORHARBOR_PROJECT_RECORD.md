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
- Per-user SQLite persistence and atomic feedback storage.
- Credential isolation through Windows credential abstractions.
- Bounded Plex and TMDB clients with redirect restrictions and parser limits.
- Private-IP-literal enforcement for explicitly enabled Plex HTTP.
- Generated artwork fallback and explicit official-artwork provenance.
- Analyzer warnings as errors, layered tests, static validation, performance
  checks, and Windows publish automation.

## Public-release remediation status

The repository remains private while release remediation and verification are
in progress. Historical personal-history inputs, derived artifacts, export
tools, hashes, statistics, and runtime dependencies have been removed from the
candidate source tree.

No public binary is approved. Release promotion remains blocked until the full
clean restore, format, build, test, static/privacy scan, performance, publish,
payload-inventory, dependency, workflow, and repository-history gates pass.
Unsigned installers remain development artifacts even after source publication.

## Provider representation

HorrorHarbor is an independent community project and is not reviewed, approved,
or endorsed by Plex or TMDB. Provider credentials are supplied by each user and
remain subject to current provider terms. See `THIRD_PARTY_NOTICES.md`.

## Evidence policy

Verification results are recorded only after execution against the exact
candidate revision. Generated evidence must not be committed if it contains
local paths, credentials, user history, caches, or other private state. Stale
counts and manifests are not release evidence.
