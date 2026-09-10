# Sources

## Source Precedence

1. Target framework reference assembly and current .NET runtime source.
2. Current Microsoft Learn conceptual and API documentation.
3. Official .NET samples and tests.
4. Practitioner articles for measured architecture examples, not API authority.

## Reference Links

| Source | Use |
| --- | --- |
| [System.IO.Pipelines](https://learn.microsoft.com/dotnet/standard/io/pipelines) | Primary API contract, lifecycle, parsing patterns, cancellation, and documented failure modes |
| [System.IO.Pipelines API](https://learn.microsoft.com/dotnet/api/system.io.pipelines) | Type signatures, options, exceptions, and target-framework availability |
| [System.Buffers API](https://learn.microsoft.com/dotnet/api/system.buffers) | `ReadOnlySequence`, `SequenceReader`, and memory sequence behavior |
| [Pipelines and Channels performance example](https://dev.to/joni2nja/use-system-io-pipelines-and-system-threading-channels-apis-to-boost-performance-2nj5) | File parsing plus bounded concurrent work example; benchmark is workload-specific and from .NET 5 |
| [Large-file processing article](https://www.linkedin.com/pulse/processando-grandes-arquivos-com-o-systemiopipelines-angelo-belchior/) | Supplied supplementary reading; content requires LinkedIn sign-in and was not independently verified |

- Verify the installed target framework before relying on new overloads or implementation details.
- `System.IO.Pipelines` is part of the shared framework on modern .NET; avoid adding a package reference unless the target framework requires it.
