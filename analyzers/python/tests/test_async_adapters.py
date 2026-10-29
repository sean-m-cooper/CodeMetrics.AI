import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import async_adapters as adapters
import async_context
import async_usage as usage
import source_facts


class AdapterTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def write(self, source, name="code.py"):
        file = self.root / name
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(source, encoding="utf-8")
        return file

    def report(self, source, **options):
        self.write(source)
        report = adapters.analyze(self.root, **options)
        self.assertEqual(report["status"], "complete", report["diagnostics"])
        return report

    def calls(self, report):
        return [call for owner in report["functions"] for call in owner["calls"]]

    def links(self, report, field, status):
        return [call[field] for call in self.calls(report) if call[field] and call[field]["status"] == status]

    def test_partial_records_bound_expressions_without_values_or_execution_claims(self):
        report = self.report('import functools as f\nimport asyncio\ndef work(*args, **kwargs):\n    return 1\n'
            'async def run(args, kwargs):\n    callback = f.partial(work, 1, *args, limit=2, **kwargs)\n'
            '    await asyncio.to_thread(callback)\n    callback()\n')
        partial = self.links(report, "partialConstruction", "partial-construction-syntax")[0]
        self.assertEqual(partial["callback"]["reference"]["status"], "local-definition")
        self.assertEqual([arg["kind"] for arg in partial["boundPositionalExpressions"]], ["Constant", "Starred"])
        self.assertEqual([kw["name"] for kw in partial["keywordExpressions"]], ["limit", None])
        self.assertTrue(partial["keywordExpressions"][1]["unpacking"])
        self.assertEqual(len(self.links(report, "callbackPartial", "stored-partial-syntax")), 1)
        self.assertEqual(len(self.links(report, "partialCallee", "stored-partial-syntax")), 1)
        self.assertEqual(report["summary"]["contexts"]["async-body"]["callExpressions"], 3)
        self.assertNotIn('"score"', json.dumps(report))
        self.assertNotIn('"findings"', json.dumps(report))

    def test_starlette_parameter_rebinding_records_construction_without_inventing_identity(self):
        report = self.report('import functools\nimport anyio\nasync def run(func, *args, **kwargs):\n'
            '    func = functools.partial(func, *args, **kwargs)\n    return await anyio.to_thread.run_sync(func)\n')
        partial = self.links(report, "partialConstruction", "partial-construction-syntax")[0]
        self.assertEqual(partial["callback"]["reference"]["status"], "unresolved")
        self.assertEqual(self.links(report, "callbackPartial", "stored-partial-syntax"), [])

    def test_inline_partial_and_immediate_invocation_share_owned_construction_id(self):
        report = self.report('from functools import partial as p\nfrom asyncio import to_thread\n'
            'async def run(work):\n    await to_thread(p(work, 1))\n    p(work, 2)()\n')
        callbacks = self.links(report, "callbackPartial", "inline-partial-syntax")
        callees = self.links(report, "partialCallee", "inline-partial-syntax")
        self.assertEqual((len(callbacks), len(callees)), (1, 1))
        ids = {call["callId"] for call in self.calls(report) if call["partialConstruction"]}
        self.assertEqual({row["constructionCallId"] for row in callbacks + callees}, ids)

    def test_eager_factory_lambda_and_nested_partial_do_not_gain_callback_definition(self):
        report = self.report('from functools import partial\ndef factory():\n    return 1\n'
            'async def run(work):\n    partial(factory())\n    partial(lambda: work())\n    partial(partial(work, 1), 2)\n')
        rows = self.links(report, "partialConstruction", "partial-construction-syntax")
        self.assertEqual(len(rows), 4)
        self.assertTrue(all(row["callback"]["reference"]["status"] == "unresolved" for row in rows))
        self.assertEqual(len(self.calls(report)), 5)  # No traversal of the lambda body.

    def test_missing_or_starred_callback_layout_is_visible_but_stored_link_withheld(self):
        for construction in ('partial()', 'partial(*args)', 'partial(func=work)'):
            with self.subTest(construction=construction):
                report = self.report('from functools import partial\nimport asyncio\nasync def run(args, work):\n'
                    f'    callback = {construction}\n    await asyncio.to_thread(callback)\n')
                partial = self.links(report, "partialConstruction", "partial-construction-syntax")[0]
                self.assertEqual(partial["argumentLayout"], "unresolved")
                self.assertIsNone(partial["boundPositionalExpressions"])
                self.assertEqual(self.links(report, "callbackPartial", "stored-partial-syntax"), [])

    def test_stored_partial_ambiguous_bindings_are_withheld(self):
        bodies = [
            '    callback = partial(work)\n    callback = other\n    callback()\n',
            '    if flag:\n        callback = partial(work)\n    callback()\n',
            '    callback: object = partial(work)\n    callback()\n',
            '    callback = alias = partial(work)\n    callback()\n',
            '    callback = partial(work)\n    del callback\n    callback()\n',
            '    callback()\n    callback = partial(work)\n',
            '    callback = partial(work)\n    exec(code)\n    callback()\n',
            '    global callback\n    callback = partial(work)\n    callback()\n',
            '    callback = partial(work)\n    callback += other\n    callback()\n',
            '    callback = partial(work)\n    a = [(callback := x) for x in items]\n    callback()\n',
            '    callback = partial(work)\n    try: pass\n    except Exception as callback: pass\n    callback()\n',
            '    callback = partial(work)\n    match x:\n        case {"callback": callback}: pass\n    callback()\n',
        ]
        for body in bodies:
            with self.subTest(body=body):
                report = self.report('from functools import partial\nasync def run(work):\n' + body)
                self.assertEqual(self.links(report, "partialCallee", "stored-partial-syntax"), [])

    def test_cross_owner_alias_and_parameter_partial_not_followed(self):
        report = self.report('from functools import partial\ncallback = partial(work)\n'
            'async def run(callback):\n    callback()\n    local = partial(work)\n    alias = local\n    alias()\n'
            '    async def inner():\n        local()\n')
        self.assertEqual(self.links(report, "partialCallee", "stored-partial-syntax"), [])

    def test_partial_comprehension_shadow_and_deferred_contexts(self):
        report = self.report('from functools import partial\nasync def run(items):\n'
            '    callback = partial(work)\n    a = [callback() for callback in items]\n'
            '    b = (callback() for x in callback())\n    c = (partial(work) for x in items)\n')
        self.assertEqual(len(self.links(report, "partialCallee", "stored-partial-syntax")), 1)
        deferred = report["summary"]["contexts"]["deferred-generator-body"]
        self.assertEqual(deferred["partialConstructions"], 1)
        self.assertEqual(deferred["partialCalleeReferences"], 0)

    def test_stored_wrapper_uses_callback_at_construction_not_use_site(self):
        report = self.report('from functools import partial\nfrom asgiref.sync import sync_to_async\n'
            'async def run(items):\n    callback = partial(work)\n    wrapped = sync_to_async(callback)\n'
            '    a = [wrapped() for callback in items]\n')
        links = self.links(report, "callbackPartial", "stored-partial-syntax")
        self.assertEqual(len(links), 2)
        self.assertEqual(len({row["constructionCallId"] for row in links}), 1)

    def test_same_line_assignment_and_unicode_names(self):
        report = self.report('from functools import partial\nasync def run():\n    café = partial(work); café()\n')
        self.assertEqual(len(self.links(report, "partialCallee", "stored-partial-syntax")), 1)

    def test_import_mutation_shadowing_conditional_and_late_imports_withhold_partial(self):
        for source in (
            'import functools\nfunctools.partial = fake\nasync def run():\n    functools.partial(work)\n',
            'from functools import partial\nasync def run(partial):\n    partial(work)\n',
            'if flag:\n    from functools import partial\nasync def run():\n    partial(work)\n',
            'async def run():\n    partial(work)\nfrom functools import partial\n',
            'from functools import partial\nexec(code)\nasync def run():\n    partial(work)\n',
        ):
            with self.subTest(source=source):
                self.assertEqual(self.links(self.report(source), "partialConstruction", "partial-construction-syntax"), [])

    def test_bare_and_factory_decorators_link_without_traversing_sync_body(self):
        for spelling in ('wrap', 'wrap()', 'wrap(thread_sensitive=False)', 'wrap(thread_sensitive=True)'):
            with self.subTest(spelling=spelling):
                report = self.report('from asgiref.sync import sync_to_async as wrap\n'
                    f'@{spelling}\ndef work():\n    time.sleep(1)\nasync def run():\n    await work()\n')
                linked = self.links(report, "decoratedCallee", "decorated-wrapper-syntax")
                self.assertEqual(len(linked), 1)
                owner = next(row for row in report["functions"] if row["id"] == linked[0]["definition"]["ownerId"])
                self.assertEqual(owner["status"], "excluded")
                self.assertEqual(len(self.calls(report)), 1)

    def test_decorator_header_resolved_in_definition_scope(self):
        report = self.report('from asgiref.sync import sync_to_async\n@sync_to_async\ndef work():\n    return 1\n'
            'async def run(sync_to_async):\n    await work()\n')
        self.assertEqual(len(self.links(report, "decoratedCallee", "decorated-wrapper-syntax")), 1)

    def test_decorated_callback_is_context_not_valid_offload_claim(self):
        report = self.report('from asgiref.sync import sync_to_async\nimport asyncio\n'
            '@sync_to_async\ndef work():\n    return 1\nasync def run():\n    await asyncio.to_thread(work)\n')
        self.assertEqual(len(self.links(report, "callbackDecorator", "decorated-wrapper-syntax")), 1)

    def test_unknown_stacked_async_and_unsupported_decorator_arguments_withheld(self):
        headers = ['@other', '@wrap\n@other', '@other\n@wrap', '@wrap(executor=pool)',
                   '@wrap(thread_sensitive=flag)', '@wrap(**options)', '@wrap(work)', '@wrap(*args)']
        for header in headers:
            with self.subTest(header=header):
                report = self.report('from asgiref.sync import sync_to_async as wrap\n'
                    f'{header}\ndef work():\n    return 1\nasync def run():\n    await work()\n')
                self.assertEqual(self.links(report, "decoratedCallee", "decorated-wrapper-syntax"), [])
        report = self.report('from asgiref.sync import sync_to_async as wrap\n@wrap\n'
            'async def work():\n    return 1\nasync def run():\n    await work()\n')
        self.assertEqual(self.links(report, "decoratedCallee", "decorated-wrapper-syntax"), [])

    def test_decorated_rebound_conditional_late_parameters_and_import_mutation_withheld(self):
        sources = [
            '@wrap\ndef work():\n    return 1\nwork = other\nasync def run():\n    await work()\n',
            'if flag:\n    @wrap\n    def work():\n        return 1\nasync def run():\n    await work()\n',
            'async def run():\n    await work()\n@wrap\ndef work():\n    return 1\n',
            '@wrap\ndef work():\n    return 1\nasync def run(work):\n    await work()\n',
            '@wrap\ndef work():\n    return 1\nexec(code)\nasync def run():\n    await work()\n',
            '@wrap\ndef work():\n    return 1\nasync def run():\n    global work\n    await work()\n',
            'wrap = other\n@wrap\ndef work():\n    return 1\nasync def run():\n    await work()\n',
        ]
        for source in sources:
            with self.subTest(source=source):
                report = self.report('from asgiref.sync import sync_to_async as wrap\n' + source)
                self.assertEqual(self.links(report, "decoratedCallee", "decorated-wrapper-syntax"), [])
        report = self.report('import asgiref.sync as sync\nsync.sync_to_async = other\n'
            '@sync.sync_to_async\ndef work():\n    return 1\nasync def run():\n    await work()\n')
        self.assertEqual(self.links(report, "decoratedCallee", "decorated-wrapper-syntax"), [])

    def test_nested_decorator_and_class_receivers_remain_separate(self):
        report = self.report('from asgiref.sync import sync_to_async\nclass C:\n'
            '    @sync_to_async\n    def work(self):\n        return 1\n'
            '    async def run(self):\n        await self.work()\n        await work()\n'
            'async def run():\n    @sync_to_async\n    def nested():\n        return 1\n    await nested()\n')
        self.assertEqual(len(self.links(report, "decoratedCallee", "decorated-wrapper-syntax")), 1)

    def test_decorator_definition_outside_selected_owners_withheld(self):
        self.write('from asgiref.sync import sync_to_async\n@sync_to_async\ndef work():\n    return 1\n'
            'async def run():\n    await work()\n')
        facts = source_facts.analyze(self.root)
        tree = adapters.ast.parse((self.root / 'code.py').read_text())
        bindings = usage.Bindings(tree)
        provenance = adapters.Adapters(bindings, {}, 'code.py')
        run = tree.body[-1]
        result = provenance.decorated(run.body[0].value.value.func, bindings.scopes[run], frozenset())
        self.assertEqual(result["reason"], "definition-outside-selected-owners")

    def test_population_sync_only_and_empty_are_unavailable(self):
        self.write('from functools import partial\nasync def test_run():\n    partial(work)\n', 'tests/test_run.py')
        report = self.report('async def run():\n    missing()\n', include_tests=True)
        self.assertEqual(report["summary"]["populations"]["test"]["contexts"]["async-body"]["partialConstructions"], 1)
        for source in ('def run():\n    missing()\n', 'value = 1\n'):
            report = self.report(source)
            self.assertEqual(report["summary"]["availability"], "unavailable")
            self.assertIsNone(report["summary"]["contexts"]["async-body"])

    def assert_failed(self, report):
        self.assertEqual(report["status"], "incomplete")
        self.assertEqual(report["functions"], [])
        self.assertIsNone(report["summary"])
        self.assertTrue(report["diagnostics"])

    def test_prior_reports_immutable_stale_tampered_inputs_and_source_drift_fail_closed(self):
        self.write('from functools import partial\nasync def run():\n    partial(work)\n')
        facts = source_facts.analyze(self.root)
        base = usage.review(self.root, facts)
        old_context = async_context.review(self.root, facts, base)
        originals = copy.deepcopy([facts, base])
        report = adapters.review(self.root, facts, base)
        self.assertEqual(report["status"], "complete")
        self.assertEqual([facts, base], originals)
        self.assertEqual(old_context["functions"], async_context.review(self.root, facts, base)["functions"])
        self.assertEqual(report["runId"], base["runId"])
        for field, value in (("runId", "bad"), ("functions", []), ("summary", {}), ("policy", "bad"), ("subject", {})):
            self.assert_failed(adapters.review(self.root, facts, {**base, field: value}))
        changed = copy.deepcopy(facts)
        changed["functions"][0]["complexity"]["value"] = 100
        self.assert_failed(adapters.review(self.root, changed, base))
        self.write('async def added():\n    pass\n', 'new.py')
        self.assert_failed(adapters.review(self.root, facts, base))
        self.write('async def run():\n    changed()\n')
        self.assert_failed(adapters.review(self.root, facts, base))

    def test_parse_budget_and_collection_failures_withhold_all_rows(self):
        self.write('async def broken(:\n')
        self.assert_failed(adapters.analyze(self.root))
        self.write('async def run():\n    first()\n    second()\n')
        for name, limit in (("MAX_OWNERS", 0), ("MAX_CALLS", 1)):
            with patch.object(usage, name, limit):
                self.assert_failed(adapters.analyze(self.root))
        with patch.object(adapters, 'collect', side_effect=ValueError('source drift')):
            self.assert_failed(adapters.analyze(self.root))

    def test_cli_deterministic_fresh_no_execution_or_overwrite(self):
        file = self.write('raise RuntimeError("never execute")\nasync def run():\n    missing()\n')
        first, second = adapters.analyze(file), adapters.analyze(file)
        self.assertEqual(first["functions"], second["functions"])
        self.assertNotEqual(first["runId"], second["runId"])
        output = self.root / 'adapters.json'
        command = [sys.executable, adapters.__file__, str(file), '--output', str(output)]
        result = subprocess.run(command, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        before = output.read_bytes()
        self.assertEqual(subprocess.run(command, capture_output=True).returncode, 2)
        self.assertEqual(output.read_bytes(), before)
        self.assertEqual(subprocess.run([sys.executable, adapters.__file__, str(file), '--run-id', 'bad'], capture_output=True).returncode, 2)


if __name__ == '__main__':
    unittest.main()
