import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import duplication
import source_facts

BODY = "    value = items[0]\n    value += 1\n    value *= 2\n    result = str(value)\n    return result\n"


class DuplicationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def write(self, name, source):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(source, encoding="utf-8")
        return path

    def test_exact_bodies_ignore_format_comments_docstrings_and_signature(self):
        self.write("one.py", 'def first(items):\n    "First documentation"\n' + BODY)
        self.write("two.py", '@decorator\ndef second(items=None):\n    "Different docs"\n    # intentional independent implementation\n' + BODY.replace("value += 1", "value   +=   1"))
        report = duplication.analyze(self.root)
        self.assertEqual(report["status"], "complete")
        self.assertEqual(report["summary"]["duplicateGroups"], 1)
        self.assertEqual(report["summary"]["matchedFunctions"], 2)
        group = report["groups"][0]
        self.assertEqual(group["statementCount"], 5)
        self.assertEqual([(row["file"], row["line"], row["endLine"]) for row in group["occurrences"]], [("one.py", 1, 7), ("two.py", 2, 9)])
        self.assertEqual(group["disposition"], "review-only")
        self.assertNotIn("score", report)

    def test_names_literals_and_statement_order_are_retained(self):
        self.write("base.py", "def original(items):\n" + BODY)
        self.write("names.py", "def renamed(items):\n" + BODY.replace("value", "number"))
        self.write("literal.py", "def changed(items):\n" + BODY.replace("+= 1", "+= 3"))
        self.write("order.py", "def reordered(items):\n" + BODY.replace("value += 1\n    value *= 2", "value *= 2\n    value += 1"))
        report = duplication.analyze(self.root)
        self.assertEqual(report["summary"]["eligibleFunctions"], 4)
        self.assertEqual(report["groups"], [])

    def test_three_occurrences_form_one_group_without_pairwise_double_counting(self):
        for name in ("one", "two", "three"):
            self.write(name + ".py", f"def {name}(items):\n" + BODY)
        report = duplication.analyze(self.root)
        self.assertEqual(report["summary"]["duplicateGroups"], 1)
        self.assertEqual(report["summary"]["matchedFunctions"], 3)
        self.assertEqual(len({item["id"] for item in report["groups"][0]["occurrences"]}), 3)

    def test_statement_count_is_not_physical_lines(self):
        source = "def one():\n    return {\n" + "".join(f"        '{i}': {i},\n" for i in range(10)) + "    }\n"
        self.write("one.py", source)
        self.write("two.py", source.replace("def one", "def two"))
        report = duplication.analyze(self.root)
        self.assertEqual(report["summary"]["eligibleFunctions"], 0)
        self.assertEqual([item["statementCount"] for item in report["excludedFunctions"]], [1, 1])

    def test_whole_body_boundary_and_statement_minimum(self):
        self.write("code.py", "def one(items):\n" + BODY + "\ndef extra(items):\n" + BODY + "    print(result)\n\ndef short(items):\n    return items\n")
        report = duplication.analyze(self.root)
        self.assertEqual(report["groups"], [])
        self.assertEqual(report["summary"]["exclusionReasons"], {"below-statement-minimum": 1})
        raised = duplication.analyze(self.root, minimum_statements=6)
        self.assertEqual(raised["summary"]["eligibleFunctions"], 1)
        self.assertNotEqual(report["configurationFingerprint"], raised["configurationFingerprint"])
        for invalid in [0, 1, True, 2.5]:
            with self.assertRaises(ValueError):
                duplication.analyze(self.root, minimum_statements=invalid)

    def test_population_and_async_kind_are_partitioned(self):
        self.write("one.py", "def one(items):\n" + BODY)
        self.write("async_one.py", "async def one(items):\n" + BODY)
        self.write("tests/test_one.py", "def test_one(items):\n" + BODY)
        self.write("tests/test_two.py", "def test_two(items):\n" + BODY)
        default = duplication.analyze(self.root)
        self.assertEqual(default["summary"]["eligibleFunctions"], 2)
        self.assertEqual(default["groups"], [])
        selected = duplication.analyze(self.root, include_tests=True)
        self.assertEqual(len(selected["groups"]), 1)
        self.assertEqual(selected["groups"][0]["population"], "test")
        self.assertEqual(selected["summary"]["matchedFunctions"], 2)

    def test_nested_owners_are_separate_and_parents_are_excluded(self):
        nested = "    def inner(items):\n" + "\n".join("    " + line for line in BODY.rstrip().splitlines()) + "\n    return inner(items)\n"
        self.write("one.py", "def outer(items):\n" + nested)
        self.write("two.py", "def wrapper(items):\n" + nested)
        self.write("lambda.py", "def with_lambda():\n    func = lambda value: value + 1\n    return func\n")
        report = duplication.analyze(self.root)
        self.assertEqual(report["summary"]["exclusionReasons"], {"lambda": 1, "nested-definition": 3})
        self.assertEqual({row["member"] for row in report["groups"][0]["occurrences"]}, {"outer.inner", "wrapper.inner"})
        self.assertEqual(report["summary"]["selectedFunctions"], report["summary"]["eligibleFunctions"] + report["summary"]["excludedFunctions"])

    def test_declarations_and_noops_cannot_reach_minimum(self):
        self.write("code.py", 'from typing import Protocol\nclass Reader(Protocol):\n    def read(self): ...\ndef noops():\n    "docs"\n    value: int\n    pass\n    pass\n    pass\n    pass\n')
        report = duplication.analyze(self.root)
        self.assertEqual(report["summary"]["selectedFunctions"], 1)
        self.assertEqual(report["excludedFunctions"][0]["statementCount"], 0)
        self.assertEqual(len(report["selection"]["excludedFunctions"]), 1)

    def test_deterministic_groups_fresh_ids_and_no_target_execution(self):
        self.write("code.py", 'raise RuntimeError("must not execute")\ndef one(items):\n' + BODY + "\ndef two(items):\n" + BODY)
        first = duplication.analyze(self.root)
        second = duplication.analyze(self.root)
        self.assertEqual(first["groups"], second["groups"])
        self.assertEqual(first["configurationFingerprint"], second["configurationFingerprint"])
        self.assertNotEqual(first["runId"], second["runId"])
        self.assertNotEqual(first["auditId"], second["auditId"])
        self.assertEqual(duplication.analyze(self.root, run_id=first["runId"], audit_id=first["auditId"])["auditId"], first["auditId"])

    def test_partial_parse_never_reports_duplicate_groups(self):
        self.write("good.py", "def one(items):\n" + BODY + "\ndef two(items):\n" + BODY)
        self.write("bad.py", "def broken(:")
        report = duplication.analyze(self.root)
        self.assertEqual(report["status"], "incomplete")
        self.assertEqual(report["groups"], [])
        self.assertIsNone(report["summary"])
        self.assertTrue(report["diagnostics"])
        self.assertEqual(duplication.analyze(self.root, exclude=["bad.py"])["summary"]["duplicateGroups"], 1)

    def test_source_drift_and_budgets_fail_without_partial_results(self):
        path = self.write("code.py", "def one(items):\n" + BODY + "\ndef two(items):\n" + BODY)
        original = source_facts.analyze
        def changed(*args, **kwargs):
            facts = original(*args, **kwargs)
            path.write_text(path.read_text() + "# changed\n")
            return facts
        with patch.object(source_facts, "analyze", side_effect=changed):
            self.assert_failed(duplication.analyze(self.root))
        with patch.object(duplication, "MAX_NORMALIZED_BYTES", 1):
            self.assert_failed(duplication.analyze(self.root))
        with patch.object(duplication, "MAX_OWNERS", 1):
            self.assert_failed(duplication.analyze(self.root))

    def assert_failed(self, report):
        self.assertEqual(report["status"], "incomplete")
        self.assertIsNone(report["summary"])
        self.assertEqual(report["groups"], [])
        self.assertTrue(report["diagnostics"])

    def test_empty_complete_selection_is_explicit_and_cli_preserves_outputs(self):
        self.write("empty.py", "VALUE = 1\n")
        report = duplication.analyze(self.root)
        self.assertEqual(report["summary"]["eligibleFunctions"], 0)
        self.assertEqual(report["status"], "complete")
        script = Path(duplication.__file__)
        output = self.root / "review.json"
        command = [sys.executable, str(script), str(self.root), "--output", str(output)]
        run = subprocess.run(command, capture_output=True, text=True)
        self.assertEqual(run.returncode, 0, run.stderr)
        saved = output.read_bytes()
        self.assertEqual(json.loads(saved)["artifactKind"], "experimental-python-duplication-review")
        self.assertEqual(subprocess.run(command, capture_output=True).returncode, 2)
        self.assertEqual(output.read_bytes(), saved)
        self.write("bad.py", "def broken(:")
        failed = subprocess.run([sys.executable, str(script), str(self.root)], capture_output=True, text=True)
        self.assertEqual(failed.returncode, 2)
        self.assertIsNone(json.loads(failed.stdout)["summary"])


if __name__ == "__main__":
    unittest.main()
