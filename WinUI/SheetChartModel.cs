using System.Globalization;
using System.Text.Json.Nodes;

namespace Gaius.Office;

internal sealed record SheetChartSeries(string Name, double?[] Values);
internal sealed record SheetChartData(string[] Labels, double?[] XValues, SheetChartSeries[] Series)
{
    public bool HasValues => Series.Any(s => s.Values.Any(v => v.HasValue));
}

internal static class SheetChartModel
{
    public static readonly (string Key, string Label)[] Types = [("column", "Column"), ("bar", "Horizontal bar"), ("line", "Line"), ("area", "Area"), ("scatter", "Scatter"), ("pie", "Pie"), ("doughnut", "Doughnut"), ("stackedColumn", "Stacked column"), ("stackedBar", "Stacked bar")];
    public static bool IsSupported(string? kind) => Types.Any(t => t.Key == kind);
    public static SheetChartData Read(JsonObject sheet, string source, int limit = 200)
    {
        var positions = SheetData.Range(source, Math.Max(10000, sheet["rows"]?.GetValue<int>() ?? 100), Math.Max(256, sheet["columns"]?.GetValue<int>() ?? 26)).Select(SheetData.Position).ToArray();
        int left = positions.Min(p => p.Column), right = positions.Max(p => p.Column), first = positions.Min(p => p.Row), last = positions.Max(p => p.Row);
        if (left == right) throw new FormatException("Select labels or X values in the first column and at least one numeric series in the following columns.");
        if (right - left > 12) throw new FormatException("Select at most 12 chart series.");
        double? Numeric(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number) ? number : null;
        string Cell(int column, int row) => SheetData.Value(sheet, OfficeFile.Column(column) + row);
        bool header = Enumerable.Range(left + 1, right - left).All(c => Numeric(Cell(c, first)) is null);
        int start = first + (header ? 1 : 0), count = Math.Max(0, last - start + 1);
        if (count > limit) throw new FormatException($"Select at most {limit} data rows per chart.");
        var labels = Enumerable.Range(start, count).Select(r => Cell(left, r)).ToArray();
        var x = labels.Select(Numeric).ToArray();
        var series = Enumerable.Range(left + 1, right - left).Select(c => new SheetChartSeries(header && !string.IsNullOrEmpty(Cell(c, first)) ? Cell(c, first) : "Series " + (c - left), Enumerable.Range(start, count).Select(r => Numeric(Cell(c, r))).ToArray())).ToArray();
        return new(labels, x, series);
    }
    public static void Validate(SheetChartData data, string kind)
    {
        if (!IsSupported(kind)) throw new FormatException("Choose a supported chart type.");
        if (!data.HasValues) throw new FormatException("The selected range has no numeric values.");
        if (kind == "scatter" && !data.XValues.Where((x, i) => x.HasValue && data.Series.Any(s => s.Values[i].HasValue)).Any()) throw new FormatException("Scatter charts need numeric X values in the first column and numeric Y values in the following columns.");
        if (kind is "pie" or "doughnut" && !data.Series[0].Values.Any(v => v > 0)) throw new FormatException("Pie and doughnut charts need positive values in their first series.");
    }
}
