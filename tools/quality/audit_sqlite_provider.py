#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import os
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path


EXPECTED_CENTRAL_PACKAGES = {
    "Microsoft.Data.Sqlite.Core": "10.0.10",
    "SQLite3MC.PCLRaw.bundle": "2.3.6",
}

REQUIRED_ENCRYPTION_TESTS = (
    "NewDatabaseIsOpaqueAndRoundTripsOnlyWithTheProtectedKey",
    "WrongKeyIsRejectedWithoutChangingTheEncryptedDatabase",
    "MissingKeyIsRejectedWithoutSilentlyGeneratingAReplacement",
    "PlaintextVersionOneDatabaseMigratesAtomicallyAndPreservesRows",
    "UnknownPlaintextSchemaIsRejectedAndOriginalFileIsPreserved",
    "PlaintextSchemaDriftIsRejectedWithoutDiscardingUnknownColumnData",
    "SqliteLikePrefixSchemaBypassIsRejectedWithoutDiscardingUnknownTableData",
    "ConcurrentInitializationIsIdempotent",
    "ConcurrentProcessesInitializeOneEncryptedDatabaseWithOneProtectedKey",
    "ConcurrentPlaintextWriterCannotLoseCommittedRowsDuringMigration",
    "CanceledInitializationCreatesNeitherDatabaseNorKey",
    "StaleMigrationArtifactsAreRemovedBeforeMigration",
    "ReplacementFailurePreservesReadablePlaintextOriginal",
    "PathReplacementRaceRestoresConcurrentlyCommittedPlaintextDatabase",
    "PostInstallPathReplacementIsPreservedWithoutUnsafeRollback",
    "InterruptedCompletedMigrationIsReconciledWithoutLeavingPlaintextBackup",
    "BusyPlaintextWalFailsWithoutLosingCommittedData",
)

REQUIRED_DPAPI_TESTS = (
    "ProtectedKeyRoundTripsWithoutPersistingPlaintext",
    "MalformedProtectedFileIsRejected",
    "ConcurrentCreationHasOneWinnerAndPreservesItsKey",
    "InvalidKeyLengthIsRejectedWithoutWritingAFile",
    "CanceledSaveLeavesNoKeyOrTemporaryFile",
    "PostPublicationDurabilityFailureIsNotMisclassifiedAsContention",
)

FORBIDDEN_PACKAGE_PREFIXES = (
    "Microsoft.Data.Sqlite",
    "SQLitePCLRaw.bundle_",
    "SQLitePCLRaw.lib.",
    "SQLitePCLRaw.provider.",
    "SQLitePCLRaw.provider.dynamic",
    "SQLitePCLRaw.provider.sqlite3",
    "System.Data.SQLite",
)

GENERATED_DIRECTORY_NAMES = frozenset(
    {".git", ".vs", "artifacts", "bin", "obj", "testresults"}
)


def authored_files(root: Path, suffixes: frozenset[str]) -> list[Path]:
    result: list[Path] = []
    for directory, directory_names, file_names in os.walk(root):
        directory_names[:] = sorted(
            name
            for name in directory_names
            if name.casefold() not in GENERATED_DIRECTORY_NAMES
        )
        for file_name in sorted(file_names):
            path = Path(directory, file_name)
            if path.suffix.casefold() in suffixes:
                result.append(path)
    return result


def package_versions(path: Path) -> dict[str, list[str]]:
    root = ET.parse(path).getroot()
    result: dict[str, list[str]] = {}
    for node in root.findall(".//PackageVersion"):
        package = node.attrib.get("Include", "")
        version = node.attrib.get("Version", "")
        result.setdefault(package, []).append(version)
    return result


def package_references(paths: list[Path], root: Path) -> dict[str, list[str]]:
    result: dict[str, list[str]] = {}
    for path in paths:
        project = ET.parse(path).getroot()
        for node in project.findall(".//PackageReference"):
            package = node.attrib.get("Include", "")
            result.setdefault(package, []).append(path.relative_to(root).as_posix())
    return result


def add_check(
    results: list[dict[str, str]],
    condition: bool,
    name: str,
    detail: str,
) -> None:
    results.append(
        {
            "name": name,
            "status": "PASS" if condition else "FAIL",
            "detail": detail,
        }
    )


def is_forbidden(package: str) -> bool:
    normalized = package.casefold()
    allowed = {
        expected.casefold()
        for expected in EXPECTED_CENTRAL_PACKAGES
    }
    if normalized in allowed:
        return False
    return any(
        normalized.startswith(prefix.casefold())
        for prefix in FORBIDDEN_PACKAGE_PREFIXES
    )


def package_values(
    packages: dict[str, list[str]],
    expected_package: str,
) -> list[str]:
    expected = expected_package.casefold()
    return [
        value
        for package, values in packages.items()
        if package.casefold() == expected
        for value in values
    ]


def audit(root: Path) -> dict:
    results: list[dict[str, str]] = []
    central_path = root / "Directory.Packages.props"
    data_project_path = root / "src/HorrorHarbor.Data/HorrorHarbor.Data.csproj"

    required_paths = (
        central_path,
        data_project_path,
    )
    for path in required_paths:
        add_check(
            results,
            path.is_file(),
            f"required:{path.relative_to(root).as_posix()}",
            "Required encrypted-storage policy artifact exists.",
        )

    if not all(path.is_file() for path in required_paths):
        return build_report(results)

    versions = package_versions(central_path)
    references = package_references(
        authored_files(root, frozenset({".csproj", ".props", ".targets"})),
        root,
    )

    for package, expected_version in EXPECTED_CENTRAL_PACKAGES.items():
        observed_versions = package_values(versions, package)
        add_check(
            results,
            observed_versions == [expected_version],
            f"package-version:{package}",
            f"Expected={expected_version}; observed={observed_versions}",
        )

    forbidden_versions = sorted(package for package in versions if is_forbidden(package))
    forbidden_references = sorted(package for package in references if is_forbidden(package))
    add_check(
        results,
        not forbidden_versions,
        "no-forbidden-central-sqlite-packages",
        f"Forbidden central packages={forbidden_versions}",
    )
    add_check(
        results,
        not forbidden_references,
        "no-forbidden-sqlite-references",
        f"Forbidden package references={forbidden_references}",
    )

    data_root = ET.parse(data_project_path).getroot()
    data_references = sorted(
        node.attrib.get("Include", "").casefold()
        for node in data_root.findall(".//PackageReference")
        if "sqlite" in node.attrib.get("Include", "").casefold()
    )
    expected_data_references = sorted(
        package.casefold()
        for package in EXPECTED_CENTRAL_PACKAGES
    )
    add_check(
        results,
        data_references == expected_data_references,
        "data-project-provider-set",
        f"Expected={expected_data_references}; observed={data_references}",
    )

    encrypted_bundle_references = [
        location
        for package, locations in references.items()
        if package.casefold() == "sqlite3mc.pclraw.bundle"
        for location in locations
    ]
    expected_bundle_location = data_project_path.relative_to(root).as_posix()
    add_check(
        results,
        encrypted_bundle_references == [expected_bundle_location],
        "single-encrypted-bundle-owner",
        f"Observed={encrypted_bundle_references}",
    )

    return build_report(results)


def build_report(results: list[dict[str, str]]) -> dict:
    failed = [result for result in results if result["status"] == "FAIL"]
    return {
        "policy": "HorrorHarbor encrypted SQLite dependency composition policy v2",
        "status": "PASS" if not failed else "FAIL",
        "checks": len(results),
        "passed": len(results) - len(failed),
        "failed": len(failed),
        "results": results,
    }


def write_report(report: dict, path: Path | None) -> None:
    if path is not None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(
        json.dumps(
            {
                key: report[key]
                for key in ("policy", "status", "checks", "passed", "failed")
            }
        )
    )


def run_self_test() -> int:
    with tempfile.TemporaryDirectory(prefix="horrorharbor-sqlite-policy-") as temporary:
        root = Path(temporary)
        required_files = {
            "Directory.Packages.props": """
                <Project><ItemGroup>
                  <PackageVersion Include="microsoft.data.sqlite.core" Version="10.0.10" />
                  <PackageVersion Include="sqlite3mc.pclraw.bundle" Version="2.3.6" />
                </ItemGroup></Project>
            """,
            "src/HorrorHarbor.Data/HorrorHarbor.Data.csproj": """
                <Project><ItemGroup>
                  <PackageReference Include="MICROSOFT.DATA.SQLITE.CORE" />
                  <PackageReference Include="SQLite3MC.PCLRaw.Bundle" />
                </ItemGroup></Project>
            """,
        }
        for relative, content in required_files.items():
            path = root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")

        passing = audit(root)
        if passing["status"] != "PASS":
            raise RuntimeError("The compliant synthetic provider policy fixture did not pass.")

        central = root / "Directory.Packages.props"
        central.write_text(
            central.read_text(encoding="utf-8").replace(
                "</ItemGroup>",
                '<PackageVersion Include="sqlitepclraw.BuNdLe_E_SQLite3" Version="2.1.12" />'
                "</ItemGroup>",
            ),
            encoding="utf-8",
        )
        failing = audit(root)
        if failing["status"] != "FAIL":
            raise RuntimeError("The forbidden ordinary SQLite fixture was not rejected.")

    print('{"policy":"HorrorHarbor encrypted SQLite dependency composition self-test","status":"PASS"}')
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Audit HorrorHarbor's encrypted SQLite provider and keying policy."
    )
    parser.add_argument("--root", default=".")
    parser.add_argument("--report")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()

    if args.self_test:
        return run_self_test()

    root = Path(args.root).resolve()
    report = audit(root)
    report_path = None if args.report is None else root / args.report
    write_report(report, report_path)
    return 0 if report["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
