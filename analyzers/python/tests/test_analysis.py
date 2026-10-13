import contextlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import analysis
import duplication
import decomposition


class AnalysisTests(unittest.TestCase):
    def test_combined_measurements_share_population_and_propagate_duplication_failure(self):
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, "work.py").write_text("def work(x):\n    a = x\n    a += 1\n    a += 2\n    a += 3\n    return a\n")
            argv = ["analysis.py", directory, "--run-id", "11111111-1111-4111-8111-111111111111", "--audit-id", "22222222-2222-4222-8222-222222222222"]
            for budget, code in [(67108864, 0), (1, 2)]:
                with patch.object(sys, "argv", argv), patch.object(duplication, "MAX_NORMALIZED_BYTES", budget), contextlib.redirect_stdout(io.StringIO()) as output:
                    self.assertEqual(analysis.main(), code)
                result = json.loads(output.getvalue())
                self.assertEqual(result["sourceFacts"]["status"], "complete")
                self.assertEqual(result["sourceFacts"]["selection"], result["duplication"]["selection"])
                self.assertEqual(result["duplication"]["runId"], argv[3])
                self.assertEqual(result["decomposition"]["status"], "complete")
                self.assertEqual(result["decomposition"]["selection"], result["sourceFacts"]["selection"])
                self.assertEqual(result["decomposition"]["runId"], argv[3])
                if code:
                    self.assertEqual(result["duplication"]["status"], "incomplete")
                    self.assertIsNone(result["duplication"]["summary"])
                    self.assertEqual(result["duplication"]["groups"], [])

    def test_decomposition_failure_propagates_with_complete_other_measurements(self):
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, "work.py").write_text("def work(): return 1\n")
            argv = ["analysis.py", directory, "--run-id", "11111111-1111-4111-8111-111111111111", "--audit-id", "22222222-2222-4222-8222-222222222222"]
            with patch.object(sys, "argv", argv), patch.object(decomposition, "MAX_OWNERS", 0), contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(analysis.main(), 2)
            result = json.loads(output.getvalue())
            for key in ("sourceFacts", "duplication", "coverageReview"):
                self.assertEqual(result[key]["status"], "complete")
            self.assertEqual(result["decomposition"]["status"], "incomplete")
            self.assertEqual(result["decomposition"]["functions"], [])
            self.assertIsNone(result["decomposition"]["summary"])


if __name__ == "__main__":
    unittest.main()
