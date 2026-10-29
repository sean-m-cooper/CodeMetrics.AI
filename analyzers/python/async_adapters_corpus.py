"""Validate callback adapters against pinned sources and unchanged prior evidence."""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys

import async_adapters as adapters
import async_context_corpus as prior
from corpus import REPOSITORY, git, write


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repos", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True, help="New output directory")
    args = parser.parse_args()
    # The prior runner validates pinned clean checkouts, source facts, complexity,
    # exception evidence, and the original async call rows against frozen baselines.
    subprocess.run([sys.executable, prior.__file__, "--repos", str(args.repos), "--output", str(args.output)], check=True)
    load = lambda path: json.loads(path.read_text(encoding="utf-8"))
    earlier = load(args.output / "summary.json")
    frozen = load(REPOSITORY / "shared/scorecard-schema/calibration-runs/python-async-context-2026-10-17.json")
    paths = [Path(__file__), Path(adapters.__file__), Path(prior.__file__),
             *[Path(__file__).with_name(name) for name in (
                 "async_context.py", "async_provenance.py", "async_usage.py", "async_bindings.py",
                 "source_facts.py", "source_selection.py", "duplication.py")]]
    implementation = hashlib.sha256(b"\0".join(path.name.encode() + b"\0" +
        path.read_bytes().replace(b"\r\n", b"\n") for path in paths)).hexdigest()
    result = {"schemaVersion": 1, "policy": adapters.POLICY, "auditId": earlier["auditId"],
              "implementationSha256": implementation, "samples": [],
              "scope": "Syntax and bounded source links only. Async/Blocking Usage and Error Handling remain unscored."}
    for sample in earlier["samples"]:
        id = sample["id"]
        old = next(row for row in frozen["samples"] if row["id"] == id)
        if any(sample[key] != old[key] for key in ("revision", "contextRowsSha256", "summary")):
            raise RuntimeError("Existing async context changed: " + id)
        root = args.repos / sample["repository"]
        facts, base = load(args.output / f"{id}-source-facts.json"), load(args.output / f"{id}.json")
        report = adapters.review(root, facts, base)
        if report["status"] != "complete":
            raise RuntimeError(str(report["diagnostics"]))
        if git(root, "rev-parse", "HEAD") != sample["revision"] or git(root, "status", "--porcelain"):
            raise RuntimeError("Checkout changed during adapter review.")
        write(args.output / f"{id}-adapters.json", report)
        examples = {}
        for owner in report["functions"]:
            for call in owner["calls"]:
                for field in ("partialConstruction", "partialCallee", "callbackPartial", "decoratedCallee", "callbackDecorator"):
                    if call[field] and call[field]["status"] != "unresolved" and len(examples.setdefault(field, [])) < 5:
                        examples[field].append({"ownerId": owner["id"], "file": owner["file"], "member": owner["member"], **call})
        result["samples"].append({key: sample[key] for key in ("id", "repository", "revision", "runId",
            "functionFactsSha256", "selectedSourceSha256", "asyncRowsSha256", "contextRowsSha256", "methodComplexityScore")})
        result["samples"][-1].update(configurationFingerprint=report["configurationFingerprint"],
            adapterRowsSha256=adapters.usage.fingerprint(report["functions"]), summary=report["summary"], examples=examples,
            unchangedMethodComplexityAndErrorHandlingEvidence=True, unchangedAsyncEvidence=True, unchangedContextEvidence=True)
        print(id, json.dumps(report["summary"]["contexts"]), flush=True)
    write(args.output / "adapters-summary.json", result)


if __name__ == "__main__":
    main()
