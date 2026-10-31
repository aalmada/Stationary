using System.Reactive.Concurrency;

namespace Stationary.Ftms.Dashboard.ViewModels.Tests;

public sealed class ReactiveOperationQueueTests
{
    [Test]
    public async Task Operations_StartOnTheConfiguredScheduler()
    {
        var scheduler = new HistoricalScheduler();
        var ran = false;
        var errors = new List<Exception>();
        using var queue = new ReactiveOperationQueue(scheduler, errors.Add);
        queue.Enqueue(_ =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        await Assert.That(ran).IsFalse();
        scheduler.Start();
        await Assert.That(ran).IsTrue();
        await Assert.That(errors.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Operations_RunInOrderWithoutOverlapping()
    {
        var firstFinished = new TaskCompletionSource();
        var secondFinished = new TaskCompletionSource();
        var started = new List<int>();
        var errors = new List<Exception>();
        using var queue = new ReactiveOperationQueue(ImmediateScheduler.Instance, errors.Add);
        queue.Enqueue(async _ =>
        {
            started.Add(1);
            await firstFinished.Task;
        });
        queue.Enqueue(_ =>
        {
            started.Add(2);
            secondFinished.SetResult();
            return Task.CompletedTask;
        });

        await Assert.That(started.SequenceEqual([1])).IsTrue();
        firstFinished.SetResult();
        await secondFinished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(started.SequenceEqual([1, 2])).IsTrue();
        await Assert.That(errors.Count).IsEqualTo(0);
    }

    [Test]
    public async Task FailedOperation_ReportsErrorAndAllowsNextRequest()
    {
        var errors = new List<Exception>();
        var nextRan = false;
        var failure = new InvalidOperationException("Presentation failed");
        using var queue = new ReactiveOperationQueue(ImmediateScheduler.Instance, errors.Add);
        queue.Enqueue(_ => throw failure);
        queue.Enqueue(_ =>
        {
            nextRan = true;
            return Task.CompletedTask;
        });

        await Assert.That(errors.SequenceEqual([failure])).IsTrue();
        await Assert.That(nextRan).IsTrue();
    }

    [Test]
    public async Task Disposal_CancelsActiveOperationAndDropsPendingRequests()
    {
        CancellationToken activeToken = default;
        var pendingRan = false;
        var errors = new List<Exception>();
        var never = new TaskCompletionSource();
        var queue = new ReactiveOperationQueue(ImmediateScheduler.Instance, errors.Add);
        queue.Enqueue(token =>
        {
            activeToken = token;
            return never.Task;
        });
        queue.Enqueue(_ =>
        {
            pendingRan = true;
            return Task.CompletedTask;
        });

        queue.Dispose();
        queue.Dispose();
        never.SetResult();

        await Assert.That(activeToken.IsCancellationRequested).IsTrue();
        await Assert.That(pendingRan).IsFalse();
        await Assert.That(errors.Count).IsEqualTo(0);
    }
}