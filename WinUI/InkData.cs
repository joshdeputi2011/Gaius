using System.Globalization;
using System.Text.Json.Nodes;
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;

namespace Gaius.Office;

/// <summary>Portable ink records, page mapping, eraser geometry, and vector PDF output.</summary>
public static class InkData
{
    public static IEnumerable<JsonObject> Records(JsonObject document, string surface, int page)
        => document["drawings"] is JsonArray drawings
            ? drawings.OfType<JsonObject>().Where(record => record["surface"]?.ToString() == surface && Number(record["page"]) == page)
            : [];

    internal static double Number(JsonNode? value, double fallback = 0)
        => double.TryParse(value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : fallback;

    public static bool Hit(JsonObject stroke, double x, double y, double radius)
    {
        if (stroke["points"] is not JsonArray points) return false;
        double previousX = 0, previousY = 0; bool previous = false;
        double threshold = Math.Max(0, radius) + Number(stroke["width"], 3) / 2;
        foreach (var point in points.OfType<JsonArray>()) {
            if (point.Count < 2) continue;
            double px = Number(point[0]), py = Number(point[1]);
            if (previous) {
                double dx = px - previousX, dy = py - previousY, length = dx * dx + dy * dy;
                double t = length > 0 ? Math.Clamp(((x - previousX) * dx + (y - previousY) * dy) / length, 0, 1) : 0;
                if (Math.Pow(x - previousX - t * dx, 2) + Math.Pow(y - previousY - t * dy, 2) <= threshold * threshold) return true;
            } else if (Math.Pow(x - px, 2) + Math.Pow(y - py, 2) <= threshold * threshold) return true;
            previousX = px; previousY = py; previous = true;
        }
        return false;
    }

    public static void RemapPdfPages(JsonObject document, IReadOnlyList<int> sourcePages, IReadOnlySet<int>? rotatedPages = null)
    {
        if (document["drawings"] is not JsonArray drawings) return;
        var existing = drawings.OfType<JsonObject>().Where(record => record["surface"]?.ToString() == "pdf").ToArray();
        foreach (var record in existing) drawings.Remove(record);
        for (int page = 0; page < sourcePages.Count; page++) {
            int source = sourcePages[page];
            foreach (var original in existing.Where(record => Number(record["page"]) == source)) {
                var record = (JsonObject)original.DeepClone(); record["page"] = page;
                if (rotatedPages?.Contains(source) == true) {
                    double width = Number(record["width"], 794), height = Number(record["height"], 1123);
                    if (record["strokes"] is JsonArray strokes)
                        foreach (var point in strokes.OfType<JsonObject>().SelectMany(stroke => (stroke["points"] as JsonArray ?? []).OfType<JsonArray>()))
                            if (point.Count >= 2) { double x = Number(point[0]), y = Number(point[1]); point[0] = height - y; point[1] = x; }
                    record["width"] = height; record["height"] = width;
                }
                drawings.Add(record);
            }
        }
    }

    public static byte[] ExportPdf(byte[] source, JsonObject document)
    {
        if (document["drawings"] is not JsonArray drawings || !drawings.OfType<JsonObject>().Any(record => record["surface"]?.ToString() == "pdf" && record["strokes"] is JsonArray { Count: > 0 })) return source;
        using var stream = new MemoryStream(source);
        using var output = PdfReader.Open(stream, PdfDocumentOpenMode.Modify);
        for (int page = 0; page < output.PageCount; page++) {
            var records = Records(document, "pdf", page).ToArray(); if (records.Length == 0) continue;
            var target = output.Pages[page];
            using var graphics = XGraphics.FromPdfPage(target, XGraphicsPdfPageOptions.Append);
            // Windows renders the rotated crop box. PDFsharp appends graphics in
            // the unrotated media coordinate system, so map back before drawing.
            var crop = target.EffectiveCropBoxReadOnly;
            if (crop.Width <= 0 || crop.Height <= 0) crop = target.MediaBox;
            graphics.TranslateTransform(crop.X1, target.Height.Point - crop.Y2);
            DrawPdfPage(graphics, records, crop.Width, crop.Height, target.Rotate);
        }
        using var result = new MemoryStream(); output.Save(result, false); return result.ToArray();
    }

    internal static (double X, double Y) PdfPagePoint(double x, double y, double width, double height, int rotation)
        => ((rotation % 360 + 360) % 360) switch {
            90 => (y, height - x),
            180 => (width - x, height - y),
            270 => (width - y, x),
            _ => (x, y)
        };

    public static void DrawPdfPage(XGraphics graphics, IEnumerable<JsonObject> records, double width, double height, int rotation = 0)
    {
        bool sideways = Math.Abs(rotation / 90) % 2 == 1;
        double displayWidth = sideways ? height : width, displayHeight = sideways ? width : height;
        foreach (var record in records) {
            double sx = displayWidth / Math.Max(1, Number(record["width"], displayWidth)), sy = displayHeight / Math.Max(1, Number(record["height"], displayHeight));
            if (record["strokes"] is not JsonArray strokes) continue;
            foreach (var stroke in strokes.OfType<JsonObject>()) {
                if (stroke["points"] is not JsonArray points) continue;
                var line = points.OfType<JsonArray>().Where(point => point.Count >= 2).Select(point => {
                    var mapped = PdfPagePoint(Number(point[0]) * sx, Number(point[1]) * sy, width, height, rotation);
                    return new XPoint(mapped.X, mapped.Y);
                }).ToArray();
                if (line.Length == 0) continue;
                var color = ParseColor(stroke["color"]?.ToString(), stroke["highlighter"]?.ToString() == "true" ? (byte)90 : (byte)255);
                var pen = new XPen(color, Math.Max(.1, Number(stroke["width"], 3) * sx)) { LineCap = XLineCap.Round, LineJoin = XLineJoin.Round };
                if (line.Length == 1) graphics.DrawEllipse(new XSolidBrush(color), line[0].X - pen.Width / 2, line[0].Y - pen.Width / 2, pen.Width, pen.Width);
                else graphics.DrawLines(pen, line);
            }
        }
    }

    private static XColor ParseColor(string? value, byte alpha)
    {
        string hex = value?.TrimStart('#') ?? "185abd";
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)) rgb = 0x185abd;
        return XColor.FromArgb(alpha, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
    }
}
