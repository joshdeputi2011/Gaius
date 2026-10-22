using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Printing;
using Windows.Graphics.Printing;
using Windows.Storage.Streams;
using Windows.Storage;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json.Nodes;
using PdfSharp.Drawing;

namespace Gaius.Office;

public sealed class PrintPreviewWindow : Window
{
    private readonly OfficeFile file;
    private readonly StackPanel pages = new() { Spacing = 24, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly PrintDocument document = new();
    private PrintManager? manager;
    private double pageWidth = 794, pageHeight = 1123;
    private readonly List<BitmapImage> images = [];
    private DocumentPreview? richPreview;
    private bool closed, ready, exporting;
    public PrintPreviewWindow(OfficeFile snapshot)
    {
        file = snapshot; Title = file.Title + " — Print preview"; AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 900));
        if (file.Data["layout"]?["pageSize"]?.ToString() is "letter" or "legal") { pageWidth = 816; pageHeight = file.Data["layout"]?["pageSize"]?.ToString() == "legal" ? 1344 : 1056; }
        if (file.Kind == "presentation" || file.Data["layout"]?["orientation"]?.GetValue<string>() == "landscape") (pageWidth, pageHeight) = (pageHeight, pageWidth);
        var root = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) }; root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition());
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Padding = new Thickness(20, 12, 20, 12) }; bar.Children.Add(UI.Text("Print preview", 18));
        bar.Children.Add(UI.Button("Print", async () => { try { if (!PrintManager.IsSupported()) throw new InvalidOperationException("Printing is unavailable on this device."); await PrintManagerInterop.ShowPrintUIForWindowAsync(WinRT.Interop.WindowNative.GetWindowHandle(this)); } catch (Exception e) { await UI.Error(this, e); } }, Symbol.Print));
        bar.Children.Add(UI.Button("Save PDF", async () => await SavePdf(), Symbol.Save));
        bar.Children.Add(UI.Text(file.Kind == "pdf" ? "Review your pages before saving a PDF copy." : "Review the layout before saving. PDF pages are rendered images.", 12, "#69788d"));
        root.Children.Add(bar); var scroll = new ScrollViewer { Content = pages, Padding = new Thickness(24), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled }; Grid.SetRow(scroll, 1); root.Children.Add(scroll); Content = root;
        document.Paginate += (_, args) => {
            var description = ((PrintTaskOptions)args.PrintTaskOptions).GetPageDescription(0);
            pageWidth = description.PageSize.Width; pageHeight = description.PageSize.Height;
            BuildPages(); document.SetPreviewPageCount(pages.Children.Count, PreviewPageCountType.Final);
        };
        document.GetPreviewPage += (_, args) => document.SetPreviewPage(args.PageNumber, pages.Children[args.PageNumber - 1]);
        document.AddPages += (_, _) => { foreach (var page in pages.Children) document.AddPage(page); document.AddPagesComplete(); };
        ((FrameworkElement)Content).Loaded += async (_, _) => {
            try {
                await LoadImages(); if (closed) return; BuildPages(); ready = true;
                if (PrintManager.IsSupported()) { manager = PrintManagerInterop.GetForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)); manager.PrintTaskRequested += Requested; }
            } catch (Exception e) { if (!closed) await UI.Error(this, e); }
        };
        Closed += (_, _) => { closed = true; if (manager is not null) manager.PrintTaskRequested -= Requested; };
    }
    private async Task SavePdf()
    {
        if (closed || exporting || !ready) return; exporting = true;
        try {
            var target = await UI.PickSave(this, file.Title, ".pdf"); if (target is null || closed) return;
            if (file.Kind == "pdf") { var source = Convert.FromBase64String(file.Data["pdf"]!["dataUrl"]!.ToString().Split(',').Last()); await FileIO.WriteBytesAsync(target, InkData.ExportPdf(source, file.Data)); return; }
            using var output = new PdfSharp.Pdf.PdfDocument(); output.Info.Title = file.Title;
            foreach (var element in pages.Children.OfType<FrameworkElement>()) {
                if (closed) return;
                element.UpdateLayout(); var bitmap = new RenderTargetBitmap();
                await bitmap.RenderAsync(element, Math.Max(1, (int)Math.Round(element.ActualWidth * 1.5)), Math.Max(1, (int)Math.Round(element.ActualHeight * 1.5)));
                if (closed) return; var pixels = (await bitmap.GetPixelsAsync()).ToArray();
                if (bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0) throw new IOException("A page could not be rendered. Reopen the preview and try again.");
                var png = await BitmapTools.Pixels(pixels, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, png: true);
                using var source = new MemoryStream(png); using var image = XImage.FromStream(source); var page = output.AddPage();
                page.Width = XUnit.FromPoint(element.ActualWidth * .75); page.Height = XUnit.FromPoint(element.ActualHeight * .75);
                using var graphics = XGraphics.FromPdfPage(page); graphics.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
            }
            using var buffer = new MemoryStream(); output.Save(buffer, false); if (!closed) await FileIO.WriteBytesAsync(target, buffer.ToArray());
        } catch (Exception error) { if (!closed) await UI.Error(this, error); }
        finally { exporting = false; }
    }
    private void Requested(PrintManager sender, PrintTaskRequestedEventArgs args)
    {
        var task = args.Request.CreatePrintTask(file.Title, request => request.SetSource(document.DocumentSource));
        if (file.Kind == "presentation" || file.Data["layout"]?["orientation"]?.GetValue<string>() == "landscape") task.Options.Orientation = PrintOrientation.Landscape;
    }
    private Thickness Margins() {
        if (file.Kind != "document") return new Thickness(48);
        var layout = file.Data["layout"]; double preset = layout?["margins"]?.ToString() switch { "narrow" => 12.7, "wide" => 38.1, _ => 25.4 };
        double Side(string side) => SlideRenderer.Number(layout?["customMargins"]?[side], preset) * 96 / 25.4;
        return new Thickness(Side("left"), Side("top"), Side("right"), Side("bottom"));
    }
    private Border Paper(UIElement child) { var paper = new Border { Width = pageWidth, Height = pageHeight, Padding = Margins(), Background = UI.DocumentBrush("#ffffff"), Child = child }; if (file.Kind == "document") { paper.BorderBrush = UI.DocumentBrush(file.Data["layout"]?["borderColor"]?.ToString() ?? "#64748b"); paper.BorderThickness = new Thickness(SlideRenderer.Number(file.Data["layout"]?["borderWidth"])); } pages.Children.Add(paper); return paper; }
    private async Task LoadImages()
    {
        if (file.Kind == "document") { richPreview = new DocumentPreview(file.Content["html"]?.ToString() ?? ""); await richPreview.LoadImages(); }
        if (file.Kind == "pdf") {
            var data = file.Data["pdf"]!["dataUrl"]!.GetValue<string>(); if (data.Length == 0) return;
            using var source = new MemoryStream(InkData.ExportPdf(Convert.FromBase64String(data.Split(',').Last()), file.Data)); using var adapter = source.AsRandomAccessStream();
            var pdf = await Windows.Data.Pdf.PdfDocument.LoadFromStreamAsync(adapter);
            for (uint i = 0; i < pdf.PageCount; i++) { using var page = pdf.GetPage(i); using var stream = new InMemoryRandomAccessStream(); await page.RenderToStreamAsync(stream); stream.Seek(0); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(stream); images.Add(bitmap); }
        } else if (file.Kind == "image") {
            var data = file.Image["dataUrl"]!.GetValue<string>(); if (data.Length == 0) return;
            using var source = new MemoryStream(Convert.FromBase64String(data.Split(',').Last())); var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(source.AsRandomAccessStream()); images.Add(bitmap);
        }
    }
    private void BuildPages()
    {
        pages.Children.Clear();
        if (file.Kind is "image" or "pdf") { foreach (var image in images) Paper(new Microsoft.UI.Xaml.Controls.Image { Source = image, Stretch = Stretch.Uniform }); }
        else if (file.Kind == "presentation") {
            foreach (var slide in file.Slides.OfType<JsonObject>()) Paper(new Viewbox { Child = SlideRenderer.Create(slide), Stretch = Stretch.Uniform });
        } else if (file.Kind == "spreadsheet") PrintSheets();
        else {
            string text = file.Content["text"]!.GetValue<string>();
            int columnCount = file.Kind == "document" ? Math.Clamp((int)SlideRenderer.Number(file.Data["layout"]?["columns"], 1), 1, 4) : 1;
            double gap = SlideRenderer.Number(file.Data["layout"]?["columnGap"], 10) * 96 / 25.4;
            var margins = Margins(); double width = (pageWidth - margins.Left - margins.Right - gap * (columnCount - 1)) / columnCount;
            Grid ColumnPage() { var grid = new Grid { ColumnSpacing = gap }; for (int i = 0; i < columnCount; i++) grid.ColumnDefinitions.Add(new ColumnDefinition()); Paper(grid); return grid; }
            var page = ColumnPage(); int column = 0;
            var block = richPreview?.Create(width) ?? new RichTextBlock { FontSize = 14, Foreground = UI.DocumentBrush("#242424"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = false };
            if (richPreview is null) { var paragraph = new Paragraph(); paragraph.Inlines.Add(new Run { Text = text }); block.Blocks.Add(paragraph); }
            page.Children.Add(block); pages.UpdateLayout();
            RichTextBlockOverflow? previous = null;
            while ((previous?.HasOverflowContent ?? block.HasOverflowContent) && pages.Children.Count < 1000) {
                var overflow = new RichTextBlockOverflow();
                if (previous is null) block.OverflowContentTarget = overflow; else previous.OverflowContentTarget = overflow;
                column++; if (column == columnCount) { page = ColumnPage(); column = 0; }
                Grid.SetColumn(overflow, column); page.Children.Add(overflow); pages.UpdateLayout(); previous = overflow;
            }
        }
        if (pages.Children.Count == 0) Paper(new Grid());
        if (file.Kind == "document") {
            int inkPages = InkPreview.DocumentPageCount(file.Data, pageWidth, pageHeight);
            while (pages.Children.Count < Math.Min(inkPages, 1000)) Paper(new Grid());
            for (int i = 0; i < pages.Children.Count; i++) {
                var paper = (Border)pages.Children[i]; var child = paper.Child; var margin = paper.Padding; paper.Child = null; paper.Padding = new Thickness(0);
                var layers = new Grid(); if (child is FrameworkElement element) element.Margin = margin; layers.Children.Add(child);
                layers.Children.Add(InkPreview.Create(file.Data, "document", 0, pageWidth, pageHeight, verticalOffset: i * pageHeight)); paper.Child = layers;
            }
        }
    }
    private void PrintSheets()
    {
        var sheets = (file.Data["worksheets"] as JsonArray)?.OfType<JsonObject>().ToArray() ?? [file.Sheet];
        double availableWidth = pageWidth - 96, availableHeight = pageHeight - 138;
        foreach (var sheet in sheets) {
            var cells = (JsonObject)sheet["cells"]!; var used = cells.Where(p => !string.IsNullOrEmpty(p.Value?.ToString())).Select(p => SheetData.Position(p.Key)).ToArray();
            int rowCount = used.Select(p => p.Row).DefaultIfEmpty(1).Max(), columnCount = used.Select(p => p.Column + 1).DefaultIfEmpty(1).Max();
            double Width(int column) => Math.Clamp(SlideRenderer.Number(sheet["columnWidths"]?[OfficeFile.Column(column)], 112), 32, availableWidth);
            double Height(int row) => Math.Clamp(SlideRenderer.Number(sheet["rowHeights"]?[row.ToString()], 32), 20, 220);
            for (int firstColumn = 0; firstColumn < columnCount;) {
                int lastColumn = firstColumn; double totalWidth = 0;
                while (lastColumn < columnCount && (totalWidth + Width(lastColumn) <= availableWidth || lastColumn == firstColumn)) totalWidth += Width(lastColumn++);
                for (int firstRow = 1; firstRow <= rowCount;) {
                    if (pages.Children.Count >= 1000) throw new IOException("This workbook exceeds the 1,000-page preview limit. Export a smaller workbook.");
                    int lastRow = firstRow; double totalHeight = 0;
                    while (lastRow <= rowCount && totalHeight + Height(lastRow) <= availableHeight) totalHeight += Height(lastRow++);
                    if (lastRow == firstRow) lastRow++;
                    var stack = new StackPanel { Spacing = 12 }; var title = UI.Text(sheet["name"]?.ToString() ?? "Worksheet", 16); title.Foreground = UI.DocumentBrush("#24334a"); stack.Children.Add(title);
                    var grid = new Grid(); for (int column = firstColumn; column < lastColumn; column++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Width(column)) });
                    for (int row = firstRow; row < lastRow; row++) {
                        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Height(row)) });
                        for (int column = firstColumn; column < lastColumn; column++) {
                            string address = OfficeFile.Column(column) + row; var style = sheet["styles"]?[address];
                            var text = new TextBlock { Text = SheetData.Display(sheet, address), Foreground = UI.DocumentBrush(style?["color"]?.ToString() ?? "#24334a"), FontSize = 12, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = style?["bold"]?.GetValue<bool>() == true ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal, TextAlignment = style?["align"]?.ToString() switch { "right" => TextAlignment.Right, "center" => TextAlignment.Center, _ => TextAlignment.Left } };
                            var border = new Border { Background = UI.DocumentBrush(style?["background"]?.ToString() ?? "#ffffff"), BorderBrush = UI.DocumentBrush("#dbe3ee"), BorderThickness = new Thickness(0, 0, 1, 1), Padding = new Thickness(6, 4, 6, 4), Child = text };
                            Grid.SetRow(border, row - firstRow); Grid.SetColumn(border, column - firstColumn); grid.Children.Add(border);
                        }
                    }
                    stack.Children.Add(grid); Paper(stack); firstRow = lastRow;
                }
                firstColumn = lastColumn;
            }
            foreach (var chart in (sheet["charts"] as JsonArray ?? []).OfType<JsonObject>()) Paper(new Viewbox { Child = SheetChart.Create(sheet, chart), Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Top });
        }
    }
}
