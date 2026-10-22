using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Gaius.Office;

internal static class OfficeShapes
{
    public static readonly Dictionary<string, string> Paths = new() {
        ["rectangle"] = "M2 2 H98 V98 H2 Z", ["roundedRectangle"] = "M12 2 H88 Q98 2 98 12 V88 Q98 98 88 98 H12 Q2 98 2 88 V12 Q2 2 12 2 Z",
        ["ellipse"] = "M50 2 A48 48 0 1 0 50 98 A48 48 0 1 0 50 2 Z", ["triangle"] = "M50 2 L98 98 H2 Z", ["rightTriangle"] = "M2 2 L98 98 H2 Z",
        ["diamond"] = "M50 2 L98 50 L50 98 L2 50 Z", ["pentagon"] = "M50 2 L98 38 L80 98 H20 L2 38 Z", ["hexagon"] = "M25 2 H75 L98 50 L75 98 H25 L2 50 Z",
        ["star"] = "M50 2 L62 35 L98 37 L70 59 L80 96 L50 75 L20 96 L30 59 L2 37 L38 35 Z",
        ["heart"] = "M50 96 C42 84 2 58 2 30 C2 2 36 -8 50 20 C64 -8 98 2 98 30 C98 58 58 84 50 96 Z",
        ["arrow"] = "M2 30 H62 V2 L98 50 L62 98 V70 H2 Z", ["leftArrow"] = "M98 30 H38 V2 L2 50 L38 98 V70 H98 Z",
        ["doubleArrow"] = "M2 50 L28 2 V30 H72 V2 L98 50 L72 98 V70 H28 V98 Z", ["chevron"] = "M2 2 H64 L98 50 L64 98 H2 L36 50 Z",
        ["callout"] = "M2 2 H98 V76 H42 L18 98 V76 H2 Z", ["line"] = "M2 50 H98"
    };
    public static readonly Dictionary<string, string> Presets = new() { ["rectangle"] = "rect", ["roundedRectangle"] = "roundRect", ["rightTriangle"] = "rtTriangle", ["star"] = "star5", ["arrow"] = "rightArrow", ["doubleArrow"] = "leftRightArrow", ["callout"] = "wedgeRectCallout" };
    public static string Label(string kind) => System.Text.RegularExpressions.Regex.Replace(char.ToUpper(kind[0]) + kind[1..], "([a-z])([A-Z])", "$1 $2");
    public static FrameworkElement Visual(string kind, string fill, string stroke = "#185abd", double strokeWidth = 0)
    {
        var path = (Microsoft.UI.Xaml.Shapes.Path)XamlReader.Load($"<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Data='{Paths.GetValueOrDefault(kind, Paths["rectangle"])}' />");
        path.Fill = kind == "line" ? null : UI.DocumentBrush(fill); path.Stroke = UI.DocumentBrush(stroke); path.StrokeThickness = kind == "line" ? Math.Max(1, strokeWidth) : strokeWidth;
        var grid = new Grid { Width = 100, Height = 100 }; grid.Children.Add(path); return new Viewbox { Stretch = Stretch.Fill, Child = grid };
    }
    private static readonly Dictionary<string, double[]> Polygons = Paths.Keys.ToDictionary(kind => kind, PolygonPoints);
    private static double[] PolygonPoints(string kind) => kind switch {
            "triangle" => [.5,.02,.98,.98,.02,.98], "rightTriangle" => [.02,.02,.98,.98,.02,.98], "diamond" => [.5,.02,.98,.5,.5,.98,.02,.5],
            "pentagon" => [.5,.02,.98,.38,.8,.98,.2,.98,.02,.38], "hexagon" => [.25,.02,.75,.02,.98,.5,.75,.98,.25,.98,.02,.5],
            "star" => [.5,.02,.62,.35,.98,.37,.7,.59,.8,.96,.5,.75,.2,.96,.3,.59,.02,.37,.38,.35],
            "arrow" => [.02,.3,.62,.3,.62,.02,.98,.5,.62,.98,.62,.7,.02,.7], "leftArrow" => [.98,.3,.38,.3,.38,.02,.02,.5,.38,.98,.38,.7,.98,.7],
            "doubleArrow" => [.02,.5,.28,.02,.28,.3,.72,.3,.72,.02,.98,.5,.72,.98,.72,.7,.28,.7,.28,.98],
            "chevron" => [.02,.02,.64,.02,.98,.5,.64,.98,.02,.98,.36,.5], "callout" => [.02,.02,.98,.02,.98,.76,.42,.76,.18,.98,.18,.76,.02,.76],
            _ => [.02,.02,.98,.02,.98,.98,.02,.98]
        };
    public static bool Contains(string kind, double x, double y)
    {
        if (kind == "ellipse") return Math.Pow((x - .5) / .48, 2) + Math.Pow((y - .5) / .48, 2) <= 1;
        if (kind == "line") return Math.Abs(y - .5) < .012 && x >= .02 && x <= .98;
        if (kind == "roundedRectangle") { double dx = Math.Max(.12 - x, Math.Max(x - .88, 0)), dy = Math.Max(.12 - y, Math.Max(y - .88, 0)); return dx * dx + dy * dy <= .01; }
        if (kind == "heart") { double a = (x - .5) * 2.2, b = (.58 - y) * 2.2; return Math.Pow(a * a + b * b - .7, 3) - a * a * b * b * b <= 0; }
        var coordinates = Polygons.GetValueOrDefault(kind, Polygons["rectangle"]);
        bool inside = false; int count = coordinates.Length / 2;
        for (int i = 0, j = count - 1; i < count; j = i++) { double xi = coordinates[i * 2], yi = coordinates[i * 2 + 1], xj = coordinates[j * 2], yj = coordinates[j * 2 + 1]; if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside; }
        return inside;
    }
}
