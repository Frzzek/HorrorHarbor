# Release Status

This status records the encrypted-storage source release merged through
protected pull request #5 on 2026-07-29. The exact pull-request head and resulting
public `main` tree passed the required quality and CodeQL checks. Nothing here
approves an unsigned binary or installer.

| Gate | Status |
| --- | --- |
| Public `main` revision | PASS — protected pull request #5 merged as `827e767`; its tree matches the exact validated candidate tree |
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
| GitHub quality workflow | PASS — exact pull-request head and resulting public `main` commit passed |
| CodeQL | PASS — exact pull-request head and resulting public `main` commit passed |
| GitHub secret scanning and public-ref re-audit | PASS — zero open secret, code-scanning, or Dependabot alerts; no unexpected public or pull refs |
| Live TMDB provider acceptance | DEFERRED — requires a user-supplied authorized credential |
| Signed binary and installer promotion | NOT APPROVED |

**Promotion status:** the reviewed source is published on protected `main`.
Executables and installers remain unsigned development artifacts and are not
publisher-verified public releases.

Generated artwork is accepted as a clearly labeled fallback and is not
represented as official or bespoke studio key art.
