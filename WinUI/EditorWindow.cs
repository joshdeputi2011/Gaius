using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Text;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;
using Windows.UI.Text;
using PdfSharp.Pdf.IO;
namespace Gaius.Office;
public sealed class EditorWindow : Window
{
    private readonly OfficeFile file;
    private readonly StackPanel toolbar = new() { Orientation = Orientation.Horizontal, Spacing = 10, Padding = new Thickness(16, 10, 16, 10), Background = UI.Brush("#ffffff") };
    private readonly Grid body = new() { Background = UI.Brush("#edf0f4") };
    private readonly TextBlock status = UI.Text("Saved on this device", 10, "#777777");
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private string? savedRevision;
    private bool dirty, allowClose;
    public EditorWindow(OfficeFile document, string? revision = null)
    {
        file = document; savedRevision = revision; Title = $"{file.Title} — Gaius {UI.Labels[file.Kind]}"; AppWindow.Resize(new Windows.Graphics.SizeInt32(1200, 850));
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        var header = new Grid { Background = UI.Brush(UI.Accent(file.Kind)), Padding = new Thickness(16, 8, 16, 8), ColumnSpacing = 16 }; header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var home = UI.Button("Home", () => App.Show(new HomeWindow()), Symbol.Home); header.Children.Add(home);
        var title = new TextBox { Text = file.Title, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Foreground = UI.Brush("#ffffff"), BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center }; title.TextChanged += (_, _) => { file.Title = title.Text; Title = $"{file.Title} — Gaius {UI.Labels[file.Kind]}"; Changed(); }; Grid.SetColumn(title, 1); header.Children.Add(title);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; actions.Children.Add(UI.Button("New window", () => { var copy = file.Copy(); copy.Data["id"] = Guid.NewGuid().ToString(); copy.Title += " — Copy"; App.Show(new EditorWindow(copy)); }, Symbol.NewWindow)); actions.Children.Add(UI.Button("Export file", async () => await ExportGaius(), Symbol.Download)); Grid.SetColumn(actions, 2); header.Children.Add(actions); root.Children.Add(header);
        var ribbonScroll = new ScrollViewer { Content = toolbar, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(ribbonScroll, 1); root.Children.Add(ribbonScroll); Grid.SetRow(body, 2); root.Children.Add(body);
        var footer = new Border { Child = status, Background = UI.Brush("#ffffff"), Padding = new Thickness(16, 6, 16, 0) }; Grid.SetRow(footer, 3); root.Children.Add(footer); Content = root;
        switch (file.Kind) { case "document": BuildWrite(); break; case "spreadsheet": BuildSheet(); break; case "presentation": BuildSlides(); break; case "image": BuildImage(); break; case "pdf": BuildPdf(); break; }
        timer.Tick += async (_, _) => { timer.Stop(); await SaveCurrent(); };
        Closed += (_, _) => { timer.Stop(); pdfSourceAdapter?.Dispose(); pdfSource?.Dispose(); };
        AppWindow.Closing += async (_, args) => { if (allowClose || !dirty) return; args.Cancel = true; timer.Stop(); if (await SaveCurrent()) { allowClose = true; Close(); } };
        if (revision is null) Changed();
    }
    private void Changed() { file.Data["updatedAt"] = DateTime.UtcNow.ToString("O"); dirty = true; status.Text = "Saving on this device…"; timer.Stop(); timer.Start(); }
    private async Task<bool> SaveCurrent()
    {
        await saveGate.WaitAsync();
        try { if (!dirty) return true; var snapshot = file.Copy(); await DocumentStore.SaveAsync(snapshot, savedRevision); savedRevision = snapshot.Revision; if (snapshot.Revision == file.Revision) { dirty = false; status.Text = "Saved on this device"; } return !dirty; }
        catch (Exception e) { status.Text = "Save failed — export a copy"; await UI.Error(this, e); return false; }
        finally { saveGate.Release(); }
    }
    private async Task ExportGaius() { try { var target = await UI.PickSave(this, file.Title, ".gaius"); if (target is not null) await FileIO.WriteTextAsync(target, file.Data.ToJsonString()); } catch (Exception e) { await UI.Error(this, e); } }
    private async Task ExportText(string text, string extension) { try { var target = await UI.PickSave(this, file.Title, extension); if (target is not null) await FileIO.WriteTextAsync(target, text); } catch (Exception e) { await UI.Error(this, e); } }
    private void BuildWrite()
    {
        var editor = new RichEditBox { Background = UI.Brush("#ffffff"), BorderThickness = new Thickness(0), Padding = new Thickness(64), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 1050, FontFamily = new FontFamily("Calibri"), FontSize = 16 };
        if (file.Content["rtf"] is JsonValue rtf) editor.Document.SetText(TextSetOptions.FormatRtf, rtf.GetValue<string>()); else editor.Document.SetText(TextSetOptions.None, file.Content["text"]!.GetValue<string>());
        editor.TextChanged += (_, _) => { editor.Document.GetText(TextGetOptions.None, out var text); editor.Document.GetText(TextGetOptions.FormatRtf, out var rich); file.Data["content"] = new JsonObject { ["text"] = text, ["html"] = "<p>" + System.Net.WebUtility.HtmlEncode(text).Replace("\r", "<br>") + "</p>", ["rtf"] = rich }; Changed(); };
        toolbar.Children.Add(UI.Button("Bold", () => { editor.Document.Selection.CharacterFormat.Bold = FormatEffect.Toggle; editor.Focus(FocusState.Programmatic); }, Symbol.Bold));
        toolbar.Children.Add(UI.Button("Italic", () => { editor.Document.Selection.CharacterFormat.Italic = FormatEffect.Toggle; editor.Focus(FocusState.Programmatic); }, Symbol.Italic));
        toolbar.Children.Add(UI.Button("Underline", () => { var f = editor.Document.Selection.CharacterFormat; f.Underline = f.Underline == UnderlineType.None ? UnderlineType.Single : UnderlineType.None; editor.Focus(FocusState.Programmatic); }, Symbol.Underline));
        var fonts = new ComboBox { ItemsSource = new[] { "Calibri", "Arial", "Georgia", "Times New Roman" }, SelectedIndex = 0, Width = 150 }; fonts.SelectionChanged += (_, _) => editor.Document.Selection.CharacterFormat.Name = (string)fonts.SelectedItem; toolbar.Children.Add(fonts);
        var sizes = new ComboBox { ItemsSource = new[] { 10, 12, 14, 16, 18, 24, 32, 48 }, SelectedItem = 12, Width = 75 }; sizes.SelectionChanged += (_, _) => editor.Document.Selection.CharacterFormat.Size = Convert.ToSingle(sizes.SelectedItem); toolbar.Children.Add(sizes);
        toolbar.Children.Add(UI.Button("Undo", () => editor.Document.Undo(), Symbol.Undo)); toolbar.Children.Add(UI.Button("Redo", () => editor.Document.Redo(), Symbol.Redo)); toolbar.Children.Add(UI.Button("Export text", async () => await ExportText(file.Content["text"]!.GetValue<string>(), ".txt"), Symbol.Download));
        body.Children.Add(new ScrollViewer { Content = new Border { Width = 794, Background = UI.Brush("#ffffff"), Margin = new Thickness(32), Child = editor } });
    }
    private void BuildSheet()
    {
        var cells = (JsonObject)file.Sheet["cells"]!; string selected = "A1";
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) }); root.RowDefinitions.Add(new RowDefinition());
        var formula = new TextBox { Text = cells[selected]?.GetValue<string>() ?? "", PlaceholderText = "Value or =SUM(A1:A10)", Margin = new Thickness(12, 4, 12, 4), Header = null };
        var grid = new Grid { Background = UI.Brush("#ffffff") }; grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) }); for (int c = 0; c < 26; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) }); for (int r = 0; r <= 100; r++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        var boxes = new Dictionary<string, TextBox>(); bool rendering = false;
        void Refresh() { rendering = true; foreach (var pair in boxes) { var raw = cells[pair.Key]?.GetValue<string>() ?? ""; pair.Value.Text = pair.Key == selected ? raw : raw.StartsWith('=') ? NativeEngine.Calculate(pair.Key, cells) : raw; } rendering = false; }
        for (int c = 0; c < 26; c++) { var label = UI.Text(OfficeFile.Column(c), 11, "#777777"); label.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(label, c + 1); grid.Children.Add(label); }
        for (int r = 1; r <= 100; r++) { var label = UI.Text(r.ToString(), 11, "#777777"); label.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetRow(label, r); grid.Children.Add(label); for (int c = 0; c < 26; c++) { var address = $"{OfficeFile.Column(c)}{r}"; var box = new TextBox { Padding = new Thickness(6, 2, 6, 2), FontSize = 12, MinHeight = 28, BorderBrush = UI.Brush("#e1e3e7"), BorderThickness = new Thickness(0, 0, 1, 1), CornerRadius = new CornerRadius(0), Background = UI.Brush("#ffffff") }; box.GotFocus += (_, _) => { selected = address; formula.Text = cells[address]?.GetValue<string>() ?? ""; Refresh(); }; box.TextChanged += (_, _) => { if (rendering) return; cells[address] = box.Text; formula.Text = box.Text; Changed(); }; box.LostFocus += (_, _) => Refresh(); Grid.SetRow(box, r); Grid.SetColumn(box, c + 1); boxes[address] = box; grid.Children.Add(box); } }
        formula.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { cells[selected] = formula.Text; Changed(); Refresh(); } };
        toolbar.Children.Add(UI.Text("Enter formulas with =  ·  SUM  AVERAGE  MIN  MAX  COUNT  IF  ROUND  ABS", 12, "#777777")); toolbar.Children.Add(UI.Button("Export CSV", async () => { try { await ExportText(Csv.Export(cells), ".csv"); } catch (Exception e) { await UI.Error(this, e); } }, Symbol.Download));
        root.Children.Add(formula); var scroll = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled }; Grid.SetRow(scroll, 1); root.Children.Add(scroll); body.Children.Add(root); Refresh();
    }
    private void BuildSlides()
    {
        int index = 0; var root = new Grid { Padding = new Thickness(24), ColumnSpacing = 24 }; root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) }); root.ColumnDefinitions.Add(new ColumnDefinition());
        var list = new ListView(); var stage = new StackPanel { Background = UI.Brush("#ffffff"), Padding = new Thickness(50), Spacing = 24, VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(stage, 1); root.Children.Add(list); root.Children.Add(stage);
        void Render() { stage.Children.Clear(); list.Items.Clear(); foreach (var s in file.Slides) list.Items.Add(s!["title"]!.GetValue<string>()); if (file.Slides.Count == 0) return; index = Math.Clamp(index, 0, file.Slides.Count - 1); var slide = file.Slides[index]!; list.SelectedIndex = index; stage.Background = UI.Brush(slide["background"]!.GetValue<string>()); stage.Children.Add(new Border { Width = 45, Height = 5, Background = UI.Brush("#c43e1c"), HorizontalAlignment = HorizontalAlignment.Left }); var title = new TextBox { Text = slide["title"]!.GetValue<string>(), FontSize = 36, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) }; title.TextChanged += (_, _) => { slide["title"] = title.Text; Changed(); }; stage.Children.Add(title); var text = new TextBox { Text = slide["body"]!.GetValue<string>(), FontSize = 22, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 240, BorderThickness = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) }; text.TextChanged += (_, _) => { slide["body"] = text.Text; Changed(); }; stage.Children.Add(text); }
        bool selecting = false; list.SelectionChanged += (_, _) => { if (selecting || list.SelectedIndex < 0 || list.SelectedIndex == index) return; index = list.SelectedIndex; selecting = true; Render(); selecting = false; };
        toolbar.Children.Add(UI.Button("New slide", () => { file.Slides.Add(new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["title"] = "New slide", ["body"] = "Add your content", ["background"] = "#ffffff" }); index = file.Slides.Count - 1; Changed(); selecting = true; Render(); selecting = false; }, Symbol.Add));
        toolbar.Children.Add(UI.Button("Delete slide", () => { if (file.Slides.Count < 2) return; file.Slides.RemoveAt(index); index = Math.Max(0, index - 1); Changed(); selecting = true; Render(); selecting = false; }, Symbol.Delete));
        toolbar.Children.Add(UI.Button("Present", () => { if (file.Slides.Count > 0) App.Show(new PresentationWindow(file.Copy(), index)); }, Symbol.Play));
        body.Children.Add(root); Render();
    }
    private readonly Microsoft.UI.Xaml.Controls.Image imagePreview = new() { Stretch = Stretch.Uniform, Margin = new Thickness(30) };
    private byte[]? renderedImage;
    private int imageGeneration;
    private void BuildImage()
    {
        toolbar.Children.Add(UI.Button("Open image", async () => { try { var picked = await UI.PickOpen(this, ".png", ".jpg", ".jpeg", ".bmp"); if (picked is null) return; var bytes = (await FileIO.ReadBufferAsync(picked)).ToArray(); if (bytes.Length > 40 * 1024 * 1024) throw new IOException("Choose an image smaller than 40 MB."); file.Image["dataUrl"] = $"data:image/{(picked.FileType == ".png" ? "png" : "jpeg")};base64,{Convert.ToBase64String(bytes)}"; Changed(); await RenderImage(); } catch (Exception e) { await UI.Error(this, e); } }, Symbol.OpenFile));
        toolbar.Children.Add(UI.Button("Rotate", async () => { file.Image["rotation"] = file.Image["rotation"]!.GetValue<double>() + 90; Changed(); await RenderImage(); }, Symbol.Rotate));
        foreach (var key in new[] { "brightness", "contrast" }) { var group = new StackPanel { Width = 150 }; group.Children.Add(UI.Text(key, 10, "#777777")); var slider = new Slider { Minimum = 0, Maximum = 200, Value = file.Image[key]!.GetValue<double>() }; slider.ValueChanged += async (_, e) => { file.Image[key] = e.NewValue; Changed(); await RenderImage(); }; group.Children.Add(slider); toolbar.Children.Add(group); }
        var grayscale = new CheckBox { Content = "Grayscale", IsChecked = file.Image["grayscale"]!.GetValue<bool>() }; grayscale.Click += async (_, _) => { file.Image["grayscale"] = grayscale.IsChecked == true; Changed(); await RenderImage(); }; toolbar.Children.Add(grayscale);
        toolbar.Children.Add(UI.Button("Export PNG", async () => { try { if (renderedImage is null) return; var target = await UI.PickSave(this, file.Title, ".png"); if (target is not null) await FileIO.WriteBytesAsync(target, renderedImage); } catch (Exception e) { await UI.Error(this, e); } }, Symbol.Download)); body.Children.Add(imagePreview);
        ((FrameworkElement)Content).Loaded += async (_, _) => await RenderImage();
    }
    private async Task RenderImage()
    {
        var generation = ++imageGeneration;
        try { var data = file.Image["dataUrl"]!.GetValue<string>(); if (string.IsNullOrEmpty(data)) return; using var source = new MemoryStream(Convert.FromBase64String(data[(data.IndexOf(',') + 1)..])); var decoder = await BitmapDecoder.CreateAsync(source.AsRandomAccessStream());
            if ((long)decoder.PixelWidth * decoder.PixelHeight > 40000000) throw new IOException("Image limit: 40 megapixels.");
            var rotation = ((int)file.Image["rotation"]!.GetValue<double>() % 360 + 360) % 360; var transform = new BitmapTransform { Rotation = rotation switch { 90 => BitmapRotation.Clockwise90Degrees, 180 => BitmapRotation.Clockwise180Degrees, 270 => BitmapRotation.Clockwise270Degrees, _ => BitmapRotation.None } };
            var pixels = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
            var brightness = file.Image["brightness"]!.GetValue<double>() / 100; var contrast = file.Image["contrast"]!.GetValue<double>() / 100; bool gray = file.Image["grayscale"]!.GetValue<bool>();
            for (int i = 0; i < pixels.Length; i += 4) { for (int c = 0; c < 3; c++) pixels[i + c] = (byte)Math.Clamp(((pixels[i + c] / 255.0 - 0.5) * contrast + 0.5) * brightness * 255, 0, 255); if (gray) { byte l = (byte)(pixels[i] * .114 + pixels[i + 1] * .587 + pixels[i + 2] * .299); pixels[i] = pixels[i + 1] = pixels[i + 2] = l; } }
            using var stream = new InMemoryRandomAccessStream(); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream); bool swap = rotation is 90 or 270; encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, swap ? decoder.PixelHeight : decoder.PixelWidth, swap ? decoder.PixelWidth : decoder.PixelHeight, 96, 96, pixels); await encoder.FlushAsync(); stream.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream); stream.Seek(0); var bytes = new byte[(int)stream.Size]; await stream.AsStreamForRead().ReadExactlyAsync(bytes); if (generation != imageGeneration) return; renderedImage = bytes; imagePreview.Source = bitmap;
        } catch (Exception e) { if (generation == imageGeneration) await UI.Error(this, e); }
    }
    private Windows.Data.Pdf.PdfDocument? pdf;
    private MemoryStream? pdfSource;
    private IRandomAccessStream? pdfSourceAdapter;
    private int pageIndex;
    private readonly TextBox pageRange = new() { Text = "1", Width = 100, PlaceholderText = "1,3-5" };
    private readonly Microsoft.UI.Xaml.Controls.Image pdfPreview = new() { Stretch = Stretch.Uniform, Margin = new Thickness(24) };
    private byte[] PdfBytes => Convert.FromBase64String(file.Data["pdf"]!["dataUrl"]!.GetValue<string>().Split(',').Last());
    private void BuildPdf()
    {
        toolbar.Children.Add(UI.Button("Open PDF", async () => { try { var picked = await UI.PickOpen(this, ".pdf"); if (picked is null) return; var bytes = (await FileIO.ReadBufferAsync(picked)).ToArray(); if (bytes.Length > 40 * 1024 * 1024) throw new IOException("Choose a PDF smaller than 40 MB."); SetPdf(bytes); await LoadPdf(); } catch (Exception e) { await UI.Error(this, e); } }, Symbol.OpenFile));
        toolbar.Children.Add(UI.Button("Previous", async () => { if (pdf is null) return; pageIndex = Math.Max(0, pageIndex - 1); await RenderPdfPage(); }, Symbol.Back)); toolbar.Children.Add(UI.Button("Next", async () => { if (pdf is null) return; pageIndex = Math.Min((int)pdf.PageCount - 1, pageIndex + 1); await RenderPdfPage(); }, Symbol.Forward)); toolbar.Children.Add(pageRange);
        foreach (var op in new[] { "Merge", "Rotate", "Keep pages", "Delete pages" }) { var action = op; toolbar.Children.Add(UI.Button(action, async () => await ModifyPdf(action))); }
        toolbar.Children.Add(UI.Button("Export PDF", async () => { try { if (pdf is null) return; var target = await UI.PickSave(this, file.Title, ".pdf"); if (target is not null) await FileIO.WriteBytesAsync(target, PdfBytes); } catch (Exception e) { await UI.Error(this, e); } }, Symbol.Download)); body.Children.Add(pdfPreview); ((FrameworkElement)Content).Loaded += async (_, _) => await LoadPdf();
    }
    private void SetPdf(byte[] bytes) { file.Data["pdf"]!["dataUrl"] = "data:application/pdf;base64," + Convert.ToBase64String(bytes); Changed(); }
    private async Task LoadPdf() { try { if (string.IsNullOrEmpty(file.Data["pdf"]!["dataUrl"]!.GetValue<string>())) return; var source = new MemoryStream(PdfBytes); var adapter = source.AsRandomAccessStream(); var loaded = await Windows.Data.Pdf.PdfDocument.LoadFromStreamAsync(adapter); pdfSourceAdapter?.Dispose(); pdfSource?.Dispose(); pdfSource = source; pdfSourceAdapter = adapter; pdf = loaded; pageIndex = Math.Clamp(pageIndex, 0, (int)pdf.PageCount - 1); await RenderPdfPage(); } catch (Exception e) { await UI.Error(this, e); } }
    private async Task RenderPdfPage() { if (pdf is null) return; using var page = pdf.GetPage((uint)pageIndex); using var stream = new InMemoryRandomAccessStream(); await page.RenderToStreamAsync(stream); stream.Seek(0); var image = new BitmapImage(); await image.SetSourceAsync(stream); pdfPreview.Source = image; status.Text = $"Page {pageIndex + 1} of {pdf.PageCount} · Local file"; }
    private List<int> SelectedPages(int count) { var output = new List<int>(); foreach (var part in pageRange.Text.Split(',')) { var m = System.Text.RegularExpressions.Regex.Match(part.Trim(), "^(\\d+)(?:-(\\d+))?$"); if (!m.Success) throw new FormatException("Use page numbers like 1,3-5."); int start = int.Parse(m.Groups[1].Value), end = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : start; if (start < 1 || end > count || end < start) throw new FormatException($"Choose pages between 1 and {count}."); for (int p = start - 1; p < end; p++) if (!output.Contains(p)) output.Add(p); } return output; }
    private async Task ModifyPdf(string operation)
    {
        try { if (pdf is null) return; using var input = new MemoryStream(PdfBytes); using var original = PdfReader.Open(input, PdfDocumentOpenMode.Import); using var output = new PdfSharp.Pdf.PdfDocument();
            if (operation == "Merge") { var picked = await UI.PickOpen(this, ".pdf"); if (picked is null) return; using var secondStream = new MemoryStream((await FileIO.ReadBufferAsync(picked)).ToArray()); using var second = PdfReader.Open(secondStream, PdfDocumentOpenMode.Import); foreach (var page in original.Pages) output.AddPage(page); foreach (var page in second.Pages) output.AddPage(page); using var merged = new MemoryStream(); output.Save(merged, false); SetPdf(merged.ToArray()); }
            else { var indices = SelectedPages(original.PageCount); if (operation == "Delete pages" && indices.Count == original.PageCount) throw new InvalidOperationException("Keep at least one page."); var order = operation == "Keep pages" ? indices : Enumerable.Range(0, original.PageCount).Where(i => operation != "Delete pages" || !indices.Contains(i)).ToList(); foreach (var i in order) { var page = output.AddPage(original.Pages[i]); if (operation == "Rotate" && indices.Contains(i)) page.Rotate = (page.Rotate + 90) % 360; } using var encoded = new MemoryStream(); output.Save(encoded, false); SetPdf(encoded.ToArray()); }
            await LoadPdf();
        } catch (Exception e) { await UI.Error(this, e); }
    }
}
public sealed class PresentationWindow : Window
{
    public PresentationWindow(OfficeFile file, int initial)
    {
        Title = "Gaius Slides — Presentation"; AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 760)); int index = initial;
        var panel = new StackPanel { Padding = new Thickness(90), Spacing = 32, VerticalAlignment = VerticalAlignment.Center };
        var root = new Grid { Background = UI.Brush("#ffffff") }; root.Children.Add(panel);
        var host = new ContentControl { Content = root, IsTabStop = true, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch }; Content = host;
        host.Loaded += (_, _) => host.Focus(FocusState.Programmatic);
        AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
        void Render() { panel.Children.Clear(); var s = file.Slides[index]!; root.Background = UI.Brush(s["background"]!.GetValue<string>()); panel.Children.Add(UI.Text(s["title"]!.GetValue<string>(), 48)); panel.Children.Add(UI.Text(s["body"]!.GetValue<string>(), 28, "#616161")); panel.Children.Add(UI.Text($"{index + 1} / {file.Slides.Count} · Use arrow keys to navigate", 12, "#888888")); }
        host.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Right || e.Key == Windows.System.VirtualKey.Space) index = Math.Min(file.Slides.Count - 1, index + 1); if (e.Key == Windows.System.VirtualKey.Left) index = Math.Max(0, index - 1); if (e.Key == Windows.System.VirtualKey.Escape) Close(); Render(); }; Render();
    }
}
