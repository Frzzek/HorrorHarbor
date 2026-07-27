# ADR-0001: Keep catalog and user-history identities separate

**Status:** Superseded by the public-release privacy design
**Date:** 2026-07-24

The distributable catalog uses stable application-owned candidate identifiers and
title/year watch-exclusion keys. It does not contain a developer's Plex item,
show, account, server, or history identifiers.

Plex identifiers imported by a user are private per-user data. Matching occurs
locally and conservatively against the shipped catalog. Provider identifiers
must never become public catalog identity merely because they were observed in
one user's history.

Consequences:

- Remakes and same-title editions remain distinct.
- Catalog identity is deterministic without shipping personal history.
- Plex history can be deleted without changing the shared catalog.
- Ambiguous imported items do not silently train or exclude a catalog title.
- Provider identifiers remain local user data and are excluded from releases.
