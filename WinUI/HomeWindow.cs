using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using System.Runtime.InteropServices.WindowsRuntime;
namespace Gaius.Office;
public sealed class HomeWindow : Window
{
    private readonly StackPanel recent = new() { Spacing = 8 };
    private readonly TextBox search = new() { PlaceholderText = "Search your files", Width = 300 };
    private List<OfficeFile> files = [];
    private string section = "home";
    public HomeWindow()
    {
        Title = "Gaius Office"; AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 860));
        var layout = new Grid { Background = UI.Brush("#f4f5f7") }; layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(224) }); layout.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = new StackPanel { Background = UI.Brush("#fafafa"), Padding = new Thickness(20, 32, 20, 20), Spacing = 10 };
        var brand = UI.Text("G  Gaius Office", 25, "#185abd"); brand.Margin = new Thickness(10, 0, 0, 35); sidebar.Children.Add(brand);
        sidebar.Children.Add(UI.Text("WORKSPACE", 10, "#888888")); sidebar.Children.Add(UI.Button("Home", () => { section = "home"; RenderRecent(); }, Symbol.Home));
        foreach (var kind in UI.Kinds) { var k = kind; var button = UI.Button(UI.Labels[k], () => { section = k; RenderRecent(); }); var panel = (StackPanel)button.Content; panel.Children.Insert(0, new FontIcon { Glyph = UI.Glyph(k), Foreground = UI.Brush(UI.Accent(k)), FontSize = 20 }); sidebar.Children.Add(button); }
        sidebar.Children.Add(UI.Button("Open from device", async () => await Import(), Symbol.OpenFile)); var note = UI.Text("Your files. Your device.\nStored locally. No cloud account.", 11, "#777777"); note.Margin = new Thickness(5, 36, 0, 0); sidebar.Children.Add(note); layout.Children.Add(sidebar);
        var content = new StackPanel { Padding = new Thickness(44, 32, 44, 32), Spacing = 20 }; Grid.SetColumn(content, 1);
        var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); header.Children.Add(UI.Text("Home", 15)); Grid.SetColumn(search, 1); header.Children.Add(search); search.TextChanged += (_, _) => RenderRecent(); content.Children.Add(header);
        var greeting = new StackPanel { Spacing = 10, Margin = new Thickness(0, 20, 0, 15) }; greeting.Children.Add(UI.Text("YOUR EVERYDAY WORKSPACE", 10, "#777777")); greeting.Children.Add(UI.Text("Make room for your next idea.", 32)); greeting.Children.Add(UI.Text("Write, calculate, present, and create. Everything starts here.", 14, "#777777")); content.Children.Add(greeting);
        content.Children.Add(UI.Text("Create something new", 16));
        var cards = new Grid { ColumnSpacing = 12 };
        foreach (var _ in UI.Kinds) cards.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < UI.Kinds.Length; i++) { var kind = UI.Kinds[i]; var panel = new StackPanel { Spacing = 15 }; panel.Children.Add(new FontIcon { Glyph = UI.Glyph(kind), FontSize = 30, Foreground = UI.Brush(UI.Accent(kind)), HorizontalAlignment = HorizontalAlignment.Left }); panel.Children.Add(UI.Text(UI.Labels[kind], 15)); panel.Children.Add(UI.Text("Create new", 11, "#777777")); var button = new Button { Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(18, 22, 18, 22), Background = UI.Brush("#ffffff"), BorderBrush = UI.Brush("#e1e3e7"), CornerRadius = new CornerRadius(8) }; button.Click += (_, _) => App.Show(new EditorWindow(OfficeFile.Create(kind))); Grid.SetColumn(button, i); cards.Children.Add(button); }
        content.Children.Add(cards); content.Children.Add(UI.Text("Recent files", 16)); content.Children.Add(recent); content.Children.Add(UI.Text("Files stay on this device. Export a copy to share or back up your work.", 11, "#888888"));
        var scroll = new ScrollViewer { Content = content }; Grid.SetColumn(scroll, 1); layout.Children.Add(scroll); Content = layout;
        Activated += async (_, _) => { try { files = await DocumentStore.ListAsync(); RenderRecent(); } catch (Exception e) { await UI.Error(this, e); } };
    }
    private void RenderRecent()
    {
        recent.Children.Clear();
        var filtered = files.Where(f => (section == "home" || f.Kind == section) && f.Title.Contains(search.Text, StringComparison.OrdinalIgnoreCase));
        foreach (var file in filtered)
        {
            var row = new Grid { Padding = new Thickness(16, 12, 16, 12), Background = UI.Brush("#ffffff"), CornerRadius = new CornerRadius(6) }; row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var open = UI.Button(file.Title, () => App.Show(new EditorWindow(file.Copy(), file.Revision))); row.Children.Add(open);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 }; actions.Children.Add(UI.Text(UI.Labels[file.Kind], 12, "#888888")); actions.Children.Add(UI.Button("Delete", async () => { var dialog = new ContentDialog { XamlRoot = Content.XamlRoot, Title = "Delete file?", Content = $"Delete “{file.Title}” from this device?", PrimaryButtonText = "Delete", CloseButtonText = "Cancel" }; if (await dialog.ShowAsync() == ContentDialogResult.Primary) { try { DocumentStore.Delete(file); files.Remove(file); RenderRecent(); } catch (Exception e) { await UI.Error(this, e); } } }, Symbol.Delete)); Grid.SetColumn(actions, 1); row.Children.Add(actions); recent.Children.Add(row);
        }
        if (recent.Children.Count == 0) recent.Children.Add(new Border { Background = UI.Brush("#ffffff"), Padding = new Thickness(30), CornerRadius = new CornerRadius(8), Child = UI.Text("A clear desk, ready for work. Create a file above or open one from your device.", 13, "#888888") });
    }
    private async Task Import()
    {
        try { var picked = await UI.PickOpen(this, ".gaius", ".txt", ".csv", ".png", ".jpg", ".jpeg", ".pdf"); if (picked is null) return;
            if ((await picked.GetBasicPropertiesAsync()).Size > 40 * 1024 * 1024) throw new IOException("Choose a file smaller than 40 MB.");
            OfficeFile doc; var ext = picked.FileType.ToLowerInvariant();
            if (ext == ".gaius") { doc = OfficeFile.Parse(await FileIO.ReadTextAsync(picked)); doc.Data["id"] = Guid.NewGuid().ToString(); }
            else if (ext == ".txt") { doc = OfficeFile.Create("document"); doc.Content["text"] = await FileIO.ReadTextAsync(picked); doc.Content["html"] = "<p>" + System.Net.WebUtility.HtmlEncode(doc.Content["text"]!.GetValue<string>()).Replace("\n", "<br>") + "</p>"; }
            else if (ext == ".csv") { doc = OfficeFile.Create("spreadsheet"); var rows = Csv.Parse(await FileIO.ReadTextAsync(picked)); if (rows.Count > 10000 || rows.Any(r => r.Count > 100)) throw new IOException("CSV limit: 10,000 rows and 100 columns."); for (int r = 0; r < rows.Count; r++) for (int c = 0; c < rows[r].Count; c++) doc.Sheet["cells"]![$"{OfficeFile.Column(c)}{r + 1}"] = rows[r][c]; }
            else { var bytes = (await FileIO.ReadBufferAsync(picked)).ToArray(); doc = OfficeFile.Create(ext == ".pdf" ? "pdf" : "image"); var mime = ext == ".pdf" ? "application/pdf" : ext == ".png" ? "image/png" : "image/jpeg"; doc.Data[ext == ".pdf" ? "pdf" : "image"]!["dataUrl"] = $"data:{mime};base64,{Convert.ToBase64String(bytes)}"; }
            doc.Title = picked.DisplayName; App.Show(new EditorWindow(doc));
        } catch (Exception e) { await UI.Error(this, e); }
    }
}
