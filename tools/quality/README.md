# HorrorHarbor quality tools

These tools encode release-blocking policies that are easy to weaken accidentally
during dependency upgrades, storage changes, and incident investigation. They are
safe to run in CI and use only the Python standard library.

## Encrypted SQLite provider policy

`audit_sqlite_provider.py` verifies the authored source policy for encrypted local
storage. It requires the approved `Microsoft.Data.Sqlite.Core` and
`SQLite3MC.PCLRaw.bundle` versions, rejects ordinary or duplicate SQLite bundles,
and checks non-pooling, private cache use, explicit ChaCha20 selection, zero
plaintext-header configuration, binary keying, DPAPI registration, and required
test contracts.

Run its synthetic positive/negative fixtures:

```powershell
python tools/quality/audit_sqlite_provider.py --self-test
```

Audit the repository and write machine-readable evidence:

```powershell
python tools/quality/audit_sqlite_provider.py `
  --root . `
  --report artifacts/validation/sqlite-provider-policy.json
```

The tool intentionally pins reviewed package versions. A dependency upgrade must
include package-signature, license/notice, native-runtime, compatibility, security,
and encrypted-storage regression evidence before updating the policy.

## Keyless database opacity inspection

`verify_encrypted_database.py` performs a read-only, content-redacting inspection
of an explicitly supplied database and its `-wal`, `-shm`, and `-journal`
sidecars. It rejects:

- The plaintext SQLite file header.
- The built-in synthetic private-data canary.
- Additional caller-supplied synthetic canaries.
- Symbolic links and non-regular artifacts.
- A stale `.encryption-migration` file.

Run only its synthetic fixtures:

```powershell
python tools/quality/verify_encrypted_database.py --self-test
```

Inspect a deliberately selected synthetic test database:

```powershell
python tools/quality/verify_encrypted_database.py `
  --database artifacts/synthetic-user-data/user-data.db `
  --canary HORRORHARBOR_SYNTHETIC_PRIVATE_CANARY `
  --report artifacts/validation/synthetic-database-opacity.json
```

Safety properties:

- There is no default database path.
- The tool does not discover or inspect `%LOCALAPPDATA%`.
- It never requests, accepts, derives, or prints a database key.
- It never prints file contents or canary values.
- It streams inspection and hashes rather than loading an entire database.

A passing result means only that the inspected files lacked known plaintext
markers. It does **not** independently prove cipher strength, correct key custody,
successful keyed reads, authenticated decryption, or runtime failure behavior.
Those claims require the integration tests and provider-policy audit.

## Exact-payload encrypted-storage runtime probe

`verify-published-storage.ps1` verifies the actual self-contained Windows
payload after `verify-publish.ps1` accepts its inventory. It compiles a small
non-shipping probe against the published assemblies, copies the candidate into
a disposable directory, hash-compares every required runtime file, and then
executes only the cloned payload.

The probe uses explicit synthetic database and DPAPI-key paths beneath ignored
`artifacts/validation/` storage. It never resolves or inspects HorrorHarbor's
default `%LOCALAPPDATA%` paths. It proves:

- the expected app, data, Microsoft.Data.Sqlite, SQLitePCLRaw, and SQLite3MC
  assemblies load from the candidate clone;
- the copied app, data, and native-provider hashes match the verified payload;
- current-user DPAPI key creation and round-trip work;
- a new database is opaque and rejects a keyless schema read;
- an exact supported plaintext version-1 fixture migrates and preserves its
  synthetic row;
- the migrated database is opaque and leaves no migration sidecar behind.

Safe invocation:

```powershell
./tools/quality/verify-published-storage.ps1 `
  -PublishDirectory ./artifacts/publish/win-x64 `
  -ReportPath artifacts/validation/published-storage-runtime-win-x64.json
```

The probe and all fixture data are removed in `finally`; only a content-free
validation summary remains. The probe project is deliberately outside the
shipping solution and is never included in the application payload.

## Static validation

`static_validate.py` validates authored XML/JSON, solution boundaries, catalog and
search contracts, privacy exclusions, basic secret patterns, analyzer policy, and
other release invariants. Generated directories are excluded deliberately.

```powershell
python tools/quality/static_validate.py `
  --root . `
  --report artifacts/validation/static-validation.json
```

## Lessons retained from provider evaluation

1. **Do not claim encryption merely because a package accepts a password.** Verify
   the loaded native provider, cipher selection, plaintext-header behavior,
   keyless/wrong-key failure, sidecars, and correct-key round-trip.
2. **Disable SQLite connection pooling for keyed connections.** A pooled native
   handle can retain key state and file locks beyond managed connection disposal.
3. **Key an attached migration database before its first page is written.** An
   already encrypted file cannot be attached by a plaintext source connection
   before that attached schema is keyed.
4. **A read/write source connection may not create a missing attached file.**
   Pre-create a zero-length, same-directory file, then attach, configure, and key
   it before schema creation.
5. **Do not use backup APIs across incompatible plaintext/encrypted providers.**
   Copy the owned schema and rows transactionally, then independently reopen and
   verify the encrypted destination before atomic replacement.
6. **Do not create disposable package probes casually inside a repository using
   central package management.** `dotnet add package` in a nested probe can update
   the repository-level `Directory.Packages.props`. Explicitly disable central
   package management for a disposable probe or isolate it outside the repository.
7. **Keep experiments under ignored artifact storage and commit only reusable
   policy.** Package archives, extracted native binaries, upstream source clones,
   and synthetic databases are evidence inputs—not source-release payloads.
8. **Never test an inspector against a real user database by default.** Require an
   explicit path and prefer self-contained synthetic fixtures.

## Release-gate relationship

The canonical local gate and GitHub quality workflow run:

1. Static validation.
2. Provider-policy self-test.
3. Live provider-policy audit with JSON evidence.
4. Keyless opacity-tool self-test.
5. Source/dependency security, formatting, build, tests, and performance.
6. Publish privacy and exact-payload encrypted-storage runtime verification.

The opacity tool self-test belongs in automation. Inspection of a real personal
database does not; release evidence must use synthetic fixtures only.
