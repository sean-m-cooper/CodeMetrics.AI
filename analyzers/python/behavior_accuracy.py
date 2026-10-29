"""Evaluate curated source-contract labels, not runtime correctness or field accuracy."""

import argparse
import json
from pathlib import Path
import tempfile

import async_helper_paths as helpers
import error_handling_dispositions as dispositions
from corpus import write


HANDLERS = [
    ("bare-swallow", "pass", "unexplained-swallowing-candidate"),
    ("directive-is-not-rationale", "pass # noqa", "unexplained-swallowing-candidate"),
    ("documented-choice", "pass # Missing optional notification is acceptable", "documented-suppression"),
    ("conditional-propagation", "if flag:\n    raise\nelse:\n    raise Wrapped()", "propagation"),
    ("conditional-fallback", "if flag:\n    return None\nreturn cached", "explicit-fallback"),
    ("recovery-call", "restore()", "recovery-action"),
    ("mixed-path", "if flag:\n    return None\nraise", "mixed-explicit-disposition"),
    ("missing-branch", "if flag:\n    restore()", "unresolved"),
    ("nested-definition-is-not-recovery", "def restore():\n    log()", "unresolved"),
]

CONTEXTS = [
    ("route-search", "def run():\n    for item in items:\n        try: return probe(item)\n        except Missing: pass\n    raise Missing()\n", "unresolved"),
    ("iterator-disconnect", "async def run():\n    try:\n        while True: yield await receive()\n    except Disconnected: pass\n", "unresolved"),
    ("selective-config-fallback", "def run():\n    try: return read()\n    except Missing: pass\n    except Invalid: raise BadConfig()\n", "unresolved"),
    ("optional-import", "try:\n    import optional\nexcept ImportError:\n    pass\n", "explicit-fallback"),
    ("enclosing-finally", "def run():\n    try: work()\n    except Error: pass\n    finally: cleanup()\n", "unresolved"),
    ("preceding-rationale", "def run():\n    # Optional operation may be unavailable\n    try: work()\n    except Missing: pass\n", "unresolved"),
]

ASYNC = [
    ("direct-blocking", "async def run():\n    time.sleep(1)\n", 1),
    ("helper-blocking", "async def run():\n    work()\n", 1),
    ("offload-reference", "async def run():\n    await asyncio.to_thread(work)\n", 0),
    ("eager-offload-argument", "async def run():\n    await asyncio.to_thread(work())\n", 1),
    ("deferred-generator", "async def run():\n    gen = (work() for item in items)\n", 0),
    ("eager-generator-iterable", "async def run():\n    gen = (item for item in work())\n", 1),
    ("shadowed-parameter", "async def run(work):\n    work()\n", 0),
    ("unresolved-receiver", "async def run():\n    obj.work()\n", 0),
    ("partial-invocation", "async def run():\n    p = partial(work)\n    p()\n", 1),
    ("partial-construction-only", "async def run():\n    p = partial(work)\n", 0),
    ("wrapper-construction-only", "async def run():\n    wrapped = sync_to_async(work)\n", 0),
    ("wrapper-invocation", "async def run():\n    wrapped = sync_to_async(work)\n    await wrapped()\n", 0),
]


def evaluate():
    rows = []
    handlers = [(name, "def run(flag):\n    try: work()\n    except Error:\n" +
        "".join("        " + line + "\n" for line in body.splitlines()), expected) for name, body, expected in HANDLERS] + CONTEXTS
    prelude = "import time\nimport asyncio\nfrom functools import partial\nfrom asgiref.sync import sync_to_async\ndef work():\n    time.sleep(1)\n"
    with tempfile.TemporaryDirectory(prefix="behavior-accuracy-") as folder:
        path = Path(folder) / "sample.py"
        for name, source, expected in handlers:
            path.write_text(source, encoding="utf-8")
            report = dispositions.analyze(path)
            if report["status"] != "complete":
                raise ValueError(report["diagnostics"])
            actual = report["handlers"][0]["classification"]
            rows.append({"case": name, "reviewer": "handlers", "sourceSha256": helpers.usage.digest(source),
                "expected": expected, "actual": actual, "matches": actual == expected,
                "expectedCandidate": expected == "unexplained-swallowing-candidate",
                "actualCandidate": actual == "unexplained-swallowing-candidate"})
        for name, body, expected in ASYNC:
            source = prelude + body
            path.write_text(source, encoding="utf-8")
            report = helpers.analyze(path)
            if report["status"] != "complete":
                raise ValueError(report["diagnostics"])
            actual = report["summary"]["functionsWithDirectOrHelperCandidates"]
            rows.append({"case": name, "reviewer": "async", "sourceSha256": helpers.usage.digest(source),
                "expected": expected, "actual": actual, "matches": actual == expected,
                "expectedCandidate": bool(expected), "actualCandidate": bool(actual)})
    metrics = {}
    for reviewer in ("handlers", "async"):
        selected = [row for row in rows if row["reviewer"] == reviewer]
        metrics[reviewer] = {"cases": len(selected), "exactMatches": sum(row["matches"] for row in selected),
            "truePositives": sum(row["expectedCandidate"] and row["actualCandidate"] for row in selected),
            "trueNegatives": sum(not row["expectedCandidate"] and not row["actualCandidate"] for row in selected),
            "falsePositives": sum(not row["expectedCandidate"] and row["actualCandidate"] for row in selected),
            "falseNegatives": sum(row["expectedCandidate"] and not row["actualCandidate"] for row in selected)}
    return {"schemaVersion": 1, "scope": "Curated source-contract examples including corpus-inspired negatives. Not a random sample, independent holdout, runtime accuracy estimate, or scoring calibration.",
            "passed": all(row["matches"] for row in rows), "metrics": metrics, "cases": rows}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.output.exists() or args.output.suffix != ".json":
        parser.error("Output must be a new .json file.")
    result = evaluate()
    write(args.output, result)
    print(json.dumps(result["metrics"]))
    return 0 if result["passed"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
