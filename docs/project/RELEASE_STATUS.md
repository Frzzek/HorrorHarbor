# Release Status

This status distinguishes the public `main` revision from the encrypted-storage
candidate validated locally on 2026-07-29 and submitted through protected pull
request #5. The prior exact pull-request head passed GitHub quality and CodeQL
checks, but the candidate has not been merged. Every updated exact head requires
fresh checks. Nothing here approves an unsigned binary or installer.

| Gate | Status |
| --- | --- |
| Public `main` revision | UNCHANGED — encrypted-storage candidate is in protected pull request #5, not merged or represented as public `main` |
| Local candidate source-tree privacy | PASS — no bundled watch-history database, credentials, provider cache, logs, or private diagnostics |
| Static validation | PASS — 60/60 checks |
| Formatting | PASS — `dotnet format --verify-no-changes` |
| Compiler and analyzers | PASS — Release build with zero warnings and errors |
| Dependency and source security scan | PASS — transitive NuGet audit and candidate-source pattern scan |
| Encrypted SQLite dependency composition | PASS — 8/8 checks |
| Unit tests and coverage | PASS — 222 tests; 84.15% line coverage against an 80% threshold |
| Integration tests | PASS — 60/60 tests |
| Compiled encrypted-storage evidence | PASS — all 23 required exact test identities passed once |
| Security tests | PASS — 29/29 tests |
| Privacy-neutral self-test | PASS — 116 candidates and zero bundled watch-history events |
| Recommendation performance | PASS — 272.359 ms and 119,128,904 allocated bytes for 50,000 candidates |
| Search performance | PASS — 980.986 ms index build and 162.496 ms query budget gate |
| Poster audit | PASS — 116 generated fallbacks, zero failures, zero exact duplicate groups, and complete provenance |
| Windows x64 publish | PASS — self-contained ReadyToRun payload; exact 440-file inventory verified |
| Published encrypted-storage runtime | PASS — exact source/candidate/runtime payload, encrypted initialization, plaintext migration, keyless rejection, opacity, and provider identity verified using synthetic fixtures only |
| Independent blocker review | PASS — no remaining BLOCKER or HIGH finding |
| GitHub quality workflow for this candidate | REQUIRED — prior pull-request head passed; the updated exact head must pass before merge |
| CodeQL for this candidate | REQUIRED — prior pull-request head passed; the updated exact head must pass before merge |
| GitHub secret scanning and public-ref re-audit | PENDING — required after candidate publication and merge |
| Live TMDB provider acceptance | DEFERRED — requires a user-supplied authorized credential |
| Signed binary and installer promotion | NOT APPROVED |

**Promotion status:** local candidate verification is accepted as a prerequisite
to pull-request review. Public source promotion remains blocked until the exact
candidate passes required GitHub `quality-gate` and CodeQL `analyze` checks and
is merged through protected `main`. Executables and installers remain unsigned
development artifacts and are not publisher-verified public releases.

Generated artwork is accepted as a clearly labeled fallback and is not
represented as official or bespoke studio key art.
