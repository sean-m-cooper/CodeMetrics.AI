import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import coverage_collect
import coverage_provenance
import coverage_review


FIXTURE = Path(__file__).parent / "fixtures" / "coverage"


class ProvenanceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.base = Path(self.temporary.name)
        self.root = self.base / "project"
        self.root.mkdir()
        shutil.copyfile(FIXTURE / "sample.py", self.root / "sample.py")
        self.output = self.base / "collection"

    def fake_run(self, command, root, output, timeout):
        shutil.copyfile(FIXTURE / "coverage.json", output / "coverage.json")
        (output / "runtime.json").write_text(json.dumps({"testExitCode": 0, "pythonVersion": "3.12.10", "coverageVersion": "7.6.1"}))
        return 0

    def collect(self, runner=None, **kwargs):
        with patch.object(coverage_collect, "run_command", side_effect=runner or self.fake_run):
            return coverage_collect.collect(self.root, self.output, **kwargs)

    def review(self, **kwargs):
        return coverage_review.analyze(self.root, coverage_json=self.output / "coverage.json",
                                       collection_manifest=self.output / "provenance.json", **kwargs)

    def assert_rejected(self, result):
        self.assertEqual(result["status"], "incomplete", result)
        self.assertEqual(result["functions"], [])
        self.assertIsNone(result["summary"])
        self.assertTrue(result["diagnostics"])

    def test_bound_source_preserves_arithmetic_and_collection_identity(self):
        collected = self.collect()
        self.assertEqual(collected["status"], "complete", collected["diagnostics"])
        verified = self.review()
        self.assertEqual(verified["status"], "complete", verified["diagnostics"])
        self.assertEqual(verified["collectionProvenance"]["collectionId"], collected["collectionId"])
        self.assertNotEqual(verified["runId"], collected["collectionId"])
        self.assertEqual(verified["coverageInput"]["sourceBinding"], "matched-pre-post-snapshots")
        self.assertEqual(verified["summary"]["sourceBoundEstimates"], 9)
        self.assertEqual(verified["summary"]["conditionalEstimates"], 0)
        plain = coverage_review.analyze(self.root, coverage_json=self.output / "coverage.json")
        for bound, unbound in zip(verified["functions"], plain["functions"]):
            if bound["crapEstimate"]:
                self.assertEqual(bound["crapEstimate"]["value"], unbound["crapEstimate"]["value"])
                self.assertEqual(bound["crapEstimate"]["disposition"], "review-only")
        self.assertEqual(plain["summary"]["conditionalEstimates"], 9)

    def test_report_substitution_and_same_line_count_source_edit_rejected(self):
        self.collect()
        source = self.root / "sample.py"
        original = source.read_text()
        source.write_text(original.replace("return -1", "return -2"))
        self.assert_rejected(self.review())
        source.write_text(original)
        path = self.output / "coverage.json"
        path.write_bytes(path.read_bytes() + b"\n")
        self.assert_rejected(self.review())

    def test_source_addition_deletion_selection_and_parser_drift_rejected(self):
        self.collect()
        extra = self.root / "new.py"
        extra.write_text("def added():\n    return 1\n")
        self.assert_rejected(self.review())
        extra.unlink()
        self.assert_rejected(self.review(include=["sample.py"]))
        path = self.output / "provenance.json"
        data = json.loads(path.read_text())
        data["sourceFactsProducer"]["parserVersion"] = "3.12.0"
        path.write_text(json.dumps(data))
        self.assert_rejected(self.review())
        (self.root / "sample.py").unlink()
        self.assert_rejected(self.review())

    def test_no_laundering_failed_or_malformed_receipts(self):
        self.collect()
        path = self.output / "provenance.json"
        valid = json.loads(path.read_text())
        for key, value in [("status", "incomplete"), ("testExitCode", True), ("testExitCode", 1),
                           ("policy", "future"), ("schemaVersion", True), ("runtime", None),
                           ("collectionId", "not-a-uuid"), ("root", str(self.base)),
                           ("reportSha256", "wrong"), ("beforeSourceSha256", "wrong"),
                           ("afterSourceSha256", "wrong"), ("sourceSelection", {}), ("command", []),
                           ("configuration", {}), ("finishedAtUtc", "2000-01-01T00:00:00+00:00")]:
            with self.subTest(key=key):
                path.write_text(json.dumps({**valid, key: value}))
                self.assert_rejected(self.review())
        for payload in ["null", "{}", "[]", '{"status": 1, "status": 2}', "{"]:
            path.write_text(payload)
            self.assert_rejected(self.review())
        path.write_text(json.dumps(valid))
        with patch.object(coverage_provenance, "MAX_MANIFEST_BYTES", 1):
            self.assert_rejected(self.review())
        path.unlink()
        self.assert_rejected(self.review())

    def test_mutation_or_failed_tests_leave_failed_receipt(self):
        def mutated(*args):
            result = self.fake_run(*args)
            source = self.root / "sample.py"
            source.write_text(source.read_text().replace("return -1", "return -2"))
            return result
        result = self.collect(mutated)
        self.assertEqual(result["status"], "incomplete")
        self.assertIn("changed", result["diagnostics"][0]["message"])
        self.assert_rejected(self.review())
        self.output = self.base / "failed-tests"
        def failed(*args):
            self.fake_run(*args)
            return 1
        result = self.collect(failed)
        self.assertEqual(result["testExitCode"], 1)
        self.assert_rejected(self.review())

    def test_generated_source_added_during_collection_is_detected(self):
        def generated(*args):
            self.fake_run(*args)
            (self.root / "generated.py").write_text("# Generated by fixture\ndef generated():\n    return 1\n")
            return 0
        result = self.collect(generated, include_generated=True)
        self.assertEqual(result["status"], "incomplete")

    def test_cache_creation_is_not_source_drift(self):
        def cached(*args):
            self.fake_run(*args)
            (self.root / ".pytest_cache").mkdir()
            return 0
        result = self.collect(cached)
        self.assertEqual(result["status"], "complete")
        self.assertEqual(self.review()["status"], "complete")

    def test_output_reuse_nested_output_and_failures_before_execution(self):
        self.collect()
        saved = (self.output / "provenance.json").read_bytes()
        with self.assertRaises(FileExistsError):
            self.collect()
        self.assertEqual((self.output / "provenance.json").read_bytes(), saved)
        with self.assertRaises(ValueError):
            coverage_collect.collect(self.root, self.root / "output")
        for timeout in (0, -1, float("nan"), float("inf")):
            with self.assertRaises(ValueError):
                coverage_collect.collect(self.root, self.base / "invalid-timeout", timeout=timeout)
        self.output = self.base / "bad-source"
        (self.root / "broken.py").write_text("def broken(:")
        with patch.object(coverage_collect, "run_command") as runner:
            result = coverage_collect.collect(self.root, self.output)
            runner.assert_not_called()
        self.assertEqual(result["status"], "incomplete")

    def test_timeout_records_failure(self):
        def timeout(*args):
            raise ValueError("Test command timed out")
        self.assertEqual(self.collect(timeout)["status"], "incomplete")


@unittest.skipUnless(importlib.util.find_spec("coverage"), "Integration requires coverage==7.6.1 in the test interpreter")
class CollectionIntegrationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.base = Path(self.temporary.name)
        self.root = self.base / "project"
        self.root.mkdir()
        (self.root / "code.py").write_text("def choose(value):\n    if value:\n        return 1\n    return 0\n")

    def test_real_child_collection_cli_and_offline_review(self):
        (self.root / "exercise.py").write_text("import code\nfrom enum import IntEnum\nassert code.choose(True) == 1\nclass ExitCode(IntEnum):\n    OK = 0\nraise SystemExit(ExitCode.OK)\n")
        output = self.base / "run"
        command = [sys.executable, str(Path(coverage_collect.__file__)), str(self.root), "--include", "code.py",
                   "--module", "exercise", "--output-directory", str(output)]
        result = subprocess.run(command, capture_output=True, text=True, timeout=60)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        review_command = [sys.executable, str(Path(coverage_review.__file__)), str(self.root), "--include", "code.py",
                          "--coverage-json", str(output / "coverage.json"), "--collection-manifest", str(output / "provenance.json")]
        reviewed = subprocess.run(review_command, capture_output=True, text=True, timeout=60)
        self.assertEqual(reviewed.returncode, 0, reviewed.stdout + reviewed.stderr)
        report = json.loads(reviewed.stdout)
        self.assertEqual(report["functions"][0]["coverage"]["fraction"], 2 / 3)
        self.assertEqual(report["summary"]["sourceBoundEstimates"], 1)
        # A receipt copied beside a substituted JSON file must not authenticate it.
        path = output / "coverage.json"
        path.write_bytes(path.read_bytes() + b"\n")
        self.assertEqual(subprocess.run(review_command, capture_output=True, timeout=60).returncode, 2)

    def test_real_failed_test_and_mutation_never_complete(self):
        exercise = self.root / "exercise.py"
        exercise.write_text("assert False, 'intentional test failure'\n")
        failed = coverage_collect.collect(self.root, self.base / "failed", module="exercise", include=["code.py"])
        self.assertEqual(failed["status"], "incomplete")
        self.assertNotEqual(failed["testExitCode"], 0)
        exercise.write_text("from pathlib import Path\np = Path('code.py')\np.write_text(p.read_text().replace('return 0', 'return 2'))\n")
        changed = coverage_collect.collect(self.root, self.base / "changed", module="exercise", include=["code.py"])
        self.assertEqual(changed["status"], "incomplete")
        self.assertIn("changed", changed["diagnostics"][0]["message"])

    def test_real_timeout_terminates_collection(self):
        (self.root / "exercise.py").write_text("import time\ntime.sleep(60)\n")
        result = coverage_collect.collect(self.root, self.base / "timeout", module="exercise", timeout=1, include=["code.py"])
        self.assertEqual(result["status"], "incomplete")
        self.assertIn("timed out", result["diagnostics"][0]["message"])


if __name__ == "__main__":
    unittest.main()
