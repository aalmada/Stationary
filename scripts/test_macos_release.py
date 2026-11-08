import os
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

from validate_macos_bundle import run, validate_metadata

ROOT = Path(__file__).resolve().parent.parent


class BundleMetadataTests(unittest.TestCase):
    def test_native_validation_failures_preserve_diagnostics(self):
        with patch("validate_macos_bundle.subprocess.run", return_value=subprocess.CompletedProcess(
            ["codesign"], 1, b"", b"resource envelope is obsolete"
        )):
            with self.assertRaisesRegex(RuntimeError, "resource envelope is obsolete"):
                run("codesign", "--verify", "test.app")

    def setUp(self):
        self.info = {
            "CFBundleIdentifier": "com.stationary.ftms.dashboard",
            "CFBundleShortVersionString": "1.2.3",
            "CFBundleVersion": "4",
            "CFBundleExecutable": "Stationary",
            "LSMinimumSystemVersion": "14.0",
        }
        self.entitlements = {"com.apple.security.device.bluetooth": True}

    def test_valid_metadata_reports_generated_minimum(self):
        self.assertEqual(validate_metadata(self.info, self.entitlements, "1.2.3", "4"), "14.0")

    def test_version_build_identity_and_minimum_are_required(self):
        for key, value in (
            ("CFBundleIdentifier", "other.app"),
            ("CFBundleShortVersionString", "1.2.4"),
            ("CFBundleVersion", "5"),
            ("LSMinimumSystemVersion", None),
            ("CFBundleExecutable", "../unsafe"),
        ):
            with self.subTest(key=key), self.assertRaises(ValueError):
                validate_metadata(self.info | {key: value}, self.entitlements, "1.2.3", "4")

    def test_both_debug_entitlement_spellings_are_rejected(self):
        for key in ("get-task-allow", "com.apple.security.get-task-allow"):
            for value in (True, 0, "false"):
                with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                    validate_metadata(self.info, self.entitlements | {key: value}, "1.2.3", "4")

    def test_bluetooth_must_remain_enabled(self):
        for value in (None, False, "true"):
            with self.subTest(value=value), self.assertRaises(ValueError):
                validate_metadata(self.info, {"com.apple.security.device.bluetooth": value}, "1.2.3", "4")


class PackagingPreflightTests(unittest.TestCase):
    def test_prerelease_sdk_fails_before_output_is_created(self):
        with tempfile.TemporaryDirectory(prefix="stationary-preflight-") as directory:
            root = Path(directory)
            tools = root / "tools"
            tools.mkdir()
            for name, content in (
                ("uname", "#!/bin/sh\nprintf 'Darwin\\n'\n"),
                ("dotnet", "#!/bin/sh\nprintf '11.0.100-rc.3\\n'\n"),
            ):
                script = tools / name
                script.write_text(content)
                script.chmod(0o755)
            output = root / "output"
            result = subprocess.run(
                ["bash", str(ROOT / "scripts" / "publish-macos.sh"), "1.2.3", "4", str(output)],
                env=os.environ | {"PATH": f"{tools}:{os.environ['PATH']}"},
                capture_output=True, text=True,
            )
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("stable .NET 11", result.stderr)
            self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
