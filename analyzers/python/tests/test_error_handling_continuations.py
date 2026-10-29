import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import error_handling_continuations as continuation


CASES = [
    ("dedented-raise", "def run(flag):\n    if flag:\n        try: validate()\n        except Invalid: pass\n    raise Invalid()\n", "propagation"),
    ("dedented-false", "def run(flag):\n    if flag:\n        try: validate()\n        except Invalid: pass\n    return False\n", "explicit-fallback"),
    ("delegate", "def run(flag):\n    if flag:\n        try: return serve()\n        except Missing: pass\n    return parent()\n", "explicit-fallback"),
    ("default-getter", "def run(data, flag):\n    getter = data.get\n    if flag:\n        try: getter = data.getlist\n        except AttributeError: pass\n    return getter('key')\n", "explicit-fallback"),
    ("try-else-skipped", "def run():\n    try: work()\n    except Missing: pass\n    else: raise WrongBranch()\n    return fallback\n", "explicit-fallback"),
    ("sibling-handler-skipped", "def run():\n    try: work()\n    except Missing: pass\n    except Other: raise WrongBranch()\n    return fallback\n", "explicit-fallback"),
    ("opposite-if-branch-skipped", "def run(flag):\n    if flag:\n        try: work()\n        except Missing: pass\n    else: raise WrongBranch()\n    return fallback\n", "explicit-fallback"),
    ("else-suite", "def run(flag):\n    if flag: return early\n    else:\n        try: work()\n        except Missing: pass\n    raise Later()\n", "propagation"),
    ("mixed-terminals", "def run(flag):\n    try: work()\n    except Missing: pass\n    if flag: return fallback\n    raise Later()\n", "mixed-explicit-disposition"),
    ("partial-terminal", "def run(flag):\n    try: work()\n    except Missing: pass\n    if flag: return fallback\n", None),
    ("unrelated-call", "def run():\n    try: work()\n    except Missing: pass\n    notify()\n", None),
    ("no-following-code", "def run():\n    try: work()\n    except Missing: pass\n", None),
    ("own-finally", "def run():\n    try: work()\n    except Missing: pass\n    finally: cleanup()\n    return fallback\n", None),
    ("outer-finally", "def run():\n    try:\n        try: work()\n        except Missing: pass\n        return fallback\n    finally: cleanup()\n", None),
    ("outer-handler", "def run():\n    try:\n        try: work()\n        except Missing: pass\n    except Other: return fallback\n    return result\n", None),
    ("with-boundary", "def run():\n    with manager():\n        try: work()\n        except Missing: pass\n    return fallback\n", None),
    ("loop-boundary", "def run():\n    for x in items:\n        try: work()\n        except Missing: pass\n    return fallback\n", None),
    ("later-with", "def run():\n    try: work()\n    except Missing: pass\n    with manager(): action()\n    return fallback\n", None),
    ("bare-raise", "def run():\n    try: work()\n    except Missing: pass\n    raise\n", None),
    ("generator", "def run():\n    try: work()\n    except Missing: pass\n    yield 1\n    return fallback\n", None),
    ("exception-group", "def run():\n    try: work()\n    except* Missing: pass\n    return fallback\n", None),
    ("documented-choice", "def run():\n    try: work()\n    except Missing: pass # Deliberate optional behavior\n    return fallback\n", None),
    ("nested-definition", "def run():\n    try: work()\n    except Missing: pass\n    def fallback(): return value\n", None),
    ("unreachable-tail", "def run():\n    try: work()\n    except Missing: pass\n    return fallback\n    with manager(): action()\n", "explicit-fallback"),
]


class ContinuationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def write(self, source):
        path = self.root / "work.py"
        path.write_text(source, encoding="utf-8")
        return path

    def report(self, source):
        self.write(source)
        report = continuation.analyze(self.root)
        self.assertEqual(report["status"], "complete", report["diagnostics"])
        self.assertNotIn('"score"', json.dumps(report))
        self.assertNotIn('"findings"', json.dumps(report))
        return report

    def test_labeled_continuation_matrix(self):
        for name, source, expected in CASES:
            with self.subTest(name=name):
                row = self.report(source)["handlers"][0]
                if expected:
                    self.assertEqual(row["continuation"]["status"], "explicit-continuation")
                    self.assertEqual(row["classification"], expected)
                else:
                    self.assertNotEqual(row["continuation"]["status"], "explicit-continuation")
                    self.assertEqual(row["classification"], row["originalClassification"])

    def test_default_binding_evidence_and_nested_if_continuation(self):
        source = "def run(data, a, b):\n    getter = data.get\n    if a:\n        if b:\n            try: getter = data.getlist\n            except AttributeError: pass\n    return getter('key')\n"
        row = self.report(source)["handlers"][0]
        evidence = row["continuation"]
        self.assertEqual(len(evidence["crossedBranches"]), 2)
        default = evidence["retainedDefault"]
        self.assertEqual(default["name"], "getter")
        self.assertEqual(default["initializerSymbol"], "data.get")
        self.assertEqual(default["uses"], [{"line": 7, "kind": "returned-call-through-name"}])

    def test_default_vetoes_do_not_claim_retained_binding(self):
        cases = [
            "def run(data):\n    getter = data.get\n    try: getter = data.getlist\n    except AttributeError as getter: pass\n    return getter('key')\n",
            "def run(data):\n    getter = data.get\n    try: getter = data.getlist\n    except AttributeError: pass\n    getter = replacement\n    return getter('key')\n",
            "def run(data, flag):\n    getter = data.get\n    if flag: getter = replacement\n    try: getter = data.getlist\n    except AttributeError: pass\n    return getter('key')\n",
            "def run(data):\n    global getter\n    getter = data.get\n    try: getter = data.getlist\n    except AttributeError: pass\n    return getter('key')\n",
            "def run(data):\n    getter = data.get\n    exec(code)\n    try: getter = data.getlist\n    except AttributeError: pass\n    return getter('key')\n",
            "def run(data):\n    getter = data.get\n    try:\n        getter = other\n        getter = data.getlist\n    except AttributeError: pass\n    return getter('key')\n",
            "def run(data):\n    getter = data.get\n    try: getter = factory()\n    except AttributeError: pass\n    return getter('key')\n",
        ]
        for source in cases:
            with self.subTest(source=source):
                self.assertIsNone(self.report(source)["handlers"][0]["continuation"]["retainedDefault"])

    def test_unreachable_nested_return_cannot_credit_default(self):
        source = "def run(data, flag):\n    getter = data.get\n    try: getter = data.getlist\n    except AttributeError: pass\n    if flag:\n        raise Invalid()\n        return getter('key')\n    else:\n        return None\n"
        row = self.report(source)["handlers"][0]
        self.assertEqual(row["classification"], "mixed-explicit-disposition")
        self.assertIsNone(row["continuation"]["retainedDefault"])

    def test_limits_withhold_claims_and_global_exhaustion_discards_rows(self):
        source = CASES[0][1]
        self.write(source)
        with patch.object(continuation, "MAX_NODES", 0):
            report = continuation.analyze(self.root)
        self.assertEqual(report["status"], "complete")
        self.assertEqual(report["handlers"][0]["continuation"]["reason"], "continuation-node-limit")
        with patch.object(continuation, "MAX_ENCLOSING", 0):
            report = continuation.analyze(self.root)
        self.assertEqual(report["handlers"][0]["continuation"]["reason"], "continuation-enclosing-limit")
        with patch.object(continuation, "MAX_TOTAL_NODES", 0):
            report = continuation.analyze(self.root)
        self.assertEqual(report["status"], "incomplete")
        self.assertEqual(report["handlers"], [])
        self.assertIsNone(report["summary"])

    def test_input_integrity_and_immutability(self):
        self.write(CASES[0][1])
        facts = continuation.usage.source_facts.analyze(self.root)
        base = continuation.dispositions.operations.analyze(self.root)
        before = copy.deepcopy([facts, base])
        self.assertEqual(continuation.review(self.root, facts, base)["status"], "complete")
        self.assertEqual([facts, base], before)
        altered = copy.deepcopy(base)
        altered["handlers"] = []
        self.assertEqual(continuation.review(self.root, facts, altered)["status"], "incomplete")
        self.write("changed = True\n")
        self.assertEqual(continuation.review(self.root, facts, base)["status"], "incomplete")

    def test_cli_and_no_target_execution(self):
        path = self.write("raise RuntimeError('do not execute')\n")
        output = self.root / "result.json"
        command = [sys.executable, continuation.__file__, str(path), "--output", str(output)]
        result = subprocess.run(command, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        contents = output.read_bytes()
        self.assertEqual(subprocess.run(command, capture_output=True).returncode, 2)
        self.assertEqual(output.read_bytes(), contents)
        self.assertEqual(json.loads(contents)["summary"]["handlers"], 0)


if __name__ == "__main__":
    unittest.main()
