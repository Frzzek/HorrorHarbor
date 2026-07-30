# HorrorHarbor Privacy Notice

Last updated: 2026-07-28

HorrorHarbor is a local-first Windows desktop application. It does not include
telemetry, advertising identifiers, cloud recommendation profiles, remote crash
reporting, or a HorrorHarbor-operated account service.

## Data stored locally

The application may store the following under the current Windows profile:

- `%LOCALAPPDATA%\HorrorHarbor\user-data.db`: Plex server metadata, imported
  watch-history fields needed for matching and recommendations, feedback-event
  history, and settings. Database pages and SQLite journal pages are encrypted.
- `%LOCALAPPDATA%\HorrorHarbor\credentials\user-data-key-v1.dpapi`: the random
  database key protected by Windows Data Protection API for the current user.
- `%LOCALAPPDATA%\HorrorHarbor\recommendation_feedback.json`: the current
  per-title feedback projection. This separate JSON projection is not encrypted.
- `%LOCALAPPDATA%\HorrorHarbor\logs`: bounded, redacted local error reports.
- `%LOCALAPPDATA%\NocturneHarbor\posters-v1`: generated poster cache.
- `%LOCALAPPDATA%\NocturneHarbor\official-posters-v1`: official TMDB poster
  cache when the user explicitly enables TMDB.

The SQLite user database uses SQLite3 Multiple Ciphers with an explicitly
selected ChaCha20-Poly1305 cipher and no plaintext SQLite header. A random
256-bit key is created per Windows user and protected with current-user DPAPI;
the key is not stored in the database, configuration, command line, or a SQL
statement. Existing valid version-1 plaintext HorrorHarbor databases are
validated and migrated to an encrypted replacement during initialization.
Unknown or corrupt schemas, a missing key for an encrypted database, and a
wrong key fail closed rather than silently resetting personal data.

This protects copied database and journal files against casual offline reading.
It does not protect data from malware running as the same Windows user, a
compromised signed-in session, an administrator, a compromised operating
system, screen capture, or process-memory inspection. The separate feedback
JSON, poster caches, and local diagnostics are ordinary per-user files and are
not covered by database encryption. Do not use Plex history sync on a Windows
profile shared with people who should not see that viewing history.

The DPAPI key file is required to reopen the database. Deleting it, losing the
Windows profile, or making the protected key unreadable makes the encrypted
database intentionally unrecoverable; HorrorHarbor does not contain a recovery
key or back door.

This public repository and its release payload do not include any developer or
user watch history, Plex GUIDs, private provider caches, credentials, or local
error logs.

## Credentials

- Plex credentials use Windows Credential Manager target
  `HorrorHarbor/Plex/primary`. If Credential Manager is unavailable, the app
  uses a current-user DPAPI-protected local fallback.
- The TMDB credential uses Windows Credential Manager target
  `HorrorHarbor/TMDB/primary`.
- The database encryption key is separate from provider credentials. It is
  random binary key material stored only in the current-user DPAPI-protected
  key file described above.
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
