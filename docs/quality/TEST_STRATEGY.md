# Test Strategy

- **Unit tests:** title normalization, deterministic scoring, watched exclusion, invalid weights, duplicate IDs, taste-profile sanitization.
- **Integration tests:** encrypted SQLite creation and round-trip, keyless/wrong-key/missing-key rejection, atomic version-1 plaintext migration, unknown-schema preservation, production DPAPI key round-trip/corruption/concurrency/cancellation behavior, repository behavior, and catalog loading.
- **Security tests:** malformed and hostile catalog fixtures, unexpected JSON properties, invalid ranges, traversal-like candidate IDs, corrupt feedback quarantine.
- **Performance gate:** 50,000-candidate ranking stress test with explicit time and allocation budgets.
- **Smoke gate:** fixed canonical counts, score reconciliation, explanation presence, and user-confirmed watched exclusion.
- **Coverage:** unit tests require at least 80% line coverage in CI. Integration and security coverage is recorded separately and reviewed rather than gamed.
- **Storage-policy tools:** provider-policy fixtures reject ordinary/duplicate SQLite bundles and enforce non-pooling, explicit cipher/keying, DI, and test invariants; keyless opacity fixtures reject plaintext headers, synthetic canaries, and stale migration artifacts without loading a key or inspecting a personal database.
