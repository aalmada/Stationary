# Website releases

Stationary's website distribution uses an Apple-silicon Mac Catalyst `.app` inside a read-only DMG. The app is ad-hoc signed: it is not Developer ID signed or Apple-notarized, and requires no Apple Developer Program subscription.

## Current release gate

The repository currently pins prerelease .NET 11/MAUI versions. The installed Mac Catalyst workload requires Xcode 27.0; the current development host has Xcode 27.2. A supported production SDK/workload/Xcode combination must be selected before a public release. Do not downgrade .NET or disable `ValidateXcodeVersion` to conceal the mismatch.

Check the [macios releases](https://github.com/dotnet/macios/releases) and [Xcode compatibility guidance](https://learn.microsoft.com/dotnet/ios/troubleshooting/xcode-requirement). Update the SDK pin, central MAUI package versions, project `MauiVersion`, and workload selection together. Release CI rejects prerelease SDKs.

## Build a DMG

On a Mac with the supported .NET 11 workload and matching Xcode:

```bash
# Only if selecting an installed Xcode alongside the system default:
export DEVELOPER_DIR=/Applications/Xcode_27.0.app/Contents/Developer

dotnet workload restore src/Stationary.Ftms.Dashboard/Stationary.Ftms.Dashboard.csproj
dotnet restore Stationary.slnx
dotnet test --solution Stationary.slnx --no-restore
dotnet format Stationary.slnx --verify-no-changes --no-restore
bash scripts/publish-macos.sh 1.0.0 1
```

Select the Xcode version required by the chosen workload, not necessarily the example above. The script does not use Developer ID certificates or provisioning profiles. It explicitly publishes the dashboard project using the `Website` profile, verifies signatures/arm64 architecture/bundle versions, and creates a DMG with the app and an Applications link.

Outputs under `artifacts/macos/<version>/`:

- `Stationary-<version>-macos-arm64.dmg`
- `SHA256SUMS`
- `release-info.txt`, including the generated minimum macOS and toolchain information

Use a new version/build for a release. Existing output assets are never overwritten. Retain build symbols separately when diagnosing native crashes.

The packaging script is a validation gate, not proof that the app works on another Mac. It must not be run with a development Xcode-validation bypass or distribute altered signatures.

## Draft and verify

The manually dispatched **Prepare macOS website release** workflow builds artifacts, verifies checksums, and creates a **draft** GitHub Release on main. Configure the `macos-release` environment with appropriate maintainer protections. If the exact Xcode is unavailable on hosted runners, use the same local script and upload its outputs to a draft with `gh release create --draft`; do not introduce untrusted self-hosted runners.

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
```

Preview the generated `artifacts/site/` directory locally. The site works without JavaScript. Before a verified release it deliberately shows no binary download link.

To activate downloads, publish the tested GitHub Release with tag `v<version>` and the exact asset names above. Update both values in `site/release.json` using the **verified** display version and generated minimum macOS. Do not invent the minimum from the Catalyst target number.

Enable repository GitHub Pages using **GitHub Actions**, with HTTPS and the `github-pages` environment. The workflow deploys only the generated site and checks published asset URLs when release metadata is set. The planned URL is <https://aalmada.github.io/Stationary/>; it is not live merely because the workflow exists.

After deployment, test the live download/checksum/support/privacy links and install the browser-downloaded application again. Keep previous release assets; rollback by linking the previous verified version.

## Updates and privacy

Updates are manual app replacement, with stable bundle identity. No automatic updater, extra background service, accounts, paid features, or sample mode is introduced.

App data uses MAUI application storage and preferences. Removing the app may leave data behind; do not promise uninstall deletes recorded rides or cloud exports. Review dependency behavior and website-host logging separately. Public GitHub issues must not include health data, device identifiers, secrets, or unredacted logs.
