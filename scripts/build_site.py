#!/usr/bin/env python3
"""Build and check the static Pages site without a frontend toolchain."""

import argparse
import html
from html.parser import HTMLParser
import json
from pathlib import Path
import re
import shutil
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parent.parent


def download_markup(release: dict) -> str:
    if set(release) != {"version", "minimumMacOS"}:
        raise ValueError("Release metadata must contain version and minimumMacOS only.")
    version, minimum = release["version"], release["minimumMacOS"]
    if version is None and minimum is None:
        return (
            '<p><strong>Download not yet available.</strong> A website build will be linked '
            'here after installation and hardware validation.</p>'
            '<p><a href="https://github.com/aalmada/Stationary#run-from-source">'
            "Build from source in the meantime</a>.</p>"
        )
    if not isinstance(version, str) or not re.fullmatch(r"\d+\.\d+\.\d+", version):
        raise ValueError("A published version must be major.minor.patch.")
    if not isinstance(minimum, str) or not re.fullmatch(r"\d+\.\d+(?:\.\d+)?", minimum):
        raise ValueError("A published release requires a verified minimum macOS version.")
    base = f"https://github.com/aalmada/Stationary/releases/download/v{version}"
    return (
        f'<a class="button" href="{base}/Stationary-{version}-macos-arm64.dmg">'
        f"Download Stationary {html.escape(version)}</a>"
        f"<p>Requires macOS {html.escape(minimum)} or later and Apple silicon.</p>"
        f'<p><a href="{base}/SHA256SUMS">SHA-256 checksums</a> &middot; '
        f'<a href="https://github.com/aalmada/Stationary/releases/tag/v{version}">'
        "Release notes</a></p>"
    )


class Links(HTMLParser):
    def __init__(self):
        super().__init__()
        self.urls = []
        self.ids = set()

    def handle_starttag(self, tag, attrs):
        attributes = dict(attrs)
        if "id" in attributes:
            if attributes["id"] in self.ids:
                raise ValueError(f"Duplicate HTML id: {attributes['id']}")
            self.ids.add(attributes["id"])
        for key in ("href", "src"):
            if key in attributes:
                self.urls.append(attributes[key])


def check_links(output: Path) -> None:
    pages = {}
    for path in output.glob("*.html"):
        parser = Links()
        parser.feed(path.read_text())
        pages[path.resolve()] = parser
    for path, parser in pages.items():
        for url in parser.urls:
            parts = urlsplit(url)
            if parts.scheme or parts.netloc:
                continue
            target = (path.parent / unquote(parts.path)).resolve() if parts.path else path
            if not target.is_relative_to(output.resolve()) or not target.is_file():
                raise ValueError(f"{path.name}: broken or out-of-site link {url}")
            if parts.fragment and target in pages and unquote(parts.fragment) not in pages[target].ids:
                raise ValueError(f"{path.name}: missing fragment {url}")


def build(output: Path, release: dict) -> None:
    output = output.resolve()
    if output == ROOT or output in ROOT.parents or output.is_relative_to(ROOT / "site"):
        raise ValueError("Build output must not overwrite the repository or site sources.")
    markup = download_markup(release)
    output.mkdir(parents=True, exist_ok=True)
    for source in (ROOT / "site").iterdir():
        if source.suffix == ".html":
            text = source.read_text()
            if source.name == "index.html" and text.count("{{DOWNLOAD}}") != 1:
                raise ValueError("index.html must contain one download placeholder.")
            (output / source.name).write_text(text.replace("{{DOWNLOAD}}", markup))
        elif source.suffix == ".css":
            shutil.copyfile(source, output / source.name)
    (output / "images").mkdir(exist_ok=True)
    for name in ("ride.png", "ride-scene.png"):
        shutil.copyfile(ROOT / "assets" / "screenshots" / name, output / "images" / name)
    (output / ".nojekyll").write_text("")
    check_links(output)


if __name__ == "__main__":
    argument_parser = argparse.ArgumentParser(description=__doc__)
    argument_parser.add_argument("--output", type=Path, default=ROOT / "artifacts" / "site")
    arguments = argument_parser.parse_args()
    with (ROOT / "site" / "release.json").open() as metadata:
        build(arguments.output, json.load(metadata))
    print(f"Built and checked {arguments.output}")
