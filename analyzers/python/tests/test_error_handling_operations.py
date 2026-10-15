import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import error_handling_operations as operations


class OperationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def write(self, source, name="work.py"):
        file = self.root / name
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(source, encoding="utf-8")
        return file

    def report(self, source, **selection):
        self.write(source)
        result = operations.analyze(self.root, **selection)
        self.assertEqual(result["status"], "complete", result["diagnostics"])
        return result

    def test_narrow_shapes_preserve_prior_patterns_and_are_unscored(self):
        bodies = ['logger.exception(error)\n        raise', 'recover(error)',
                  'notify(error)\n        cleanup()', 'return fallback(error)', 'return fallback',
                  'return defaults.value', 'raise Wrapped() from error', 'return None']
        source = ''.join(f'def f{i}():\n    try: work()\n    except Error as error:\n        {body}\n' for i, body in enumerate(bodies))
        report = self.report(source)
        self.assertEqual([row['classification']['pattern'] for row in report['handlers']],
                         ['direct-calls-then-bare-raise', 'direct-call-statement-sequence', 'direct-call-statement-sequence',
                          'direct-symbol-or-call-return', 'direct-symbol-or-call-return', 'direct-symbol-or-call-return',
                          'direct-raise-from-handler-name', 'direct-literal-fallback-return'])
        self.assertEqual(report['summary']['newlyRecognizedShapes'], 6)
        self.assertEqual(report['summary']['inputUnresolvedInventory']['handlers'], 6)
        self.assertEqual(report['summary']['inputUnresolvedInventory']['multiStatement'], 2)
        self.assertEqual(report['summary']['loggingNameSyntaxCalls'], 1)
        self.assertNotIn('"score":', json.dumps(report))
        self.assertNotIn('"findings":', json.dumps(report))
        self.assertTrue(all(row['disposition'] == 'review-only' for row in report['handlers']))
        self.assertEqual(report['handlers'][-1]['classification'], report['handlers'][-1]['originalClassification'])

    def test_names_are_not_resolved_logging_or_recovery(self):
        report = self.report('def exception(value): return False\n'
                             'def f():\n    try: work()\n    except Error as error:\n        exception(error)\n        raise\n')
        call = report['handlers'][0]['calls'][0]
        self.assertEqual(call['callee']['symbol'], 'exception')
        self.assertEqual(call['loggingNameSyntax'], 'exception')
        self.assertEqual(report['handlers'][0]['classification']['pattern'], 'direct-calls-then-bare-raise')
        self.assertNotIn('isLogger', call)
        self.assertNotIn('recovered', report['handlers'][0])

    def test_conditional_rebinding_and_computed_receivers_stay_unresolved(self):
        for body in ['if ready: recover()', 'error = other\n        log(error)\n        raise error',
                     'log(error)\n        raise Wrapped() from error', 'return registry[key]',
                     'return factory()()', 'factory().recover()', 'logger.error(value := error)',
                     'return recover(value := error)', 'log(error)\n        return fallback()',
                     'result = recover(error)\n        return result']:
            with self.subTest(body=body):
                row = self.report('def f():\n    try: work()\n    except Error as error:\n        '+body+'\n')['handlers'][0]
                self.assertEqual(row['classification']['status'], 'unresolved')

    def test_context_blockers_keep_raw_calls_and_returns(self):
        for source, reason in [
            ('def f():\n    try: work()\n    except Error: return fallback()\n    finally: cleanup()\n', 'enclosing-finally'),
            ('with resource():\n    try: work()\n    except Error: recover()\n', 'enclosing-context-manager'),
            ('try: work()\nexcept* Error: recover()\n', 'exception-group-semantics'),
            ('try: work()\nexcept Error:\n    try: again()\n    except Other: recover()\n', 'nested-handler')]:
            with self.subTest(reason=reason):
                row = self.report(source)['handlers'][-1]
                self.assertIn(reason, row['classification']['unresolvedReasons'])
                self.assertTrue(row['calls'])

    def test_ownership_and_projection_exclude_nested_definitions_and_handlers(self):
        report = self.report('def f():\n    try: work()\n    except Error:\n'
                             '        callback = lambda: hidden()\n'
                             '        def nested(value=default()):\n            return hidden()\n'
                             '        class Inner:\n            value = hidden_class()\n'
                             '        try: retry()\n        except Other: nested_recover()\n'
                             '        return wrap(inner())\n')
        outer, inner = report['handlers']
        self.assertEqual([call['callee']['symbol'] for call in outer['calls']], ['retry', 'wrap', 'inner'])
        self.assertEqual([call['callee']['symbol'] for call in inner['calls']], ['nested_recover'])
        self.assertEqual(outer['returns'][0]['expression']['symbol'], 'wrap')
        base = report['transferReview']['contextReview']['handlerReview']['handlers']
        for row, original in zip(report['handlers'], base):
            self.assertEqual(len(row['calls']), original['bodyObservations']['calls'])
            self.assertEqual([{'line': value['line'], 'hasValue': value['expression']['kind'] != 'absent'} for value in row['returns']], original['bodyObservations']['returns'])

    def test_await_and_nested_expression_context_does_not_claim_execution(self):
        report = self.report('async def f():\n    try: work()\n    except Error:\n'
                             '        await log(error)\n        values = [convert(x) for x in values if allowed(x)]\n'
                             '        deferred = (later(x) for x in values)\n'
                             '        ready and conditional()\n        return await fallback(error)\n')
        row = report['handlers'][0]
        self.assertEqual(row['classification']['status'], 'unresolved')
        calls = {call['callee']['symbol']: call for call in row['calls']}
        self.assertTrue(calls['log']['immediatelyAwaited'])
        self.assertTrue(calls['log']['directStatement'])
        self.assertFalse(calls['fallback']['directStatement'])
        self.assertIn('GeneratorExp', [a['kind'] for a in calls['later']['ancestors']])
        self.assertIn('BoolOp', [a['kind'] for a in calls['conditional']['ancestors']])
        self.assertEqual(row['returns'][0]['expression']['astKind'], 'Await')
        self.assertEqual(row['returns'][0]['awaitedExpression']['symbol'], 'fallback')

    def test_class_body_context_is_retained_and_methods_are_independent(self):
        report = self.report('with resource():\n    class Client:\n'
                             '        try: work()\n        except Error: recover()\n'
                             '        def f(self):\n            try: work()\n            except Error: return fallback()\n')
        self.assertEqual(report['handlers'][0]['classification']['status'], 'unresolved')
        self.assertEqual(report['handlers'][1]['classification']['pattern'], 'direct-symbol-or-call-return')

    def test_call_arguments_ids_and_literal_omission(self):
        report = self.report('try: work()\nexcept Error as erreur: log("PRIVATE", *args, code=3, **kw); log(erreur)\n')
        calls = report['handlers'][0]['calls']
        self.assertEqual(len({call['id'] for call in calls}), 2)
        self.assertEqual((calls[0]['positionalArguments'], calls[0]['keywordArguments'], calls[0]['starredArguments'], calls[0]['keywordUnpacks']), (2, 2, 1, 1))
        self.assertNotIn('PRIVATE', json.dumps(report['handlers']))
        unicode_row = self.report('def f():\n    try: work()\n    except Error as erreur: return dépôt.récupérer(erreur)\n')['handlers'][0]
        self.assertEqual(unicode_row['returns'][0]['expression']['symbol'], 'dépôt.récupérer')

    def test_bounds_are_explicit_and_do_not_truncate_population(self):
        source = 'try: work()\nexcept Error:\n' + ''.join('    '*(i+1)+'if ready:\n' for i in range(40)) + '    '*41+'recover()\n'
        call = self.report(source)['handlers'][0]['calls'][0]
        self.assertEqual(len(call['ancestors']), 32)
        self.assertTrue(call['ancestorsTruncated'])
        row = self.report('try: work()\nexcept Error:\n'+'    recover()\n'*40)['handlers'][0]
        self.assertEqual(len(row['calls']), 40)
        self.assertEqual(row['suite']['statementCount'], 40)
        self.assertTrue(row['suite']['truncated'])
        self.assertEqual(row['classification']['pattern'], 'direct-call-statement-sequence')
        row = self.report('try: work()\nexcept Error: '+ 'x'*201+'()\n')['handlers'][0]
        self.assertIsNone(row['calls'][0]['callee']['symbol'])
        self.assertEqual(row['classification']['status'], 'unresolved')

    def test_empty_and_excluded_populations_are_not_handling_scores(self):
        self.write('try: work()\nexcept Error: recover()\n', 'tests/test_work.py')
        report = operations.analyze(self.root)
        self.assertEqual(report['summary']['handlers'], 0)
        included = operations.analyze(self.root, include_tests=True)
        self.assertEqual(included['summary']['populations']['test']['callExpressions'], 1)
        self.assertEqual(included['summary']['inputUnresolvedInventory']['handlers'], 1)
        empty = operations.analyze(self.root, include=['absent/**'])
        self.assertEqual(empty['summary']['populations'], {})

    def test_provenance_tampering_drift_budgets_and_input_immutability(self):
        self.write('try: work()\nexcept Error: recover()\n')
        facts = operations.context.handlers.source_facts.analyze(self.root)
        base = operations.transfers.analyze(self.root)
        before = copy.deepcopy([facts, base])
        self.assertEqual(operations.review(self.root, facts, base)['status'], 'complete')
        self.assertEqual([facts, base], before)
        for mutate in [lambda b: b['summary'].update(handlers=999),
                       lambda b: b['handlers'][0].update(sourceSha256='0'*64),
                       lambda b: b.update(runId='11111111-1111-4111-8111-111111111111'),
                       lambda b: b['configuration'].update(maxRaises=999)]:
            changed = copy.deepcopy(base); mutate(changed)
            result = operations.review(self.root, facts, changed)
            self.assertEqual(result['status'], 'incomplete')
            self.assertEqual(result['handlers'], [])
            self.assertIsNone(result['summary'])
        with patch.object(operations, 'MAX_OPERATIONS', 0):
            self.assertEqual(operations.review(self.root, facts, base)['status'], 'incomplete')
        with patch.object(operations, 'read_verified', side_effect=ValueError('Changed after validation')):
            self.assertEqual(operations.review(self.root, facts, base)['status'], 'incomplete')
        self.write('try: changed()\nexcept Error: recover()\n')
        self.assertEqual(operations.review(self.root, facts, base)['status'], 'incomplete')

    def test_global_budget_across_files_and_incomplete_inputs(self):
        self.write('try: work()\nexcept Error: recover()\n', 'one.py')
        self.write('try: work()\nexcept Error: recover()\n', 'two.py')
        with patch.object(operations, 'MAX_OPERATIONS', 1):
            report = operations.analyze(self.root)
        self.assertEqual(report['status'], 'incomplete')
        self.assertEqual(report['handlers'], [])
        self.assertIsNone(report['summary'])
        self.assertEqual(operations.analyze(self.root, include=['../escape/**'])['status'], 'incomplete')
        self.write('def broken(:')
        self.assertEqual(operations.analyze(self.root)['status'], 'incomplete')

    def test_cli_ids_new_output_and_nonexecution(self):
        file = self.write('from pathlib import Path\nPath("EXECUTED").write_text("bad")\ntry: work()\nexcept Error: recover()\n')
        output = self.root/'report.json'
        run_id, audit_id = '11111111-1111-4111-8111-111111111111', '22222222-2222-4222-8222-222222222222'
        command = [sys.executable, operations.__file__, str(file), '--output', str(output), '--run-id', run_id, '--audit-id', audit_id]
        result = subprocess.run(command, cwd=self.root, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        report = json.loads(output.read_text(encoding='utf-8'))
        self.assertEqual((report['runId'], report['auditId']), (run_id, audit_id))
        self.assertFalse((self.root/'EXECUTED').exists())
        before = output.read_bytes()
        self.assertEqual(subprocess.run(command, capture_output=True).returncode, 2)
        self.assertEqual(output.read_bytes(), before)
        self.write('def broken(:')
        result = subprocess.run([sys.executable, operations.__file__, str(file)], capture_output=True, text=True)
        self.assertEqual(result.returncode, 2)
        self.assertEqual(json.loads(result.stdout)['handlers'], [])


if __name__ == '__main__':
    unittest.main()
