# Packages and Versioning

## Current Baseline

The research baseline for this skill is Rx.NET 7.0.0. NuGet lists it as the current stable `System.Reactive` release; the repository `main` branch is already developing 7.1. Verify the resolved package version and use the matching tag/source before relying on an API that may have changed.

`System.IObservable<T>` and `System.IObserver<T>` are part of the .NET runtime. The operators, factories, schedulers, subjects, disposables, and delegate-based `Subscribe` overloads come from Rx.NET packages.

## Package Selection

| Package | Use | Status / constraint |
| --- | --- | --- |
| `System.Reactive` | Core Rx.NET operators, schedulers, subjects, disposables, task/event adapters | Primary stable package |
| `Microsoft.Reactive.Testing` | `TestScheduler`, hot/cold test sources, recorded notifications | Published but officially unsupported for compatibility; keep test-only |
| `System.Reactive.Wpf` | WPF dispatcher integration | Required for source builds using WPF-specific Rx APIs on v7 |
| `System.Reactive.Windows.Forms` | Windows Forms control scheduler/`ObserveOn` integration | Required for source builds using WinForms-specific Rx APIs on v7 |
| `System.Reactive.WindowsRuntime` | Windows Runtime and `CoreDispatcher` integration | Framework-specific v7 package |
| `System.Reactive.Uwp` | Classic UWP-specific integration | Add only for UWP |
| `System.Interactive` | Additional operators for `IEnumerable<T>` (Ix) | Related library, not Rx push streams |
| `System.Linq.Async` | LINQ over `IAsyncEnumerable<T>` | Async pull, not `IObservable<T>` |
| `System.Interactive.Async` | LINQ-adjacent async-enumerable adapters in current repository work | Check package/API version before using conversions |
| `System.Reactive.Async` | Experimental `IAsyncObservable<T>` implementation | Prerelease/experimental; not the default answer for async callbacks |

Use the repository's package-management convention. Basic setup is:

```bash
dotnet add package System.Reactive
dotnet add package Microsoft.Reactive.Testing
```

Add the testing package only to test projects. Pin compatible versions centrally when the solution uses central package management.

## Common Namespaces

| Namespace | Main APIs |
| --- | --- |
| `System` | BCL `IObservable<T>`, `IObserver<T>`; Rx delegate-based `Subscribe` extensions are also surfaced here |
| `System.Reactive` | `Unit`, `Notification<T>`, `EventPattern<TEventArgs>`, observer helpers |
| `System.Reactive.Linq` | `Observable` factories and query operators |
| `System.Reactive.Subjects` | `ISubject`, `Subject`, `BehaviorSubject`, `ReplaySubject`, `AsyncSubject` |
| `System.Reactive.Concurrency` | `IScheduler` and production scheduler types |
| `System.Reactive.Disposables` | Disposable implementations |
| `System.Reactive.Disposables.Fluent` | `DisposeWith` |
| `System.Reactive.Threading.Tasks` | `Task.ToObservable`, `IObservable<T>.ToTask` |
| `Microsoft.Reactive.Testing` | Virtual-time testing types |

Do not add legacy split packages merely to obtain a namespace. Since Rx.NET 4, core APIs live in `System.Reactive` even though namespaces retain their historical names.

## Rx.NET 7 Compatibility

The stable v7 package targets `net8.0`, `netstandard2.0`, and `net472`; its `net8.0` asset supports .NET 8, 9, and 10 applications. Direct .NET 6 and .NET 7 targets were removed because those runtimes are out of support.

Source-level migration from v6 requires attention to:

- Add `System.Reactive.Wpf`, `System.Reactive.Windows.Forms`, `System.Reactive.WindowsRuntime`, or `System.Reactive.Uwp` when code uses those APIs.
- Set `<UseWPF>true</UseWPF>` or `<UseWindowsForms>true</UseWindowsForms>` when the application had relied on Rx to add the desktop framework implicitly.
- Expect no source change beyond the package reference for framework APIs; replacement types retain the names and namespaces.
- Account for the corrected nullability signature of `OfType`.
- Remove direct references to old facade packages instead of waiting for v7 variants.

Rx.NET 7 keeps hidden runtime copies of older UI types for binary compatibility, but removes them from the compiler-facing `System.Reactive` reference assemblies. Existing binaries built against v6 can continue to run; rebuilt source must reference the new framework package.

## Legacy Packages to Avoid

Rx.NET 7 stopped producing new facade releases for:

- `System.Reactive.Compatibility`
- `System.Reactive.Core`
- `System.Reactive.Experimental`
- `System.Reactive.Interfaces`
- `System.Reactive.Linq`
- `System.Reactive.PlatformServices`
- `System.Reactive.Providers`
- `System.Reactive.Runtime.Remoting`
- `System.Reactive.Windows.Threading`

Reference `System.Reactive` directly. For WPF, replace the deprecated `System.Reactive.Windows.Threading` facade with `System.Reactive.Wpf`; use `System.Reactive.WindowsRuntime` for WinRT.

## Async Ecosystem Boundaries

Do not confuse three models in the same repository:

| Model | Interface | Direction / pacing | Production guidance |
| --- | --- | --- | --- |
| Rx.NET | `IObservable<T>` | Synchronous push callbacks; source controls pace | Stable `System.Reactive` baseline |
| Async LINQ / Ix | `IAsyncEnumerable<T>` | Async pull; consumer controls reads | Prefer for naturally pull-based async streams |
| AsyncRx.NET | `IAsyncObservable<T>` | Async observer callbacks | Experimental preview; adopt only with explicit prerelease risk |

Ordinary Rx.NET already interoperates with `Task`, supports cancellation-aware `FromAsync` and `Create`, and can model async per-item work through nested observables. Do not adopt `System.Reactive.Async` solely to avoid an `async void` subscription; fix that composition with `FromAsync` and `Concat`, `Merge`, or `Switch`.

## Version Review

1. Inspect direct and transitive package versions with the repository's package tooling.
2. Match `Microsoft.Reactive.Testing` to the production Rx line.
3. Read release notes from the exact tag, not only the moving `main` branch.
4. Check target-framework assets and UI package requirements before upgrading.
5. Compile for every supported target because conditional platform APIs can differ.
6. Re-run virtual-time, subscription-lifetime, and concurrency tests after any major/minor update.
