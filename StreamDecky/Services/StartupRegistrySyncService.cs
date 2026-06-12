using Microsoft.Win32;

namespace StreamDecky.Services;

public interface IStartupRegistryStore : IDisposable
{
    void SetValue(string name, string value);

    void DeleteValue(string name, bool throwOnMissingValue);
}

public interface IStartupRegistryStoreFactory
{
    IStartupRegistryStore? OpenCurrentUserRunKey();
}

public sealed class StartupRegistrySyncService
{
    public const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    public const string AppRegistryName = "StreamDecky";

    private readonly IStartupRegistryStoreFactory _storeFactory;

    public StartupRegistrySyncService(IStartupRegistryStoreFactory? storeFactory = null)
    {
        _storeFactory = storeFactory ?? new RegistryStartupStoreFactory();
    }

    public static string BuildStartupCommand(string exePath)
    {
        return $"\"{exePath}\" --minimized";
    }

    public bool Sync(bool startWithWindows, string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return false;

        using var store = _storeFactory.OpenCurrentUserRunKey();
        if (store == null)
            return false;

        if (startWithWindows)
        {
            store.SetValue(AppRegistryName, BuildStartupCommand(exePath));
        }
        else
        {
            store.DeleteValue(AppRegistryName, false);
        }

        return true;
    }

    private sealed class RegistryStartupStoreFactory : IStartupRegistryStoreFactory
    {
        public IStartupRegistryStore? OpenCurrentUserRunKey()
        {
            RegistryKey? key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
            return key == null ? null : new RegistryStartupStore(key);
        }
    }

    private sealed class RegistryStartupStore : IStartupRegistryStore
    {
        private readonly RegistryKey _key;

        public RegistryStartupStore(RegistryKey key)
        {
            _key = key;
        }

        public void SetValue(string name, string value)
        {
            _key.SetValue(name, value);
        }

        public void DeleteValue(string name, bool throwOnMissingValue)
        {
            _key.DeleteValue(name, throwOnMissingValue);
        }

        public void Dispose()
        {
            _key.Dispose();
        }
    }
}