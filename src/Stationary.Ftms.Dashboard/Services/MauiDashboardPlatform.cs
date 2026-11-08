using System.Reactive.Disposables;
using System.Reactive.Linq;

#if MACCATALYST
using Foundation;

using UIKit;
#endif

namespace Stationary.Ftms.Dashboard.Services;

internal sealed class MauiDashboardPlatform : IDashboardPlatform
{
    public string CacheDirectory => FileSystem.CacheDirectory;

    public string AppDataDirectory => FileSystem.AppDataDirectory;

    public IObservable<int> DisplayCount { get; } = CreateDisplayCount();

    public bool GetPreference(string key, bool defaultValue) => Preferences.Default.Get(key, defaultValue);

    public string GetPreference(string key, string defaultValue) => Preferences.Default.Get(key, defaultValue);

    public void SetPreference(string key, bool value) => Preferences.Default.Set(key, value);

    public void SetPreference(string key, string value) => Preferences.Default.Set(key, value);

    public void RemovePreference(string key) => Preferences.Default.Remove(key);

    public Task InvokeOnMainThreadAsync(Action action) => MainThread.InvokeOnMainThreadAsync(action);

    public Task<FileExportResult> ExportFileAsync(string path, CancellationToken cancellationToken) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var source = NSUrl.FromFilename(path);
            using var picker = new UIDocumentPickerViewController([source], asCopy: true);
            picker.AllowsMultipleSelection = false;
            var completion = new TaskCompletionSource<FileExportResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            picker.DidPickDocumentAtUrls += (_, args) =>
            {
                if (args.Urls is [var destination] && destination.Path is { Length: > 0 } savedPath)
                {
                    completion.TrySetResult(FileExportResult.Saved(savedPath));
                }
                else
                {
                    completion.TrySetException(new IOException("The file picker did not report a saved file."));
                }
            };
            picker.WasCancelled += (_, _) => completion.TrySetResult(FileExportResult.Cancelled);
            var presenter = Platform.GetCurrentUIViewController()
                ?? throw new InvalidOperationException("No window is available to export the session.");
            await presenter.PresentViewControllerAsync(picker, animated: true);
            try
            {
                return await completion.Task.WaitAsync(cancellationToken);
            }
            finally
            {
                if (picker.PresentingViewController is not null)
                {
                    await picker.DismissViewControllerAsync(animated: true);
                }
            }
        });

#if MACCATALYST
    public IDisposable KeepAwake(string reason) => AppKitInterop.PreventSleep(reason);

    private static IObservable<int> CreateDisplayCount() =>
        Observable.Create<int>(static observer =>
            {
                observer.OnNext(AppKitInterop.ScreenCount);
                var token = NSNotificationCenter.DefaultCenter.AddObserver(
                    new NSString(AppKitInterop.ScreenParametersChangedNotification),
                    _ => observer.OnNext(AppKitInterop.ScreenCount));
                return Disposable.Create(() => NSNotificationCenter.DefaultCenter.RemoveObserver(token));
            })
            .DistinctUntilChanged()
            .Replay(1)
            .RefCount();
#else
    public IDisposable KeepAwake(string reason) => Disposable.Empty;

    private static IObservable<int> CreateDisplayCount() => Observable.Return(1);
#endif
}