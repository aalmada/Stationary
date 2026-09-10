# Commands and Errors

## Factory Selection

| Work shape | Factory / generator shape |
| --- | --- |
| Fast synchronous action or function | `ReactiveCommand.Create` |
| Naturally asynchronous I/O | `ReactiveCommand.CreateFromTask` or task-returning `[ReactiveCommand]` method |
| Existing observable operation | `ReactiveCommand.CreateFromObservable` or observable-returning generated method |
| Synchronous CPU-bound work | `ReactiveCommand.CreateRunInBackground` or `[ReactiveCommand(RunInBackground = true)]` |
| Homogeneous child commands | `ReactiveCommand.CreateCombined` |

Use the selected distribution's empty type: `RxVoid` in the default family and `Unit` in `.Reactive`. Prefer concrete `ReactiveCommand<TInput, TOutput>` properties over `ICommand`; the reactive type exposes execution, results, busy state, failures, and testable scheduling.

## Generated Command

```csharp
using ReactiveUI;
using ReactiveUI.SourceGenerators;

public partial class EditorViewModel : ReactiveObject
{
    private readonly IDocumentStore _store;
    private readonly IObservable<bool> _canSave;

    [Reactive]
    private string _text = string.Empty;

    public EditorViewModel(IDocumentStore store)
    {
        _store = store;
        _canSave = this.WhenAnyValue(
            x => x.Text,
            static text => !string.IsNullOrWhiteSpace(text));
    }

    [ReactiveCommand(CanExecute = nameof(_canSave))]
    private Task SaveAsync(CancellationToken cancellationToken) =>
        _store.SaveAsync(Text, cancellationToken);
}
```

The generator creates `SaveCommand`; it removes the `Async` suffix. Use a final `CancellationToken` parameter so disposal of an execution can cancel cooperative task work.

## Execution Semantics

- `Execute()` returns a cold execution observable. Nothing runs until code subscribes or awaits it.
- Command binding subscribes on behalf of the view; direct calls must do so explicitly.
- The command itself is an observable of results from all executions. Subscribe to it for aggregate result handling; subscribe to `Execute(...)` for one invocation.
- A command disables re-execution while it is running. A supplied `canExecute` supplements that built-in busy gate.
- `IsExecuting` emits busy transitions. Derive a read-only busy property through OAPH instead of toggling a flag around the method.
- Dispose the subscription returned by `Execute` to stop observing and to request cancellation when the selected factory supports it. The operation must honor its token.
- Do not execute commands in a view-model constructor. Trigger initial work from activation or an explicit application workflow so tests can construct the view model without side effects.

Direct invocation:

```csharp
Document saved = await viewModel.SaveAsCommand.Execute(path);
```

Observable invocation:

```csharp
IDisposable execution = viewModel.RefreshCommand
    .Execute()
    .Subscribe(_ => { }, ReportFailure);
```

## `CanExecute`

Create `canExecute` as a stable, non-failing observable of domain readiness:

```csharp
IObservable<bool> canSubmit = this.WhenAnyValue(
    x => x.Email,
    x => x.Password,
    static (email, password) =>
        !string.IsNullOrWhiteSpace(email) && password.Length >= 12);
```

The command caches the latest value. Parameter-dependent `CanExecute` is not the normal ReactiveUI pattern; bind control input to view-model state and derive executability from that state.

ReactiveCommand does not marshal the supplied `canExecute` stream to the UI scheduler. Add the selected family's UI scheduling operator before command creation when values can arrive from a worker thread. A `canExecute` error is also published through `ThrownExceptions` and permanently ends that executability source; treat it as a programming defect or recover upstream.

## Scheduling

`outputScheduler` controls delivery of command results, `CanExecute`, `IsExecuting`, and `ThrownExceptions`. It does not choose where the execution delegate runs.

| Need | Do |
| --- | --- |
| Async I/O | Use `CreateFromTask`; let the API remain asynchronous |
| CPU work off the UI thread | Use `CreateRunInBackground` with an explicit background scheduler/sequencer when tests need control |
| UI result delivery | Use the default main-thread output or pass `outputScheduler` |
| Internal multi-stage observable work | Place scheduling operators inside the execution pipeline |

Do not wrap naturally asynchronous I/O in `Task.Run` or `CreateRunInBackground`. Do not assume a command created on the UI thread executes its delegate there; the factory and caller determine execution.

## Error Contract

The per-execution observable reports its error. The command-level result observable does not terminate on an execution failure; every failure is also emitted as a value on `ThrownExceptions`.

If nobody observes `ThrownExceptions`, the default exception handler schedules the exception onto the main thread and the application crashes. This deliberate fail-fast behavior prevents silent command failures.

Use three layers deliberately:

1. Convert expected domain outcomes into result values when failure is part of normal flow.
2. Observe recoverable command failures, log them, and map them to view-model state or an `Interaction`.
3. Configure `RxAppBuilder.WithExceptionHandler(...)` for last-resort diagnostics and fatal policy, not as a substitute for local recovery.

Do not subscribe with an empty handler merely to suppress crashes. When forwarding an unhandled exception from a local handler, send it to `RxState.DefaultExceptionHandler`.

Nested commands can publish the same failure through each command's `ThrownExceptions`; centralize ownership or deduplicate only with a clearly defined incident identity. Time-based throttling is not a general substitute for error identity.

## Composition

- `InvokeCommand` executes when the source emits and respects current command executability; dispose the returned subscription.
- `CreateCombined` requires compatible input/output types and is executable only when all children and optional parent conditions allow it.
- Prefer one orchestration command over commands imperatively calling commands when duplicate error publication or cancellation ownership becomes unclear.
- Keep command methods focused on application work. Dialogs, windows, navigation controls, and visual feedback belong in interactions, routing, derived state, or views.

## Shared Operation Coordination

ReactiveCommand prevents overlapping executions of one command. It does not make separate commands mutually exclusive, and `CanExecute` is not an atomic resource lock. When several commands mutate one session, document, transport, or transaction:

- Prefer one orchestration command when the operations form one workflow.
- Otherwise, route every conflicting command through one cancellation-aware asynchronous gate or service coordinator.
- Pass the command token through both gate admission and the admitted operation. A token used only by `WaitAsync` leaves active work uncancellable.
- Link caller cancellation with operation timeouts, then distinguish caller cancellation from timeout when deriving status.
- Combine current execution states, then project `states.Any(executing => executing)`; merging transition booleans can report idle when one command finishes while another remains active.

Keep resource correctness in the coordinator. `CanExecute` and `IsExecuting` remain presentation state and must not be the only exclusion mechanism.

## Tests

- Use a real command; do not mock `ReactiveCommand` semantics.
- Subscribe or await every direct `Execute` call.
- Assert result, `IsExecuting` transitions, `CanExecute`, cancellation, and `ThrownExceptions` where relevant.
- Test cancellation while waiting for shared admission and after admission reaches the underlying operation.
- Test overlapping commands and assert aggregate busy state remains true until the final execution ends.
- Supply an immediate or virtual output scheduler so tests do not depend on a UI dispatcher.
- Verify a rejected second execution when the first is still active.
- Verify expected failures are handled once and unexpected failures reach the chosen fatal policy.
