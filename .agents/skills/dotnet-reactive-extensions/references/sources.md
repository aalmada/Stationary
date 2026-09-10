# Research Sources

Research snapshot: 2026-09-07. Rx.NET 7.0.0 was the current stable release; repository `main` was preparing 7.1. Recheck versions and exact APIs when applying this skill later.

## Primary Sources

| Source | Authority | Use |
| --- | --- | --- |
| [dotnet/reactive repository](https://github.com/dotnet/reactive) | Rx.NET source and maintainer documentation | Current APIs, implementation contracts, roadmap, package family |
| [Rx.NET v7 release history](https://github.com/dotnet/reactive/blob/main/Rx.NET/Documentation/ReleaseHistory/Rx.v7.md) | Maintainer release notes | Supported frameworks, v7 package split, breaking changes, retired facades |
| [Rx.NET v7.0.0 release](https://github.com/dotnet/reactive/releases/tag/rxnet-v7.0.0) | Stable release record | Shipped fixes and enhancements |
| [Introduction to Rx.NET](https://introtorx.com/) | Maintainer-authored book, also stored in the repository | Contracts, operators, scheduling, disposal, publishing, testing, usage guidance |
| [IntroToRx repository sources](https://github.com/dotnet/reactive/tree/main/Rx.NET/Documentation/IntroToRx) | Versioned source for the book | Exact chapter text and examples matched to repository changes |
| [System.Reactive on NuGet](https://www.nuget.org/packages/System.Reactive) | Published package metadata | Current stable version, target assets, dependencies, install command |
| [Microsoft.Reactive.Testing on NuGet](https://www.nuget.org/packages/Microsoft.Reactive.Testing) | Published test package metadata | Version/target compatibility and unsupported-API warning |
| [ReactiveX documentation](https://reactivex.io/) | Cross-language conceptual reference | Observable model, marble diagrams, operator taxonomy, generic terminology |
| [Microsoft observer pattern guidance](https://learn.microsoft.com/dotnet/standard/events/observer-design-pattern-best-practices) | Official .NET interface guidance | Choosing events, async streams, channels, or Rx; provider/observer safety |
| [IObservable API](https://learn.microsoft.com/dotnet/api/system.iobservable-1) | Official .NET API reference | BCL interface definition and variance |
| [IObserver API](https://learn.microsoft.com/dotnet/api/system.iobserver-1) | Official .NET API reference | BCL callback contract |

## Topic Map

| Topic | Source chapter/page |
| --- | --- |
| Core grammar, hot/cold behavior, disposal | [Key types](https://introtorx.com/chapters/key-types.html) |
| Factories, events, tasks, subjects | [Creating observable sequences](https://introtorx.com/chapters/creating-observable-sequences.html) |
| Selection and flattening | [Transformation](https://introtorx.com/chapters/transformation-of-sequences.html) |
| Filtering and blocking alternatives | [Filtering](https://introtorx.com/chapters/filtering.html) |
| Buffers, windows, groups | [Partitioning](https://introtorx.com/chapters/partitioning.html) |
| Merge, Concat, Switch, Zip, CombineLatest | [Combining sequences](https://introtorx.com/chapters/combining-sequences.html) |
| Threads and scheduler semantics | [Scheduling and threading](https://introtorx.com/chapters/scheduling-and-threading.html) |
| Time operators | [Time-based sequences](https://introtorx.com/chapters/time-based-sequences.html) |
| Await, tasks, blocking boundaries, exception state | [Leaving Rx's world](https://introtorx.com/chapters/leaving-reactive-extensions-world.html) |
| Catch, Retry, Finally, Using | [Error handling](https://introtorx.com/chapters/error-handling-operators.html) |
| Publish, Replay, RefCount, AutoConnect | [Publishing operators](https://introtorx.com/chapters/publishing-operators.html) |
| Virtual time and test observables | [Testing Rx](https://introtorx.com/chapters/testing-reactive-extensions-for-dotnet.html) |
| Disposable implementations | [Disposables appendix](https://introtorx.com/chapters/disposables.html) |
| Recommended practices | [Usage guidelines](https://introtorx.com/chapters/usage-guidelines.html) |

## Package-Specific Sources

| Package/change | Source |
| --- | --- |
| WPF integration | [System.Reactive.Wpf](https://www.nuget.org/packages/System.Reactive.Wpf) |
| Windows Forms integration | [System.Reactive.Windows.Forms](https://www.nuget.org/packages/System.Reactive.Windows.Forms) |
| Windows Runtime integration | [System.Reactive.WindowsRuntime](https://www.nuget.org/packages/System.Reactive.WindowsRuntime) |
| UI package rationale | [ADR 0005](https://github.com/dotnet/reactive/blob/main/Rx.NET/Documentation/adr/0005-package-split.md) |
| Experimental async-observer model | [System.Reactive.Async](https://www.nuget.org/packages/System.Reactive.Async) |

## Source Rules

1. Resolve API questions against the installed package version or matching repository tag.
2. Prefer Rx.NET source/XML documentation and IntroToRx over cross-language summaries.
3. Use ReactiveX pages for concepts and aliases only; implementations differ in names, null handling, backpressure, scheduler defaults, and edge cases.
4. Treat `main` as future development when it differs from the latest stable tag.
5. Treat examples in older articles as hypotheses until current API signatures compile.
6. Keep `System.Reactive.Async` guidance explicitly experimental until NuGet publishes a stable release and maintainers change its status.
