using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using StreamDecky.Models;

namespace StreamDecky.ViewModels;

public partial class QuickTextItemViewModel : ObservableObject
{
    private readonly QuickTextItem _model;
    private readonly Action _onChanged;

    public QuickTextItemViewModel(
        QuickTextItem model,
        IEnumerable<QuickTextCollection> collections,
        IEnumerable<QuickTextCategory> categories,
        Action onChanged,
        Action<QuickTextCollection, bool>? onCollectionsChanged = null,
        Action<QuickTextCategory, bool>? onTagsChanged = null)
    {
        _model = model;
        _onChanged = onChanged;
        CollectionAssignments = new ObservableCollection<QuickTextCollectionAssignmentViewModel>(collections.Select(collection =>
            new QuickTextCollectionAssignmentViewModel(
                collection,
                model.HasCollection(collection.Id),
                (changedCollection, isSelected) =>
                {
                    var ids = model.CollectionIds.ToList();
                    if (isSelected && !ids.Contains(changedCollection.Id, StringComparer.Ordinal))
                        ids.Add(changedCollection.Id);
                    else if (!isSelected)
                        ids.RemoveAll(id => string.Equals(id, changedCollection.Id, StringComparison.Ordinal));

                    model.SetCollections(ids);
                    OnPropertyChanged(nameof(CollectionSummary));
                    OnPropertyChanged(nameof(AssignedCollectionCount));
                    _onChanged();
                    onCollectionsChanged?.Invoke(changedCollection, isSelected);
                })));

        TagAssignments = new ObservableCollection<QuickTextTagAssignmentViewModel>(categories.Select(category =>
            new QuickTextTagAssignmentViewModel(
                category,
                model.HasCategory(category.Id),
                (changedCategory, isSelected) =>
                {
                    var ids = model.CategoryIds.ToList();
                    if (isSelected && !ids.Contains(changedCategory.Id, StringComparer.Ordinal))
                        ids.Add(changedCategory.Id);
                    else if (!isSelected)
                        ids.RemoveAll(id => string.Equals(id, changedCategory.Id, StringComparison.Ordinal));

                    model.SetCategories(ids);
                    OnPropertyChanged(nameof(CategoryName));
                    OnPropertyChanged(nameof(TagSummary));
                    OnPropertyChanged(nameof(AssignedTagCount));
                    _onChanged();
                    onTagsChanged?.Invoke(changedCategory, isSelected);
                })));
    }

    public QuickTextItem Model => _model;
    public string Id => _model.Id;
    public ObservableCollection<QuickTextCollectionAssignmentViewModel> CollectionAssignments { get; }
    public ObservableCollection<QuickTextTagAssignmentViewModel> TagAssignments { get; }
    public string CategoryName => TagSummary;
    public string TagSummary
    {
        get
        {
            string summary = string.Join(", ", TagAssignments
                .Where(tag => tag.IsSelected)
                .Select(tag => tag.DisplayName));
            return string.IsNullOrWhiteSpace(summary) ? "No tags" : summary;
        }
    }
    public int AssignedTagCount => TagAssignments.Count(tag => tag.IsSelected);
    public string CollectionSummary
    {
        get
        {
            string summary = string.Join(", ", CollectionAssignments
                .Where(collection => collection.IsSelected)
                .Select(collection => collection.Name));
            return string.IsNullOrWhiteSpace(summary) ? "No collections" : summary;
        }
    }
    public int AssignedCollectionCount => CollectionAssignments.Count(collection => collection.IsSelected);

    public string Text
    {
        get => _model.Text;
        set
        {
            string normalized = value ?? string.Empty;
            if (string.Equals(_model.Text, normalized, StringComparison.Ordinal))
                return;

            _model.Text = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PreviewText));
            _onChanged();
        }
    }

    public string PreviewText => string.IsNullOrWhiteSpace(_model.Text) ? "(Empty text)" : _model.Text;
}
