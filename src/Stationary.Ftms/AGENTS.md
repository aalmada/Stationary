# Library Instructions

- Keep public codec APIs transport-neutral and span-oriented.
- Decode directly from `ReadOnlySpan<byte>` and encode into caller-provided `Span<byte>` buffers; do not allocate, pool, reflect, or use LINQ on codec hot paths.
- Use `BinaryPrimitives` for little-endian multi-byte values and preserve FTMS unavailable sentinels.
- Keep strict validation for RFU flags and trailing payload bytes; compatible mode must retain its existing permissive behavior.
- Add a focused TUnit test and a BenchmarkDotNet case when changing a codec's wire layout or hot path.
