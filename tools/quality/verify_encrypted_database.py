#!/usr/bin/env python3
from __future__ import annotations

import argparse
import contextlib
import ctypes
import hashlib
import json
import msvcrt
import os
import stat
import subprocess
import tempfile
from pathlib import Path


SQLITE_HEADER = b"SQLite format 3\x00"
DEFAULT_SYNTHETIC_CANARY = b"HORRORHARBOR_SYNTHETIC_PRIVATE_CANARY"
CHUNK_SIZE = 1024 * 1024
SIDECAR_SUFFIXES = ("-wal", "-shm", "-journal")
MIGRATION_SUFFIX = ".encryption-migration"
REPLACEMENT_BACKUP_SUFFIX = ".encryption-replacement-backup"
RECOVERY_STATE_SUFFIX = ".encryption-recovery-state.json"
FILE_ATTRIBUTE_REPARSE_POINT = getattr(
    stat,
    "FILE_ATTRIBUTE_REPARSE_POINT",
    0x0400,
)
GENERIC_READ = 0x80000000
FILE_SHARE_READ = 0x00000001
OPEN_EXISTING = 3
FILE_ATTRIBUTE_NORMAL = 0x00000080
FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000
FILE_FLAG_SEQUENTIAL_SCAN = 0x08000000
FILE_ATTRIBUTE_TAG_INFO = 9


class UnsafeArtifactError(OSError):
    """Raised before reading when an artifact cannot be safely identity-bound."""


def exists_lexically(path: Path) -> bool:
    return os.path.lexists(path)


def is_link_or_reparse(path: Path) -> bool:
    metadata = path.lstat()
    return stat.S_ISLNK(metadata.st_mode) or bool(
        getattr(metadata, "st_file_attributes", 0)
        & FILE_ATTRIBUTE_REPARSE_POINT
    )


def has_link_or_reparse_component(path: Path) -> bool:
    current = Path(path.anchor)
    for part in path.parts[1:]:
        current /= part
        if exists_lexically(current) and is_link_or_reparse(current):
            return True
    return False


def open_locked_artifact(path: Path):
    if os.name != "nt":
        flags = os.O_RDONLY | getattr(os, "O_CLOEXEC", 0) | getattr(os, "O_NOFOLLOW", 0)
        descriptor = os.open(path, flags)
        return os.fdopen(descriptor, "rb", closefd=True)

    class FileAttributeTagInformation(ctypes.Structure):
        _fields_ = (
            ("file_attributes", ctypes.c_uint32),
            ("reparse_tag", ctypes.c_uint32),
        )

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    create_file = kernel32.CreateFileW
    create_file.argtypes = (
        ctypes.c_wchar_p,
        ctypes.c_uint32,
        ctypes.c_uint32,
        ctypes.c_void_p,
        ctypes.c_uint32,
        ctypes.c_uint32,
        ctypes.c_void_p,
    )
    create_file.restype = ctypes.c_void_p
    get_file_information = kernel32.GetFileInformationByHandleEx
    get_file_information.argtypes = (
        ctypes.c_void_p,
        ctypes.c_int,
        ctypes.c_void_p,
        ctypes.c_uint32,
    )
    get_file_information.restype = ctypes.c_int
    handle = create_file(
        str(path),
        GENERIC_READ,
        FILE_SHARE_READ,
        None,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL
        | FILE_FLAG_OPEN_REPARSE_POINT
        | FILE_FLAG_SEQUENTIAL_SCAN,
        None,
    )
    invalid_handle = ctypes.c_void_p(-1).value
    if handle == invalid_handle:
        error = ctypes.get_last_error()
        raise OSError(error, os.strerror(error), str(path))

    try:
        attributes = FileAttributeTagInformation()
        if not get_file_information(
            ctypes.c_void_p(handle),
            FILE_ATTRIBUTE_TAG_INFO,
            ctypes.byref(attributes),
            ctypes.sizeof(attributes),
        ):
            error = ctypes.get_last_error()
            raise OSError(error, os.strerror(error), str(path))
        if attributes.file_attributes & FILE_ATTRIBUTE_REPARSE_POINT:
            raise UnsafeArtifactError(
                f"Refusing to inspect reparse-point artifact: {path}"
            )
        descriptor = msvcrt.open_osfhandle(handle, os.O_RDONLY)
    except Exception:
        kernel32.CloseHandle(ctypes.c_void_p(handle))
        raise
    return os.fdopen(descriptor, "rb", closefd=True)


def inspect_open_artifact(
    path: Path,
    stream,
    canaries: tuple[bytes, ...],
    expected_identity,
) -> tuple[int, str, bool, bool, bool]:
    markers = (SQLITE_HEADER,) + canaries
    maximum_overlap = max(len(marker) for marker in markers) - 1
    digest = hashlib.sha256()
    previous = b""
    header_found = False
    canary_found = False
    size = 0
    lexical_before = path.lstat()
    opened_before = os.fstat(stream.fileno())
    if (
        is_link_or_reparse(path)
        or not os.path.samestat(expected_identity, lexical_before)
        or not os.path.samestat(lexical_before, opened_before)
    ):
        raise UnsafeArtifactError(
            f"Artifact identity changed before inspection: {path}"
        )
    stream.seek(0)
    while chunk := stream.read(CHUNK_SIZE):
        size += len(chunk)
        digest.update(chunk)
        combined = previous + chunk
        header_found = header_found or SQLITE_HEADER in combined
        canary_found = canary_found or any(
            canary in combined
            for canary in canaries
        )
        previous = combined[-maximum_overlap:] if maximum_overlap else b""
    opened_after = os.fstat(stream.fileno())
    try:
        lexical_after = path.lstat()
    except FileNotFoundError:
        lexical_after = None
    stable_identity = (
        lexical_after is not None
        and os.path.samestat(lexical_before, opened_before)
        and os.path.samestat(opened_before, opened_after)
        and os.path.samestat(opened_after, lexical_after)
        and opened_before.st_size == opened_after.st_size == size
        and opened_before.st_mtime_ns == opened_after.st_mtime_ns
        and opened_before.st_ctime_ns == opened_after.st_ctime_ns
    )
    return (
        size,
        digest.hexdigest().upper(),
        header_found,
        canary_found,
        stable_identity,
    )


def add_check(
    checks: list[dict[str, str]],
    condition: bool,
    name: str,
    detail: str,
) -> None:
    checks.append(
        {
            "name": name,
            "status": "PASS" if condition else "FAIL",
            "detail": detail,
        }
    )


def inspect(database: Path, canaries: tuple[bytes, ...]) -> dict:
    database = Path(os.path.abspath(os.fspath(database)))
    checks: list[dict[str, str]] = []
    artifacts: list[dict[str, str | int]] = []

    if os.name == "nt" and str(database).startswith("\\\\"):
        add_check(
            checks,
            False,
            "primary-local-path",
            "Remote and UNC database paths are rejected before metadata access.",
        )
        return build_report(checks, artifacts)

    primary_present = exists_lexically(database)
    primary_is_link_or_reparse = (
        is_link_or_reparse(database) if primary_present else False
    )
    path_has_link_or_reparse = has_link_or_reparse_component(database)
    primary_metadata = (
        database.lstat()
        if primary_present
        and not primary_is_link_or_reparse
        and not path_has_link_or_reparse
        else None
    )
    primary_exists = primary_metadata is not None
    primary_is_file = (
        primary_metadata is not None
        and stat.S_ISREG(primary_metadata.st_mode)
    )

    add_check(
        checks,
        primary_exists,
        "primary-exists",
        "The explicitly supplied primary database path exists.",
    )
    add_check(
        checks,
        primary_is_file,
        "primary-is-file",
        "The explicitly supplied primary database path is a file.",
    )
    add_check(
        checks,
        not primary_is_link_or_reparse,
        "primary-not-symlink",
        "The lexical primary database path is not a link or reparse point.",
    )
    add_check(
        checks,
        not path_has_link_or_reparse,
        "primary-path-no-link-or-reparse-components",
        "No existing component of the lexical primary path is a link or reparse point.",
    )

    if (
        not primary_exists
        or not primary_is_file
        or primary_is_link_or_reparse
        or path_has_link_or_reparse
    ):
        return build_report(checks, artifacts)

    primary_candidates = [("primary", database)] + [
        (suffix.removeprefix("-"), Path(str(database) + suffix))
        for suffix in SIDECAR_SUFFIXES
    ]
    migration_path = Path(str(database) + MIGRATION_SUFFIX)
    migration_candidates = [("migration", migration_path)] + [
        (
            f"migration-{suffix.removeprefix('-')}",
            Path(str(migration_path) + suffix),
        )
        for suffix in SIDECAR_SUFFIXES
    ]
    replacement_backup_path = Path(str(database) + REPLACEMENT_BACKUP_SUFFIX)
    replacement_backup_candidates = [
        ("replacement-backup", replacement_backup_path)
    ] + [
        (
            f"replacement-backup-{suffix.removeprefix('-')}",
            Path(str(replacement_backup_path) + suffix),
        )
        for suffix in SIDECAR_SUFFIXES
    ]
    recovery_state_candidates = [
        ("recovery-state", Path(str(database) + RECOVERY_STATE_SUFFIX))
    ]
    all_candidates = (
        primary_candidates
        + migration_candidates
        + replacement_backup_candidates
        + recovery_state_candidates
    )
    with contextlib.ExitStack() as stack:
        initial_identities = {
            path: path.lstat()
            for _, path in all_candidates
            if exists_lexically(path)
        }
        stale_migration_roles = [
            role
            for role, path in (
                migration_candidates
                + replacement_backup_candidates
                + recovery_state_candidates
            )
            if exists_lexically(path)
        ]
        add_check(
            checks,
            not stale_migration_roles,
            "no-stale-migration-artifact",
            (
                "No encryption-migration, replacement-backup, or recovery-state artifact "
                f"remains; observed count={len(stale_migration_roles)}."
            ),
        )

        locked_artifacts = {}
        for role, path in all_candidates:
            if not exists_lexically(path):
                if role != "primary":
                    artifacts.append({"role": role, "status": "ABSENT"})
                continue

            is_regular = (
                not is_link_or_reparse(path)
                and not has_link_or_reparse_component(path)
                and stat.S_ISREG(path.lstat().st_mode)
            )
            add_check(
                checks,
                is_regular,
                f"{role}-regular-file",
                f"The {role} artifact is a regular non-symbolic-link file.",
            )
            if is_regular:
                try:
                    locked_artifacts[(role, path)] = stack.enter_context(
                        open_locked_artifact(path)
                    )
                except (OSError, UnsafeArtifactError):
                    add_check(
                        checks,
                        False,
                        f"{role}-safe-open",
                        f"The {role} artifact could not be opened without following or changing identity.",
                    )
                    artifacts.append({"role": role, "status": "REJECTED"})

        for (role, path), stream in locked_artifacts.items():
            try:
                (
                    size,
                    artifact_hash,
                    header_found,
                    canary_found,
                    stable_identity,
                ) = inspect_open_artifact(
                    path,
                    stream,
                    canaries,
                    initial_identities[path],
                )
            except (OSError, UnsafeArtifactError):
                add_check(
                    checks,
                    False,
                    f"{role}-stable-identity",
                    f"The {role} artifact changed identity before content inspection.",
                )
                artifacts.append({"role": role, "status": "REJECTED"})
                continue
            add_check(
                checks,
                stable_identity,
                f"{role}-stable-identity",
                "The lexical path and opened file identity remained stable for the complete scan.",
            )
            add_check(checks, size > 0, f"{role}-nonempty", f"Observed byte length={size}.")
            add_check(
                checks,
                not header_found,
                f"{role}-no-sqlite-plaintext-header",
                f"SQLite plaintext header found={header_found}.",
            )
            add_check(
                checks,
                not canary_found,
                f"{role}-no-synthetic-canary",
                f"Synthetic canary found={canary_found}; canary values are never reported.",
            )
            artifacts.append(
                {
                    "role": role,
                    "status": "INSPECTED",
                    "bytes": size,
                    "sha256": artifact_hash,
                }
            )

        final_identities = {
            path: path.lstat()
            for _, path in all_candidates
            if exists_lexically(path)
        }
        aggregate_stable = (
            initial_identities.keys() == final_identities.keys()
            and all(
                os.path.samestat(initial_identities[path], final_identities[path])
                and initial_identities[path].st_size == final_identities[path].st_size
                and initial_identities[path].st_mtime_ns
                == final_identities[path].st_mtime_ns
                and initial_identities[path].st_ctime_ns
                == final_identities[path].st_ctime_ns
                for path in initial_identities
            )
        )
        add_check(
            checks,
            aggregate_stable,
            "artifact-set-stable-identity",
            "The complete locked artifact set remained unchanged through report construction.",
        )
        return build_report(checks, artifacts)


def build_report(
    checks: list[dict[str, str]],
    artifacts: list[dict[str, str | int]],
) -> dict:
    failed = [check for check in checks if check["status"] == "FAIL"]
    return {
        "policy": "HorrorHarbor keyless database opacity inspection v1",
        "status": "PASS" if not failed else "FAIL",
        "claim": (
            "PASS means inspected files lacked known plaintext markers. "
            "It does not independently prove cipher strength, key custody, or runtime behavior."
        ),
        "checks": len(checks),
        "passed": len(checks) - len(failed),
        "failed": len(failed),
        "artifacts": artifacts,
        "results": checks,
    }


def write_report(report: dict, report_path: Path | None) -> None:
    if report_path is not None:
        report_path.parent.mkdir(parents=True, exist_ok=True)
        report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(
        json.dumps(
            {
                key: report[key]
                for key in ("policy", "status", "checks", "passed", "failed")
            }
        )
    )


def run_self_test() -> int:
    with tempfile.TemporaryDirectory(prefix="horrorharbor-opacity-") as temporary:
        root = Path(temporary)
        opaque = root / "opaque.db"
        opaque.write_bytes(os.urandom(8192))
        passing = inspect(opaque, (DEFAULT_SYNTHETIC_CANARY,))
        if passing["status"] != "PASS":
            raise RuntimeError("The opaque synthetic fixture did not pass.")

        plaintext = root / "plaintext.db"
        plaintext.write_bytes(
            SQLITE_HEADER
            + os.urandom(128)
            + DEFAULT_SYNTHETIC_CANARY
            + os.urandom(128)
        )
        failing = inspect(plaintext, (DEFAULT_SYNTHETIC_CANARY,))
        if failing["status"] != "FAIL":
            raise RuntimeError("The plaintext synthetic fixture was not rejected.")

        stale = root / "stale.db"
        stale.write_bytes(os.urandom(8192))
        Path(str(stale) + ".encryption-migration").write_bytes(os.urandom(64))
        stale_result = inspect(stale, (DEFAULT_SYNTHETIC_CANARY,))
        if stale_result["status"] != "FAIL":
            raise RuntimeError("The stale migration fixture was not rejected.")

        replacement_backup_database = root / "replacement-backup.db"
        replacement_backup_database.write_bytes(os.urandom(8192))
        Path(
            str(replacement_backup_database) + REPLACEMENT_BACKUP_SUFFIX
        ).write_bytes(os.urandom(64))
        replacement_backup_result = inspect(
            replacement_backup_database,
            (DEFAULT_SYNTHETIC_CANARY,),
        )
        if replacement_backup_result["status"] != "FAIL":
            raise RuntimeError("The replacement-backup fixture was not rejected.")

        migration_wal_database = root / "migration-wal.db"
        migration_wal_database.write_bytes(os.urandom(8192))
        Path(
            str(migration_wal_database) + MIGRATION_SUFFIX + "-wal"
        ).write_bytes(
            SQLITE_HEADER
            + os.urandom(128)
            + DEFAULT_SYNTHETIC_CANARY
            + os.urandom(128)
        )
        migration_wal_result = inspect(
            migration_wal_database,
            (DEFAULT_SYNTHETIC_CANARY,),
        )
        migration_wal_checks = {
            check["name"]: check["status"]
            for check in migration_wal_result["results"]
        }
        if (
            migration_wal_result["status"] != "FAIL"
            or migration_wal_checks.get(
                "migration-wal-no-sqlite-plaintext-header"
            )
            != "FAIL"
            or migration_wal_checks.get("migration-wal-no-synthetic-canary")
            != "FAIL"
        ):
            raise RuntimeError(
                "The plaintext migration-WAL fixture was not fully inspected and rejected."
            )

        symlink_target = root / "symlink-target.db"
        symlink_target.write_bytes(os.urandom(8192))
        symlink_database = root / "symlink.db"
        expected_link_check = "primary-not-symlink"
        junction: Path | None = None
        try:
            symlink_database.symlink_to(symlink_target.name)
        except OSError as exception:
            if os.name != "nt" or getattr(exception, "winerror", None) != 1314:
                raise RuntimeError(
                    "The opacity self-test could not create its synthetic file symlink."
                ) from exception

            junction_target = root / "junction-target"
            junction_target.mkdir()
            (junction_target / symlink_target.name).write_bytes(os.urandom(8192))
            junction = root / "junction"
            completed = subprocess.run(
                [
                    "cmd.exe",
                    "/d",
                    "/c",
                    "mklink",
                    "/J",
                    str(junction),
                    str(junction_target),
                ],
                check=False,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
            if completed.returncode != 0:
                raise RuntimeError(
                    "The opacity self-test could not create its synthetic link fixture."
                ) from exception
            symlink_database = junction / symlink_target.name
            expected_link_check = "primary-path-no-link-or-reparse-components"

        try:
            symlink_result = inspect(
                symlink_database,
                (DEFAULT_SYNTHETIC_CANARY,),
            )
            symlink_checks = {
                check["name"]: check["status"]
                for check in symlink_result["results"]
            }
            if (
                symlink_result["status"] != "FAIL"
                or symlink_checks.get(expected_link_check) != "FAIL"
            ):
                raise RuntimeError(
                    "The linked primary fixture was not rejected."
                )
        finally:
            if junction is not None and exists_lexically(junction):
                junction.rmdir()

    print(
        '{"policy":"HorrorHarbor keyless database opacity self-test",'
        '"status":"PASS"}'
    )
    return 0


def encode_canaries(values: list[str]) -> tuple[bytes, ...]:
    encoded = [DEFAULT_SYNTHETIC_CANARY]
    for value in values:
        if not value:
            raise ValueError("Canary values must not be empty.")
        encoded.append(value.encode("utf-8"))
    return tuple(dict.fromkeys(encoded))


def main() -> int:
    parser = argparse.ArgumentParser(
        description=(
            "Perform a keyless, content-redacting opacity inspection of an explicitly "
            "supplied HorrorHarbor database and its SQLite sidecars."
        )
    )
    parser.add_argument("--database", help="Explicit database path; no default is provided.")
    parser.add_argument(
        "--canary",
        action="append",
        default=[],
        help="Additional synthetic marker to reject. Values are never printed.",
    )
    parser.add_argument("--report")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()

    if args.self_test:
        if args.database or args.canary or args.report:
            parser.error("--self-test cannot be combined with database inspection arguments.")
        return run_self_test()

    if not args.database:
        parser.error("--database is required unless --self-test is used.")

    database = Path(args.database)
    report = inspect(database, encode_canaries(args.canary))
    report_path = None if args.report is None else Path(args.report)
    write_report(report, report_path)
    return 0 if report["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
