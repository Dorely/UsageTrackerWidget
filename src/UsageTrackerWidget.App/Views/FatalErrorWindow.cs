using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UsageTrackerWidget.App.Views;

internal sealed class FatalErrorWindow : Window
{
    public FatalErrorWindow(string title, string message)
    {
        Title = "Usage Tracker";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Transparent;
        Foreground = Brush("TextPrimaryBrush", Brushes.White);
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var closeButton = new Button
        {
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 18, 0, 0),
            Background = Brush("AccentBrush", Brushes.DodgerBlue),
            Foreground = Brush("TextOnAccentBrush", Brushes.White),
            BorderThickness = new Thickness(0),
        };
        closeButton.Click += (_, _) => Close();

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
        });
        panel.Children.Add(new TextBlock
        {
            Text = message,
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = Brush("TextSecondaryBrush", Brushes.LightGray),
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(closeButton);

        Content = new Border
        {
            Margin = new Thickness(16),
            Padding = new Thickness(22),
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("BorderBrush", Brushes.Gray),
            Background = Brush("SurfaceBrush", Brushes.Black),
            Child = panel,
        };
    }

    private Brush Brush(string resourceKey, Brush fallback)
    {
        return TryFindResource(resourceKey) as Brush ?? fallback;
    }
}
