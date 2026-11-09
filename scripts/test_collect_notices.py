import json
import tempfile
import unittest
from pathlib import Path

from collect_notices import collect, validate_release_assets


class NoticesTests(unittest.TestCase):
    def test_release_requires_stable_dependencies_and_exact_runtime_notices(self):
        with tempfile.TemporaryDirectory(prefix="stationary-notices-") as directory:
            supplements = Path(directory)
            assets = {
                "libraries": {"Example/1.0.0": {"type": "package"}},
                "project": {"frameworks": {"net11.0-maccatalyst": {"downloadDependencies": [{
                    "name": "Microsoft.NETCore.App.Runtime.maccatalyst-arm64",
                    "version": "[11.0.1, 11.0.1]",
                }]}}},
            }
            with self.assertRaisesRegex(ValueError, "runtime are missing"):
                validate_release_assets(assets, supplements, allow_prerelease=False)
            for name in ("license", "third-party-notices"):
                (supplements / f"additional-dotnet-runtime-11.0.1-{name}.txt").write_text("Runtime fixture")
            validate_release_assets(assets, supplements)
            with self.assertRaisesRegex(ValueError, "stable packages"):
                validate_release_assets(
                    assets | {"libraries": {"Example/1.0.0-rc.1": {"type": "package"}}},
                    supplements,
                    allow_prerelease=False,
                )
            runtime = assets["project"]["frameworks"]["net11.0-maccatalyst"]["downloadDependencies"][0]
            runtime["version"] = "[11.0.1-rc.1, 11.0.1-rc.1]"
            with self.assertRaisesRegex(ValueError, "stable .NET 11 runtime"):
                validate_release_assets(assets, supplements)
            (supplements / "additional-dotnet-runtime-11.0.1-rc.1-license.txt").write_text("RC license")
            (supplements / "additional-dotnet-runtime-11.0.1-rc.1-third-party-notices.txt").write_text("RC notices")
            validate_release_assets(assets, supplements, allow_prerelease=True)
            for version in ("[11.0.1-rc.1, 11.0.1-rc.2]", "[10.0.1, 10.0.1]"):
                runtime["version"] = version
                with self.assertRaisesRegex(ValueError, "exact .NET 11"):
                    validate_release_assets(assets, supplements, allow_prerelease=True)

    def test_package_files_are_preserved_and_missing_text_is_an_error(self):
        with tempfile.TemporaryDirectory(prefix="stationary-notices-") as directory:
            root = Path(directory)
            package = root / "packages/example/1.0.0"
            package.mkdir(parents=True)
            (package / "example.nuspec").write_text(
                '<package><metadata><license type="expression">MIT</license>'
                '<copyright>Example test author</copyright></metadata></package>'
            )
            assets = root / "assets.json"
            assets.write_text(json.dumps({
                "packageFolders": {str(root / "packages"): {}},
                "libraries": {"Example/1.0.0": {"type": "package", "path": "example/1.0.0"}},
            }))
            output = root / "output"
            supplements = root / "supplements"
            with self.assertRaises(ValueError):
                collect(assets, output, supplements)
            self.assertFalse((output / "THIRD-PARTY-NOTICES.txt").exists())
            self.assertFalse(json.loads((output / "dependency-licenses.json").read_text())[0]["fullLicenseIncluded"])
            (package / "LICENSE.txt").write_text("Example license fixture.\n")
            (package / "THIRD-PARTY-NOTICES.txt").write_text("Example nested notice fixture.\n")
            self.assertEqual(collect(assets, output, supplements), ["Example/1.0.0"])
            text = (output / "THIRD-PARTY-NOTICES.txt").read_text()
            self.assertIn("Example license fixture.\n", text)
            self.assertIn("Example nested notice fixture.\n", text)
            self.assertIn("Example test author", text)
            (package / "LICENSE.txt").unlink()
            with self.assertRaises(ValueError):
                collect(assets, output, supplements)
            self.assertFalse((output / "THIRD-PARTY-NOTICES.txt").exists())

    def test_reviewed_supplements_can_supply_missing_license_text(self):
        with tempfile.TemporaryDirectory(prefix="stationary-notices-") as directory:
            root = Path(directory)
            package = root / "packages/example/1.0.0"
            package.mkdir(parents=True)
            (package / "example.nuspec").write_text("<package><metadata/></package>")
            assets = root / "assets.json"
            assets.write_text(json.dumps({
                "packageFolders": {str(root / "packages"): {}},
                "libraries": {"Example/1.0.0": {"type": "package", "path": "example/1.0.0"}},
            }))
            supplements = root / "supplements"
            supplements.mkdir()
            (supplements / "example-1.0.0.txt").write_text("Reviewed license fixture.\n")
            (supplements / "additional-runtime.txt").write_text("Runtime notice fixture.\n")
            output = root / "output"
            collect(assets, output, supplements)
            text = (output / "THIRD-PARTY-NOTICES.txt").read_text()
            self.assertIn("Reviewed license fixture.", text)
            self.assertIn("Runtime notice fixture.", text)
            (supplements / "example-1.0.0.txt").write_text(" \n")
            with self.assertRaisesRegex(ValueError, "Empty license supplement"):
                collect(assets, output, supplements)
            self.assertFalse((output / "THIRD-PARTY-NOTICES.txt").exists())

    def test_empty_or_invalid_assets_invalidate_previous_outputs(self):
        with tempfile.TemporaryDirectory(prefix="stationary-notices-") as directory:
            root = Path(directory)
            assets = root / "assets.json"
            output = root / "output"
            output.mkdir()
            for content in ('{"packageFolders": {}, "libraries": {}}', "{invalid"):
                assets.write_text(content)
                (output / "THIRD-PARTY-NOTICES.txt").write_text("Stale notices")
                (output / "dependency-licenses.json").write_text("Stale inventory")
                with self.assertRaises(ValueError):
                    collect(assets, output, root / "supplements")
                self.assertFalse((output / "THIRD-PARTY-NOTICES.txt").exists())
                self.assertFalse((output / "dependency-licenses.json").exists())


if __name__ == "__main__":
    unittest.main()
