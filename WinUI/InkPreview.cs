using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Text.Json.Nodes;
using Windows.Foundation;

namespace Gaius.Office;

/// <summary>Read-only native ink for print pages. Offset is measured in the preview page's logical pixels.</summary>
public static class InkPreview
{
    public static Canvas Create(JsonObject document, string surface, int sourcePage, double width, double height, double verticalOffset = 0)
    {
        var canvas = new Canvas { Width = width, Height = height, IsHitTestVisible = false, Clip = new RectangleGeometry { Rect = new Rect(0, 0, width, height) } };
        foreach (var record in InkData.Records(document, surface, sourcePage)) {
            double scale = width / Math.Max(1, InkData.Number(record["width"], width));
            if (record["strokes"] is not JsonArray strokes) continue;
            foreach (var stroke in strokes.OfType<JsonObject>()) {
                Windows.UI.Color color;
                try { color = UI.Color(stroke["color"]?.ToString() ?? "#185abd"); } catch { color = UI.Color("#185abd"); }
                color.A = stroke["highlighter"]?.ToString() == "true" ? (byte)90 : (byte)255;
                var line = new Polyline { Stroke = new SolidColorBrush(color), StrokeThickness = Math.Max(.1, InkData.Number(stroke["width"], 3) * scale), StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
                if (stroke["points"] is JsonArray points)
                    foreach (var point in points.OfType<JsonArray>().Where(point => point.Count >= 2)) line.Points.Add(new Point(InkData.Number(point[0]) * scale, InkData.Number(point[1]) * scale - verticalOffset));
                if (line.Points.Count == 1) line.Points.Add(new Point(line.Points[0].X + .1, line.Points[0].Y));
                canvas.Children.Add(line);
            }
        }
        return canvas;
    }

    public static int DocumentPageCount(JsonObject document, double pageWidth, double pageHeight)
    {
        double extent = 0;
        foreach (var record in InkData.Records(document, "document", 0)) {
            double scale = pageWidth / Math.Max(1, InkData.Number(record["width"], pageWidth));
            if (record["strokes"] is JsonArray strokes)
                foreach (var stroke in strokes.OfType<JsonObject>())
                    if (stroke["points"] is JsonArray points)
                        foreach (var point in points.OfType<JsonArray>().Where(point => point.Count >= 2)) extent = Math.Max(extent, (InkData.Number(point[1]) + InkData.Number(stroke["width"], 3) / 2) * scale);
        }
        return Math.Clamp((int)Math.Ceiling(extent / pageHeight), 1, 1000);
    }
}
