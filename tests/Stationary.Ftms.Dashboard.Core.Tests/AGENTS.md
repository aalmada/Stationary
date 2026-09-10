# Dashboard Core Test Instructions

- Run with `dotnet test tests/Stationary.Ftms.Dashboard.Core.Tests/Stationary.Ftms.Dashboard.Core.Tests.csproj --no-restore`.
- Use TUnit attributes and assertions through Microsoft.Testing.Platform.
- Test latest-only behavior under a full buffer, reader completion, and producer completion errors.
- Test telemetry-history boundary behavior, including samples exactly at the retention cutoff and invalid windows.
- Keep tests deterministic; use explicit timestamps and do not depend on wall-clock timing.
