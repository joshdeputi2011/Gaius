using System.Globalization;
using System.Text.Json.Nodes;

namespace Gaius.Office;

internal static class SheetDimensions
{
    public const double DefaultColumnWidth = 112, DefaultRowHeight = 32;
    public static double ColumnWidth(JsonObject sheet, int column) => Read(sheet["columnWidths"]?[OfficeFile.Column(column)], DefaultColumnWidth, 32, 1200);
    public static double RowHeight(JsonObject sheet, int row) => Read(sheet["rowHeights"]?[row.ToString(CultureInfo.InvariantCulture)], DefaultRowHeight, 20, 600);
    public static void SetColumnWidth(JsonObject sheet, int column, double value)
    {
        sheet["columnWidths"] ??= new JsonObject(); sheet["columnWidths"]![OfficeFile.Column(column)] = Math.Clamp(double.IsFinite(value) ? value : DefaultColumnWidth, 32, 1200);
    }
    public static void SetRowHeight(JsonObject sheet, int row, double value)
    {
        sheet["rowHeights"] ??= new JsonObject(); sheet["rowHeights"]![row.ToString(CultureInfo.InvariantCulture)] = Math.Clamp(double.IsFinite(value) ? value : DefaultRowHeight, 20, 600);
    }
    // Excel's column unit is the maximum digit width of its default font (7 px for Calibri 11).
    public static double FromExcelWidth(double width) => Math.Max(32, Math.Floor(width * 7 + 5));
    public static double ToExcelWidth(double pixels) => Math.Max(0, (pixels - 5) / 7);
    private static double Read(JsonNode? node, double fallback, double min, double max) => double.TryParse(node?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}
