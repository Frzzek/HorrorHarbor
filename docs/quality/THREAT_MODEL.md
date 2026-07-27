# HorrorHarbor Threat Model

## Status and scope

This document is the security baseline for the Horror Search Engine overhaul. It covers the Windows desktop application, the local search and recommendation engines, the curated catalog, Plex synchronization, TMDB integration, local persistence, caches, logs, build automation, and published artifacts.

The product remains **local-first**. Online database access is permitted only through reviewed, optional provider adapters. An online provider failure must never prevent local catalog search or generated poster fallback.

This model distinguishes:

- **Existing** controls that are implemented and tested.
- **Required** controls that must pass before the overhauled engine is releasable.
- **Residual** risks that cannot be eliminated without changing the product or operating-system trust model.

## Security objectives

1. Keep Plex and TMDB credentials confidential.
2. Keep watch history, feedback, taste signals, and search activity local unless a user explicitly invokes a disclosed online operation.
3. Preserve catalog, index, ranking, explanation, and cache integrity.
4. Keep search responsive under malformed or intentionally expensive input.
5. Produce deterministic local results from the same catalog, profile, and query.
6. Fail safely without leaking credentials, private paths, raw private data, or provider payloads.
7. Preserve useful offline behavior during network, provider, credential, cache, or enrichment failures.
8. Publish only artifacts produced by repeatable, audited, analyzer-clean, and test-gated builds.

## Protected assets and classification

| Asset | Classification | Storage or transit | Required handling |
|---|---|---|---|
| Plex token | Secret | Windows Credential Manager; DPAPI fallback | Never place in a URL, log, cache, report, or source file |
| TMDB read-access token | Secret | Windows Credential Manager | Provider-scoped authorization header only; never forward to image hosts |
| Plex watch history | Private | Local SQLite user database | Never transmit to metadata providers; bound retention and import volume |
| Feedback and taste profile | Private | Local user state and memory | Never transmit to providers; update transactionally |
| Search query | Potentially private | Memory by default | Do not persist or transmit unless an explicit online-search action discloses the provider |
| Curated catalog | Integrity-sensitive | Published JSON | Strict schema, size, range, identity, and duplicate validation |
| Online metadata | Untrusted | HTTPS response and bounded local cache | Validate, attribute, timestamp, and keep separate provenance |
| Search index | Integrity-sensitive | Immutable in-process snapshot | Build only from validated records; atomically replace complete snapshots |
| Ranking and explanations | Integrity-sensitive | Memory and UI | Deterministic scoring, stable ties, finite numbers, component-level explanations |
| Poster and metadata caches | Untrusted local state | Local application-data directory | Versioned paths, bounded files, integrity checks, atomic writes, safe invalidation |
| Error logs | Sensitive operational data | Local application-data directory | Structured, bounded, rotated, and redacted |
| Release artifacts | Integrity-sensitive | Build and distribution directories | Audited build, manifest, checksums, and eventual code signing |

## Trust boundaries

### 1. User interface to query engine

Text entered into search controls is untrusted. It crosses into a parser, normalizer, filter planner, index, scorer, and explanation builder. It must never be interpreted as SQL, a regular expression, XAML, a file path, a shell command, or a provider URL.

### 2. Published catalog to application

The candidate catalog is structured but untrusted input. Existing controls reject oversized files, malformed JSON, comments, trailing commas, unknown fields, invalid ranges, unsafe text, duplicate candidate IDs, and duplicate title/year identities. The search index must consume only records that passed this validation.

### 3. Canonical database to application

The bundled canonical database is opened read-only and query-only after verification. Database reads remain a trust boundary because an installed file can be replaced or corrupted. Read values require bounded mapping and safe failure behavior.

### 4. Mutable local state to application

The user database, feedback state, settings, caches, and logs can be corrupted or modified by another process running as the same Windows user. They must be treated as untrusted on every load. Quarantine or rebuild is preferred over partially trusting malformed state.

### 5. Credential stores to network clients

Credential Manager and the DPAPI fallback protect persisted secrets. Loaded secrets become managed strings and therefore cannot be reliably zeroed. Provider services must minimize their lifetime, avoid caching them, scope them to one request authority, and prevent them from entering exception messages.

### 6. Application to Plex

Plex may be a private server selected by the user. HTTPS is the default. Explicitly enabled HTTP is restricted to private, link-local, or loopback destinations. Tokens are sent in a request header. XML is size-bounded and parsed with DTD processing prohibited and no resolver.

### 7. Application to online metadata providers

Provider data is remote and untrusted. Every provider requires a dedicated client policy, fixed HTTPS authority allowlist, redirect rejection, response timeout, cancellation, byte limit, media-type validation, strict payload validation, bounded retries, and explicit provenance. Provider results enrich but do not replace authoritative offline results.

### 8. Provider response to local cache and index

Downloaded metadata and images are untrusted until validated. Cache writes must be atomic. Cached records require a schema version, provider identity, retrieval time, source identifier, and content integrity field. Invalid, stale, partial, or mismatched entries are discarded without suppressing local search.

### 9. Exception to diagnostic log

Exceptions can contain URLs, headers, paths, queries, provider payload fragments, or database content. Only an allowlisted structured subset may be logged after redaction. UI messages expose a local correlation reference, not raw exception details.

### 10. Source and dependencies to release artifact

NuGet packages, GitHub Actions, scripts, generated files, and build machines are supply-chain boundaries. Central package versions, transitive vulnerability auditing, Dependabot, analyzers, tests, CodeQL, secret scanning, reproducible settings, artifact manifests, and checksums reduce this risk.

## Threat register and required controls

### Query and search engine

| ID | Threat | Impact | Required controls | Verification |
|---|---|---|---|---|
| QRY-01 | Extremely long or complex input exhausts CPU or memory | UI denial of service | Bound raw query length, token count, phrase count, filter count, range count, fuzzy terms, and result count; no recursive grammar | Boundary, fuzz, cancellation, and performance tests |
| QRY-02 | Catastrophic regular-expression behavior | UI denial of service | Do not compile user input as a regular expression; use deterministic token and edit-distance algorithms with explicit limits | Static review and adversarial repeated-character tests |
| QRY-03 | SQL, command, path, URL, or XAML injection | Data or system compromise | Query grammar produces typed in-memory predicates only; never concatenate query input into SQL, commands, paths, markup, or provider authorities | Injection corpus and static review |
| QRY-04 | Control characters, invalid Unicode, or invisible formatting corrupt parsing or display | Incorrect results, spoofing, log corruption | Reject controls and invalid UTF-16; reject every Unicode `Format` character except U+200C ZERO WIDTH NON-JOINER and U+200D ZERO WIDTH JOINER; preserve script-significant non-Latin marks and permitted join controls; report bounded UTF-16 spans; normalize only a matching copy; preserve validated display text; use invariant comparisons | Unicode, malformed-input, join-control, bidi-control, zero-width, and UTF-16-span tests |
| QRY-05 | Unbounded typo matching multiplies work | CPU denial of service | Apply fuzzy matching only to bounded tokens and a bounded candidate set; cap edit distance; short tokens require exact or prefix matching | Worst-case typo benchmark |
| QRY-06 | Older asynchronous work overwrites newer results | Integrity and UX failure | Cancel superseded work and use monotonically increasing request versions; publish only the latest completed snapshot | Deterministic stale-result race test |
| QRY-07 | Cancellation is ignored | UI denial of service | Accept and check cancellation at parse, retrieval, scoring, facet, and online boundaries | Pre-cancel and mid-query tests |
| QRY-08 | Explanations expose raw unsafe input or misrepresent ranking | Privacy and trust failure | Build explanations from typed score contributions and validated metadata; do not echo raw query text unnecessarily | Explanation snapshot and injection tests |
| QRY-09 | Personalized recommendation score hides an exact title | Incorrect retrieval | Direct search inspects the full eligible catalog; exact normalized identity has lexical priority; recommendation score is a separately labeled contribution | Acceptance corpus and top-rank assertions |
| QRY-10 | Unsupported metadata facets imply false precision | Misleading results | Report unavailable or incomplete fields explicitly; never infer language, franchise, or availability from missing data | Missing-metadata acceptance tests |

### Catalog, index, and ranking integrity

| ID | Threat | Impact | Required controls | Verification |
|---|---|---|---|---|
| CAT-01 | Malformed, oversized, or unexpected catalog content | Crash or memory exhaustion | Preserve strict JSON options, file-size bounds, record bounds, text bounds, and range validation | Existing malicious fixtures plus record-count tests |
| CAT-02 | Duplicate or confusable identities split ranking state | Incorrect exclusions and feedback | Canonical identity policy, duplicate ID rejection, duplicate title/year rejection, and explicit alias provenance | Identity collision corpus |
| CAT-03 | Mutable source records change beneath an active query | Nondeterministic results | Build immutable index snapshots and atomically swap only complete snapshots | Concurrent rebuild/query test |
| CAT-04 | NaN, infinity, overflow, or invalid weights corrupt ordering | Nondeterministic ranking | Validate every score input, use checked arithmetic where appropriate, reject non-finite values, and validate normalized totals | Numeric edge tests |
| CAT-05 | Unstable collection enumeration changes ties | Nondeterministic ranking | Explicit ordinal/invariant comparers and complete stable tie-break keys | Repeated and cross-culture determinism tests |
| CAT-06 | Online metadata silently overwrites curated facts | Integrity failure | Keep source fields and provenance explicit; merge through field-level policy; never downgrade trusted curated values with lower-confidence data | Merge-policy tests |

### Network and online providers

| ID | Threat | Impact | Required controls | Verification |
|---|---|---|---|---|
| NET-01 | Token appears in URL, redirect, log, or wrong host | Credential disclosure | Authorization headers only, dedicated provider clients, redirect disabled, fixed authority allowlist, redaction, and tests asserting host/header separation | Capturing-handler security tests |
| NET-02 | User-controlled or provider-controlled URL enables SSRF | Internal network access | Fixed provider roots; reject user info, fragments, non-HTTPS schemes, unexpected ports and hosts; validate provider paths before combining | Destination allowlist tests |
| NET-03 | Plex DNS preflight differs from socket destination | SSRF through DNS rebinding | Keep per-request validation; required hardening is a connect-time address policy or explicit documented residual risk for insecure HTTP | Resolver and connection-policy tests |
| NET-04 | Redirect forwards secrets or pivots authority | Credential disclosure or SSRF | Disable automatic redirects and reject all 3xx responses at the adapter boundary | Redirect tests |
| NET-05 | Response body, decompression, JSON depth, or result count is excessive | Memory or CPU denial of service | Header and streaming byte limits, bounded decompression, strict JSON depth, bounded arrays/pages, and cancellation | Oversized/chunked/deep payload tests |
| NET-06 | Retry storm or provider throttling blocks the UI | Availability failure | Small bounded retry count, capped server delay, jitter only where determinism is irrelevant, total operation deadline, and no UI-thread blocking | Retry-budget tests |
| NET-07 | Free-form query, watch history, or taste profile is transmitted unexpectedly | Privacy breach | Never transmit watch history or taste profile; online search must be an explicit, disclosed operation; enrichment should prefer stable film identity | Request-content tests and UI disclosure review |
| NET-08 | Provider outage suppresses local results | Availability failure | Local search completes independently; online results are additive; stale validated cache may be used with visible age/provenance | Offline and timeout tests |
| NET-09 | Provider returns malicious text or identity mismatch | Spoofing or poisoned metadata | Text/range/control validation, identity confidence policy, no markup rendering, and provenance display | Malicious payload and ambiguity tests |
| NET-10 | Adult or non-horror content is imported unintentionally | Product integrity failure | Provider filters plus local horror-classification and confidence policy; online content is not promoted to curated status automatically | Provider acceptance corpus |

### Persistence, credentials, caches, and logs

| ID | Threat | Impact | Required controls | Verification |
|---|---|---|---|---|
| PST-01 | Feedback current state and event history diverge after partial writes | Incorrect personalization and undo | Consolidate feedback state and event history behind one transactional persistence service; migrate existing JSON safely | Fault-injection integration tests |
| PST-02 | Corrupt or unsupported user-database schema is trusted | Crash or silent data loss | Enforce schema version compatibility, transactional migrations, integrity check, validated reads, and recoverable backup/quarantine policy | Migration and corruption tests |
| PST-03 | Watch-history import or retention grows without bound | Disk or memory denial of service | Per-sync cap, explicit truncation status, retention policy, indexes, and paged reads | Large-history integration test |
| PST-04 | Same-user process reads plaintext watch history | Privacy breach | Store only needed fields, document local-account boundary, provide deletion/retention controls, and consider optional DPAPI-protected export or database encryption separately | Privacy review; residual risk documented |
| PST-05 | Cache path split or unsafe migration loses or mixes state | Integrity and disk-sprawl failure | One versioned application-data root, fixed application-owned subpaths, safe one-time migration, and no user-controlled production cache root | Path and migration tests |
| PST-06 | Partial or tampered cache entry is consumed | Poisoned UI or metadata | Atomic writes, schema/provenance validation, size limits, integrity hash, stale policy, and rebuild-on-failure | Corrupt-cache tests |
| PST-07 | Temporary files accumulate after cancellation or crash | Disk exhaustion | Unique temporary names, cleanup in finally blocks, and startup scavenging restricted to application-owned patterns | Cancellation and scavenging tests |
| CRED-01 | Persisted credential is readable from ordinary files | Credential disclosure | Credential Manager primary; DPAPI current-user fallback only where required; no plaintext fallback | Credential-store tests |
| CRED-02 | Secret remains in avoidable buffers | Credential disclosure | Zero mutable byte and unmanaged buffers; minimize unavoidable managed-string lifetime; do not retain tokens in view models | Code review and store tests |
| LOG-01 | Exception log contains token, API key, URL credentials, query parameters, raw query, path, or private provider payload | Privacy or credential disclosure | Allowlisted fields; redact bearer/token/key/password patterns, URLs, query strings, and paths; cap entry and file size; rotate locally | Redaction and rotation tests |
| LOG-02 | Log forging through control characters | Diagnostic integrity failure | Structured serialization, control-character removal, and single-record writes | Newline/control tests |

### Build, supply chain, and release

| ID | Threat | Impact | Required controls | Verification |
|---|---|---|---|---|
| SUP-01 | Vulnerable or compromised dependency | Code execution or data exposure | Minimize packages, central versions, transitive NuGet audit, Dependabot, lock/review policy, and CodeQL | CI restore and dependency report |
| SUP-02 | Secret is committed outside scanned directories or under an unrecognized name | Credential disclosure | Scan all authored text and release configuration with an allowlisted fixture policy; exclude generated binaries by type, not by broad directory omission | Seeded scanner tests |
| SUP-03 | Local and CI gates differ | Defective release | One authoritative gate invoked by CI and locally; mandatory prerequisites; no silent skipped critical checks | Gate parity test/review |
| SUP-04 | Runtime-specific publish uses an incompatible restore graph | Unreleasable artifact | Restore explicitly for `win-x64` before `--no-restore` publish | Clean-agent publish test |
| SUP-05 | Development or testing configuration ships and enables verbose logs | Privacy leak | Publish only production configuration unless a reviewed diagnostic mode is explicitly enabled | Publish-content assertion |
| SUP-06 | Artifact is modified after validation | Supply-chain tampering | Generate manifest and SHA-256 checksums after final publish; verify before distribution | Manifest verification |
| SUP-07 | Unsigned executable or installer cannot establish publisher identity | Spoofing | Code signing is required for a trusted public release; until then, document unsigned status and distribute checksums through a trusted channel | Signature verification gate when certificate exists |

## Existing controls confirmed by the audit

- The application runs as the invoking user and does not request elevation or UI-access privileges.
- The canonical database is configured read-only and verified before normal use.
- SQL commands use parameters.
- Catalog loading is strict, bounded, and rejects unknown fields and duplicate identities.
- Recommendation ranking has explicit deterministic tie-breaking.
- Plex tokens use headers rather than URLs.
- Plex XML parsing prohibits DTDs and external resolution and bounds response bytes.
- Plain HTTP Plex access requires explicit opt-in and a private/link-local/loopback preflight.
- TMDB uses fixed HTTPS hosts, bearer authorization only for API requests, no authorization on image requests, redirect rejection, byte/media/image validation, atomic writes, provenance, and integrity hashes.
- TMDB failure falls back to local generated artwork and preserves caller cancellation.
- Credential Manager stores Plex and TMDB credentials; the Plex fallback uses current-user DPAPI.
- Mutable credential buffers are zeroed where practical.
- CI runs audited restore, formatting, analyzers, unit/integration/security tests, self-test, performance checks, publish, and evidence upload.
- CodeQL and Dependabot are configured.

## Required release gates introduced by this model

1. Query grammar limits and a typed parser with no recursive or executable constructs.
2. Full-catalog direct search independent of recommendation truncation.
3. Immutable index snapshots, request cancellation, and stale-result suppression.
4. Query fuzz, Unicode, complexity, injection, determinism, and performance suites.
5. Dedicated HTTP policy per provider rather than one shared client.
6. Explicit online-search disclosure; no transmission of watch history or taste profiles.
7. Transactional feedback persistence and tested migration from the existing JSON projection.
8. Schema enforcement, retention bounds, and safer validation for mutable user data.
9. Comprehensive secret and private-data redaction with bounded log rotation.
10. Unified versioned local-storage root and safe cache migration.
11. Mandatory parity between local and CI quality gates.
12. RID-specific restore before Windows publish and production-only configuration packaging.
13. Artifact manifest and checksum verification after the final publish.

## Residual risks and explicit non-goals

- A process running as the same Windows user can read or modify plaintext local watch-history and feedback files. Protecting against a compromised user account or administrator is outside this release boundary.
- HTTPS provider traffic reveals destination and timing to the network and sends explicitly submitted online-search terms to the disclosed provider.
- Provider truth, availability data, and horror classification can be incomplete or stale. Provenance and confidence reduce but cannot eliminate this risk.
- Insecure HTTP Plex support retains DNS-rebinding risk until connect-time address enforcement is implemented; HTTPS remains the recommended path.
- The current development executable is unsigned. Checksums provide integrity evidence but not publisher identity.
- This application does not implement an automatic updater, DRM, anti-tamper system, sandbox, or protection against a compromised Windows installation.

## Incident-safe behavior

- Credential or provider failure: keep offline search and generated artwork available.
- Invalid query: return a bounded, actionable parse diagnostic without executing a partial unsafe interpretation.
- Corrupt cache: discard and rebuild; do not trust partial metadata.
- Corrupt mutable state: quarantine or restore a validated backup; never silently merge malformed records.
- Catalog verification failure: stop catalog indexing and present a correlation reference.
- Superseded search: cancel it and prevent stale publication.
- Unexpected exception: display a local reference and write only a redacted, bounded structured event.
- Failed release gate: do not publish or update release hashes.

## Review triggers

Re-review this model before adding a new provider, hosted search service, telemetry, cloud sync, automatic update channel, installer elevation, database encryption, or transmission of any local history/profile data.
