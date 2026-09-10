# Performance And Trimming

## Measure First

- Profile Release builds on representative physical hardware before optimizing. Measure startup, navigation, scrolling, memory, CPU, network latency, battery use, and crash-free behavior.
- Use platform profilers and traces to identify the actual hot path. Do not optimize XAML, layout, or allocations based on guesses.

## Common Improvements

| Symptom | First Checks |
| --- | --- |
| Slow startup | Startup I/O, unnecessary service construction, image/font assets, synchronous initialization |
| Janky lists | `CollectionView`, template depth, image size/cache, converters, UI-thread work |
| Memory growth | Event subscriptions, retained pages, singleton references to UI/native objects, image lifetimes |
| Slow navigation | Blocking constructors, synchronous data loads, excessive layout nesting, repeated resource lookup |
| Battery/network drain | Polling, uncanceled requests, background streams, duplicate refresh work |

- Virtualize lists with `CollectionView`, page data deliberately, use appropriately sized images, and cache only when measurement justifies it.
- Avoid synchronous I/O and CPU-heavy work on the UI thread. Batch collection changes and dispatch only the final UI mutation.

## Trimming

- Treat linker warnings as release defects. Remove unused dependencies first, then preserve dynamically accessed code with the narrowest supported annotation/configuration.
- Reflection, runtime XAML, serialization, and dynamic type activation can break trimming. Prefer statically analyzable APIs and test the trimmed package on every target.
- A successful Debug build does not validate a trimmed or signed Release artifact.

## Sources

- [Improve app performance](https://learn.microsoft.com/dotnet/maui/deployment/performance?view=net-maui-10.0)
- [Trim a .NET MAUI app](https://learn.microsoft.com/dotnet/maui/deployment/trimming?view=net-maui-10.0)
