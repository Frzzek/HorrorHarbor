# Poster Build P1 — identity, provenance, and official artwork integration

**Implementation status:** Complete for the TMDB-to-generated-fallback source path  
**Analyzer and automated quality gate:** PASS  
**Offline/fallback verification:** PASS  
**Mocked TMDB provider verification:** PASS  
**Live TMDB provider verification:** BLOCKED — no `HorrorHarbor/TMDB/primary` credential is configured  
**Fresh self-contained WPF publish:** PASS  
**Installer rebuild and installed-app acceptance:** BLOCKED until live official artwork is visibly verified  

## Delivered scope

Build P1 adds stable optional artwork identity (`OriginalTitle`, `OriginalLanguage`, and `TmdbId`) to the recommendation domain and forwards it into the poster subsystem. Existing catalog entries remain compatible and use conservative title/year matching when no explicit TMDB ID exists. No catalog TMDB IDs were guessed or inserted.

The implemented source order is:

1. TMDB official artwork when a secure credential exists and identity is explicit or unambiguous.
2. Deterministic locally generated fallback.

User-selected artwork and Plex artwork are not implemented in this build and remain Build P2 work.

## Identity and ambiguity policy

- A positive catalog TMDB ID uses the direct `/movie/{id}` endpoint and receives confidence `1.0` only when the response ID matches and poster artwork exists.
- Search matching normalizes title text, requires exact normalized title or original-title agreement, uses exact or adjacent release-year evidence, and optionally uses original-language evidence.
- Automatic search matches require confidence of at least `0.85`.
- The winning result must exceed the runner-up by at least `0.10`.
- Missing posters, malformed results, null result entries, weak matches, and ambiguous matches are rejected and use generated fallback through the hybrid service.

## Provenance contract

Every accepted poster asset records:

- source kind;
- provider;
- provider asset path;
- provider media ID;
- match method and confidence;
- provider original title and language;
- attribution text;
- terms URI;
- retrieval timestamp; and
- content SHA-256.

Generated fallback assets carry local-renderer provenance and content SHA-256. The audit verifies that the provenance hash matches the accepted image bytes.

## Security boundaries

- The TMDB read-access token is stored under the Windows Credential Manager target `HorrorHarbor/TMDB/primary`.
- Tokens are not written to catalogs, cache metadata, URLs, manifests, logs, or poster provenance.
- Bearer authorization is sent only to `api.themoviedb.org`; it is not sent to `image.tmdb.org`.
- Destinations must use HTTPS, port 443, no URI user information, and the explicit TMDB API/image host allowlist.
- Redirect responses are rejected. The application HTTP handler also disables automatic redirects.
- Search and image payloads are bounded by declared and streamed byte limits.
- Media type, PNG/JPEG structure, encoded size, width, height, and pixel area are validated before cache acceptance.
- Caller cancellation is preserved. Provider timeouts and network failures use generated fallback.
- Cache writes are atomic; expired, malformed, corrupt, or hash-mismatched cache entries are removed and fetched again.
- Concurrent requests for the same identity are deduplicated.

## User experience

The WPF sidebar contains an official-poster setup panel. It stores or deletes the TMDB token through Windows Credential Manager and reports whether official artwork is enabled. Recommendation cards visibly distinguish:

- `OFFICIAL ARTWORK · TMDB`; and
- `GENERATED FALLBACK`.

Startup now reads the credential state so the sidebar does not incorrectly report an already-configured provider as disabled.

## Verification evidence

The accepted Release gate completed on 2026-07-26 with:

| Gate | Result |
|---|---:|
| Static validation | 170 / 170 PASS |
| Security scan | PASS |
| Release build | 12 projects, 0 warnings, 0 errors |
| Unit tests | 23 PASS |
| Unit line coverage | 83.41% total |
| Poster subsystem line coverage | 85.56% |
| Integration tests | 14 PASS |
| Security tests | 12 PASS |
| Self-test | PASS |
| Performance test | PASS; 265.903 ms / 8,000 ms budget |
| Published-app smoke test | PASS; process remained alive through eight-second initialization |

The mocked provider suite covers:

- exact and adjacent-year matching;
- ambiguity rejection;
- explicit-ID lookup;
- API-only bearer authorization;
- image retrieval and validation;
- provenance;
- cache reuse and corruption repair;
- concurrent request deduplication;
- missing credentials;
- redirects;
- oversized responses;
- wrong media types;
- corrupt images;
- null provider results;
- network failure;
- provider timeout; and
- caller cancellation.

## Offline audit result

The complete 116-title audit at `artifacts/poster-audit/p0` now emits P1-compatible source/provenance fields. The accepted offline run reports:

| Measure | Result |
|---|---:|
| Titles attempted | 116 |
| Official TMDB artwork | 0 |
| Generated fallback | 116 |
| Complete provenance | 116 |
| Failures | 0 |
| Evaluation corpus | 100 |
| Contact sheets | 6 |

This is intentionally honest evidence of offline behavior. It does not prove live TMDB retrieval or production poster quality.

## Publish boundary

The fresh self-contained ReadyToRun `win-x64` application is at `artifacts/publish/win-x64/HorrorHarbor.exe`. Its timestamp advanced during this build, and it passed an eight-second launch smoke test.

The installer was not rebuilt. A new installer must not be presented until a real TMDB credential is configured, multiple recognizable official posters are visibly verified in the running application, fallback labels are confirmed on rejected or unavailable matches, and the installed application repeats that result.

## TMDB attribution and terms boundary

The UI and provenance contain the required product notice: "This product uses the TMDB API but is not endorsed or certified by TMDB." Provenance references `https://www.themoviedb.org/api-terms-of-use`.

Provider operation still requires the user to obtain and configure a valid TMDB read-access token and remain responsible for current TMDB API terms. No live-provider acceptance claim is made by this build.

## Remaining Build P2 work

1. Configure a real TMDB read-access token and execute live-provider visual acceptance.
2. Curate stable TMDB IDs, original titles, and original languages for ambiguous catalog entries using verified provider data rather than guesses.
3. Add user-selected artwork as the highest-priority source.
4. Add securely matched Plex artwork ahead of TMDB.
5. Add bounded global cache cleanup and provider revalidation policy.
6. Improve generated fallback art beyond the current atmospheric placeholder renderer.
7. Rebuild, install, and verify the installer only after live official artwork acceptance passes.
