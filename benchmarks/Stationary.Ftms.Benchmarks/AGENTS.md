# Benchmark Instructions

- Use BenchmarkDotNet and return an observable result to prevent dead-code elimination.
- Allocate payloads and encode destinations in fields or `GlobalSetup`, never in benchmark methods.
- Retain the existing memory, exception, threading, distribution, and rank diagnosers unless measurements justify a change.
- Use the Release command from the repository `AGENTS.md`; BenchmarkDotNet results are written to `BenchmarkDotNet.Artifacts/results/`.
