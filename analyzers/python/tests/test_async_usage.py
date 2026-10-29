import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import async_usage as usage
import source_facts


class AsyncUsageTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def write(self, source, name="code.py"):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(source, encoding="utf-8")
        return path

    def report(self, source, **options):
        self.write(source)
        report = usage.analyze(self.root, **options)
        self.assertEqual(report["status"], "complete", report["diagnostics"])
        return report

    def calls(self, report):
        return [call for row in report["functions"] if row["measurement"] for call in row["measurement"]["calls"]]

    def categories(self, report):
        return [call["category"] for call in self.calls(report)]

    def test_import_aliases_match_explicit_catalogue_only(self):
        report = self.report('import time as clock\nfrom subprocess import run as launch\nimport urllib.request\n'
            'async def work():\n    clock.sleep(0)\n    launch(["tool"])\n    urllib.request.urlopen("url")\n'
            '    await clock.monotonic()\n    obj.wait()\n    obj.result()\n    sleep()\n')
        self.assertEqual(self.categories(report).count("blocking-call-candidate"), 3)
        self.assertEqual(self.categories(report).count("unresolved-call"), 3)
        self.assertEqual(self.categories(report).count("other-import-call"), 1)
        self.assertTrue(self.calls(report)[3]["directlyAwaited"])

    def test_each_blocking_catalogue_entry_has_an_import_fixture(self):
        for target in usage.BLOCKING:
            module, name = target.rsplit(".", 1)
            with self.subTest(target=target):
                report = self.report(f'from {module} import {name} as invoke\nasync def work():\n    invoke()\n')
                self.assertEqual(self.categories(report), ["blocking-call-candidate"])

    def test_nested_sync_lambda_and_async_owners_do_not_double_count(self):
        report = self.report('import time\nasync def outer():\n    time.sleep(1)\n'
            '    def sync():\n        time.sleep(2)\n    callback = lambda: time.sleep(3)\n'
            '    class Local:\n        seed = time.sleep(4)\n        async def nested(self):\n            time.sleep(5)\n'
            '    async def inner():\n        time.sleep(6)\n    return callback\n')
        self.assertEqual(report["summary"]["asyncFunctions"], 3)
        self.assertEqual(report["summary"]["excludedSynchronousFunctions"], 2)
        self.assertEqual(len(self.calls(report)), 3)

    def test_callback_references_are_not_invocations_and_eager_arguments_remain(self):
        report = self.report('import time\nimport asyncio\nasync def work():\n'
            '    await asyncio.to_thread(time.sleep, 1)\n'
            '    await asyncio.to_thread(time.sleep(1))\n'
            '    await asyncio.to_thread(lambda: time.sleep(2))\n')
        self.assertEqual(self.categories(report).count("blocking-call-candidate"), 1)
        self.assertEqual(self.categories(report).count("offload-call"), 3)
        helpers = [call for call in self.calls(report) if call["category"] == "offload-call"]
        self.assertEqual(helpers[0]["callback"]["binding"]["target"], "time.sleep")
        self.assertEqual(helpers[1]["callback"]["evaluation"], "eager-call-expression")
        self.assertEqual(helpers[2]["callback"]["kind"], "Lambda")

    def test_offload_imports_and_wrapper_setup_are_distinct(self):
        report = self.report('from anyio.to_thread import run_sync as offload\n'
            'from starlette.concurrency import run_in_threadpool\nfrom asgiref.sync import sync_to_async as wrap\n'
            'async def work():\n    await offload(fn)\n    await run_in_threadpool(fn)\n'
            '    await wrap(fn, thread_sensitive=True)(1)\n    callback = wrap(fn)\n'
            '    await loop.run_in_executor(None, fn)\n')
        categories = self.categories(report)
        self.assertEqual(categories.count("offload-call"), 2)
        self.assertEqual(categories.count("offload-wrapper-construction"), 2)
        self.assertEqual(categories.count("offload-wrapped-invocation"), 1)
        self.assertEqual(categories.count("unresolved-call"), 1)
        ids = [call["id"] for call in self.calls(report)]
        self.assertEqual(len(ids), len(set(ids)), "Nested wrapper calls need full extent identities")

    def test_generator_first_iterable_is_eager_body_is_deferred(self):
        report = self.report('import time\nasync def work():\n'
            '    deferred = (time.sleep(x) for x in time.sleep(1) if time.sleep(x))\n'
            '    eager = [time.sleep(x) for x in items]\n')
        calls = self.calls(report)
        self.assertEqual(sum(call["executionContext"] == "async-body" for call in calls), 2)
        self.assertEqual(sum(call["executionContext"] == "deferred-generator-body" for call in calls), 2)
        self.assertEqual(report["summary"]["asyncBodyCallCategories"], {"blocking-call-candidate": 2})

    def test_comprehension_shadow_does_not_leak_to_function(self):
        report = self.report('import time\nasync def work(items):\n'
            '    values = [time.sleep(1) for time in items]\n    time.sleep(2)\n')
        self.assertEqual(self.categories(report), ["unresolved-call", "blocking-call-candidate"])
        self.assertEqual(self.calls(report)[0]["binding"]["reason"], "comprehension-local-binding")

    def test_parameters_local_definitions_and_rebinding_veto_matches(self):
        cases = [
            'async def work(time):\n    time.sleep(1)\n',
            'async def work():\n    time.sleep(1)\n    time = custom\n',
            'async def work():\n    def time(): pass\n    time.sleep(1)\n',
            'async def work():\n    try: pass\n    except Exception as time: pass\n    time.sleep(1)\n',
            'async def work():\n    match value:\n        case {"x": time}: pass\n    time.sleep(1)\n',
            'async def work():\n    values = [(time := value) for value in values]\n    time.sleep(1)\n',
            'async def work():\n    del time\n    time.sleep(1)\n',
        ]
        for source in cases:
            with self.subTest(source=source):
                self.assertNotIn("blocking-call-candidate", self.categories(self.report('import time\n' + source)))

    def test_enclosing_function_binding_is_visible_but_class_binding_is_not(self):
        report = self.report('import time\nclass Container:\n    time = custom\n'
            '    async def work(self):\n        time.sleep(1)\n'
            'def outer(time):\n    async def nested():\n        time.sleep(2)\n')
        self.assertEqual(self.categories(report), ["blocking-call-candidate", "unresolved-call"])

    def test_conditional_relative_star_late_and_duplicate_imports_are_unresolved(self):
        cases = [
            'if enabled:\n    import time\nasync def work():\n    time.sleep(1)\n',
            'from . import time\nasync def work():\n    time.sleep(1)\n',
            'from plugin import *\nimport time\nasync def work():\n    time.sleep(1)\n',
            'async def work():\n    time.sleep(1)\n    import time\n',
            'import time\nimport time\nasync def work():\n    time.sleep(1)\n',
        ]
        for source in cases:
            with self.subTest(source=source):
                self.assertEqual(self.categories(self.report(source)), ["unresolved-call"])

    def test_local_imports_and_nested_closure_imports_are_supported(self):
        report = self.report('def outer():\n    import time as clock\n'
            '    async def inner():\n        clock.sleep(1)\n'
            'async def work():\n    from time import sleep\n    sleep(2)\n')
        self.assertEqual(self.categories(report), ["blocking-call-candidate"] * 2)

    def test_attribute_mutation_aliases_global_nonlocal_and_reflection_veto(self):
        cases = [
            'import time as clock\nclock.sleep = custom\n',
            'def mutate():\n    global time\n    time = custom\n',
            'def mutate():\n    setattr(time, "sleep", custom)\n',
            'exec(code)\n',
        ]
        for source in cases:
            with self.subTest(source=source):
                report = self.report('import time\n' + source + 'async def work():\n    time.sleep(1)\n')
                self.assertEqual(self.categories(report), ["unresolved-call"])
        report = self.report('def outer():\n    import time\n    def mutate():\n'
            '        nonlocal time\n        time = custom\n    async def work():\n        time.sleep(1)\n')
        self.assertEqual(self.categories(report), ["unresolved-call"])

    def test_annotations_headers_and_class_initializers_not_owned_calls(self):
        report = self.report('import time\n@decorate(time.sleep(1))\nasync def work(x=time.sleep(2)):\n'
            '    value: time.sleep(3) = time.sleep(4)\n    type Alias = time.sleep(5)\n'
            '    def nested(x=time.sleep(6)): pass\n    class Local:\n        value = time.sleep(7)\n')
        self.assertEqual(self.categories(report), ["blocking-call-candidate"])

    def test_async_generator_and_await_counts_are_syntax_only(self):
        report = self.report('async def work():\n    yield await fetch()\n'
            '    values = (await fetch() for x in items)\n')
        measurement = report["functions"][0]["measurement"]
        self.assertEqual(measurement["kind"], "async-generator")
        self.assertEqual(measurement["awaitExpressions"], {"async-body": 1, "deferred-generator-body": 1})

    def test_annotated_assignment_target_executes_but_annotation_is_excluded(self):
        report = self.report('import time\nasync def work():\n    values[time.sleep(1)]: time.sleep(2) = 3\n')
        self.assertEqual(self.categories(report), ["blocking-call-candidate"])

    def test_documented_intent_is_not_judged_or_scored(self):
        report = self.report('import time\nasync def diagnostic_probe():\n'
            '    # Intentionally block to exercise the event-loop watchdog.\n    time.sleep(0.1)\n')
        self.assertEqual(self.categories(report), ["blocking-call-candidate"])
        self.assertEqual(self.calls(report)[0]["disposition"], "review-only")
        self.assertNotIn('"score"', json.dumps(report))

    def test_call_budget_applies_across_function_owners(self):
        self.write('async def first():\n    invoke()\nasync def second():\n    invoke()\n')
        with patch.object(usage, "MAX_CALLS", 1):
            self.assert_failed(usage.analyze(self.root))

    def test_sync_only_and_empty_selection_are_unavailable_not_clean_async(self):
        report = self.report('import time\ndef work():\n    time.sleep(1)\n')
        self.assertEqual(report["summary"]["availability"], "unavailable")
        self.assertIsNone(report["summary"]["asyncBodyCallExpressions"])
        self.assertIsNone(report["summary"]["functionsWithBlockingCandidates"])
        empty = self.report('value = 1\n')
        self.assertEqual(empty["summary"]["selectedFunctions"], 0)

    def test_populations_remain_separate(self):
        self.write('import time\nasync def test_something():\n    time.sleep(1)\n', 'tests/test_code.py')
        report = self.report('async def production():\n    return 1\n', include_tests=True)
        self.assertEqual(report["summary"]["populations"]["test"]["functionsWithBlockingCandidates"], 1)
        self.assertEqual(report["summary"]["populations"]["production"]["functionsWithBlockingCandidates"], 0)
        self.assertEqual(usage.analyze(self.root)["summary"]["selectedFunctions"], 1)

    def test_stable_rows_fresh_ids_preserved_ids_no_input_mutation_or_execution(self):
        self.write('raise RuntimeError("must not execute")\nimport time\nasync def work():\n    time.sleep(1)\n')
        facts = source_facts.analyze(self.root)
        before = copy.deepcopy(facts)
        first, second = usage.review(self.root, facts), usage.review(self.root, facts)
        self.assertEqual(first["functions"], second["functions"])
        self.assertEqual(facts, before)
        self.assertNotEqual(first["runId"], second["runId"])
        replay = usage.review(self.root, facts, run_id=first["runId"], audit_id=first["auditId"])
        self.assertEqual(replay["runId"], first["runId"])
        self.assertEqual(replay["auditId"], first["auditId"])
        self.assertNotIn('"score"', json.dumps(first))
        self.assertNotIn('"findings"', json.dumps(first))

    def assert_failed(self, report):
        self.assertEqual(report["status"], "incomplete")
        self.assertEqual(report["functions"], [])
        self.assertIsNone(report["summary"])

    def test_stale_tampered_and_new_source_fail_closed(self):
        self.write('async def work():\n    return 1\n')
        facts = source_facts.analyze(self.root)
        modified = copy.deepcopy(facts)
        modified["functions"][0]["complexity"]["value"] = 99
        self.assert_failed(usage.review(self.root, modified))
        modified = copy.deepcopy(facts)
        modified["functions"] = []
        self.assert_failed(usage.review(self.root, modified))
        self.write('async def another():\n    return 2\n', 'another.py')
        self.assert_failed(usage.review(self.root, facts))
        self.write('async def work():\n    return 3\n')
        self.assert_failed(usage.review(self.root, facts))

    def test_parse_failure_and_owner_call_budgets_fail_closed(self):
        self.assert_failed(usage.analyze(self.write('async def broken(:\n')))
        self.write('async def work():\n    first()\n    second()\n')
        with patch.object(usage, "MAX_OWNERS", 0):
            self.assert_failed(usage.analyze(self.root))
        with patch.object(usage, "MAX_CALLS", 1):
            self.assert_failed(usage.analyze(self.root))

    def test_cli_writes_fresh_output_and_refuses_overwrite_or_bad_ids(self):
        target = self.write('async def work():\n    return 1\n')
        output = self.root / 'review.json'
        command = [sys.executable, usage.__file__, str(target), '--output', str(output)]
        result = subprocess.run(command, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        before = output.read_bytes()
        self.assertEqual(subprocess.run(command, capture_output=True).returncode, 2)
        self.assertEqual(output.read_bytes(), before)
        self.assertEqual(subprocess.run([sys.executable, usage.__file__, str(target), '--run-id', 'bad'], capture_output=True).returncode, 2)


if __name__ == '__main__':
    unittest.main()
