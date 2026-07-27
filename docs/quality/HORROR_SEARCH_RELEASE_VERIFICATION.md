# Horror Search Release Verification

## Product boundary

Nocturne Harbor now routes catalog search through `HorrorHarbor.Search` rather
than the former title/tag substring filter. The ordinary search path is local,
deterministic, and does not invoke Plex, TMDB, HTTP, credentials, or provider
clients. Online services remain explicit, separately labeled user actions.

## Application integration

- The curated catalog is validated and published once as an immutable search
  snapshot during application initialization.
- User feedback is projected into typed local watched/unseen status.
- Queries support quoted phrases, exclusions, typed fields, numeric ranges and
  comparisons, status facets, and verified-availability facets.
- Rejected input is shown as a stable diagnostic code with a source position.
- Ranking occurs before truncation and remains stable across repeated runs and
  cultures.
- Search results outside the recommendation window can still surface from the
  complete curated catalog and receive local generated or official artwork.

## Persistence and undo

Feedback writes are coordinated across JSON current state and SQLite event
history. UI state is published only after persistence succeeds. Failed
cross-store writes are compensated, undo checks persisted history truth, and a
failed undo current-state write restores the SQLite event.

## Release gates

The release is accepted only when all of the following pass:

1. Static architectural and corpus validation.
2. NuGet vulnerability audit and security tests.
3. Formatting verification and analyzer-clean Release build.
4. Unit and integration tests, including catalog-search and feedback
   coordination tests.
5. Poster evidence generation.
6. Recommendation, normalization, and 50,000-document search performance
   budgets.
7. Self-contained `win-x64` publication and portable package hashing.

Unsigned artifacts are for local/internal use. Public distribution requires an
Authenticode signing certificate and successful signature verification.
