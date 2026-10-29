import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import error_handling_context as context


class ContextTests(unittest.TestCase):
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
        result = context.analyze(self.root, **selection)
        self.assertEqual(result["status"], "complete", result["diagnostics"])
        return result

    def patterns(self, report):
        return [row["classification"]["pattern"] for row in report["contexts"]]

    def test_supported_shapes_do_not_assign_handling_scores(self):
        result = self.report('try: import optional\nexcept ImportError: pass\n'
                             'def fallback():\n    try: work()\n    except Missing: return None\n'
                             '    except Other: raise\n'
                             'def after():\n    try: work()\n    except Missing: pass\n    return False\n'
                             'try: work()\nexcept Missing:\n    # Intent is accepted, not judged.\n    pass\n')
        self.assertEqual(self.patterns(result), ['guarded-import-shape', 'direct-literal-fallback-return',
                                               'direct-bare-rethrow', 'no-op-then-literal-return', 'locally-documented-no-op'])
        self.assertEqual(result['summary']['recognizedShapes'], 5)
        self.assertEqual(result['summary']['unresolved'], 0)
        self.assertNotIn('"score":', json.dumps(result))
        self.assertTrue(all(row['disposition'] == 'review-only' for row in result['contexts']))

    def test_try_else_finally_and_following_are_lexical_context(self):
        result = self.report('def work():\n    try:\n        parse()\n    except ValueError:\n        pass\n'
                             '    else:\n        accept()\n    finally:\n        cleanup()\n    return None\n')
        row = result['contexts'][0]
        self.assertEqual(row['tryStatement']['body']['statements'][0]['line'], 3)
        self.assertEqual(row['tryStatement']['else']['statements'][0]['line'], 7)
        self.assertEqual(row['tryStatement']['finally']['statements'][0]['line'], 9)
        self.assertEqual(row['followingInSameSuite']['statements'][0]['literalReturnKind'], 'none')
        self.assertEqual(row['classification']['unresolvedReasons'], ['enclosing-finally'])

    def test_surrounding_prose_is_preserved_without_reclassifying_local_documentation(self):
        result = self.report('def probe():\n    """Search supported backends."""\n'
                             '    # A missing optional library is expected.\n    try: import optional\n    except ImportError: pass\n')
        row = result['contexts'][0]
        self.assertEqual([item['kind'] for item in row['surroundingDocumentation']['items']], ['preceding-try-comment', 'owner-docstring'])
        self.assertEqual(result['handlerReview']['handlers'][0]['classification'], 'undocumented-no-op')
        self.assertEqual(row['classification']['pattern'], 'guarded-import-shape')

    def test_calls_conditional_recovery_and_dynamic_imports_stay_unresolved(self):
        result = self.report('def work():\n    try: load()\n    except A: return fallback()\n'
                             '    except B:\n        if retry: return None\n'
                             '    except C:\n        log()\n        raise\n'
                             '    except D: return cached\n'
                             'try: import_module("optional")\nexcept ImportError: pass\n')
        self.assertEqual(self.patterns(result), [None] * 5)
        self.assertEqual(result['summary']['unresolved'], 5)

    def test_import_shape_requires_only_imports_and_narrow_unshadowed_type_syntax(self):
        for source in ['try:\n    import optional\n    work()\nexcept ImportError: pass\n',
                       'try: import optional\nexcept Exception: pass\n',
                       'try: import optional\nexcept (ImportError, ValueError): pass\n',
                       'ImportError = Custom\ntry: import optional\nexcept ImportError: pass\n',
                       'def elsewhere(ImportError): return 1\ntry: import optional\nexcept ImportError: pass\n',
                       'from something import *\ntry: import optional\nexcept ImportError: pass\n',
                       'exec(configuration)\ntry: import optional\nexcept ImportError: pass\n',
                       'match value:\n    case {"x": ImportError}: pass\ntry: import optional\nexcept ImportError: pass\n']:
            with self.subTest(source=source):
                self.assertEqual(self.patterns(self.report(source)), [None])
        self.assertEqual(self.patterns(self.report('try: from optional import feature\nexcept (ImportError, ModuleNotFoundError): pass\n')), ['guarded-import-shape'])

    def test_exception_groups_nested_handlers_and_context_managers_remain_unresolved(self):
        result = self.report('try: work()\nexcept* ValueError: raise\n'
                             'with resource():\n    try: work()\n    except ValueError: raise\n'
                             'try: work()\nexcept ValueError:\n    try: recover()\n    except Other: raise\n')
        self.assertEqual(self.patterns(result), [None] * 4)
        self.assertIn('exception-group-semantics', result['contexts'][0]['classification']['unresolvedReasons'])
        self.assertIn('enclosing-context-manager', result['contexts'][1]['classification']['unresolvedReasons'])
        self.assertIn('nested-handler', result['contexts'][3]['classification']['unresolvedReasons'])
        self.assertEqual(len({row['handlerId'] for row in result['contexts']}), 4)

    def test_import_defaults_require_literal_assignment_to_the_imported_binding(self):
        result = self.report('try: from optional import version as selected\nexcept ImportError: selected = None\n')
        self.assertEqual(self.patterns(result), ['guarded-import-literal-default'])
        self.assertEqual(result['contexts'][0]['handlerBody']['statements'][0]['literalAssignment'], {'targets': ['selected'], 'valueKind': 'none'})
        for assignment in ['unrelated = None', 'selected = create_default()', 'settings.selected = None', 'selected: factory() = None']:
            with self.subTest(assignment=assignment):
                result = self.report(f'try: import selected\nexcept ImportError: {assignment}\n')
                self.assertEqual(self.patterns(result), [None])

    def test_class_body_keeps_execution_context_but_method_resets_it(self):
        result = self.report('with resource():\n    class Client:\n        try: run()\n        except ValueError: raise\n'
                             '        def work(self):\n            try: run()\n            except ValueError: raise\n')
        self.assertEqual(self.patterns(result), [None, 'direct-bare-rethrow'])

    def test_following_does_not_cross_suite_boundary_or_skip_active_statements(self):
        result = self.report('def work():\n    if enabled:\n        try: run()\n        except ValueError: pass\n'
                             '    return None\n'
                             'def other():\n    try: run()\n    except ValueError: pass\n    reset()\n    return None\n')
        self.assertEqual(self.patterns(result), [None, None])
        self.assertEqual(result['contexts'][0]['followingInSameSuite']['statements'], [])
        self.assertEqual(result['contexts'][1]['followingInSameSuite']['statementCount'], 2)

    def test_literal_return_shapes_exclude_computation_and_containers(self):
        for value, expected in [('None', True), ('False', True), ('-1', True), ('"fallback"', True),
                                ('[]', False), ('a or b', False), ('result.value', False), ('...', False)]:
            with self.subTest(value=value):
                report = self.report(f'def work():\n    try: run()\n    except ValueError: return {value}\n')
                self.assertEqual(self.patterns(report), ['direct-literal-fallback-return' if expected else None])

    def test_documentation_does_not_judge_business_intent_or_bypass_behavioral_uncertainty(self):
        result = self.report('try: run()\nexcept ValueError:\n    # TODO review later\n    pass\nfinally: cleanup()\n')
        self.assertEqual(self.patterns(result), ['locally-documented-no-op'])
        self.assertEqual(result['contexts'][0]['tryStatement']['finally']['statementCount'], 1)
        result = self.report('# noqa\ntry: run()\nexcept ValueError: pass # noqa\n')
        self.assertEqual(self.patterns(result), [None])
        self.assertEqual(result['contexts'][0]['surroundingDocumentation']['items'], [])

    def test_bounded_context_reports_truncation_without_inventing_continuation(self):
        result = self.report('def work():\n    """' + 'x' * 2500 + '"""\n    try:\n' + '        run()\n' * 40 +
                             '    except ValueError: pass\n' + '    reset()\n' * 5)
        row = result['contexts'][0]
        self.assertTrue(row['tryStatement']['body']['truncated'])
        self.assertEqual(len(row['tryStatement']['body']['statements']), 32)
        self.assertEqual(row['followingInSameSuite']['statementCount'], 5)
        self.assertEqual(len(row['followingInSameSuite']['statements']), 3)
        self.assertTrue(row['surroundingDocumentation']['truncated'])

    def test_populations_and_empty_selections_remain_unscored(self):
        self.write('try: run()\nexcept ValueError: raise\n', 'tests/test_work.py')
        report = self.report('VALUE = 1\n')
        self.assertEqual(report['summary']['handlers'], 0)
        report = context.analyze(self.root, include_tests=True)
        self.assertEqual(report['summary']['populations']['test']['recognizedShapes'], 1)

    def test_source_or_base_drift_and_parse_failure_withhold_context(self):
        file = self.write('def work():\n    try: run()\n    except ValueError: raise\n')
        facts = context.handlers.source_facts.analyze(self.root)
        base = context.handlers.review(self.root, facts)
        before = copy.deepcopy((facts, base))
        self.assertEqual(context.review(self.root, facts, base)['status'], 'complete')
        self.assertEqual((facts, base), before)
        changed = copy.deepcopy(base); changed['handlers'][0]['classification'] = 'other-body'
        self.assertEqual(context.review(self.root, facts, changed)['status'], 'incomplete')
        changed = copy.deepcopy(facts); changed['functions'][0]['id'] = '0' * 64
        self.assertEqual(context.review(self.root, changed, base)['status'], 'incomplete')
        file.write_text('def work(): return None\n')
        failed = context.review(self.root, facts, base)
        self.assertEqual(failed['status'], 'incomplete'); self.assertEqual(failed['contexts'], [])
        self.assertIsNone(failed['summary'])
        self.write('def broken(:')
        self.assertEqual(context.analyze(self.root)['status'], 'incomplete')

    def test_cli_preserves_ids_existing_output_and_never_executes_target(self):
        self.write('from pathlib import Path\nPath("EXECUTED").write_text("bad")\ntry: run()\nexcept: pass\n')
        output = self.root / 'review.json'
        ids = ['11111111-1111-4111-8111-111111111111', '22222222-2222-4222-8222-222222222222']
        command = [sys.executable, context.__file__, str(self.root), '--output', str(output), '--run-id', ids[0], '--audit-id', ids[1]]
        result = subprocess.run(command, cwd=self.root, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        data = output.read_bytes(); report = json.loads(data)
        self.assertEqual([report['runId'], report['auditId']], ids)
        self.assertEqual(report['handlerReview']['runId'], ids[0])
        self.assertFalse((self.root / 'EXECUTED').exists())
        self.assertEqual(subprocess.run(command, capture_output=True).returncode, 2)
        self.assertEqual(output.read_bytes(), data)


if __name__ == '__main__':
    unittest.main()
