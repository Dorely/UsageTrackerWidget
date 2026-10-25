using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using UsageTrackerWidget.App.ViewModels;

namespace UsageTrackerWidget.App.Views;

public partial class UsageFlyoutWindow : Window
{
    private bool _allowClose;
    private bool _positioning;

    internal UsageFlyoutWindow(UsageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SizeChanged += OnSizeChanged;
    }

    public void ShowNearTray()
    {
        if (!IsVisible)
        {
            Show();
        }

        PositionAboveTaskbar();
        Activate();
        Focus();
        Dispatcher.BeginInvoke(PositionAboveTaskbar, DispatcherPriority.Loaded);
    }

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        PositionAboveTaskbar();
    }

    private void PositionAboveTaskbar()
    {
        if (_positioning)
        {
            return;
        }

        _positioning = true;
        try
        {
            var workArea = SystemParameters.WorkArea;
            MaxHeight = Math.Max(320, workArea.Height - 24);
            UpdateLayout();

            var height = ActualHeight > 0 ? ActualHeight : DesiredSize.Height;
            height = Math.Min(height, MaxHeight);
            Left = Math.Max(workArea.Left + 12, workArea.Right - Width - 10);
            Top = Math.Max(workArea.Top + 12, workArea.Bottom - height - 10);
        }
        finally
        {
            _positioning = false;
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        if (IsVisible)
        {
            PositionAboveTaskbar();
        }
    }

    private void OnDeactivated(object? sender, EventArgs eventArgs)
    {
        if (IsVisible)
        {
            Hide();
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape)
        {
            Hide();
            eventArgs.Handled = true;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs eventArgs)
    {
        Hide();
    }

    private void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_allowClose)
        {
            return;
        }

        eventArgs.Cancel = true;
        Hide();
    }
}
