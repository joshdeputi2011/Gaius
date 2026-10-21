using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Gaius.Office;

// The ribbon, its menus and every editor below it are native XAML controls.
public sealed class OfficeRibbon : Grid
{
    private readonly StackPanel tabs = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly Grid track = new();
    private readonly Border pill;
    private readonly TranslateTransform translation = new();
    private readonly ScrollViewer commands = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled };
    private readonly Dictionary<string, StackPanel> panels = [];
    private readonly Dictionary<string, Button> buttons = [];
    private readonly string accent;
    private string selected = "Home";
    public MenuFlyout FileMenu { get; } = new();
    public StackPanel Home => Panel("Home");

    public OfficeRibbon(string color)
    {
        accent = color;
        Background = UI.Brush("#eef2f8");
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(94) });
        var row = new Grid { Padding = new Thickness(12, 4, 12, 4), ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var file = new DropDownButton { Content = "File", Flyout = FileMenu, Background = UI.Brush("#f7f9fc"), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(8) };
        AutomationProperties.SetName(file, "File");
        row.Children.Add(file);
        pill = new Border { Background = UI.Brush(accent), CornerRadius = new CornerRadius(18), HorizontalAlignment = HorizontalAlignment.Left, Height = 34, RenderTransform = translation, IsHitTestVisible = false };
        track.Children.Add(pill); track.Children.Add(tabs);
        var scroll = new ScrollViewer { Content = track, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroll, 1); row.Children.Add(scroll); Children.Add(row);
        Grid.SetRow(commands, 1); Children.Add(commands);
        Loaded += (_, _) => MovePill(false);
        SizeChanged += (_, _) => MovePill(false);
    }

    public StackPanel Panel(string name)
    {
        if (panels.TryGetValue(name, out var existing)) return existing;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Padding = new Thickness(16, 14, 16, 14), VerticalAlignment = VerticalAlignment.Center };
        panels[name] = panel;
        var button = new Button { Content = name, Padding = new Thickness(16, 7, 16, 7), CornerRadius = new CornerRadius(18), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), MinHeight = 34 };
        AutomationProperties.SetName(button, name + " ribbon tab");
        button.Click += (_, _) => Select(name);
        button.KeyDown += (_, e) => {
            var names = buttons.Keys.ToList(); int i = names.IndexOf(selected);
            int next = e.Key == Windows.System.VirtualKey.Right ? (i + 1) % names.Count : e.Key == Windows.System.VirtualKey.Left ? (i + names.Count - 1) % names.Count : -1;
            if (next >= 0) { e.Handled = true; Select(names[next]); buttons[names[next]].Focus(FocusState.Keyboard); }
        };
        buttons[name] = button; tabs.Children.Add(button);
        button.SizeChanged += (_, _) => MovePill(false);
        if (name == selected) Select(name, false);
        return panel;
    }

    public void Select(string name, bool animate = true)
    {
        selected = name; commands.Content = panels[name];
        foreach (var (label, button) in buttons) {
            button.Foreground = UI.Brush(label == name ? "#ffffff" : "#394b65");
            button.FontWeight = label == name ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            AutomationProperties.SetHelpText(button, label == name ? "Selected" : "");
        }
        MovePill(animate);
    }

    private void MovePill(bool animate)
    {
        if (!buttons.TryGetValue(selected, out var button) || button.ActualWidth == 0) return;
        double x = button.TransformToVisual(track).TransformPoint(new Windows.Foundation.Point()).X;
        bool motion = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        if (!animate || !motion) { translation.X = x; pill.Width = button.ActualWidth; return; }
        var storyboard = new Storyboard();
        var move = new DoubleAnimation { To = x, Duration = TimeSpan.FromMilliseconds(220), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(move, translation); Storyboard.SetTargetProperty(move, "X"); storyboard.Children.Add(move);
        // Width stays on the UI thread; the position uses the compositor.
        pill.Width = button.ActualWidth; storyboard.Begin();
    }

    public void Menu(string label, Action action, Symbol? icon = null)
    {
        var item = new MenuFlyoutItem { Text = label };
        if (icon.HasValue) item.Icon = new SymbolIcon(icon.Value);
        item.Click += (_, _) => action(); FileMenu.Items.Add(item);
    }
}
