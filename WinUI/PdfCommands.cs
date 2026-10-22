using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;
using Symbol = Microsoft.UI.Xaml.Controls.Symbol;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Gaius.Office;

public sealed partial class EditorWindow
{
    private bool pdfBusy;
    private CancellationTokenSource? pdfCancellation;
    private byte[]? pdfUndo;
    private System.Text.Json.Nodes.JsonNode? pdfUndoInk;
    private TaskCompletionSource? pdfActionCompletion;
    private async Task PdfAction(Func<Task> action)
    {
        if (pdfBusy || pdf is null || WindowLifetime.IsClosed(this)) return; pdfBusy = true; pdfCancellation = new(); pdfActionCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try { await action(); }
        catch (OperationCanceledException) { status.Text = "Cancelled"; }
        catch (Exception error) { await UI.Error(this, error); }
        finally { pdfBusy = false; pdfCancellation.Dispose(); pdfCancellation = null; pdfActionCompletion.TrySetResult(); }
    }
    private void PdfCommands()
    {
        foreach (var tab in new[] { "Home", "Organize", "Edit", "Convert", "Compress", "View" }) ribbon.Panel(tab);
        Command("Home", "Select text", async () => await PdfAction(SelectPdfText), Symbol.Font);
        Command("Home", "Search", async () => await PdfAction(SearchPdf), Symbol.Find);
        Command("Home", "Undo edit", async () => { if (pdfUndo is not null && !pdfBusy) { var previous = pdfUndo; pdfUndo = null; file.Data["drawings"] = pdfUndoInk?.DeepClone(); pdfUndoInk = null; SetPdf(previous, false); await LoadPdf(); } }, Symbol.Undo);
        Command("Home", "Cancel operation", () => pdfCancellation?.Cancel(), Symbol.Cancel);
        Command("Organize", "Reverse pages", async () => await PdfAction(async () => { using var input = new MemoryStream(PdfBytes); using var original = PdfReader.Open(input, PdfDocumentOpenMode.Import); using var output = new PdfSharp.Pdf.PdfDocument(); for (int i = original.PageCount - 1; i >= 0; i--) output.AddPage(original.Pages[i]); using var buffer = new MemoryStream(); output.Save(buffer, false); SetPdf(buffer.ToArray()); RemapPdfInk(Enumerable.Range(0, original.PageCount).Reverse().ToArray()); await LoadPdf(); }));
        Command("Edit", "Add text", async () => await PdfAction(() => AddPdfText(false)));
        Command("Edit", "Watermark", async () => await PdfAction(() => AddPdfText(true)));
        Command("Edit", "Replace word", async () => await PdfAction(ReplacePdfWord));
        Command("Edit", "Page numbers", async () => await PdfAction(async () => {
            using var stream = new MemoryStream(PdfBytes); using var output = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
            for (int i = 0; i < output.PageCount; i++) { using var graphics = XGraphics.FromPdfPage(output.Pages[i]); graphics.DrawString((i + 1).ToString(), new XFont("Arial", 10), XBrushes.DimGray, new XRect(0, output.Pages[i].Height.Point - 32, output.Pages[i].Width.Point, 20), XStringFormats.Center); }
            using var buffer = new MemoryStream(); output.Save(buffer, false); SetPdf(buffer.ToArray()); await LoadPdf();
        }));
        Command("Compress", "Target size", async () => await PdfAction(CompressPdf));
        Command("Compress", "Compact", async () => await PdfAction(async () => { using var input = new MemoryStream(PdfBytes); using var output = PdfReader.Open(input, PdfDocumentOpenMode.Modify); output.Options.CompressContentStreams = true; using var buffer = new MemoryStream(); output.Save(buffer, false); var bytes = buffer.ToArray(); if (bytes.Length < PdfBytes.Length) { SetPdf(bytes); await LoadPdf(); } await Message("Compact PDF", $"{PdfBytes.Length / 1024.0:N1} KB"); }));
        Command("Convert", "Extract text", async () => await PdfAction(async () => {
            var bytes = PdfBytes; var text = await Task.Run(() => { using var document = UglyToad.PdfPig.PdfDocument.Open(bytes); return string.Join("\n\n", document.GetPages().Select(page => ContentOrderTextExtractor.GetText(page))); }); await ExportText(text, ".txt");
        }));
        Command("Convert", "Image to PDF", async () => {
            try { var image = await UI.PickOpen(this, ".png", ".jpg", ".jpeg"); if (image is null) return; var bytes = (await FileIO.ReadBufferAsync(image)).ToArray(); using var input = new MemoryStream(bytes); using var source = XImage.FromStream(input); using var output = new PdfSharp.Pdf.PdfDocument(); var page = output.AddPage(); page.Width = XUnit.FromPoint(source.PointWidth); page.Height = XUnit.FromPoint(source.PointHeight); using (var graphics = XGraphics.FromPdfPage(page)) graphics.DrawImage(source, 0, 0, page.Width.Point, page.Height.Point); using var buffer = new MemoryStream(); output.Save(buffer, false); SetPdf(buffer.ToArray()); await LoadPdf(); } catch (Exception e) { await UI.Error(this, e); }
        });
    }
    private async Task SelectPdfText()
    {
        var bytes = PdfBytes; int index = pageIndex;
        var text = await Task.Run(() => { using var document = UglyToad.PdfPig.PdfDocument.Open(bytes); return ContentOrderTextExtractor.GetText(document.GetPage(index + 1)); });
        var content = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Width = 460, Height = 400 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(content, "PDF page text");
        await WindowLifetime.ShowDialog(this, new ContentDialog { Title = $"Page {index + 1} text", Content = content, CloseButtonText = "Close" });
    }
    private async Task SearchPdf()
    {
        var query = await Prompt("Find in PDF"); if (string.IsNullOrWhiteSpace(query)) return; var bytes = PdfBytes; var token = pdfCancellation!.Token;
        var matches = await Task.Run(() => { using var document = UglyToad.PdfPig.PdfDocument.Open(bytes); return document.GetPages().Where(page => { token.ThrowIfCancellationRequested(); return ContentOrderTextExtractor.GetText(page).Contains(query, StringComparison.OrdinalIgnoreCase); }).Select(page => page.Number).ToArray(); }, token);
        if (matches.Length == 0) { await Message("Find", "No matches."); return; }
        var list = new ListView { ItemsSource = matches.Select(n => "Page " + n).ToArray(), SelectedIndex = 0, MinWidth = 300, MaxHeight = 350 }; var dialog = new ContentDialog { Title = "Search results", Content = list, PrimaryButtonText = "Go to page", CloseButtonText = "Close" };
        if (await WindowLifetime.ShowDialog(this, dialog) == ContentDialogResult.Primary && list.SelectedIndex >= 0) { pageIndex = matches[list.SelectedIndex] - 1; await RenderPdfPage(); }
    }
    private async Task AddPdfText(bool watermark)
    {
        var text = new TextBox { Header = watermark ? "Watermark" : "Text", Text = watermark ? "Draft" : "", MinWidth = 300 };
        var x = new NumberBox { Header = "Left (%)", Value = watermark ? 25 : 10, Minimum = 0, Maximum = 95 }; var y = new NumberBox { Header = "Top (%)", Value = watermark ? 50 : 10, Minimum = 0, Maximum = 95 }; var size = new NumberBox { Header = "Font size", Value = watermark ? 40 : 16, Minimum = 6, Maximum = 120 };
        string hex = watermark ? "#a0a0a0" : "#242424"; var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(text); panel.Children.Add(x); panel.Children.Add(y); panel.Children.Add(size); panel.Children.Add(UI.ColorButton("Color", color => hex = $"#{color.R:x2}{color.G:x2}{color.B:x2}"));
        var dialog = new ContentDialog { Title = watermark ? "Watermark all pages" : $"Add text to page {pageIndex + 1}", Content = panel, PrimaryButtonText = "Apply", CloseButtonText = "Cancel" };
        if (await WindowLifetime.ShowDialog(this, dialog) != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(text.Text)) return;
        using var input = new MemoryStream(PdfBytes); using var output = PdfReader.Open(input, PdfDocumentOpenMode.Modify); var color = UI.Color(hex);
        foreach (int index in watermark ? Enumerable.Range(0, output.PageCount) : new[] { pageIndex }) { var page = output.Pages[index]; using var graphics = XGraphics.FromPdfPage(page); graphics.DrawString(text.Text, new XFont("Arial", double.IsFinite(size.Value) ? size.Value : 16), new XSolidBrush(XColor.FromArgb(color.R, color.G, color.B)), new XPoint(page.Width.Point * (double.IsFinite(x.Value) ? x.Value : 10) / 100, page.Height.Point * (double.IsFinite(y.Value) ? y.Value : 10) / 100)); }
        using var buffer = new MemoryStream(); output.Save(buffer, false); SetPdf(buffer.ToArray()); await LoadPdf();
    }
    private async Task<byte[]> RasterPdfPage(uint index, double scale)
    {
        using var page = pdf!.GetPage(index); using var stream = new InMemoryRandomAccessStream();
        uint width = Math.Max(1, (uint)Math.Min(3000, page.Size.Width * scale)); uint height = Math.Max(1, (uint)(width * page.Size.Height / page.Size.Width));
        if ((long)width * height > 16000000) { width = (uint)(width * Math.Sqrt(16000000.0 / ((long)width * height))); height = (uint)(width * page.Size.Height / page.Size.Width); }
        await page.RenderToStreamAsync(stream, new Windows.Data.Pdf.PdfPageRenderOptions { DestinationWidth = width, DestinationHeight = height }); stream.Seek(0); var bytes = new byte[(int)stream.Size]; await stream.AsStreamForRead().ReadExactlyAsync(bytes); return bytes;
    }
    private async Task CompressPdf()
    {
        var target = new NumberBox { Header = "Target size (KB)", Value = Math.Max(10, Math.Round(PdfBytes.Length / 2048.0)), Minimum = 1, Maximum = 100000, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        var panel = new StackPanel { Spacing = 16, MinWidth = 320 }; panel.Children.Add(target); panel.Children.Add(UI.Text("Pages become images. Selectable text, links, forms and accessibility tags are removed. Image quality and dimensions may be reduced.", 13));
        if (await WindowLifetime.ShowDialog(this, new ContentDialog { Title = "Compress PDF", Content = panel, PrimaryButtonText = "Compress", CloseButtonText = "Cancel" }) != ContentDialogResult.Primary || !double.IsFinite(target.Value)) return;
        long limit = (long)(target.Value * 1024); var original = PdfBytes; byte[]? best = null; var token = pdfCancellation!.Token;
        for (int attempt = 0; attempt < 7; attempt++) {
            using var output = new PdfSharp.Pdf.PdfDocument(); double scale = 1.5 * Math.Pow(.75, attempt), quality = Math.Max(.12, .85 - attempt * .11);
            for (uint i = 0; i < pdf!.PageCount; i++) {
                token.ThrowIfCancellationRequested(); status.Text = $"Compressing · pass {attempt + 1}, page {i + 1} of {pdf.PageCount}";
                var png = await RasterPdfPage(i, scale); using var raw = new MemoryStream(png); var decoder = await BitmapDecoder.CreateAsync(raw.AsRandomAccessStream()); var jpeg = await BitmapTools.Encode(png, decoder.PixelWidth, decoder.PixelHeight, quality);
                using var source = new MemoryStream(jpeg); using var image = XImage.FromStream(source); using var sourcePage = pdf.GetPage(i); var page = output.AddPage(); page.Width = XUnit.FromPoint(sourcePage.Size.Width * .75); page.Height = XUnit.FromPoint(sourcePage.Size.Height * .75); using var graphics = XGraphics.FromPdfPage(page); graphics.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
            }
            using var encoded = new MemoryStream(); output.Save(encoded, false); var candidate = encoded.ToArray(); if (best is null || candidate.Length < best.Length) best = candidate; if (best.Length <= limit) break;
        }
        token.ThrowIfCancellationRequested(); if (best is null) return;
        string result = $"Original: {original.Length / 1024.0:N1} KB\nResult: {best.Length / 1024.0:N1} KB\nTarget: {limit / 1024.0:N1} KB" + (best.Length > limit ? "\nThe requested size could not be reached." : "");
        var review = new ContentDialog { Title = "Compression result", Content = result, PrimaryButtonText = "Use result", CloseButtonText = "Keep original" };
        if (await WindowLifetime.ShowDialog(this, review) == ContentDialogResult.Primary) { SetPdf(best); await LoadPdf(); }
    }
    private async Task ReplacePdfWord()
    {
        var find = new TextBox { Header = "Word to replace (first match on this page)" }; var replacement = new TextBox { Header = "Replacement" }; var panel = new StackPanel { Spacing = 12, MinWidth = 360 }; panel.Children.Add(find); panel.Children.Add(replacement); panel.Children.Add(UI.Text("For horizontal text on a white background. This page becomes an image with the replacement text added; its other text and links are flattened. Review the result before exporting.", 13));
        if (await WindowLifetime.ShowDialog(this, new ContentDialog { Title = $"Replace word on page {pageIndex + 1}", Content = panel, PrimaryButtonText = "Replace", CloseButtonText = "Cancel" }) != ContentDialogResult.Primary || string.IsNullOrEmpty(find.Text)) return;
        var source = PdfBytes; using var document = UglyToad.PdfPig.PdfDocument.Open(source); var extracted = document.GetPage(pageIndex + 1); var word = extracted.GetWords().FirstOrDefault(w => w.Text.Equals(find.Text, StringComparison.Ordinal));
        if (word is null) throw new InvalidOperationException("That word was not found on this page.");
        using var importedStream = new MemoryStream(source); using var imported = PdfReader.Open(importedStream, PdfDocumentOpenMode.Import);
        if (imported.Pages[pageIndex].Rotate != 0 || Math.Abs(word.BoundingBox.Rotation) > 1) throw new InvalidOperationException("Rotated text is not supported by this edit operation.");
        var png = await RasterPdfPage((uint)pageIndex, 2); using var pixelStream = new MemoryStream(png); var decoder = await BitmapDecoder.CreateAsync(pixelStream.AsRandomAccessStream()); var pixels = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
        double left = word.BoundingBox.Left, top = extracted.Height - word.BoundingBox.Top, width = word.BoundingBox.Width, height = word.BoundingBox.Height;
        int x0 = Math.Clamp((int)(left / extracted.Width * decoder.PixelWidth) - 2, 0, (int)decoder.PixelWidth), x1 = Math.Clamp((int)Math.Ceiling((left + width) / extracted.Width * decoder.PixelWidth) + 2, 0, (int)decoder.PixelWidth), y0 = Math.Clamp((int)(top / extracted.Height * decoder.PixelHeight) - 2, 0, (int)decoder.PixelHeight), y1 = Math.Clamp((int)Math.Ceiling((top + height) / extracted.Height * decoder.PixelHeight) + 2, 0, (int)decoder.PixelHeight);
        for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++) { int p = (y * (int)decoder.PixelWidth + x) * 4; pixels[p] = pixels[p + 1] = pixels[p + 2] = pixels[p + 3] = 255; }
        var cleaned = await BitmapTools.Pixels(pixels, decoder.PixelWidth, decoder.PixelHeight, png: true); using var output = new PdfSharp.Pdf.PdfDocument();
        for (int i = 0; i < imported.PageCount; i++) {
            if (i != pageIndex) { output.AddPage(imported.Pages[i]); continue; }
            var page = output.AddPage(); page.Width = XUnit.FromPoint(extracted.Width); page.Height = XUnit.FromPoint(extracted.Height); using var graphics = XGraphics.FromPdfPage(page); using var imageStream = new MemoryStream(cleaned); using var image = XImage.FromStream(imageStream); graphics.DrawImage(image, 0, 0, extracted.Width, extracted.Height); graphics.DrawString(replacement.Text, new XFont("Arial", Math.Clamp(height, 6, 120)), XBrushes.Black, new XPoint(left, top + height));
        }
        using var buffer = new MemoryStream(); output.Save(buffer, false); SetPdf(buffer.ToArray()); await LoadPdf();
    }
}
