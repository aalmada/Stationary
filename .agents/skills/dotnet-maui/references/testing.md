# Testing

## Test Layers

| Layer | Scope | Execution Environment |
| --- | --- | --- |
| Unit | View models, services, validation, mapping, cancellation, errors | .NET test runner without MAUI UI objects |
| Integration | Storage, HTTP clients, serialization, platform abstraction contracts | Controlled test host, emulator, or device as needed |
| UI | Critical user workflows, navigation, forms, accessibility labels | Appium on target-platform emulator/simulator/device |
| Release validation | Signing, permissions, lifecycle, performance, installation | Signed Release package on physical devices |

- Unit-test presentation logic through injected interfaces, fake clocks, deterministic schedulers, and cancellation tokens. Do not construct pages to test view-model behavior.
- Make UI automation selectors stable with semantic identifiers or automation IDs; do not use visible text that changes with localization as the only selector.
- Cover initial launch, offline/error paths, interrupted permission prompts, resume/background transitions, navigation reset, and accessibility-critical interactions.
- Test physical devices in addition to emulators/simulators for permissions, sensors, notifications, memory behavior, network changes, performance, and signing.

## Sources

- [Unit testing](https://learn.microsoft.com/dotnet/maui/deployment/unit-testing?view=net-maui-10.0)
- [UI testing with Appium](https://learn.microsoft.com/dotnet/maui/deployment/ui-testing?view=net-maui-10.0)
- [Deployment and testing](https://learn.microsoft.com/dotnet/maui/deployment/?view=net-maui-10.0)
