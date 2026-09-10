# Test Instructions

- Use TUnit attributes and assertions; the test project runs through Microsoft.Testing.Platform.
- Run the focused test project with `dotnet test tests/Stationary.Ftms.Tests/Stationary.Ftms.Tests.csproj --no-restore`.
- Test valid strict and compatible payloads, truncation, reserved flags, trailing data, unavailable sentinels, and encode/decode round trips as applicable.
- In async tests, do not retain `Span<T>` or `ReadOnlySpan<T>` across an `await`; use arrays or copy scalar values first.
