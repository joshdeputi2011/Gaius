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
using Microsoft.UI.Text;
using Symbol = Microsoft.UI.Xaml.Controls.Symbol;
using PdfSharp.Pdf.IO;
namespace Gaius.Office;
public sealed partial class EditorWindow : Window
{
    private readonly OfficeFile file;
    public string Kind => file.Kind;
    public void SelectRibbonTab(string tab) => ribbon.Select(tab);
    public async Task RunStartAction(string action) {
        if (Kind == "pdf" && action == "compress") { await EnsureInitialPdf(); if (!WindowLifetime.IsClosed(this)) await PdfAction(CompressPdf); }
        else ribbon.Select(action == "compress" ? "Compress" : "Convert");
    }
    private readonly StackPanel toolbar;
    private readonly OfficeRibbon ribbon;
    private readonly Grid body = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly TextBlock status = UI.Text("Saved on this device", 10, "#777777");
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private string? savedRevision;
    private bool dirty, allowClose, closingPrompt, applyingViewFormat;
    private Action flushEditor = () => { };
    public EditorWindow(OfficeFile document, string? revision = null)
    {
        file = document; savedRevision = revision; Title = $"{file.Title} — Gaius {UI.Labels[file.Kind]}"; AppWindow.Resize(new Windows.Graphics.SizeInt32(1200, 850));
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ribbon = new OfficeRibbon(UI.Accent(file.Kind)); toolbar = ribbon.Home;
        var title = new TextBox { Text = file.Title, MinWidth = 140, MaxWidth = 500, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Foreground = UI.Brush("#25344b"), BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(title, "Document name"); title.TextChanged += (_, _) => { file.Title = title.Text; Title = $"{file.Title} ? Gaius {UI.Labels[file.Kind]}"; Changed(); };
        var titleBar = WindowChrome.Create(this, "Gaius " + UI.Labels[file.Kind], file.Kind, title);
        titleBar.LeftHeader = UI.Button("Home", () => App.ShowHome(), Symbol.Home); root.Children.Add(titleBar);
        Grid.SetRow(ribbon, 1); root.Children.Add(ribbon); Grid.SetRow(body, 2); root.Children.Add(body);
        var footer = BuildStatusBar(); Grid.SetRow(footer, 3); root.Children.Add(footer); Content = root;
        switch (file.Kind) { case "document": BuildWrite(); break; case "spreadsheet": BuildSheet(); break; case "presentation": BuildSlides(); break; case "image": BuildImage(); break; case "pdf": BuildPdf(); break; }
        BuildDrawRibbon();
        ribbon.SelectionChanged += tab => { if (tab != "Draw") { inkTool = InlineInkTool.Select; ApplyInkTool(); } };
        if (file.Data["importWarning"]?.ToString() is { Length: > 0 } warning) {
            root.RowDefinitions.Insert(2, new RowDefinition { Height = GridLength.Auto }); Grid.SetRow(body, 3); Grid.SetRow(footer, 4);
            var information = new InfoBar { IsOpen = true, IsClosable = true, Severity = InfoBarSeverity.Informational, Title = "Imported content", Message = warning, Margin = new Thickness(14, 0, 14, 8) }; Grid.SetRow(information, 2); root.Children.Add(information);
        }
        ribbon.OrderTabs(file.Kind switch {
            "document" => new[] { "Home", "Insert", "Draw", "Design", "Layout", "References", "Review", "View" },
            "spreadsheet" => new[] { "Home", "Insert", "Draw", "Page Layout", "Formulas", "Data", "Review", "View" },
            "presentation" => new[] { "Home", "Insert", "Draw", "Design", "Transitions", "Animations", "Slide Show", "Review", "View" },
            "pdf" => new[] { "Home", "Draw", "Organize", "Edit", "Convert", "Compress", "View" },
            _ => new[] { "Home", "Draw", "Transform", "Adjust", "Annotate", "Compress", "Convert", "View" }
        });
        ribbon.Menu("New", () => App.Show(new EditorWindow(OfficeFile.Create(file.Kind))), Symbol.Add);
        ribbon.Menu("Open", async () => { var homeWindow = App.ShowHome(); await homeWindow.Import(); }, Symbol.OpenFile);
        ribbon.Menu("Save", async () => await SaveCurrent(), Symbol.Save);
        ribbon.Menu("Save as", async () => await ExportGaius(), Symbol.Save);
        ribbon.Menu("Share", async () => { try { flushEditor(); await NativeShare.Show(this, file); } catch (Exception e) { await UI.Error(this, e); } }, Symbol.Share);
        ribbon.Menu("Print preview", () => App.Show(new PrintPreviewWindow(PrintSnapshot())), Symbol.Print);
        ribbon.Menu("New window", () => { flushEditor(); var copy = file.Copy(); copy.Data["id"] = Guid.NewGuid().ToString(); copy.Title += " ? Copy"; App.Show(new EditorWindow(copy)); }, Symbol.NewWindow);
        if (file.Kind is "document" or "spreadsheet" or "presentation") ribbon.Menu("Export " + OfficeInterchange.Extension(file.Kind)[1..].ToUpperInvariant(), async () => { try { flushEditor(); var snapshot = file.Copy(); if (!await ConfirmOfficeExport(snapshot, OfficeInterchange.Extension(file.Kind))) return; var bytes = await Task.Run(() => OfficeInterchange.Export(snapshot)); var target = await UI.PickSave(this, file.Title, OfficeInterchange.Extension(file.Kind)); if (target is not null) await FileIO.WriteBytesAsync(target, bytes); } catch (Exception error) { await UI.Error(this, error); } }, Symbol.Download);
        if (file.Kind == "document") ribbon.Menu("Export ODT", async () => { try { flushEditor(); var snapshot = file.Copy(); if (!await ConfirmOfficeExport(snapshot, ".odt")) return; var bytes = await Task.Run(() => OfficeInterchange.ExportOdt(snapshot)); var target = await UI.PickSave(this, file.Title, ".odt"); if (target is not null) await FileIO.WriteBytesAsync(target, bytes); } catch (Exception error) { await UI.Error(this, error); } }, Symbol.Download);
        if (file.Data["originalFile"] is JsonObject original) ribbon.Menu("Save original " + original["extension"]!.ToString().ToUpperInvariant() + " copy", async () => { try { var target = await UI.PickSave(this, file.Title, original["extension"]!.ToString()); if (target is not null) await FileIO.WriteBytesAsync(target, Convert.FromBase64String(original["dataBase64"]!.ToString())); } catch (Exception error) { await UI.Error(this, error); } });
        ribbon.Menu("Settings", async () => await SuiteSettings.Show(this), Symbol.Setting);
        ribbon.Menu("Close", () => Close());
        void Shortcut(Windows.System.VirtualKey key, Windows.System.VirtualKeyModifiers modifiers, Action action) { var accelerator = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = key, Modifiers = modifiers }; accelerator.Invoked += (_, e) => { e.Handled = true; action(); }; root.KeyboardAccelerators.Add(accelerator); }
        Shortcut(Windows.System.VirtualKey.S, Windows.System.VirtualKeyModifiers.Control, async () => await SaveCurrent());
        Shortcut(Windows.System.VirtualKey.S, Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift, async () => await ExportGaius());
        Shortcut(Windows.System.VirtualKey.P, Windows.System.VirtualKeyModifiers.Control, () => App.Show(new PrintPreviewWindow(PrintSnapshot())));
        timer.Tick += async (_, _) => { timer.Stop(); await SaveCurrent(); };
        Closed += (_, _) => { ribbon.Dispose(); timer.Stop(); imageGeneration++; imageRendering?.Cancel(); pdfGeneration++; pdfCancellation?.Cancel(); _ = ReleasePdfAfterRender(); };
        AppWindow.Closing += async (_, args) => {
            if (allowClose || !dirty) { WindowLifetime.DismissDialog(this); return; } args.Cancel = true; timer.Stop(); if (closingPrompt) return; closingPrompt = true;
            try {
                WindowLifetime.DismissDialog(this);
                var dialog = new ContentDialog { Title = "Save changes?", Content = $"Save “{file.Title}” before closing?", PrimaryButtonText = "Save", SecondaryButtonText = "Discard", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
                var result = await WindowLifetime.ShowDialog(this, dialog);
                if (!WindowLifetime.IsClosed(this) && (result == ContentDialogResult.Secondary || result == ContentDialogResult.Primary && await SaveCurrent())) { allowClose = true; Close(); }
            } catch (Exception error) { await UI.Error(this, error); }
            finally { closingPrompt = false; if (!WindowLifetime.IsClosed(this) && dirty && savedRevision is not null) timer.Start(); }
        };
        EnsureZoomHost();
        if (revision is null) Changed();
    }
    private void Changed() { if (applyingViewFormat || WindowLifetime.IsClosed(this)) return; file.Data["updatedAt"] = DateTime.UtcNow.ToString("O"); dirty = true; status.Text = savedRevision is null ? "Unsaved new file" : "Saving on this device…"; timer.Stop(); if (savedRevision is not null) timer.Start(); }
    private async Task<bool> SaveCurrent()
    {
        await saveGate.WaitAsync();
        try { if (WindowLifetime.IsClosed(this)) return false; if (!dirty) return true; flushEditor(); var snapshot = file.Copy(); await DocumentStore.SaveAsync(snapshot, savedRevision); savedRevision = snapshot.Revision; if (snapshot.Revision == file.Revision) { dirty = false; if (!WindowLifetime.IsClosed(this)) status.Text = "Saved on this device"; } return !dirty; }
        catch (Exception e) { if (!WindowLifetime.IsClosed(this)) status.Text = "Save failed — export a copy"; await UI.Error(this, e); return false; }
        finally { saveGate.Release(); }
    }
    private async Task ExportGaius() { try { flushEditor(); var target = await UI.PickSave(this, file.Title, ".gaius"); if (target is not null) await FileIO.WriteTextAsync(target, file.Copy().Data.ToJsonString()); } catch (Exception e) { await UI.Error(this, e); } }
    private async Task ExportText(string text, string extension) { try { var target = await UI.PickSave(this, file.Title, extension); if (target is not null) await FileIO.WriteTextAsync(target, text); } catch (Exception e) { await UI.Error(this, e); } }
    private void BuildWrite()
    {
        var editor = new RichEditBox { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Foreground = Appearance.PaperText, BorderThickness = new Thickness(0), Padding = new Thickness(64), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 1050, FontFamily = new FontFamily("Calibri"), FontSize = 16 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(editor, "Document content");
        bool loaded = false;
        editor.Loaded += (_, _) => { if (loaded) return; RichTextInterop.Load(editor, file); loaded = true; editor.TextChanged += (_, _) => Changed(); };
        flushEditor = () => { if (loaded) { snapshotWriteWrapping(); RichTextInterop.Save(editor, file); } };
        var clipboard = ribbon.Group("Home", "Clipboard");
        clipboard.Children.Add(UI.RibbonButton("Paste", async () => { try { if (Windows.ApplicationModel.DataTransfer.Clipboard.GetContent().Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Bitmap)) { var picture = await ClipboardPicture(); if (picture is not null) await insertWriteObject("image", picture); } else editor.Document.Selection.Paste(0); } catch (Exception error) { await UI.Error(this, error); } }, Symbol.Paste));
        var clips = new StackPanel { Spacing = 4 }; clips.Children.Add(UI.Button("Cut", () => editor.Document.Selection.Cut(), Symbol.Cut)); clips.Children.Add(UI.Button("Copy", () => editor.Document.Selection.Copy(), Symbol.Copy)); clipboard.Children.Add(clips);
        var fontGroup = ribbon.Group("Home", "Font"); var fontRows = new StackPanel { Spacing = 6 }; fontGroup.Children.Add(fontRows);
        var familyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 }; fontRows.Children.Add(familyRow);
        var fonts = new ComboBox { ItemsSource = new[] { "Calibri", "Arial", "Georgia", "Times New Roman", "Verdana", "Courier New" }, SelectedIndex = 0, Width = 145, MinHeight = 30 }; fonts.SelectionChanged += (_, _) => { if (!loaded) return; editor.Document.Selection.CharacterFormat.Name = (string)fonts.SelectedItem; Changed(); }; familyRow.Children.Add(fonts);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(fonts, "Font family");
        var sizes = new ComboBox { ItemsSource = new[] { 8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 48, 72 }, SelectedItem = 12, Width = 65, MinHeight = 30 }; sizes.SelectionChanged += (_, _) => { if (!loaded) return; editor.Document.Selection.CharacterFormat.Size = Convert.ToSingle(sizes.SelectedItem); Changed(); }; familyRow.Children.Add(sizes);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(sizes, "Font size");
        var formatting = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 }; fontRows.Children.Add(formatting);
        formatting.Children.Add(UI.CompactButton("Bold", () => { editor.Document.Selection.CharacterFormat.Bold = FormatEffect.Toggle; editor.Focus(FocusState.Programmatic); Changed(); }, Symbol.Bold));
        formatting.Children.Add(UI.CompactButton("Italic", () => { editor.Document.Selection.CharacterFormat.Italic = FormatEffect.Toggle; editor.Focus(FocusState.Programmatic); Changed(); }, Symbol.Italic));
        formatting.Children.Add(UI.CompactButton("Underline", () => { var format = editor.Document.Selection.CharacterFormat; format.Underline = format.Underline == UnderlineType.None ? UnderlineType.Single : UnderlineType.None; editor.Focus(FocusState.Programmatic); Changed(); }, Symbol.Underline));
        formatting.Children.Add(UI.ColorButton("Text color", color => { editor.Document.Selection.CharacterFormat.ForegroundColor = color; editor.Focus(FocusState.Programmatic); Changed(); }));
        formatting.Children.Add(UI.ColorButton("Highlight", color => { editor.Document.Selection.CharacterFormat.BackgroundColor = color; editor.Focus(FocusState.Programmatic); Changed(); }));
        var paragraph = ribbon.Group("Home", "Paragraph"); var paragraphRows = new StackPanel { Spacing = 5 }; paragraph.Children.Add(paragraphRows); var aligns = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 }; paragraphRows.Children.Add(aligns);
        foreach (var (label, value, icon) in new[] { ("Align left", ParagraphAlignment.Left, Symbol.AlignLeft), ("Center", ParagraphAlignment.Center, Symbol.AlignCenter), ("Align right", ParagraphAlignment.Right, Symbol.AlignRight), ("Justify", ParagraphAlignment.Justify, (Symbol)0xE8E1) }) { var align = value; aligns.Children.Add(UI.CompactButton(label, () => { editor.Document.Selection.ParagraphFormat.Alignment = align; editor.Focus(FocusState.Programmatic); Changed(); }, icon)); }
        var lists = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 }; paragraphRows.Children.Add(lists);
        lists.Children.Add(UI.CompactButton("Bulleted list", () => { editor.Document.Selection.ParagraphFormat.ListType = MarkerType.Bullet; editor.Focus(FocusState.Programmatic); Changed(); }, Symbol.List));
        lists.Children.Add(UI.Button("Numbering", () => { editor.Document.Selection.ParagraphFormat.ListType = MarkerType.Arabic; editor.Focus(FocusState.Programmatic); Changed(); }));
        var style = new ComboBox { ItemsSource = new[] { "Normal", "Title", "Heading 1", "Heading 2" }, SelectedIndex = 0, Width = 130 };
        style.SelectionChanged += (_, _) => { if (!loaded) return; var format = editor.Document.Selection.CharacterFormat; format.Size = style.SelectedIndex switch { 1 => 36, 2 => 28, 3 => 20, _ => 12 }; format.Bold = style.SelectedIndex == 0 ? FormatEffect.Off : FormatEffect.On; editor.Focus(FocusState.Programmatic); Changed(); }; ribbon.Group("Home", "Styles").Children.Add(style);
        var editing = ribbon.Group("Home", "Editing"); var editRows = new StackPanel { Spacing = 4 }; editRows.Children.Add(UI.Button("Find / replace", async () => await FindReplace(editor), Symbol.Find)); editRows.Children.Add(UI.Button("Select all", () => { editor.Document.Selection.SetRange(0, int.MaxValue); editor.Focus(FocusState.Programmatic); }, Symbol.SelectAll)); editing.Children.Add(editRows);
        var history = ribbon.Group("Home", "History"); history.Children.Add(UI.CompactButton("Undo", () => editor.Document.Undo(), Symbol.Undo)); history.Children.Add(UI.CompactButton("Redo", () => editor.Document.Redo(), Symbol.Redo));
        ribbon.OrderGroups("Home", "Clipboard", "Font", "Paragraph", "Styles", "Editing", "History");
        ribbon.Menu("Export text", async () => { flushEditor(); await ExportText(file.Content["text"]!.GetValue<string>(), ".txt"); }, Symbol.Download);
        var paper = new Border { Width = 794, Background = Appearance.PaperBrush, Margin = new Thickness(32), Child = editor };
        void ThemePaper() { paper.Background = Appearance.PaperBrush; editor.Foreground = Appearance.PaperText; }
        Appearance.Changed += ThemePaper; Closed += (_, _) => Appearance.Changed -= ThemePaper;
        var scroll = new ScrollViewer { Content = paper, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled }; body.Children.Add(scroll); WriteCommands(editor, paper, scroll);
        AttachDocumentInk(paper);
        insertDrawing = bytes => insertWriteObject("image", "data:image/png;base64," + Convert.ToBase64String(bytes));
        editor.TextChanged += (_, _) => { editor.Document.GetText(TextGetOptions.None, out var text); details.Text = $"{System.Text.RegularExpressions.Regex.Matches(text, @"\S+").Count} words"; };
    }
    private void BuildSheet() => BuildNativeSheet();
    private void BuildSlides() => BuildNativeSlides();
    private readonly Microsoft.UI.Xaml.Controls.Image imagePreview = new() { Stretch = Stretch.Uniform, Margin = new Thickness(30) };
    private byte[]? renderedImage;
    private int imageGeneration;
    private CancellationTokenSource? imageRendering;
    private void BuildImage()
    {
        toolbar.Children.Add(UI.Button("Open image", async () => { try { var picked = await UI.PickOpen(this, ".png", ".jpg", ".jpeg", ".bmp", ".img"); if (picked is null) return; var bytes = (await FileIO.ReadBufferAsync(picked)).ToArray(); if (bytes.Length > 40 * 1024 * 1024) throw new IOException("Choose an image smaller than 40 MB."); file.Image["dataUrl"] = $"data:{await OfficeQuickTools.ImageMime(bytes)};base64,{Convert.ToBase64String(bytes)}"; Changed(); await RenderImage(); } catch (Exception e) { await UI.Error(this, e); } }, Symbol.OpenFile));
        toolbar.Children.Add(UI.Button("Rotate", async () => { file.Image["rotation"] = file.Image["rotation"]!.GetValue<double>() + 90; Changed(); await RenderImage(); }, Symbol.Rotate));
        foreach (var key in new[] { "brightness", "contrast" }) { var group = new StackPanel { Width = 150 }; group.Children.Add(UI.Text(key, 10, "#777777")); var slider = new Slider { Minimum = 0, Maximum = 200, Value = file.Image[key]!.GetValue<double>() }; slider.ValueChanged += async (_, e) => { file.Image[key] = e.NewValue; Changed(); await RenderImage(); }; group.Children.Add(slider); toolbar.Children.Add(group); }
        var grayscale = new CheckBox { Content = "Grayscale", IsChecked = file.Image["grayscale"]!.GetValue<bool>() }; grayscale.Click += async (_, _) => { file.Image["grayscale"] = grayscale.IsChecked == true; Changed(); await RenderImage(); }; toolbar.Children.Add(grayscale);
        body.Children.Add(imagePreview);
        var rotate = toolbar.Children[1]; toolbar.Children.Remove(rotate); ribbon.Panel("Transform").Children.Add(rotate);
        foreach (var control in toolbar.Children.Skip(1).Take(3).ToList()) { toolbar.Children.Remove(control); ribbon.Panel("Adjust").Children.Add(control); }
        Command("Convert", "Save PNG", async () => { try { if (renderedImage is null) return; var target = await UI.PickSave(this, file.Title, ".png"); if (target is not null) await FileIO.WriteBytesAsync(target, renderedImage); } catch (Exception e) { await UI.Error(this, e); } });
        ImageCommands(); ViewCommands();
        ((FrameworkElement)Content).Loaded += async (_, _) => await RenderImage();
    }
    private async Task RenderImage()
    {
        var generation = ++imageGeneration;
        imageRendering?.Cancel(); using var cancellation = new CancellationTokenSource(); imageRendering = cancellation; var token = cancellation.Token;
        try { var data = file.Image["dataUrl"]!.GetValue<string>(); if (string.IsNullOrEmpty(data)) return; using var source = new MemoryStream(Convert.FromBase64String(data[(data.IndexOf(',') + 1)..])); var decoder = await BitmapDecoder.CreateAsync(source.AsRandomAccessStream());
            token.ThrowIfCancellationRequested();
            if ((long)decoder.PixelWidth * decoder.PixelHeight > 40000000) throw new IOException("Image limit: 40 megapixels.");
            var rotation = ((int)file.Image["rotation"]!.GetValue<double>() % 360 + 360) % 360; var transform = new BitmapTransform { Rotation = rotation switch { 90 => BitmapRotation.Clockwise90Degrees, 180 => BitmapRotation.Clockwise180Degrees, 270 => BitmapRotation.Clockwise270Degrees, _ => BitmapRotation.None } };
            var pixels = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
            token.ThrowIfCancellationRequested();
            var brightness = file.Image["brightness"]!.GetValue<double>() / 100; var contrast = file.Image["contrast"]!.GetValue<double>() / 100; bool gray = file.Image["grayscale"]!.GetValue<bool>();
            bool flipX = file.Image["flipX"]?.GetValue<bool>() ?? false, flipY = file.Image["flipY"]?.GetValue<bool>() ?? false; int blur = Math.Clamp((int)(file.Image["blur"]?.GetValue<double>() ?? 0), 0, 20);
            int width = (int)(rotation is 90 or 270 ? decoder.PixelHeight : decoder.PixelWidth), height = (int)(rotation is 90 or 270 ? decoder.PixelWidth : decoder.PixelHeight);
            pixels = await Task.Run(() => ImageEffects(pixels, width, height, brightness, contrast, gray, flipX, flipY, blur, token), token);
            token.ThrowIfCancellationRequested();
            using var stream = new InMemoryRandomAccessStream(); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream); bool swap = rotation is 90 or 270; encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, swap ? decoder.PixelHeight : decoder.PixelWidth, swap ? decoder.PixelWidth : decoder.PixelHeight, 96, 96, pixels); await encoder.FlushAsync(); stream.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream); stream.Seek(0); var bytes = new byte[(int)stream.Size]; await stream.AsStreamForRead().ReadExactlyAsync(bytes); if (generation != imageGeneration || WindowLifetime.IsClosed(this)) return; renderedImage = bytes; imagePreview.Source = bitmap;
        } catch (OperationCanceledException) { }
        catch (Exception e) { if (generation == imageGeneration) await UI.Error(this, e); }
        finally { if (ReferenceEquals(imageRendering, cancellation)) imageRendering = null; }
    }
    private Windows.Data.Pdf.PdfDocument? pdf;
    private MemoryStream? pdfSource;
    private IRandomAccessStream? pdfSourceAdapter;
    private int pdfGeneration;
    private readonly SemaphoreSlim pdfLoadGate = new(1, 1);
    private readonly SemaphoreSlim pdfRenderGate = new(1, 1);
    private Task? initialPdfLoad;
    private Task EnsureInitialPdf() => initialPdfLoad ??= LoadPdf();
    private int pageIndex;
    private readonly TextBox pageRange = new() { Text = "1", Width = 100, PlaceholderText = "1,3-5" };
    private readonly Microsoft.UI.Xaml.Controls.Image pdfPreview = new() { Stretch = Stretch.Uniform, Margin = new Thickness(24) };
    private byte[] PdfBytes => Convert.FromBase64String(file.Data["pdf"]!["dataUrl"]!.GetValue<string>().Split(',').Last());
    private void BuildPdf()
    {
        toolbar.Children.Add(UI.Button("Open PDF", async () => { try { if (pdfBusy) return; var picked = await UI.PickOpen(this, ".pdf"); if (picked is null) return; var bytes = (await FileIO.ReadBufferAsync(picked)).ToArray(); if (bytes.Length > 40 * 1024 * 1024) throw new IOException("Choose a PDF smaller than 40 MB."); SetPdf(bytes, resetInk: true); await LoadPdf(); } catch (Exception e) { await UI.Error(this, e); } }, Symbol.OpenFile));
        toolbar.Children.Add(UI.Button("Previous", async () => { if (pdf is null) return; pageIndex = Math.Max(0, pageIndex - 1); await RenderPdfPage(); }, Symbol.Back)); toolbar.Children.Add(UI.Button("Next", async () => { if (pdf is null) return; pageIndex = Math.Min((int)pdf.PageCount - 1, pageIndex + 1); await RenderPdfPage(); }, Symbol.Forward)); toolbar.Children.Add(pageRange);
        foreach (var op in new[] { "Merge", "Rotate", "Keep pages", "Delete pages" }) { var action = op; toolbar.Children.Add(UI.Button(action, async () => await ModifyPdf(action))); }
        toolbar.Children.Add(UI.Button("Export PDF", async () => { try { if (pdf is null) return; var target = await UI.PickSave(this, file.Title, ".pdf"); if (target is not null) await FileIO.WriteBytesAsync(target, PdfExportBytes()); } catch (Exception e) { await UI.Error(this, e); } }, Symbol.Download)); body.Children.Add(CreatePdfInkSurface()); ((FrameworkElement)Content).Loaded += async (_, _) => await EnsureInitialPdf();
        foreach (var control in toolbar.Children.Skip(3).Take(5).ToList()) { toolbar.Children.Remove(control); ribbon.Panel("Organize").Children.Add(control); }
        Command("Convert", "Page to PNG", async () => { try { if (pdf is null) return; var target = await UI.PickSave(this, file.Title + "-" + (pageIndex + 1), ".png"); if (target is null) return; using var page = pdf.GetPage((uint)pageIndex); using var stream = new InMemoryRandomAccessStream(); await page.RenderToStreamAsync(stream); stream.Seek(0); var bytes = new byte[(int)stream.Size]; await stream.AsStreamForRead().ReadExactlyAsync(bytes); await FileIO.WriteBytesAsync(target, bytes); } catch (Exception e) { await UI.Error(this, e); } });
        PdfCommands(); ViewCommands();
    }
    private void SetPdf(byte[] bytes, bool remember = true, bool resetInk = false) { if (remember && !string.IsNullOrEmpty(file.Data["pdf"]!["dataUrl"]?.ToString())) { pdfUndo = PdfBytes; pdfUndoInk = file.Data["drawings"]?.DeepClone(); } if (resetInk && file.Data["drawings"] is JsonArray drawings) foreach (var ink in drawings.OfType<JsonObject>().Where(d => d["surface"]?.ToString() == "pdf").ToArray()) drawings.Remove(ink); file.Data["pdf"]!["dataUrl"] = "data:application/pdf;base64," + Convert.ToBase64String(bytes); Changed(); }
    private void ReleasePdfSource() { pdf = null; pdfSourceAdapter?.Dispose(); pdfSource?.Dispose(); pdfSourceAdapter = null; pdfSource = null; }
    private async Task ReleasePdfAfterRender() { if (pdfActionCompletion is { } action) await action.Task; await pdfRenderGate.WaitAsync(); try { ReleasePdfSource(); } finally { pdfRenderGate.Release(); } }
    private async Task LoadPdf() {
        await pdfLoadGate.WaitAsync();
        try {
            if (WindowLifetime.IsClosed(this) || string.IsNullOrEmpty(file.Data["pdf"]!["dataUrl"]!.GetValue<string>())) return;
            var source = new MemoryStream(PdfBytes); var adapter = source.AsRandomAccessStream();
            try {
                var loaded = await Windows.Data.Pdf.PdfDocument.LoadFromStreamAsync(adapter);
                await pdfRenderGate.WaitAsync();
                try { if (WindowLifetime.IsClosed(this)) { adapter.Dispose(); source.Dispose(); return; } ReleasePdfSource(); pdfSource = source; pdfSourceAdapter = adapter; pdf = loaded; }
                finally { pdfRenderGate.Release(); }
                pageIndex = Math.Clamp(pageIndex, 0, (int)pdf.PageCount - 1); await RenderPdfPage();
            } catch { if (pdfSource != source) { adapter.Dispose(); source.Dispose(); } throw; }
        } catch (Exception e) { await UI.Error(this, e); }
        finally { pdfLoadGate.Release(); }
    }
    private async Task RenderPdfPage() {
        var generation = ++pdfGeneration; await pdfRenderGate.WaitAsync();
        try {
            if (pdf is null || WindowLifetime.IsClosed(this) || generation != pdfGeneration) return;
            int index = pageIndex, count = (int)pdf.PageCount;
            using var page = pdf.GetPage((uint)index); using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream); stream.Seek(0); var image = new BitmapImage(); await image.SetSourceAsync(stream);
            if (generation != pdfGeneration || WindowLifetime.IsClosed(this)) return;
            pdfPreview.Source = image; RefreshPdfInkPage(image.PixelWidth, image.PixelHeight); status.Text = $"Page {index + 1} of {count} · Local file";
        } catch (Exception error) { if (!WindowLifetime.IsClosed(this) && generation == pdfGeneration) await UI.Error(this, error); }
        finally { pdfRenderGate.Release(); }
    }
    private List<int> SelectedPages(int count) { var output = new List<int>(); foreach (var part in pageRange.Text.Split(',')) { var m = System.Text.RegularExpressions.Regex.Match(part.Trim(), "^(\\d+)(?:-(\\d+))?$"); if (!m.Success) throw new FormatException("Use page numbers like 1,3-5."); int start = int.Parse(m.Groups[1].Value), end = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : start; if (start < 1 || end > count || end < start) throw new FormatException($"Choose pages between 1 and {count}."); for (int p = start - 1; p < end; p++) if (!output.Contains(p)) output.Add(p); } return output; }
    private async Task ModifyPdf(string operation)
    {
        try { if (pdf is null || pdfBusy) return; using var input = new MemoryStream(PdfBytes); using var original = PdfReader.Open(input, PdfDocumentOpenMode.Import); using var output = new PdfSharp.Pdf.PdfDocument();
            if (operation == "Merge") { var picked = await UI.PickOpen(this, ".pdf"); if (picked is null) return; using var secondStream = new MemoryStream((await FileIO.ReadBufferAsync(picked)).ToArray()); using var second = PdfReader.Open(secondStream, PdfDocumentOpenMode.Import); foreach (var page in original.Pages) output.AddPage(page); foreach (var page in second.Pages) output.AddPage(page); using var merged = new MemoryStream(); output.Save(merged, false); SetPdf(merged.ToArray()); }
            else { var indices = SelectedPages(original.PageCount); if (operation == "Delete pages" && indices.Count == original.PageCount) throw new InvalidOperationException("Keep at least one page."); var order = operation == "Keep pages" ? indices : Enumerable.Range(0, original.PageCount).Where(i => operation != "Delete pages" || !indices.Contains(i)).ToList(); foreach (var i in order) { var page = output.AddPage(original.Pages[i]); if (operation == "Rotate" && indices.Contains(i)) page.Rotate = (page.Rotate + 90) % 360; } using var encoded = new MemoryStream(); output.Save(encoded, false); SetPdf(encoded.ToArray()); RemapPdfInk(order, operation == "Rotate" ? indices.ToHashSet() : null); }
            await LoadPdf();
        } catch (Exception e) { await UI.Error(this, e); }
    }
}
