#!/bin/bash
set -Eeuo pipefail
trap 'printf "Packaging failed at line %s. Do not publish this build.\n" "$LINENO" >&2' ERR

usage() {
    printf '%s\n' 'Usage: bash scripts/publish-macos.sh VERSION BUILD [OUTPUT_DIRECTORY]' \
        'Creates an ad-hoc-signed Apple-silicon DMG and SHA256SUMS without Apple membership.' \
        'Set DEVELOPER_DIR to a workload-compatible Xcode installation if needed.'
}

if [[ "${1:-}" == "--help" ]]; then usage; exit 0; fi
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
output="${3:-$root/artifacts/macos/$version}"
mkdir -p "$output"
output="$(cd "$output" && pwd)"
name="Stationary-$version-macos-arm64.dmg"
if [[ -e "$output/$name" || -e "$output/SHA256SUMS" ]]; then
    printf '%s\n' 'Release output already exists. Use a new empty output directory; existing releases are never overwritten.' >&2
    exit 1
fi
work="$(mktemp -d "${TMPDIR:-/tmp}/stationary-release.XXXXXX")"
mount="$work/mounted"
mounted=false
cleanup() {
    if [[ "$mounted" == true ]]; then
        hdiutil detach "$mount" >&2
    fi
    rm -r -- "$work"
}
trap cleanup EXIT
project="$root/src/Stationary.Ftms.Dashboard/Stationary.Ftms.Dashboard.csproj"
cd "$root"
dotnet publish "$project" -f net11.0-maccatalyst -c Release -r maccatalyst-arm64 \
    -p:PublishProfile=Website -p:ApplicationDisplayVersion="$version" -p:ApplicationVersion="$build"

app_root="$root/src/Stationary.Ftms.Dashboard/bin/Release/net11.0-maccatalyst/maccatalyst-arm64"
apps=()
while IFS= read -r -d '' app; do apps+=("$app"); done < <(find "$app_root" -maxdepth 1 -type d -name '*.app' -print0)
if [[ ${#apps[@]} -ne 1 ]]; then
    printf '%s\n' 'Expected exactly one published .app.' >&2
    exit 1
fi
mkdir "$work/image"
ditto "${apps[0]}" "$work/image/Stationary.app"
app="$work/image/Stationary.app"
plist="$app/Contents/Info.plist"
read_plist() { /usr/libexec/PlistBuddy -c "Print :$1" "$plist"; }
[[ "$(read_plist CFBundleIdentifier)" == com.stationary.ftms.dashboard ]]
[[ "$(read_plist CFBundleShortVersionString)" == "$version" ]]
[[ "$(read_plist CFBundleVersion)" == "$build" ]]
executable="$(read_plist CFBundleExecutable)"
[[ "$(lipo -archs "$app/Contents/MacOS/$executable")" == arm64 ]]
codesign --verify --deep --strict --verbose=2 "$app"
codesign --display --entitlements :- "$app" > "$work/entitlements.plist"
plutil -lint "$work/entitlements.plist"
if /usr/libexec/PlistBuddy -c 'Print :get-task-allow' "$work/entitlements.plist" 2>/dev/null | /usr/bin/awk '$0 == "true" { found=1 } END { exit !found }'; then
    printf '%s\n' 'Release app contains the debugging entitlement get-task-allow.' >&2
    exit 1
fi
if [[ -f "$app/Contents/embedded.provisionprofile" ]]; then
    printf '%s\n' 'Website releases must not depend on a provisioning profile.' >&2
    exit 1
fi
while IFS= read -r -d '' binary; do
    case "$(file -b "$binary")" in
        *Mach-O*)
            [[ "$(lipo -archs "$binary")" == arm64 ]]
            codesign --verify --strict --verbose=2 "$binary"
            ;;
    esac
done < <(find "$app" -type f -print0)
ln -s /Applications "$work/image/Applications"
hdiutil create -srcfolder "$work/image" -volname "Stationary $version" \
    -format UDZO "$work/$name"
hdiutil verify "$work/$name"
mkdir "$mount"
hdiutil attach -readonly -nobrowse -mountpoint "$mount" "$work/$name"
mounted=true
[[ -d "$mount/Stationary.app" && "$(readlink "$mount/Applications")" == /Applications ]]
codesign --verify --deep --strict --verbose=2 "$mount/Stationary.app"
hdiutil detach "$mount"
mounted=false
rmdir "$mount"

minimum="$(read_plist LSMinimumSystemVersion)"
printf 'Version: %s\nBuild: %s\nMinimum macOS: %s\n' "$version" "$build" "$minimum" > "$work/release-info.txt"
dotnet --info >> "$work/release-info.txt"
xcodebuild -version >> "$work/release-info.txt"
ditto "$work/$name" "$output/$name"
ditto "$work/release-info.txt" "$output/release-info.txt"
(cd "$output" && shasum -a 256 "$name" > SHA256SUMS)
printf 'Created %s\nRead release-info.txt and test the browser-downloaded DMG before publishing.\n' "$output/$name"
