using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
namespace Gaius.Office;
public static class UI
{
    public static readonly string[] Kinds = ["document", "spreadsheet", "presentation", "image", "pdf"];
    public static readonly Dictionary<string, string> Labels = new() { ["document"] = "Write", ["spreadsheet"] = "Sheets", ["presentation"] = "Slides", ["image"] = "Image", ["pdf"] = "PDF" };
    public static Color Color(string hex) => Windows.UI.Color.FromArgb(255, Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16));
    public static SolidColorBrush Brush(string hex) => Appearance.Brush(hex);
    public static SolidColorBrush DocumentBrush(string hex) => new(Color(hex));
    public static string Accent(string kind) => kind switch { "spreadsheet" => "#107c41", "presentation" => "#c43e1c", "image" => "#744da9", "pdf" => "#c42b32", _ => "#185abd" };
    // Segoe Fluent Icons ships with Windows App SDK through SymbolIcon/FontIcon.
    public static string Glyph(string kind) => kind switch { "spreadsheet" => "\uE80A", "presentation" => "\uE8A5", "image" => "\uEB9F", "pdf" => "\uEA90", _ => "\uE8A5" };
    public static TextBlock Text(string text, double size = 14, string color = "#242424") => new() { Text = text, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap };
    public static Button Button(string text, Action action, Symbol? icon = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (icon.HasValue) row.Children.Add(new SymbolIcon(icon.Value) { Width = 18, Height = 18 });
        row.Children.Add(Text(text, 12)); var button = new Button { Content = row, Padding = new Thickness(12, 7, 12, 7) }; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, text); button.Click += (_, _) => action(); return button;
    }
    public static Button RibbonButton(string text, Action action, Symbol? icon = null)
    {
        var content = new StackPanel { Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center };
        if (icon.HasValue) content.Children.Add(new SymbolIcon(icon.Value) { Width = 22, Height = 22, Foreground = Brush("#3b5d88") });
        else content.Children.Add(new FontIcon { Glyph = OfficeGlyphs.ForLabel(text), FontSize = 22, Foreground = Brush("#3b5d88") });
        content.Children.Add(Text(text, 11));
        var button = new Button { Content = content, Tag = "RibbonCommand", Padding = new Thickness(10, 6, 10, 6), MinHeight = 62, MinWidth = 54, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(48, 255, 255, 255)), BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(72, 255, 255, 255)) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, text); ToolTipService.SetToolTip(button, text); button.Click += (_, _) => action(); return button;
    }
    public static Button CompactButton(string text, Action action, Symbol symbol)
    {
        var button = new Button { Content = new SymbolIcon(symbol) { Width = 16, Height = 16 }, Padding = new Thickness(6), MinWidth = 30, MinHeight = 30, Background = Brush("#f5f8fc"), BorderBrush = Brush("#e2e8f0"), CornerRadius = new CornerRadius(5) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, text); ToolTipService.SetToolTip(button, text); button.Click += (_, _) => action(); return button;
    }
    public static DropDownButton ColorButton(string label, Action<Windows.UI.Color> changed)
    {
        var panel = new StackPanel { Spacing = 12, Width = 280 };
        var colors = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        for (int i = 0; i < 6; i++) colors.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < 2; i++) colors.RowDefinitions.Add(new RowDefinition());
        var flyout = new Flyout { Content = panel };
        string[] palette = ["#242424", "#ffffff", "#185abd", "#107c41", "#c43e1c", "#744da9", "#dce8fa", "#dff1e6", "#fff1ba", "#ffd8d2", "#f1dffc", "#64748b"];
        for (int i = 0; i < palette.Length; i++) { var hex = palette[i]; var swatch = new Button { Background = DocumentBrush(hex), Width = 34, Height = 30, BorderBrush = Brush("#c9d3e1"), CornerRadius = new CornerRadius(6) }; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(swatch, hex); swatch.Click += (_, _) => { changed(Color(hex)); flyout.Hide(); }; Grid.SetColumn(swatch, i % 6); Grid.SetRow(swatch, i / 6); colors.Children.Add(swatch); }
        panel.Children.Add(colors);
        var picker = new ColorPicker { IsAlphaEnabled = false, IsColorSliderVisible = true, IsColorChannelTextInputVisible = false, IsHexInputVisible = true, IsMoreButtonVisible = false, MinWidth = 0, Width = 280, Color = Color("#185abd") }; panel.Children.Add(picker);
        panel.Children.Add(Button("Apply", () => { changed(picker.Color); flyout.Hide(); }));
        var button = new DropDownButton { Content = label, Flyout = flyout, CornerRadius = new CornerRadius(8), Background = Brush("#f5f8fc"), BorderBrush = Brush("#ffffff") }; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label); return button;
    }
    public static async Task Error(Window window, Exception error)
    {
        if (WindowLifetime.IsClosed(window)) return;
        var dialog = new ContentDialog { Title = "Gaius Office", Content = error.Message, CloseButtonText = "Close" };
        await WindowLifetime.ShowDialog(window, dialog);
    }
    public static async Task<StorageFile?> PickOpen(Window window, params string[] extensions)
    {
        if (WindowLifetime.IsClosed(window)) return null;
        var picker = new FileOpenPicker(); foreach (var extension in extensions) picker.FileTypeFilter.Add(extension);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window)); return await picker.PickSingleFileAsync();
    }
    public static async Task<StorageFile?> PickSave(Window window, string name, string extension)
    {
        if (WindowLifetime.IsClosed(window)) return null;
        var picker = new FileSavePicker { SuggestedFileName = name }; picker.FileTypeChoices.Add("Gaius export", new List<string> { extension });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window)); return await picker.PickSaveFileAsync();
    }
}
