#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path

from audit_sqlite_provider import REQUIRED_DPAPI_TESTS, REQUIRED_ENCRYPTION_TESTS


TEST_CLASSES = {
    "HorrorHarbor.IntegrationTests.UserDataEncryptionIntegrationTests": tuple(
        name
        for name in REQUIRED_ENCRYPTION_TESTS
        if not name.startswith("ConcurrentProcesses")
        and name != "ConcurrentPlaintextWriterCannotLoseCommittedRowsDuringMigration"
    ),
    "HorrorHarbor.IntegrationTests.UserDataMultiprocessIntegrationTests": (
        "ConcurrentProcessesInitializeOneEncryptedDatabaseWithOneProtectedKey",
        "ConcurrentPlaintextWriterCannotLoseCommittedRowsDuringMigration",
    ),
    "HorrorHarbor.IntegrationTests.DpapiUserDataEncryptionKeyProviderTests": (
        REQUIRED_DPAPI_TESTS
    ),
}


def local_name(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def verify(trx_path: Path) -> dict:
    root = ET.parse(trx_path).getroot()
    definitions: dict[str, tuple[str, str]] = {}
    for node in root.iter():
        if local_name(node.tag) != "UnitTest":
            continue
        test_id = node.attrib.get("id", "")
        name = node.attrib.get("name", "")
        method = next(
            (
                child
                for child in node.iter()
                if local_name(child.tag) == "TestMethod"
            ),
            None,
        )
        if not test_id or method is None:
            continue
        definitions[test_id] = (
            method.attrib.get("className", ""),
            method.attrib.get("name", name),
        )

    outcomes: dict[str, list[str]] = {}
    for node in root.iter():
        if local_name(node.tag) != "UnitTestResult":
            continue
        test_id = node.attrib.get("testId", "")
        identity = definitions.get(test_id)
        if identity is None:
            continue
        class_name, method_name = identity
        fully_qualified_name = f"{class_name}.{method_name}"
        outcomes.setdefault(fully_qualified_name, []).append(
            node.attrib.get("outcome", "")
        )

    expected = {
        f"{class_name}.{method_name}"
        for class_name, methods in TEST_CLASSES.items()
        for method_name in methods
    }
    missing = sorted(expected - outcomes.keys())
    duplicate = sorted(
        name for name in expected if len(outcomes.get(name, ())) != 1
    )
    not_passed = sorted(
        name
        for name in expected
        if outcomes.get(name) != ["Passed"]
    )
    passed = not missing and not duplicate and not not_passed
    return {
        "policy": "HorrorHarbor compiled encrypted-storage test evidence v1",
        "status": "PASS" if passed else "FAIL",
        "required": len(expected),
        "passed": len(expected) if passed else len(expected) - len(set(missing + not_passed)),
        "missing": missing,
        "duplicate": duplicate,
        "notPassed": not_passed,
    }


def write_synthetic_trx(path: Path, omit: str | None = None) -> None:
    test_run = ET.Element("TestRun")
    definitions = ET.SubElement(test_run, "TestDefinitions")
    results = ET.SubElement(test_run, "Results")
    index = 0
    for class_name, methods in TEST_CLASSES.items():
        for method_name in methods:
            if method_name == omit:
                continue
            index += 1
            test_id = f"test-{index}"
            unit_test = ET.SubElement(
                definitions,
                "UnitTest",
                {"id": test_id, "name": method_name},
            )
            ET.SubElement(
                unit_test,
                "TestMethod",
                {"className": class_name, "name": method_name},
            )
            ET.SubElement(
                results,
                "UnitTestResult",
                {"testId": test_id, "testName": method_name, "outcome": "Passed"},
            )
    ET.ElementTree(test_run).write(path, encoding="utf-8", xml_declaration=True)


def self_test() -> int:
    with tempfile.TemporaryDirectory(prefix="horrorharbor-test-evidence-") as temporary:
        root = Path(temporary)
        passing_path = root / "passing.trx"
        write_synthetic_trx(passing_path)
        if verify(passing_path)["status"] != "PASS":
            raise RuntimeError("Compliant compiled test evidence did not pass.")

        failing_path = root / "failing.trx"
        write_synthetic_trx(failing_path, omit=REQUIRED_DPAPI_TESTS[0])
        if verify(failing_path)["status"] != "FAIL":
            raise RuntimeError("Missing compiled test evidence was not rejected.")

    print(
        json.dumps(
            {
                "policy": "HorrorHarbor compiled encrypted-storage test evidence self-test",
                "status": "PASS",
            },
            separators=(",", ":"),
        )
    )
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Require exact passing compiled encrypted-storage test identities in TRX evidence."
    )
    parser.add_argument("--trx")
    parser.add_argument("--report")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()

    if args.self_test:
        return self_test()
    if not args.trx:
        parser.error("--trx is required unless --self-test is used")

    report = verify(Path(args.trx))
    if args.report:
        report_path = Path(args.report)
        report_path.parent.mkdir(parents=True, exist_ok=True)
        report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(
        json.dumps(
            {
                key: report[key]
                for key in ("policy", "status", "required", "passed")
            }
        )
    )
    return 0 if report["status"] == "PASS" else 1


if __name__ == "__main__":
    raise SystemExit(main())
