using CommunityToolkit.Mvvm.ComponentModel;
using StreamDecky.Models;

namespace StreamDecky.ViewModels;

public partial class QuickTextTagAssignmentViewModel : ObservableObject
{
    private readonly Action<QuickTextCategory, bool> _onChanged;

    public QuickTextTagAssignmentViewModel(
        QuickTextCategory category,
        bool isSelected,
        Action<QuickTextCategory, bool> onChanged)
    {
        Category = category;
        _isSelected = isSelected;
        _onChanged = onChanged;
    }

    public QuickTextCategory Category { get; }
    public string Name => Category.Name;
    public string DisplayName => Name;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _onChanged(Category, value);
}
