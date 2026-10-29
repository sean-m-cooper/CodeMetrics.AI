import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import async_scoring as scoring


class AsyncScoringTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.path = self.root / "work.py"

    def analyze(self, source, **selection):
        self.path.write_text(source, encoding="utf-8")
        result = scoring.analyze(self.root, **selection)
        self.assertEqual(result["status"], "complete", result["diagnostics"])
        return result

    def test_exact_ladder_boundaries_and_invalid_populations(self):
        cases = [(0, 0, None), (0, 1, 10), (1, 101, 10), (1, 100, 9), (1, 51, 9),
                 (1, 50, 8), (1, 21, 8), (1, 20, 6), (1, 10, 6), (11, 100, 4),
                 (15, 100, 4), (16, 100, 2), (20, 100, 2), (21, 100, 0), (1, 1, 0),
                 (999, 100000, 10), (1999, 100000, 9), (4999, 100000, 8)]
        for affected, eligible, expected in cases:
            with self.subTest(affected=affected, eligible=eligible):
                self.assertEqual(scoring.owner_score(affected, eligible), expected)
        for affected, eligible in [(-1, 1), (1, 0), (2, 1), (False, 1), (0, True), (0.0, 1), (0, -1)]:
            with self.assertRaises(ValueError):
                scoring.owner_score(affected, eligible)

    def test_catalogue_and_aliases_are_scored(self):
        for target in sorted(scoring.usage.BLOCKING):
            module, name = target.rsplit(".", 1)
            with self.subTest(target=target):
                report = self.analyze(f"from {module} import {name} as operation\nasync def run():\n    operation(1)\n")
                self.assertEqual(report["summary"]["score"], 0)
                self.assertEqual(report["summary"]["affectedOwners"], 1)
                self.assertEqual(report["functions"][0]["observations"][0]["target"], target)

    def test_owner_counting_and_population_separation(self):
        self.root.joinpath("test_other.py").write_text("import time\nasync def test_run(): time.sleep(1)\n")
        self.root.joinpath("generated.py").write_text("# Automatically generated\nasync def generated(): pass\n")
        self.root.joinpath("docs").mkdir()
        self.root.joinpath("docs", "support.py").write_text("async def support(): pass\n")
        source = "import time\ndef sync(): time.sleep(1)\nasync def run():\n    time.sleep(1)\n    time.sleep(2)\n"
        source += "".join(f"async def other_{index}(): pass\n" for index in range(49))
        report = self.analyze(source, include_tests=True, include_generated=True, include_support=True)
        self.assertEqual(report["summary"]["eligibleOwners"], 50)
        self.assertEqual(report["summary"]["affectedOwners"], 1)
        self.assertEqual(report["summary"]["score"], 8)
        self.assertEqual(report["summary"]["distinctScoredSourceCalls"], 2)
        self.assertEqual(report["summary"]["excludedOwnersByPopulation"], {"generated": 1, "support": 1, "test": 1})
        self.assertFalse(next(row for row in report["functions"] if row["population"] == "test")["affected"])

    def test_helper_reuse_counts_each_async_root_once(self):
        report = self.analyze("import time\ndef work(): time.sleep(1)\nasync def first():\n    work()\n    work()\nasync def second(): work()\n")
        self.assertEqual(report["summary"]["affectedOwners"], 2)
        self.assertEqual(report["summary"]["eligibleOwners"], 2)
        self.assertEqual(report["summary"]["distinctScoredSourceCalls"], 1)
        self.assertEqual(report["summary"]["observationPathsByClassification"], {"scored-signal": 3})

    def test_execution_context_matrix(self):
        prefix = "import time\nimport asyncio\nfrom functools import partial\nfrom asgiref.sync import sync_to_async\ndef work():\n    time.sleep(1)\n"
        cases = [
            ("time.sleep(1)", 1), ("work()", 1),
            ("await asyncio.to_thread(work)", 0), ("await asyncio.to_thread(work())", 1),
            ("await asyncio.to_thread(time.sleep, 1)", 0),
            ("await asyncio.to_thread(lambda: time.sleep(1))", 0),
            ("gen = (work() for item in items)", 0), ("gen = (item for item in work())", 1),
            ("p = partial(work)\n    p()", 1), ("p = partial(work)", 0),
            ("wrapped = sync_to_async(work)\n    await wrapped()", 0),
            ("wrapped = sync_to_async(work)", 0),
            ("values = [work() for item in items]", 1),
            ("await asyncio.sleep(1)", 0), ("yield work()", 1),
        ]
        for body, affected in cases:
            with self.subTest(body=body):
                report = self.analyze(prefix + "async def run():\n    " + body + "\n")
                self.assertEqual(report["summary"]["affectedOwners"], affected)
                self.assertEqual(report["summary"]["eligibleOwners"], 1)
                self.assertEqual(report["summary"]["score"], 0 if affected else 10)

    def test_nested_owners_do_not_inherit_blocking_or_intent(self):
        report = self.analyze("import time\nasync def outer():\n    # Blocking is intentional for the outer boundary\n    async def inner():\n        time.sleep(1)\n    def deferred(): time.sleep(1)\n")
        self.assertEqual(report["summary"]["eligibleOwners"], 2)
        self.assertEqual(report["summary"]["affectedOwners"], 1)
        self.assertEqual([row["member"] for row in report["functions"] if row["affected"]], ["outer.inner"])

    def test_adjacent_rationale_is_trusted_but_not_general_comments(self):
        cases = [
            ("# Blocking is intentional because ordering is required\n    time.sleep(1)", 0),
            ("time.sleep(1)  # Synchronous operation required by vendor", 0),
            ("# This sleep is required by the protocol\n    time.sleep(1)", 0),
            ("time.sleep(0)", 1),
            ("# unrelated explanation\n    time.sleep(1)", 1),
            ("# TODO replace this blocking operation\n    time.sleep(1)", 1),
            ("# noqa: blocking is intentional\n    time.sleep(1)", 1),
            ("# time.sleep(1)\n    time.sleep(1)", 1),
            ("# blocking\n    time.sleep(1)", 1),
            ("# Blocking intentionally allowed\n\n    time.sleep(1)", 1),
            ("# Blocking intentionally allowed\n    unrelated()\n    time.sleep(1)", 1),
            ("reason = '# Blocking is intentional'\n    time.sleep(1)", 1),
            ("time.sleep(1); other()  # Blocking intentionally allowed", 1),
            ("# Blocking intentionally allowed\n    if flag:\n        time.sleep(1)", 1),
            ("# Blocking should be avoided\n    time.sleep(1)", 1),
            ("# This call uses synchronous I/O\n    time.sleep(1)", 1),
            ("# Blocking is not intentional\n    time.sleep(1)", 1),
            ("# Replace the required synchronous operation\n    time.sleep(1)", 1),
            ("if flag: time.sleep(1)  # Blocking intentionally allowed", 1),
            ("time.sleep(\n        1\n    )  # Blocking deliberately retained", 0),
            ("résultat = time.sleep(1)  # Blocking deliberately retained", 0),
        ]
        for body, affected in cases:
            with self.subTest(body=body):
                report = self.analyze("import time\nasync def run():\n    " + body + "\n")
                self.assertEqual(report["summary"]["affectedOwners"], affected)
                observation = report["functions"][0]["observations"][0]
                self.assertEqual(bool(observation["documentation"]), not affected)

    def test_rationale_can_apply_to_one_helper_call_path(self):
        report = self.analyze("import time\ndef work():\n    time.sleep(1)\nasync def first():\n    # Blocking intentionally allowed to retain ordering\n    work()\nasync def second():\n    work()\n")
        self.assertEqual(report["summary"]["affectedOwners"], 1)
        self.assertEqual([row["member"] for row in report["functions"] if row["affected"]], ["second"])
        report = self.analyze("import time\ndef work():\n    # Blocking intentionally allowed for this protocol\n    time.sleep(1)\nasync def first(): work()\nasync def second(): work()\n")
        self.assertEqual(report["summary"]["affectedOwners"], 0)
        self.assertEqual(report["summary"]["observationPathsByClassification"], {"documented-choice": 2})

    def test_unknowns_remain_visible_and_do_not_receive_deductions(self):
        sources = [
            "async def run(): receiver.wait()\n",
            "import time\nasync def run(time): time.sleep(1)\n",
            "from other import work\nasync def run(): work()\n",
            "import time\ndef work(): yield time.sleep(1)\nasync def run(): work()\n",
        ]
        for source in sources:
            with self.subTest(source=source):
                report = self.analyze(source)
                self.assertEqual(report["summary"]["affectedOwners"], 0)
                self.assertEqual(report["summary"]["score"], 10)
        # Unrecognized imported calls are also outside the catalogue, even when
        # their import spelling resolves. The score is not all-call coverage.
        report = self.analyze(sources[0])
        self.assertEqual(report["summary"]["ownersWithUnresolvedPaths"], 1)
        self.assertTrue(report["functions"][0]["unresolvedPaths"])

    def test_unavailable_and_incomplete_are_not_tens(self):
        for source in ("", "def run(): pass\n"):
            report = self.analyze(source)
            self.assertEqual(report["summary"]["availability"], "unavailable")
            self.assertIsNone(report["summary"]["score"])
        self.path.unlink()
        self.root.joinpath("test_only.py").write_text("async def test_run(): pass\n")
        report = scoring.analyze(self.root, include_tests=True)
        self.assertEqual(report["summary"]["eligibleOwners"], 0)
        self.assertIsNone(report["summary"]["score"])
        self.path.write_text("async def broken(:\n")
        report = scoring.analyze(self.root)
        self.assertEqual(report["status"], "incomplete")
        self.assertEqual(report["functions"], [])
        self.assertIsNone(report["summary"])

    def test_input_integrity_and_no_mutation(self):
        self.path.write_text("import time\nasync def run(): time.sleep(1)\n")
        facts = scoring.usage.source_facts.analyze(self.root)
        original = scoring.usage.review(self.root, facts)
        base = scoring.helpers.review(self.root, facts, original)
        before = copy.deepcopy([facts, base])
        self.assertEqual(scoring.review(self.root, facts, base)["status"], "complete")
        self.assertEqual([facts, base], before)
        for mutation in ("functions", "summary", "configurationFingerprint", "runId"):
            changed = copy.deepcopy(base)
            changed[mutation] = [] if mutation == "functions" else "tampered"
            report = scoring.review(self.root, facts, changed)
            self.assertEqual(report["status"], "incomplete")
            self.assertIsNone(report["summary"])
            self.assertEqual(report["functions"], [])
        self.path.write_text("async def run(): pass\n")
        self.assertEqual(scoring.review(self.root, facts, base)["status"], "incomplete")

    def test_budgets_fail_closed_or_preserve_explicit_unresolved(self):
        source = "import time\ndef work(): time.sleep(1)\nasync def run(): work()\n"
        self.path.write_text(source)
        with patch.object(scoring, "MAX_NODES", 1):
            report = scoring.analyze(self.root)
            self.assertEqual(report["status"], "incomplete")
            self.assertIsNone(report["summary"])
        with patch.object(scoring.helpers, "MAX_STEPS", 0):
            self.assertEqual(scoring.analyze(self.root)["status"], "incomplete")
        with patch.object(scoring.helpers, "MAX_DEPTH", 0):
            report = scoring.analyze(self.root)
            self.assertEqual(report["status"], "complete")
            self.assertEqual(report["summary"]["affectedOwners"], 0)
            self.assertEqual(report["summary"]["unresolvedPathsByReason"], {"helper-depth-limit": 1})

    def test_cli_fresh_ids_overwrite_protection_and_no_execution(self):
        self.path.write_text("raise RuntimeError('must not execute')\nimport time\nasync def run(): time.sleep(1)\n")
        output = self.root / "report.json"
        command = [sys.executable, str(Path(scoring.__file__)), str(self.root), "--output", str(output)]
        first = subprocess.run(command, capture_output=True, text=True)
        self.assertEqual(first.returncode, 0, first.stderr)
        saved = output.read_bytes()
        report = json.loads(saved)
        self.assertEqual(report["summary"]["score"], 0)
        self.assertEqual(subprocess.run(command, capture_output=True).returncode, 2)
        self.assertEqual(output.read_bytes(), saved)
        other = scoring.analyze(self.root)
        self.assertNotEqual(report["runId"], other["runId"])
        self.assertNotEqual(report["auditId"], other["auditId"])


if __name__ == "__main__":
    unittest.main()
