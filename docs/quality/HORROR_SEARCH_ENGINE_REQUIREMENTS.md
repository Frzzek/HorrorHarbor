# Horror Search Engine Requirements

## Authority and purpose

This document defines the measurable product contract for the HorrorHarbor search-engine overhaul. It is governed by the [Engineering Constitution](ENGINEERING_CONSTITUTION.md), constrained by the [Threat Model](THREAT_MODEL.md), and verified by a versioned machine-readable relevance corpus.

The goal is not merely to filter the current recommendation list. The application must provide a deterministic horror-specific retrieval system that can:

1. Find a known film reliably.
2. Explore horror concepts, subgenres, formats, moods, and intensity ranges.
3. Personalize discovery without corrupting lexical relevance.
4. Explain every important match and ranking contribution.
5. Remain useful and truthful offline.
6. Optionally search or enrich from reviewed online databases without transmitting local history or taste profiles.

## Product principles

### Local catalog is authoritative

- Local search executes without a network connection or provider credential.
- A network failure never removes, delays, or reorders completed local results.
- Curated local fields are not silently overwritten by lower-confidence online metadata.
- Online fields retain provider, provider record ID, retrieval time, confidence, and source attribution.

### Search and recommendation are different operations

- **Personalized discovery** answers “What should I watch?”
- **Local catalog search** answers “Find films matching this query.”
- **Explicit online search** answers “Search the disclosed provider beyond my local catalog.”
- Direct search examines the complete validated eligible catalog before result truncation.
- Recommendation diversity cannot suppress an exact title match.
- Recommendation score is a separately labeled contribution and cannot move a weaker lexical tier above a stronger lexical tier.

### Missing data stays missing

- The current catalog has no populated original-title or original-language fields, only four franchise values, no TMDB IDs, and no verified availability values.
- The UI reports facet coverage and unavailable capabilities honestly.
- A tag such as “Korean horror” is not silently converted into an original-language fact.
- “US streaming availability requires live verification” is treated as unknown, not available.
- Online metadata may activate a facet only for records whose validated field and provenance are present.
- A verified availability fact requires bounded online-provider provenance. Curated
  free-text availability labels cannot create verified provider facts or activate
  provider availability facets.

## Search intents and eligibility

### Personalized discovery

- Triggered by an empty text query with mood/profile controls, or by an explicit Discover action.
- Excludes watched and disliked candidates by default.
- Uses the deterministic recommendation engine and recommendation diversity policy.
- May apply selected hard facets before recommendation ranking.
- Labels the result score as a recommendation match, not a search relevance score.

### Local catalog search

- Triggered by one or more text, phrase, exclusion, or query-language clauses.
- Searches every validated local candidate, including candidates outside the recommendation top 30.
- Includes watched, disliked, and “not now” titles when they match, with visible status labels; status filters can narrow them explicitly.
- Uses lexical relevance tiers first and personalization only inside the same tier.
- Executes entirely in process against an immutable index snapshot.
- Intent classification is deterministic: a bare term, quoted phrase, or positive `title`, `tag`, `lens`, `franchise`, or `language` clause makes the operation a local catalog search, even when the selected field has zero metadata coverage.
- A valid query containing only numeric, Boolean, availability, status, or exclusion constraints is filtered discovery and uses the filter-only ranking contract.

### Explicit online search

- Runs only after an explicit user action that names the provider.
- Sends only the submitted search text and provider-required locale/page parameters.
- Never sends Plex history, watched keys, feedback, taste weights, recommendation scores, local paths, or credentials for another provider.
- Returns provider-attributed results in a separate section until a reviewed merge/import action occurs.
- Is bounded, cancellable, optional, and unavailable without affecting local search.

## Query grammar version 1

### General rules

- Matching is case-insensitive and culture-invariant.
- Whitespace separates clauses; repeated whitespace is ignored.
- Bare terms are positive lexical clauses.
- A double-quoted value is a normalized phrase.
- A leading `-` excludes a lexical clause or field clause.
- Positive clauses use implicit `AND`: every positive clause must match somewhere unless it is a hard facet.
- Hard facets always use `AND` across clauses.
- No recursive groups, arbitrary Boolean expressions, wildcards, user regular expressions, scripts, SQL, paths, URLs, or provider authorities are supported.
- An identifier followed by a colon and a non-whitespace value is field syntax. Unknown fields and malformed operators in that form produce a typed diagnostic; they are not reinterpreted as ordinary text.
- To support pasted display titles, an unknown identifier followed by a colon and then whitespace or end of input is ordinary punctuation-bearing lexical text. Thus `Noroi: The Curse` searches the title, while `director:carpenter` produces `unknown-field`.
- A recognized field followed by an empty value remains field syntax, so `tag:` produces `missing-value` rather than becoming ordinary text.
- An unquoted recognized textual or availability value beginning with `<`, `>`, `=`, or `!` produces `invalid-operator` before normalization. Quoted lexical values continue to treat those characters as data.
- An unterminated quote, empty recognized-field value, invalid range, or unsupported enum value produces a typed diagnostic and no partial execution.

### Text examples

| Query | Meaning |
|---|---|
| `descent` | Match normalized title, alias, tags, lenses, or franchise |
| `"found footage"` | Match the normalized adjacent phrase |
| `cosmic isolation` | Require both concepts somewhere in searchable fields |
| `slasher -comedy` | Require slasher and exclude comedy matches |
| `Noroi: The Curse` | Treat the pasted colon as title punctuation and require the normalized title terms |
| `title:"you're next"` | Search title and validated title aliases only |
| `tag:"home invasion"` | Require an exact normalized tag |
| `lens:cosmic` | Require a primary or secondary horror lens containing cosmic |

### Supported fields

| Field | Value form | Semantics |
|---|---|---|
| `title` | token or quoted phrase | Canonical title, original title, and validated aliases |
| `tag` | token or quoted phrase | Exact normalized tag for quoted values; token match otherwise |
| `lens` | token or quoted phrase | Primary or secondary horror lens |
| `franchise` | token or quoted phrase | Validated franchise metadata only |
| `language` | token or quoted phrase | Validated original-language code or name only |
| `year` | exact, comparison, or inclusive range | Release year |
| `runtime` | exact, comparison, or inclusive range | Runtime in minutes |
| `scare` | exact, comparison, or inclusive range | Curated 0–5 scare intensity |
| `gore` | exact, comparison, or inclusive range | Curated 0–5 gore intensity |
| `dread` | exact, comparison, or inclusive range | Curated 0–5 dread intensity |
| `slasher` | `true` or `false` | Curated slasher flag |
| `extreme` | `true` or `false` | Curated extreme flag |
| `availability` | `verified`, `unknown`, or provider-defined value | Validated availability state only |
| `status` | `unseen`, `watched`, `interested`, `not-now`, or `disliked` | Local user state supplied by the application layer |

### Numeric forms

- Exact: `year:2009`, `scare:5`.
- Inclusive range: `year:2000..2009`, `runtime:80..100`, `gore:4..5`.
- Comparison: `year:>=2020`, `runtime:<90`, `dread:>=4`.
- Range endpoints must be ordered and within the validated domain.
- An extra or repeated range dot, such as `year:2000...2009`, produces `invalid-range`; a comparison operator inside a range endpoint produces `invalid-operator`.
- Runtime is expressed in whole minutes.
- Release year uses the same supported domain as candidate validation.
- Intensity values are whole numbers from 0 through 5 in grammar version 1.

### Exclusion behavior

- `-comedy` excludes a record if the normalized term matches any searchable text field.
- `-tag:comedy` excludes only matching tags.
- `-year:1980..1989` excludes the numeric range.
- Exclusions never add relevance score.
- A query containing only exclusions is valid and ranks the remaining records as filtered discovery.

### Grammar safety limits

| Limit | Maximum |
|---|---:|
| Raw UTF-16 query length | 512 characters |
| Total clauses | 32 |
| Positive lexical clauses | 16 |
| Exclusion clauses | 16 |
| Hard facet clauses | 16 |
| Quoted phrases | 8 |
| Normalized tokens per phrase | 12 |
| Normalized token length | 64 characters |
| Fuzzy-eligible terms | 4 |
| Requested local results | 100 |
| Requested online results per provider page | 20 |

Control characters, unpaired UTF-16 surrogates, unsafe Unicode format controls, and values outside these limits are rejected with a typed diagnostic. U+200C ZERO WIDTH NON-JOINER and U+200D ZERO WIDTH JOINER are the only permitted Unicode `Format` characters because they can carry meaning in supported writing systems.

## Normalization and aliases

### Search normalization

- Validate well-formed UTF-16 before normalization.
- Apply Unicode compatibility decomposition consistently.
- Remove combining marks from the matching copy only when they are attached to a Latin base letter. This provides bounded Latin accent folding without erasing distinctions in other writing systems.
- Preserve combining marks attached to non-Latin base letters and preserve U+200C ZERO WIDTH NON-JOINER and U+200D ZERO WIDTH JOINER.
- Reject every other Unicode `Format` character before matching; bidirectional overrides, isolates, zero-width spaces, and similar invisible controls are not normalized away silently.
- Delete apostrophe-like punctuation when it occurs inside an alphanumeric run, so `You're Next` has the matching copy `youre next`.
- Convert all remaining punctuation, symbols, and separators to spaces for the primary matching copy, so `Noroi: The Curse` becomes `noroi the curse` and `Frontier(s)` becomes `frontier s`.
- Lowercase with invariant rules.
- Collapse whitespace and trim.
- Preserve the validated display value unchanged for presentation.
- Do not perform general transliteration, stemming, phonetic conversion, or homoglyph folding.
- Never normalize by executing user-provided patterns or locale-specific code.

### Title behavior

- Punctuation-insensitive title identity means `youre next` can match `You're Next`.
- Exact normalized canonical-title matches receive the strongest tier.
- In addition to the primary matching copy, the title index must create one bounded punctuation-deletion variant by deleting punctuation and symbols while preserving whitespace that already existed in the display title. This permits `frontiers` to match `Frontier(s)` without generating arbitrary token combinations.
- Primary and punctuation-deletion variants are deterministic canonical-title normalization variants, not provider aliases. Full-query equality with either variant remains a tier-0 canonical-title identity match, while the explanation identifies which variant was used.
- Original titles and aliases are searchable only when explicitly stored with provenance.
- Leading articles may be indexed as an additional ordering alias, but dropping an article cannot create a stronger match than the canonical exact identity.
- Roman numerals remain searchable tokens; the index must add bounded Roman/Arabic numeral aliases for values from I through XX so a query such as `saw 6` can match `Saw VI` without general numeric guessing.
- Provider aliases cannot silently replace canonical local titles.

### Typo tolerance

- Tokens of three or fewer normalized characters are never fuzzy matched.
- Tokens of four through seven characters allow at most edit distance one.
- Tokens of eight or more characters allow at most edit distance two.
- Only normalized tokens from positive, unquoted lexical clauses are fuzzy-eligible. Quoted phrases, exclusions, and typed facets never consume the fuzzy-term budget or enter fuzzy expansion.
- Fuzzy matching is applied only after exact, phrase, prefix, and exact-field retrieval fail to satisfy the requested result window.
- Fuzzy work is restricted to bounded index terms and at most four query terms.
- Fuzzy matches are always explained and rank below non-fuzzy matches of otherwise comparable intent.
- No phonetic or semantic claim is made unless a separately tested component is introduced.

## Facets

### Required local facets

- Primary and secondary horror lens.
- Tags.
- Release year.
- Runtime.
- Scare, gore, and dread intensity.
- Slasher flag.
- Extreme flag.
- Local feedback/watch status.

### Conditional facets

- Franchise.
- Original language.
- Original title/alias.
- Availability provider and verified availability state.

Conditional facets remain visible only with an explicit coverage indicator or are disabled with an explanation when coverage is zero. Facet counts are calculated after the current query and all other active facets, while omitting the facet being counted when multi-select behavior requires it.

### Facet guarantees

- Hard-facet precision is 100%; no returned record may violate an active hard facet.
- Unknown values do not satisfy a positive value filter.
- `availability:unknown` matches records whose availability is explicitly unknown or requires live verification.
- Facet values use validated metadata, not inference from unrelated free-text tags.
- Counts and result lists come from the same immutable index snapshot.

## Retrieval and ranking contract

### Searchable fields and relative importance

1. Canonical title.
2. Original title and validated aliases.
3. Exact tag values.
4. Primary horror lens.
5. Secondary horror lens.
6. Validated franchise.
7. Other normalized tag tokens.
8. Validated original language.

Availability labels are facets and display metadata, not general free-text relevance fields.

### Lexical relevance tiers

Results are ordered by the strongest satisfied tier before any score within a tier:

| Tier | Match class |
|---:|---|
| 0 | Exact normalized canonical title or mandatory deterministic punctuation variant |
| 1 | Exact normalized original title or validated alias |
| 2 | Canonical-title phrase or all-title-token match |
| 3 | Canonical-title prefix/token match |
| 4 | Exact tag, lens, or franchise phrase match |
| 5 | Multi-field exact token match |
| 6 | Bounded fuzzy title match |
| 7 | Bounded fuzzy non-title match |

No result from a weaker tier may outrank a result from a stronger tier under the default Best Match sort.

### Score components

Each result exposes finite normalized contributions in the range 0–100:

- Title identity.
- Phrase and token coverage.
- Field importance.
- Exactness or bounded typo penalty.
- Facet satisfaction summary.
- Optional personalized recommendation affinity.
- Optional curated quality/discovery tie-break signals.

Version 1 computes the text-query lexical score from evidence using these fixed
weights:

| Lexical component | Weight |
|---|---:|
| Title identity | 30% |
| Phrase and token completeness | 25% |
| Searchable-field importance | 20% |
| Exactness or bounded typo penalty | 20% |
| Active-facet satisfaction summary | 5% |

Title identity uses the strongest title evidence. Phrase/token completeness is
100 for a returned text result because positive lexical clauses use mandatory AND
semantics; filter-only results report zero. Exactness uses the weakest matched
lexical fact so one typo or prefix cannot be hidden by stronger terms. Field
importance is the arithmetic mean across distinct matched lexical facts after
duplicate evidence for the same normalized query value, indexed value, and field
is collapsed. The facet component is 100
when all active positive hard facets were satisfied, including the vacuous
no-facet case, so an otherwise perfect exact-title result can still reach 100.
The active positive-facet count is retained separately so explanations omit this
component when no facet was requested. Because hard facets are mandatory, the
component is constant among records evaluated for the same query. Exclusions
never add score.

Filter-only queries report a lexical score of zero. Their satisfied-facet
component remains available for explanation, but is not folded into a synthetic
lexical relevance value.

For a text-bearing local catalog query:

- Lexical relevance determines the tier and at least 85% of the within-tier score.
- Personalized recommendation affinity contributes at most 15% within the same lexical tier.
- Curated quality and discovery values are final within-tier tie-break signals and cannot cross tiers.

For a filter-only query, recommendation affinity is the default ranking signal after all hard filters pass. The UI labels this as filtered discovery.

### Stable ordering

The complete default tie-break order is:

1. Lexical tier ascending.
2. Hybrid within-tier score descending.
3. Lexical score descending.
4. Recommendation score descending.
5. Curated quality score descending.
6. Curated discovery score descending.
7. Normalized title ascending with ordinal comparison.
8. Release year ascending, with missing values last.
9. Candidate ID ascending with ordinal comparison.

The same inputs must produce identical candidate IDs, order, rounded scores, match kinds, diagnostics, and explanations across repeated execution and supported UI cultures.

## Explanations

Every local search result must provide:

- Search intent and strongest match class.
- Fields and terms that matched.
- Whether a typo correction or alias was used.
- Active hard facets satisfied by the record.
- Lexical score and its component rows.
- Recommendation contribution, when used, labeled separately from search relevance.
- Local status such as watched or disliked, when applicable.
- Metadata provenance and freshness for online-enriched fields.

Explanations are generated from typed match evidence, not reconstructed from formatted prose. They do not echo untrusted raw query text unless safely normalized and necessary for an actionable parse diagnostic.

Structured explanation rows expose a stable row kind, stable code, optional
normalized percentage value, optional percentage weight, and optional typed
evidence reference. Evidence rows reuse the stable evidence code. Score rows use
these stable nonlocalized codes:

| Explanation score code | Required meaning |
|---|---|
| `score-title-identity` | Title-identity lexical component and its 30% lexical weight. |
| `score-lexical-coverage` | Mandatory lexical-coverage component and its 25% lexical weight. |
| `score-field-importance` | Searchable-field component and its 20% lexical weight. |
| `score-exactness` | Exactness or bounded-typo component and its 20% lexical weight. |
| `score-facet-satisfaction` | Active positive-facet component and its 5% lexical weight; omitted when no positive facet was requested. |
| `score-lexical-total` | Lexical total and its 85% text-query ranking weight; omitted for filter-only discovery. |
| `score-affinity` | Separate recommendation-affinity contribution: 15% for text queries and 100% for filter-only discovery. |
| `score-hybrid-total` | Final within-tier hybrid value before stable tie-break fields. |
| `score-quality-tie-break` | Curated quality tie-break value; not folded into hybrid score. |
| `score-discovery-tie-break` | Curated discovery tie-break value; not folded into hybrid score. |

### Stable evidence codes

Evidence codes are versioned API values. They are ordinal strings, are never localized, and are converted to user-facing text only at the presentation boundary. A result may contain multiple codes in deterministic ordinal order. Existing code semantics cannot be changed or reused; additions require tests and a corpus revision.

| Evidence code | Required meaning |
|---|---|
| `canonical-title-exact` | The complete query equals the canonical title after case and whitespace normalization; no punctuation or alias transformation was needed. |
| `canonical-title-normalized-exact` | The complete query equals the canonical title after compatibility, approved Latin-diacritic, punctuation-separator, and whitespace normalization. |
| `canonical-title-punctuation-alias` | The complete query equals the mandatory bounded punctuation-deletion title variant. |
| `roman-numeral-title-alias` | A bounded I-through-XX Roman/Arabic title variant supplied the match. |
| `original-title-exact` | The complete positive lexical query exactly equals the normalized original title. |
| `explicit-title-alias` | The complete positive lexical query exactly equals a validated explicit title alias. |
| `canonical-title-phrase` | A positive phrase matched adjacent canonical-title tokens, but the complete normalized strings were not equal. |
| `canonical-title-all-query-tokens` | Every positive query token matched the canonical title, although the full normalized strings were not equal. |
| `canonical-title-token` | At least one exact positive query token matched a canonical-title token. |
| `canonical-title-prefix` | At least one positive query token was a proper ordinal prefix of a canonical-title token. |
| `lexical-phrase-match` | A positive phrase matched adjacent tokens in the typed evidence field when no stronger field-specific evidence code applies. |
| `lexical-token-match` | A positive query token exactly matched an indexed token in the typed evidence field when no stronger field-specific evidence code applies. |
| `lexical-token-prefix` | A positive query token was a proper ordinal prefix of an indexed token in the typed evidence field when no stronger field-specific evidence code applies. |
| `fuzzy-title-edit-distance-1` | A title token matched at bounded edit distance one. |
| `fuzzy-title-edit-distance-2` | A title token of at least eight Unicode scalar values matched at bounded edit distance two. |
| `tag-exact` | A field-scoped value exactly matched a complete normalized tag without a lossy normalization difference. |
| `tag-exact-normalized` | A field-scoped value exactly matched a complete tag after compatibility or approved Latin-diacritic normalization changed the matching copy. |
| `tag-token-match` | A positive token exactly matched a tag token without matching the complete tag value. |
| `primary-lens-match` | A positive lens clause or lexical term matched the primary horror lens. |
| `secondary-lens-match` | A positive lens clause or lexical term matched the secondary horror lens. |
| `franchise-match` | A positive franchise clause matched validated franchise metadata. |
| `original-language-match` | A positive language clause matched validated original-language metadata. |
| `fuzzy-non-title-edit-distance-1` | A non-title lexical token matched at bounded edit distance one in the typed evidence field. |
| `fuzzy-non-title-edit-distance-2` | A non-title lexical token of at least eight Unicode scalar values matched at bounded edit distance two in the typed evidence field. |
| `all-positive-clauses-matched` | Every positive lexical clause matched at least one permitted searchable field. |
| `year-filter-satisfied` | The candidate satisfied every active release-year constraint. |
| `runtime-filter-satisfied` | The candidate satisfied every active runtime constraint. |
| `scare-filter-satisfied` | The candidate satisfied every active scare-intensity constraint. |
| `gore-filter-satisfied` | The candidate satisfied every active gore-intensity constraint. |
| `dread-filter-satisfied` | The candidate satisfied every active dread-intensity constraint. |
| `slasher-filter-satisfied` | The candidate satisfied every active slasher constraint. |
| `extreme-filter-satisfied` | The candidate satisfied every active extreme constraint. |
| `availability-unknown` | The candidate has no verified availability fact and therefore satisfied `availability:unknown`. |
| `availability-verified` | The candidate has at least one verified availability fact and satisfied `availability:verified`. |
| `availability-provider-match` | The candidate has a verified fact for the normalized provider named by the positive availability clause. |
| `status-unseen` | The candidate's application-supplied local status is unseen. |
| `status-watched` | The candidate's application-supplied local status is watched. |
| `status-interested` | The candidate's application-supplied local status is interested. |
| `status-not-now` | The candidate's application-supplied local status is not-now. |
| `status-disliked` | The candidate's application-supplied local status is disliked. |
| `exclusion-satisfied` | The candidate survived every exclusion clause; excluded values are not echoed. |

## Diagnostics and empty states

The query API returns a typed outcome:

- Success with results.
- Success with zero results.
- Invalid syntax.
- Unsupported field or value.
- Limit exceeded.
- Canceled.
- Local index unavailable.
- Online provider unavailable, separately from local success.

The UI must distinguish these states. A syntax error is never presented as “no films found.” Diagnostics identify a bounded character span and suggest valid syntax without exposing exception details.

Diagnostic and warning codes are versioned API values with the same stability and localization rules as evidence codes. A diagnostic span uses a zero-based UTF-16 start and non-negative length bounded by the validated raw query length. Invalid queries return no parsed partial query and no results.

### Stable diagnostic codes

| Diagnostic code | Outcome | Required meaning |
|---|---|---|
| `invalid-utf16` | Invalid syntax | The raw query contains an unpaired UTF-16 surrogate. |
| `control-character` | Invalid syntax | The raw query contains a prohibited control character. |
| `unsafe-format-character` | Invalid syntax | The raw query contains a Unicode `Format` character other than U+200C ZERO WIDTH NON-JOINER or U+200D ZERO WIDTH JOINER. |
| `query-too-long` | Limit exceeded | The raw query exceeds 512 UTF-16 code units. |
| `clause-limit-exceeded` | Limit exceeded | The total, positive, exclusion, or hard-facet clause budget was exceeded. |
| `phrase-limit-exceeded` | Limit exceeded | The quoted-phrase budget was exceeded. |
| `phrase-token-limit-exceeded` | Limit exceeded | A normalized phrase contains more than 12 tokens. |
| `token-too-long` | Limit exceeded | A normalized token exceeds 64 characters. |
| `fuzzy-term-limit-exceeded` | Limit exceeded | More than four query terms would require fuzzy retrieval. |
| `unknown-field` | Unsupported field or value | A field name is not part of grammar version 1. |
| `missing-value` | Invalid syntax | A field or comparison operator has no value. |
| `unterminated-quote` | Invalid syntax | A quoted phrase has no closing double quote. |
| `invalid-operator` | Invalid syntax | A field uses an operator not allowed by its value type. |
| `invalid-number` | Invalid syntax | A numeric field value is not a whole invariant-culture number. |
| `invalid-boolean` | Unsupported field or value | A Boolean field value is not exactly `true` or `false`, ignoring case. |
| `invalid-range` | Invalid syntax | A range is malformed or has a descending endpoint. |
| `value-out-of-range` | Unsupported field or value | A parsed value is outside the validated field domain. |
| `unsupported-value` | Unsupported field or value | A textual facet value is syntactically valid but unsupported by grammar version 1. |

### Stable warning codes

Warnings do not invalidate a query and cannot hide matching records.

| Warning code | Required meaning |
|---|---|
| `facet-no-coverage` | The requested conditional facet has zero validated local coverage; the result is truthfully empty. |
| `facet-no-verified-coverage` | The requested verified-availability facet has no verified local values; the result is truthfully empty. |

Zero-result guidance may suggest:

- Removing one active facet.
- Clearing exclusions.
- Trying a corrected title when a bounded high-confidence correction exists.
- Searching the disclosed online provider explicitly.

## Online database behavior

### Local enrichment

- Online metadata enrichment is opt-in at the provider level.
- Local results render before enrichment completes.
- Enrichment uses a stable provider ID when available; otherwise it uses bounded title/year identity matching and must reject ambiguity.
- Only the displayed result window may be enriched automatically, with bounded concurrency.
- Provider data is validated before caching or display.
- Provider data never changes local lexical rank during the active query unless the user explicitly refreshes after a complete, validated index rebuild.

### Explicit provider search

- Provider search appears as a separate command and result section.
- The UI identifies the provider before transmission.
- Provider results do not masquerade as curated local results.
- Importing or saving a provider result requires a validated identity and provenance record.
- Adult-content provider flags are disabled unless a future reviewed product decision explicitly enables them.

## Relevance acceptance metrics

The versioned corpus contains known-item, concept, facet, exclusion, typo, punctuation, pasted-title, Unicode-safety, missing-metadata, and malformed-query cases. Additive judgments keep schema version 1 but increment the top-level corpus revision; a structural shape change requires a schema-version change.

Promotion requires:

| Metric | Required threshold |
|---|---:|
| Exact normalized title top-1 accuracy | 100% |
| Known-item query mean reciprocal rank at 10 | 1.000 |
| Required-result recall at 10 | 100% |
| Forbidden-result rate | 0% |
| Hard-facet precision | 100% |
| Exclusion precision | 100% |
| Typo known-item top-3 accuracy | 100% |
| Normalized discounted cumulative gain at 10 | At least 0.95 |
| Invalid-query diagnostic accuracy | 100% |
| Unsupported/missing-metadata honesty cases | 100% |
| Repeated-order determinism | 100% across 100 runs |

Corpus changes require review and a corpus-revision increment. A catalog change that invalidates judgments must update the pinned catalog hash and explain each changed judgment rather than silently lowering thresholds.

## Performance and resource budgets

Initial promotion budgets use 50,000 validated synthetic candidates on a GitHub-hosted Windows runner after one warm-up. They may be tightened after reference-machine measurement but may not be removed or loosened silently.

| Operation | Time budget | Managed allocation budget |
|---|---:|---:|
| Build immutable index snapshot | 2,000 ms | 256 MiB |
| Exact-title warm query, p95 of 200 | 50 ms | 2 MiB per query |
| Typical multi-token warm query, p95 of 200 | 100 ms | 8 MiB per query |
| Complex phrase plus five facets, p95 of 100 | 200 ms | 12 MiB per query |
| Bounded typo query, p95 of 100 | 250 ms | 16 MiB per query |
| Filter-only query, p95 of 200 | 100 ms | 8 MiB per query |
| Cancellation observed after signal | 100 ms | No unbounded post-cancel growth |

Additional guarantees:

- No query blocks the WPF dispatcher while parsing, retrieving, scoring, or contacting a provider.
- Search-as-you-type uses a 200 ms debounce and cancels superseded work.
- Only the newest request version may publish results.
- A local query never waits for online enrichment.
- Online provider requests use a provider-specific total deadline no greater than 10 seconds, no more than two concurrent requests per provider, and bounded retry behavior.

## Accessibility and user experience

- `Ctrl+F` focuses the search box.
- `Escape` clears suggestions or dismisses search help before closing other modal UI.
- Query diagnostics are announced through a polite live region and associated with the search control.
- Active facets are keyboard-focusable, named chips with individual remove actions.
- Search help is reachable by keyboard and documents the supported grammar with examples.
- Result items expose title, year, match type, score label, watched status, and poster provenance to UI Automation.
- Empty, invalid, loading, offline, provider-failure, and result states are visually and programmatically distinct.
- High-contrast mode uses system colors and does not rely on color alone to communicate match or error state.

## Version-1 deferred capabilities

The following are intentionally deferred unless separately designed and measured:

- Arbitrary Boolean grouping and nested expressions.
- User-provided regular expressions or wildcards.
- Vector embeddings or cloud-hosted semantic search.
- Automatic transmission of queries to online services.
- Live streaming-provider claims without a reviewed provider and freshness policy.
- Language filtering inferred from tags.
- Automatic promotion of provider results into the curated catalog.
- Telemetry, cloud history sync, collaborative filtering, or sharing taste profiles.

Deferral is not a hidden failure: unavailable capabilities are documented and represented honestly in the UI.
