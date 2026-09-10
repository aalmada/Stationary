# Scheduling and Testing

## Scheduler Model

ReactiveUI exposes application schedulers through `RxSchedulers` after `RxAppBuilder` configures the platform:

| Purpose | Default family | System.Reactive family |
| --- | --- | --- |
| Scheduling type | `ISequencer` | `IScheduler` |
| UI scheduler | `RxSchedulers.MainThreadScheduler` | Same property, `IScheduler`-typed |
| Background scheduler | `RxSchedulers.TaskpoolScheduler` | Same property, `IScheduler`-typed |
| Immediate test execution | `Sequencer.Immediate` | `ImmediateScheduler.Instance` |
| Virtual time | ReactiveUI.Primitives `VirtualClock` | `Microsoft.Reactive.Testing.TestScheduler` |
| Test package | `ReactiveUI.Testing` | `ReactiveUI.Testing.Reactive` |

Configure custom schedulers through `RxAppBuilder.WithMainThreadScheduler` and `WithTaskPoolScheduler`. Access them through `RxSchedulers` at runtime; even where compatibility setters remain public, keep configuration centralized in the builder. Do not assign legacy `RxApp` properties.

## Place Scheduling at Boundaries

- Property notifications and control updates must reach the UI scheduler.
- Perform I/O asynchronously without occupying a scheduler thread.
- Move CPU-bound synchronous work to the task-pool scheduler or a supplied background scheduler.
- Apply `ObserveOn` as late as possible, after filtering/transformation and immediately before UI-bound state or collection binding.
- `SubscribeOn` controls subscription side effects, not downstream notification delivery.
- A `ReactiveCommand` output scheduler delivers results, `CanExecute`, `IsExecuting`, and `ThrownExceptions`; it does not schedule the execution delegate.
- An OAPH defaults to current-thread notification. Pass its scheduler explicitly when upstream context is not guaranteed.

For reusable view models, inject the scheduler/sequencer used for time or delivery:

```csharp
public SearchViewModel(ISequencer ui, ISequencer timer)
{
    _results = this.WhenAnyValue(x => x.Query)
        .Throttle(TimeSpan.FromMilliseconds(300), timer)
        .SelectLatestAsync(SearchAsync)
        .ObserveOn(ui)
        .ToProperty(this, nameof(Results), scheduler: ui);
}
```

The exact async operator depends on the selected observable family. Preserve the intent: debounce on an injected clock, cancel stale searches, and marshal only the result. Load the Rx.NET skill for System.Reactive operator semantics.

## Test Isolation

Test view models as ordinary objects. Replace services and time, not ReactiveUI framework types. Avoid constructing controls unless testing platform binding/activation integration.

ReactiveUI.Testing provides `With`, `WithAsync`, and `WithScheduler` extensions for the selected family's scheduler contract. The scope temporarily replaces both `RxSchedulers.MainThreadScheduler` and `RxSchedulers.TaskpoolScheduler` and restores them afterward:

```csharp
virtualClock.With(clock =>
{
    var viewModel = new SearchViewModel(clock, clock);
    viewModel.Query = "reactive";

    clock.AdvanceBy(quietPeriod);

    Assert.That(viewModel.Results, Is.Not.Empty);
});
```

Use the installed virtual clock's exact advance API. For `.Reactive`, `TestSchedulerExtensions` also provides millisecond helpers such as `AdvanceByMs` and recorded notification helpers.

The global scheduler and resolver surfaces make these scopes process-wide. Mark tests that replace them as non-parallel, or prefer constructor-injected schedulers and isolated resolver scopes. `RxTest.AppBuilderTestAsync`/`AppBuilderTestBase` exist for builder-focused tests; follow the installed package's fixtures rather than sharing mutable global setup across tests.

## Commands

Do not mock `ReactiveCommand`. Execute the real command with a fake service:

```csharp
List<Result> result = await viewModel.SearchCommand.Execute("query");
```

Cover:

- Initial and changing `CanExecute` values.
- `IsExecuting` false/true/false ordering.
- A second invocation while work is active.
- Result delivery on the configured output scheduler.
- Expected and unexpected `ThrownExceptions` behavior.
- Cancellation when the execution subscription is disposed or its token is canceled.
- Lazy behavior: no side effect before subscribe/await.

Avoid `.Wait()`, `.Result`, sleeping, and dispatcher pumping. Drive virtual time or await a deterministic fake.

## OAPH and Property Tests

- Read/observe a deferred OAPH before expecting its source to subscribe.
- Assert the initial value before the first source emission.
- Advance the scheduler that delivers property notifications, not only the source timer.
- Verify an upstream error follows the designed recovery path and does not silently freeze state.
- For nested `WhenAnyValue`, test null intermediate, replacement, same final value, and notification-capable versus plain properties.

The scheduler scope changes ReactiveUI globals, but an OAPH using its default current-thread scheduler does not automatically use the replacement. Pass the scheduler explicitly when the test must control delivery.

## Interactions and Routing

Register a deterministic interaction handler in the test and dispose it afterward. Assert both output and unhandled behavior. For shared interactions, use a `using` scope so one test cannot affect another.

For routing, execute navigation commands by subscribing/awaiting, then assert stack contents, current view model, back/reset executability, and activation/deactivation. Test missing view registration separately in a platform integration test.

## Activation

Test one full activation cycle and at least one reactivation:

1. Activate and assert setup ran once.
2. Emit from a long-lived source and assert the state change.
3. Deactivate and verify cancellation/unsubscription.
4. Emit again and assert no change.
5. Reactivate and verify one fresh subscription.

## DynamicData

Assert change reasons and keys where collection semantics matter, not only the final list. Test batched edits, item refresh, sort movement, removal disposal, source completion/error, and connection disposal. Keep the UI scheduler deterministic and place it immediately before `Bind`, matching production.

## Platform Integration Tests

Use platform tests for:

- `IViewFor<T>` view-model property behavior.
- Activation signals from visual-tree attach/detach.
- Binding conversion and default command events.
- Dispatcher affinity.
- View location and routed-host rendering.
- Trimming/AOT view registration in a published app.

Run those separately from fast view-model tests. Virtual time proves ordering, not thread safety; add focused concurrency tests when callbacks can overlap in production.
