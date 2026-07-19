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
}