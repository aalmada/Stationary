# FTMS Client Instructions

- Build with `dotnet build src/Stationary.Ftms.Client/Stationary.Ftms.Client.csproj --no-restore`.
- Keep this project transport-adjacent but platform-neutral; depend only on the codec and dashboard-core projects.
- Serialize Control Point writes, match each response indication to its pending opcode, and complete every request on success, timeout, cancellation, disconnect, or disposal.
- Preserve latest-only telemetry behavior: decode notification payloads into merged snapshots and publish through `LatestTelemetryBuffer` without blocking the BLE callback.
- Keep notification processing allocation-conscious. Decode payloads directly from `ReadOnlySpan<byte>` and do not add UI or platform dependencies.
