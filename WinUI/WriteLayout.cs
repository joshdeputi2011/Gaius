using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Text.Json.Nodes;

namespace Gaius.Office;

public sealed partial class EditorWindow
{
    private void ConfigureWriteLayout(RichEditBox editor, Border paper, ScrollViewer scroll)
    {
        file.Data["layout"] ??= new JsonObject(); var settings = (JsonObject)file.Data["layout"]!;
        void Apply()
        {
            bool landscape = settings["orientation"]?.ToString() == "landscape"; string size = settings["pageSize"]?.ToString() ?? "a4";
            double w = size is "letter" or "legal" ? 816 : 794, h = size == "legal" ? 1344 : size == "letter" ? 1056 : 1123;
            paper.Width = landscape ? h : w; editor.MinHeight = landscape ? w : h;
            double preset = settings["margins"]?.ToString() switch { "narrow" => 12.7, "wide" => 38.1, _ => 25.4 };
            var margins = settings["customMargins"]; double Margin(string side) => margins is null ? preset * 96 / 25.4 : SlideRenderer.Number(margins[side], preset) * 96 / 25.4;
            editor.Padding = new Thickness(Margin("left"), Margin("top"), Margin("right"), Margin("bottom"));
            paper.BorderBrush = UI.DocumentBrush(settings["borderColor"]?.ToString() ?? "#64748b"); paper.BorderThickness = new Thickness(SlideRenderer.Number(settings["borderWidth"]));
        }
        Apply();
        var ruler = new Grid { Width = paper.Width, Height = 26, Margin = new Thickness(32, 8, 32, 0), Background = UI.Brush("#f5f8fc") };
        for (int i = 0; i < 9; i++) { ruler.ColumnDefinitions.Add(new ColumnDefinition()); var tick = UI.Text(i.ToString() + " in", 10, "#64748b"); Grid.SetColumn(tick, i); ruler.Children.Add(tick); }
        var surface = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center }; scroll.Content = null; surface.Children.Add(ruler); surface.Children.Add(paper); scroll.Content = surface;
        var navigation = new ListView { Width = 170, Visibility = Visibility.Collapsed, SelectionMode = ListViewSelectionMode.Single, Header = "Document overview" };
        var host = new Grid(); host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); host.ColumnDefinitions.Add(new ColumnDefinition()); body.Children.Remove(scroll); host.Children.Add(navigation); Grid.SetColumn(scroll, 1); host.Children.Add(scroll); body.Children.Add(host);
        int outlineGeneration = 0;
        async void Outline() {
            int generation = ++outlineGeneration;
            navigation.Items.Clear();
            if (paper.IsLoaded && paper.ActualWidth > 0) { try { var bitmap = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap(); await bitmap.RenderAsync(paper, 140, (int)Math.Clamp(paper.ActualHeight / paper.ActualWidth * 140, 1, 2000)); if (generation != outlineGeneration || navigation.Visibility != Visibility.Visible) return; navigation.Items.Add(new ListViewItem { Content = new Microsoft.UI.Xaml.Controls.Image { Source = bitmap, Width = 140, MaxHeight = 200, Stretch = Stretch.Uniform }, Tag = -1 }); } catch (Exception) { status.Text = "Thumbnail unavailable; navigation remains available"; } }
            editor.Document.GetText(Microsoft.UI.Text.TextGetOptions.None, out var text); int index = 0; foreach (string paragraph in text.Split('\r').Where(p => !string.IsNullOrWhiteSpace(p)).Take(100)) navigation.Items.Add(new ListViewItem { Content = UI.Text(paragraph.Length > 45 ? paragraph[..45] + "…" : paragraph, 11), Tag = index++ });
        }
        navigation.SelectionChanged += (_, _) => { if (navigation.SelectedItem is not ListViewItem item || item.Tag is not int selected) return; if (selected < 0) { scroll.ChangeView(null, 0, null); return; } editor.Document.GetText(Microsoft.UI.Text.TextGetOptions.None, out var text); var paragraphs = text.Split('\r'); int position = 0, at = 0; foreach (var p in paragraphs) { if (!string.IsNullOrWhiteSpace(p) && at++ == selected) break; position += p.Length + 1; } editor.Document.Selection.SetRange(position, position); editor.Focus(FocusState.Programmatic); };
        Command("View", "Rulers", () => ruler.Visibility = ruler.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible);
        Command("View", "Thumbnails / navigation", () => { navigation.Visibility = navigation.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; if (navigation.Visibility == Visibility.Visible) Outline(); });
        Command("View", "Reading view", () => { editor.IsReadOnly = !editor.IsReadOnly; status.Text = editor.IsReadOnly ? "Reading view" : "Editing"; });
        Command("View", "Page width", () => scroll.ChangeView(null, null, (float)Math.Clamp((scroll.ActualWidth - 64) / paper.Width, .25, 4)));
        Command("View", "Print layout", () => { Apply(); ruler.Width = paper.Width; scroll.ChangeView(null, null, 1); editor.IsReadOnly = false; });
        Command("View", "Web layout", () => { paper.Width = Math.Max(400, scroll.ActualWidth - 64); ruler.Width = paper.Width; editor.MinHeight = 700; });
        Canvas? grid = null;
        Command("View", "Gridlines", () => {
            if (paper.Child is not Grid page) return;
            if (grid is not null) { page.Children.Remove(grid); grid = null; return; }
            grid = new Canvas { IsHitTestVisible = false };
            for (double x = editor.Padding.Left; x < paper.Width - editor.Padding.Right; x += 24) grid.Children.Add(new Line { X1 = x, X2 = x, Y1 = editor.Padding.Top, Y2 = editor.MinHeight - editor.Padding.Bottom, Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(35, 100, 125, 160)), StrokeThickness = 1 });
            for (double y = editor.Padding.Top; y < editor.MinHeight - editor.Padding.Bottom; y += 24) grid.Children.Add(new Line { X1 = editor.Padding.Left, X2 = paper.Width - editor.Padding.Right, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(35, 100, 125, 160)), StrokeThickness = 1 });
            page.Children.Insert(0, grid);
        });
        var sizes = new ComboBox { Header = "Paper size", ItemsSource = new[] { "a4", "letter", "legal" }, SelectedItem = settings["pageSize"]?.ToString() ?? "a4" };
        sizes.SelectionChanged += (_, _) => { settings["pageSize"] = sizes.SelectedItem?.ToString(); Apply(); ruler.Width = paper.Width; Changed(); }; ribbon.Group("Layout", "Page setup").Children.Add(sizes);
        Command("Layout", "Margins", async () => {
            var panel = new StackPanel { Spacing = 12, Width = 320 }; var fields = new Dictionary<string, NumberBox>();
            foreach (string side in new[] { "top", "right", "bottom", "left" }) { var input = new NumberBox { Header = OfficeShapes.Label(side) + " (mm)", Minimum = 0, Maximum = 80, Value = SlideRenderer.Number(settings["customMargins"]?[side], 25.4), SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline }; fields[side] = input; panel.Children.Add(input); }
            if (await WindowLifetime.ShowDialog(this, new ContentDialog { Title = "Page margins", Content = panel, PrimaryButtonText = "Apply", CloseButtonText = "Cancel" }) != ContentDialogResult.Primary) return;
            if (fields.Values.Any(field => !double.IsFinite(field.Value))) return; var margins = new JsonObject(); foreach (var (side, field) in fields) margins[side] = Math.Clamp(field.Value, 0, 80); settings["customMargins"] = margins; Apply(); Changed();
        });
        var columns = new ComboBox { Header = "Columns", ItemsSource = new[] { 1, 2, 3, 4 }, SelectedItem = (int)SlideRenderer.Number(settings["columns"], 1) };
        columns.SelectionChanged += (_, _) => { settings["columns"] = (int)columns.SelectedItem; Changed(); }; ribbon.Group("Layout", "Page setup").Children.Add(columns);
        Command("Layout", "Column preview", async () => {
            flushEditor(); var preview = new DocumentPreview(file.Content["html"]!.ToString()); await preview.LoadImages(); int count = (int)SlideRenderer.Number(settings["columns"], 1);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 }; var first = preview.Create((650 - 24 * (count - 1)) / count); first.Width = (650 - 24 * (count - 1)) / count; first.Height = 600; row.Children.Add(first); RichTextBlockOverflow? previous = null;
            for (int i = 1; i < count; i++) { var overflow = new RichTextBlockOverflow { Width = first.Width, Height = first.Height }; if (previous is null) first.OverflowContentTarget = overflow; else previous.OverflowContentTarget = overflow; row.Children.Add(overflow); previous = overflow; }
            await WindowLifetime.ShowDialog(this, new ContentDialog { Title = "First page column preview", Content = new ScrollViewer { Content = row, MaxHeight = 600, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }, CloseButtonText = "Close", MinWidth = 700 });
        });
        Command("Design", "Page border", () => { settings["borderWidth"] = SlideRenderer.Number(settings["borderWidth"]) == 0 ? 1 : 0; settings["borderColor"] = "#64748b"; Apply(); Changed(); });
    }
}
