namespace StreamDecky.Models;

public class QuickTextItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    // Kept for compatibility with profiles and integrations created before multi-tag support.
    public string CategoryId { get; set; } = string.Empty;
    public List<string> CategoryIds { get; set; } = new();
    public List<string> CollectionIds { get; set; } = new();
    public string Text { get; set; } = string.Empty;

    public void EnsureInitialized()
    {
        if (string.IsNullOrWhiteSpace(Id))
            Id = Guid.NewGuid().ToString("N");

        CategoryId ??= string.Empty;
        CategoryIds ??= new List<string>();
        CollectionIds ??= new List<string>();
        CategoryIds = CategoryIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (!string.IsNullOrWhiteSpace(CategoryId) && !CategoryIds.Contains(CategoryId, StringComparer.Ordinal))
            CategoryIds.Insert(0, CategoryId);

        if (CategoryIds.Count > 0)
            CategoryId = CategoryIds[0];

        CollectionIds = CollectionIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Text ??= string.Empty;
    }

    public bool HasCategory(string categoryId) =>
        CategoryIds.Contains(categoryId, StringComparer.Ordinal);

    public bool HasCollection(string collectionId) =>
        CollectionIds.Contains(collectionId, StringComparer.Ordinal);

    public void SetCategories(IEnumerable<string> categoryIds)
    {
        CategoryIds = categoryIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        CategoryId = CategoryIds.FirstOrDefault() ?? string.Empty;
    }

    public void SetCollections(IEnumerable<string> collectionIds)
    {
        CollectionIds = collectionIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
