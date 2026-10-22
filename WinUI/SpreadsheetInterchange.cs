using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Gaius.Office;

internal static partial class OfficeInterchange
{
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart", Xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static (JsonObject Widths, JsonObject Heights) ImportSheetDimensions(XDocument xml)
    {
        var widths = new JsonObject(); var heights = new JsonObject();
        double defaultWidth = Number(xml.Root?.Element(S + "sheetFormatPr")?.Attribute("defaultColWidth")?.Value, -1), defaultHeight = Number(xml.Root?.Element(S + "sheetFormatPr")?.Attribute("defaultRowHeight")?.Value, 24);
        if (defaultWidth > 0) for (int c = 0; c < 256; c++) widths[OfficeFile.Column(c)] = SheetDimensions.FromExcelWidth(defaultWidth);
        if (Math.Abs(defaultHeight - 24) > .01) for (int r = 1; r <= 100; r++) heights[r.ToString(CultureInfo.InvariantCulture)] = defaultHeight / .75;
        foreach (var column in xml.Descendants(S + "col")) {
            int min = Math.Max(1, (int)Number(column.Attribute("min")?.Value, 1)), max = Math.Min(256, (int)Number(column.Attribute("max")?.Value, min)); double width = Number(column.Attribute("width")?.Value, -1);
            if (width >= 0) for (int c = min; c <= max; c++) widths[OfficeFile.Column(c - 1)] = SheetDimensions.FromExcelWidth(width);
        }
        foreach (var row in xml.Descendants(S + "row")) if (int.TryParse(row.Attribute("r")?.Value, out int index) && index > 0 && index <= 10000 && Number(row.Attribute("ht")?.Value, -1) is double height && height >= 0) heights[index.ToString(CultureInfo.InvariantCulture)] = height / .75;
        return (widths, heights);
    }
    private static XElement? ExportSheetCharts(Dictionary<string, byte[]> parts, JsonObject sheet, int sheetIndex, List<(string Part, string Type)> types)
    {
        if (sheet["charts"] is not JsonArray charts || charts.Count == 0) return null;
        string token = Guid.NewGuid().ToString("N")[..12], drawingPath = $"xl/drawings/gaius-{token}.xml";
        var drawing = E(Xdr, "wsDr", new XAttribute(XNamespace.Xmlns + "a", A), new XAttribute(XNamespace.Xmlns + "r", R)); var rels = new List<(string, string, string)>();
        for (int i = 0; i < charts.Count; i++) {
            if (charts[i] is not JsonObject chart) continue;
            string kind = chart["type"]?.ToString() ?? "column"; if (!SheetChartModel.IsSupported(kind)) continue;
            var data = SheetChartModel.Read(sheet, chart["range"]?.ToString() ?? "A1:B6"); SheetChartModel.Validate(data, kind);
            string chartPath = $"xl/charts/gaius-{token}-{i + 1}.xml", relId = "chart" + (i + 1); Put(parts, chartPath, ExportChartXml(sheet, chart, data)); rels.Add((relId, "chart", "../charts/" + Path.GetFileName(chartPath))); types.Add((chartPath, "drawingml.chart"));
            XElement Marker(string name, int column, int row) => E(Xdr, name, E(Xdr, "col", column), E(Xdr, "colOff", 0), E(Xdr, "row", row), E(Xdr, "rowOff", 0));
            drawing.Add(E(Xdr, "twoCellAnchor", Marker("from", 1, 3 + i * 18), Marker("to", 9, 20 + i * 18), E(Xdr, "graphicFrame", new XAttribute("macro", ""), E(Xdr, "nvGraphicFramePr", E(Xdr, "cNvPr", new XAttribute("id", i + 2), new XAttribute("name", chart["title"]?.ToString() ?? "Chart")), E(Xdr, "cNvGraphicFramePr")), E(Xdr, "xfrm", E(A, "off", new XAttribute("x", 0), new XAttribute("y", 0)), E(A, "ext", new XAttribute("cx", 0), new XAttribute("cy", 0))), E(A, "graphic", E(A, "graphicData", new XAttribute("uri", C), E(C, "chart", new XAttribute(R + "id", relId))))), E(Xdr, "clientData")));
        }
        if (rels.Count == 0) return null;
        Put(parts, drawingPath, drawing); Put(parts, $"xl/drawings/_rels/{Path.GetFileName(drawingPath)}.rels", Relationships(rels)); types.Add((drawingPath, "drawing"));
        Put(parts, $"xl/worksheets/_rels/sheet{sheetIndex}.xml.rels", Relationships(new[] { ("gaiusDrawing", "drawing", "../drawings/" + Path.GetFileName(drawingPath)) })); return E(S, "drawing", new XAttribute(R + "id", "gaiusDrawing"));
    }
    private static XElement ExportChartXml(JsonObject sheet, JsonObject chart, SheetChartData data)
    {
        string kind = chart["type"]?.ToString() ?? "column"; bool scatter = kind == "scatter", pie = kind is "pie" or "doughnut", horizontal = kind is "bar" or "stackedBar", stacked = kind is "stackedColumn" or "stackedBar";
        var addresses = SheetData.Range(chart["range"]!.ToString()).Select(SheetData.Position).ToArray(); int first = addresses.Min(p => p.Row), last = addresses.Max(p => p.Row), left = addresses.Min(p => p.Column), start = last - data.Labels.Length + 1;
        string sheetName = Regex.Replace(sheet["name"]?.ToString() ?? "Sheet 1", @"[\[\]:*?/\\]", ""); if (sheetName.Length > 27) sheetName = sheetName[..27]; if (sheetName.Length == 0) sheetName = "Sheet 1";
        string Reference(int column) => $"'{sheetName.Replace("'", "''")}'!${OfficeFile.Column(column)}${start}:${OfficeFile.Column(column)}${last}";
        XElement Numbers(string outer, int column, double?[] values) => E(C, outer, E(C, "numRef", E(C, "f", Reference(column)), E(C, "numCache", E(C, "formatCode", "General"), E(C, "ptCount", new XAttribute("val", values.Length)), values.Select((value, index) => value.HasValue ? E(C, "pt", new XAttribute("idx", index), E(C, "v", value.Value.ToString("R", CultureInfo.InvariantCulture))) : null))));
        XElement Categories() => E(C, "cat", E(C, "strRef", E(C, "f", Reference(left)), E(C, "strCache", E(C, "ptCount", new XAttribute("val", data.Labels.Length)), data.Labels.Select((label, index) => E(C, "pt", new XAttribute("idx", index), E(C, "v", label))))));
        string element = pie ? kind + "Chart" : scatter ? "scatterChart" : kind is "line" or "area" ? kind + "Chart" : "barChart";
        var plot = E(C, element);
        if (element == "barChart") plot.Add(E(C, "barDir", new XAttribute("val", horizontal ? "bar" : "col")), E(C, "grouping", new XAttribute("val", stacked ? "stacked" : "clustered")));
        else if (scatter) plot.Add(E(C, "scatterStyle", new XAttribute("val", "marker")));
        else if (!pie) plot.Add(E(C, "grouping", new XAttribute("val", "standard")));
        plot.Add(E(C, "varyColors", new XAttribute("val", pie ? 1 : 0)));
        for (int i = 0; i < (pie ? 1 : data.Series.Length); i++) {
            var series = data.Series[i]; var title = start > first ? E(C, "strRef", E(C, "f", $"'{sheetName.Replace("'", "''")}'!${OfficeFile.Column(left + i + 1)}${first}"), E(C, "strCache", E(C, "ptCount", new XAttribute("val", 1)), E(C, "pt", new XAttribute("idx", 0), E(C, "v", series.Name)))) : E(C, "v", series.Name);
            var entry = E(C, "ser", E(C, "idx", new XAttribute("val", i)), E(C, "order", new XAttribute("val", i)), E(C, "tx", title));
            if (scatter || kind == "line") entry.Add(E(C, "marker", E(C, "symbol", new XAttribute("val", "circle")), E(C, "size", new XAttribute("val", 5))));
            if (scatter) entry.Add(Numbers("xVal", left, data.XValues), Numbers("yVal", left + i + 1, series.Values)); else entry.Add(Categories(), Numbers("val", left + i + 1, series.Values));
            if (scatter || kind == "line") entry.Add(E(C, "smooth", new XAttribute("val", 0))); plot.Add(entry);
        }
        if (element == "barChart") { plot.Add(E(C, "gapWidth", new XAttribute("val", 150))); if (stacked) plot.Add(E(C, "overlap", new XAttribute("val", 100))); }
        if (kind == "doughnut") plot.Add(E(C, "firstSliceAng", new XAttribute("val", 270)), E(C, "holeSize", new XAttribute("val", 53)));
        if (!pie) plot.Add(E(C, "axId", new XAttribute("val", 100)), E(C, "axId", new XAttribute("val", 200)));
        var plotArea = E(C, "plotArea", E(C, "layout"), plot);
        XElement Axis(string name, int id, string position, int cross) => E(C, name, E(C, "axId", new XAttribute("val", id)), E(C, "scaling", E(C, "orientation", new XAttribute("val", "minMax"))), E(C, "delete", new XAttribute("val", 0)), E(C, "axPos", new XAttribute("val", position)), name == "valAx" ? E(C, "majorGridlines") : null, E(C, "numFmt", new XAttribute("formatCode", "General"), new XAttribute("sourceLinked", 1)), E(C, "tickLblPos", new XAttribute("val", "nextTo")), E(C, "crossAx", new XAttribute("val", cross)), E(C, "crosses", new XAttribute("val", "autoZero")), name == "catAx" ? new[] { E(C, "auto", new XAttribute("val", 1)), E(C, "lblAlgn", new XAttribute("val", "ctr")), E(C, "lblOffset", new XAttribute("val", 100)) } : new[] { E(C, "crossBetween", new XAttribute("val", "between")) });
        if (!pie) plotArea.Add(Axis(scatter ? "valAx" : "catAx", 100, horizontal ? "l" : "b", 200), Axis("valAx", 200, horizontal ? "b" : "l", 100));
        return E(C, "chartSpace", new XAttribute(XNamespace.Xmlns + "a", A), new XAttribute(XNamespace.Xmlns + "r", R), E(C, "lang", new XAttribute("val", "en-US")), E(C, "chart", E(C, "title", E(C, "tx", E(C, "rich", E(A, "bodyPr"), E(A, "lstStyle"), E(A, "p", E(A, "r", E(A, "rPr", new XAttribute("lang", "en-US")), E(A, "t", chart["title"]?.ToString() ?? "Chart")))))), E(C, "autoTitleDeleted", new XAttribute("val", 0)), plotArea, E(C, "legend", E(C, "legendPos", new XAttribute("val", "b")), E(C, "layout")), E(C, "plotVisOnly", new XAttribute("val", 1)), E(C, "dispBlanksAs", new XAttribute("val", "gap"))));
    }
    private static JsonArray ImportSheetCharts(Dictionary<string, byte[]> parts, string sheetPath, XDocument sheet)
    {
        var charts = new JsonArray(); var targets = Targets(parts, sheetPath);
        foreach (var drawing in sheet.Descendants(S + "drawing")) {
            string id = drawing.Attribute(R + "id")?.Value ?? ""; if (!targets.TryGetValue(id, out var drawingPath) || !parts.ContainsKey(drawingPath)) continue; var chartTargets = Targets(parts, drawingPath);
            foreach (var reference in Read(parts, drawingPath).Descendants(C + "chart")) {
                string referenceId = reference.Attribute(R + "id")?.Value ?? ""; if (!chartTargets.TryGetValue(referenceId, out var chartPath) || !parts.ContainsKey(chartPath)) continue;
                var xml = Read(parts, chartPath); var plot = xml.Descendants(C + "plotArea").FirstOrDefault()?.Elements().FirstOrDefault(e => e.Name.Namespace == C && e.Name.LocalName.EndsWith("Chart", StringComparison.Ordinal)); if (plot is null) continue;
                string kind = plot.Name.LocalName switch { "barChart" => plot.Element(C + "barDir")?.Attribute("val")?.Value == "bar" ? "bar" : "column", "lineChart" => "line", "areaChart" => "area", "scatterChart" => "scatter", "pieChart" => "pie", "doughnutChart" => "doughnut", _ => "" }; if (kind.Length == 0) continue;
                if (plot.Element(C + "grouping")?.Attribute("val")?.Value == "stacked") kind = kind == "bar" ? "stackedBar" : kind == "column" ? "stackedColumn" : kind;
                var ranges = plot.Elements(C + "ser").SelectMany(s => s.Descendants(C + "f")).Select(e => Regex.Match(e.Value, @"!\$?([A-Z]+)\$?([1-9][0-9]*):\$?([A-Z]+)\$?([1-9][0-9]*)$", RegexOptions.IgnoreCase)).Where(m => m.Success).ToArray(); if (ranges.Length == 0) continue;
                int left = ranges.Min(m => SheetData.Position(m.Groups[1].Value.ToUpperInvariant() + "1").Column), right = ranges.Max(m => SheetData.Position(m.Groups[3].Value.ToUpperInvariant() + "1").Column), first = ranges.Min(m => int.Parse(m.Groups[2].Value)), last = ranges.Max(m => int.Parse(m.Groups[4].Value));
                if (first > 1) { var header = plot.Elements(C + "ser").FirstOrDefault()?.Element(C + "tx"); if (header?.Descendants(C + "f").Any() == true) first--; }
                charts.Add(new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["type"] = kind, ["title"] = string.Concat(xml.Descendants(C + "title").FirstOrDefault()?.Descendants(A + "t").Select(t => t.Value) ?? []), ["range"] = OfficeFile.Column(left) + first + ":" + OfficeFile.Column(right) + last, ["sourcePart"] = chartPath, ["sourceDrawing"] = drawingPath, ["sourceRelationship"] = referenceId });
            }
        }
        return charts;
    }
}
