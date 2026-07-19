using StreamDecky.Models;
using StreamDecky.Services;
using StreamDecky.ViewModels;
using Xunit;

namespace StreamDecky.Tests;

public sealed class QuickTextCollectionsTests
{
    [Fact]
    public void VersionTwoProfile_MigratesCategoriesToTagsWithoutLosingItems()
    {
        const string json = """
            {
              "SchemaVersion": 2,
              "Name": "Existing profile",
              "QuickTextCategories": [
                { "Id": "cat-one", "Name": "Healthcare" },
                { "Id": "cat-two", "Name": "Police" }
              ],
              "ActiveQuickTextCategoryId": "cat-two",
              "QuickTextItems": [
                { "Id": "item-one", "CategoryId": "cat-one", "Text": "Keep me" }
              ]
            }
            """;

        DeckProfile profile = ProfileService.DeserializeProfileJson(json);

        var collection = Assert.Single(profile.QuickTextCollections);
        Assert.All(profile.QuickTextCategories, tag => Assert.Equal(string.Empty, tag.CollectionId));
        Assert.Equal(collection.Id, profile.ActiveQuickTextCollectionId);
        var item = Assert.Single(profile.QuickTextItems);
        Assert.Equal("Keep me", item.Text);
        Assert.Equal(new[] { "cat-one" }, item.CategoryIds);
        Assert.Equal(new[] { collection.Id }, item.CollectionIds);
    }

    [Fact]
    public void Item_CanBelongToTagsInMultipleCollections()
    {
        var firstCollection = new QuickTextCollection { Id = "collection-a", Name = "Profile A" };
        var secondCollection = new QuickTextCollection { Id = "collection-b", Name = "Profile B" };
        var firstTag = new QuickTextCategory { Id = "tag-a", Name = "Support", CollectionId = firstCollection.Id };
        var secondTag = new QuickTextCategory { Id = "tag-b", Name = "Healthcare", CollectionId = secondCollection.Id };
        var item = new QuickTextItem
        {
            Text = "Shared text",
            CategoryIds = new List<string> { firstTag.Id, secondTag.Id },
            CollectionIds = new List<string> { firstCollection.Id, secondCollection.Id }
        };
        item.EnsureInitialized();

        var viewModel = new QuickTextItemViewModel(
            item,
            new[] { firstCollection, secondCollection },
            new[] { firstTag, secondTag },
            () => { });

        Assert.Equal(2, viewModel.AssignedTagCount);
        Assert.Equal("Support, Healthcare", viewModel.TagSummary);
        Assert.Contains("Profile A", viewModel.CollectionSummary);
        Assert.Contains("Profile B", viewModel.CollectionSummary);

        viewModel.TagAssignments[0].IsSelected = false;

        Assert.Equal(new[] { secondTag.Id }, item.CategoryIds);
        Assert.Equal(secondTag.Id, item.CategoryId);
    }

    [Fact]
    public void ProfileRoundTrip_PreservesCollectionsTagOrderAndMultiTagAssignments()
    {
        var firstCollection = new QuickTextCollection { Id = "collection-a", Name = "Profile A" };
        var secondCollection = new QuickTextCollection { Id = "collection-b", Name = "Profile B" };
        var firstTag = new QuickTextCategory { Id = "tag-a", Name = "Support", CollectionId = firstCollection.Id };
        var secondTag = new QuickTextCategory { Id = "tag-b", Name = "Police", CollectionId = secondCollection.Id };
        var profile = new DeckProfile
        {
            QuickTextCollections = new List<QuickTextCollection> { secondCollection, firstCollection },
            ActiveQuickTextCollectionId = secondCollection.Id,
            QuickTextCategories = new List<QuickTextCategory> { secondTag, firstTag },
            ActiveQuickTextCategoryId = secondTag.Id,
            QuickTextItems = new List<QuickTextItem>
            {
                new()
                {
                    Text = "Shared",
                    CategoryIds = new List<string> { firstTag.Id, secondTag.Id },
                    CollectionIds = new List<string> { firstCollection.Id, secondCollection.Id }
                }
            }
        };

        string json = ProfileService.SerializeProfileJson(profile);
        DeckProfile restored = ProfileService.DeserializeProfileJson(json);

        Assert.Equal(new[] { secondCollection.Id, firstCollection.Id }, restored.QuickTextCollections.Select(item => item.Id));
        Assert.Equal(new[] { secondTag.Id, firstTag.Id }, restored.QuickTextCategories.Select(item => item.Id));
        Assert.Equal(new[] { firstTag.Id, secondTag.Id }, Assert.Single(restored.QuickTextItems).CategoryIds);
        Assert.Equal(new[] { firstCollection.Id, secondCollection.Id }, Assert.Single(restored.QuickTextItems).CollectionIds);
    }

    [Fact]
    public void MoveCommands_PersistCollectionAndTagOrder()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var viewModel = new MainViewModel(new ProfileService(tempDirectory.Path));
        var firstCollection = viewModel.Profile.QuickTextCollections[0];
        viewModel.AddQuickTextCollectionCommand.Execute(null);
        var secondCollection = viewModel.Profile.QuickTextCollections[1];

        viewModel.MoveQuickTextCollection(secondCollection, firstCollection);

        Assert.Same(secondCollection, viewModel.Profile.QuickTextCollections[0]);

        viewModel.SelectedQuickTextCollectionId = firstCollection.Id;
        var firstTag = viewModel.QuickTextCategories[0];
        viewModel.AddQuickTextCategoryCommand.Execute(null);
        var secondTag = viewModel.QuickTextCategories[1];

        viewModel.MoveQuickTextCategory(secondTag, firstTag);

        Assert.Same(secondTag, viewModel.QuickTextCategories[0]);

    }

    [Fact]
    public void RenameCommands_NotifyExistingDropdownItemsImmediately()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var viewModel = new MainViewModel(new ProfileService(tempDirectory.Path));
        var collection = viewModel.QuickTextCollections[0];
        var tag = viewModel.QuickTextCategories[0];
        bool collectionNotified = false;
        bool tagNotified = false;
        collection.PropertyChanged += (_, e) => collectionNotified |= e.PropertyName == nameof(QuickTextCollection.Name);
        tag.PropertyChanged += (_, e) => tagNotified |= e.PropertyName == nameof(QuickTextCategory.Name);

        viewModel.RenameQuickTextCollectionCommand.Execute("Renamed collection");
        viewModel.RenameQuickTextCategoryCommand.Execute("Renamed tag");

        Assert.True(collectionNotified);
        Assert.True(tagNotified);
        Assert.Equal("Renamed collection", collection.Name);
        Assert.Equal("Renamed tag", tag.Name);
    }

    [Fact]
    public void VersionThreeProfile_MergesCollectionSpecificTagsIntoGlobalTags()
    {
        const string json = """
            {
              "SchemaVersion": 3,
              "QuickTextCollections": [
                { "Id": "my", "Name": "My" },
                { "Id": "zelda", "Name": "Zelda" }
              ],
              "ActiveQuickTextCollectionId": "my",
              "QuickTextCategories": [
                { "Id": "my-health", "Name": "Sjukvård", "CollectionId": "my" },
                { "Id": "zelda-health", "Name": "Sjukvård", "CollectionId": "zelda" }
              ],
              "ActiveQuickTextCategoryId": "my-health",
              "QuickTextItems": [
                {
                  "Id": "shared",
                  "Text": "Shared text",
                  "CategoryIds": [ "my-health", "zelda-health" ]
                }
              ]
            }
            """;

        DeckProfile profile = ProfileService.DeserializeProfileJson(json);

        var tag = Assert.Single(profile.QuickTextCategories);
        Assert.Equal("Sjukvård", tag.Name);
        var item = Assert.Single(profile.QuickTextItems);
        Assert.Equal(new[] { tag.Id }, item.CategoryIds);
        Assert.Equal(new[] { "my", "zelda" }, item.CollectionIds);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "StreamDecky.Tests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Path, recursive: true); }
            catch { }
        }
    }
}
