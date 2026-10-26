using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace UsageTrackerWidget.App.ViewModels;

internal sealed partial class TrayMetricOptionViewModel : ObservableObject
{
    private readonly Action<string> _select;
    private bool _isSelected;

    public TrayMetricOptionViewModel(string key, string label, Action<string> select)
    {
        Key = key;
        Label = label;
        _select = select;
        SelectCommand = new RelayCommand(() => _select(Key));
    }

    public string Key { get; }

    public string Label { get; }

    public IRelayCommand SelectCommand { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
