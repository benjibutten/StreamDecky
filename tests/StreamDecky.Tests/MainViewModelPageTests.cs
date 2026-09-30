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

    [Theory]
    [InlineData("#102030", 0.4, "#66102030")]
    [InlineData("Red", 1.0, "#FFFF0000")]
    [InlineData("not a colour", 0.4, "Transparent")]
    public void PageTabBarBackground_AppliesOpacityToTheBarColour(string color, double opacity, string expected)
    {
        using var tempDirectory = new TemporaryDirectory();
        using var viewModel = new MainViewModel(new ProfileService(tempDirectory.Path));

        viewModel.PageTabBarColor = color;
        viewModel.PageTabBarOpacity = opacity;

        Assert.Equal(expected, viewModel.PageTabBarBackground);
    }

    [Fact]
    public void PageTabs_UseThePageColourFadedAndInFullWhenCurrent()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var viewModel = new MainViewModel(new ProfileService(tempDirectory.Path));
        viewModel.PageTabActiveColor = "#6C5CE7";
        viewModel.CurrentPageTabColor = "#FF0000";
        viewModel.AddPageCommand.Execute(null);

        Assert.Equal(["#59FF0000", "#6C5CE7"], viewModel.PageTabs.Select(tab => tab.Background));

        viewModel.PreviousPageCommand.Execute(null);

        Assert.Equal(["#FF0000", "Transparent"], viewModel.PageTabs.Select(tab => tab.Background));
    }

    [Fact]
    public void CurrentPageTabColor_IsNotSetFromAVirtualLayout()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var viewModel = new MainViewModel(new ProfileService(tempDirectory.Path));
        viewModel.AddVirtualLayoutCommand.Execute(null);

        viewModel.CurrentPageTabColor = "#FF0000";

        Assert.False(viewModel.CanColorCurrentPage);
        Assert.Equal(string.Empty, viewModel.CurrentPageTabColor);
        Assert.All(viewModel.Profile.Pages, page => Assert.Equal(string.Empty, page.TabColor));
    }

    [Fact]
    public void CanRemoveCurrentLayout_KeepsTheLastPageButAllowsAVirtualLayout()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var viewModel = new MainViewModel(new ProfileService(tempDirectory.Path));
        Assert.False(viewModel.CanRemoveCurrentLayout);

        viewModel.AddVirtualLayoutCommand.Execute(null);
        Assert.True(viewModel.CanRemoveCurrentLayout);

        viewModel.ExitVirtualLayoutCommand.Execute(null);
        viewModel.AddPageCommand.Execute(null);
        Assert.True(viewModel.CanRemoveCurrentLayout);
    }
}
