# Deployment And Publishing

## Release Workflow

1. Build a Release configuration for each target framework and inspect warnings, especially trimming warnings.
2. Set unique application identifiers, version name/display version, build version, icons, splash screens, and privacy/store metadata.
3. Configure signing and provisioning through secure CI secrets or platform credential stores; never commit certificates, profiles, or passwords.
4. Install the signed package on physical devices and test upgrades, permissions, deep links, offline use, lifecycle transitions, and failure recovery.
5. Submit the platform package only after it passes the appropriate store validation.

## Platform Outputs

| Target | Distribution Artifact | Release Considerations |
| --- | --- | --- |
| Android | AAB for stores; APK for direct installation | Signing key, manifest permissions, Play requirements |
| iOS | IPA | macOS build host, signing certificate, provisioning profile, App Store requirements |
| Mac Catalyst | APP or PKG | Signing, provisioning/capabilities, sandbox requirements |
| Windows | MSIX or unpackaged app | Package identity/signing for MSIX, Windows deployment requirements |

- Match the installed SDK/workload to CI and pin it as part of reproducible builds.
- Run final checks against the actual generated artifact, not a Debug deployment or a package assembled locally with different credentials.
- Maintain versioning rules that support store requirements and in-place upgrades.

## Sources

- [Deployment and testing](https://learn.microsoft.com/dotnet/maui/deployment/?view=net-maui-10.0)
- [Publish for Android](https://learn.microsoft.com/dotnet/maui/android/deployment/?view=net-maui-10.0)
- [Publish for iOS](https://learn.microsoft.com/dotnet/maui/ios/deployment/?view=net-maui-10.0)
- [Publish for Windows](https://learn.microsoft.com/dotnet/maui/windows/deployment/overview?view=net-maui-10.0)
