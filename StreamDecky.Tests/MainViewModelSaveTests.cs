using StreamDecky.Services;
using StreamDecky.ViewModels;
using Xunit;

namespace StreamDecky.Tests;

public sealed class MainViewModelSaveTests
{
    [Fact]
    public void Dispose_FlushesChangesThatAreStillWaitingForAutosave()
    {
        using var directory = new TemporaryDirectory();
        var profileService = new ProfileService(directory.Path);
        var viewModel = new MainViewModel(profileService);

        viewModel.OverlayBackgroundColor = "#123456";
        viewModel.Dispose();

        Assert.Equal("#123456", profileService.LoadStore().GetActiveProfile().OverlayBackgroundColor);
    }
}
