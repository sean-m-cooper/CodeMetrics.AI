import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import async_context as context
import async_usage as usage
import source_facts


class AsyncContextTests(unittest.TestCase):
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
        report = context.analyze(self.root, **options)
        self.assertEqual(report["status"], "complete", report["diagnostics"])
        return report

    def calls(self, report):
        return [call for row in report["functions"] for call in row["calls"]]

    def linked(self, report, field, status):
        return [call[field] for call in self.calls(report) if call[field] and call[field]["status"] == status]

    def test_unresolved_inventory_has_joint_shapes_reasons_examples_and_separate_contexts(self):
        report = self.report('async def work(obj):\n    obj.read()\n    missing()\n    factory()()\n'
            '    factory().read()\n    items[0]()\n    deferred = (obj.read() for x in items)\n')
        direct = report["summary"]["contexts"]["async-body"]
        self.assertEqual(direct["unresolvedShapes"], {"attribute-path": 1, "name": 3, "call-result": 1,
            "attribute-on-expression": 1, "subscript": 1})
        self.assertEqual(sum(row["callExpressions"] for row in direct["unresolvedInventory"]), 7)
        self.assertEqual(report["summary"]["contexts"]["deferred-generator-body"]["callExpressions"], 1)
        self.assertEqual(sum(direct["unresolvedReasons"].values()), 7)

    def test_local_callback_links_do_not_traverse_or_count_body(self):
        report = self.report('import asyncio\nimport time\ndef work():\n    time.sleep(1)\n'
            'async def run():\n    await asyncio.to_thread(work)\n    work()\n')
        callback = next(call["callback"] for call in self.calls(report) if call["callback"])
        self.assertEqual(callback["reference"]["status"], "local-definition")
        target = callback["reference"]["definition"]["ownerId"]
        self.assertEqual(next(row for row in report["functions"] if row["id"] == target)["status"], "excluded")
        self.assertEqual(len(self.calls(report)), 2)
        self.assertEqual(len(self.linked(report, "localCallee", "local-definition")), 1)
        self.assertNotIn('"score"', json.dumps(report))
        self.assertNotIn('"findings"', json.dumps(report))

    def test_stored_wrapper_links_construction_once_and_callback_definition(self):
        report = self.report('from asgiref.sync import sync_to_async as wrap\n'
            'async def run():\n    def work(x):\n        return x + 1\n'
            '    wrapped = wrap(work)\n    await wrapped(1)\n    await wrapped(2)\n')
        wrappers = self.linked(report, "storedWrapper", "stored-wrapper-syntax")
        self.assertEqual(len(wrappers), 2)
        ids = {row["definition"]["constructionCallId"] for row in wrappers}
        self.assertEqual(len(ids), 1)
        self.assertTrue(ids <= {row["callId"] for row in self.calls(report)})
        counts = report["summary"]["contexts"]["async-body"]
        self.assertEqual(counts["callExpressions"], 3)
        self.assertEqual(counts["unresolvedImportCallExpressions"], 2)
        self.assertEqual(counts["storedWrapperInvocationLinks"], 2)
        self.assertEqual(counts["localCallbackReferenceLinks"], 3)  # Three references; not executions.

    def test_same_line_before_use_and_unicode_ids(self):
        report = self.report('from asgiref.sync import sync_to_async\nasync def run():\n'
            '    café = sync_to_async(work); await café()\n')
        self.assertEqual(len(self.linked(report, "storedWrapper", "stored-wrapper-syntax")), 1)

    def test_ambiguous_conditional_and_reassigned_wrappers_are_withheld(self):
        bodies = [
            '    wrapped = wrap(work)\n    wrapped = other\n    await wrapped()\n',
            '    if flag:\n        wrapped = wrap(work)\n    await wrapped()\n',
            '    wrapped: object = wrap(work)\n    await wrapped()\n',
            '    wrapped = alias = wrap(work)\n    await wrapped()\n',
            '    wrapped = wrap(work)\n    del wrapped\n    await wrapped()\n',
            '    await wrapped()\n    wrapped = wrap(work)\n',
            '    wrapped = wrap(*callbacks)\n    await wrapped()\n',
            '    wrapped = wrap(func=work)\n    await wrapped()\n',
            '    wrapped = wrap(work)\n    global something\n    something = 1\n    wrapped += other\n    await wrapped()\n',
            '    wrapped = wrap(work)\n    values = [(wrapped := item) for item in items]\n    await wrapped()\n',
            '    wrapped = wrap(work)\n    try: pass\n    except Exception as wrapped: pass\n    await wrapped()\n',
            '    wrapped = wrap(work)\n    match value:\n        case {"x": wrapped}: pass\n    await wrapped()\n',
        ]
        for body in bodies:
            with self.subTest(body=body):
                report = self.report('from asgiref.sync import sync_to_async as wrap\nasync def run():\n' + body)
                self.assertEqual(self.linked(report, "storedWrapper", "stored-wrapper-syntax"), [])

    def test_cross_owner_stored_wrapper_and_parameters_are_not_followed(self):
        report = self.report('from asgiref.sync import sync_to_async as wrap\nwrapped = wrap(work)\n'
            'async def first():\n    await wrapped()\n'
            'async def second(wrapped):\n    wrapped = wrap(work)\n    await wrapped()\n'
            'async def outer():\n    wrapped = wrap(work)\n    async def inner():\n        await wrapped()\n')
        self.assertEqual(self.linked(report, "storedWrapper", "stored-wrapper-syntax"), [])

    def test_import_shadow_mutation_and_reflection_cannot_create_wrapper_links(self):
        sources = [
            'import asgiref.sync as sync\nsync.sync_to_async = other\nasync def run():\n    wrapped = sync.sync_to_async(work)\n    await wrapped()\n',
            'from asgiref.sync import sync_to_async as wrap\nasync def run(wrap):\n    wrapped = wrap(work)\n    await wrapped()\n',
            'from asgiref.sync import sync_to_async as wrap\nexec(code)\nasync def run():\n    wrapped = wrap(work)\n    await wrapped()\n',
            'from asgiref.sync import sync_to_async as wrap\nasync def run():\n    global wrapped\n    wrapped = wrap(work)\n    await wrapped()\n',
        ]
        for source in sources:
            with self.subTest(source=source):
                self.assertEqual(self.linked(self.report(source), "storedWrapper", "stored-wrapper-syntax"), [])

    def test_wrapper_shadow_and_deferred_body_not_credited_but_first_iterable_is_eager(self):
        report = self.report('from asgiref.sync import sync_to_async as wrap\nasync def run(items):\n'
            '    wrapped = wrap(work)\n    a = [wrapped() for wrapped in items]\n'
            '    b = (wrapped() for x in wrapped())\n    await wrapped()\n')
        self.assertEqual(len(self.linked(report, "storedWrapper", "stored-wrapper-syntax")), 2)
        deferred = [call for call in self.calls(report) if call["executionContext"] == "deferred-generator-body"]
        self.assertEqual(deferred[0]["storedWrapper"]["reason"], "deferred-execution-context")

    def test_callback_is_bound_at_construction_not_use_site_comprehension(self):
        report = self.report('from asgiref.sync import sync_to_async as wrap\nasync def run(items):\n'
            '    def work(x):\n        return x + 1\n    wrapped = wrap(work)\n'
            '    values = [wrapped(x) for work in items for x in items]\n')
        invocation = next(call for call in self.calls(report) if call["storedWrapper"])
        self.assertEqual(invocation["callback"]["reference"]["status"], "local-definition")

    def test_decorated_conditional_late_rebound_and_parameter_callbacks_unresolved(self):
        sources = [
            '@decorate\ndef work():\n    return 1\nasync def run():\n    await asyncio.to_thread(work)\n',
            'if flag:\n    def work():\n        return 1\nasync def run():\n    await asyncio.to_thread(work)\n',
            'async def run():\n    await asyncio.to_thread(work)\ndef work():\n    return 1\n',
            'def work():\n    return 1\nwork = other\nasync def run():\n    await asyncio.to_thread(work)\n',
            'def work():\n    return 1\nasync def run(work):\n    await asyncio.to_thread(work)\n',
        ]
        for source in sources:
            with self.subTest(source=source):
                report = self.report('import asyncio\n' + source)
                callback = next(call["callback"] for call in self.calls(report) if call["callback"])
                self.assertEqual(callback["reference"]["status"], "unresolved")

    def test_class_namespace_is_not_lexical_closure_and_method_receivers_stay_unresolved(self):
        report = self.report('import asyncio\ndef work():\n    return 1\nclass Container:\n'
            '    def work(self):\n        return 2\n    async def run(self):\n'
            '        await asyncio.to_thread(work)\n        await asyncio.to_thread(self.work)\n')
        callbacks = [call["callback"] for call in self.calls(report)]
        self.assertEqual(callbacks[0]["reference"]["definition"]["line"], 2)
        self.assertEqual(callbacks[1]["reference"]["status"], "unresolved")

    def test_eager_callback_result_is_not_a_function_reference(self):
        report = self.report('import asyncio\ndef work():\n    return 1\nasync def run():\n'
            '    await asyncio.to_thread(work())\n    await asyncio.to_thread(lambda: work())\n')
        callbacks = [call["callback"] for call in self.calls(report) if call["callback"]]
        self.assertEqual(callbacks[0]["evaluation"], "eager-call-expression")
        self.assertEqual(callbacks[0]["reference"]["status"], "unresolved")
        self.assertEqual(callbacks[1]["reference"]["status"], "unresolved")
        self.assertEqual(len(self.calls(report)), 3)

    def test_excluded_declarations_and_imports_do_not_become_local_definitions(self):
        report = self.report('import asyncio\nfrom external import work\nfrom typing import overload\n@overload\ndef stub(): ...\nasync def run():\n'
            '    await asyncio.to_thread(work)\n    await asyncio.to_thread(stub)\n')
        callbacks = [call["callback"] for call in self.calls(report)]
        self.assertEqual(callbacks[0]["reference"]["status"], "import-spelling")
        self.assertEqual(callbacks[1]["reference"]["reason"], "decorated-definition")

    def test_async_callback_kind_is_recorded_without_claiming_valid_offload(self):
        report = self.report('import asyncio\nasync def work():\n    return 1\nasync def run():\n'
            '    await asyncio.to_thread(work)\n')
        self.assertEqual(self.calls(report)[0]["callback"]["reference"]["definition"]["functionKind"], "AsyncFunctionDef")

    def test_populations_empty_and_sync_only_unavailable_examples_bounded(self):
        self.write('async def test_work():\n    missing()\n', 'tests/test_work.py')
        report = self.report('async def run():\n' + '    missing()\n' * 5, include_tests=True)
        self.assertEqual(len(report["summary"]["contexts"]["async-body"]["unresolvedInventory"][0]["examples"]), 3)
        self.assertEqual(report["summary"]["populations"]["test"]["contexts"]["async-body"]["callExpressions"], 1)
        for source in ('def run():\n    missing()\n', 'value = 1\n'):
            report = self.report(source)
            self.assertEqual(report["summary"]["availability"], "unavailable")
            self.assertIsNone(report["summary"]["contexts"]["async-body"])

    def assert_failed(self, report):
        self.assertEqual(report["status"], "incomplete")
        self.assertEqual(report["functions"], [])
        self.assertIsNone(report["summary"])
        self.assertTrue(report["diagnostics"])

    def test_stale_input_selection_tampering_and_source_drift_fail_closed(self):
        self.write('async def run():\n    missing()\n')
        facts = source_facts.analyze(self.root)
        base = usage.review(self.root, facts)
        before = copy.deepcopy([facts, base])
        report = context.review(self.root, facts, base)
        self.assertEqual(report["status"], "complete")
        self.assertEqual([facts, base], before)
        self.assertEqual(report["runId"], base["runId"])
        for field, value in (("runId", "bad"), ("functions", []), ("summary", {}), ("subject", {}), ("policy", "unsupported")):
            changed = {**base, field: value}
            self.assert_failed(context.review(self.root, facts, changed))
        changed = copy.deepcopy(facts)
        changed["functions"][0]["complexity"]["value"] = 100
        self.assert_failed(context.review(self.root, changed, base))
        self.write('async def added():\n    return 1\n', 'new.py')
        self.assert_failed(context.review(self.root, facts, base))
        self.write('async def run():\n    other()\n')
        self.assert_failed(context.review(self.root, facts, base))

    def test_parse_and_budget_failures_no_partial_rows(self):
        self.write('async def broken(:\n')
        self.assert_failed(context.analyze(self.root))
        self.write('async def first():\n    invoke()\nasync def second():\n    invoke()\n')
        for name, limit in (("MAX_OWNERS", 0), ("MAX_CALLS", 1)):
            with patch.object(usage, name, limit):
                self.assert_failed(context.analyze(self.root))

    def test_fresh_deterministic_cli_no_execution_or_overwrite(self):
        target = self.write('raise RuntimeError("do not execute")\nasync def run():\n    invoke()\n')
        first, second = context.analyze(target), context.analyze(target)
        self.assertEqual(first["functions"], second["functions"])
        self.assertEqual(first["summary"], second["summary"])
        self.assertNotEqual(first["runId"], second["runId"])
        output = self.root / 'context.json'
        command = [sys.executable, context.__file__, str(target), '--output', str(output)]
        result = subprocess.run(command, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        before = output.read_bytes()
        self.assertEqual(subprocess.run(command, capture_output=True).returncode, 2)
        self.assertEqual(output.read_bytes(), before)
        self.assertEqual(subprocess.run([sys.executable, context.__file__, str(target), '--run-id', 'bad'], capture_output=True).returncode, 2)


if __name__ == '__main__':
    unittest.main()
