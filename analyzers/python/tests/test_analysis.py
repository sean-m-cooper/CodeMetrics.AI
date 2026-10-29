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
import async_adapters
import duplication
import decomposition
import error_handling
import error_handling_context
import error_handling_transfers
import error_handling_operations
import async_usage
import async_context


class AnalysisTests(unittest.TestCase):
    def test_adapter_failure_preserves_prior_measurements_but_fails_run(self):
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, "work.py").write_text("async def work():\n    return 1\n")
            argv = ["analysis.py", directory, "--run-id", "11111111-1111-4111-8111-111111111111", "--audit-id", "22222222-2222-4222-8222-222222222222"]
            with patch.object(sys, "argv", argv), patch.object(async_adapters, "collect", side_effect=ValueError("Source drift")), contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(analysis.main(), 2)
            result = json.loads(output.getvalue())
            for key, report in result.items():
                self.assertEqual(report["status"], "incomplete" if key == "asyncAdapterReview" else "complete")
            report = result["asyncAdapterReview"]
            self.assertEqual(report["functions"], [])
            self.assertIsNone(report["summary"])
            self.assertEqual(report["runId"], argv[3])

    def test_async_context_failure_fails_combined_run_with_prior_async_complete(self):
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, "work.py").write_text("async def work():\n    call()\n", encoding="utf-8")
            argv = ["analysis.py", directory, "--run-id", "11111111-1111-4111-8111-111111111111", "--audit-id", "22222222-2222-4222-8222-222222222222"]
            with patch.object(sys, "argv", argv), patch.object(async_context, "collect", side_effect=ValueError("Context source drift")), contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(analysis.main(), 2)
            result = json.loads(output.getvalue())
            self.assertEqual(result["asyncUsageReview"]["status"], "complete")
            self.assertEqual(result["errorHandlingOperations"]["status"], "complete")
            report = result["asyncContextReview"]
            self.assertEqual(report["status"], "incomplete")
            self.assertEqual(report["runId"], argv[3])
            self.assertEqual(report["auditId"], argv[5])
            self.assertEqual(report["functions"], [])
            self.assertIsNone(report["summary"])

    def test_async_failure_fails_combined_run_with_prior_measurements_complete(self):
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, "work.py").write_text("import time\nasync def work():\n    time.sleep(1)\n", encoding="utf-8")
            argv = ["analysis.py", directory, "--run-id", "11111111-1111-4111-8111-111111111111", "--audit-id", "22222222-2222-4222-8222-222222222222"]
            with patch.object(sys, "argv", argv), patch.object(async_usage, "read_verified", side_effect=ValueError("Source drift")), contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(analysis.main(), 2)
            result = json.loads(output.getvalue())
            for key in ("sourceFacts", "duplication", "coverageReview", "decomposition", "errorHandlingReview", "errorHandlingContext", "errorHandlingTransfers", "errorHandlingOperations"):
                self.assertEqual(result[key]["status"], "complete")
            report = result["asyncUsageReview"]
            self.assertEqual(report["status"], "incomplete")
            self.assertEqual(report["runId"], argv[3])
            self.assertEqual(report["auditId"], argv[5])
            self.assertEqual(report["selection"], result["sourceFacts"]["selection"])
            self.assertEqual(report["functions"], [])
            self.assertIsNone(report["summary"])

    def test_operation_failure_withholds_only_new_rows_and_fails_combined_run(self):
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, "work.py").write_text("def work():\n    try: call()\n    except Missing: return recover()\n", encoding="utf-8")
            argv = ["analysis.py", directory, "--run-id", "11111111-1111-4111-8111-111111111111", "--audit-id", "22222222-2222-4222-8222-222222222222"]
            with patch.object(sys, "argv", argv), patch.object(error_handling_operations, "read_verified", side_effect=ValueError("Source drift")), contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(analysis.main(), 2)
            result = json.loads(output.getvalue())
            for key in ("sourceFacts", "duplication", "coverageReview", "decomposition", "errorHandlingReview", "errorHandlingContext", "errorHandlingTransfers"):
                self.assertEqual(result[key]["status"], "complete")
            report = result["errorHandlingOperations"]
            self.assertEqual(report["transferReview"], result["errorHandlingTransfers"])
            self.assertEqual(report["status"], "incomplete")
            self.assertEqual(report["handlers"], [])
            self.assertIsNone(report["summary"])

    def test_transfer_failure_propagates_without_mutating_prior_evidence(self):
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, "work.py").write_text("def work():\n    try: call()\n    except Missing as error: raise New() from error\n")
            argv = ["analysis.py", directory, "--run-id", "11111111-1111-4111-8111-111111111111", "--audit-id", "22222222-2222-4222-8222-222222222222"]
            with patch.object(sys, "argv", argv), patch.object(error_handling_transfers, "read_verified", side_effect=ValueError("Source drift")), contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(analysis.main(), 2)
            result = json.loads(output.getvalue())
            for key in ("sourceFacts", "duplication", "coverageReview", "decomposition", "errorHandlingReview", "errorHandlingContext"):
                self.assertEqual(result[key]["status"], "complete")
            report = result["errorHandlingTransfers"]
            self.assertEqual(report["contextReview"], result["errorHandlingContext"])
            self.assertEqual(report["status"], "incomplete")
            self.assertEqual(report["handlers"], [])
            self.assertIsNone(report["summary"])
            self.assertEqual(report["diagnostics"][0]["code"], "exception-transfer-analysis")

    def test_context_failure_withholds_context_without_mutating_base_measurements(self):
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, "work.py").write_text("def work():\n    try: call()\n    except Missing: return None\n")
            argv = ["analysis.py", directory, "--run-id", "11111111-1111-4111-8111-111111111111", "--audit-id", "22222222-2222-4222-8222-222222222222"]
            with patch.object(sys, "argv", argv), patch.object(error_handling_context, "read_verified", side_effect=ValueError("Source drift")), contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(analysis.main(), 2)
            result = json.loads(output.getvalue())
            for key in ("sourceFacts", "duplication", "coverageReview", "decomposition", "errorHandlingReview"):
                self.assertEqual(result[key]["status"], "complete")
            context = result["errorHandlingContext"]
            self.assertEqual(context["handlerReview"], result["errorHandlingReview"])
            self.assertEqual(context["status"], "incomplete")
            self.assertEqual(context["contexts"], [])
            self.assertIsNone(context["summary"])
            self.assertEqual(context["diagnostics"][0]["code"], "exception-context-analysis")

    def test_handler_failure_propagates_with_complete_other_measurements(self):
        with tempfile.TemporaryDirectory() as directory:
            Path(directory, "work.py").write_text("try: call()\nexcept: pass\n")
            argv = ["analysis.py", directory, "--run-id", "11111111-1111-4111-8111-111111111111", "--audit-id", "22222222-2222-4222-8222-222222222222"]
            with patch.object(sys, "argv", argv), patch.object(error_handling, "MAX_HANDLERS", 0), contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(analysis.main(), 2)
            result = json.loads(output.getvalue())
            for key in ("sourceFacts", "duplication", "coverageReview", "decomposition"):
                self.assertEqual(result[key]["status"], "complete")
            report = result["errorHandlingReview"]
            self.assertEqual(report["runId"], argv[3])
            self.assertEqual(report["selection"], result["sourceFacts"]["selection"])
            self.assertEqual(report["status"], "incomplete")
            self.assertEqual(report["handlers"], [])
            self.assertIsNone(report["summary"])

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
