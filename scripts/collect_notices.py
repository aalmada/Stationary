#!/usr/bin/env python3
"""Collect restored package notices; fail when full license texts are missing."""

import argparse
import json
import re
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent


def validate_release_assets(assets: dict, supplements: Path) -> None:
    prerelease = [
        identity for identity, library in assets["libraries"].items()
        if library["type"] == "package" and "-" in identity.rsplit("/", 1)[-1]
    ]
    if prerelease:
        raise ValueError("Public releases require stable packages: " + ", ".join(prerelease))
    runtimes = [
        dependency
        for framework in assets["project"]["frameworks"].values()
        for dependency in framework.get("downloadDependencies", [])
        if dependency["name"].startswith("Microsoft.NETCore.App.Runtime.")
        and dependency["name"].endswith("maccatalyst-arm64")
    ]
    if len(runtimes) != 1:
        raise ValueError("Expected one exact restored arm64 Mac Catalyst runtime")
    match = re.fullmatch(r"\[(11\.[0-9]+\.[0-9]+),\s*\1\]", runtimes[0]["version"])
    if match is None:
        raise ValueError("Public releases require an exact stable .NET 11 runtime")
    for name in ("license", "third-party-notices"):
        file = supplements / f"additional-dotnet-runtime-{match[1]}-{name}.txt"
        if not file.is_file() or file.is_symlink() or not file.read_text().strip():
            raise ValueError(f"Reviewed notices for the restored runtime are missing: {file}")


def collect(assets_path: Path, output: Path, supplements: Path, release: bool = False) -> list[str]:
    output.mkdir(parents=True, exist_ok=True)
    for name in ("THIRD-PARTY-NOTICES.txt", "dependency-licenses.json"):
        (output / name).unlink(missing_ok=True)
    assets = json.loads(assets_path.read_text())
    if release:
        validate_release_assets(assets, supplements)
    folders = [Path(folder).resolve() for folder in assets["packageFolders"]]
    report = []
    sections = []
    missing = []
    for identity, library in sorted(assets["libraries"].items()):
        if library["type"] != "package":
            continue
        candidates = [(folder / library["path"]).resolve() for folder in folders]
        package = next(
            (path for folder, path in zip(folders, candidates) if path.is_relative_to(folder) and path.is_dir()),
            None,
        )
        if package is None:
            raise FileNotFoundError(f"Restore the package before collecting notices: {identity}")
        specs = list(package.glob("*.nuspec"))
        if len(specs) != 1:
            raise ValueError(f"Expected one nuspec for {identity}")
        metadata = next(node for node in ET.parse(specs[0]).getroot() if node.tag.endswith("metadata"))
        fields = {node.tag.split("}")[-1]: node.text for node in metadata}
        texts = []
        has_license = False
        for file in sorted(package.rglob("*")):
            if not file.is_file() or file.is_symlink():
                continue
            name = file.name.lower()
            if not (name.startswith(("license", "copying")) or "third-party-notice" in name or name == "notice"):
                continue
            if file.suffix.lower() not in ("", ".txt", ".md"):
                continue
            text = file.read_text(encoding="utf-8-sig")
            if not text.strip():
                raise ValueError(f"Empty license or notice: {file}")
            texts.append(f"--- {file.relative_to(package)} ---\n{text}")
            has_license |= name.startswith(("license", "copying"))
        supplement_name = identity.replace("/", "-").lower() + ".txt"
        supplement = supplements / supplement_name
        if supplement.is_symlink():
            raise ValueError(f"License supplements must not be symlinks: {supplement}")
        if supplement.is_file():
            text = supplement.read_text()
            if not text.strip():
                raise ValueError(f"Empty license supplement: {supplement}")
            texts.append(f"--- Maintainer-supplied license/notice: {supplement_name} ---\n{text}")
            has_license = True
        if not has_license:
            missing.append(identity)
        report.append({
            "package": identity,
            "license": fields.get("license"),
            "licenseUrl": fields.get("licenseUrl"),
            "projectUrl": fields.get("projectUrl"),
            "fullLicenseIncluded": has_license,
        })
        sections.append(
            f"=== {identity} ===\nLicense metadata: {fields.get('license') or 'Not specified'}\n"
            f"Copyright metadata: {fields.get('copyright') or 'Not specified'}\n\n" + "\n\n".join(texts)
        )
    for file in sorted(supplements.glob("additional-*.txt")):
        if file.is_symlink():
            raise ValueError(f"License supplements must not be symlinks: {file}")
        text = file.read_text()
        if not text.strip():
            raise ValueError(f"Empty additional notice: {file}")
        sections.append(f"=== Additional redistributed component: {file.name} ===\n{text}")
    if not report:
        raise ValueError("No restored packages found; check the dashboard assets file")
    (output / "dependency-licenses.json").write_text(json.dumps(report, indent=2) + "\n")
    if missing:
        raise ValueError(
            "Full license texts are missing for: " + ", ".join(missing)
            + ". Supply reviewed package-version.txt files in licenses/; see RELEASING.md."
        )
    (output / "THIRD-PARTY-NOTICES.txt").write_text(
        "Restored dependency licenses and notices. Includes build-time packages conservatively.\n\n"
        + "\n\n".join(sections) + "\n"
    )
    return [entry["package"] for entry in report]


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assets", type=Path, default=ROOT / "src/Stationary.Ftms.Dashboard/obj/project.assets.json")
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts/notices")
    parser.add_argument("--supplements", type=Path, default=ROOT / "licenses")
    parser.add_argument("--release", action="store_true", help="Require stable packages and exact runtime notices")
    args = parser.parse_args()
    packages = collect(args.assets, args.output, args.supplements, args.release)
    print(f"Collected notices for {len(packages)} packages in {args.output}")
