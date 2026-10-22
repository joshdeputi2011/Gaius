using Gaius.Office;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json.Nodes;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

internal static class RasterFormatTests
{
    public static async Task Run(Action<bool, string> check)
    {
        byte[] png = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "gradient.png"));
        using var input = new MemoryStream(png); var decoder = await BitmapDecoder.CreateAsync(input.AsRandomAccessStream());
        var pixels = (await decoder.GetPixelDataAsync()).DetachPixelData();
        using var output = new InMemoryRandomAccessStream(); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.BmpEncoderId, output);
        encoder.SetPixelData(decoder.BitmapPixelFormat, decoder.BitmapAlphaMode, decoder.PixelWidth, decoder.PixelHeight, 96, 96, pixels); await encoder.FlushAsync(); output.Seek(0);
        var bmp = new byte[(int)output.Size]; await output.AsStreamForRead().ReadExactlyAsync(bmp);
        check(RichTextInterop.ImageDimensions(bmp) == ((int)decoder.PixelWidth, (int)decoder.PixelHeight), "BMP dimensions failed native document sizing");
        var jpeg = await BitmapTools.Encode(png, decoder.PixelWidth, decoder.PixelHeight);
        foreach (var (data, mime) in new[] { (png, "image/png"), (jpeg, "image/jpeg"), (bmp, "image/bmp") }) {
            check(await OfficeQuickTools.ImageMime(data) == mime, "Native image signature decoding failed: " + mime);
            var normalized = await BitmapTools.Encode(data, decoder.PixelWidth, decoder.PixelHeight, png: true);
            check(RichTextInterop.ImageDimensions(normalized) == ((int)decoder.PixelWidth, (int)decoder.PixelHeight), "Raster conversion changed image proportions: " + mime);
        }
        var file = OfficeFile.Create("document"); file.Content["delta"] = new JsonArray(new JsonObject { ["insert"] = new JsonObject { ["image"] = "data:image/bmp;base64," + Convert.ToBase64String(bmp) } });
        var reopened = OfficeInterchange.Import(OfficeInterchange.Export(file), ".docx");
        check(reopened.Content["html"]!.ToString().Contains("data:image/png;base64,"), "BMP media in DOCX was not normalized for native rich text");
    }
}
