import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import async_helper_paths as helpers
import async_usage as usage
import error_handling_dispositions as dispositions
import error_handling_operations as operations
import behavior_accuracy


class BehaviorTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def write(self, source):
        path = self.root / "work.py"
        path.write_text(source, encoding="utf-8")
        return path

    def analyze(self, analyzer, source):
        self.write(source)
        report = analyzer.analyze(self.root)
        self.assertEqual(report["status"], "complete", report["diagnostics"])
        self.assertNotIn('"score"', json.dumps(report))
        self.assertNotIn('"findings"', json.dumps(report))
        return report

    def handler(self, body):
        return "def run(flag):\n    try: work()\n    except Error:\n" + "".join("        " + line + "\n" for line in body.splitlines())

    def test_curated_accuracy_contract(self):
        report = behavior_accuracy.evaluate()
        self.assertTrue(report["passed"], report["metrics"])
        self.assertEqual(len(report["cases"]), 27)

    def test_handler_labeled_positive_and_counterexample_matrix(self):
        cases = [
            ("raise", "propagation"),
            ("raise Wrapped() from None", "propagation"),
            ("return None", "explicit-fallback"),
            ("return recover()", "explicit-fallback"),
            ("recover()", "recovery-action"),
            ("fallback = None", "recovery-action"),
            ("pass # Deliberate optional operation", "documented-suppression"),
            ("pass # noqa", "unexplained-swallowing-candidate"),
            ("pass", "unexplained-swallowing-candidate"),
            ("if flag:\n    raise\nelse:\n    raise Wrapped()", "propagation"),
            ("if flag:\n    return None\nraise", "mixed-explicit-disposition"),
            ("if flag:\n    recover()\nelse:\n    recover_other()", "recovery-action"),
            ("if flag:\n    recover()", "unresolved"),
            ("if flag:\n    pass\nraise", "propagation"),
            ("return None\nunknown()", "explicit-fallback"),
            ("if flag:\n    raise\nrecover()", "mixed-explicit-disposition"),
            ("with manager():\n    recover()\nraise", "unresolved"),
            ("for item in items:\n    recover(item)\nraise", "unresolved"),
            ("def recover():\n    log()", "unresolved"),
            ("return (yield value)", "unresolved"),
        ]
        for body, expected in cases:
            with self.subTest(body=body):
                result = self.analyze(dispositions, self.handler(body))
                self.assertEqual(result["handlers"][0]["classification"], expected)

    def test_handler_context_does_not_become_swallowing(self):
        cases = [
            "def run():\n    try: work()\n    except Error: pass\n    return None\n",
            "def run():\n    # Probe whether work is supported\n    try: work()\n    except Error: pass\n",
            "def run():\n    try: work()\n    except Error: pass\n    else: accepted()\n",
            "def run():\n    try: work()\n    except Error: pass\n    finally: cleanup()\n",
            "def run():\n    try: work()\n    except* Error: pass\n",
            "def run():\n    for item in items:\n        try: return probe(item)\n        except Missing: pass\n    raise Missing()\n",
            "async def run():\n    try:\n        while True: yield await receive()\n    except Disconnected: pass\n",
            "def run():\n    try: return read()\n    except Missing: pass\n    except Invalid: raise BadConfig()\n",
        ]
        for source in cases:
            with self.subTest(source=source):
                row = self.analyze(dispositions, source)["handlers"][0]
                self.assertNotEqual(row["classification"], "unexplained-swallowing-candidate")

    def test_import_guard_and_loop_transfer(self):
        source = "try:\n    import optional\nexcept ImportError:\n    pass\n"
        self.assertEqual(self.analyze(dispositions, source)["handlers"][0]["classification"], "explicit-fallback")
        source = "def run():\n    for item in items:\n        try: work()\n        except Error: continue\n"
        self.assertEqual(self.analyze(dispositions, source)["handlers"][0]["classification"], "loop-transfer")

    def test_nested_handlers_are_not_credited_to_parent(self):
        report = self.analyze(dispositions, self.handler("try:\n    work()\nexcept Error:\n    raise"))
        self.assertEqual(len(report["handlers"]), 2)
        self.assertEqual([row["classification"] for row in report["handlers"]], ["unresolved", "unresolved"])

    def candidates(self, report, mode="async-body"):
        return [trace for row in report["functions"] for trace in row["traces"]
                if trace["status"] == "blocking-candidate" and trace["executionContext"] == mode]

    def test_direct_helper_and_thread_callback_keep_separate_paths(self):
        source = "import time\nimport asyncio\ndef work():\n    time.sleep(1)\nasync def run():\n    work()\n    await asyncio.to_thread(work)\n"
        report = self.analyze(helpers, source)
        self.assertEqual(len(self.candidates(report)), 1)
        self.assertEqual(len(self.candidates(report, "thread-helper-callback")), 1)
        self.assertEqual(len(self.candidates(report)[0]["path"]), 2)
        self.assertEqual(report["summary"]["distinctBlockingSourceCalls"], 1)
        self.assertEqual(report["summary"]["functionsWithDirectOrHelperCandidates"], 1)

    def test_eager_arguments_are_not_offloaded(self):
        source = "import time\nimport asyncio\ndef work():\n    time.sleep(1)\nasync def run():\n    await asyncio.to_thread(work())\n"
        report = self.analyze(helpers, source)
        self.assertEqual(len(self.candidates(report)), 1)
        self.assertEqual(len(self.candidates(report, "thread-helper-callback")), 0)

    def test_partial_and_wrapper_construction_are_not_invocations(self):
        source = "import time\nimport asyncio\nfrom functools import partial\nfrom asgiref.sync import sync_to_async\ndef work():\n    time.sleep(1)\nasync def run():\n    p = partial(work)\n    p()\n    await asyncio.to_thread(p)\n    wrapped = sync_to_async(work)\n    await wrapped()\n"
        report = self.analyze(helpers, source)
        self.assertEqual(len(self.candidates(report)), 1)
        self.assertEqual(len(self.candidates(report, "thread-helper-callback")), 2)
        self.assertEqual(len(self.candidates(report, "wrapper-callback-reference")), 1)

    def test_imported_callback_and_decorator_context(self):
        source = "import time\nimport asyncio\nfrom asgiref.sync import sync_to_async\n@sync_to_async\ndef work():\n    time.sleep(1)\nasync def run():\n    await work()\n    await asyncio.to_thread(time.sleep, 1)\n"
        report = self.analyze(helpers, source)
        self.assertEqual(len(self.candidates(report)), 0)
        self.assertEqual(len(self.candidates(report, "thread-helper-callback")), 2)

    def test_deferred_generators_and_coroutine_construction_are_not_direct_calls(self):
        source = "import time\ndef gen():\n    time.sleep(1)\n    yield 1\nasync def other():\n    time.sleep(1)\nasync def run():\n    gen()\n    other()\n    deferred = (time.sleep(1) for x in items)\n"
        report = self.analyze(helpers, source)
        run_row = next(row for row in report["functions"] if row["member"].endswith("run"))
        self.assertFalse(any(trace["status"] == "blocking-candidate" and trace["executionContext"] == "async-body" for trace in run_row["traces"]))
        self.assertEqual(len(self.candidates(report, "deferred-generator-body")), 1)
        self.assertIn("generator-body-deferred", report["summary"]["unresolvedPathsByReason"])

    def test_shadowed_mutated_decorated_and_cross_file_helpers_are_unresolved(self):
        cases = [
            "async def run(work):\n    work()\n",
            "async def run():\n    work = custom\n    work()\n",
            "async def run():\n    alias = work\n    alias()\n",
            "async def run():\n    obj.work()\n",
        ]
        for tail in cases:
            with self.subTest(tail=tail):
                report = self.analyze(helpers, "import time\ndef work():\n    time.sleep(1)\n" + tail)
                self.assertEqual(len(self.candidates(report)), 0)
        report = self.analyze(helpers, "import time\n@custom\ndef work():\n    time.sleep(1)\nasync def run():\n    work()\n")
        self.assertEqual(len(self.candidates(report)), 0)
        report = self.analyze(helpers, "import time\ndef work():\n    time.sleep(1)\nasync def run():\n    work()\ntime.sleep = custom\n")
        self.assertEqual(len(self.candidates(report)), 0)

    def test_depth_limit_is_explicit(self):
        source = "import time\ndef a():\n    time.sleep(1)\ndef b():\n    a()\ndef c():\n    b()\ndef d():\n    c()\nasync def run():\n    d()\n"
        report = self.analyze(helpers, source)
        self.assertEqual(len(self.candidates(report)), 0)
        self.assertIn("helper-depth-limit", report["summary"]["unresolvedPathsByReason"])

    def test_counts_each_async_owner_once_and_preserves_population(self):
        source = "import time\ndef work():\n    time.sleep(1)\nasync def first():\n    work()\n    work()\nasync def second():\n    work()\n"
        report = self.analyze(helpers, source)
        self.assertEqual(report["summary"]["asyncFunctions"], 2)
        self.assertEqual(report["summary"]["functionsWithDirectOrHelperCandidates"], 2)
        self.assertEqual(report["summary"]["candidatePaths"], 3)
        self.assertEqual(report["summary"]["distinctBlockingSourceCalls"], 1)
        self.assertEqual(report["summary"]["populations"]["production"]["asyncFunctions"], 2)

    def test_local_chain_and_comprehension_shadowing(self):
        source = "import time\ndef inner():\n    time.sleep(1)\ndef outer():\n    inner()\nasync def run():\n    outer()\n    [outer() for outer in callbacks]\n"
        report = self.analyze(helpers, source)
        self.assertEqual(len(self.candidates(report)), 1)
        self.assertEqual(len(self.candidates(report)[0]["path"]), 3)
        self.assertIn("comprehension-local-binding", report["summary"]["unresolvedPathsByReason"])

    def test_incomplete_sources_and_no_execution(self):
        for reviewer, key in ((helpers, "functions"), (dispositions, "handlers")):
            with self.subTest(reviewer=reviewer.__name__):
                self.write("async def broken(:\n")
                failed = reviewer.analyze(self.root)
                self.assertEqual(failed["status"], "incomplete")
                self.assertEqual(failed[key], [])
                self.assertIsNone(failed["summary"])
                self.write("raise RuntimeError('target must never execute')\n")
                self.assertEqual(reviewer.analyze(self.root)["status"], "complete")

    def test_failure_drift_tampering_budgets_and_input_immutability(self):
        self.write("import time\nasync def run():\n    time.sleep(1)\n    try: work()\n    except Error: pass\n")
        facts = usage.source_facts.analyze(self.root)
        for reviewer, producer, rows_key in [(helpers, usage, "functions"), (dispositions, operations, "handlers")]:
            with self.subTest(reviewer=reviewer.__name__):
                base = producer.analyze(self.root)
                saved = copy.deepcopy([facts, base])
                self.assertEqual(reviewer.review(self.root, facts, base)["status"], "complete")
                self.assertEqual([facts, base], saved)
                altered = copy.deepcopy(base)
                altered[rows_key] = []
                self.assertEqual(reviewer.review(self.root, facts, altered)["status"], "incomplete")
                budget = "MAX_STEPS" if reviewer is helpers else "MAX_STATEMENTS"
                with patch.object(reviewer, budget, 0):
                    failed = reviewer.review(self.root, facts, base)
                self.assertEqual(failed["status"], "incomplete")
                self.assertEqual(failed[rows_key], [])
                self.assertIsNone(failed["summary"])
        base = usage.analyze(self.root)
        self.write("async def changed(): pass\n")
        self.assertEqual(helpers.review(self.root, facts, base)["status"], "incomplete")

    def test_empty_population_and_cli_output_preservation(self):
        for reviewer in (helpers, dispositions):
            with self.subTest(reviewer=reviewer.__name__):
                source = self.write("value = 1\n")
                report = reviewer.analyze(self.root)
                self.assertEqual(report["status"], "complete")
                output = self.root / (reviewer.__name__ + ".json")
                command = [sys.executable, reviewer.__file__, str(source), "--output", str(output)]
                first = subprocess.run(command, capture_output=True, text=True)
                self.assertEqual(first.returncode, 0, first.stderr)
                saved = output.read_bytes()
                self.assertEqual(subprocess.run(command, capture_output=True).returncode, 2)
                self.assertEqual(output.read_bytes(), saved)


if __name__ == "__main__":
    unittest.main()
