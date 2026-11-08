import tempfile
import unittest
from pathlib import Path

from build_site import build, check_links, download_markup


class SiteTests(unittest.TestCase):
    def test_unreleased_site_has_no_fake_download(self):
        with tempfile.TemporaryDirectory(prefix="stationary-site-test-") as directory:
            output = Path(directory)
            build(output, {"version": None, "minimumMacOS": None})
            index = (output / "index.html").read_text()
            self.assertIn("Download not yet available", index)
            self.assertNotIn("{{DOWNLOAD}}", index)
            self.assertNotIn('href="https://github.com/aalmada/Stationary/releases/download/', index)
            self.assertTrue((output / "images" / "ride.png").is_file())
            check_links(output)

    def test_published_site_uses_versioned_assets(self):
        markup = download_markup({"version": "1.2.3", "minimumMacOS": "14.0"})
        self.assertIn("/v1.2.3/Stationary-1.2.3-macos-arm64.dmg", markup)
        self.assertIn("/v1.2.3/SHA256SUMS", markup)
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


if __name__ == "__main__":
    unittest.main()
