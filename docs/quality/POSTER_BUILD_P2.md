# Poster Build P2 — Generated Art Acceptance Record

## Decision

Build P2 is accepted as the production generated-fallback artwork system for the current Windows application. It replaces the earlier repetitive moon, fog, and silhouette placeholder renderer with deterministic, metadata-directed compositions rendered through SkiaSharp.

This acceptance does **not** classify generated fallbacks as official film posters or bespoke studio key art. The application labels them `GENERATED FALLBACK`, preserves provenance, and continues to prefer confidently matched official artwork when that provider is configured.

## Architecture

- Renderer: SkiaSharp 4.150.1 with matching Windows native assets.
- Render version: 3. Advancing the version invalidates stale generated cache entries.
- Inputs: title identity, release year, primary and secondary lens, tags, franchise, poster tone, and scare/gore/dread intensity.
- Determinism: stable cache identity and seeded visual variation; identical descriptors and dimensions produce identical PNG bytes.
- Output: validated PNG artwork with in-image title typography.
- Composition families: Slasher, Occult, Psychological, Body Horror, Cosmic, Creature, Isolation, Found Footage, Gothic, Home Invasion, Horror Comedy, and a General fallback.
- Era direction: Grindhouse, Analog, Millennium, and Contemporary.
- Typography direction: Condensed, Monumental, Editorial, Distressed, and Technical.

## Catalog-wide evidence

The production-resolution audit rendered all 116 catalog titles at 480 by 720 pixels.

| Metric | Result |
|---|---:|
| Catalog entries | 116 |
| Valid generated fallbacks | 116 |
| Failures | 0 |
| Complete provenance records | 116 |
| Official artwork in offline audit | 0 |
| Catalog-represented composition families | 11 |
| Semantic style combinations | 45 |
| Unique perceptual hashes | 103 |
| Exact duplicate groups | 0 |
| Near-duplicate pairs | 332 |
| Human-review contact sheets | 6 |
| Evaluation corpus | 100 |

The evidence is stored under `artifacts/poster-audit/p2`. Its `summary.json` is the machine-readable authority, while `index.html` and the contact sheets support human review.

## Visual review and iteration

The first P2 visual pass exposed four material weaknesses:

1. Excess dead space beneath one-line titles.
2. Occult `threshold` metadata paired with generic sigil imagery.
3. Tag-first classification that overrepresented Body Horror and suppressed authoritative lens families.
4. Horror Comedy imagery that appeared too benign.

Renderer version 3 corrected those findings by moving the art/title split, adding a footer rule and selection label, introducing threshold-specific haunted architecture, prioritizing the primary lens while retaining the Found Footage semantic override, and adding crossed-eye/fang details to Horror Comedy.

The final six-sheet review found readable titles, no observed title clipping, no exact duplicate images, and visibly distinct family grammars. Same-family motifs remain intentionally recognizable and are the principal accepted limitation of local generated art.

## Engineering verification

The complete Windows 11 quality and publish gate passed after the P2 changes:

- Static validation: 177 of 177.
- Release build: zero warnings and zero errors.
- Unit tests: 51 of 51.
- Integration tests: 15 of 15.
- Security tests: 12 of 12.
- Canonical self-test: pass.
- NuGet vulnerability audit: pass.
- Formatting and analyzer verification: pass.
- P2 poster audit: 116 of 116 valid.
- Performance gate: 302.283 milliseconds against an 8,000 millisecond budget.
- Allocation gate: 119,332,472 bytes against a 536,870,912 byte budget.
- Poster assembly coverage: 88.41% line, 65.24% branch, and 93.36% method.
- Fresh self-contained ReadyToRun `win-x64` publish: pass.

## Published-application acceptance

The fresh published application was launched from `artifacts/publish/win-x64/HorrorHarbor.exe` and observed for 20 seconds.

- Process remained responsive.
- The WPF window exposed 149 UI Automation descendants.
- Twelve recommendations loaded from 116 curated candidates.
- Dark application resources composed correctly.
- The 154 by 228 recommendation-card crop displayed generated posters without clipping.
- In-image typography remained readable at card size.
- `GENERATED FALLBACK` source badges were visible.
- The final capture is `artifacts/runtime/p2-app-diagnostic-20s.png`.

An earlier white screenshot was a transient capture/compositor race. It was not reproduced during the longer probe; the application had a populated automation tree, remained responsive, produced no local error report, and rendered correctly in the final capture. No startup or theme code change was warranted.

## Accepted boundary and deferred work

P2 is accepted as a substantial local fallback improvement, not as a substitute for licensed official art. The following remain separate future work:

- Live TMDB provider acceptance with user-supplied credentials.
- User-selected artwork as the highest-priority source.
- Plex artwork ingestion ahead of provider and generated sources.
- Further within-family motif expansion to reduce recognizable grammar reuse.
- Installer rebuilding only after a separately requested release/installer gate.

No TMDB token was required for P2 acceptance, and no installer was rebuilt as part of this work.
