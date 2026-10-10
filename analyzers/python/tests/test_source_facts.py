import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


MODULE = Path(__file__).resolve().parents[1] / "source_facts.py"
sys.path.insert(0, str(MODULE.parent))
spec = importlib.util.spec_from_file_location("source_facts", MODULE)
facts = importlib.util.module_from_spec(spec)
spec.loader.exec_module(facts)


class SourceFactsTests(unittest.TestCase):
    def measures(self, source):
        return {item["member"]: item["complexity"] for item in facts.measure_source(source, "code.py")}

    def test_exclusive_ownership_and_definition_metadata(self):
        rows = self.measures('''
@decorator(lambda x: x if x else 0)
def outer(value=1 if flag else 2):
    def inner(x):
        if x:
            return 1
        return 0
    class Nested:
        setting = 1 if flag else 2
        def method(self):
            return 1 if self else 0
    return inner(value)
''')
        self.assertEqual({key: row["value"] for key, row in rows.items()}, {"outer": 1, "outer.inner": 2, "outer.Nested.method": 2})

    def test_async_and_lambdas(self):
        rows = self.measures('''
async def work(items):
    async for item in items:
        if item:
            await save(item)
    return lambda x: x if x else 0
''')
        self.assertEqual(rows["work"]["value"], 3)
        self.assertEqual(len(rows), 2)
        self.assertEqual(list(rows.values())[1]["value"], 2)

    def test_local_annotations_are_excluded_but_values_are_measured(self):
        rows = self.measures('''
def work(flag):
    result: (str if flag else int) = 1 if flag else 2
    callback: (lambda x: x if x else 0)
    return result
''')
        self.assertEqual(len(rows), 1)
        self.assertEqual(rows["work"]["value"], 2)

    def test_python_specific_decisions(self):
        row = self.measures('''
def work(items, x):
    result = [item for item in items if item and x]
    if 0 < x < 10 or x == 20:
        return result
    try:
        save(result)
    except ValueError:
        return []
    except TypeError:
        raise
''')["work"]
        self.assertEqual(row["value"], 9)
        self.assertEqual(row["decisions"]["chainedComparisons"], 1)
        self.assertEqual(row["decisions"]["booleanOperators"], 2)
        self.assertEqual(row["decisions"]["exceptHandlers"], 2)

    def test_match_default_guard_and_alternatives(self):
        row = self.measures('''
def choose(value):
    match value:
        case 1 | 2:
            return True
        case x if x > 3 and x < 10:
            return x
        case _:
            return False
''')["choose"]
        self.assertEqual(row["value"], 4)
        self.assertEqual(row["decisions"]["matchCases"], 1)
        self.assertEqual(row["decisions"]["matchGuards"], 1)
        self.assertEqual(row["decisions"]["matchAlternatives"], 0)

    def test_no_execution_and_deterministic_relative_ids(self):
        with tempfile.TemporaryDirectory() as first, tempfile.TemporaryDirectory() as second:
            source = 'raise RuntimeError("Do not execute")\n\ndef café(x):\n    return x\n'
            for root in (first, second):
                (Path(root) / "code.py").write_text(source, encoding="utf-8")
            self.assertEqual(facts.analyze(first), facts.analyze(second))
            row = facts.analyze(first)["functions"][0]
            self.assertEqual(row["file"], "code.py")
            self.assertEqual(row["line"], 3)

    def test_encoding_cookie_and_default_directory_exclusions(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "code.py").write_bytes(b'# coding: latin-1\n# caf\xe9\ndef run():\n    pass\n')
            (root / ".venv").mkdir()
            (root / ".venv" / "broken.py").write_text("def broken(")
            document = facts.analyze(root)
            self.assertEqual(document["status"], "complete")
            self.assertEqual(len(document["functions"]), 1)

    def test_parse_failure_preserves_diagnostic_only_facts(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "good.py").write_text("def run():\n    return 1\n")
            (root / "broken.py").write_text("def broken(")
            run = subprocess.run([sys.executable, str(MODULE), str(root)], capture_output=True, text=True)
            self.assertEqual(run.returncode, 2)
            document = json.loads(run.stdout)
            self.assertEqual(document["status"], "incomplete")
            self.assertEqual(len(document["functions"]), 1)
            self.assertEqual(document["diagnostics"][0]["file"], "broken.py")

    def test_empty_missing_and_limits(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.assertEqual(facts.analyze(root)["functions"], [])
            self.assertEqual(facts.analyze(root / "missing")["status"], "incomplete")
            (root / "big.py").write_text("#" * (facts.MAX_FILE_BYTES + 1))
            self.assertEqual(facts.analyze(root)["status"], "incomplete")
            old_limit = facts.MAX_FILES
            try:
                facts.MAX_FILES = 0
                self.assertEqual(facts.analyze(root)["diagnostics"][0]["code"], "file-limit")
            finally:
                facts.MAX_FILES = old_limit

    def test_output_is_never_allowed_to_overwrite_a_file(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / "code.py"
            source.write_text("def run():\n    pass\n")
            for output in (source, root / "existing.json"):
                if output != source:
                    output.write_text("existing")
                before = output.read_bytes()
                run = subprocess.run([sys.executable, str(MODULE), str(source), "--output", str(output)], capture_output=True, text=True)
                self.assertEqual(run.returncode, 2)
                self.assertEqual(output.read_bytes(), before)
            output = root / "new.json"
            run = subprocess.run([sys.executable, str(MODULE), str(source), "--output", str(output)], capture_output=True, text=True)
            self.assertEqual(run.returncode, 0, run.stderr)
            self.assertEqual(json.loads(output.read_text())["status"], "complete")


if __name__ == "__main__":
    unittest.main()
