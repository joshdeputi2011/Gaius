using System.Text.Json.Nodes;
using Gaius.Office;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

internal static class InkTests
{
    public static async Task VerifyPdfRendering(Action<bool, string> check)
    {
        using var source = new PdfSharp.Pdf.PdfDocument();
        foreach (int rotation in new[] { 0, 90, 180, 270 }) {
            var page = source.AddPage(); page.Width = XUnit.FromPoint(300); page.Height = XUnit.FromPoint(400);
            page.CropBox = new PdfSharp.Pdf.PdfRectangle(new XPoint(20, 30), new XPoint(280, 370)); page.Rotate = rotation;
            using var graphics = XGraphics.FromPdfPage(page); graphics.DrawRectangle(XPens.LightGray, 25, 35, 250, 330);
        }
        using var originalBytes = new MemoryStream(); source.Save(originalBytes, false);
        using var nativeSourceStream = new MemoryStream(originalBytes.ToArray()); using var nativeSourceAdapter = nativeSourceStream.AsRandomAccessStream();
        var nativeSource = await Windows.Data.Pdf.PdfDocument.LoadFromStreamAsync(nativeSourceAdapter);
        var document = OfficeFile.Create("pdf"); document.Data["drawings"] = new JsonArray();
        var dimensions = new List<(int Width, int Height)>();
        for (uint index = 0; index < nativeSource.PageCount; index++) {
            using var page = nativeSource.GetPage(index); using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream); stream.Seek(0); var decoder = await BitmapDecoder.CreateAsync(stream);
            int width = (int)decoder.PixelWidth, height = (int)decoder.PixelHeight; dimensions.Add((width, height));
            document.Data["drawings"]!.AsArray().Add(new JsonObject {
                ["surface"] = "pdf", ["page"] = (int)index, ["width"] = width, ["height"] = height,
                ["strokes"] = new JsonArray(new JsonObject { ["color"] = "#ff0000", ["width"] = 4,
                    ["points"] = new JsonArray(new JsonArray(width * .2, height * .2, 1), new JsonArray(width * .3, height * .2, 1)) })
            });
        }
        var annotated = InkData.ExportPdf(originalBytes.ToArray(), document.Data);
        using var outputStream = new MemoryStream(annotated); using var outputAdapter = outputStream.AsRandomAccessStream();
        var nativeOutput = await Windows.Data.Pdf.PdfDocument.LoadFromStreamAsync(outputAdapter);
        for (uint index = 0; index < nativeOutput.PageCount; index++) {
            using var page = nativeOutput.GetPage(index); using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream); stream.Seek(0); var decoder = await BitmapDecoder.CreateAsync(stream);
            var pixels = (await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)).DetachPixelData();
            int minX = (int)decoder.PixelWidth, minY = (int)decoder.PixelHeight, count = 0;
            for (int y = 0; y < decoder.PixelHeight; y++) for (int x = 0; x < decoder.PixelWidth; x++) {
                int offset = (y * (int)decoder.PixelWidth + x) * 4;
                if (pixels[offset + 2] > 180 && pixels[offset + 1] < 100 && pixels[offset] < 100 && pixels[offset + 3] > 100) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); count++; }
            }
            var size = dimensions[(int)index];
            check(count >= 20 && Math.Abs(minX - size.Width * .2) <= 4 && Math.Abs(minY - size.Height * .2) <= 4,
                $"Native PDF ink moved on cropped page rotated {index * 90} degrees (ink pixels {count}, origin {minX},{minY}; expected {size.Width * .2},{size.Height * .2})");
        }
    }

    public static void Run(Action<bool, string> check)
    {
        var stroke = new JsonObject { ["color"] = "#185abd", ["width"] = 3, ["points"] = new JsonArray(new JsonArray(10, 10, .5), new JsonArray(200, 100, .8)) };
        check(InkData.Hit(stroke, 105, 55, 2), "Ink eraser missed the middle of a sparse pen segment");
        check(!InkData.Hit(stroke, 105, 85, 2), "Ink eraser removed a distant stroke");
        check(InkData.PdfPagePoint(390, 10, 300, 400, 90) == (10, 10), "PDF export did not invert clockwise display rotation");
        check(InkData.PdfPagePoint(290, 390, 300, 400, 180) == (10, 10), "PDF export did not invert upside-down display rotation");
        check(InkData.PdfPagePoint(10, 290, 300, 400, 270) == (10, 10), "PDF export did not invert counterclockwise display rotation");
        var document = OfficeFile.Create("pdf");
        document.Data["drawings"] = new JsonArray(
            new JsonObject { ["id"] = "legacy", ["width"] = 960, ["height"] = 540, ["strokes"] = new JsonArray(stroke.DeepClone()) },
            new JsonObject { ["id"] = "first", ["surface"] = "pdf", ["page"] = 0, ["width"] = 300, ["height"] = 400, ["strokes"] = new JsonArray(stroke.DeepClone()) },
            new JsonObject { ["id"] = "second", ["surface"] = "pdf", ["page"] = 1, ["width"] = 300, ["height"] = 400, ["strokes"] = new JsonArray(stroke.DeepClone()) });
        check(InkData.Records(document.Data, "pdf", 0).Count() == 1, "Legacy drawings were treated as new page annotations");
        var copy = document.Copy();
        InkData.RemapPdfPages(copy.Data, [1, 0], new HashSet<int> { 1 });
        var first = InkData.Records(copy.Data, "pdf", 0).Single();
        check(first["id"]?.ToString() == "second" && first["width"]?.ToString() == "400", "PDF reverse/rotation detached page ink");
        var point = first["strokes"]![0]!["points"]![0]!;
        check(point[0]?.ToString() == "390" && point[1]?.ToString() == "10", "PDF clockwise ink transform is incorrect");
        check(InkData.Records(document.Data, "pdf", 0).Single()["strokes"]![0]!["points"]![0]![0]?.ToString() == "10", "Ink page transform mutated the original snapshot");
        InkData.RemapPdfPages(copy.Data, [1]);
        check(InkData.Records(copy.Data, "pdf", 0).Single()["id"]?.ToString() == "first" && !InkData.Records(copy.Data, "pdf", 1).Any(), "Deleting a PDF page left orphan page annotations");
        check(copy.Data["drawings"]!.AsArray().Any(item => item?["id"]?.ToString() == "legacy"), "Page changes removed a legacy inserted drawing");
        var saved = JsonNode.Parse(document.Data.ToJsonString())!.AsObject();
        check(InkData.Records(saved, "pdf", 1).Single()["strokes"]![0]!["points"]!.AsArray().Count == 2, "Saving ink lost editable points");

        var source = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "selection.pdf"));
        var originalBytes = source.ToArray();
        var exported = InkData.ExportPdf(source, document.Data);
        using var originalStream = new MemoryStream(source); using var original = PdfReader.Open(originalStream, PdfDocumentOpenMode.Import);
        using var outputStream = new MemoryStream(exported); using var output = PdfReader.Open(outputStream, PdfDocumentOpenMode.Import);
        check(output.PageCount == original.PageCount && output.Pages[0].Contents.Elements.Count > original.Pages[0].Contents.Elements.Count, "PDF export omitted vector ink content");
        check(source.SequenceEqual(originalBytes), "PDF ink export modified the source bytes");
    }
}
