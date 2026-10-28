namespace Stationary.Ftms.Dashboard.Services;

public interface IDashboardPlatform
{
    string CacheDirectory { get; }

    string AppDataDirectory { get; }

    bool GetPreference(string key, bool defaultValue);

    string GetPreference(string key, string defaultValue);

    void SetPreference(string key, bool value);

    void SetPreference(string key, string value);

    void RemovePreference(string key);

    Task InvokeOnMainThreadAsync(Action action);
}
