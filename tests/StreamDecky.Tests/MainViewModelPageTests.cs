using StreamDecky.Models;
using StreamDecky.Services;
using StreamDecky.ViewModels;
using Xunit;

namespace StreamDecky.Tests;

public sealed class MainViewModelPageTests
{
    [Fact]
    public void SwitchingPages_EditsLandOnTheShownPage()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var viewModel = new MainViewModel(new ProfileService(tempDirectory.Path));
        viewModel.Buttons[0].Title = "First";
        viewModel.AddPageCommand.Execute(null);

        Assert.Equal(string.Empty, viewModel.Buttons[0].Title);
        viewModel.Buttons[0].Title = "Second";

        viewModel.PreviousPageCommand.Execute(null);

        Assert.Equal("First", viewModel.Buttons[0].Title);
        Assert.Equal("First", viewModel.Profile.Pages[0].Buttons[0].Title);
        Assert.Equal("Second", viewModel.Profile.Pages[1].Buttons[0].Title);
    }

    [Fact]
    public void StepsCollectionFromPreviousPage_DoesNotWriteIntoCurrentPage()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var viewModel = new MainViewModel(new ProfileService(tempDirectory.Path));
        var firstPageSteps = viewModel.Buttons[0].Steps;
        viewModel.AddPageCommand.Execute(null);

        firstPageSteps.Add(new ActionStep());

        Assert.Single(viewModel.Profile.Pages[0].Buttons[0].Steps);
        Assert.Empty(viewModel.Profile.Pages[1].Buttons[0].Steps);
        Assert.Empty(viewModel.Buttons[0].Steps);
    }

    [Fact]
    public void PageTabs_ReplaceArrowsAndFollowTheCurrentPage()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var viewModel = new MainViewModel(new ProfileService(tempDirectory.Path));
        viewModel.AddPageCommand.Execute(null);
        Assert.True(viewModel.ShowOverlayPageArrows);
        Assert.False(viewModel.ShowOverlayPageTabs);

        viewModel.OverlayPageTabsEnabled = true;
        viewModel.SelectedLayoutId = viewModel.PageTabs[0].Id;

        Assert.False(viewModel.ShowOverlayPageArrows);
        Assert.True(viewModel.ShowOverlayPageTabs);
        Assert.Equal(0, viewModel.CurrentPageIndex);
        Assert.Equal([true, false], viewModel.PageTabs.Select(tab => tab.IsCurrent));
    }

    [Fact]
    public void PageTabs_StayVisibleInsideVirtualLayoutWithNoTabCurrent()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var viewModel = new MainViewModel(new ProfileService(tempDirectory.Path));
        viewModel.OverlayPageTabsEnabled = true;
        Assert.False(viewModel.ShowOverlayPageTabs);

        viewModel.AddVirtualLayoutCommand.Execute(null);

        Assert.True(viewModel.ShowOverlayPageTabs);
        Assert.DoesNotContain(viewModel.PageTabs, tab => tab.IsCurrent);
    }
}
