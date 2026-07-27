# Poster Build P0 — baseline evidence and findings

**Status:** Complete  
**Technical evidence gate:** PASS  
**Production poster quality:** NOT ACCEPTED  

## Purpose

Build P0 establishes an honest, reproducible baseline before official-provider integration or a new generated-art system is designed. The baseline attempts every catalog title, records failures rather than hiding them, hashes every successful poster, computes perceptual fingerprints, creates a deterministic 100-title review corpus, and publishes human-readable contact sheets.

The generated evidence is rooted at `artifacts/poster-audit/p0`. Open `index.html` for the review index and `contact-sheets/page-001.svg` through `page-006.svg` for the catalog-wide visual audit.

## Reproduction contract

Run from the repository root:

```text
dotnet run --project src/HorrorHarbor.Tools.PosterAudit -c Release -- --catalog data/catalog/recommendation_catalog.json --output artifacts/poster-audit/p0
```

The command deletes its output directory before rendering. This guarantees a cold, contamination-free baseline. Poster bytes, SHA-256 values, perceptual hashes, corpus membership, and contact-sheet ordering are deterministic for a fixed catalog and renderer version. Render durations and UTC generation time are environmental evidence and are not byte-reproducible.

The complete engineering gate also executes the P0 audit through `eng/quality-gate.ps1`.

## Observed baseline

The accepted P0 run used catalog SHA-256 `8E4065F56E14257BD015E39098D6FAE935C2DDF89A28280C6FF2C096FEF23109` and renderer version 1.

| Measure | Result |
|---|---:|
| Catalog titles attempted | 116 |
| Valid PNG posters generated | 116 |
| Failures | 0 |
| Evaluation-corpus titles | 100 |
| Contact-sheet pages | 6 |
| Exact SHA-256 duplicate groups | 0 |
| Near-duplicate dHash pairs at distance ≤ 4 | 0 |
| Unique perceptual hashes | 116 |
| Total poster bytes | 9,710,396 |
| Cold-cache generated assets | 116 |
| Cold-cache reused assets | 0 |

Timing is retained in `summary.json` and should be interpreted as machine-specific evidence, not a stable acceptance threshold.

## Human visual review

The contact sheet confirms the user's concern. The current renderer produces technically valid atmospheric placeholders, not finished posters.

Observed limitations:

1. Every composition relies on the same moon, fog, ridge, and central silhouette or occult-symbol vocabulary.
2. Palette and geometry vary, but art direction does not meaningfully communicate title-specific story, location, era, cast, creature, or subgenre.
3. The image contains no title typography, hierarchy, credits, release context, or provider identity.
4. Franchise entries and remakes are not visually coherent as families and are not clearly differentiated from one another.
5. The lack of exact or low-distance dHash duplicates proves byte/shape variation only; it does not contradict the obvious semantic repetition visible in the contact sheets.
6. No image has official-art provenance, attribution, provider identifiers, rights metadata, or a match-confidence record.

Therefore, P0 passes technical completeness but fails production-art readiness. These placeholders remain an offline last-resort fallback only.

## Evidence files

- `manifest.json`: complete summary, cache metrics, posters, failures, similarity pairs, and corpus.
- `summary.json`: compact run-level metrics and reproduction command.
- `cache.json`: cold-cache generation/reuse metrics.
- `posters.json`: title-level paths, SHA-256, dHash, bytes, render duration, and corpus reasons.
- `failures.json`: explicit per-title failures; empty in the accepted baseline.
- `similarities.json`: exact and dHash-near duplicate pairs.
- `evaluation-corpus.json`: deterministic review selection and reason tags, including lenses, eras, franchises, duplicate titles, extreme/slasher coverage, and language-market signals available in catalog tags.
- `contact-sheets/*.svg`: paginated visual evidence with title, year, lens, and corpus markers.
- `index.html`: human review index.

## Build P1 entry criteria

Build P1 may begin only with these constraints accepted:

1. **Stable identity schema:** introduce original title, release date/year, original language, alternate titles, TMDB ID, Plex GUID/rating key, and identity source.
2. **Ambiguity-safe matching:** automatic matches require a documented confidence threshold using normalized title, year tolerance, media type, and language. Ambiguous results must not silently select artwork.
3. **Provenance schema:** every artwork asset records source kind, provider, provider asset path/ID, retrieval time, rights/terms reference, attribution text, match method, confidence, and content SHA-256.
4. **Source priority contract:** user-selected artwork, then securely matched Plex artwork, then official provider artwork, then generated fallback.
5. **Credential boundary:** provider secrets use Windows Credential Manager and never enter catalogs, logs, manifests, image URLs, or exception messages.
6. **Network boundary:** HTTPS only, provider-host allowlisting, redirect revalidation, bounded response sizes, timeouts, cancellation, and conservative retries.
7. **Image safety boundary:** encoded-byte, pixel, dimension, chunk, and decompression bounds are enforced before an asset is accepted.
8. **Cache lifecycle:** provenance-aware content addressing, expiry/revalidation, offline reuse, corruption recovery, and bounded cleanup are specified and tested.
9. **Quality evidence continuity:** provider matching and fallback changes must continue to emit catalog-wide manifests, failures, similarity reports, and contact sheets; technical PASS must remain distinct from human visual acceptance.
10. **No provider claim without terms review:** TMDB or another provider is not enabled in production until its current API terms, attribution requirements, image-base configuration, and intended application use have been documented.

P1 is an identity-and-provenance build. It must not jump directly to downloading images by title string.
