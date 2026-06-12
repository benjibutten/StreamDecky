using StreamDecky.Services;
using Xunit;

namespace StreamDecky.Tests;

public sealed class StartupRegistrySyncServiceTests
{
    [Fact]
    public void Sync_WhenEnabled_SetsRunEntryWithMinimizedArgument()
    {
        var store = new RecordingStartupRegistryStore();
        var service = new StartupRegistrySyncService(new RecordingStartupRegistryStoreFactory(store));
        const string exePath = @"C:\Apps\StreamDecky\StreamDecky.exe";

        bool synced = service.Sync(startWithWindows: true, exePath);

        Assert.True(synced);
        Assert.Equal(StartupRegistrySyncService.AppRegistryName, store.LastSetName);
        Assert.Equal(StartupRegistrySyncService.BuildStartupCommand(exePath), store.LastSetValue);
        Assert.False(store.DeleteCalled);
    }

    [Fact]
    public void Sync_WhenDisabled_DeletesRunEntry()
    {
        var store = new RecordingStartupRegistryStore();
        var service = new StartupRegistrySyncService(new RecordingStartupRegistryStoreFactory(store));

        bool synced = service.Sync(startWithWindows: false, @"C:\Apps\StreamDecky\StreamDecky.exe");

        Assert.True(synced);
        Assert.True(store.DeleteCalled);
        Assert.Equal(StartupRegistrySyncService.AppRegistryName, store.LastDeletedName);
    }

    private sealed class RecordingStartupRegistryStoreFactory : IStartupRegistryStoreFactory
    {
        private readonly RecordingStartupRegistryStore _store;

        public RecordingStartupRegistryStoreFactory(RecordingStartupRegistryStore store)
        {
            _store = store;
        }

        public IStartupRegistryStore? OpenCurrentUserRunKey()
        {
            return _store;
        }
    }

    private sealed class RecordingStartupRegistryStore : IStartupRegistryStore
    {
        public string? LastSetName { get; private set; }

        public string? LastSetValue { get; private set; }

        public string? LastDeletedName { get; private set; }

        public bool DeleteCalled { get; private set; }

        public void SetValue(string name, string value)
        {
            LastSetName = name;
            LastSetValue = value;
        }

        public void DeleteValue(string name, bool throwOnMissingValue)
        {
            LastDeletedName = name;
            DeleteCalled = true;
        }

        public void Dispose()
        {
        }
    }
}