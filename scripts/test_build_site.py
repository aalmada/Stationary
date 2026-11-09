import tempfile
import unittest
from pathlib import Path

from build_site import build, check_links, download_markup
from check_release_links import validate_documents


class SiteTests(unittest.TestCase):
    def test_unreleased_site_has_no_fake_download(self):
        with tempfile.TemporaryDirectory(prefix="stationary-site-test-") as directory:
            output = Path(directory)
            build(output, {"version": None, "minimumMacOS": None})
            index = (output / "index.html").read_text()
            self.assertIn("Beta download not yet available", index)
            self.assertIn("features and behavior may change", index)
            self.assertNotIn("{{DOWNLOAD}}", index)
            self.assertNotIn('href="https://github.com/aalmada/Stationary/releases/download/', index)
            self.assertTrue((output / "images" / "ride.png").is_file())
            check_links(output)

    def test_published_site_uses_versioned_assets(self):
        markup = download_markup({"version": "1.2.3", "minimumMacOS": "14.0"})
        self.assertIn("/v1.2.3/Stationary-1.2.3-macos-arm64.dmg", markup)
        self.assertIn("/v1.2.3/SHA256SUMS", markup)
        self.assertIn("Download Stationary Beta 1.2.3", markup)
        self.assertIn("macOS 14.0", markup)

    def test_invalid_metadata_fails(self):
        for metadata in (
            {"version": "latest", "minimumMacOS": "14.0"},
            {"version": "1.2.3", "minimumMacOS": None},
            {"version": None, "minimumMacOS": "14.0"},
            {"version": "../unsafe", "minimumMacOS": "14.0"},
            {"version": None, "minimumMacOS": None, "extra": True},
        ):
            with self.subTest(metadata=metadata), self.assertRaises(ValueError):
                download_markup(metadata)

    def test_broken_link_is_an_error(self):
        with tempfile.TemporaryDirectory(prefix="stationary-site-test-") as directory:
            output = Path(directory)
            (output / "index.html").write_text('<a href="missing.html">Missing</a>')
            with self.assertRaises(ValueError):
                check_links(output)

    def test_existing_output_cannot_publish_incidental_files(self):
        with tempfile.TemporaryDirectory(prefix="stationary-site-test-") as directory:
            output = Path(directory)
            (output / "internal.log").write_text("Not a website asset")
            with self.assertRaises(ValueError):
                build(output, {"version": None, "minimumMacOS": None})
            self.assertTrue((output / "internal.log").is_file())

    def test_source_output_is_rejected(self):
        from build_site import ROOT
        with self.assertRaises(ValueError):
            build(ROOT / "src", {"version": None, "minimumMacOS": None})

    def test_release_documents_agree_with_website(self):
        release = {"version": "1.2.3", "minimumMacOS": "14.0"}
        checksum = "a" * 64 + "  Stationary-1.2.3-macos-arm64.dmg\n"
        info = "Channel: beta\nVersion: 1.2.3\nBuild: 4\nMinimum macOS: 14.0\nToolchain details\n"
        validate_documents(release, checksum, info)
        for invalid in (
            info.replace("Version: 1.2.3", "Version: 1.2.4"),
            info.replace("macOS: 14.0", "macOS: 15.0"),
            info.replace("Build: 4", "Build: 0"),
        ):
            with self.subTest(info=invalid), self.assertRaises(ValueError):
                validate_documents(release, checksum, invalid)
        for invalid in (checksum.replace("1.2.3", "1.2.4"), checksum + checksum):
            with self.subTest(checksum=invalid), self.assertRaises(ValueError):
                validate_documents(release, invalid, info)


if __name__ == "__main__":
    unittest.main()
