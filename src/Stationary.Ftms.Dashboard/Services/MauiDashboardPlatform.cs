namespace Stationary.Ftms.Dashboard.Services;

internal sealed class MauiDashboardPlatform : IDashboardPlatform
{
    public string CacheDirectory => FileSystem.CacheDirectory;

    public string AppDataDirectory => FileSystem.AppDataDirectory;

    public bool GetPreference(string key, bool defaultValue) => Preferences.Default.Get(key, defaultValue);

    public string GetPreference(string key, string defaultValue) => Preferences.Default.Get(key, defaultValue);

    public void SetPreference(string key, bool value) => Preferences.Default.Set(key, value);

    public void SetPreference(string key, string value) => Preferences.Default.Set(key, value);

    public void RemovePreference(string key) => Preferences.Default.Remove(key);

    public Task InvokeOnMainThreadAsync(Action action) => MainThread.InvokeOnMainThreadAsync(action);
}