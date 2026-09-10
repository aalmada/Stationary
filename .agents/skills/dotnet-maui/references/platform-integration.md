# Platform Integration

## Choose A Boundary

| Need | Preferred Approach |
| --- | --- |
| Cross-platform device capability | MAUI Essentials API, wrapped by an app service when it affects business flow |
| Native-only behavior | Shared interface with an implementation in `Platforms/<target>/` |
| Small UI variation | Platform-specific view-layer code or XAML |
| Native control customization | Handler mapper scoped to the control and platform |
| Build/resource variation | Conditional MSBuild in the single project |

- Keep direct Android, iOS, Mac Catalyst, and Windows types out of shared domain and view-model code.
- Prefer partial classes for small target implementations and services for behavior that needs mocking or replacement.
- Use handler customization sparingly. A global mapper changes every instance of that control type; isolate it when behavior is feature-specific.

## Permissions And Privacy

- Declare Android permissions in `Platforms/Android/AndroidManifest.xml`. Add required iOS/Mac Catalyst usage-description keys and entitlements/capabilities before access.
- Request runtime permissions immediately before the feature needs them, check the returned status, and provide a usable denial path.
- Validate all data from platform callbacks, files, URI/deep links, sensors, and share targets.
- Use `SecureStorage` only for small secrets. Never log credentials, access tokens, locations, or personal data.

## Sources

- [Platform integration](https://learn.microsoft.com/dotnet/maui/platform-integration/?view=net-maui-10.0)
- [Handlers](https://learn.microsoft.com/dotnet/maui/user-interface/handlers/?view=net-maui-10.0)
- [Permissions](https://learn.microsoft.com/dotnet/maui/platform-integration/appmodel/permissions?view=net-maui-10.0)
- [Secure storage](https://learn.microsoft.com/dotnet/maui/platform-integration/storage/secure-storage?view=net-maui-10.0)
