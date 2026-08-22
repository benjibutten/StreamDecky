using StreamDecky.Models;
using Xunit;

namespace StreamDecky.Tests;

public sealed class DeckProfileTests
{
    [Fact]
    public void Initialize_RepairsQuickTextAndPanelDefaults()
    {
        var category = new QuickTextCategory { Id = "cat-1", Name = "General" };
        var profile = new DeckProfile
        {
            QuickTextCategories = new List<QuickTextCategory> { category },
            ActiveQuickTextCategoryId = "missing-category",
            QuickTextItems = new List<QuickTextItem>
            {
                new() { Id = "item-1", CategoryId = "orphaned", Text = "hello" }
            },
            QuickTextPanelX = -10,
            QuickTextPanelY = -20,
            QuickTextFontSize = 0,
            QuickTextPanelWidth = 0,
            QuickTextPanelHeight = 0
        };

        profile.Initialize();

        Assert.Equal(category.Id, profile.ActiveQuickTextCategoryId);
        Assert.Equal(category.Id, profile.QuickTextItems[0].CategoryId);
        Assert.Equal(0, profile.QuickTextPanelX);
        Assert.Equal(0, profile.QuickTextPanelY);
        Assert.Equal(12, profile.QuickTextFontSize);
        Assert.Equal(420, profile.QuickTextPanelWidth);
        Assert.Equal(380, profile.QuickTextPanelHeight);
    }

    [Fact]
    public void Initialize_RepairsTextHelperDefaults()
    {
        var profile = new DeckProfile
        {
            TextHelperX = -30,
            TextHelperY = -5,
            TextHelperWidth = 0,
            TextHelperHeight = 0,
            TextHelperFontSize = 0,
            TextHelperFontFamily = "  "
        };

        profile.Initialize();

        Assert.Equal(0, profile.TextHelperX);
        Assert.Equal(0, profile.TextHelperY);
        Assert.Equal(420, profile.TextHelperWidth);
        Assert.Equal(320, profile.TextHelperHeight);
        Assert.Equal(17, profile.TextHelperFontSize);
        Assert.Equal(DeckProfile.DefaultTextHelperFontFamily, profile.TextHelperFontFamily);
    }

    [Fact]
    public void Initialize_ClampsTextHelperSizeToTheAllowedRange()
    {
        var profile = new DeckProfile
        {
            TextHelperWidth = 5000,
            TextHelperHeight = 1,
            TextHelperFontSize = 200
        };

        profile.Initialize();

        Assert.Equal(DeckProfile.MaxTextHelperWidth, profile.TextHelperWidth);
        Assert.Equal(DeckProfile.MinTextHelperHeight, profile.TextHelperHeight);
        Assert.Equal(DeckProfile.MaxTextHelperFontSize, profile.TextHelperFontSize);
    }
}