using CommunityToolkit.Mvvm.ComponentModel;
using StreamDecky.Models;

namespace StreamDecky.ViewModels;

public partial class QuickTextCollectionAssignmentViewModel : ObservableObject
{
    private readonly Action<QuickTextCollection, bool> _onChanged;

    public QuickTextCollectionAssignmentViewModel(
        QuickTextCollection collection,
        bool isSelected,
        Action<QuickTextCollection, bool> onChanged)
    {
        Collection = collection;
        _isSelected = isSelected;
        _onChanged = onChanged;
    }

    public QuickTextCollection Collection { get; }
    public string Name => Collection.Name;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _onChanged(Collection, value);
}
