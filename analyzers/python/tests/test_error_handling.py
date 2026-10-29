import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import error_handling as review
import source_facts


class ErrorHandlingTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def write(self, source, name="work.py"):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(source, encoding="utf-8")
        return path

    def report(self, source, **options):
        self.write(source)
        report = review.analyze(self.root, **options)
        self.assertEqual(report["status"], "complete", report["diagnostics"])
        return report

    def test_classifications_describe_syntax_not_handling_quality(self):
        report = self.report('try: work()\nexcept A: pass\nexcept B:\n    # An absent cache entry is expected.\n    pass\n'
                             'except C: raise\nexcept D: raise Translated() from original\nexcept E: logger.exception("error")\n'
                             'def fallback():\n    try: work()\n    except A: return None\n'
                             'for item in items:\n    try: work()\n    except A: continue\n')
        self.assertEqual([row["classification"] for row in report["handlers"]],
                         ["undocumented-no-op", "documented-no-op", "terminal-raise", "terminal-raise", "other-body", "terminal-return", "terminal-loop-control"])
        self.assertTrue(report["handlers"][2]["bodyObservations"]["raises"][0]["bare"])
        self.assertTrue(report["handlers"][3]["bodyObservations"]["raises"][0]["explicitCause"])
        self.assertEqual(report["handlers"][4]["bodyObservations"]["calls"], 1)
        self.assertTrue(all(row["disposition"] == "review-only" for row in report["handlers"]))
        self.assertNotIn('"score":', json.dumps(report))

    def test_comments_header_inline_trailing_and_strings_are_preserved(self):
        report = self.report('try: work()\nexcept A: # Expected optional feature\n    pass\n'
                             'except B:\n    pass # Expected missing key\n'
                             'except C:\n    pass\n\n    # Another backend handles this\n'
                             'except D:\n    "The resource may already be closed."\n    ...\n')
        self.assertEqual([row["classification"] for row in report["handlers"]], ["documented-no-op"] * 4)
        self.assertEqual(report["handlers"][2]["documentation"][0]["text"], "Another backend handles this")
        self.assertEqual(report["handlers"][3]["documentation"][0]["kind"], "string-statement")

    def test_comments_do_not_cross_suite_boundaries_or_claim_directives_are_rationale(self):
        report = self.report('# Outside reason\ntry: work()\nexcept A: pass\n# Dedented reason\n'
                             'try: work()\nexcept B:\n    # noqa: E722\n    # pragma: no cover\n    pass\n'
                             'except C:\n    # ---\n    pass\nexcept D:\n    # Missing metadata is tolerated.\n    pass\n')
        self.assertEqual([row["classification"] for row in report["handlers"]],
                         ["undocumented-no-op"] * 3 + ["documented-no-op"])
        self.assertEqual(len(report["handlers"][1]["directiveComments"]), 2)

    def test_multiline_header_comment_is_not_a_suite_explanation_and_unicode_columns_work(self):
        report = self.report('try: work()\nexcept (\n    A, # About the exception type\n    B,\n):\n    pass\n'
                             'try: work()\nexcept Échec: pass # Déjà fermé\n')
        self.assertEqual(report["handlers"][0]["classification"], "undocumented-no-op")
        self.assertEqual(report["handlers"][1]["documentation"][0]["text"], "Déjà fermé")

    def test_documentation_presence_does_not_judge_the_business_explanation(self):
        report = self.report('try: work()\nexcept A:\n    # TODO decide whether to retry\n    pass\n')
        self.assertEqual(report["handlers"][0]["classification"], "documented-no-op")
        self.assertIn("content-not-adjudicated", report["configuration"]["documentationPolicy"])

    def test_nested_handlers_and_definitions_are_counted_once(self):
        report = self.report('def outer():\n    try: work()\n    except A:\n'
                             '        def nested():\n            try: work()\n            except B: raise\n'
                             '        try: recover()\n        except C: return None\n')
        outer, nested, inner = report["handlers"]
        self.assertEqual(outer["classification"], "other-body")
        self.assertEqual(outer["bodyObservations"]["raises"], [])
        self.assertEqual(outer["bodyObservations"]["returns"], [])
        self.assertEqual(outer["bodyObservations"]["nestedHandlersExcluded"], 1)
        self.assertNotEqual(outer["owner"]["functionId"], nested["owner"]["functionId"])
        self.assertEqual(outer["owner"]["functionId"], inner["owner"]["functionId"])
        self.assertEqual(report["summary"]["functionsWithHandlers"], 2)
        self.assertEqual(len({row["id"] for row in report["handlers"]}), 3)

    def test_finally_context_is_visible_without_claiming_propagation(self):
        report = self.report('def work():\n    try:\n        try: call()\n        except A: raise\n'
                             '        finally: cleanup()\n    except B: raise\n    finally: return None\n')
        self.assertEqual(report["handlers"][0]["enclosingFinallyLines"], [2, 3])
        self.assertEqual(report["handlers"][1]["enclosingFinallyLines"], [2])
        self.assertTrue(all(row["classification"] == "terminal-raise" for row in report["handlers"]))
        self.assertIn("propagation guarantee", report["scope"])

    def test_conditional_transfers_and_arbitrary_activity_remain_other_body(self):
        report = self.report('def work():\n    try: call()\n    except A:\n        if recoverable: return None\n'
                             '    except B:\n        if known: raise\n        else: raise Other()\n'
                             '    except C:\n        fallback = None\n')
        self.assertEqual([row["classification"] for row in report["handlers"]], ["other-body"] * 3)
        self.assertEqual(len(report["handlers"][1]["bodyObservations"]["raises"]), 2)

    def test_class_body_retains_enclosing_finally_but_deferred_method_does_not(self):
        report = self.report('try:\n    class Client:\n        try: load()\n        except A: raise\n'
                             '        def work(self):\n            try: call()\n            except B: raise\n'
                             'finally:\n    try: cleanup()\n    except C: pass\n')
        self.assertEqual([row["enclosingFinallyLines"] for row in report["handlers"]], [[1], [], []])

    def test_module_class_async_and_exception_groups_have_explicit_scope(self):
        report = self.report('try: import optional\nexcept ImportError: optional = None\n'
                             'class Client:\n    try: metadata = load()\n    except: pass\n'
                             '    async def request(self):\n        try: await work()\n        except* ValueError: raise\n')
        self.assertEqual([row["owner"]["kind"] for row in report["handlers"]], ["module", "class-body", "function"])
        self.assertTrue(report["handlers"][1]["bareExcept"])
        self.assertTrue(report["handlers"][2]["exceptionGroup"])
        self.assertEqual(report["handlers"][2]["exceptionTypeSyntax"], "ValueError")

    def test_nonproduction_populations_and_empty_results_remain_unscored(self):
        self.write('try: work()\nexcept: pass\n', 'tests/test_work.py')
        report = self.report('VALUE = 1\n')
        self.assertEqual(report["summary"]["handlers"], 0)
        self.assertEqual(report["summary"]["populations"], {})
        included = review.analyze(self.root, include_tests=True)
        self.assertEqual(included["summary"]["populations"]["test"]["handlers"], 1)
        self.assertNotIn("score", included["summary"])

    def test_partial_source_or_drift_withholds_all_handler_rows(self):
        path = self.write('try: work()\nexcept: pass\n')
        facts = source_facts.analyze(self.root)
        path.write_text('try: work()\nexcept: raise\n')
        failed = review.review(self.root, facts)
        self.assertEqual(failed["status"], "incomplete")
        self.assertEqual(failed["handlers"], [])
        self.assertIsNone(failed["summary"])
        self.write('def broken(:', 'broken.py')
        failed = review.analyze(self.root)
        self.assertEqual(failed["status"], "incomplete")
        self.assertEqual(failed["handlers"], [])

    def test_owner_identity_drift_and_global_budget_fail_closed_without_mutating_facts(self):
        self.write('def work():\n    try: call()\n    except: pass\n')
        self.write('try: call()\nexcept: pass\n', 'other.py')
        facts = source_facts.analyze(self.root)
        before = copy.deepcopy(facts)
        with patch.object(review, "MAX_HANDLERS", 1):
            failed = review.review(self.root, facts)
        self.assertEqual(failed["status"], "incomplete")
        self.assertEqual(failed["handlers"], [])
        self.assertEqual(facts, before)
        facts["functions"][0]["id"] = '0' * 64
        self.assertEqual(review.review(self.root, facts)["status"], "incomplete")

    def test_cli_preserves_ids_never_executes_target_and_refuses_existing_output(self):
        self.write('from pathlib import Path\nPath("EXECUTED").write_text("bad")\ntry: work()\nexcept: pass\n')
        output = self.root / 'review.json'
        ids = ['11111111-1111-4111-8111-111111111111', '22222222-2222-4222-8222-222222222222']
        command = [sys.executable, review.__file__, str(self.root), '--output', str(output), '--run-id', ids[0], '--audit-id', ids[1]]
        result = subprocess.run(command, cwd=self.root, capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        data = output.read_bytes()
        report = json.loads(data)
        self.assertEqual([report["runId"], report["auditId"]], ids)
        self.assertFalse((self.root / 'EXECUTED').exists())
        self.assertEqual(subprocess.run(command, capture_output=True).returncode, 2)
        self.assertEqual(output.read_bytes(), data)


if __name__ == '__main__':
    unittest.main()
