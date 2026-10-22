using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Gaius.Office;

internal static class OfficeQuickTools
{
    public static readonly string[] OpenExtensions = [".gaius", ".docx", ".odt", ".doc", ".xlsx", ".pptx", ".txt", ".rtf", ".csv", ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".img", ".pdf"];
    private static readonly string[] Images = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".img"];
    private static async Task<byte[]> Read(StorageFile picked)
    {
        if ((await picked.GetBasicPropertiesAsync()).Size > 40 * 1024 * 1024) throw new IOException("Choose a file smaller than 40 MB.");
        return (await FileIO.ReadBufferAsync(picked)).ToArray();
    }
    public static async Task<string> ImageMime(byte[] bytes)
    {
        using var input = new MemoryStream(bytes); var decoder = await BitmapDecoder.CreateAsync(input.AsRandomAccessStream());
        if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0 || (long)decoder.PixelWidth * decoder.PixelHeight > 40000000) throw new FormatException("Choose an image smaller than 40 megapixels.");
        return decoder.DecoderInformation.MimeTypes.FirstOrDefault(mime => mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) ?? "image/png";
    }
    public static async Task ToPdf(Window owner, string kind)
    {
        string[] extensions = kind switch { "document" => [".docx", ".odt", ".doc", ".txt", ".rtf"], "spreadsheet" => [".xlsx", ".csv"], "presentation" => [".pptx"], _ => Images };
        var picked = await UI.PickOpen(owner, extensions); if (picked is null || WindowLifetime.IsClosed(owner)) return; var bytes = await Read(picked); if (WindowLifetime.IsClosed(owner)) return;
        OfficeFile file;
        if (kind == "image") {
            await ImageMime(bytes); using var input = new MemoryStream(bytes); var decoder = await BitmapDecoder.CreateAsync(input.AsRandomAccessStream());
            var png = await BitmapTools.Encode(bytes, decoder.PixelWidth, decoder.PixelHeight, png: true); file = OfficeFile.Create("image"); file.Image["dataUrl"] = "data:image/png;base64," + Convert.ToBase64String(png);
        } else if (picked.FileType is ".txt" or ".rtf") {
            file = OfficeFile.Create("document"); string text = await FileIO.ReadTextAsync(picked);
            if (picked.FileType == ".rtf") { file.Content["rtf"] = text; file.Content["html"] = RtfPipe.Rtf.ToHtml(text); }
            else file.Content["html"] = "<p>" + System.Net.WebUtility.HtmlEncode(text).Replace("\n", "<br>") + "</p>";
            var parsed = new HtmlAgilityPack.HtmlDocument(); parsed.LoadHtml(file.Content["html"]!.ToString()); file.Content["text"] = HtmlAgilityPack.HtmlEntity.DeEntitize(parsed.DocumentNode.InnerText);
        } else if (picked.FileType == ".csv") {
            file = OfficeFile.Create("spreadsheet"); var rows = Csv.Parse(await FileIO.ReadTextAsync(picked)); if (rows.Count > 10000 || rows.Any(row => row.Count > 100)) throw new IOException("CSV limit: 10,000 rows and 100 columns.");
            for (int r = 0; r < rows.Count; r++) for (int c = 0; c < rows[r].Count; c++) file.Sheet["cells"]![OfficeFile.Column(c) + (r + 1)] = rows[r][c];
        } else file = await Task.Run(() => OfficeInterchange.Import(bytes, picked.FileType));
        if (WindowLifetime.IsClosed(owner)) return; file.Title = picked.DisplayName; App.Show(new PrintPreviewWindow(file.Copy()));
    }
    public static async Task Compress(Window owner, string kind)
    {
        if (kind == "pdf") {
            var picked = await UI.PickOpen(owner, ".pdf"); if (picked is null || WindowLifetime.IsClosed(owner)) return; var bytes = await Read(picked); if (WindowLifetime.IsClosed(owner)) return; var file = OfficeFile.Create("pdf"); file.Title = picked.DisplayName; file.Data["pdf"]!["dataUrl"] = "data:application/pdf;base64," + Convert.ToBase64String(bytes);
            var editor = new EditorWindow(file); App.Show(editor); await editor.RunStartAction("compress"); return;
        }
        var source = await UI.PickOpen(owner, Images); if (source is null || WindowLifetime.IsClosed(owner)) return; var original = await Read(source); await ImageMime(original); if (WindowLifetime.IsClosed(owner)) return;
        var target = new NumberBox { Header = "Target size (KB)", Value = Math.Max(10, Math.Round(original.Length / 2048.0)), Minimum = 1, Maximum = 100000, Width = 320 };
        var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(target); panel.Children.Add(UI.Text("Save a compressed JPEG copy. Transparent areas become white.", 13));
        if (await WindowLifetime.ShowDialog(owner, new ContentDialog { Title = "Quick compress image", Content = panel, PrimaryButtonText = "Compress", CloseButtonText = "Cancel" }) != ContentDialogResult.Primary || !double.IsFinite(target.Value)) return;
        var result = await BitmapTools.Target(original, (long)(target.Value * 1024));
        string detail = $"Original: {original.Length / 1024.0:N1} KB\nResult: {result.Bytes.Length / 1024.0:N1} KB · {result.Width} × {result.Height}" + (result.Bytes.Length > target.Value * 1024 ? "\nThe requested size could not be reached." : "");
        if (await WindowLifetime.ShowDialog(owner, new ContentDialog { Title = "Compression result", Content = detail, PrimaryButtonText = "Save JPEG", CloseButtonText = "Cancel" }) != ContentDialogResult.Primary) return;
        if (WindowLifetime.IsClosed(owner)) return; var save = await UI.PickSave(owner, source.DisplayName + "-compressed", ".jpg"); if (save is not null) await FileIO.WriteBytesAsync(save, result.Bytes);
    }
    public static async Task ConvertImage(Window owner)
    {
        var source = await UI.PickOpen(owner, Images); if (source is null || WindowLifetime.IsClosed(owner)) return; var bytes = await Read(source); await ImageMime(bytes); if (WindowLifetime.IsClosed(owner)) return;
        var format = new ComboBox { Header = "Output format", ItemsSource = new[] { "PNG", "JPEG", "BMP" }, SelectedIndex = 0, Width = 320 };
        if (await WindowLifetime.ShowDialog(owner, new ContentDialog { Title = "Convert image", Content = format, PrimaryButtonText = "Save copy", CloseButtonText = "Cancel" }) != ContentDialogResult.Primary) return;
        using var input = new MemoryStream(bytes); var decoder = await BitmapDecoder.CreateAsync(input.AsRandomAccessStream());
        byte[] output; string extension = format.SelectedIndex switch { 1 => ".jpg", 2 => ".bmp", _ => ".png" };
        if (format.SelectedIndex == 2) { var pixels = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData(); using var stream = new InMemoryRandomAccessStream(); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.BmpEncoderId, stream); encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, decoder.PixelWidth, decoder.PixelHeight, 96, 96, pixels); await encoder.FlushAsync(); stream.Seek(0); output = new byte[(int)stream.Size]; await stream.AsStreamForRead().ReadExactlyAsync(output); }
        else output = await BitmapTools.Encode(bytes, decoder.PixelWidth, decoder.PixelHeight, png: format.SelectedIndex == 0);
        if (WindowLifetime.IsClosed(owner)) return; var save = await UI.PickSave(owner, source.DisplayName, extension); if (save is not null) await FileIO.WriteBytesAsync(save, output);
    }
}
