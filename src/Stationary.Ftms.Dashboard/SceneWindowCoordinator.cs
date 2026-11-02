using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using ReactiveUI;

using Stationary.Ftms.Dashboard.ViewModels;

namespace Stationary.Ftms.Dashboard;

// Shows the 3D ride scene in its own window whenever the scene view model asks for it, and places that window on a
// secondary display when one is connected. Closing the window from the title bar is routed back to the view model.
internal sealed class SceneWindowCoordinator(DashboardViewModel viewModel) : IDisposable
{
    private static readonly TimeSpan PlacementInterval = TimeSpan.FromMilliseconds(300);
    private const int PlacementAttempts = 15;
    private readonly CompositeDisposable subscriptions = [];
    private readonly SerialDisposable placement = new();
    private Window? sceneWindow;
    private ScenePage? scenePage;
    private bool closingFromViewModel;

    public void Start()
    {
        subscriptions.Add(placement);
        subscriptions.Add(viewModel.Scene.WhenAnyValue(static scene => scene.ShouldShowWindow)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(show =>
            {
                if (show)
                {
                    Open();
                }
                else
                {
                    Close();
                }
            }));
        subscriptions.Add(viewModel.Scene.WhenAnyValue(static scene => scene.HasSecondDisplay)
            .Where(static second => second)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => PlaceOnSecondaryDisplay()));
#if DEBUG
        if (Scene.SceneDemo.IsEnabled)
        {
            subscriptions.Add(RxApp.MainThreadScheduler.Schedule(TimeSpan.FromSeconds(2d), Open));
        }
#endif
    }

    public void Dispose()
    {
        subscriptions.Dispose();
        Close();
    }

    private void Open()
    {
        if (sceneWindow is not null || Application.Current is not { } application)
        {
            return;
        }

        scenePage = new ScenePage(viewModel);
        var window = new Window(scenePage) { Title = ScenePage.WindowTitle };
        window.Destroying += OnSceneWindowDestroying;
        sceneWindow = window;
        application.OpenWindow(window);
        PlaceOnSecondaryDisplay();
    }

    private void Close()
    {
        placement.Disposable = null;
        if (sceneWindow is not { } window)
        {
            return;
        }

        closingFromViewModel = true;
        try
        {
            Application.Current?.CloseWindow(window);
        }
        finally
        {
            closingFromViewModel = false;
        }

        Release(window);
    }

    private void OnSceneWindowDestroying(object? sender, EventArgs e)
    {
        if (sender is not Window window || window != sceneWindow)
        {
            return;
        }

        var closedByUser = !closingFromViewModel;
        Release(window);
        if (closedByUser)
        {
            viewModel.Scene.CloseWindowCommand.Execute().Subscribe(
                static _ => { },
                static exception => Trace.TraceError("Closing the ride scene failed: {0}", exception));
        }
    }

    private void Release(Window window)
    {
        window.Destroying -= OnSceneWindowDestroying;
        scenePage?.StopRendering();
        scenePage = null;
        sceneWindow = null;
    }

    // The AppKit window appears asynchronously after the scene connects, so retry until it can be moved.
    private void PlaceOnSecondaryDisplay()
    {
#if MACCATALYST
        if (sceneWindow is null || !viewModel.Scene.HasSecondDisplay)
        {
            return;
        }

        placement.Disposable = Observable.Interval(PlacementInterval, RxApp.MainThreadScheduler)
            .Take(PlacementAttempts)
            .Select(static _ => AppKitInterop.MoveToSecondaryScreen(ScenePage.WindowTitle))
            .TakeWhile(static moved => !moved)
            .Subscribe(static _ => { }, static exception => Trace.TraceError("Placing the ride scene failed: {0}", exception));
#endif
    }
}