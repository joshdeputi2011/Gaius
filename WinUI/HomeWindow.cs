using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using System.Runtime.InteropServices.WindowsRuntime;
namespace Gaius.Office;
public sealed class HomeWindow : Window
{
    private readonly ListView recent = new() { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = true };
    private readonly AutoSuggestBox search = new() { PlaceholderText = "Search files", Width = 280 };
    private readonly TextBlock heading = UI.Text("Home", 28);
    private readonly TextBlock introduction = UI.Text("Create something you can make your own.", 14, "#69788d");
    private readonly TextBlock startHeading = UI.Text("Create new", 18);
    private readonly GridView cards = new() { SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = false, IsTabStop = false, Padding = new Thickness(0), Margin = new Thickness(-4, 0, -4, 0) };
    private readonly Grid satin = new() { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Right, Width = 390, Height = 120, Opacity = .7 };
    private readonly NavigationView navigation = new() { PaneDisplayMode = NavigationViewPaneDisplayMode.Left, IsSettingsVisible = true, IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed, OpenPaneLength = 220, PaneTitle = "Gaius Office" };
    private List<OfficeFile> files = [];
    private string section = "home";
    private bool closed, refreshing;
    public HomeWindow()
    {
        navigation.Resources["NavigationViewContentBackground"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Title = "Gaius Office"; AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 860));
        var home = new NavigationViewItem { Content = "Home", Icon = new SymbolIcon(Symbol.Home), Tag = "home" };
        navigation.MenuItems.Add(home);
        foreach (var kind in UI.Kinds) navigation.MenuItems.Add(new NavigationViewItem { Content = UI.Labels[kind], Icon = new FontIcon { Glyph = UI.Glyph(kind), Foreground = UI.Brush(UI.Accent(kind)) }, Tag = kind });
        navigation.SelectedItem = home;
        navigation.ItemInvoked += async (_, args) => { if (args.IsSettingsInvoked) await SuiteSettings.Show(this); };
        navigation.SelectionChanged += (_, args) => { if (!args.IsSettingsSelected && args.SelectedItem is NavigationViewItem { Tag: string next } && (next == "home" || UI.Kinds.Contains(next))) { section = next; RenderStartPage(); RenderRecent(); } };
        var open = new NavigationViewItem { Content = "Open a file", Icon = new SymbolIcon(Symbol.OpenFile) };
        open.Tapped += async (_, _) => await Import(); navigation.FooterMenuItems.Add(open);
        var content = new Grid { Padding = new Thickness(28, 18, 28, 24), RowSpacing = 18, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); content.RowDefinitions.Add(new RowDefinition());
        var header = new Grid { MinHeight = 122 }; header.Children.Add(satin);
        var headerText = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 470, HorizontalAlignment = HorizontalAlignment.Left }; headerText.Children.Add(heading); headerText.Children.Add(introduction); header.Children.Add(headerText);
        header.SizeChanged += (_, _) => headerText.MaxWidth = Math.Max(170, header.ActualWidth - 270);
        search.Width = 240; search.HorizontalAlignment = HorizontalAlignment.Right; search.VerticalAlignment = VerticalAlignment.Top; header.Children.Add(search); search.TextChanged += (_, _) => RenderRecent(); content.Children.Add(header);
        var start = new StackPanel { Spacing = 12 }; start.Children.Add(startHeading); start.Children.Add(cards); Grid.SetRow(start, 1); content.Children.Add(start);
        var recentHeader = new Grid(); recentHeader.ColumnDefinitions.Add(new ColumnDefinition()); recentHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); recentHeader.Children.Add(UI.Text("Recent files", 18));
        var openButton = UI.Button("Open from device", async () => await Import(), Symbol.OpenFile); Grid.SetColumn(openButton, 1); recentHeader.Children.Add(openButton); Grid.SetRow(recentHeader, 2); content.Children.Add(recentHeader);
        recent.ItemClick += (_, args) => { if (args.ClickedItem is ListViewItem item && item.Tag is OfficeFile file) App.Show(new EditorWindow(file.Copy(), file.Revision)); };
        Grid.SetRow(recent, 3); content.Children.Add(recent); navigation.Content = content;
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(WindowChrome.Create(this, "Gaius Office")); Grid.SetRow(navigation, 1); root.Children.Add(navigation); Content = root;
        Appearance.Changed += RefreshAppearance;
        Closed += (_, _) => { closed = true; Appearance.Changed -= RefreshAppearance; };
        Activated += async (_, _) => { if (closed || refreshing) return; refreshing = true; try { var loaded = await DocumentStore.ListAsync(); if (!closed) { files = loaded; RenderRecent(); } } catch (Exception e) { if (!closed) await UI.Error(this, e); } finally { refreshing = false; } };
        RenderStartPage();
    }
    private void RefreshAppearance() { if (!closed) { RenderStartPage(); RenderRecent(); } }
    private void RenderStartPage()
    {
        if (closed) return;
        heading.Text = section == "home" ? "Welcome to Gaius Office" : UI.Labels[section];
        introduction.Text = section switch { "document" => "Start with a template and make room for your ideas.", "spreadsheet" => "Organise your work, explore the numbers and see what comes next.", "presentation" => "Give your next idea a clear, confident beginning.", "pdf" => "Convert, compress and organise your documents.", "image" => "Resize, compress and prepare images for your next project.", _ => "Your documents, spreadsheets, presentations and creative tools." };
        SatinCurve(section == "home" ? "#185abd" : UI.Accent(section));
        cards.Items.Clear(); startHeading.Text = section is "pdf" or "image" ? "Quick tools" : section == "home" ? "Create new" : "Start with a template";
        if (section == "home") foreach (string kind in UI.Kinds) { string selectedKind = kind; cards.Items.Add(Card(UI.Labels[kind], kind switch { "pdf" => "Open and organise PDFs", "image" => "Edit and prepare images", _ => "Start something new" }, kind, () => { navigation.SelectedItem = navigation.MenuItems.OfType<NavigationViewItem>().First(i => i.Tag?.ToString() == selectedKind); }, false)); }
        else if (section is "document" or "spreadsheet" or "presentation") foreach (var template in OfficeTemplates.ForKind(section)) { var selected = template; cards.Items.Add(Card(template.Name, template.Description, section, () => App.Show(new EditorWindow(OfficeTemplates.Create(section, selected.Id))), true, template.Id)); }
        else {
            AddTool(section == "pdf" ? "Open PDF" : "Open image", "Open a file in the editor", section, async () => await Import(section == "pdf" ? [".pdf"] : [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".img"]));
            AddTool("Quick compress", section == "pdf" ? "Reduce PDF size" : "Create a smaller JPEG copy", section, async () => await OfficeQuickTools.Compress(this, section));
            if (section == "image") AddTool("Convert image", "Save as PNG, JPEG or BMP", "image", async () => await OfficeQuickTools.ConvertImage(this));
            AddTool("Image to PDF", "Create a PDF from an image", "image", async () => await OfficeQuickTools.ToPdf(this, "image"));
            if (section == "pdf") {
                AddTool("Word to PDF", "DOCX, ODT and text documents", "document", async () => await OfficeQuickTools.ToPdf(this, "document"));
                AddTool("Excel to PDF", "Preview and export a workbook", "spreadsheet", async () => await OfficeQuickTools.ToPdf(this, "spreadsheet"));
                AddTool("PowerPoint to PDF", "Export slides as PDF pages", "presentation", async () => await OfficeQuickTools.ToPdf(this, "presentation"));
            }
        }
    }
    private void AddTool(string title, string detail, string kind, Func<Task> action) => cards.Items.Add(Card(title, detail, kind, async () => { if (closed) return; try { await action(); } catch (Exception error) { if (!closed) await UI.Error(this, error); } }, false));
    private Button Card(string title, string detail, string kind, Action action, bool preview, string template = "")
    {
        var panel = new StackPanel { Spacing = 10, Width = 154 };
        var picture = new Grid { Height = preview ? 92 : 44 };
        if (preview) {
            var paper = new Border { Width = kind == "presentation" ? 138 : 78, Height = 86, CornerRadius = new CornerRadius(3), Background = UI.DocumentBrush("#ffffff"), BorderBrush = UI.DocumentBrush("#dbe3ee"), BorderThickness = new Thickness(1), Padding = new Thickness(9), HorizontalAlignment = HorizontalAlignment.Center };
            var lines = new StackPanel { Spacing = 5 }; lines.Children.Add(new Border { Height = 5, Width = template == "blank" ? 14 : 42, Background = UI.DocumentBrush(UI.Accent(kind)), HorizontalAlignment = HorizontalAlignment.Left });
            if (template != "blank") for (int i = 0; i < 6; i++) lines.Children.Add(new Border { Height = kind == "spreadsheet" ? 5 : 3, Background = UI.DocumentBrush(i % 3 == 0 ? "#cbd5e1" : "#e5eaf1"), Width = kind == "presentation" ? 100 - i * 8 : 48 - i % 3 * 6, HorizontalAlignment = HorizontalAlignment.Left });
            paper.Child = lines; picture.Children.Add(paper);
        } else picture.Children.Add(new FontIcon { Glyph = title.Contains("compress", StringComparison.OrdinalIgnoreCase) ? "\uE740" : UI.Glyph(kind), FontSize = 28, Foreground = UI.Brush(UI.Accent(kind)), HorizontalAlignment = HorizontalAlignment.Left });
        panel.Children.Add(picture); var label = UI.Text(title, 14); label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; panel.Children.Add(label); var caption = UI.Text(detail, 11, "#69788d"); caption.MinHeight = 30; panel.Children.Add(caption);
        var button = new Button { Content = panel, Padding = new Thickness(14), Margin = new Thickness(4), Background = Appearance.Glass(), BorderBrush = UI.Brush("#ffffff"), CornerRadius = new CornerRadius(12), HorizontalContentAlignment = HorizontalAlignment.Left };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, title); button.Click += (_, _) => action(); return button;
    }
    private void SatinCurve(string accent)
    {
        satin.Children.Clear();
        var canvas = new Canvas { Width = 520, Height = 160 };
        string[] curves = ["M0,132 C105,157 120,8 239,16 C354,24 322,141 520,82 L520,119 C342,180 327,60 236,53 C145,47 119,189 0,159 Z", "M30,132 C160,141 149,30 262,47 C365,62 362,143 520,95 L520,112 C365,166 350,84 255,68 C157,52 149,160 30,149 Z", "M123,138 C217,144 234,68 302,83 C369,99 415,150 520,120 L520,133 C410,165 367,118 300,104 C239,90 219,162 123,153 Z"];
        for (int i = 0; i < curves.Length; i++) {
            var path = (Microsoft.UI.Xaml.Shapes.Path)Microsoft.UI.Xaml.Markup.XamlReader.Load($"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{curves[i]}' />");
            var brush = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 1) }; brush.GradientStops.Add(new GradientStop { Color = UI.Color(accent), Offset = 0 }); brush.GradientStops.Add(new GradientStop { Color = Microsoft.UI.Colors.White, Offset = .5 }); brush.GradientStops.Add(new GradientStop { Color = UI.Color(accent), Offset = 1 }); path.Fill = brush; path.Opacity = .14 + i * .1; canvas.Children.Add(path);
        }
        satin.Children.Add(new Viewbox { Child = canvas, Stretch = Stretch.Uniform });
    }
    private void RenderRecent()
    {
        if (closed) return; recent.Items.Clear();
        foreach (var file in files.Where(f => (section == "home" || f.Kind == section) && f.Title.Contains(search.Text, StringComparison.OrdinalIgnoreCase))) {
            var row = new Grid { Padding = new Thickness(10, 12, 10, 12), ColumnSpacing = 14 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new FontIcon { Glyph = UI.Glyph(file.Kind), FontSize = 24, Foreground = UI.Brush(UI.Accent(file.Kind)) });
            var details = new StackPanel { Spacing = 3 }; details.Children.Add(UI.Text(file.Title, 14)); details.Children.Add(UI.Text(UI.Labels[file.Kind], 11, "#69788d"));
            var open = new Button { Content = details, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(0) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(open, "Open " + file.Title); open.Click += (_, _) => App.Show(new EditorWindow(file.Copy(), file.Revision)); Grid.SetColumn(open, 1); row.Children.Add(open);
            var date = UI.Text(DateTime.TryParse(file.Revision, out var edited) ? edited.ToLocalTime().ToString("dd MMM yyyy") : "", 12, "#69788d"); date.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(date, 2); row.Children.Add(date);
            var menu = new MenuFlyout(); var delete = new MenuFlyoutItem { Text = "Delete", Icon = new SymbolIcon(Symbol.Delete) }; menu.Items.Add(delete);
            delete.Click += async (_, _) => { if (closed) return; var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Delete file?", Content = file.Title, PrimaryButtonText = "Delete", CloseButtonText = "Cancel" }; if (await WindowLifetime.ShowDialog(this, dialog) == ContentDialogResult.Primary && !closed) { try { DocumentStore.Delete(file); files.Remove(file); RenderRecent(); } catch (Exception e) { if (!closed) await UI.Error(this, e); } } };
            var more = new Button { Content = new SymbolIcon(Symbol.More), Flyout = menu, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0) }; Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(more, "File options for " + file.Title); Grid.SetColumn(more, 3); row.Children.Add(more);
            recent.Items.Add(new ListViewItem { Content = row, Tag = file, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }
        if (recent.Items.Count == 0) recent.Items.Add(new ListViewItem { Content = UI.Text(string.IsNullOrWhiteSpace(search.Text) ? "No files yet" : "No matching files", 13, "#69788d"), IsEnabled = false, Padding = new Thickness(16, 24, 16, 24) });
    }
    public async Task Import(params string[] extensions)
    {
        try { var picked = await UI.PickOpen(this, extensions.Length == 0 ? OfficeQuickTools.OpenExtensions : extensions); if (picked is null || closed) return;
            if ((await picked.GetBasicPropertiesAsync()).Size > 40 * 1024 * 1024) throw new IOException("Choose a file smaller than 40 MB.");
            OfficeFile doc; var ext = picked.FileType.ToLowerInvariant();
            if (ext == ".gaius") { doc = OfficeFile.Parse(await FileIO.ReadTextAsync(picked)); doc.Data["id"] = Guid.NewGuid().ToString(); }
            else if (ext is ".docx" or ".xlsx" or ".pptx" or ".odt" or ".doc") { var bytes = (await FileIO.ReadBufferAsync(picked)).ToArray(); doc = await Task.Run(() => OfficeInterchange.Import(bytes, ext)); }
            else if (ext == ".rtf") { doc = OfficeFile.Create("document"); doc.Content["rtf"] = await FileIO.ReadTextAsync(picked); }
            else if (ext == ".txt") { doc = OfficeFile.Create("document"); doc.Content["text"] = await FileIO.ReadTextAsync(picked); doc.Content["html"] = "<p>" + System.Net.WebUtility.HtmlEncode(doc.Content["text"]!.GetValue<string>()).Replace("\n", "<br>") + "</p>"; }
            else if (ext == ".csv") { doc = OfficeFile.Create("spreadsheet"); var rows = Csv.Parse(await FileIO.ReadTextAsync(picked)); if (rows.Count > 10000 || rows.Any(r => r.Count > 100)) throw new IOException("CSV limit: 10,000 rows and 100 columns."); for (int r = 0; r < rows.Count; r++) for (int c = 0; c < rows[r].Count; c++) doc.Sheet["cells"]![$"{OfficeFile.Column(c)}{r + 1}"] = rows[r][c]; }
            else { var bytes = (await FileIO.ReadBufferAsync(picked)).ToArray(); doc = OfficeFile.Create(ext == ".pdf" ? "pdf" : "image"); string mime = ext == ".pdf" ? "application/pdf" : await OfficeQuickTools.ImageMime(bytes); doc.Data[ext == ".pdf" ? "pdf" : "image"]!["dataUrl"] = $"data:{mime};base64,{Convert.ToBase64String(bytes)}"; }
            if (closed) return; doc.Title = picked.DisplayName; App.Show(new EditorWindow(doc));
        } catch (Exception e) { if (!closed) await UI.Error(this, e); }
    }
}
