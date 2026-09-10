# FTMS Client Test Instructions

- Run with `dotnet test tests/Stationary.Ftms.Client.Tests/Stationary.Ftms.Client.Tests.csproj --no-restore`.
- Use TUnit attributes and assertions through Microsoft.Testing.Platform.
- Cover notification decode and snapshot merging for each supported machine data type.
- When changing Control Point coordination, test request serialization, matching response indications, invalid or unmatched indications, timeout, cancellation, and disposal.
- Keep tests transport-free by using deterministic test doubles for `IFtmsControlPointTransport`.
