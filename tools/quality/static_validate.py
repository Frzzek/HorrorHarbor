#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from collections.abc import Iterator
from pathlib import Path


GENERATED_DIRECTORY_NAMES = frozenset(
    {".git", ".vs", "artifacts", "bin", "obj", "testresults"}
)
POST_PUBLISH_PROJECT = (
    "tools/quality/PublishedStorageProbe/"
    "HorrorHarbor.PublishedStorageProbe.csproj"
)


def authored_files(root: Path, suffixes: frozenset[str] | None = None) -> Iterator[Path]:
    for directory, directory_names, file_names in os.walk(root):
        directory_names[:] = sorted(
            name
            for name in directory_names
            if name.casefold() not in GENERATED_DIRECTORY_NAMES
        )
        for file_name in sorted(file_names):
            path = Path(directory, file_name)
            if suffixes is None or path.suffix.casefold() in suffixes:
                yield path


def check(condition: bool, name: str, detail: str, results: list[dict]) -> None:
    results.append({"name": name, "status": "PASS" if condition else "FAIL", "detail": detail})


def markdown_table_codes(document: str, heading: str) -> set[str]:
    start = document.find(heading)
    if start < 0:
        return set()

    following_heading = re.search(r"(?m)^#{1,3} ", document[start + len(heading):])
    end = len(document) if following_heading is None else start + len(heading) + following_heading.start()
    return set(re.findall(r"(?m)^\| `([a-z0-9-]+)` \|", document[start:end]))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=".")
    parser.add_argument("--report", default="artifacts/validation/static-validation.json")
    args = parser.parse_args()
    root = Path(args.root).resolve()
    results: list[dict] = []

    required = [
        "HorrorHarbor.sln",
        "Directory.Build.props",
        "Directory.Packages.props",
        "data/catalog/recommendation_catalog.json",
        "tests/HorrorHarbor.UnitTests/HorrorHarbor.UnitTests.csproj",
        "tests/HorrorHarbor.IntegrationTests/HorrorHarbor.IntegrationTests.csproj",
        "tests/HorrorHarbor.SecurityTests/HorrorHarbor.SecurityTests.csproj",
        "tests/HorrorHarbor.PerformanceTests/HorrorHarbor.PerformanceTests.csproj",
    ]
    for rel in required:
        check((root / rel).is_file(), f"required:{rel}", "Required artifact exists.", results)

    xml_files = list(authored_files(root, frozenset({".csproj", ".props", ".targets", ".xaml"})))
    for path in xml_files:
        try:
            ET.parse(path)
            ok, detail = True, "XML parsed."
        except Exception as exc:
            ok, detail = False, str(exc)
        check(ok, f"xml:{path.relative_to(root)}", detail, results)

    json_files = [
        path
        for path in authored_files(root, frozenset({".json"}))
        if "fixtures/malicious/catalog-malformed" not in path.as_posix()
    ]
    for path in json_files:
        try:
            json.loads(path.read_text(encoding="utf-8"))
            ok, detail = True, "JSON parsed."
        except Exception as exc:
            ok, detail = False, str(exc)
        check(ok, f"json:{path.relative_to(root)}", detail, results)

    catalog = json.loads((root / "data/catalog/recommendation_catalog.json").read_text(encoding="utf-8"))
    ids = [item.get("candidateId", "").casefold() for item in catalog]
    identities = [(item.get("title", "").casefold(), item.get("releaseYear")) for item in catalog]
    check(len(catalog) == 116, "catalog-count", f"Observed {len(catalog)} candidates.", results)
    check(len(ids) == len(set(ids)), "catalog-unique-ids", "Candidate IDs are unique.", results)
    check(len(identities) == len(set(identities)), "catalog-unique-identities", "Title/year identities are unique.", results)
    valid_ranges = all(
        0 <= item.get(field, -1) <= 5
        for item in catalog
        for field in ("scareIntensity", "goreIntensity", "dreadIntensity")
    ) and all(
        0 <= item.get(field, -1) <= 1
        for item in catalog
        for field in ("discoveryScore", "qualityScore", "availabilityScore")
    )
    check(valid_ranges, "catalog-ranges", "Intensity and score ranges are valid.", results)

    bundled_databases = [
        path.relative_to(root).as_posix()
        for path in authored_files(root, frozenset({".db", ".sqlite", ".sqlite3"}))
    ]
    check(
        not bundled_databases,
        "privacy-no-bundled-databases",
        f"Authored source-tree database candidates={bundled_databases}",
        results,
    )

    sln = (root / "HorrorHarbor.sln").read_text(encoding="utf-8")
    project_files = list(authored_files(root, frozenset({".csproj"})))
    missing_from_solution = sorted(
        p.relative_to(root).as_posix() for p in project_files if p.name not in sln
    )
    unexpected_missing_projects = [
        path for path in missing_from_solution if path != POST_PUBLISH_PROJECT
    ]
    check(
        not unexpected_missing_projects,
        "solution-project-coverage",
        f"UnexpectedMissing={unexpected_missing_projects}",
        results,
    )
    check(
        missing_from_solution == [POST_PUBLISH_PROJECT],
        "post-publish-probe-boundary",
        f"ExpectedNonSolutionProject={POST_PUBLISH_PROJECT}; Missing={missing_from_solution}",
        results,
    )

    search_root = root / "src/HorrorHarbor.Search"
    search_project_path = search_root / "HorrorHarbor.Search.csproj"
    search_project = ET.parse(search_project_path).getroot()
    target_frameworks = [node.text for node in search_project.findall(".//TargetFramework")]
    check(target_frameworks == ["net10.0"], "search-target-framework", f"Observed={target_frameworks}", results)

    search_references = [
        node.attrib.get("Include", "").replace("\\", "/")
        for node in search_project.findall(".//ProjectReference")
    ]
    expected_search_reference = "../HorrorHarbor.Core/HorrorHarbor.Core.csproj"
    check(
        search_references == [expected_search_reference],
        "search-project-boundary",
        f"ProjectReferences={search_references}",
        results,
    )
    package_references = [node.attrib.get("Include", "") for node in search_project.findall(".//PackageReference")]
    framework_references = [node.attrib.get("Include", "") for node in search_project.findall(".//FrameworkReference")]
    check(
        not package_references and not framework_references,
        "search-external-dependencies",
        f"PackageReferences={package_references}; FrameworkReferences={framework_references}",
        results,
    )

    search_source = "\n".join(
        path.read_text(encoding="utf-8", errors="ignore")
        for path in authored_files(search_root, frozenset({".cs"}))
    )
    forbidden_search_dependencies = {
        token
        for token in (
            "HorrorHarbor.App",
            "HorrorHarbor.Data",
            "HorrorHarbor.Posters",
            "HorrorHarbor.Recommendations",
            "Microsoft.Data.Sqlite",
            "Microsoft.Extensions",
            "System.IO",
            "System.Net",
            "System.Windows",
        )
        if token in search_source
    }
    check(
        not forbidden_search_dependencies,
        "search-forbidden-namespaces",
        f"Hits={sorted(forbidden_search_dependencies)}",
        results,
    )

    acceptance_path = root / "tests/fixtures/search/horror-search-acceptance-v1.json"
    acceptance = json.loads(acceptance_path.read_text(encoding="utf-8"))
    check(
        acceptance.get("schemaVersion") == 1 and acceptance.get("corpusRevision") == 5,
        "search-corpus-version",
        f"schemaVersion={acceptance.get('schemaVersion')}; corpusRevision={acceptance.get('corpusRevision')}",
        results,
    )
    authored_hash = acceptance.get("catalog", {}).get("sha256AtAuthoring", "")
    catalog_text = (root / "data/catalog/recommendation_catalog.json").read_text(encoding="utf-8")
    canonical_catalog_bytes = catalog_text.replace("\r\n", "\n").replace("\r", "\n").encode("utf-8")
    current_hash = hashlib.sha256(canonical_catalog_bytes).hexdigest().upper()
    authored_count = acceptance.get("catalog", {}).get("candidateCount")
    check(
        authored_hash == current_hash and authored_count == len(catalog),
        "search-corpus-catalog-pin",
        f"hashMatches={authored_hash == current_hash}; authoredCount={authored_count}; actualCount={len(catalog)}",
        results,
    )
    case_ids = [case.get("id", "") for case in acceptance.get("cases", [])]
    check(
        bool(case_ids) and all(case_ids) and len(case_ids) == len(set(case_ids)),
        "search-corpus-case-identities",
        f"Cases={len(case_ids)}; unique={len(set(case_ids))}; blank={case_ids.count('')}",
        results,
    )

    requirements = (root / "docs/quality/HORROR_SEARCH_ENGINE_REQUIREMENTS.md").read_text(encoding="utf-8")
    evidence_contract = markdown_table_codes(requirements, "### Stable evidence codes")
    diagnostic_contract = markdown_table_codes(requirements, "### Stable diagnostic codes")
    warning_contract = markdown_table_codes(requirements, "### Stable warning codes")
    corpus_evidence = {
        code
        for case in acceptance.get("cases", [])
        for code in case.get("requiredEvidenceCodes", [])
    }
    corpus_diagnostics = {
        case["expectedDiagnosticCode"]
        for case in acceptance.get("cases", [])
        if "expectedDiagnosticCode" in case
    }
    corpus_warnings = {
        code
        for case in acceptance.get("cases", [])
        for code in case.get("expectedWarningCodes", [])
    }
    unknown_corpus_codes = sorted(
        (corpus_evidence - evidence_contract) |
        (corpus_diagnostics - diagnostic_contract) |
        (corpus_warnings - warning_contract)
    )
    check(
        not unknown_corpus_codes,
        "search-corpus-code-contract",
        f"Unknown={unknown_corpus_codes}",
        results,
    )

    diagnostic_source = (search_root / "Querying/SearchDiagnostic.cs").read_text(encoding="utf-8")
    implemented_diagnostics = set(re.findall(r'=> "([a-z0-9-]+)"', diagnostic_source))
    check(
        implemented_diagnostics == diagnostic_contract,
        "search-diagnostic-implementation-contract",
        f"Missing={sorted(diagnostic_contract - implemented_diagnostics)}; Extra={sorted(implemented_diagnostics - diagnostic_contract)}",
        results,
    )

    evidence_source = (search_root / "Contracts/SearchEvidence.cs").read_text(encoding="utf-8")
    implemented_evidence = set(re.findall(r'=> "([a-z0-9-]+)"', evidence_source))
    check(
        implemented_evidence == evidence_contract,
        "search-evidence-implementation-contract",
        f"Missing={sorted(evidence_contract - implemented_evidence)}; Extra={sorted(implemented_evidence - evidence_contract)}",
        results,
    )

    secret_patterns = [
        re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
        re.compile(r"(?i)(api[_-]?key|client[_-]?secret|access[_-]?token)\s*[:=]\s*[\"'][^\"']{12,}[\"']"),
        re.compile(r"(?i)password\s*[:=]\s*[\"'][^\"']+[\"']"),
    ]
    secret_hits = []
    for base in (root / "src", root / "tests", root / "eng"):
        for path in authored_files(base):
            if path.is_file() and path.suffix.lower() not in {".db", ".png", ".zip"}:
                text = path.read_text(encoding="utf-8", errors="ignore")
                if any(pattern.search(text) for pattern in secret_patterns):
                    secret_hits.append(path.relative_to(root).as_posix())
    check(not secret_hits, "secret-scan", f"Hits={secret_hits}", results)

    cs_text = "\n".join(
        path.read_text(encoding="utf-8", errors="ignore")
        for path in authored_files(root, frozenset({".cs"}))
    )
    check('CommandText = $"' not in cs_text and "CommandText = $@\"" not in cs_text, "sql-interpolation", "No interpolated SQL command text found.", results)
    check("Recommendation launch failed: \" + ex.Message" not in cs_text, "safe-ui-errors", "Raw exception messages are not exposed by the view model.", results)
    strict_props = (root / "Directory.Build.props").read_text(encoding="utf-8")
    check("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>" in strict_props, "warnings-as-errors", "Compiler warnings are release-blocking.", results)
    check("<NuGetAuditMode>all</NuGetAuditMode>" in strict_props, "transitive-audit", "Transitive package audit is enabled.", results)

    failed = [result for result in results if result["status"] == "FAIL"]
    report = {
        "release": "HorrorHarbor Alpha 2 Enforcement",
        "status": "PASS" if not failed else "FAIL",
        "checks": len(results),
        "passed": len(results) - len(failed),
        "failed": len(failed),
        "results": results,
    }
    report_path = root / args.report
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({k: report[k] for k in ("release", "status", "checks", "passed", "failed")}))
    return 0 if not failed else 1


if __name__ == "__main__":
    raise SystemExit(main())
