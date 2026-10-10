"""Verify pinned Python source selections without installing or executing target code."""

import argparse
from collections import Counter
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import tokenize

import source_facts
from calibration_preview import candidate, membership_discounts


REPOSITORY = Path(__file__).resolve().parents[2]


def git(root, *args):
    return subprocess.check_output(["git", *args], cwd=root, text=True).strip()


def digest(data):
    return hashlib.sha256(data).hexdigest()


def write(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")


def preview(facts_file):
    run = subprocess.run(["node", str(REPOSITORY / "shared/source-facts/preview.mjs"), str(facts_file)], capture_output=True, text=True, timeout=120)
    if run.returncode:
        raise RuntimeError(run.stderr or run.stdout)
    return json.loads(run.stdout)["population"]


def reference_population(reference, root, document):
    previous = reference.new_document()
    discounts = {}
    previous["scope"]["description"] = "Same selected and successfully parsed files, with the initial adapter's declaration population."
    for file in document["selection"]["files"]:
        if file["disposition"] == "analyzed":
            with tokenize.open(root / file["file"]) as stream:
                source = stream.read()
            previous["functions"].extend(reference.measure_source(source, file["file"]))
            discounts.update(membership_discounts(source, file["file"]))
    # Independently implemented offline candidate must exactly explain every
    # retained body's change from the trusted original analyzer.
    adjusted, changes = candidate(previous, "membership", discounts)
    by_id = {item["id"]: item for item in adjusted["functions"]}
    for item in document["functions"]:
        assert item["complexity"] == by_id[item["id"]]["complexity"], item
    current_ids = {item["id"] for item in document["functions"]}
    excluded_ids = {item["id"] for item in document["selection"]["excludedFunctions"]}
    assert current_ids.isdisjoint(excluded_ids)
    assert current_ids | excluded_ids == set(by_id), "Every previous owner must be accounted for once."
    return previous, [item for item in changes if item["id"] in current_ids]


def sample(root, entry, output, reference):
    assert git(root, "rev-parse", "HEAD") == entry["revision"], f"Revision drift: {root}"
    assert not git(root, "status", "--porcelain"), f"Working tree drift: {root}"
    destination = output / entry["id"]
    destination.mkdir(parents=True, exist_ok=True)
    document = source_facts.analyze(root, include=entry["include"], production_paths=entry["productionPaths"])
    write(destination / "source-facts.json", document)
    if document["status"] != "complete":
        raise RuntimeError(f"{entry['id']}: {document['diagnostics']}")
    prior, changes = reference_population(reference, root, document)
    write(destination / "reference-facts.json", prior)
    current_score = preview(destination / "source-facts.json")
    prior_score = preview(destination / "reference-facts.json")
    selection = document["selection"]
    owners = sorted(document["functions"], key=lambda item: (-item["complexity"]["value"], item["id"]))
    source_hash = digest(json.dumps([(item["file"], item["contentSha256"]) for item in selection["files"] if item["contentSha256"]], separators=(",", ":")).encode())
    function_hash = digest(json.dumps(sorted(document["functions"], key=lambda item: item["id"]), sort_keys=True, separators=(",", ":")).encode())
    declaration_hash = digest(json.dumps(selection["excludedFunctions"], sort_keys=True, separators=(",", ":")).encode())
    return {**entry, "configurationFingerprint": selection["configurationFingerprint"], "selectedSourceSha256": source_hash,
            "status": document["status"], "filesObserved": len(selection["files"]),
            "filesAnalyzed": sum(item["disposition"] == "analyzed" for item in selection["files"]),
            "excludedFileReasons": dict(Counter(item["reason"] for item in selection["files"] if item["disposition"] == "excluded")),
            "implementedFunctions": len(owners), "functionFactsSha256": function_hash, "excludedDeclarationsSha256": declaration_hash,
            "priorFunctionsInSameFiles": len(prior["functions"]),
            "excludedDeclarationReasons": dict(Counter(item["reason"] for item in selection["excludedFunctions"])),
            "declarationExamples": selection["excludedFunctions"][:6], "unchangedComplexityForEveryRetainedOwner": not changes,
            "matchesReviewedMatchPolicy": True, "complexityChanges": changes,
            "everyPriorOwnerAccountedForOnce": True,
            "complexityAtLeast10": sum(item["complexity"]["value"] >= 10 for item in owners),
            "maxComplexity": owners[0]["complexity"]["value"] if owners else None,
            "experimentalPreview": current_score["score"] if current_score else None,
            "priorExperimentalPreviewSameFiles": prior_score["score"] if prior_score else None,
            "topFunctions": owners[:5]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repos", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--reference-analyzer", type=Path, required=True, help="Trusted original source_facts.py from e91af93")
    parser.add_argument("--record", action="store_true", help="Explicitly record reviewed population and score-preview baselines")
    args = parser.parse_args()
    manifest = json.loads((REPOSITORY / "shared/calibration/corpus/python-selection.json").read_text())
    expected = subprocess.check_output(["git", "show", f"{manifest['referenceAnalyzerRevision']}:analyzers/python/source_facts.py"], cwd=REPOSITORY).replace(b"\r\n", b"\n")
    actual = args.reference_analyzer.read_bytes().replace(b"\r\n", b"\n")
    if actual != expected:
        raise RuntimeError("Reference analyzer must match the pinned repository revision.")
    spec = importlib.util.spec_from_file_location("reference_source_facts", args.reference_analyzer)
    reference = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(reference)
    args.output.mkdir(parents=True, exist_ok=True)
    code_files = [Path(__file__), Path(source_facts.__file__), Path(__file__).with_name("source_selection.py"),
                  Path(__file__).with_name("calibration_preview.py")]
    analyzer_hash = digest(b"\0".join(path.name.encode() + b"\0" + path.read_bytes().replace(b"\r\n", b"\n") for path in code_files))
    report = {"schemaVersion": 1, "measurementPolicy": source_facts.POLICY, "selectionPolicy": "python/source-selection-v1",
              "analyzerSourceSha256": analyzer_hash, "referenceAnalyzerRevision": manifest["referenceAnalyzerRevision"],
              "scope": manifest["scope"], "samples": []}
    for entry in manifest["samples"]:
        result = sample(args.repos / entry["repository"], entry, args.output, reference)
        report["samples"].append(result)
        print(entry["id"], json.dumps({key: result[key] for key in ("filesAnalyzed", "implementedFunctions", "excludedDeclarationReasons", "maxComplexity", "experimentalPreview")}))
    write(args.output / "summary.json", report)
    fields = ("id", "revision", "configurationFingerprint", "selectedSourceSha256", "filesAnalyzed", "implementedFunctions",
              "functionFactsSha256", "excludedDeclarationsSha256", "excludedDeclarationReasons", "complexityAtLeast10",
              "maxComplexity", "experimentalPreview")
    baseline = {"measurementPolicy": source_facts.POLICY, "scope": manifest["scope"],
                "samples": [{key: item[key] for key in fields} for item in report["samples"]]}
    baseline_path = REPOSITORY / "shared/calibration/baselines/python-selection.json"
    if args.record:
        write(baseline_path, baseline)
        print("Reviewed baseline recorded; previews remain experimental.")
    elif not baseline_path.exists() or json.loads(baseline_path.read_text()) != baseline:
        raise RuntimeError("Python corpus baseline drift; inspect the report before recording a reviewed policy change.")
    else:
        print("Pinned Python population and complexity baselines passed.")


if __name__ == "__main__":
    main()
