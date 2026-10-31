using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Stationary.Ftms.Dashboard.ViewModels;

public sealed class ReactiveOperationQueue : IDisposable
{
    private readonly ISubject<Func<CancellationToken, Task>> requests = Subject.Synchronize(new Subject<Func<CancellationToken, Task>>());
    private readonly IDisposable subscription;
    private int isDisposed;

    public ReactiveOperationQueue(IScheduler scheduler, Action<Exception> reportError)
    {
        subscription = requests
            .Select(operation => Observable.Defer(() => Observable.FromAsync(operation))
                .SubscribeOn(scheduler)
                .ObserveOn(scheduler)
                .Catch<Unit, Exception>(exception =>
                {
                    reportError(exception);
                    return Observable.Empty<Unit>();
                }))
            .Concat()
            .Subscribe(static _ => { }, reportError);
    }

    public void Enqueue(Func<CancellationToken, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref isDisposed) != 0, this);
        requests.OnNext(operation);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref isDisposed, 1) != 0)
        {
            return;
        }

        subscription.Dispose();
        requests.OnCompleted();
    }
}