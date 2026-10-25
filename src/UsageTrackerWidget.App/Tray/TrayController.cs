using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using UsageTrackerWidget.App.ViewModels;

namespace UsageTrackerWidget.App.Tray;

internal sealed class TrayController : IDisposable
{
    private readonly TaskbarIcon _taskbarIcon;
    private readonly UsageViewModel _viewModel;
    private readonly Action _toggleFlyout;
    private bool _disposed;

    public TrayController(UsageViewModel viewModel, Action toggleFlyout)
    {
        _viewModel = viewModel;
        _toggleFlyout = toggleFlyout;

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = viewModel.ToolTipText,
            NoLeftClickDelay = true,
            MenuActivation = PopupActivationMode.None,
            ContextMenu = CreateContextMenu(),
            Icon = DynamicUsageIcon.Create(viewModel.IconPercent, viewModel.IconWarning),
        };
        _taskbarIcon.TrayLeftMouseUp += OnTrayLeftMouseUp;
        _taskbarIcon.TrayRightMouseUp += OnTrayRightMouseUp;
        _taskbarIcon.ForceCreate();
        UpdateTrayIcon();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu
        {
            DataContext = _viewModel,
        };

        var openItem = new MenuItem { Header = "Open usage" };
        openItem.Click += (_, _) => _toggleFlyout();
        menu.Items.Add(openItem);

        var refreshItem = new MenuItem
        {
            Header = "Refresh now",
            Command = _viewModel.RefreshCommand,
        };
        menu.Items.Add(refreshItem);
        menu.Items.Add(new Separator());

        var startupItem = new MenuItem
        {
            Header = "Start with Windows",
            IsCheckable = true,
        };
        startupItem.SetBinding(
            MenuItem.IsCheckedProperty,
            new Binding(nameof(UsageViewModel.StartWithWindows))
            {
                Source = _viewModel,
                Mode = BindingMode.TwoWay,
            });
        menu.Items.Add(startupItem);

        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem
        {
            Header = "Exit",
            Command = _viewModel.ExitCommand,
        });

        return menu;
    }

    private void OnTrayLeftMouseUp(object sender, RoutedEventArgs eventArgs)
    {
        _toggleFlyout();
    }

    private void OnTrayRightMouseUp(object sender, RoutedEventArgs eventArgs)
    {
        if (GetCursorPos(out var position))
        {
            _taskbarIcon.ShowContextMenu(new System.Drawing.Point(position.X, position.Y));
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(UsageViewModel.IconPercent)
            or nameof(UsageViewModel.IconWarning))
        {
            UpdateTrayIcon();
        }

        if (eventArgs.PropertyName is nameof(UsageViewModel.ToolTipText))
        {
            _taskbarIcon.ToolTipText = _viewModel.ToolTipText;
        }
    }

    private void UpdateTrayIcon()
    {
        _taskbarIcon.Icon = DynamicUsageIcon.Create(
            _viewModel.IconPercent,
            _viewModel.IconWarning);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _taskbarIcon.TrayLeftMouseUp -= OnTrayLeftMouseUp;
        _taskbarIcon.TrayRightMouseUp -= OnTrayRightMouseUp;
        _taskbarIcon.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
