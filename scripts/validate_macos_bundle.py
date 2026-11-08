#!/usr/bin/env python3
"""Validate the identity, signatures and entitlements of a website release app."""

import argparse
import plistlib
import re
import subprocess
from pathlib import Path


def run(*arguments: str) -> str:
    return subprocess.run(
        arguments, check=True, capture_output=True, text=True
    ).stdout.strip()


def validate_metadata(info: dict, entitlements: dict, version: str, build: str) -> str:
    expected = {
        "CFBundleIdentifier": "com.stationary.ftms.dashboard",
        "CFBundleShortVersionString": version,
        "CFBundleVersion": build,
    }
    for key, value in expected.items():
        if info.get(key) != value:
            raise ValueError(f"{key}: expected {value!r}, got {info.get(key)!r}")
    executable = info.get("CFBundleExecutable")
    if not isinstance(executable, str) or Path(executable).name != executable or executable in ("", ".", ".."):
        raise ValueError("CFBundleExecutable must be a single filename.")
    minimum = info.get("LSMinimumSystemVersion")
    if not isinstance(minimum, str) or not re.fullmatch(r"[0-9]+\.[0-9]+(?:\.[0-9]+)?", minimum):
        raise ValueError("The generated app must report its minimum macOS version.")
    for key in ("get-task-allow", "com.apple.security.get-task-allow"):
        if entitlements.get(key) not in (None, False):
            raise ValueError(f"Public releases must not enable {key}.")
    if entitlements.get("com.apple.security.device.bluetooth") is not True:
        raise ValueError("The app must retain its Bluetooth entitlement.")
    return minimum


def validate(app: Path, version: str, build: str) -> str:
    app = app.resolve(strict=True)
    contents = app / "Contents"
    with (contents / "Info.plist").open("rb") as stream:
        info = plistlib.load(stream)
    if any(app.rglob("embedded.provisionprofile")) or any(app.rglob("embedded.mobileprovision")):
        raise ValueError("Website apps must not contain provisioning profiles.")
    run("codesign", "--verify", "--deep", "--strict", "--verbose=2", str(app))
    signature = subprocess.run(
        ["codesign", "--display", "--verbose=2", str(app)],
        check=True, capture_output=True, text=True,
    )
    if "Signature=adhoc" not in signature.stderr.splitlines():
        raise ValueError("Expected an ad-hoc signature, not a paid/development signing identity.")
    raw_entitlements = subprocess.run(
        ["codesign", "--display", "--entitlements", ":-", str(app)],
        check=True, capture_output=True,
    ).stdout
    entitlements = plistlib.loads(raw_entitlements)
    minimum = validate_metadata(info, entitlements, version, build)
    executable = contents / "MacOS" / info["CFBundleExecutable"]
    if not executable.is_file():
        raise ValueError("The generated executable is missing.")
    if run("lipo", "-archs", str(executable)) != "arm64":
        raise ValueError("The release executable must be Apple silicon only.")
    for binary in app.rglob("*"):
        if binary.is_file() and not binary.is_symlink() and "Mach-O" in run("file", "-b", str(binary)):
            if run("lipo", "-archs", str(binary)) != "arm64":
                raise ValueError(f"Unexpected native architecture: {binary.relative_to(app)}")
            run("codesign", "--verify", "--strict", "--verbose=2", str(binary))
    return minimum


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("app", type=Path)
    parser.add_argument("version")
    parser.add_argument("build")
    args = parser.parse_args()
    print(validate(args.app, args.version, args.build))
