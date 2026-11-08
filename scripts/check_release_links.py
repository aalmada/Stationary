#!/usr/bin/env python3
"""Check public release assets and their agreement with website metadata."""

import json
from pathlib import Path
import re
from urllib.request import Request, urlopen

from build_site import ROOT, download_markup


def validate_documents(release: dict, checksums: str, info: str) -> None:
    download_markup(release)
    version = release["version"]
    expected_name = f"Stationary-{version}-macos-arm64.dmg"
    if not re.fullmatch(rf"[0-9a-f]{{64}}  {re.escape(expected_name)}\n?", checksums):
        raise ValueError("SHA256SUMS must contain exactly the versioned DMG checksum.")
    fields = {}
    for line in info.splitlines()[:3]:
        key, separator, value = line.partition(": ")
        if not separator or key in fields:
            raise ValueError("Invalid release-info.txt metadata.")
        fields[key] = value
    if fields.get("Version") != version or fields.get("Minimum macOS") != release["minimumMacOS"]:
        raise ValueError("Website version/minimum macOS disagrees with the packaged artifact.")
    if not re.fullmatch(r"[1-9][0-9]*", fields.get("Build", "")):
        raise ValueError("release-info.txt must identify a positive build number.")


def read_document(url: str) -> str:
    with urlopen(url, timeout=30) as response:
        content = response.read(65537)
    if len(content) > 65536:
        raise ValueError(f"Release document exceeds 64 KiB: {url}")
    return content.decode("utf-8")


def check(release: dict) -> None:
    download_markup(release)
    version = release["version"]
    if version is None:
        print("No published binary configured; website will show the source-build option.")
        return
    base = f"https://github.com/aalmada/Stationary/releases/download/v{version}"
    name = f"Stationary-{version}-macos-arm64.dmg"
    with urlopen(Request(f"{base}/{name}", method="HEAD"), timeout=30) as response:
        if response.status != 200:
            raise ValueError(f"DMG download is unavailable: {name}")
    validate_documents(
        release, read_document(f"{base}/SHA256SUMS"), read_document(f"{base}/release-info.txt")
    )
    print(f"Verified public release links and metadata for {version}.")


if __name__ == "__main__":
    check(json.loads((ROOT / "site" / "release.json").read_text()))
