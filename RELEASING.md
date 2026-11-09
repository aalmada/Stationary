# Website releases

Stationary is beta software. Website beta distribution uses an Apple-silicon Mac Catalyst `.app` inside a read-only DMG. The app is ad-hoc signed: it is not Developer ID signed or Apple-notarized, and requires no Apple Developer Program subscription. Beta releases may use the currently pinned .NET 11 preview toolchain; stable releases retain a separate stable-only gate.

## Current release gate

The repository currently pins prerelease .NET 11/MAUI versions. A normal arm64 Release publish and bundle validation passed with Xcode 27.0, which matches the installed Mac Catalyst workload. Beta packaging is explicitly opt-in and accepts .NET 11 prerelease SDKs/packages while still validating the bundle, embedded full license texts, and exact runtime notices. The default stable packaging path continues to reject prereleases. Do not disable `ValidateXcodeVersion` to conceal an Xcode mismatch.

Check the [macios releases](https://github.com/dotnet/macios/releases) and [Xcode compatibility guidance](https://learn.microsoft.com/dotnet/ios/troubleshooting/xcode-requirement). Update the SDK pin, central MAUI package versions, project `MauiVersion`, and workload selection together. Select the beta channel for prerelease SDKs/packages. The stable channel requires stable .NET 11 and restored packages. Both channels require exact runtime notices.

## Build a DMG

On a Mac with .NET 11 RC3, the pinned MAUI workload, and Xcode 27.0, create a beta DMG with:

```bash
dotnet workload restore src/Stationary.Ftms.Dashboard/Stationary.Ftms.Dashboard.csproj
dotnet restore Stationary.slnx
dotnet test --solution Stationary.CI.slnf --no-restore
dotnet format Stationary.slnx --verify-no-changes --no-restore
bash scripts/publish-macos.sh --beta 0.1.0 1
```

The explicit `--beta` option permits prerelease .NET 11 SDK/runtime/packages; without it the script enforces the stable-release policy. For stable releases, use `bash scripts/publish-macos.sh 1.0.0 1` after installing the stable supported toolchain. The script does not use Developer ID certificates or provisioning profiles. It explicitly publishes the dashboard project using the `Website` profile, collects and embeds dependency notices before signing, and creates a DMG with the app and an Applications link. The bundle validator checks the staged and mounted copies for signatures, arm64 native code, bundle identity/version/build, the generated minimum macOS, Bluetooth entitlement, missing notices, and development entitlements/profiles.

Outputs under `artifacts/macos/<version>/`:

- `Stationary-<version>-macos-arm64.dmg`
- `SHA256SUMS`
- `release-info.txt`, including the generated minimum macOS and toolchain information
- `dependency-licenses.json`, listing restored packages and included full license texts

Use a new version/build for a release. Existing output assets are never overwritten. Retain build symbols separately when diagnosing native crashes.

The packaging script is a validation gate, not proof that the app works on another Mac. It must not be run with a development Xcode-validation bypass or distribute altered signatures.

## Dependency licenses and notices

Inspect the restored dashboard's inventory independently of packaging:

```bash
python3 scripts/collect_notices.py
# Beta packaging policy:
python3 scripts/collect_notices.py --channel beta
# Stable production policy:
python3 scripts/collect_notices.py --channel stable
```

The collector preserves package-provided license/copyright and third-party notice files, includes build-time packages conservatively, and writes the inventory and combined notices under `artifacts/notices/`. An SPDX expression or license URL alone is not a full license text. Missing or empty texts fail collection; a failed rerun removes stale generated notices.

For packages without full license files, `licenses/<lowercase-package-id>-<exact-version>.txt` contains upstream license text. Additional native/runtime notices use `licenses/additional-*.txt`. The current supplements were retrieved from package repository commits, the Silk.NET-pinned MoltenVK submodule and its external dependency revisions, or the installed versioned .NET runtime pack. `licenses/sources.json` records provenance and SHA-256 hashes; retain original upstream texts rather than inventing license terms.

After any dependency/toolchain change, review and update these supplements and their provenance. Production collection requires both `additional-dotnet-runtime-<exact-restored-version>-license.txt` and `additional-dotnet-runtime-<exact-restored-version>-third-party-notices.txt`; old runtime notices do not satisfy a newer runtime. Confirm actual native build provenance and all required attribution notices before distribution. Successful collection is an inventory check, not a legal opinion or proof that every native component has been audited.

Packaging embeds `THIRD-PARTY-NOTICES.txt` in the app before signing and uploads the package inventory with the draft release.

## Draft and verify

The manually dispatched **Prepare Stationary beta release** workflow builds artifacts, verifies checksums, and creates a **draft prerelease** on main. The local script retains stable SDK/package checks when `--beta` is omitted. Configure the `macos-release` environment with appropriate maintainer protections. If the exact Xcode is unavailable on hosted runners, use the same local script and upload its outputs to a draft with `gh release create --draft --prerelease`; do not introduce untrusted self-hosted runners.

For beta 0.1.0, the maintainer confirmed browser-downloaded installation/launch, Bluetooth, session CSV export, and the 3D scene on their Mac. A separate clean Mac, minimum-macOS host, secondary display, and upgrade testing were not confirmed. These are additional compatibility checks, not claims made by the beta release. The maintainer approved public beta distribution with that test scope.

Before publishing:

- Confirm redistribution licenses and required full license/copyright notices for all shipped .NET, MAUI, ReactiveUI, Plugin.BLE, Silk.NET/MoltenVK, and transitive components. Package-provided notices must be retained. The current website is not a claim that this legal audit is complete.
- Approve the app icon and website screenshots; remove identifying ride/device data from public screenshots.
- Verify all public privacy/support/safety claims against the actual distributed behavior.
- Download the DMG in a browser on a clean Apple-silicon Mac so macOS quarantine applies. Do not test only an unquarantined local copy.
- Verify checksums, mount the image, drag the app into Applications, eject, and launch the installed copy.
- Test [Apple's per-app Open Anyway flow](https://support.apple.com/en-us/102445) only for an unidentified/unnotarized app from a trusted source. Do not disable Gatekeeper/SIP, delete quarantine, or override malware/damaged-code warnings.
- Test both the generated minimum and current macOS, without .NET/Xcode or development provisioning installed.
- Exercise Bluetooth permission allowed/denied, Bluetooth off, reconnect, real bike/trainer and heart-rate sensors, supported/rejected/unavailable controls, training/stop/pause, results/history, CSV export/save/cancel/failure, and settings.
- Exercise 3D, secondary-display connection/removal, window close/reopen, sleep/wake, keep-awake release, and quit.
- Install an update over the previous version and verify retained ride data/settings. Back up important data first.

Do not publish when any gate fails. Managed Macs may prohibit per-app exceptions; document this rather than bypassing their restrictions.

## Website

Website source is in `site/`, separate from the existing OKF bundle in `docs/`. Build and validate it without frontend packages:

```bash
python3 -m unittest discover -s scripts -p 'test_*.py'
python3 scripts/build_site.py
python3 scripts/check_release_links.py
```

Preview the generated `artifacts/site/` directory locally. The site identifies Stationary as beta and works without JavaScript. Before a verified beta release it deliberately shows no binary download link.

To activate downloads, publish the tested GitHub Release with tag `v<version>` and the exact asset names above. Update both values in `site/release.json` using the **verified** display version and generated minimum macOS. Do not invent the minimum from the Catalyst target number.

Enable repository GitHub Pages using **GitHub Actions**, with HTTPS and the `github-pages` environment. The workflow deploys only the generated site and checks published asset URLs, checksum shape, and agreement between website and packaged version/minimum macOS when release metadata is set. The site builder rejects unexpected output files rather than publishing incidental artifacts. The planned URL is <https://aalmada.github.io/Stationary/>; it is not live merely because the workflow exists.

After deployment, test the live download/checksum/support/privacy links and install the browser-downloaded application again. Keep previous release assets; rollback by linking the previous verified version.

## Updates and privacy

Updates are manual app replacement, with stable bundle identity. No automatic updater, extra background service, accounts, paid features, or sample mode is introduced.

App data uses MAUI application storage and preferences. Removing the app may leave data behind; do not promise uninstall deletes recorded rides or cloud exports. Review dependency behavior and website-host logging separately. Public GitHub issues must not include health data, device identifiers, secrets, or unredacted logs.
