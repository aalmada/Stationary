using System.Reactive.Disposables;
using System.Reactive.Linq;

#if MACCATALYST
using Foundation;
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