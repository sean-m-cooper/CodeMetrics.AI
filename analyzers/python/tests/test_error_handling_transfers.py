import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import error_handling_transfers as transfers


class TransferTests(unittest.TestCase):
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
        result = transfers.analyze(self.root, **selection)
        self.assertEqual(result["status"], "complete", result["diagnostics"])
        return result

    def test_raise_forms_and_cause_syntax_remain_distinct_and_unscored(self):
        source = ''.join(f'def f{i}():\n    try: call()\n    except Error as error:\n        {body}\n'
                         for i, body in enumerate(['raise', 'raise error', 'raise NewError(error)',
                                                   'raise pkg.NewError(error) from error', 'raise NewError() from None',
                                                   'raise OtherError from error', 'raise NewError() from other']))
        report = self.report(source)
        self.assertEqual([r['classification']['pattern'] for r in report['handlers']],
                         ['direct-bare-rethrow', 'direct-handler-name-raise', 'direct-symbol-or-call-raise',
                          'direct-raise-from-handler-name', 'direct-raise-from-none', 'direct-raise-from-handler-name', None])
        rows = [r['raises'][0] for r in report['handlers']]
        self.assertEqual([r['cause']['kind'] for r in rows], ['absent', 'absent', 'absent', 'name', 'none', 'name', 'name'])
        self.assertEqual(rows[3]['exception']['symbol'], 'pkg.NewError')
        self.assertTrue(rows[1]['exception']['matchesHandlerNameSyntax'])
        self.assertEqual(report['summary']['newlyRecognizedShapes'], 5)
        self.assertEqual(report['summary']['handlerNameRaiseSyntax'], 1)
        self.assertEqual(report['summary']['handlerNameCauseSyntax'], 2)
        self.assertNotIn('"score":', json.dumps(report))
        self.assertTrue(all(r['disposition'] == 'review-only' for r in report['handlers']))

    def test_ownership_does_not_credit_nested_handlers_or_definitions(self):
        report = self.report('def work():\n    try: call()\n    except Error as e:\n'
                             '        if ready: raise First() from e\n'
                             '        try: recover()\n        except Other as other: raise Second() from other\n'
                             '        def nested():\n            raise Hidden()\n'
                             '        class Nested:\n            raise HiddenClass()\n'
                             '        raise Last()\n')
        outer, inner = report['handlers']
        self.assertEqual([r['exception']['symbol'] for r in outer['raises']], ['First', 'Last'])
        self.assertEqual(outer['raises'][0]['containers'], [{'kind': 'If', 'line': 4}])
        self.assertFalse(outer['raises'][0]['direct'])
        self.assertEqual(inner['raises'][0]['cause']['symbol'], 'other')
        self.assertEqual(inner['classification']['unresolvedReasons'], ['nested-handler'])
        self.assertEqual(report['summary']['raiseStatements'], 3)

    def test_finally_context_managers_groups_and_nested_handlers_remain_blocked(self):
        samples = [
            ('try: call()\nexcept Error as e: raise New() from e\nfinally: cleanup()\n', 'enclosing-finally'),
            ('with resource():\n    try: call()\n    except Error as e: raise New() from e\n', 'enclosing-context-manager'),
            ('try: call()\nexcept* Error as e: raise New() from e\n', 'exception-group-semantics'),
            ('try: call()\nexcept Error:\n    try: recover()\n    except Other as e: raise New() from e\n', 'nested-handler')]
        for source, reason in samples:
            with self.subTest(reason=reason):
                report = self.report(source)
                row = report['handlers'][-1]
                self.assertEqual(row['classification']['status'], 'unresolved')
                self.assertIn(reason, row['classification']['unresolvedReasons'])
                self.assertEqual(row['raises'][0]['cause']['kind'], 'name')

    def test_class_bodies_keep_context_but_methods_reset_deferred_execution(self):
        report = self.report('with resource():\n    class Client:\n'
                             '        try: call()\n        except Error: raise New()\n'
                             '        def work(self):\n            try: call()\n            except Error: raise New()\n')
        self.assertEqual(report['handlers'][0]['classification']['status'], 'unresolved')
        self.assertEqual(report['handlers'][1]['classification']['pattern'], 'direct-symbol-or-call-raise')

    def test_complex_expressions_rebinding_and_multistatement_suites_remain_unresolved(self):
        for body in ['raise registry[key]', 'raise factory()()', 'raise error.with_traceback(tb) from other',
                     'raise Error() from cause()', 'raise Error() from error.__cause__',
                     'raise Error(error := other) from error', 'raise (error := other)',
                     'error = other\n        raise error', 'log(error)\n        raise',
                     'if ready: raise Error() from error']:
            with self.subTest(body=body):
                row = self.report('def work():\n    try: call()\n    except Error as error:\n        ' + body + '\n')['handlers'][0]
                self.assertEqual(row['classification']['status'], 'unresolved')
                self.assertGreater(len(row['raises']), 0)

    def test_syntax_does_not_resolve_exception_types_or_suppress_context_intent(self):
        report = self.report('def Error(): return 42\ntry: call()\nexcept Something as e: raise Error() from None\n')
        row = report['handlers'][0]
        self.assertEqual(row['classification']['pattern'], 'direct-raise-from-none')
        self.assertEqual(row['raises'][0]['exception']['kind'], 'call')
        self.assertEqual(row['raises'][0]['cause']['kind'], 'none')
        self.assertEqual(row['disposition'], 'review-only')

    def test_existing_patterns_and_base_reports_are_unchanged(self):
        report = self.report('try: call()\nexcept Missing:\n    # Deliberate fallback.\n    pass\n'
                             'def work():\n    try: call()\n    except Missing: return None\n')
        self.assertEqual(report['summary']['newlyRecognizedShapes'], 0)
        for row, old in zip(report['handlers'], report['contextReview']['contexts']):
            self.assertEqual(row['originalClassification'], old['classification'])
            self.assertEqual(row['classification'], old['classification'])
            self.assertEqual(row['raises'], [])

    def test_summary_populations_empty_selections_and_same_line_raise_identity(self):
        report = self.report('try: call()\nexcept A: raise One(); raise Two()\n')
        rows = report['handlers'][0]['raises']
        self.assertEqual(len({r['id'] for r in rows}), 2)
        self.assertEqual(report['summary']['byExceptionSyntax'], {'call': 2})
        self.write('try: call()\nexcept A: raise Test()\n', 'tests/test_work.py')
        report = transfers.analyze(self.root, include_tests=True)
        self.assertEqual(report['summary']['populations']['test']['raiseStatements'], 1)
        empty = transfers.analyze(self.root, include=['absent/**'])
        self.assertEqual(empty['summary']['handlers'], 0)
        self.assertEqual(empty['summary']['populations'], {})

    def test_expression_metadata_is_bounded_and_omits_literal_contents(self):
        report = self.report('try: call()\nexcept A: raise Error("SECRET-LITERAL") from None\n')
        self.assertNotIn('SECRET-LITERAL', json.dumps(report['handlers']))
        long_name = 'x' * 201
        row = self.report(f'try: call()\nexcept A: raise {long_name}()\n')['handlers'][0]
        self.assertIsNone(row['raises'][0]['exception']['symbol'])
        self.assertEqual(row['classification']['status'], 'unresolved')

    def test_deep_container_metadata_is_explicitly_truncated(self):
        source = 'try: call()\nexcept Error:\n'
        source += ''.join('    ' * (i + 1) + 'if ready:\n' for i in range(40))
        source += '    ' * 41 + 'raise New()\n'
        row = self.report(source)['handlers'][0]['raises'][0]
        self.assertEqual(len(row['containers']), 32)
        self.assertTrue(row['containersTruncated'])
        self.assertFalse(row['direct'])

    def test_invalid_selection_and_cli_failure_cannot_recover_old_results(self):
        self.write('try: call()\nexcept Error: raise New()\n')
        report = transfers.analyze(self.root, include=['../outside/**'])
        self.assertEqual(report['status'], 'incomplete')
        self.assertEqual(report['handlers'], [])
        self.assertIsNone(report['summary'])
        self.write('def broken(:')
        result = subprocess.run([sys.executable, transfers.__file__, str(self.root)], capture_output=True, text=True)
        self.assertEqual(result.returncode, 2)
        self.assertEqual(json.loads(result.stdout)['status'], 'incomplete')

    def test_drift_tampering_and_budget_fail_closed_without_mutation(self):
        self.write('try: call()\nexcept Error as e: raise New() from e\n')
        facts = transfers.context.handlers.source_facts.analyze(self.root)
        base = transfers.context.analyze(self.root)
        before = copy.deepcopy([facts, base])
        good = transfers.review(self.root, facts, base)
        self.assertEqual(good['status'], 'complete')
        self.assertEqual([facts, base], before)
        for mutate in [lambda b: b['contexts'][0].update(sourceSha256='0'*64),
                       lambda b: b['summary'].update(handlers=999),
                       lambda b: b.update(runId='11111111-1111-4111-8111-111111111111'),
                       lambda b: b['configuration'].update(maxFollowingStatements=999)]:
            altered = copy.deepcopy(base); mutate(altered)
            failed = transfers.review(self.root, facts, altered)
            self.assertEqual(failed['status'], 'incomplete')
            self.assertEqual(failed['handlers'], [])
            self.assertIsNone(failed['summary'])
        with patch.object(transfers, 'MAX_RAISES', 0):
            self.assertEqual(transfers.review(self.root, facts, base)['status'], 'incomplete')
        self.write('try: changed()\nexcept Error as e: raise New() from e\n')
        self.assertEqual(transfers.review(self.root, facts, base)['status'], 'incomplete')

    def test_source_change_between_validation_and_transfer_pass_is_rejected(self):
        self.write('try: call()\nexcept Error: raise New()\n')
        facts = transfers.context.handlers.source_facts.analyze(self.root)
        base = transfers.context.analyze(self.root)
        with patch.object(transfers, 'read_verified', side_effect=ValueError('Source drift')):
            failed = transfers.review(self.root, facts, base)
        self.assertEqual(failed['status'], 'incomplete')
        self.assertEqual(failed['handlers'], [])

    def test_cli_preserves_ids_nonexecution_output_and_incomplete_status(self):
        self.write('from pathlib import Path\nPath("EXECUTED").write_text("bad")\ntry: call()\nexcept Error: raise New()\n')
        output = self.root/'result.json'
        run_id, audit_id = '11111111-1111-4111-8111-111111111111', '22222222-2222-4222-8222-222222222222'
        command = [sys.executable, transfers.__file__, str(self.root/'work.py'), '--output', str(output), '--run-id', run_id, '--audit-id', audit_id]
        result = subprocess.run(command, cwd=self.root, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        data = json.loads(output.read_text(encoding='utf-8'))
        self.assertEqual((data['runId'], data['auditId']), (run_id, audit_id))
        self.assertFalse((self.root/'EXECUTED').exists())
        before = output.read_bytes()
        self.assertEqual(subprocess.run(command, capture_output=True).returncode, 2)
        self.assertEqual(output.read_bytes(), before)
        self.write('def broken(:')
        failed = transfers.analyze(self.root)
        self.assertEqual(failed['status'], 'incomplete')
        self.assertEqual(failed['handlers'], [])


if __name__ == '__main__':
    unittest.main()
