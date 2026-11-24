import os
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import validate


class ValidationTests(unittest.TestCase):
    def test_dotnet_does_not_inherit_msvc_platform_but_native_compiler_does(self):
        with patch.dict(os.environ, {"Platform": "x64", "VSINSTALLDIR": "native-toolchain"}), \
             patch.object(validate.subprocess, "run") as process:
            validate.run("dotnet", "restore", validate.SOLUTION)
            environment = process.call_args.kwargs["env"]
            self.assertNotIn("Platform", environment)
            self.assertEqual("native-toolchain", environment["VSINSTALLDIR"])
            validate.run("cl", "/W4")
            self.assertIsNone(process.call_args.kwargs["env"])
            self.assertEqual("x64", os.environ["Platform"])

    def test_empty_failed_and_skipped_reports_are_not_success(self):
        with tempfile.TemporaryDirectory() as directory:
            report = Path(directory) / "result.trx"
            for counters in ('total="0"', 'total="1" failed="1"', 'total="1" notExecuted="1"'):
                report.write_text(f'<TestRun><ResultSummary><Counters {counters}/></ResultSummary></TestRun>')
                with self.assertRaises(RuntimeError):
                    validate.check_report(report, "unit")

    def test_unit_resource_policy_rejects_subprocess_and_worker_calls(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            tests = root / "PoshMcp.Foundation.Tests"
            tests.mkdir()
            for call in ("Process.Start()", "WorkerProbe.StartAsync()"):
                (tests / "Misclassified.cs").write_text('[Trait("Category", "Unit")]\n' + call)
                with patch.object(validate, "ROOT", root), self.assertRaises(RuntimeError):
                    validate.unit_source_policy()

    def test_all_tests_lane_rejects_unprovisioned_runtime(self):
        with patch.dict("os.environ", {"POSHMCP_TEST_RUNTIME": ""}), self.assertRaisesRegex(RuntimeError, "missing runtimes"):
            validate.tests("all")


if __name__ == "__main__":
    unittest.main()
