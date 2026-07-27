# Horror Search Engine Architecture

## Status and authority

- Status: Accepted for implementation.
- Scope: Local horror search, filtered discovery, personalization integration, explicit online-search boundaries, application coordination, and verification ownership.
- Product contract: [Horror Search Engine Requirements](HORROR_SEARCH_ENGINE_REQUIREMENTS.md).
- Security contract: [Threat Model](THREAT_MODEL.md).
- Engineering contract: [Engineering Constitution](ENGINEERING_CONSTITUTION.md).
- Machine-readable acceptance contract: `tests/fixtures/search/horror-search-acceptance-v1.json`, schema version 1, corpus revision 3.

This document is an implementation decision, not an aspirational diagram. A change to a dependency direction, public contract, index consistency rule, ranking order, privacy boundary, or publication rule requires architecture review and corresponding tests.

## Decision summary

Create a new `net10.0` class library named `HorrorHarbor.Search`. It will:

1. Reference `HorrorHarbor.Core` only.
2. Contain no WPF, SQLite, HTTP, provider, credential, file-system, logging, or configuration dependency.
3. Accept validated search documents and immutable per-query user context.
4. Parse bounded query grammar into typed clauses without regular expressions or recursive syntax.
5. Build complete immutable index snapshots and atomically replace only successful snapshots.
6. Retrieve from the full validated local catalog before result truncation.
7. Apply hard facets and exclusions before scoring.
8. Produce deterministic lexical tiers, scores, evidence, diagnostics, warnings, and ordering.
9. Accept personalization as a precomputed finite affinity snapshot rather than calling the recommendation engine.
10. Return typed results; it will not format UI prose or contact online providers.

The WPF application will own search-session coordination, worker-thread execution, debounce, cancellation, monotonically increasing request versions, latest-request publication, and mapping into presentation state.

Personalized discovery remains a separate recommendation operation. Explicit online search remains a separate provider operation. Neither is a fallback hidden inside local search.

## Dependency direction

```text
HorrorHarbor.Core
       ↑
HorrorHarbor.Search
       ↑
HorrorHarbor.App

HorrorHarbor.Core
       ↑
HorrorHarbor.Recommendations
       ↑
HorrorHarbor.App

HorrorHarbor.Core
       ↑
HorrorHarbor.Data
       ↑
HorrorHarbor.App

HorrorHarbor.Metadata (future reviewed provider assembly)
       ↑
HorrorHarbor.App
```

Required prohibitions:

- `HorrorHarbor.Search` must not reference `HorrorHarbor.App`, `HorrorHarbor.Data`, `HorrorHarbor.Recommendations`, `HorrorHarbor.Posters`, or a Windows-specific target framework.
- `HorrorHarbor.Search` must not depend on `Microsoft.Extensions.*`; composition belongs to the application.
- `HorrorHarbor.Recommendations` must not reference `HorrorHarbor.Search` merely to supply affinity.
- Provider assemblies must not call into the local index or receive local user context.
- Local search must not know whether an online provider is configured.

The intended first implementation uses only .NET base-class-library facilities, including frozen collections where measurement supports them. No new search package is justified at the current 50,000-document target.

## Bounded contexts

### Core catalog domain

`RecommendationCandidate` remains the validated local film record and recommendation input. Its existing title normalizer remains unchanged because it contributes to persisted watch-exclusion identity. Search normalization must not alter existing history keys.

### Local search domain

`HorrorHarbor.Search` owns:

- Query syntax and diagnostics.
- Search-specific Unicode normalization and title variants.
- Search-document validation.
- Immutable index construction and publication.
- Candidate retrieval.
- Hard-facet and exclusion evaluation.
- Lexical evidence and scoring.
- Hybrid within-tier ranking.
- Stable ordering.
- Typed explanations and coverage warnings.

### Recommendation domain

`HorrorHarbor.Recommendations` owns:

- Taste-profile construction.
- Per-candidate recommendation affinity.
- Empty-query personalized discovery ranking.
- Recommendation-specific diversity.
- Recommendation explanations.

Its current `Rank` operation cannot be reused as a search-affinity source because it applies watched exclusions, count truncation, and diversity. A dedicated side-effect-free affinity operation will score every supplied candidate without exclusion, truncation, or diversity.

### Application domain

`HorrorHarbor.App` owns:

- Mapping catalog candidates into search documents.
- Resolving local title status from feedback and Plex state.
- Building immutable affinity and status snapshots.
- Selecting discovery, local search, or explicit online-search intent.
- Running CPU search away from the dispatcher.
- Canceling superseded work and suppressing stale publication.
- Converting typed results into localized, accessible presentation state.
- Keeping online results separate from local results.

### Online metadata domain

Online metadata and explicit provider search do not belong in `HorrorHarbor.Search`. A future reviewed `HorrorHarbor.Metadata` assembly will contain provider-specific adapters and transport policies. It will not receive search index snapshots, watch history, feedback, taste profiles, recommendation scores, or local paths.

## Source layout

The planned local-search project layout is:

```text
src/HorrorHarbor.Search/
  HorrorHarbor.Search.csproj
  Contracts/
    HorrorSearchDocument.cs
    SearchRequest.cs
    SearchResponse.cs
    SearchResult.cs
    SearchDiagnostic.cs
    SearchEvidence.cs
    SearchUserContext.cs
  Querying/
    SearchQueryScanner.cs
    SearchQueryBinder.cs
    ParsedSearchQuery.cs
    QueryClauses.cs
  Normalization/
    SearchTextNormalizerV1.cs
    TitleVariantBuilder.cs
    RomanNumeralAliasBuilder.cs
  Validation/
    SearchDocumentValidator.cs
    SearchRequestValidator.cs
  Indexing/
    SearchIndexBuilder.cs
    SearchIndexSnapshot.cs
    SearchIndexStore.cs
    PostingListBuilder.cs
    FacetIndexBuilder.cs
    FuzzyTermIndexBuilder.cs
  Retrieval/
    CandidateRetriever.cs
    PostingListOperations.cs
    BoundedEditDistance.cs
  Filtering/
    HardFacetEvaluator.cs
    ExclusionEvaluator.cs
    FacetCoverageAnalyzer.cs
  Ranking/
    LexicalEvidenceCollector.cs
    LexicalScorer.cs
    HybridRanker.cs
    StableSearchResultComparer.cs
  Explanations/
    SearchExplanationBuilder.cs
  Engine/
    HorrorSearchEngine.cs
```

Folders communicate ownership; they do not require an interface for every class. Interfaces are introduced only at real substitution or lifecycle boundaries. Stateless implementation details remain internal and are exercised through focused public components or the engine facade.

## Public contracts

Names below are normative unless implementation review discovers a compiler or analyzer conflict.

### Engine facade

```csharp
public interface IHorrorSearchEngine
{
    SearchResponse Execute(SearchRequest request, CancellationToken cancellationToken = default);
}
```

Local execution is intentionally synchronous and CPU-bound. This prevents a falsely asynchronous implementation from running substantial work on the WPF dispatcher before its first suspension. The application coordinator must invoke it on a worker thread.

`Execute` is thread-safe. It captures one index snapshot at entry and uses only that snapshot for the entire operation.

Low-level components use `CancellationToken.ThrowIfCancellationRequested` and may throw `OperationCanceledException` for the supplied token. The engine facade catches only cancellation associated with that token and returns `SearchCanceledResponse`; it does not catch unrelated failures as cancellation. This preserves normal internal .NET cancellation flow while satisfying the product contract that the query operation has a typed canceled outcome.

### Query parser

```csharp
public interface ISearchQueryParser
{
    SearchQueryParseResult Parse(string queryText, CancellationToken cancellationToken = default);
}
```

Expected user input errors are returned as typed diagnostics. They are not thrown as exceptions. Programmer-contract violations, such as a null API argument, may throw standard argument exceptions without embedding raw query text.

### Index lifecycle

```csharp
public interface ISearchIndexBuilder
{
    SearchIndexBuildResult Build(
        IEnumerable<HorrorSearchDocument> documents,
        CancellationToken cancellationToken = default);
}

public interface ISearchIndexStore
{
    SearchIndexSnapshot? Current { get; }
    void Publish(SearchIndexSnapshot snapshot);
}
```

`Build` is pure with respect to externally visible state. `Publish` atomically swaps a fully built snapshot. No API exposes partially built postings or mutates a published snapshot.

### Search document

`HorrorSearchDocument` owns a defensive snapshot of one validated `RecommendationCandidate` and defensively materializes search-only metadata:

- Validated aliases and alias provenance.
- Validated availability facts and provenance.
- Any provider-enriched original title or language approved for local indexing.
- A source revision used in the snapshot fingerprint.

Version 1 can construct a document from the current candidate fields and an explicit `unknown` availability state. Construction creates a new candidate record whose tag collection is a private array; neither the document nor a result retains the caller's candidate instance or collection references. Search-only additions never mutate the source candidate.

All collection inputs are copied into arrays using ordinal deterministic ordering where order has no domain meaning. Duplicate normalized aliases and tags are collapsed deterministically.

### User context

`SearchUserContext` is an immutable snapshot created before query execution. It contains only:

- Candidate ID to local status.
- Candidate ID to finite recommendation affinity in the inclusive range 0–100.

The factory defensively copies and validates both maps. Missing status means `unseen`. Missing affinity means zero. Search never retains mutable application dictionaries.

Local status precedence is deterministic when sources conflict:

1. `disliked`.
2. `watched`.
3. `interested`.
4. `not-now`.
5. `unseen`.

This precedence is resolved by the application before context creation and is covered by integration tests.

### Request

`SearchRequest` contains:

- Raw query text, held only for the duration of the call.
- Requested result limit from 1 through 100.
- Immutable `SearchUserContext`.
- Versioned sort selection; version 1 requires `BestMatch` for acceptance scoring.

The request contains no service, callback, provider credential, URL, database connection, path, logger, or UI object.

### Response

`SearchResponse` is an abstract closed result hierarchy rather than one nullable property bag:

- `SearchSuccessResponse` contains the derived intent, snapshot version, catalog fingerprint, total matching count before result-window truncation, ordered result array, and deterministically ordered warning array. Zero matches are a successful response.
- `SearchRejectedResponse` contains exactly one primary parse, unsupported-value, or limit diagnostic and no results. It may contain a captured snapshot version for diagnostics, but callers cannot assume one exists.
- `SearchCanceledResponse` contains no results, warnings, raw query, or timing data.
- `SearchIndexUnavailableResponse` contains a stable non-sensitive reason code and no catalog content.

The hierarchy makes invalid combinations such as “success plus syntax error” or “canceled plus partial results” unrepresentable. Timing, thread ID, wall-clock time, random values, and provider state are excluded from equality-bearing result data.

Each `SearchResult` contains:

- Candidate and candidate ID.
- Local status.
- Lexical tier, or an explicit filter-only marker.
- Lexical, affinity, and hybrid score components.
- Curated quality and discovery tie-break values.
- Typed evidence array.
- Structured explanation rows generated from the evidence.

User-facing prose is produced by the application from typed codes. Search does not localize strings.

## Query architecture

### Two-stage parse

Parsing is split into:

1. `SearchQueryScanner`: validates UTF-16 and emits bounded lexical tokens with zero-based UTF-16 spans.
2. `SearchQueryBinder`: resolves fields and converts tokens into typed clauses.

This split prevents numeric, Boolean, range, and field semantics from leaking into tokenization and makes both stages independently testable.

### Scanner properties

The scanner:

- Uses an iterative state machine, not a recursive parser.
- Does not compile or invoke regular expressions from user data.
- Validates the 512-code-unit raw limit before allocating proportional buffers.
- Rejects unpaired surrogates, prohibited controls, and unsafe Unicode format characters before normalization; U+200C ZERO WIDTH NON-JOINER and U+200D ZERO WIDTH JOINER remain valid.
- Recognizes quotes, a leading exclusion marker, field separator, comparison operators, and inclusive range separator only outside quoted values.
- Emits source spans rather than copied field and value substrings, and retains enough colon context for the binder to distinguish an empty recognized field from pasted title punctuation.
- Treats SQL-looking, command-looking, path-looking, and markup-looking content inside a quoted lexical value as data.
- Checks cancellation at bounded intervals.
- Stops at the first primary diagnostic in version 1.

### Binder properties

The binder:

- Uses an ordinal, closed map of grammar-version-1 field names.
- Produces immutable lexical, exclusion, and typed facet clauses.
- Applies total-clause, positive-clause, exclusion, hard-facet, phrase, phrase-token, normalized-token, fuzzy-term, and value-domain limits incrementally in source order.
- Treats a recognized empty field such as `tag:` as `missing-value`, an unknown field with an attached value such as `director:carpenter` as `unknown-field`, and an unknown identifier followed by a colon and whitespace or end of input as ordinary punctuation-bearing lexical text for pasted titles such as `Noroi: The Curse`.
- Rejects leading operator syntax on unquoted textual and availability facets before normalization, and classifies malformed numeric range separators independently from malformed endpoint operators.
- Counts only positive, unquoted lexical tokens of at least four Unicode scalar values toward the fuzzy-term budget; phrases, exclusions, and typed facets are not fuzzy-expanded.
- Never emits a partially executable query when a diagnostic exists.
- Derives intent deterministically from bound clause types.
- Does not construct SQL, file paths, URLs, XAML, commands, expressions, or delegates from user input.

The scanner owns raw-input and lexical-structure safety. The binder owns semantic budgets and field/value meaning. Semantic counters are not duplicated in the scanner; this preserves one source-ordered diagnostic policy and avoids divergent limits between stages.

### Typed clause model

The parsed model uses closed enums and records for:

- Search field.
- Positive or exclusion occurrence.
- Token or phrase lexical mode.
- Numeric operator: equal, less than, less than or equal, greater than, greater than or equal, inclusive range.
- Boolean value.
- Availability value.
- Local status value.
- Source span.

Numeric values are parsed with invariant culture and checked bounds. Comparisons and inclusive ranges are represented as values, not executable predicates.

## Search normalization

Search uses `SearchTextNormalizerV1`, separate from `TitleNormalizer` in Core.

The normalizer:

1. Validates well-formed UTF-16 and rejects unsafe Unicode format characters independently of caller validation.
2. Applies a documented compatibility-decomposition sequence.
3. Enumerates Unicode scalar values safely.
4. Removes combining marks only when attached to Latin base letters, preserving marks attached to non-Latin letters.
5. Preserves U+200C ZERO WIDTH NON-JOINER and U+200D ZERO WIDTH JOINER while rejecting every other Unicode `Format` character.
6. Applies the version-1 apostrophe and punctuation rules.
7. Lowercases invariantly without claiming full Unicode case folding.
8. Collapses matching whitespace.
9. Returns truthful transformation flags alongside text; semantic token limits remain binder-owned.

Display strings are never replaced by normalized strings. Version 1 performs no general transliteration, stemming, phonetic conversion, or homoglyph folding.

### Title variants

The bounded title-variant builder creates only:

- Primary normalized title.
- One punctuation-deletion variant when distinct.
- Roman/Arabic numeral variants for standalone I-through-XX title tokens.
- Explicit validated aliases supplied with provenance.

It does not generate arbitrary word deletions, permutations, phonetic guesses, substring aliases, provider suggestions, or unbounded numeric combinations.

Variants carry a typed origin. Evidence is selected from origin rather than inferred later from string comparison.

## Search-document validation

The index builder validates every document before building postings:

- Existing `RecommendationCandidateValidator` rules.
- Unique candidate IDs with ordinal identity.
- Unique canonical title/year identity under the approved identity normalizer.
- Bounded aliases, tags, lenses, franchise, language, availability values, and provenance fields.
- Finite affinity-independent quality and discovery values.
- Valid source revision and provider identifiers when present.
- No controls, unsafe Unicode format characters, or invalid UTF-16 in indexed text; join controls follow the documented international-text policy.

The build is all-or-nothing for the curated local catalog. Invalid documents produce a bounded build report and no publishable snapshot. If a prior healthy snapshot exists, it remains current. Initial build failure yields `local-index-unavailable`; the engine never searches a silent partial catalog.

Build issues preserve the offending record's original zero-based source position,
even though accepted documents are later sorted by candidate ID for deterministic
indexing. Duplicate issues additionally carry the validated offending candidate
ID and the first conflicting source position. Reports contain at most 32 issues
and explicitly indicate omitted overflow; they never include arbitrary title,
query, provider payload, or exception text.

Untrusted online enrichment is validated and quarantined before it can become a new document input. One invalid enrichment record does not mutate the current snapshot.

## Immutable index snapshot

### Deterministic document IDs

Before indexing, documents are sorted by candidate ID with `StringComparer.Ordinal`. The resulting zero-based integer document ID is stable for identical input and is used only inside that snapshot.

### Snapshot contents

`SearchIndexSnapshot` contains private immutable arrays and frozen lookup structures for:

- Documents by integer ID.
- Candidate ID to integer ID.
- Exact canonical title and title-variant postings.
- Title token postings and sorted title vocabulary.
- Exact tag postings and tag-token postings.
- Primary and secondary lens postings.
- Franchise postings.
- Original-language postings.
- Enumerated availability postings.
- Boolean slasher and extreme postings.
- Numeric year, runtime, scare, gore, and dread structures.
- Field-aware fuzzy vocabularies and bounded term-candidate structures.
- Coverage counts for conditional facets.
- Snapshot version, normalizer version, document count, and deterministic catalog fingerprint.

Posting lists are sorted integer arrays with no duplicates. Published arrays are never returned for mutation.

### Facet representation

- Low-cardinality Boolean and enumerated facets use immutable bit sets or sorted postings, selected by benchmark evidence.
- Numeric fields use immutable value-sorted pairs plus binary-search range boundaries, or measured bit-set alternatives for tiny domains such as 0–5 intensity.
- Missing values are represented explicitly and never satisfy positive value constraints.
- Facet counts and results are computed from the same captured snapshot.

### Snapshot publication

Index construction occurs on a worker thread owned by application orchestration. After all validation and structures succeed, `SearchIndexStore.Publish` uses an atomic reference exchange.

An active query keeps its captured snapshot even if a newer snapshot is published. Old managed snapshots become collectible only after in-flight queries release them. No query combines postings or coverage from different versions.

Version 1 does not persist the derived index to disk. Rebuilding within the two-second budget is safer than introducing stale-index migration, tamper, and cache-coherency risks. Disk persistence requires a separate reviewed design.

## Retrieval pipeline

Each execution follows this fixed order:

1. Validate programmer-controlled request fields and capture the current snapshot once.
2. Parse and bind the raw query.
3. Return a typed diagnostic without execution if parsing fails.
4. Derive local-search or filtered-discovery intent.
5. Retrieve exact positive lexical postings.
6. Intersect all positive lexical clauses using smallest-posting-first planning with deterministic tie resolution.
7. Apply hard facets.
8. Subtract exclusion postings or predicates.
9. If the exact candidate window is insufficient, perform bounded fuzzy expansion for eligible terms only.
10. Reapply all positive, facet, and exclusion requirements to fuzzy candidates.
11. Collect typed lexical and facet evidence.
12. Score and rank.
13. Apply the intent-specific selection policy.
14. Build typed explanations for the retained result window.
15. Return total match count and the requested window.

Candidate sets are represented by sorted integer buffers or measured bit sets. Set operations preserve deterministic document-ID order before ranking.

### Positive lexical clauses

All positive clauses use implicit AND. A candidate must satisfy every positive clause in an allowed field. A field-scoped clause cannot be satisfied by a different field.

### Exclusions

Exclusions are evaluated after positive retrieval and hard facets. They never add score. Evidence records only that all exclusions were satisfied; it does not echo excluded raw text.

### Filter-only retrieval

A constraints-only or exclusion-only query begins from the complete document-ID domain in the captured snapshot, then applies facets and exclusions. It is labeled filtered discovery and is not passed through watched/disliked default exclusions.

## Bounded typo retrieval

Fuzzy work runs only when stronger retrieval does not fill the requested result window.

The fuzzy index groups normalized terms by field and scalar length and uses a measured bounded candidate structure, such as normalized trigram-to-term-ID postings. The query planner first applies length and shared-fragment bounds, then verifies candidates with an exact bounded Levenshtein implementation.

Required properties:

- Unicode scalar values are compared, not isolated UTF-16 halves.
- Terms of length three or less receive exact or prefix behavior only.
- Maximum edit distance follows the requirements by normalized scalar length.
- At most four query terms enter fuzzy expansion.
- Distance computation exits as soon as the active row minimum exceeds the permitted distance.
- Fixed-size row storage is stack-based where safe under the 64-character token limit; user text is not retained in shared pools.
- Title and non-title fuzzy evidence remain distinct.
- Every fuzzy candidate must still satisfy all other clauses and facets.
- Cancellation is checked between term buckets and during bounded candidate loops.

No phonetic, stemming, synonym, embedding, or general semantic behavior is implied by fuzzy matching.

## Facet and coverage behavior

`HardFacetEvaluator` is a pure typed evaluator over snapshot data and per-query local status.

Static catalog facets are indexed. Local status is deliberately not indexed because it is mutable application state; it is evaluated from the immutable `SearchUserContext` captured for the request.

Coverage warnings are computed before returning an empty result:

- A conditional facet with zero validated static coverage emits `facet-no-coverage`.
- Verified availability with zero verified records emits `facet-no-verified-coverage`.
- A warning does not reinterpret metadata, broaden a query, or contact a provider.

Facet precision takes priority over recall. Unknown values fail positive filters except the explicit `unknown` availability value.

## Lexical evidence and scoring

### Evidence first

`LexicalEvidenceCollector` determines typed facts before scoring. `LexicalScorer` consumes those facts. `SearchExplanationBuilder` consumes the same facts and score components.

This prevents explanations from reverse-engineering a score and prevents display prose from becoming ranking input.

Evidence ordering is deterministic:

1. Strongest match-class evidence.
2. Field order from the requirements.
3. Normalized term ordinal order.
4. Stable evidence-code ordinal order.

### Tier assignment

The strongest satisfied match class assigns exactly one primary lexical tier. Secondary evidence may improve score only inside that tier. No numeric score crosses a tier boundary.

### Score domains

All components are finite doubles in the inclusive range 0–100. Inputs are validated before ranking. Arithmetic is checked where integral overflow is possible. Non-finite values are rejected at context or document creation.

`LexicalScorer` version 1 is an evidence-only pure function. It applies the
requirements weights of 30% title identity, 25% phrase/token completeness, 20%
field importance, 20% exactness, and 5% active-facet satisfaction. Exact lookup
values are closed switch mappings over evidence codes and fields; they do not
depend on display text, current culture, collection enumeration order, catalog
size, or result position. Mandatory positive-clause AND semantics make lexical
coverage 100 for every returned text result. The weakest required lexical fact
controls exactness. Duplicate evidence for one normalized query value, indexed
value, and field is collapsed before field-importance averaging.

For filter-only discovery, lexical total is exactly zero even though the facet
summary remains available for explanation. Exclusions and local status never add
lexical score.

Version 1 text-query hybrid score is:

```text
hybrid = (lexical * 0.85) + (affinity * 0.15)
```

The value is rounded once at the result boundary using a documented midpoint rule. Unrounded values are used for comparisons to avoid rounding collisions. Quality and discovery remain later tie-break values and are not hidden additions to the hybrid score.

Filter-only ranking uses affinity as the primary score after all filters pass, then quality, discovery, normalized title, year, and candidate ID. Missing affinity is zero.

### Stable comparer

`StableSearchResultComparer` implements the complete requirements order explicitly. It never relies on:

- Dictionary or hash-set enumeration order.
- Current culture.
- Process-randomized hash codes.
- Wall-clock time.
- Random numbers.
- Provider response order.
- Task completion order.

Every comparison reaches candidate ID as the final ordinal key.

## Personalization anti-corruption boundary

Search accepts only a `SearchUserContext` affinity map. It does not call `IRecommendationEngine.Rank`.

`HorrorHarbor.Recommendations` will expose a dedicated full-catalog affinity scorer with these rules:

- Score each candidate independently against one immutable taste profile.
- Do not exclude watched or disliked candidates.
- Do not truncate.
- Do not diversify.
- Do not produce search tiers or search evidence.
- Return finite normalized values keyed by candidate ID.

The application builds the map once per profile/status revision and supplies it to search. This keeps recommendation implementation details out of the local engine while allowing the documented 15% contribution.

## Intent-specific diversity

Diversity is explicitly separated by intent:

- Personalized discovery: recommendation ranking followed by recommendation diversity.
- Local catalog search: no diversity reordering; stable relevance order and direct truncation.
- Filtered discovery: no diversity reordering in version 1; stable affinity order and direct truncation.
- Explicit online search: provider order is displayed only inside a provider-attributed section and never merged into local rank.

This no-op local-search policy is deliberate. Diversity must never suppress, demote, or remove an exact known-item result. A future filtered-discovery diversity policy requires its own measurable contract and cannot alter local-search order.

## Application orchestration

### Intent router

The application uses three explicit paths:

1. Empty query and no active search facets: personalized discovery.
2. Non-empty or constraints-bearing local query: `IHorrorSearchEngine`.
3. Explicit provider command: online-search coordinator.

Local engine failure never triggers an undisclosed online request. Online failure never triggers a different provider automatically.

### Search session coordinator

A focused application service replaces fire-and-forget refresh behavior. It owns:

- One lock protecting current cancellation-source replacement and disposal.
- A monotonically increasing 64-bit request version.
- The 200 ms user-input debounce boundary.
- Worker-thread invocation of CPU-bound local search.
- Cancellation of the prior request before starting the next.
- Publication only when the completed version still equals the latest version.
- Dispatcher marshaling of one immutable presentation snapshot.
- Safe conversion of unexpected failures to a correlation reference.

No property setter invokes an unobserved asynchronous refresh. Commands and lifecycle methods await the coordinator. Cancellation is a normal outcome, not an error banner.

### Index coordinator

A separate application service owns startup and rebuild:

1. Load and validate the catalog.
2. Map candidates and approved enrichment into search documents.
3. Build off-dispatcher.
4. Publish atomically on success.
5. Retain the current healthy snapshot on rebuild failure.
6. Trigger an explicit search refresh against the new version only after publication.

Provider enrichment may request a rebuild only after a complete validated enrichment batch. It cannot mutate an active result or snapshot in place.

## Explicit online search architecture

### Separate API shape

The online-provider interface accepts only:

- Bounded submitted search text.
- Provider-supported locale.
- Page number.
- Page size no greater than 20.
- Cancellation token.

It does not accept `SearchUserContext`, taste profile, local statuses, candidate arrays, file paths, Plex objects, local index objects, or arbitrary destination URIs.

### Provider isolation

Each provider implementation requires:

- Dedicated `HttpClient` registration and handler policy.
- Fixed HTTPS scheme, host, and port allowlist.
- Automatic redirects disabled and all 3xx rejected.
- Provider-scoped authorization header attached only after destination validation.
- No credential in URI, query string, cache key, log, or exception.
- Response-headers-first streaming.
- Compressed and decompressed byte bounds.
- Strict media type, JSON depth, array length, text, range, and identity validation.
- Total deadline no greater than ten seconds.
- No more than two concurrent requests per provider.
- Small bounded retry policy honoring only capped retry delays.
- Typed provenance on every accepted result.

Online results remain in a separate collection. Import requires explicit identity validation and does not alter the active local result order.

### Privacy invariant

Capturing-handler security tests must prove that an explicit provider request contains no watched key, Plex history value, feedback value, profile weight, recommendation score, candidate path, unrelated provider credential, or local database value.

## Error and observability policy

Expected user outcomes are typed values, not exceptions at the engine boundary:

- Success with results.
- Success with zero results.
- Invalid syntax.
- Unsupported field or value.
- Limit exceeded.
- Canceled.
- Local index unavailable.

Unexpected exceptions cross into the application error reporter only. Logs must not contain raw query text, normalized query text, matched private status, full provider payload, credential, URL query string, or local path.

Permitted aggregate diagnostics are bounded and non-sensitive, for example:

- Snapshot version.
- Document count.
- Outcome code.
- Clause-count bucket.
- Result-count bucket.
- Duration bucket when diagnostic mode is explicitly enabled.

No telemetry or remote logging is introduced.

## Cancellation policy

Cancellation is checked:

- Before and during scanning.
- Between binding clauses.
- During index build document and vocabulary loops.
- Between posting intersections.
- During numeric/facet scans.
- During fuzzy term and candidate loops.
- During evidence collection and scoring batches.
- Before explanation construction and response publication.

Hot loops use a measured bounded check interval, initially no more than 256 document or term operations. The cancellation performance gate requires observation within 100 ms after signal on the 50,000-document corpus.

Low-level parser, retrieval, filtering, scoring, explanation, and index-build components throw `OperationCanceledException` only for their supplied token. `IHorrorSearchEngine.Execute` converts matching query cancellation into `SearchCanceledResponse`. The application coordinator treats that response as non-publishable work and independently blocks every stale request version. Index-build cancellation remains an `OperationCanceledException` handled by the index coordinator because no query response exists for a canceled rebuild.

## Memory and allocation policy

- Published snapshots own their immutable memory.
- Per-query code uses compact integer candidate sets rather than copying documents.
- Posting intersections reuse request-local numeric buffers where measurement proves safe.
- User strings are not placed in shared object pools.
- Stack allocation is bounded by grammar constants.
- Array-pool use, if introduced, is restricted to non-sensitive numeric buffers and returned in `finally` blocks.
- Explanations are built only for the retained result window.
- No query materializes all formatted explanations or UI view models.
- No query retains raw input after response construction.

Allocation decisions are benchmarked; clever pooling is not accepted without evidence that it improves the defined budgets and preserves cancellation safety.

## Verification ownership

### Unit tests

`HorrorHarbor.UnitTests` will reference `HorrorHarbor.Search` and own:

- Scanner and binder tables.
- UTF-16 span and diagnostic tests.
- Normalization and title-variant tests.
- Roman numeral I-through-XX tests.
- Posting intersection tests.
- Numeric, Boolean, status, and coverage facet tests.
- Edit-distance boundary tests.
- Evidence, tier, score, and tie-order tests.
- Snapshot immutability and atomic-store tests.

### Integration tests

`HorrorHarbor.IntegrationTests` will:

- Copy the versioned acceptance corpus to test output.
- Load the real catalog through production validation.
- Apply the two reviewed catalog corrections.
- Execute all corpus cases.
- Compute top-1, MRR@10, recall, forbidden rate, precision, typo top-3, NDCG@10, diagnostic accuracy, and metadata-honesty metrics.
- Repeat order under supported cultures and 100 deterministic runs.
- Test catalog rebuild while queries hold the prior snapshot.
- Test affinity and local-status application without exclusion.

### Security tests

`HorrorHarbor.SecurityTests` will reference `HorrorHarbor.Search` and own:

- Invalid Unicode, control-input, unsafe-format, UTF-16-span, and permitted-join-control cases.
- Clause, phrase, token, and result limits.
- Repeated-character and worst-case typo inputs.
- SQL, command, path, URL, and XAML-looking data.
- Cancellation at every pipeline boundary.
- Proof that no user regular expression or executable expression is created.
- Provider request-content and credential-isolation tests when online adapters are added.

### Performance gate

`HorrorHarbor.PerformanceTests` will reference `HorrorHarbor.Search` and add:

- 50,000-document immutable index build.
- Warm exact-title p95 over 200 runs.
- Warm multi-token p95 over 200 runs.
- Phrase plus five facets p95 over 100 runs.
- Bounded typo p95 over 100 runs.
- Filter-only p95 over 200 runs.
- Managed allocation per operation.
- Mid-query cancellation observation.

The existing recommendation benchmark remains a separate metric.

### Architecture checks

Static validation must assert:

- Every project remains in the solution.
- `HorrorHarbor.Search` targets `net10.0` and references only Core.
- No WPF, SQLite, HTTP, provider, credential, or application namespace is referenced by Search.
- The acceptance corpus parses and its pinned catalog hash/count match.
- Every corpus evidence, warning, and diagnostic code exists in the requirements contract.

## Migration sequence

### Stage 1: Project and contracts

- Create `HorrorHarbor.Search` with only the Core reference.
- Add contracts, parser result types, diagnostics, and normalization constants.
- Add project references to unit, integration, security, performance, and application projects.
- Add architecture dependency checks.

### Stage 2: Parser and normalization

- Implement scanner, binder, source spans, all limits, and title variants.
- Promote parser, Unicode, punctuation, diacritic, Roman numeral, and malicious-input tests.

### Stage 3: Index, retrieval, facets, and lexical rank

- Implement all-or-nothing immutable snapshots.
- Implement exact retrieval, facets, exclusions, bounded fuzzy fallback, evidence, tiers, stable score, and explanation rows.
- Execute the acceptance corpus without personalization first.

### Stage 4: Recommendation affinity boundary

- Extract per-candidate affinity scoring from the recommendation implementation without changing discovery output.
- Build a complete affinity snapshot in the application.
- Add the documented 85/15 within-tier hybrid score.
- Prove watched and disliked exact matches remain searchable.

### Stage 5: Application coordination

- Introduce search intent router, index coordinator, local-state resolver, and search-session coordinator.
- Keep empty-query discovery on the existing recommendation path.
- Route every text-bearing or constraints-bearing local query to the new engine.
- Remove `ApplySearch` and fire-and-forget refresh behavior after parity gates pass.
- Never silently fall back to the legacy top-30 filter on a new-engine failure.

### Stage 6: Search presentation

- Add active facet chips, grammar help, typed diagnostics, metadata-coverage states, evidence labels, and accessible score explanations.
- Rename recommendation-only UI labels where local search is displayed.
- Add UI Automation and visual scenarios.

### Stage 7: Reviewed online provider

- Create the separate provider assembly only after local search passes all gates.
- Implement one provider with dedicated transport policy and privacy tests.
- Add explicit provider action and separate result presentation.
- Do not merge provider metadata into the active index without validated import and rebuild.

### Stage 8: Legacy removal and release

- Delete obsolete substring search and incompatible presentation models.
- Complete SOLID, async, culture, security, allocation, and disposal review.
- Run all quality gates, publish, automate the UI, and verify release artifacts.

## Rejected alternatives

### Keep filtering the recommendation top 30

Rejected because it makes catalog items outside the truncated recommendation window undiscoverable and excludes watched/disliked known items.

### Search the full catalog with ad hoc LINQ on every keystroke

Rejected as the final architecture because it repeatedly normalizes strings, lacks a typed grammar and evidence model, scales poorly to 50,000 records, and encourages presentation and ranking concerns to mix.

### Reuse `IRecommendationEngine.Rank` as search personalization

Rejected because the operation excludes watched titles, truncates to at most 100, applies diversity, and emits recommendation prose. Search requires independent full-catalog affinity.

### Put search in the WPF application

Rejected because it prevents platform-neutral unit, security, and performance testing and encourages dispatcher, view-model, and retrieval coupling.

### Put search in the data project or SQLite FTS

Rejected for version 1 because search behavior must be independent of persistence, deterministic across platforms, rich in typed evidence, and available over an already validated in-memory catalog. SQLite FTS ranking and tokenizer behavior do not directly satisfy the specified tiers, aliases, facets, and explanations.

### Add Lucene.NET immediately

Rejected at the current scale because it introduces a large supply-chain and operational surface, its scoring would still require extensive replacement for the contract, and the measured target is only 50,000 documents. This decision can be revisited only if the base-class-library index fails measured budgets after profiling.

### Add embeddings or hosted semantic search

Rejected for version 1 because it adds network/privacy requirements, model/version nondeterminism, difficult explanations, and no demonstrated need for known-item and curated horror-facet retrieval.

### Share one HTTP client and policy across providers

Rejected because credentials, authorities, retry behavior, payload limits, and provenance are provider-specific trust boundaries.

### Persist the derived index immediately

Rejected because the build budget makes in-memory reconstruction practical and avoids index schema migration, stale cache, tamper, and partial-write risk.

## Definition of architecture completion

This architecture milestone is complete when:

1. This decision is reviewed against the requirements and threat model.
2. Every required pipeline responsibility has one owner.
3. Dependency directions are explicit and cycle-free.
4. Local search, recommendation discovery, and explicit online search have separate entry points.
5. Index immutability, cancellation, stale-publication prevention, typed evidence, and privacy boundaries are implementable without hidden global state.
6. Test ownership covers correctness, relevance, security, determinism, performance, accessibility, and release integration.
7. Static validation passes after the document is added.

Implementation may then begin with Stage 1 and Stage 2. Architecture completion does not imply that the engine itself has been implemented or promoted.
