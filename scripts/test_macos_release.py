import os
import runpy
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
    def test_prerelease_sdk_requires_explicit_beta_channel(self):
        with tempfile.TemporaryDirectory(prefix="stationary-preflight-") as directory:
            root = Path(directory)
            tools = root / "tools"
            tools.mkdir()
            for name, content in (
                ("uname", "#!/bin/sh\nprintf 'Darwin\\n'\n"),
                ("dotnet", "#!/bin/sh\nif [ \"$1\" = --version ]; then printf '11.0.100-rc.3\\n'; else echo 'mock restore reached' >&2; exit 42; fi\n"),
                ("codesign", "#!/bin/sh\nexit 1\n"),
                ("hdiutil", "#!/bin/sh\nexit 1\n"),
                ("ditto", "#!/bin/sh\nexit 1\n"),
                ("lipo", "#!/bin/sh\nexit 1\n"),
                ("xcodebuild", "#!/bin/sh\nexit 1\n"),
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
            self.assertIn("stable", result.stderr)
            self.assertFalse(output.exists())
            result = subprocess.run(
                ["bash", str(ROOT / "scripts" / "publish-macos.sh"), "--beta", "1.2.3", "4", str(output)],
                env=os.environ | {"PATH": f"{tools}:{os.environ['PATH']}"},
                capture_output=True, text=True,
            )
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("mock restore reached", result.stderr)
            self.assertTrue(output.exists())
            self.assertFalse(any(output.iterdir()))


class InstallerLayoutTests(unittest.TestCase):
    def test_layout_preserves_install_target_and_readable_positions(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            app = root / "Stationary.app"
            app.mkdir()
            background = root / "background.png"
            background.write_bytes(b"test")
            settings = runpy.run_path(
                str(ROOT / "scripts" / "dmg_settings.py"),
                init_globals={"defines": {"app": str(app), "background": str(background)}},
            )
            self.assertEqual(settings["symlinks"], {"Applications": "/Applications"})
            self.assertEqual(settings["format"], "UDZO")
            self.assertEqual(settings["window_rect"][1], (760, 480))
            self.assertEqual(settings["icon_locations"],
                             {"Stationary.app": (190, 240), "Applications": (570, 240)})
            self.assertEqual(settings["icon_size"], 112)
            self.assertFalse(settings["show_toolbar"])
            self.assertFalse(settings["show_sidebar"])
            self.assertEqual(settings["hide_extensions"], [])

    def test_missing_artwork_or_app_fails_explicitly(self):
        with self.assertRaisesRegex(ValueError, "staged app and background"):
            runpy.run_path(
                str(ROOT / "scripts" / "dmg_settings.py"),
                init_globals={"defines": {"app": "/missing.app", "background": "/missing.png"}},
            )

    def test_mounted_bundle_validator_failure_stops_packaging(self):
        script = (ROOT / "scripts" / "publish-macos.sh").read_text()
        start = script.index('mounted_minimum="')
        end = script.index('hdiutil detach "$mount"', start)
        with tempfile.TemporaryDirectory() as directory:
            tool = Path(directory) / "python3"
            tool.write_text("#!/bin/sh\necho 'invalid signature' >&2\nexit 1\n")
            tool.chmod(0o755)
            result = subprocess.run(
                ["bash", "-c", "set -e\n" + script[start:end] + "\necho INVALID_SUCCESS"],
                env=os.environ | {"PATH": f"{directory}:{os.environ['PATH']}"},
                capture_output=True, text=True,
            )
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("invalid signature", result.stderr)
            self.assertNotIn("INVALID_SUCCESS", result.stdout)


if __name__ == "__main__":
    unittest.main()
