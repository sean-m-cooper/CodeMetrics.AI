"""Reproduce the pinned Python complexity calibration study without executing target code."""

import argparse
import ast
from collections import Counter
import hashlib
import json
from pathlib import Path
import subprocess
import tokenize

import source_facts
from calibration_preview import candidate, membership_discounts
from corpus import REPOSITORY, digest, git, write


def policy(facts, destination):
    write(destination, facts)
    result = subprocess.run(["node", str(Path(__file__).with_name("calibration-preview.mjs")), str(destination)],
                            capture_output=True, text=True, timeout=120)
    if result.returncode:
        raise RuntimeError(result.stderr or result.stdout)
    return json.loads(result.stdout)


def band(cc):
    return next(label for maximum, label in [(3, "1-3"), (5, "4-5"), (10, "6-10"),
                                            (20, "11-20"), (40, "21-40"), (float("inf"), "41+")] if cc <= maximum)


def review_sources(root, entry, document, reviews):
    discounts = {}
    reviewed = []
    selected = [review for review in reviews if review["project"] == entry["id"]]
    for record in document["selection"]["files"]:
        if record["disposition"] != "analyzed":
            continue
        with tokenize.open(root / record["file"]) as handle:
            source = handle.read()
        if hashlib.sha256(source.encode("utf-8")).hexdigest() != record["contentSha256"]:
            raise RuntimeError("Source changed during calibration: " + record["file"])
        discounts.update(membership_discounts(source, record["file"]))
        relevant = [review for review in selected if review["file"] == record["file"]]
        if not relevant:
            continue
        nodes = {node.lineno: node for node in ast.walk(ast.parse(source)) if isinstance(node, source_facts.OWNERS)}
        for review in relevant:
            function = next(item for item in document["functions"] if item["file"] == review["file"] and item["member"] == review["member"])
            if function["complexity"]["value"] != review["cc"]:
                raise RuntimeError("Reviewed complexity drift: " + review["member"])
            node = nodes[function["line"]]
            text = "\n".join(source.splitlines()[node.lineno - 1:node.end_lineno])
            reviewed.append({**review, "line": function["line"], "endLine": node.end_lineno, "bodySha256": digest(text.encode()),
                             "decisions": function["complexity"]["decisions"],
                             "candidateCc": review["cc"] - discounts.get(function["id"], 0),
                             "source": f"{entry['url']}/blob/{entry['revision']}/{review['file']}#L{function['line']}"})
    if len(reviewed) != len(selected):
        raise RuntimeError("Missing reviewed source bodies.")
    return discounts, reviewed


def sample(entry, baseline, root, destination, reviews):
    if git(root, "rev-parse", "HEAD") != entry["revision"] or git(root, "status", "--porcelain"):
        raise RuntimeError("Pinned clean checkout required: " + str(root))
    document = source_facts.analyze(root, include=entry["include"], production_paths=entry["productionPaths"])
    if document["status"] != "complete":
        raise RuntimeError(str(document["diagnostics"]))
    measured = digest(json.dumps(sorted(document["functions"], key=lambda item: item["id"]), sort_keys=True, separators=(",", ":")).encode())
    if measured != baseline["functionFactsSha256"] or document["selection"]["configurationFingerprint"] != baseline["configurationFingerprint"]:
        raise RuntimeError("Production population/measurement differs from the frozen baseline.")
    destination.mkdir(parents=True, exist_ok=True)
    current = policy(document, destination / "current.json")
    if current["score"] != baseline["experimentalPreview"]:
        raise RuntimeError("Production preview drift.")
    discounts, reviewed = review_sources(root, entry, document, reviews)
    experiments = {}
    for mode in ["membership", "without-except", "without-comprehensions"]:
        proposed, changes = candidate(document, mode, discounts)
        experiments[mode] = {**policy(proposed, destination / f"{mode}.json"), "changedFunctions": len(changes),
                             "changes": changes if mode == "membership" else None,
                             "interpretation": "candidate" if mode == "membership" else "rejected ablation; not a proposed rule"}
    functions = document["functions"]
    counts = Counter()
    for item in functions:
        counts.update(item["complexity"]["decisions"])
    if git(root, "status", "--porcelain"):
        raise RuntimeError("Working tree changed during calibration.")
    return {"id": entry["id"], "revision": entry["revision"], "producer": document["producer"], "functionFactsSha256": measured,
            "configurationFingerprint": document["selection"]["configurationFingerprint"],
            "functions": len(functions), "bands": dict(sorted(Counter(band(f["complexity"]["value"]) for f in functions).items())),
            "decisionCounts": dict(counts), "functionsUsing": {key: sum(f["complexity"]["decisions"][key] > 0 for f in functions) for key in counts},
            "current": current, "experiments": experiments, "reviews": reviewed}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repos", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True, help="New directory; prior studies are never overwritten")
    args = parser.parse_args()
    if source_facts.POLICY != "python/owned-function-complexity-v1":
        parser.error("This is the historical v1 study. Replay at commit b6f953b; use corpus.py to verify the current policy.")
    args.output.mkdir(parents=True, exist_ok=False)
    manifests = REPOSITORY / "shared/calibration/corpus"
    manifest = json.loads((manifests / "python-selection.json").read_text())
    reviews = json.loads((manifests / "python-complexity-review.json").read_text())
    baselines = json.loads((REPOSITORY / "shared/calibration/baselines/python-selection.json").read_text())
    code = [Path(__file__), Path(__file__).with_name("calibration_preview.py"), Path(__file__).with_name("calibration-preview.mjs"),
            Path(source_facts.__file__), Path(__file__).with_name("source_selection.py")]
    shared = REPOSITORY / "shared/source-facts"
    code.extend(shared / name for name in ["arithmetic.mjs", "population.mjs", "contract.mjs"])
    report = {"schemaVersion": 1, "measurementPolicy": source_facts.POLICY,
              "scope": "Offline calibration study. No production policy, score status, or cross-language calibration change.",
              "reviewSelection": reviews["selection"],
              "implementationSha256": digest(b"\0".join(file.name.encode() + b"\0" + file.read_bytes().replace(b"\r\n", b"\n") for file in code)),
              "samples": []}
    for entry in manifest["samples"]:
        baseline = next(item for item in baselines["samples"] if item["id"] == entry["id"])
        result = sample(entry, baseline, args.repos / entry["repository"], args.output / entry["id"], reviews["reviews"])
        report["samples"].append(result)
        print(entry["id"], json.dumps({"current": result["current"], "experiments": {key: {k: value[k] for k in ["score", "changedFunctions"]} for key, value in result["experiments"].items()}}))
    write(args.output / "summary.json", report)
    print("Calibration study complete. Production policies and corpus baselines are unchanged.")


if __name__ == "__main__":
    main()
