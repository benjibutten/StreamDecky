using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using StreamDecky.Models;

namespace StreamDecky.ViewModels;

public partial class ButtonViewModel : ObservableObject
{
    private readonly ButtonConfig _config;
    private readonly int _index;

    public ButtonViewModel(ButtonConfig config, int index)
    {
        _config = config;
        _index = index;
    }

    public int Index => _index;
    public ButtonConfig Config => _config;

    public string Title
    {
        get => _config.Title;
        set
        {
            _config.Title = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(IsConfigured));
        }
    }

    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title))
                return Title;

            return ActionType switch
            {
                ActionType.TextInput => "Text Input",
                ActionType.KeyPress => "Key Press",
                ActionType.MultiAction => "Multi Action",
                ActionType.LayoutNavigation => "Switch Layout",
                _ => "Unconfigured"
            };
        }
    }

    public string SlotLabel => $"Slot {_index + 1}";

    public ActionType ActionType
    {
        get => _config.ActionType;
        set
        {
            _config.ActionType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAction));
            OnPropertyChanged(nameof(IsConfigured));
            OnPropertyChanged(nameof(DisplayTitle));
        }
    }

    public string BackgroundColor
    {
        get => _config.BackgroundColor;
        set { _config.BackgroundColor = value; OnPropertyChanged(); }
    }

    public string TextColor
    {
        get => _config.TextColor;
        set { _config.TextColor = value; OnPropertyChanged(); }
    }

    public string IconText
    {
        get => _config.IconText;
        set
        {
            _config.IconText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsConfigured));
        }
    }

    public double CornerRadius
    {
        get => _config.CornerRadius;
        set { _config.CornerRadius = value; OnPropertyChanged(); }
    }

    public string ImagePath
    {
        get => _config.ImagePath;
        set
        {
            _config.ImagePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasImage));
            OnPropertyChanged(nameof(IsConfigured));
        }
    }

    public bool HasImage => !string.IsNullOrEmpty(ImagePath);

    public string Text
    {
        get => _config.Text;
        set { _config.Text = value; OnPropertyChanged(); }
    }

    public bool PressEnterAfter
    {
        get => _config.PressEnterAfter;
        set { _config.PressEnterAfter = value; OnPropertyChanged(); }
    }

    public TextMode TextMode
    {
        get => _config.TextMode;
        set { _config.TextMode = value; OnPropertyChanged(); }
    }

    public string KeyText
    {
        get => _config.KeyText;
        set { _config.KeyText = value; OnPropertyChanged(); }
    }

    public string TargetLayoutId
    {
        get => _config.TargetLayoutId;
        set { _config.TargetLayoutId = value; OnPropertyChanged(); }
    }

    public ButtonShape Shape
    {
        get => _config.Shape;
        set { _config.Shape = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasShape)); }
    }

    public bool HasShape => Shape != ButtonShape.None;

    private ObservableCollection<ActionStep>? _steps;
    public ObservableCollection<ActionStep> Steps
    {
        get
        {
            if (_steps == null)
            {
                _steps = new ObservableCollection<ActionStep>(_config.Steps);
                _steps.CollectionChanged += (_, _) =>
                {
                    _config.Steps = [.. _steps];
                };
            }
            return _steps;
        }
    }

    public bool HasAction => ActionType != ActionType.None;

    public bool IsConfigured => ActionType != ActionType.None
        || !string.IsNullOrEmpty(Title)
        || !string.IsNullOrEmpty(IconText)
        || !string.IsNullOrEmpty(ImagePath);

    [ObservableProperty]
    private bool _isSelected;
}
