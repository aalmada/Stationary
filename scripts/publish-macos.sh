#!/bin/bash
set -Eeuo pipefail
trap 'printf "Packaging failed at line %s. Do not publish this build.\n" "$LINENO" >&2' ERR

usage() {
    printf '%s\n' 'Usage: bash scripts/publish-macos.sh [--beta] VERSION BUILD [OUTPUT_DIRECTORY]' \
        'Creates an ad-hoc-signed Apple-silicon DMG and SHA256SUMS without Apple membership.' \
        'Stable toolchains are required unless the explicit --beta channel is selected.' \
        'Set DEVELOPER_DIR to a workload-compatible Xcode installation if needed.'
}

if [[ "${1:-}" == "--help" ]]; then usage; exit 0; fi
channel="stable"
if [[ "${1:-}" == "--beta" ]]; then
    channel="beta"
    shift
fi
if [[ $# -lt 2 || $# -gt 3 ]]; then usage >&2; exit 2; fi
version="$1"
build="$2"
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ || ! "$build" =~ ^[1-9][0-9]*$ ]]; then
    printf '%s\n' 'VERSION must be major.minor.patch and BUILD a positive integer.' >&2
    exit 2
fi
if [[ "$(uname -s)" != Darwin ]]; then
    printf '%s\n' 'Packaging requires macOS and Xcode.' >&2
    exit 1
fi
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
sdk="$(dotnet --version)"
if [[ ! "$sdk" =~ ^11\.[0-9]+\.[0-9]+(-[[:alnum:].-]+)?$ ]] \
    || { [[ "$channel" == stable ]] && [[ "$sdk" == *-* ]]; }; then
    printf 'The %s channel does not accept selected .NET SDK %s.\n' "$channel" "$sdk" >&2
    exit 1
fi
for tool in python3 codesign hdiutil ditto shasum lipo file xcodebuild; do
    command -v "$tool" >/dev/null || { printf 'Required tool not found: %s\n' "$tool" >&2; exit 1; }
done
output="${3:-$root/artifacts/macos/$version}"
mkdir -p "$output"
output="$(cd "$output" && pwd)"
name="Stationary-$version-macos-arm64.dmg"
if [[ -n "$(find "$output" -mindepth 1 -maxdepth 1 -print -quit)" ]]; then
    printf '%s\n' 'Release output already exists. Use a new empty output directory; existing releases are never overwritten.' >&2
    exit 1
fi
work="$(mktemp -d "${TMPDIR:-/tmp}/stationary-release.XXXXXX")"
mount="$work/mounted"
mounted=false
cleanup() {
    if [[ "$mounted" == true ]]; then
        if hdiutil detach "$mount" >&2; then
            mounted=false
        else
            printf 'Could not unmount %s; preserving staging files at %s.\n' "$mount" "$work" >&2
            return 1
        fi
    fi
    if [[ "$mounted" == false ]]; then
        rm -r -- "$work"
    fi
}
trap cleanup EXIT
project="$root/src/Stationary.Ftms.Dashboard/Stationary.Ftms.Dashboard.csproj"
cd "$root"
dotnet restore "$project" -r maccatalyst-arm64
python3 "$root/scripts/collect_notices.py" --channel "$channel" --output "$work/notices"
dotnet publish "$project" -f net11.0-maccatalyst -c Release -r maccatalyst-arm64 \
    -p:PublishProfile=Website -p:ApplicationDisplayVersion="$version" -p:ApplicationVersion="$build" \
    -p:WebsiteNoticesPath="$work/notices/THIRD-PARTY-NOTICES.txt"

app_root="$root/src/Stationary.Ftms.Dashboard/bin/Release/net11.0-maccatalyst/maccatalyst-arm64"
app="$(python3 - "$app_root" "$version" "$build" <<'PY'
import plistlib
import sys
from pathlib import Path

root, version, build = Path(sys.argv[1]), sys.argv[2], sys.argv[3]
matches = []
for app in root.glob("*.app"):
    with (app / "Contents" / "Info.plist").open("rb") as stream:
        info = plistlib.load(stream)
    if (
        info.get("CFBundleIdentifier") == "com.stationary.ftms.dashboard"
        and info.get("CFBundleShortVersionString") == version
        and info.get("CFBundleVersion") == build
    ):
        matches.append(app)
if len(matches) != 1:
    raise SystemExit(f"Expected one app matching bundle ID/version/build; found {len(matches)}.")
print(matches[0])
PY
)"
mkdir "$work/image"
ditto "$app" "$work/image/Stationary.app"
app="$work/image/Stationary.app"
minimum="$(python3 "$root/scripts/validate_macos_bundle.py" "$app" "$version" "$build")"
python3 -m venv "$work/packaging-env"
"$work/packaging-env/bin/python" -m pip install --disable-pip-version-check \
    -r "$root/scripts/requirements-dmg.txt"
"$work/packaging-env/bin/dmgbuild" -s "$root/scripts/dmg_settings.py" \
    -D "app=$app" -D "background=$root/scripts/dmg-background.png" \
    "Stationary $version" "$work/$name"
hdiutil verify "$work/$name"
mkdir "$mount"
hdiutil attach -readonly -nobrowse -mountpoint "$mount" "$work/$name"
mounted=true
[[ -d "$mount/Stationary.app" && "$(readlink "$mount/Applications")" == /Applications ]]
[[ -f "$mount/.DS_Store" && -f "$mount/.background.png" ]]
"$work/packaging-env/bin/python" "$root/scripts/validate_dmg_layout.py" "$mount"
mounted_minimum="$(python3 "$root/scripts/validate_macos_bundle.py" "$mount/Stationary.app" "$version" "$build")"
[[ "$mounted_minimum" == "$minimum" ]]
hdiutil detach "$mount"
mounted=false
rmdir "$mount"

printf 'Channel: %s\nVersion: %s\nBuild: %s\nMinimum macOS: %s\n' "$channel" "$version" "$build" "$minimum" > "$work/release-info.txt"
printf 'SDK: %s\n' "$sdk" >> "$work/release-info.txt"
xcodebuild -version >> "$work/release-info.txt"
ditto "$work/$name" "$output/$name"
ditto "$work/release-info.txt" "$output/release-info.txt"
ditto "$work/notices/dependency-licenses.json" "$output/dependency-licenses.json"
(cd "$output" && shasum -a 256 "$name" > SHA256SUMS)
printf 'Created %s\nRead release-info.txt and test the browser-downloaded DMG before publishing.\n' "$output/$name"
