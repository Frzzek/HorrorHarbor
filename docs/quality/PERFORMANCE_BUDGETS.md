# Performance Budgets

The Alpha 2 ranking gate processes 50,000 validated candidates and returns 30 diversified results.

- Maximum elapsed time: 8,000 ms on a GitHub-hosted Windows runner.
- Maximum managed allocation: 512 MiB.
- Result count: exactly 30.

Budgets are intentionally conservative until a Windows 11 reference machine is measured. Future changes may tighten them but may not silently remove the gate.

## Search normalization guard

## Full-catalog horror search

The Release performance gate builds and queries a deterministic 50,000-document
synthetic horror catalog. The enforced budgets are:

- Search-index construction: no more than 12,000 ms.
- Search-index construction allocation: no more than 1 GiB.
- Representative advanced query: no more than 500 ms.
- Retained result window: exactly 30 results.

The representative query combines lexical retrieval, a year comparison, an
intensity facet, and an exclusion. Results are written to
`artifacts/performance/performance-report.json` by the quality gate.

The performance executable also measures `SearchTextNormalizerV1` independently from recommendation ranking. The corpus cycles through eight representative ASCII, punctuation, Latin-diacritic, compatibility-form, whitespace, and supplementary-plane inputs for 500,000 operations after 1,000,000 unmeasured warm-up operations. The extended warm-up allows .NET tiered profile-guided optimization to reach steady state before measurement.

- Maximum elapsed time: 500 ms in a Release quality-gate run.
- Maximum managed allocation: 400 bytes per operation on the measuring thread.
- The checksum must consume every result so the measured calls cannot be removed as dead work.

The July 2026 simplification review measured approximately 307 bytes per operation after the refactor, versus approximately 719–731 bytes per operation before it. Elapsed time remains machine- and JIT-sensitive; allocation is the primary regression signal. This microbenchmark does not replace the separate 50,000-document search-index and query budgets in the search-engine requirements.
