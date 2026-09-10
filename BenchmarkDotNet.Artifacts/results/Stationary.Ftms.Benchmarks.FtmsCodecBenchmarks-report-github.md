<!-- markdownlint-disable -->

```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.6.2 (25G83) [Darwin 25.6.0]
Apple M1, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.100
  [Host]     : .NET 10.0.0 (10.0.0, 10.0.25.52411), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 10.0.0 (10.0.0, 10.0.25.52411), Arm64 RyuJIT armv8.0-a


```
| Method                    | Mean      | Error     | StdDev    | Min       | Max       | Median    | Rank | Exceptions | Completed Work Items | Lock Contentions | Allocated |
|-------------------------- |----------:|----------:|----------:|----------:|----------:|----------:|-----:|-----------:|---------------------:|-----------------:|----------:|
| ValidateFullTreadmillData |  6.319 ns | 0.1536 ns | 0.2848 ns |  6.132 ns |  7.255 ns |  6.219 ns |    1 |          - |                    - |                - |         - |
| DecodeTreadmillData       | 27.002 ns | 0.2585 ns | 0.2418 ns | 26.619 ns | 27.274 ns | 27.036 ns |    2 |          - |                    - |                - |         - |
| EncodeTreadmillData       | 40.445 ns | 0.0795 ns | 0.0620 ns | 40.370 ns | 40.609 ns | 40.428 ns |    3 |          - |                    - |                - |         - |
