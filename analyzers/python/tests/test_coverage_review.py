import copy
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import coverage_review
import source_facts


FIXTURE = Path(__file__).parent / "fixtures" / "coverage"


class CoverageReviewTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def write(self, name, text):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
        return path

    def report(self, files, **meta):
        data = {"meta": {"format": 3, "version": "7.6.1", "timestamp": "2020-01-01T00:00:00",
                         "branch_coverage": False, **meta}, "files": files}
        return self.write("coverage.json", json.dumps(data))

    def record(self, executed=(), missing=(), excluded=()):
        return {"executed_lines": list(executed), "missing_lines": list(missing), "excluded_lines": list(excluded)}

    def analyze(self, report=None, **kwargs):
        return coverage_review.analyze(self.root, coverage_json=report, **kwargs)

    def rows(self, result):
        self.assertEqual(result["status"], "complete", result["diagnostics"])
        return {row["member"]: row for row in result["functions"]}

    def assert_incomplete(self, result):
        self.assertEqual(result["status"], "incomplete")
        self.assertEqual(result["functions"], [])
        self.assertIsNone(result["summary"])
        self.assertTrue(result["diagnostics"])

    def test_real_coveragepy_fixture_maps_bodies_and_ignores_branch_percentages(self):
        shutil.copyfile(FIXTURE / "sample.py", self.root / "sample.py")
        report = self.analyze(FIXTURE / "coverage.json")
        rows = self.rows(report)
        partial = rows["partial"]
        self.assertEqual(partial["coverage"]["executedLines"], [6, 7])
        self.assertEqual(partial["coverage"]["missingLines"], [8])
        self.assertEqual(partial["coverage"]["fraction"], 2 / 3)
        self.assertAlmostEqual(partial["crapEstimate"]["value"], 2 + 4 / 27)
        self.assertEqual(rows["untouched"]["coverage"]["fraction"], 0)
        self.assertEqual(rows["untouched"]["crapEstimate"]["value"], 6)
        self.assertEqual(rows["full"]["crapEstimate"]["value"], 1)
        self.assertEqual(rows["ignored"]["coverage"]["excludedLines"], [24])
        self.assertEqual(rows["ignored"]["coverage"]["fraction"], 1)
        self.assertEqual(rows["ignored"]["complexity"], 2)
        self.assertEqual(rows["ignored"]["crapEstimate"]["value"], 2)
        for member in ("partial", "outer.inner", "asynchronous", "Worker.run"):
            self.assertEqual(rows[member]["coverage"]["fraction"], 2 / 3)
        self.assertEqual(rows["outer"]["coverage"]["reason"], "nested-definition")
        self.assertEqual(rows["one_line"]["coverage"]["reason"], "body-shares-definition-line")
        self.assertEqual(rows["with_lambda"]["coverage"]["reason"], "nested-definition")
        self.assertEqual(rows["multi_line"]["coverage"]["executableLines"], 1)
        self.assertEqual(rows["statements_on_one_line"]["coverage"]["executableLines"], 1)
        self.assertEqual(rows["documentation_only"]["coverage"]["reason"], "no-reportable-body-lines")
        self.assertEqual(report["summary"]["selectedFunctions"], 14)
        self.assertEqual(report["summary"]["conditionalEstimates"], 9)
        self.assertEqual(report["summary"]["unavailableFunctions"], 5)
        self.assertEqual(report["summary"]["matchedFiles"], 1)
        self.assertEqual(report["coverageInput"]["sourceBinding"], "unverified")
        self.assertEqual(report["coverageInput"]["freshness"], "unverified")
        self.assertNotIn("score", report)
        self.assertNotIn("overall", report)

    def test_formula_zero_half_full_and_complexity_never_discounted(self):
        source = "def choose(value):\n    value = int(value)\n" + "".join(f"    if value == {i}:\n        return {i}\n" for i in range(9)) + "    return -1\n"
        self.write("code.py", source)
        for covered, expected in [(0, 110), (10, 22.5), (20, 10)]:
            with self.subTest(covered=covered):
                path = self.report({"code.py": self.record(range(2, 2 + covered), range(2 + covered, 22))})
                row = self.rows(self.analyze(path))["choose"]
                self.assertEqual(row["complexity"], 10)
                self.assertEqual(row["crapEstimate"]["value"], expected)
                self.assertEqual(row["crapEstimate"]["disposition"], "conditional-review-only")

    def test_missing_report_file_and_empty_denominator_are_unavailable_not_zero(self):
        self.write("code.py", "def run():\n    return 1\n")
        self.assertEqual(self.rows(self.analyze())["run"]["coverage"]["reason"], "coverage-not-supplied")
        for record, reason in [({}, "file-not-in-report"), ({"code.py": self.record()}, "no-reportable-body-lines"),
                               ({"code.py": self.record(excluded=[1, 2])}, "no-reportable-body-lines")]:
            row = self.rows(self.analyze(self.report(record)))["run"]
            self.assertEqual(row["coverage"]["reason"], reason)
            self.assertIsNone(row["crapEstimate"])
        self.assert_incomplete(self.analyze(self.root / "does-not-exist.json"))

    def test_paths_are_exact_relative_and_never_basename_matches(self):
        self.write("src/code.py", "def run():\n    return 1\n")
        report = self.analyze(self.report({"elsewhere/code.py": self.record([1, 2])}))
        self.assertEqual(self.rows(report)["run"]["coverage"]["reason"], "file-not-in-report")
        self.assertEqual(report["summary"]["unmatchedReportFiles"], ["elsewhere/code.py"])
        for path in ["src/code.py", "src\\code.py", "./src/code.py"]:
            row = self.rows(self.analyze(self.report({path: self.record([1, 2])})))["run"]
            self.assertEqual(row["coverage"]["fraction"], 1)
        for path in ["../src/code.py", "/src/code.py", "C:\\src\\code.py", "\\\\host\\code.py", "."]:
            self.assert_incomplete(self.analyze(self.report({path: self.record([1, 2])})))
        self.assert_incomplete(self.analyze(self.report({"src/code.py": self.record(), "src\\code.py": self.record()})))

    def test_definition_and_docstring_shared_lines_are_ambiguous(self):
        self.write("code.py", 'def run(\n    x=1\n): return x\ndef other():\n    "docs"; return 1\n')
        rows = self.rows(self.analyze(self.report({"code.py": self.record([1, 3, 4, 5])})))
        self.assertEqual(rows["run"]["coverage"]["reason"], "body-shares-definition-line")
        self.assertEqual(rows["other"]["coverage"]["reason"], "body-shares-docstring-line")

    def test_decorators_defaults_annotations_and_docstrings_do_not_add_coverage(self):
        self.write("code.py", '@decorate\ndef run(value: Unknown = dangerous()):\n    "docs"\n    return value\n')
        row = self.rows(self.analyze(self.report({"code.py": self.record([1, 2, 3], [4])})))["run"]
        self.assertEqual(row["coverage"]["executableLines"], 1)
        self.assertEqual(row["coverage"]["fraction"], 0)

    def test_source_selection_population_declarations_and_unmatched_report_files(self):
        self.write("src/code.py", "def run():\n    return 1\n")
        self.write("tests/test_code.py", "def test_run():\n    assert True\n")
        self.write("src/contract.py", "from typing import Protocol\nclass Port(Protocol):\n    def read(self): ...\n")
        path = self.report({"src/code.py": self.record([1, 2]), "tests/test_code.py": self.record([1, 2])})
        report = self.analyze(path)
        self.assertEqual(set(self.rows(report)), {"run"})
        self.assertEqual(report["summary"]["unmatchedReportFiles"], ["tests/test_code.py"])
        selected = self.analyze(path, include_tests=True)
        self.assertEqual(self.rows(selected)["test_run"]["population"], "test")
        self.assertEqual(len(selected["selection"]["excludedFunctions"]), 1)
        self.assertNotEqual(selected["configurationFingerprint"], report["configurationFingerprint"])

    def test_invalid_json_formats_and_line_sets_fail_without_partial_results(self):
        self.write("code.py", "def run():\n    return 1\n")
        valid = json.loads(self.report({"code.py": self.record([1, 2])}).read_text())
        invalid = [None, [], {}, {**valid, "files": []}]
        for version in (1, 4, True, "3"):
            invalid.append({**valid, "meta": {**valid["meta"], "format": version}})
        for bad in [self.record([True]), self.record([0]), self.record([1.5]), self.record(["2"]),
                    self.record([2, 2]), self.record([2], [2]), self.record([], [2], [2]),
                    self.record([999]), {"executed_lines": [], "missing_lines": []}, None]:
            invalid.append({**valid, "files": {"code.py": bad}})
        for data in invalid:
            with self.subTest(data=data):
                self.assert_incomplete(self.analyze(self.write("invalid.json", json.dumps(data))))
        for text in ['{"meta": {}, "meta": {}}', '{"value": NaN}', '{"value": Infinity}', '{']:
            self.assert_incomplete(self.analyze(self.write("invalid.json", text)))
        older = copy.deepcopy(valid)
        older["meta"]["format"] = 2
        self.assertEqual(self.analyze(self.write("format2.json", json.dumps(older)))["status"], "complete")

    def test_executed_exclusions_are_not_coverage_credit(self):
        self.write("code.py", "def run(value):\n    if value:\n        return 1\n    return 0\n")
        row = self.rows(self.analyze(self.report({"code.py": self.record([1, 2, 3], [4], [3])})))["run"]
        self.assertEqual(row["coverage"]["executedLines"], [2])
        self.assertEqual(row["coverage"]["excludedLines"], [3])
        self.assertEqual(row["coverage"]["fraction"], 0.5)
        self.assertEqual(row["complexity"], 2)

    def test_source_failure_drift_and_budgets_withhold_all_results(self):
        path = self.write("code.py", "def run():\n    return 1\n")
        report = self.report({"code.py": self.record([1, 2])})
        with patch.object(coverage_review, "MAX_REPORT_BYTES", 1):
            self.assert_incomplete(self.analyze(report))
        with patch.object(coverage_review, "MAX_OWNERS", 0):
            self.assert_incomplete(self.analyze(report))
        original = source_facts.analyze
        def drift(*args, **kwargs):
            facts = original(*args, **kwargs)
            path.write_text(path.read_text() + "# drift\n")
            return facts
        with patch.object(source_facts, "analyze", side_effect=drift):
            self.assert_incomplete(self.analyze(report))
        self.write("bad.py", "def broken(:")
        self.assert_incomplete(self.analyze(report))

    def test_fresh_review_ids_do_not_certify_historical_coverage(self):
        self.write("code.py", 'raise RuntimeError("never execute targets")\ndef run():\n    return 1\n')
        path = self.report({"code.py": self.record([2, 3])})
        first = self.analyze(path)
        second = self.analyze(path)
        self.assertEqual(self.rows(first)["run"], self.rows(second)["run"])
        self.assertNotEqual(first["runId"], second["runId"])
        self.assertNotEqual(first["auditId"], second["auditId"])
        self.assertEqual(first["coverageInput"]["sha256"], hashlib.sha256(path.read_bytes()).hexdigest())
        self.assertEqual(first["coverageInput"]["freshness"], "unverified")
        self.assertEqual(first["coverageInput"]["reportedAt"], "2020-01-01T00:00:00")
        supplied = self.analyze(path, run_id=first["runId"], audit_id=first["auditId"])
        self.assertEqual(supplied["runId"], first["runId"])
        with self.assertRaises(ValueError):
            self.analyze(path, run_id="invalid")

    def test_empty_selection_and_cli_exit_codes_preserve_outputs(self):
        self.write("empty.py", "VALUE = 1\n")
        self.assertEqual(self.analyze()["summary"]["selectedFunctions"], 0)
        output = self.root / "review.json"
        command = [sys.executable, str(Path(coverage_review.__file__)), str(self.root), "--output", str(output)]
        run = subprocess.run(command, capture_output=True, text=True)
        self.assertEqual(run.returncode, 0, run.stderr)
        saved = output.read_bytes()
        self.assertEqual(json.loads(saved)["artifactKind"], "experimental-python-coverage-review")
        self.assertEqual(subprocess.run(command, capture_output=True).returncode, 2)
        self.assertEqual(output.read_bytes(), saved)
        failed = subprocess.run(command[:-2] + ["--coverage-json", str(self.root / "missing.json")], capture_output=True, text=True)
        self.assertEqual(failed.returncode, 2)
        self.assert_incomplete(json.loads(failed.stdout))


if __name__ == "__main__":
    unittest.main()
