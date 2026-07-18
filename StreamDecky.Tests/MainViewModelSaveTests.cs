using StreamDecky.Services;
using StreamDecky.ViewModels;
using Xunit;

namespace StreamDecky.Tests;

public sealed class MainViewModelSaveTests
{
    [Fact]
    public void MoveButton_MovesConfigurationAndClearsSource()
    {
        using var directory = new TemporaryDirectory();
        using var viewModel = new MainViewModel(new ProfileService(directory.Path));
        var source = viewModel.Buttons[0];
        var target = viewModel.Buttons[1];
        source.Title = "Source";
        target.Title = "Replaced";

        Assert.True(viewModel.MoveButton(source, target));

        Assert.False(viewModel.Buttons[0].IsConfigured);
        Assert.Equal("Source", viewModel.Buttons[1].Title);
        Assert.Same(viewModel.Buttons[1], viewModel.SelectedButton);
    }

    [Fact]
    public void CopyButtonTo_CopiesConfigurationAndKeepsSource()
    {
        using var directory = new TemporaryDirectory();
        using var viewModel = new MainViewModel(new ProfileService(directory.Path));
        var source = viewModel.Buttons[0];
        var target = viewModel.Buttons[1];
        source.Title = "Source";
        source.Text = "Copied text";
        target.Title = "Replaced";

        Assert.True(viewModel.CopyButtonTo(source, target));

        Assert.Equal("Source", viewModel.Buttons[0].Title);
        Assert.Equal("Copied text", viewModel.Buttons[0].Text);
        Assert.Equal("Source", viewModel.Buttons[1].Title);
        Assert.Equal("Copied text", viewModel.Buttons[1].Text);
        Assert.NotSame(viewModel.Buttons[0].Config, viewModel.Buttons[1].Config);
        Assert.Same(viewModel.Buttons[1], viewModel.SelectedButton);
    }

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
