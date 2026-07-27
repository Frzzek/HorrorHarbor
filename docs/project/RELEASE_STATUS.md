# Release Status

| Gate | Status |
|---|---|
| Canonical data integrity | PASS — SQLite integrity, foreign keys, and baseline counts |
| Catalog schema/range/identity validation | PASS — 116 candidates, unique IDs and title/year identities |
| Static validation | PASS — 177/177 |
| Strict compiler/analyzers | PASS — Release build, zero warnings and errors |
| Formatting | PASS |
| Unit tests | PASS — 51/51 |
| Integration tests | PASS — 15/15 |
| Security tests and malicious fixtures | PASS — 12/12 |
| Canonical self-test | PASS |
| Poster assembly coverage | PASS — 88.41% line, 65.24% branch, 93.36% method |
| Performance and allocation | PASS — 302.283 ms and 119,332,472 bytes within budget |
| Dependency vulnerability audit | PASS |
| Poster Build P2 audit | PASS — 116/116, zero failures, complete provenance |
| WPF runtime launch | PASS — responsive, 149 automation descendants, 12 recommendations |
| Recommendation-card artwork crop | PASS — 154×228 crop, readable titles/badges, no clipping observed |
| Windows x64 publish | PASS — self-contained ReadyToRun publish |
| CodeQL | Configured; CI execution remains separate |
| Live TMDB provider acceptance | DEFERRED — requires user-supplied credentials |
| Installer rebuild for P2 | NOT PERFORMED |

**Promotion status: P2 application publish accepted; installer promotion remains separate.**

Generated artwork is accepted as a labeled fallback, not represented as official or bespoke studio key art. Detailed evidence and the accepted quality boundary are recorded in `docs/quality/POSTER_BUILD_P2.md`; the runtime capture is `artifacts/runtime/p2-app-diagnostic-20s.png`.
