using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Gaius.Office;

// The ribbon, its menus and every editor below it are native XAML controls.
public sealed class OfficeRibbon : Grid, IDisposable
{
    private readonly StackPanel tabs = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly Grid track = new();
    private readonly Border pill;
    private readonly TranslateTransform translation = new();
    private readonly ScrollViewer commands = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled };
    private readonly Dictionary<string, StackPanel> panels = [];
    private readonly Dictionary<string, Button> buttons = [];
    private readonly Dictionary<string, StackPanel> groups = [];
    private readonly Dictionary<string, Grid> groupContainers = [];
    private readonly Dictionary<string, RibbonTabIcon> icons = [];
    private bool normalized;
    private readonly string accent;
    private string selected = "Home";
    public MenuFlyout FileMenu { get; } = new();
    public event Action<string>? SelectionChanged;
    public StackPanel Home => Panel("Home");
    public void OrderTabs(IEnumerable<string> order)
    {
        var ordered = order.Where(buttons.ContainsKey).Select(name => buttons[name]).Concat(tabs.Children.OfType<Button>()).Distinct().ToArray(); tabs.Children.Clear(); foreach (var button in ordered) tabs.Children.Add(button);
    }
    public void OrderGroups(string tab, params string[] labels)
    {
        var panel = Panel(tab); int index = 0;
        foreach (var label in labels) if (groupContainers.TryGetValue(tab + "/" + label, out var layout)) { panel.Children.Remove(layout); panel.Children.Insert(index++, layout); }
    }
    public StackPanel Group(string tab, string label)
    {
        string key = tab + "/" + label;
        if (groups.TryGetValue(key, out var existing)) return existing;
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var layout = new Grid { Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(0, 0, 14, 0), BorderBrush = UI.Brush("#d6e0eb"), BorderThickness = new Thickness(0, 0, 1, 0) };
        layout.RowDefinitions.Add(new RowDefinition()); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(controls);
        var caption = UI.Text(label, 10, "#61738b"); caption.HorizontalAlignment = HorizontalAlignment.Center; caption.Margin = new Thickness(0, 5, 0, 0); Grid.SetRow(caption, 1); layout.Children.Add(caption);
        Panel(tab).Children.Add(layout); groups[key] = controls; groupContainers[key] = layout; return controls;
    }

    public OfficeRibbon(string color)
    {
        accent = color;
        Background = Appearance.Glass(); Margin = new Thickness(14, 6, 14, 10); CornerRadius = new CornerRadius(18); BorderBrush = UI.Brush("#d6e0eb"); BorderThickness = new Thickness(1);
        Shadow = new ThemeShadow(); Translation = new System.Numerics.Vector3(0, 0, 18);
        Appearance.Changed += UpdateGlass;
        Unloaded += (_, _) => Appearance.Changed -= UpdateGlass;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(112) });
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
        Loaded += (_, _) => { Normalize(); MovePill(false); };
        SizeChanged += (_, _) => MovePill(false);
    }
    private void UpdateGlass() { Background = Appearance.Glass(); Select(selected, false); }
    public void Dispose() => Appearance.Changed -= UpdateGlass;

    public StackPanel Panel(string name)
    {
        if (panels.TryGetValue(name, out var existing)) return existing;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Padding = new Thickness(14, 5, 14, 7), VerticalAlignment = VerticalAlignment.Stretch };
        panels[name] = panel;
        var icon = new RibbonTabIcon(name); icons[name] = icon;
        var label = new TextBlock { Text = name, FontSize = 12 };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 }; content.Children.Add(icon); content.Children.Add(label);
        var button = new Button { Content = content, Padding = new Thickness(12, 7, 12, 7), CornerRadius = new CornerRadius(18), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), MinHeight = 34 };
        AutomationProperties.SetName(button, name + " ribbon tab");
        button.Click += (_, _) => Select(name);
        button.KeyDown += (_, e) => {
            var names = tabs.Children.OfType<Button>().Select(b => buttons.First(pair => pair.Value == b).Key).ToList(); int i = names.IndexOf(selected);
            int next = e.Key == Windows.System.VirtualKey.Right ? (i + 1) % names.Count : e.Key == Windows.System.VirtualKey.Left ? (i + names.Count - 1) % names.Count : -1;
            if (next >= 0) { e.Handled = true; Select(names[next]); buttons[names[next]].Focus(FocusState.Keyboard); }
        };
        buttons[name] = button;
        if (name == "Draw") { int position = buttons.TryGetValue("Insert", out var insert) ? tabs.Children.IndexOf(insert) + 1 : 1; tabs.Children.Insert(Math.Min(position, tabs.Children.Count), button); }
        else tabs.Children.Add(button);
        button.SizeChanged += (_, _) => MovePill(false);
        if (name == selected) Select(name, false);
        return panel;
    }

    public void Select(string name, bool animate = true)
    {
        bool changed = selected != name;
        selected = name; commands.Content = panels[name];
        commands.ChangeView(0, null, null, true);
        buttons[name].StartBringIntoView();
        foreach (var (label, button) in buttons) {
            button.Foreground = label == name ? UI.DocumentBrush("#ffffff") : UI.Brush("#394b65");
            button.FontWeight = label == name ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            icons[label].SetState(label == name, button.Foreground, UI.Brush(accent));
            AutomationProperties.SetHelpText(button, label == name ? "Selected" : "");
        }
        MovePill(animate);
        if (changed) SelectionChanged?.Invoke(name);
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

    private void Normalize()
    {
        if (normalized) return; normalized = true;
        foreach (var (tab, panel) in panels.ToArray()) {
            // Older editor commands used the panel directly. Give those commands
            // the same alignment and caption baseline as explicit ribbon groups.
            var loose = panel.Children.Where(child => child is not Grid grid || !groups.Values.Any(group => group.Parent == grid)).ToArray();
            if (loose.Length > 0) {
                foreach (var child in loose) panel.Children.Remove(child);
                var group = Group(tab, tab == "Home" ? "Tools" : "Options");
                foreach (var child in loose) group.Children.Add(child);
            }
        }
        foreach (var group in groups.Values) {
            group.MinHeight = 70;
            if (group.Children.Count < 3 || group.Children.Any(child => child is not Button)) continue;
            var controls = group.Children.ToArray(); group.Children.Clear();
            for (int i = 0; i < controls.Length; i += 2) {
                var column = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
                for (int j = i; j < Math.Min(i + 2, controls.Length); j++) {
                    var button = (Button)controls[j];
                    if (button.Tag as string == "RibbonCommand" && button.Content is StackPanel content) {
                        content.Orientation = Orientation.Horizontal; content.Spacing = 6;
                        foreach (var icon in content.Children.OfType<IconElement>()) { icon.Width = icon.Height = 18; if (icon is FontIcon font) font.FontSize = 16; }
                    }
                    button.MinHeight = 32; button.Padding = new Thickness(8, 4, 8, 4); button.HorizontalAlignment = HorizontalAlignment.Stretch;
                    column.Children.Add(button);
                }
                group.Children.Add(column);
            }
        }
    }
}
