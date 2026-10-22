using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.DataTransfer;

namespace Gaius.Office;

public sealed partial class EditorWindow
{
    private void BuildNativeSheet()
    {
        foreach (var tab in new[] { "Home", "Insert", "Page Layout", "Formulas", "Data", "Review", "View" }) ribbon.Panel(tab);
        string selected = "A1"; bool refreshing = false;
        JsonObject Cells() => (JsonObject)file.Sheet["cells"]!;
        JsonObject Styles() { file.Sheet["styles"] ??= new JsonObject(); return (JsonObject)file.Sheet["styles"]!; }
        int Columns() => Math.Clamp(file.Sheet["columns"]?.GetValue<int>() ?? 26, 1, 256);
        int Rows() => Math.Clamp(file.Sheet["rows"]?.GetValue<int>() ?? 100, 1, 10000);
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var bar = new Grid { Padding = new Thickness(12, 6, 12, 6), ColumnSpacing = 12 }; bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) }); bar.ColumnDefinitions.Add(new ColumnDefinition());
        var range = new TextBox { Text = "A1", PlaceholderText = "A1:C10" }; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(range, "Selected range");
        var formula = new TextBox { PlaceholderText = "Value or =SUM(A1:A10)" }; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(formula, "Formula"); Grid.SetColumn(formula, 1); bar.Children.Add(range); bar.Children.Add(formula); root.Children.Add(bar);
        var grid = new Grid { Background = Appearance.PaperBrush }; grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) }); grid.RowDefinitions.Add(new RowDefinition());
        var headings = new StackPanel { Orientation = Orientation.Horizontal }; grid.Children.Add(headings);
        var rows = new ListView { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = false, Padding = new Thickness(0), BorderThickness = new Thickness(0) }; Grid.SetRow(rows, 1); grid.Children.Add(rows);
        rows.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Orientation='Horizontal'/></DataTemplate>");
        var scroller = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollMode = ScrollMode.Disabled }; Grid.SetRow(scroller, 1); root.Children.Add(scroller);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Padding = new Thickness(12, 6, 12, 6) }; var tabScroll = new ScrollViewer { Content = tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled }; Grid.SetRow(tabScroll, 2); root.Children.Add(tabScroll); body.Children.Add(root);
        var visible = new Dictionary<string, TextBox>();
        var visibleRows = new Dictionary<int, StackPanel>();
        var gripTemplate = (ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Border Background='{TemplateBinding Background}'/></ControlTemplate>");
        double ColumnWidth(int column) => SheetDimensions.ColumnWidth(file.Sheet, column);
        double RowHeight(int row) => SheetDimensions.RowHeight(file.Sheet, row);
        void ResizeColumn(int column, double width)
        {
            SheetDimensions.SetColumnWidth(file.Sheet, column, width);
            if (headings.Children.Count > column + 1 && headings.Children[column + 1] is FrameworkElement heading) heading.Width = ColumnWidth(column);
            foreach (var (address, box) in visible) if (SheetData.Position(address).Column == column) box.Width = ColumnWidth(column);
            grid.Width = 48 + Enumerable.Range(0, Columns()).Sum(ColumnWidth) + 24;
        }
        void ResizeRow(int row, double height)
        {
            SheetDimensions.SetRowHeight(file.Sheet, row, height);
            if (visibleRows.TryGetValue(row, out var line)) { line.Height = RowHeight(row); foreach (var child in line.Children.OfType<FrameworkElement>()) child.Height = RowHeight(row); }
        }
        void AutoFitColumn(int column)
        {
            var measure = new TextBlock { FontSize = 12 }; double width = 40;
            foreach (var pair in Cells().Where(p => SheetData.Position(p.Key).Column == column).Take(1000)) { measure.Text = SheetData.Display(file.Sheet, pair.Key); measure.Measure(new Windows.Foundation.Size(1180, 600)); width = Math.Max(width, measure.DesiredSize.Width + 18); }
            ResizeColumn(column, width); Changed();
        }
        void AutoFitRow(int row)
        {
            var measure = new TextBlock { FontSize = 12 }; double height = SheetDimensions.DefaultRowHeight;
            for (int column = 0; column < Columns(); column++) {
                string address = OfficeFile.Column(column) + row; measure.Text = SheetData.Display(file.Sheet, address); measure.TextWrapping = Styles()[address]?["wrap"]?.GetValue<bool>() == true ? TextWrapping.Wrap : TextWrapping.NoWrap;
                measure.Measure(new Windows.Foundation.Size(Math.Max(16, ColumnWidth(column) - 12), double.PositiveInfinity)); height = Math.Max(height, measure.DesiredSize.Height + 12);
            }
            ResizeRow(row, height); Changed();
        }
        Thumb Grip(string name, bool horizontal, Action<double> drag, Action complete)
        {
            var thumb = new Thumb { Width = horizontal ? 8 : double.NaN, Height = horizontal ? double.NaN : 8, HorizontalAlignment = horizontal ? HorizontalAlignment.Right : HorizontalAlignment.Stretch, VerticalAlignment = horizontal ? VerticalAlignment.Stretch : VerticalAlignment.Bottom, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), IsTabStop = true };
            thumb.Template = gripTemplate;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(thumb, name); ToolTipService.SetToolTip(thumb, "Drag to resize. Double-click to fit.");
            thumb.KeyDown += (_, e) => { double delta = e.Key == (horizontal ? Windows.System.VirtualKey.Left : Windows.System.VirtualKey.Up) ? -4 : e.Key == (horizontal ? Windows.System.VirtualKey.Right : Windows.System.VirtualKey.Down) ? 4 : 0; if (delta == 0) return; drag(delta); complete(); e.Handled = true; };
            thumb.DragDelta += (_, e) => drag(horizontal ? e.HorizontalChange : e.VerticalChange); thumb.DragCompleted += (_, _) => complete(); return thumb;
        }
        void Paint(TextBox box, string address)
        {
            var style = Styles()[address]; box.FontWeight = style?["bold"]?.GetValue<bool>() == true ? FontWeights.Bold : FontWeights.Normal;
            box.FontStyle = style?["italic"]?.GetValue<bool>() == true ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
            box.Foreground = style?["color"] is JsonNode color ? UI.DocumentBrush(color.ToString()) : Appearance.PaperText; box.Background = style?["background"] is JsonNode background ? UI.DocumentBrush(background.ToString()) : Appearance.PaperBrush;
            box.TextAlignment = style?["align"]?.ToString() switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, _ => TextAlignment.Left };
            box.TextWrapping = style?["wrap"]?.GetValue<bool>() == true ? TextWrapping.Wrap : TextWrapping.NoWrap;
            box.BorderBrush = UI.Brush(address == selected ? "#107c41" : "#e1e3e7");
        }
        void Refresh()
        {
            refreshing = true;
            foreach (var (address, box) in visible) { box.Text = address == selected ? Cells()[address]?.ToString() ?? "" : SheetData.Display(file.Sheet, address); Paint(box, address); }
            refreshing = false;
        }
        void ThemeSheet() { grid.Background = Appearance.PaperBrush; Refresh(); }
        Appearance.Changed += ThemeSheet; Closed += (_, _) => Appearance.Changed -= ThemeSheet;
        void Select(string address)
        {
            selected = address; details.Text = address; range.Text = address; formula.Text = Cells()[address]?.ToString() ?? ""; Refresh();
        }
        void RebuildRows()
        {
            visible.Clear(); visibleRows.Clear(); grid.Width = 48 + Enumerable.Range(0, Columns()).Sum(ColumnWidth) + 24;
            headings.Children.Clear(); headings.Children.Add(new Border { Width = 48 });
            for (int c = 0; c < Columns(); c++) {
                int headerColumn = c; var heading = new Grid { Width = ColumnWidth(headerColumn), Background = UI.Brush("#f4f7fb") };
                heading.Children.Add(new Border { BorderBrush = UI.Brush("#d4dce7"), BorderThickness = new Thickness(0, 0, 1, 1), Child = new TextBlock { Text = OfficeFile.Column(c), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = UI.Brush("#586b83") } });
                var grip = Grip("Resize column " + OfficeFile.Column(c), true, delta => ResizeColumn(headerColumn, ColumnWidth(headerColumn) + delta), Changed); grip.DoubleTapped += (_, e) => { AutoFitColumn(headerColumn); e.Handled = true; }; heading.Children.Add(grip); headings.Children.Add(heading);
            }
            var filter = file.Sheet["filter"]; string column = filter?["column"]?.ToString() ?? "A", query = filter?["value"]?.ToString() ?? "";
            rows.ItemsSource = Enumerable.Range(1, Rows()).Where(r => r == 1 || string.IsNullOrEmpty(query) || SheetData.Display(file.Sheet, column + r).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        rows.ContainerContentChanging += (_, args) => {
            if (args.ItemContainer.ContentTemplateRoot is StackPanel old) { foreach (var box in old.Children.OfType<TextBox>()) if (box.Tag is string key && visible.GetValueOrDefault(key) == box) visible.Remove(key); if (old.Tag is int oldRow && visibleRows.GetValueOrDefault(oldRow) == old) visibleRows.Remove(oldRow); }
            if (args.InRecycleQueue || args.Item is not int row) return;
            if (args.ItemContainer.ContentTemplateRoot is not StackPanel line) return;
            line.Children.Clear(); line.Tag = row; line.Height = RowHeight(row); visibleRows[row] = line;
            var rowHeading = new Grid { Width = 48, Height = RowHeight(row), Background = UI.Brush("#f4f7fb") };
            rowHeading.Children.Add(new Border { BorderBrush = UI.Brush("#d4dce7"), BorderThickness = new Thickness(0, 0, 1, 1), Child = new TextBlock { Text = row.ToString(), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
            var rowGrip = Grip("Resize row " + row, false, delta => ResizeRow(row, RowHeight(row) + delta), Changed); rowGrip.DoubleTapped += (_, e) => { AutoFitRow(row); e.Handled = true; }; rowHeading.Children.Add(rowGrip); line.Children.Add(rowHeading);
            for (int c = 0; c < Columns(); c++) {
                string address = OfficeFile.Column(c) + row;
                var box = new TextBox { Tag = address, Width = ColumnWidth(c), MinHeight = 20, Height = RowHeight(row), Padding = new Thickness(6, 3, 6, 3), FontSize = 12, BorderThickness = new Thickness(0, 0, 1, 1), CornerRadius = new CornerRadius(0), Text = address == selected ? Cells()[address]?.ToString() ?? "" : SheetData.Display(file.Sheet, address) };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, address); Paint(box, address);
                box.GotFocus += (_, _) => Select(address);
                box.TextChanged += (_, _) => { if (refreshing || box.FocusState == FocusState.Unfocused) return; Cells()[address] = box.Text; formula.Text = box.Text; Changed(); };
                box.LostFocus += (_, _) => Refresh(); visible[address] = box; line.Children.Add(box);
            }
            args.ItemContainer.Padding = new Thickness(0); args.ItemContainer.Margin = new Thickness(0); args.ItemContainer.MinHeight = 20; args.ItemContainer.HorizontalContentAlignment = HorizontalAlignment.Stretch; args.Handled = true;
        };
        IEnumerable<string> Selection() => SheetData.Range(range.Text, Rows(), Columns());
        void Format(string key, JsonNode value) { try { foreach (var address in Selection()) { Styles()[address] ??= new JsonObject(); Styles()[address]![key] = value.DeepClone(); } Changed(); Refresh(); } catch (Exception e) { _ = UI.Error(this, e); } }
        void Set(string value) { Cells()[selected] = value; formula.Text = value; Changed(); Refresh(); }
        formula.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { Set(formula.Text); e.Handled = true; } };
        range.KeyDown += (_, e) => { if (e.Key != Windows.System.VirtualKey.Enter) return; try { var address = Selection().First(); selected = address; formula.Text = Cells()[address]?.ToString() ?? ""; int row = SheetData.Position(address).Row; rows.ScrollIntoView(row); Refresh(); } catch (Exception error) { _ = UI.Error(this, error); } };
        var font = ribbon.Group("Home", "Font"); font.Children.Add(UI.RibbonButton("Bold", () => Format("bold", JsonValue.Create(!(Styles()[selected]?["bold"]?.GetValue<bool>() ?? false))!), Symbol.Bold)); font.Children.Add(UI.RibbonButton("Italic", () => Format("italic", JsonValue.Create(!(Styles()[selected]?["italic"]?.GetValue<bool>() ?? false))!), Symbol.Italic));
        foreach (var (label, key) in new[] { ("Text color", "color"), ("Fill color", "background") }) font.Children.Add(UI.ColorButton(label, color => Format(key, JsonValue.Create($"#{color.R:x2}{color.G:x2}{color.B:x2}")!)));
        var formats = new ComboBox { ItemsSource = new[] { "general", "number", "currency", "percent" }, SelectedIndex = 0, Width = 125 }; formats.SelectionChanged += (_, _) => { if (formats.SelectedItem is string value) Format("format", JsonValue.Create(value)!); }; ribbon.Group("Home", "Number").Children.Add(formats);
        foreach (var align in new[] { "left", "center", "right" }) { string value = align; ribbon.Group("Home", "Alignment").Children.Add(UI.RibbonButton(char.ToUpper(value[0]) + value[1..], () => Format("align", JsonValue.Create(value)!))); }
        ribbon.Group("Home", "Alignment").Children.Add(UI.RibbonButton("Wrap text", () => Format("wrap", JsonValue.Create(!(Styles()[selected]?["wrap"]?.GetValue<bool>() ?? false))!)));
        var sizing = ribbon.Group("Home", "Cells");
        sizing.Children.Add(UI.RibbonButton("Column width", async () => {
            var input = new NumberBox { Header = "Width in pixels", Value = ColumnWidth(SheetData.Position(selected).Column), Minimum = 32, Maximum = 1200, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, MinWidth = 260 };
            var dialog = new ContentDialog { Title = "Column width", Content = input, PrimaryButtonText = "Apply", CloseButtonText = "Cancel" };
            if (await WindowLifetime.ShowDialog(this, dialog) == ContentDialogResult.Primary) { foreach (int column in Selection().Select(SheetData.Position).Select(p => p.Column).Distinct()) ResizeColumn(column, input.Value); Changed(); }
        }));
        sizing.Children.Add(UI.RibbonButton("Row height", async () => {
            var input = new NumberBox { Header = "Height in pixels", Value = RowHeight(SheetData.Position(selected).Row), Minimum = 20, Maximum = 600, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, MinWidth = 260 };
            var dialog = new ContentDialog { Title = "Row height", Content = input, PrimaryButtonText = "Apply", CloseButtonText = "Cancel" };
            if (await WindowLifetime.ShowDialog(this, dialog) == ContentDialogResult.Primary) { foreach (int row in Selection().Select(SheetData.Position).Select(p => p.Row).Distinct()) ResizeRow(row, input.Value); Changed(); }
        }));
        sizing.Children.Add(UI.RibbonButton("AutoFit columns", () => { foreach (int column in Selection().Select(SheetData.Position).Select(p => p.Column).Distinct()) AutoFitColumn(column); }));
        sizing.Children.Add(UI.RibbonButton("AutoFit rows", () => { foreach (int row in Selection().Select(SheetData.Position).Select(p => p.Row).Distinct()) AutoFitRow(row); }));
        Command("Home", "Clear", () => { try { foreach (var address in Selection()) Cells().Remove(address); Changed(); Refresh(); } catch (Exception e) { _ = UI.Error(this, e); } }, Symbol.Clear);
        Command("Home", "Copy", () => { try { var positions = Selection().Select(SheetData.Position).ToArray(); var text = string.Join("\n", positions.GroupBy(p => p.Row).Select(group => string.Join("\t", group.Select(p => SheetData.Display(file.Sheet, OfficeFile.Column(p.Column) + p.Row))))); var data = new DataPackage(); data.SetText(text); Clipboard.SetContent(data); } catch (Exception e) { _ = UI.Error(this, e); } }, Symbol.Copy);
        Command("Home", "Paste", async () => { try { var data = Clipboard.GetContent(); if (!data.Contains(StandardDataFormats.Text)) return; var lines = (await data.GetTextAsync()).Replace("\r", "").TrimEnd('\n').Split('\n'); var start = SheetData.Position(selected); for (int r = 0; r < lines.Length && start.Row + r <= Rows(); r++) { var values = lines[r].Split('\t'); for (int c = 0; c < values.Length && start.Column + c < Columns(); c++) Cells()[OfficeFile.Column(start.Column + c) + (start.Row + r)] = values[c]; } Changed(); Refresh(); } catch (Exception e) { await UI.Error(this, e); } }, Symbol.Paste);
        void RenderTabs()
        {
            tabs.Children.Clear(); var worksheets = file.Data["worksheets"] as JsonArray;
            int count = worksheets?.Count ?? 1, active = file.Data["activeSheet"]?.GetValue<int>() ?? 0;
            for (int i = 0; i < count; i++) { int index = i; var button = UI.Button(worksheets?[i]?["name"]?.ToString() ?? file.Sheet["name"]?.ToString() ?? "Sheet 1", () => { file.Data["activeSheet"] = index; selected = "A1"; range.Text = "A1"; formula.Text = Cells()[selected]?.ToString() ?? ""; Changed(); RebuildRows(); RenderTabs(); }); if (i == active) button.Background = UI.Brush("#dff1e6"); tabs.Children.Add(button); }
            tabs.Children.Add(UI.Button("Add sheet", () => { if (file.Data["worksheets"] is not JsonArray) { var first = file.Sheet.DeepClone(); file.Data["worksheets"] = new JsonArray(first); } var sheets = (JsonArray)file.Data["worksheets"]!; sheets.Add(new JsonObject { ["name"] = "Sheet " + (sheets.Count + 1), ["cells"] = new JsonObject(), ["rows"] = 100, ["columns"] = 26 }); file.Data["activeSheet"] = sheets.Count - 1; selected = "A1"; range.Text = "A1"; formula.Text = ""; Changed(); RebuildRows(); RenderTabs(); }, Symbol.Add));
        }
        Command("Insert", "Rename sheet", async () => { var name = await Prompt("Sheet name", file.Sheet["name"]?.ToString() ?? "Sheet 1"); if (!string.IsNullOrWhiteSpace(name)) { file.Sheet["name"] = name.Trim(); Changed(); RenderTabs(); } });
        Command("Insert", "Add rows", () => { file.Sheet["rows"] = Math.Min(10000, Rows() + 100); Changed(); RebuildRows(); });
        Command("Insert", "Add columns", () => { file.Sheet["columns"] = Math.Min(256, Columns() + 10); Changed(); RebuildRows(); });
        Command("Insert", "Chart", async () => {
            try {
                var source = new TextBox { Header = "Range (labels or X values, then numeric series)", Text = range.Text.Contains(':') ? range.Text : "A1:B6" }; var title = new TextBox { Header = "Title", Text = "Chart" }; var kind = new ComboBox { Header = "Type", ItemsSource = SheetChartModel.Types.Select(t => t.Label).ToArray(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
                var panel = new StackPanel { Spacing = 12, MinWidth = 320 }; panel.Children.Add(source); panel.Children.Add(title); panel.Children.Add(kind);
                panel.Children.Add(UI.Text("Up to 200 data rows and 12 series. Use a header row to name each series.", 12, "#66758a"));
                var dialog = new ContentDialog { Title = "Insert chart", Content = panel, PrimaryButtonText = "Insert", CloseButtonText = "Cancel" };
                if (await WindowLifetime.ShowDialog(this, dialog) != ContentDialogResult.Primary) return;
                _ = SheetData.Range(source.Text, Rows(), Columns()).ToArray(); string type = SheetChartModel.Types[Math.Max(0, kind.SelectedIndex)].Key; SheetChartModel.Validate(SheetChartModel.Read(file.Sheet, source.Text), type); var chart = new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["type"] = type, ["range"] = source.Text.ToUpperInvariant(), ["title"] = title.Text }; file.Sheet["charts"] ??= new JsonArray(); ((JsonArray)file.Sheet["charts"]!).Add(chart); Changed(); await ShowChart(chart);
            } catch (Exception e) { await UI.Error(this, e); }
        });
        async Task ShowChart(JsonObject chart)
        {
            var dialog = new ContentDialog { Title = chart["title"]?.ToString(), Content = SheetChart.Create(file.Sheet, chart), CloseButtonText = "Close", SecondaryButtonText = "Delete chart" };
            if (await WindowLifetime.ShowDialog(this, dialog) == ContentDialogResult.Secondary) { ((JsonArray)file.Sheet["charts"]!).Remove(chart); Changed(); }
        }
        Command("Insert", "Charts", async () => { var charts = file.Sheet["charts"] as JsonArray; if (charts is null || charts.Count == 0) { await Message("Charts", "Select a range and insert a chart."); return; } var list = new ListView { ItemsSource = charts.Select(c => c?["title"]?.ToString() ?? "Chart").ToArray(), SelectedIndex = 0, MinWidth = 300 }; var dialog = new ContentDialog { Title = "Charts", Content = list, PrimaryButtonText = "Open", CloseButtonText = "Close" }; if (await WindowLifetime.ShowDialog(this, dialog) == ContentDialogResult.Primary && list.SelectedIndex >= 0) await ShowChart((JsonObject)charts[list.SelectedIndex]!); });
        var functions = new ComboBox { ItemsSource = new[] { "SUM", "AVERAGE", "MIN", "MAX", "COUNT", "IF", "ROUND", "COUNTIF", "SUMIF", "LEN", "TRIM", "CONCAT", "AND", "OR" }, PlaceholderText = "Insert function", Width = 180 }; functions.SelectionChanged += (_, _) => { if (functions.SelectedItem is string name) { Set("=" + name + "()"); formula.Focus(FocusState.Programmatic); formula.Select(name.Length + 2, 0); } }; ribbon.Group("Formulas", "Functions").Children.Add(functions); Command("Formulas", "Calculate", Refresh);
        var filter = new TextBox { Header = "Contains", Width = 180, Text = file.Sheet["filter"]?["value"]?.ToString() ?? "" }; ribbon.Group("Data", "Filter").Children.Add(filter);
        Command("Data", "Filter column", () => { file.Sheet["filter"] = new JsonObject { ["column"] = new string(selected.TakeWhile(char.IsLetter).ToArray()), ["value"] = filter.Text }; Changed(); RebuildRows(); }); Command("Data", "Clear filter", () => { file.Sheet.Remove("filter"); filter.Text = ""; Changed(); RebuildRows(); });
        foreach (bool descending in new[] { false, true }) { bool reverse = descending; Command("Data", descending ? "Sort descending" : "Sort ascending", async () => { try { SheetData.Sort(file.Sheet, range.Text, selected, reverse); Changed(); Refresh(); } catch (Exception e) { await UI.Error(this, e); } }); }
        Command("Page Layout", "Orientation", () => { file.Data["layout"] ??= new JsonObject(); file.Data["layout"]!["orientation"] = file.Data["layout"]!["orientation"]?.ToString() == "landscape" ? "portrait" : "landscape"; Changed(); });
        Command("Review", "Find", async () => { var query = await Prompt("Find"); if (string.IsNullOrWhiteSpace(query)) return; var match = Cells().FirstOrDefault(p => p.Value?.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) == true); if (match.Key is null) { await Message("Find", "No matches."); return; } Select(match.Key); rows.ScrollIntoView(SheetData.Position(match.Key).Row); });
        Command("Review", "Statistics", async () => await Message("Statistics", $"{Cells().Count(p => !string.IsNullOrEmpty(p.Value?.ToString()))} filled cells\n{Cells().Count(p => p.Value?.ToString().StartsWith('=') == true)} formulas"));
        ribbon.OrderGroups("Home", "Home", "Font", "Alignment", "Number", "Cells");
        ribbon.Menu("Export CSV", async () => await ExportText(Csv.Export(Cells()), ".csv")); ViewCommands(); RenderTabs(); RebuildRows();
    }
}

internal static class SheetData
{
    public static (int Row, int Column) Position(string address)
    {
        var match = Regex.Match(address.Trim().ToUpperInvariant(), "^([A-Z]{1,3})([1-9][0-9]{0,5})$"); if (!match.Success) throw new FormatException("Enter a cell or range, for example A1:C10.");
        return (int.Parse(match.Groups[2].Value), match.Groups[1].Value.Aggregate(0, (n, c) => n * 26 + c - 'A' + 1) - 1);
    }
    public static IEnumerable<string> Range(string source, int rows = 10000, int columns = 256)
    {
        var parts = source.Split(':'); if (parts.Length > 2) throw new FormatException("Enter a cell or range, for example A1:C10."); var a = Position(parts[0]); var b = Position(parts[^1]);
        if (Math.Max(a.Row, b.Row) > rows || Math.Max(a.Column, b.Column) >= columns) throw new FormatException("The range is outside this worksheet. Add rows or columns first.");
        if ((long)(Math.Abs(a.Row - b.Row) + 1) * (Math.Abs(a.Column - b.Column) + 1) > 100000) throw new FormatException("Select at most 100,000 cells at a time.");
        for (int r = Math.Min(a.Row, b.Row); r <= Math.Max(a.Row, b.Row); r++) for (int c = Math.Min(a.Column, b.Column); c <= Math.Max(a.Column, b.Column); c++) yield return OfficeFile.Column(c) + r;
    }
    public static string Value(JsonObject sheet, string address) { var cells = (JsonObject)sheet["cells"]!; var raw = cells[address]?.ToString() ?? ""; return raw.StartsWith('=') ? NativeEngine.Calculate(address, cells) : raw; }
    public static string Display(JsonObject sheet, string address)
    {
        var value = Value(sheet, address); if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return value;
        return sheet["styles"]?[address]?["format"]?.ToString() switch { "number" => number.ToString("N2", CultureInfo.InvariantCulture), "currency" => "$" + number.ToString("N2", CultureInfo.InvariantCulture), "percent" => (number * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%", _ => value };
    }
    public static void Sort(JsonObject sheet, string source, string selected, bool descending)
    {
        var positions = Range(source).Select(Position).ToArray(); if (positions.Length < 2) throw new InvalidOperationException("Select the full table range to sort, excluding the header.");
        int first = positions.Min(p => p.Row), last = positions.Max(p => p.Row), left = positions.Min(p => p.Column), right = positions.Max(p => p.Column); int column = Math.Clamp(Position(selected).Column, left, right);
        var cells = (JsonObject)sheet["cells"]!; sheet["styles"] ??= new JsonObject(); var styles = (JsonObject)sheet["styles"]!;
        // Sorting formula rows without rewriting references would silently change their meaning.
        if (positions.Any(p => cells[OfficeFile.Column(p.Column) + p.Row]?.ToString().StartsWith('=') == true)) throw new InvalidOperationException("Sort a range of values. Formula rows need reference-aware sorting, which is not supported yet.");
        var order = Enumerable.Range(first, last - first + 1).OrderBy(r => Value(sheet, OfficeFile.Column(column) + r), Comparer<string>.Create((a, b) => double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out double y) ? x.CompareTo(y) : StringComparer.CurrentCultureIgnoreCase.Compare(a, b))).ToArray(); if (descending) Array.Reverse(order);
        var original = (JsonObject)cells.DeepClone(); var oldStyles = (JsonObject)styles.DeepClone();
        for (int i = 0; i < order.Length; i++) for (int c = left; c <= right; c++) { string from = OfficeFile.Column(c) + order[i], to = OfficeFile.Column(c) + (first + i); cells[to] = original[from]?.DeepClone() ?? JsonValue.Create(""); if (oldStyles[from] is JsonNode style) styles[to] = style.DeepClone(); else styles.Remove(to); }
    }
}
