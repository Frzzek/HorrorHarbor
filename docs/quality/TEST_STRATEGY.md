# Test Strategy

- **Unit tests:** title normalization, deterministic scoring, watched exclusion, invalid weights, duplicate IDs, taste-profile sanitization.
- **Integration tests:** canonical SQLite integrity, read-only enforcement, repository behavior, catalog loading.
- **Security tests:** malformed and hostile catalog fixtures, unexpected JSON properties, invalid ranges, traversal-like candidate IDs, corrupt feedback quarantine.
- **Performance gate:** 50,000-candidate ranking stress test with explicit time and allocation budgets.
- **Smoke gate:** fixed canonical counts, score reconciliation, explanation presence, and user-confirmed watched exclusion.
- **Coverage:** unit tests require at least 80% line coverage in CI. Integration and security coverage is recorded separately and reviewed rather than gamed.
