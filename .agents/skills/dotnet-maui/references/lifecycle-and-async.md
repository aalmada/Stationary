# Lifecycle And Async Work

## Window Lifecycle

| Event | Typical Action |
| --- | --- |
| `Created` | Initialize work that needs a native window/handler |
| `Activated` | Resume focus-dependent UI work |
| `Deactivated` | Stop focus-dependent work and protect sensitive content |
| `Stopped` | Cancel requests, pause streams, release exclusive resources |
| `Resumed` | Reconnect subscriptions and refresh visible content |
| `Destroying` | Detach native-window/control event subscriptions |

- Subscribe in `App.CreateWindow` or override methods on a custom `Window` class. Do not assume `Resumed` occurs on the first app launch.
- The OS may terminate a stopped app without a resume. Persist important state before it becomes unrecoverable.
- On iOS and Mac Catalyst, use `Backgrounding` state for small state that must survive backgrounding, and restore it through activation state.

## Async Boundaries

- Propagate `CancellationToken` from page/workflow ownership into HTTP, storage, and long-running work. Cancel it when navigation or lifecycle makes results irrelevant.
- Represent loading, empty, failure, and retry states in the view model. Do not suppress expected operational failures.
- Use `async Task` by default. Restrict `async void` to required framework event signatures and handle exceptions inside them.
- Update native controls and bound collections from the main thread. Avoid holding a page reference across an awaited background operation.

## Sources

- [App lifecycle](https://learn.microsoft.com/dotnet/maui/fundamentals/app-lifecycle?view=net-maui-10.0)
- [Main thread](https://learn.microsoft.com/dotnet/maui/platform-integration/appmodel/main-thread?view=net-maui-10.0)
