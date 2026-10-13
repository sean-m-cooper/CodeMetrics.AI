import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import decomposition
import source_facts


class DecompositionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def write(self, source, name="code.py"):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(source, encoding="utf-8")
        return path

    def rows(self, source):
        self.write(source)
        report = decomposition.analyze(self.root)
        self.assertEqual(report["status"], "complete", report["diagnostics"])
        return {row["member"]: row for row in report["functions"]}

    def test_statement_count_ignores_wrapping_but_counts_semicolon_statements(self):
        rows = self.rows('def wrapped():\n    "docs"\n    value = call(\n        1,\n        2,\n    )\n    return value\n'
                         'def flat():\n    value = call(1, 2); return value\n')
        for row in rows.values():
            self.assertEqual(row["measurement"]["ownedStatements"], 2)
            self.assertEqual(row["measurement"]["maxBlockNesting"], 0)
            self.assertEqual(row["measurement"]["statementKinds"], {"Assign": 1, "Return": 1})
        self.assertEqual(rows["wrapped"]["measurement"]["excludedSyntax"], {"docstring": 1})

    def test_nested_bodies_and_definition_metadata_do_not_credit_parent(self):
        rows = self.rows('def outer():\n'
                         '    @decorate(1 if flag else 2)\n'
                         '    def inner(value=compute()):\n'
                         '        if value:\n            return value\n        return 0\n'
                         '    class Local:\n        seed = build()\n        def method(self):\n            return 1\n'
                         '    callback = lambda x: x if x else 0\n'
                         '    return inner\n')
        self.assertEqual(rows["outer"]["measurement"], {"ownedStatements": 2, "maxBlockNesting": 0,
                         "statementKinds": {"Assign": 1, "Return": 1},
                         "excludedSyntax": {"ClassDef": 1, "FunctionDef": 1, "Lambda": 1}})
        inner = next(row for name, row in rows.items() if name.endswith(".inner"))
        self.assertEqual(inner["measurement"]["ownedStatements"], 3)
        self.assertEqual(inner["measurement"]["maxBlockNesting"], 1)
        self.assertEqual(sum(row["measurement"]["ownedStatements"] for row in rows.values() if row["measurement"]), 6)
        lambdas = [row for row in rows.values() if row["functionKind"] == "Lambda"]
        self.assertEqual(len(lambdas), 1)
        self.assertEqual(lambdas[0]["status"], "unavailable")
        self.assertIsNone(lambdas[0]["measurement"])

    def test_docstring_and_declarations_excluded_executable_imports_retained(self):
        rows = self.rows('def declarations():\n    "docs"\n    global shared\n    local: str\n'
                         '    type Alias = list[str]\n    pass\n    ...\n    import os\n'
                         '    value: int = 3\n    "not a leading docstring"\n    return value\n')
        measurement = rows["declarations"]["measurement"]
        self.assertEqual(measurement["ownedStatements"], 4)
        self.assertEqual(measurement["excludedSyntax"], {"AnnAssign": 1, "Expr": 1, "Global": 1, "Pass": 1, "TypeAlias": 1, "docstring": 1})

    def test_elif_is_flat_while_explicit_else_if_is_nested(self):
        rows = self.rows('def chain(x):\n    if x == 1:\n        return 1\n    elif x == 2:\n        return 2\n    else:\n        return 3\n'
                         'def nested(x):\n    if x == 1:\n        return 1\n    else:\n        if x == 2:\n            return 2\n        else:\n            return 3\n')
        self.assertEqual(rows["chain"]["measurement"]["ownedStatements"], 5)
        self.assertEqual(rows["nested"]["measurement"]["ownedStatements"], 5)
        self.assertEqual(rows["chain"]["measurement"]["maxBlockNesting"], 1)
        self.assertEqual(rows["nested"]["measurement"]["maxBlockNesting"], 2)

    def test_handler_finally_and_match_arms_are_siblings(self):
        rows = self.rows('def handle(x):\n    try:\n        work()\n    except ValueError:\n        recover()\n'
                         '    except TypeError:\n        raise\n    else:\n        complete()\n    finally:\n        cleanup()\n'
                         '    match x:\n        case 1 | 2:\n            return 1\n        case _:\n            return 0\n')
        measurement = rows["handle"]["measurement"]
        self.assertEqual(measurement["ownedStatements"], 9)
        self.assertEqual(measurement["maxBlockNesting"], 1)
        self.assertEqual(measurement["statementKinds"], {"Expr": 4, "Match": 1, "Raise": 1, "Return": 2, "Try": 1})

    def test_async_loops_with_and_try_star_contribute_structural_depth(self):
        rows = self.rows('async def walk(items):\n    async with manager():\n        async for item in items:\n'
                         '            try:\n                if item:\n                    await consume(item)\n'
                         '            except* ValueError:\n                recover()\n    return None\n')
        measurement = rows["walk"]["measurement"]
        self.assertEqual(measurement["ownedStatements"], 7)
        self.assertEqual(measurement["maxBlockNesting"], 4)

    def test_expressions_do_not_create_statements_or_block_depth(self):
        rows = self.rows('def values(items):\n    return [x if x else 0 for row in items for x in row if x > 1]\n')
        self.assertGreater(rows["values"]["complexity"], 1)
        self.assertEqual(rows["values"]["measurement"]["ownedStatements"], 1)
        self.assertEqual(rows["values"]["measurement"]["maxBlockNesting"], 0)

    def test_populations_are_separate_and_declaration_exclusions_retained(self):
        self.write('from typing import Protocol\nclass Reader(Protocol):\n    def read(self): ...\ndef production():\n    return 1\n')
        self.write('def test_value():\n    value = 1\n    assert value\n', 'tests/test_value.py')
        report = decomposition.analyze(self.root, include_tests=True)
        self.assertEqual(len(report["selection"]["excludedFunctions"]), 1)
        self.assertEqual(report["summary"]["selectedFunctions"], 2)
        self.assertEqual(report["summary"]["populations"]["production"]["ownedStatements"]["max"], 1)
        self.assertEqual(report["summary"]["populations"]["test"]["ownedStatements"]["max"], 2)
        self.assertNotEqual(report["configurationFingerprint"], decomposition.analyze(self.root)["configurationFingerprint"])

    def test_empty_lambda_and_zero_statement_populations_are_distinct(self):
        empty = decomposition.analyze(self.root)
        self.assertEqual(empty["summary"]["measuredFunctions"], 0)
        self.assertEqual(empty["summary"]["populations"], {})
        self.write('value = lambda x: x\n')
        report = decomposition.analyze(self.root)
        self.assertIsNone(report["summary"]["populations"]["production"]["ownedStatements"])
        rows = self.rows('def noops():\n    value: int\n    pass\n')
        self.assertEqual(rows["noops"]["measurement"]["ownedStatements"], 0)

    def test_distribution_nearest_rank_and_empty_semantics(self):
        self.assertIsNone(decomposition.distribution([]))
        self.assertEqual(decomposition.distribution([4]), {"count": 1, "p50": 4, "p90": 4, "p95": 4, "max": 4})
        self.assertEqual(decomposition.distribution(range(1, 101)), {"count": 100, "p50": 50, "p90": 90, "p95": 95, "max": 100})

    def test_deterministic_measurements_fresh_ids_without_target_execution(self):
        self.write('raise RuntimeError("do not execute")\ndef safe():\n    return 1\n')
        first, second = decomposition.analyze(self.root), decomposition.analyze(self.root)
        self.assertEqual(first["functions"], second["functions"])
        self.assertEqual(first["summary"], second["summary"])
        self.assertNotEqual(first["runId"], second["runId"])
        self.assertNotEqual(first["auditId"], second["auditId"])
        self.assertNotIn('"score":', json.dumps(first))
        pinned = decomposition.analyze(self.root, run_id=first["runId"], audit_id=first["auditId"])
        self.assertEqual(pinned["runId"], first["runId"])
        self.assertEqual(pinned["auditId"], first["auditId"])

    def test_source_drift_identity_drift_and_budget_failure_withhold_rows(self):
        path = self.write('def safe():\n    return 1\n')
        facts = source_facts.analyze(self.root)
        path.write_text(path.read_text().replace('return 1', 'return 2'))
        reports = [decomposition.review(self.root, facts)]
        facts = source_facts.analyze(self.root)
        changed = copy.deepcopy(facts)
        changed["functions"][0]["id"] = 'a' * 64
        reports.append(decomposition.review(self.root, changed))
        with patch.object(decomposition, "MAX_OWNERS", 0):
            reports.append(decomposition.review(self.root, facts))
        for report in reports:
            self.assertEqual(report["status"], "incomplete")
            self.assertTrue(report["diagnostics"])
            self.assertEqual(report["functions"], [])
            self.assertIsNone(report["summary"])

    def test_parse_failure_never_exposes_partial_results(self):
        self.write('def valid():\n    return 1\n')
        self.write('def broken(:\n', 'broken.py')
        report = decomposition.analyze(self.root)
        self.assertEqual(report["status"], "incomplete")
        self.assertEqual(report["functions"], [])
        self.assertIsNone(report["summary"])

    def test_cli_preserves_source_and_existing_output(self):
        source = self.write('def value():\n    return 1\n')
        script = Path(decomposition.__file__)
        output = self.root / 'review.json'
        command = [sys.executable, str(script), str(source), '--output', str(output)]
        first = subprocess.run(command, capture_output=True, timeout=30)
        self.assertEqual(first.returncode, 0, first.stderr)
        saved = output.read_bytes()
        self.assertEqual(subprocess.run(command, capture_output=True, timeout=30).returncode, 2)
        self.assertEqual(output.read_bytes(), saved)
        self.assertEqual(subprocess.run(command[:-1] + [str(source)], capture_output=True, timeout=30).returncode, 2)
        self.assertEqual(source.read_text(), 'def value():\n    return 1\n')


if __name__ == '__main__':
    unittest.main()
