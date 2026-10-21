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
    public static SolidColorBrush Brush(string hex) => new(Color(hex));
    public static string Accent(string kind) => kind switch { "spreadsheet" => "#107c41", "presentation" => "#c43e1c", "image" => "#744da9", "pdf" => "#c42b32", _ => "#185abd" };
    // Segoe Fluent Icons ships with Windows App SDK through SymbolIcon/FontIcon.
    public static string Glyph(string kind) => kind switch { "spreadsheet" => "\uE80A", "presentation" => "\uE8A5", "image" => "\uEB9F", "pdf" => "\uEA90", _ => "\uE8A5" };
    public static TextBlock Text(string text, double size = 14, string color = "#242424") => new() { Text = text, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap };
    public static Button Button(string text, Action action, Symbol? icon = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (icon.HasValue) row.Children.Add(new SymbolIcon(icon.Value) { Width = 18, Height = 18 });
        row.Children.Add(Text(text, 12)); var button = new Button { Content = row, Padding = new Thickness(12, 7, 12, 7) }; button.Click += (_, _) => action(); return button;
    }
    public static async Task Error(Window window, Exception error)
    {
        var root = window.Content.XamlRoot; if (root is null) return;
        var dialog = new ContentDialog { XamlRoot = root, Title = "Gaius Office", Content = error.Message, CloseButtonText = "Close" };
        await dialog.ShowAsync();
    }
    public static async Task<StorageFile?> PickOpen(Window window, params string[] extensions)
    {
        var picker = new FileOpenPicker(); foreach (var extension in extensions) picker.FileTypeFilter.Add(extension);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window)); return await picker.PickSingleFileAsync();
    }
    public static async Task<StorageFile?> PickSave(Window window, string name, string extension)
    {
        var picker = new FileSavePicker { SuggestedFileName = name }; picker.FileTypeChoices.Add("Gaius export", new List<string> { extension });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(window)); return await picker.PickSaveFileAsync();
    }
}
