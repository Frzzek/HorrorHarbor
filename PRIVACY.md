# HorrorHarbor Privacy Notice

Last updated: 2026-07-27

HorrorHarbor is a local-first Windows desktop application. It does not include
telemetry, advertising identifiers, cloud recommendation profiles, remote crash
reporting, or a HorrorHarbor-operated account service.

## Data stored locally

The application may store the following under the current Windows profile:

- `%LOCALAPPDATA%\HorrorHarbor\user-data.db`: Plex server metadata, imported
  watch-history fields needed for matching and recommendations, feedback-event
  history, and settings.
- `%LOCALAPPDATA%\HorrorHarbor\recommendation_feedback.json`: the current
  per-title feedback projection.
- `%LOCALAPPDATA%\HorrorHarbor\logs`: bounded, redacted local error reports.
- `%LOCALAPPDATA%\NocturneHarbor\posters-v1`: generated poster cache.
- `%LOCALAPPDATA%\NocturneHarbor\official-posters-v1`: official TMDB poster
  cache when the user explicitly enables TMDB.

The SQLite user database is not encrypted. Windows profile permissions provide
the primary local boundary; another process running as the same Windows user
can read it. Do not use Plex history sync on a Windows profile shared with
people who should not see that viewing history.

This public repository and its release payload do not include any developer or
user watch history, Plex GUIDs, private provider caches, credentials, or local
error logs.

## Credentials

- Plex credentials use Windows Credential Manager target
  `HorrorHarbor/Plex/primary`. If Credential Manager is unavailable, the app
  uses a current-user DPAPI-protected local fallback.
- The TMDB credential uses Windows Credential Manager target
  `HorrorHarbor/TMDB/primary`.
- Credentials are not intentionally written to catalog files, logs, image
  URLs, manifests, reports, command lines, or release packages.

## Network activity

Ordinary catalog search, recommendation ranking, feedback, and generated
artwork are local operations.

Network access occurs only after an explicit user action:

- **Plex:** the app contacts the server URL entered by the user, sends the Plex
  token in the `X-Plex-Token` request header, reads server identity and history,
  and stores the selected history fields locally. HTTPS is the default. Plain
  HTTP requires explicit opt-in and a private, link-local, or loopback IP
  address literal; hostnames and public destinations are blocked for HTTP.
- **TMDB:** after explicit acknowledgement and credential entry, the app sends
  film identity queries and a bearer token to `api.themoviedb.org`; image bytes
  are downloaded from `image.tmdb.org` without the token. Plex history,
  feedback, taste weights, and local database values are not sent to TMDB.

Provider services apply their own privacy policies and terms to data sent to
them. Users are responsible for using credentials they are authorized to use.

## Deleting local data

HorrorHarbor does not yet provide a complete in-app purge control. To remove all
local data:

1. Close HorrorHarbor.
2. Delete `%LOCALAPPDATA%\HorrorHarbor`.
3. Delete `%LOCALAPPDATA%\NocturneHarbor\posters-v1` and
   `%LOCALAPPDATA%\NocturneHarbor\official-posters-v1` if desired.
4. Open Windows Credential Manager and remove
   `HorrorHarbor/Plex/primary` and `HorrorHarbor/TMDB/primary`.

Uninstalling the application may preserve these per-user files and credentials
so a later reinstall is non-destructive. Perform the steps above when a full
purge is required.

## Scope

This notice describes the source in this repository. A modified third-party
build may behave differently. Verify the source revision and build provenance
before running binaries from another distributor.
