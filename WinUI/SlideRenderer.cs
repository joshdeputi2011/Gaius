using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Media.Animation;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;

namespace Gaius.Office;

public static class SlideRenderer
{
    public const double Width = 960, Height = 540;
    public static double Number(JsonNode? node, double fallback = 0) => double.TryParse(node?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : fallback;
    public static Canvas Create(JsonNode slide)
    {
        var canvas = new Canvas { Width = Width, Height = Height, Background = UI.DocumentBrush(slide["background"]?.ToString() ?? "#ffffff") };
        var title = UI.Text(slide["title"]?.ToString() ?? "", 34, "#24334a"); title.Foreground = UI.DocumentBrush("#24334a"); title.Width = 850; title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; Canvas.SetLeft(title, 55); Canvas.SetTop(title, 36); canvas.Children.Add(title);
        var body = UI.Text(slide["body"]?.ToString() ?? "", 22, "#586b83"); body.Foreground = UI.DocumentBrush("#586b83"); body.Width = 850; Canvas.SetLeft(body, 55); Canvas.SetTop(body, 106); canvas.Children.Add(body);
        foreach (var node in slide["objects"] as JsonArray ?? []) { if (node is not JsonObject obj) continue; var element = Object(obj); canvas.Children.Add(element); }
        return canvas;
    }
    public static Border Object(JsonObject obj)
    {
        string kind = obj["kind"]?.ToString() ?? "text", color = obj["color"]?.ToString() ?? "#185abd";
        var width = Math.Clamp(Number(obj["width"], 30), 1, 100) * Width / 100; var height = Math.Clamp(Number(obj["height"], 18), 1, 100) * Height / 100;
        FrameworkElement visual;
        if (kind == "image") visual = Image(obj["dataUrl"]?.ToString() ?? "");
        else if (OfficeShapes.Paths.ContainsKey(kind)) visual = OfficeShapes.Visual(kind, color, obj["stroke"]?.ToString() ?? "#185abd", Number(obj["strokeWidth"]));
        else { var text = UI.Text(obj["text"]?.ToString() ?? "", Math.Clamp(Number(obj["fontSize"], 28), 8, 160), color); text.Foreground = UI.DocumentBrush(color); text.FontWeight = kind == "wordart" ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal; visual = text; }
        var border = new Border { Width = width, Height = height, Child = visual, Tag = obj, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        border.RenderTransform = new RotateTransform { Angle = Number(obj["rotation"]), CenterX = width / 2, CenterY = height / 2 }; border.Opacity = Math.Clamp(Number(obj["opacity"], 1), 0, 1);
        if (kind == "image") { border.BorderBrush = UI.DocumentBrush(obj["stroke"]?.ToString() ?? "#185abd"); border.BorderThickness = new Thickness(Number(obj["strokeWidth"])); }
        Canvas.SetLeft(border, Number(obj["x"]) * Width / 100); Canvas.SetTop(border, Number(obj["y"]) * Height / 100); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(border, kind + " " + (obj["text"]?.ToString() ?? "")); return border;
    }
    public static Microsoft.UI.Xaml.Controls.Image Image(string data)
    {
        var bitmap = new BitmapImage(); var image = new Microsoft.UI.Xaml.Controls.Image { Source = bitmap, Stretch = Stretch.Uniform };
        _ = Load(); return image;
        async Task Load() { try { if (!data.StartsWith("data:image/", StringComparison.Ordinal)) return; using var stream = new MemoryStream(Convert.FromBase64String(data[(data.IndexOf(',') + 1)..])); await bitmap.SetSourceAsync(stream.AsRandomAccessStream()); } catch (Exception) { image.Source = null; } }
    }
    public static void Animate(Canvas canvas, JsonNode slide, JsonNode? previous)
    {
        if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled) return;
        var storyboard = new Storyboard();
        void AnimateValue(DependencyObject target, string property, double from, double to, int delay = 0) { var animation = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(420), BeginTime = TimeSpan.FromMilliseconds(delay), EnableDependentAnimation = true, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }; Storyboard.SetTarget(animation, target); Storyboard.SetTargetProperty(animation, property); storyboard.Children.Add(animation); }
        string transition = slide["transition"]?.ToString() ?? "none";
        var transform = new CompositeTransform { CenterX = Width / 2, CenterY = Height / 2 }; canvas.RenderTransform = transform;
        if (transition == "fade") AnimateValue(canvas, "Opacity", 0, 1);
        if (transition == "push") AnimateValue(transform, "TranslateX", Width, 0);
        if (transition == "zoom") { AnimateValue(transform, "ScaleX", .85, 1); AnimateValue(transform, "ScaleY", .85, 1); AnimateValue(canvas, "Opacity", 0, 1); }
        int order = 0;
        foreach (var element in canvas.Children.OfType<Border>()) {
            if (element.Tag is not JsonObject obj) continue;
            var source = (previous?["objects"] as JsonArray)?.FirstOrDefault(o => o?["id"]?.ToString() == obj["id"]?.ToString() && o?["kind"]?.ToString() == obj["kind"]?.ToString());
            if (transition == "morph" && source is not null) {
                AnimateValue(element, "(Canvas.Left)", Number(source["x"]) * Width / 100, Canvas.GetLeft(element)); AnimateValue(element, "(Canvas.Top)", Number(source["y"]) * Height / 100, Canvas.GetTop(element));
                AnimateValue(element, "Width", Number(source["width"], 30) * Width / 100, element.Width); AnimateValue(element, "Height", Number(source["height"], 18) * Height / 100, element.Height);
            } else if (obj["animation"]?.ToString() is string effect && effect != "none") {
                int delay = order++ * 120; AnimateValue(element, "Opacity", 0, 1, delay);
                var motion = new CompositeTransform { CenterX = element.Width / 2, CenterY = element.Height / 2 }; element.RenderTransform = motion;
                if (effect == "fly") AnimateValue(motion, "TranslateY", 70, 0, delay);
                if (effect == "zoom") { AnimateValue(motion, "ScaleX", .6, 1, delay); AnimateValue(motion, "ScaleY", .6, 1, delay); }
            }
        }
        storyboard.Begin();
    }
}

public sealed class PresentationWindow : Window
{
    public PresentationWindow(OfficeFile file, int initial)
    {
        Title = "Gaius Slides — Presentation"; int index = Math.Clamp(initial, 0, file.Slides.Count - 1);
        var timer = new DispatcherTimer(); var root = new Grid { Background = UI.Brush("#111827") }; root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var stage = new Viewbox { Stretch = Stretch.Uniform, Margin = new Thickness(20) }; root.Children.Add(stage);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(12) }; Grid.SetRow(controls, 1); root.Children.Add(controls);
        var count = UI.Text("", 13, "#ffffff"); var auto = new ToggleSwitch { Header = "Auto advance", Foreground = UI.Brush("#ffffff") };
        JsonNode? previous = null;
        void Render() { var current = file.Slides[index]!; var canvas = SlideRenderer.Create(current); stage.Child = canvas; canvas.Loaded += (_, _) => SlideRenderer.Animate(canvas, current, previous); count.Text = $"{index + 1} / {file.Slides.Count}"; timer.Stop(); timer.Interval = TimeSpan.FromSeconds(Math.Clamp(SlideRenderer.Number(current["duration"], 5), 1, 120)); if (auto.IsOn && index < file.Slides.Count - 1) timer.Start(); }
        void Move(int offset) { int next = Math.Clamp(index + offset, 0, file.Slides.Count - 1); if (next == index) return; previous = file.Slides[index]; index = next; Render(); }
        controls.Children.Add(UI.Button("Previous", () => Move(-1), Symbol.Back)); controls.Children.Add(count); controls.Children.Add(UI.Button("Next", () => Move(1), Symbol.Forward)); controls.Children.Add(auto); controls.Children.Add(UI.Button("End show", () => Close()));
        auto.Toggled += (_, _) => { if (auto.IsOn) timer.Start(); else timer.Stop(); }; timer.Tick += (_, _) => Move(1);
        var host = new ContentControl { Content = root, IsTabStop = true, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch }; Content = host;
        host.Loaded += (_, _) => host.Focus(FocusState.Programmatic); host.KeyDown += (_, e) => { if (e.Key is Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Space or Windows.System.VirtualKey.PageDown) Move(1); if (e.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.PageUp) Move(-1); if (e.Key == Windows.System.VirtualKey.Escape) Close(); };
        Closed += (_, _) => timer.Stop(); AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen); Render();
    }
}
