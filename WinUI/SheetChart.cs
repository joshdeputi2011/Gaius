using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Globalization;
using System.Text.Json.Nodes;
using Windows.Foundation;

namespace Gaius.Office;

internal static class SheetChart
{
    private static readonly string[] Colors = ["#185abd", "#107c41", "#c43e1c", "#744da9", "#d69a12", "#2b8c9c", "#b43c7c", "#688c24", "#317fbd", "#bd6634", "#6f66c8", "#4b786f"];
    public static FrameworkElement Create(JsonObject sheet, JsonObject chart)
    {
        var canvas = new Canvas { Width = 560, Height = 350, Background = UI.DocumentBrush("#ffffff") };
        void Label(string text, double x, double y, double width = 60, double size = 10)
        {
            var block = new TextBlock { Text = text, FontSize = size, Foreground = UI.DocumentBrush("#586b83"), Width = width, MaxHeight = 32, TextTrimming = TextTrimming.CharacterEllipsis };
            Canvas.SetLeft(block, x); Canvas.SetTop(block, y); canvas.Children.Add(block);
        }
        void Add(FrameworkElement element, double x = 0, double y = 0) { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); canvas.Children.Add(element); }
        try {
            string kind = chart["type"]?.ToString() ?? "column";
            var data = SheetChartModel.Read(sheet, chart["range"]?.ToString() ?? "A1:B6"); SheetChartModel.Validate(data, kind);
            const double left = 58, top = 28, width = 470, height = 236;
            Label(chart["title"]?.ToString() ?? "Chart", left, 3, width, 15);
            if (kind is "pie" or "doughnut") {
                var values = data.Series[0].Values.Select((v, i) => (Value: v ?? 0, Index: i)).Where(p => p.Value > 0).ToArray();
                double sum = values.Sum(p => p.Value), angle = -Math.PI / 2, centerX = 160, centerY = 164, radius = 110;
                for (int i = 0; i < values.Length; i++) {
                    var value = values[i]; double end = angle + value.Value / sum * Math.PI * 2; var brush = UI.DocumentBrush(Colors[i % Colors.Length]); FrameworkElement slice;
                    if (values.Length == 1) slice = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = brush };
                    else {
                        var figure = new PathFigure { StartPoint = new Point(centerX, centerY), IsClosed = true };
                        figure.Segments.Add(new LineSegment { Point = new Point(centerX + radius * Math.Cos(angle), centerY + radius * Math.Sin(angle)) });
                        figure.Segments.Add(new ArcSegment { Point = new Point(centerX + radius * Math.Cos(end), centerY + radius * Math.Sin(end)), Size = new Size(radius, radius), IsLargeArc = end - angle > Math.PI, SweepDirection = SweepDirection.Clockwise });
                        var geometry = new PathGeometry(); geometry.Figures.Add(figure); slice = new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Fill = brush, Stroke = UI.DocumentBrush("#ffffff"), StrokeThickness = 1 };
                    }
                    ToolTipService.SetToolTip(slice, $"{data.Labels[value.Index]}: {value.Value:G6} ({value.Value / sum:P1})"); Add(slice, values.Length == 1 ? centerX - radius : 0, values.Length == 1 ? centerY - radius : 0);
                    if (i < 10) { Add(new Rectangle { Width = 10, Height = 10, Fill = brush }, 305, 45 + i * 23); Label($"{data.Labels[value.Index]}  {value.Value / sum:P1}", 322, 42 + i * 23, 220); }
                    angle = end;
                }
                if (kind == "doughnut") Add(new Ellipse { Width = 116, Height = 116, Fill = UI.DocumentBrush("#ffffff") }, centerX - 58, centerY - 58);
                if (values.Length > 10) Label($"+ {values.Length - 10} more categories", 305, 278, 220);
            } else {
                bool horizontal = kind is "bar" or "stackedBar", stacked = kind is "stackedColumn" or "stackedBar", scatter = kind == "scatter";
                var all = data.Series.SelectMany(s => s.Values).Where(v => v.HasValue).Select(v => v!.Value).ToArray(); double min = Math.Min(0, all.Min()), max = Math.Max(0, all.Max());
                if (stacked) { min = Math.Min(0, Enumerable.Range(0, data.Labels.Length).Min(i => data.Series.Sum(s => Math.Min(0, s.Values[i] ?? 0)))); max = Math.Max(0, Enumerable.Range(0, data.Labels.Length).Max(i => data.Series.Sum(s => Math.Max(0, s.Values[i] ?? 0)))); }
                if (min == max) max = min + 1;
                double Y(double value) => top + height - (value - min) / (max - min) * height;
                double X(double value) => left + (value - min) / (max - min) * width;
                for (int tick = 0; tick <= 4; tick++) {
                    double value = min + (max - min) * tick / 4;
                    if (horizontal) { double x = X(value); Add(new Line { X1 = x, X2 = x, Y1 = top, Y2 = top + height, Stroke = UI.DocumentBrush("#e5ebf3"), StrokeThickness = 1 }); Label(value.ToString("G4", CultureInfo.InvariantCulture), x - 15, top + height + 7, 68); }
                    else { double y = Y(value); Add(new Line { X1 = left, X2 = left + width, Y1 = y, Y2 = y, Stroke = UI.DocumentBrush("#e5ebf3"), StrokeThickness = 1 }); Label(value.ToString("G4", CultureInfo.InvariantCulture), 2, y - 6, 52); }
                }
                var positive = new double[data.Labels.Length]; var negative = new double[data.Labels.Length]; double step = (horizontal ? height : width) / Math.Max(1, data.Labels.Length);
                var numericX = data.XValues.Where(x => x.HasValue).Select(x => x!.Value).ToArray(); double xMin = scatter ? numericX.Min() : 0, xMax = scatter ? numericX.Max() : 1; if (xMin == xMax) xMax = xMin + 1;
                for (int seriesIndex = 0; seriesIndex < data.Series.Length; seriesIndex++) {
                    var series = data.Series[seriesIndex]; var brush = UI.DocumentBrush(Colors[seriesIndex % Colors.Length]); var run = new List<Point>();
                    void Flush()
                    {
                        if (run.Count == 0) return;
                        if (kind == "area") { var area = new Polygon { Fill = brush, Opacity = .23 }; area.Points.Add(new Point(run[0].X, Y(0))); foreach (var point in run) area.Points.Add(point); area.Points.Add(new Point(run[^1].X, Y(0))); Add(area); }
                        if (kind is "line" or "area") { var line = new Polyline { Stroke = brush, StrokeThickness = 2.5 }; foreach (var point in run) line.Points.Add(point); Add(line); }
                        run.Clear();
                    }
                    for (int i = 0; i < series.Values.Length; i++) {
                        if (series.Values[i] is not double value || scatter && data.XValues[i] is null) { Flush(); continue; }
                        string tooltip = $"{series.Name} · {data.Labels[i]}: {value:G6}";
                        if (kind is "line" or "area" or "scatter") {
                            double x = scatter ? left + (data.XValues[i]!.Value - xMin) / (xMax - xMin) * width : left + step * (i + .5), y = Y(value); run.Add(new Point(x, y));
                            var point = new Ellipse { Width = 7, Height = 7, Fill = brush }; ToolTipService.SetToolTip(point, tooltip); Add(point, x - 3.5, y - 3.5);
                        } else {
                            double from = stacked ? value >= 0 ? positive[i] : negative[i] : 0;
                            if (stacked) { if (value >= 0) positive[i] += value; else negative[i] += value; }
                            double thickness = Math.Max(1, step * .76 / (stacked ? 1 : data.Series.Length)), offset = step * i + step * .12 + (stacked ? 0 : seriesIndex * thickness);
                            var bar = new Rectangle { Fill = brush, RadiusX = 1.5, RadiusY = 1.5 };
                            if (horizontal) { bar.Width = Math.Max(1, Math.Abs(X(from + value) - X(from))); bar.Height = thickness; Add(bar, Math.Min(X(from), X(from + value)), top + offset); }
                            else { bar.Width = thickness; bar.Height = Math.Max(1, Math.Abs(Y(from + value) - Y(from))); Add(bar, left + offset, Math.Min(Y(from), Y(from + value))); }
                            ToolTipService.SetToolTip(bar, tooltip);
                        }
                    }
                    Flush();
                }
                if (scatter) for (int i = 0; i <= 4; i++) Label((xMin + (xMax - xMin) * i / 4).ToString("G4", CultureInfo.InvariantCulture), left + width * i / 4 - 8, top + height + 7, 72);
                else for (int i = 0; i < data.Labels.Length; i += Math.Max(1, (int)Math.Ceiling(data.Labels.Length / 8.0))) { if (horizontal) Label(data.Labels[i], 2, top + step * (i + .5) - 6, 52); else Label(data.Labels[i], left + step * i, top + height + 7, Math.Max(45, step)); }
                for (int i = 0; i < data.Series.Length; i++) { double x = left + i % 4 * 122, y = 307 + i / 4 * 14; Add(new Rectangle { Width = 9, Height = 9, Fill = UI.DocumentBrush(Colors[i % Colors.Length]) }, x, y); Label(data.Series[i].Name, x + 14, y - 3, 102); }
            }
        } catch (FormatException error) { Label(error.Message, 35, 120, 490, 15); }
        return new Viewbox { Child = canvas, Stretch = Stretch.Uniform, MaxWidth = 720, MinWidth = 360 };
    }
}
