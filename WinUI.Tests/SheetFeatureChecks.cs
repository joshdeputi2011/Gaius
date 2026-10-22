using Gaius.Office;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;

internal static class SheetFeatureChecks
{
    public static void Run(Action<bool, string> check)
    {
        var book = OfficeFile.Create("spreadsheet"); var sheet = book.Sheet; sheet["name"] = "Revenue";
        sheet["cells"] = new JsonObject { ["A1"] = "Month", ["B1"] = "Actual", ["C1"] = "Plan", ["A2"] = "1", ["B2"] = "24", ["C2"] = "30", ["A3"] = "2", ["B3"] = "-8", ["C3"] = "5", ["A4"] = "3", ["B4"] = "16", ["C4"] = "20" };
        sheet["styles"] = new JsonObject { ["B2"] = new JsonObject { ["wrap"] = true, ["bold"] = true }, ["D4"] = new JsonObject { ["background"] = "#fff0aa" } };
        SheetDimensions.SetColumnWidth(sheet, 0, 218); SheetDimensions.SetColumnWidth(sheet, 1, 149); SheetDimensions.SetRowHeight(sheet, 2, 65);
        check(Math.Abs(SheetDimensions.ColumnWidth(sheet, 0) - 218) < .01 && SheetDimensions.RowHeight(sheet, 2) == 65, "Sheet dimensions did not persist in the portable model");
        SheetDimensions.SetColumnWidth(sheet, 2, double.NaN); SheetDimensions.SetRowHeight(sheet, 3, -10);
        check(SheetDimensions.ColumnWidth(sheet, 2) == SheetDimensions.DefaultColumnWidth && SheetDimensions.RowHeight(sheet, 3) == 20, "Sheet dimension bounds failed");
        var data = SheetChartModel.Read(sheet, "A1:C4");
        check(data.Series.Length == 2 && data.Series[0].Name == "Actual" && data.Labels.Length == 3 && data.Series[0].Values[1] == -8, "Chart headers, multiple series or negative values lost");
        var charts = new JsonArray();
        foreach (var (kind, _) in SheetChartModel.Types) { SheetChartModel.Validate(data, kind); charts.Add(new JsonObject { ["id"] = kind, ["type"] = kind, ["range"] = "A1:C4", ["title"] = kind + " chart" }); }
        sheet["charts"] = charts; var exported = OfficeInterchange.Export(book); var parts = Read(exported);
        check(parts.Keys.Count(p => p.StartsWith("xl/charts/", StringComparison.Ordinal) && p.EndsWith(".xml", StringComparison.Ordinal)) == 9, "Not every chart type was exported as an editable Office chart");
        var reopened = OfficeInterchange.Import(exported, ".xlsx"); var imported = reopened.Sheet;
        check(Math.Abs(SheetDimensions.ColumnWidth(imported, 0) - 218) < 1 && Math.Abs(SheetDimensions.RowHeight(imported, 2) - 65) < .01, "XLSX dimensions failed round trip");
        check(imported["styles"]?["B2"]?["wrap"]?.GetValue<bool>() == true && imported["styles"]?["D4"]?["background"]?.ToString() == "#fff0aa", "XLSX wrapping or blank-cell formatting lost");
        var importedCharts = imported["charts"]!.AsArray();
        foreach (var (kind, _) in SheetChartModel.Types) { var chart = importedCharts.FirstOrDefault(c => c?["type"]?.ToString() == kind); check(chart is not null && chart["range"]?.ToString() == (kind is "pie" or "doughnut" ? "A1:B4" : "A1:C4"), "XLSX chart type/range lost: " + kind); if (chart is not null) SheetChartModel.Validate(SheetChartModel.Read(imported, chart["range"]!.ToString()), kind); }
        // Inject a feature the editor does not model; it must survive supported edits.
        XNamespace s = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sourceSheet = XDocument.Parse(Encoding.UTF8.GetString(parts["xl/worksheets/sheet1.xml"])); sourceSheet.Root!.Add(new XElement(s + "mergeCells", new XAttribute("count", 1), new XElement(s + "mergeCell", new XAttribute("ref", "E7:F7"))));
        parts["xl/worksheets/sheet1.xml"] = Encoding.UTF8.GetBytes(sourceSheet.ToString()); parts["customXml/retained.xml"] = Encoding.UTF8.GetBytes("<retained value='123'/>");
        var sourceStyles = XDocument.Parse(Encoding.UTF8.GetString(parts["xl/styles.xml"])); var b2 = sourceSheet.Descendants(s + "c").First(c => c.Attribute("r")?.Value == "B2"); var b2Style = sourceStyles.Root!.Element(s + "cellXfs")!.Elements().ElementAt(int.Parse(b2.Attribute("s")!.Value));
        var b2Font = sourceStyles.Root.Element(s + "fonts")!.Elements().ElementAt(int.Parse(b2Style.Attribute("fontId")!.Value)); b2Font.Element(s + "name")!.SetAttributeValue("val", "Cambria"); b2Font.Element(s + "sz")!.SetAttributeValue("val", 19); b2Style.Element(s + "alignment")!.SetAttributeValue("vertical", "center"); b2Style.SetAttributeValue("numFmtId", 165); sourceStyles.Root.Element(s + "numFmts")!.Add(new XElement(s + "numFmt", new XAttribute("numFmtId", 165), new XAttribute("formatCode", "0.0000"))); sourceStyles.Root.Element(s + "numFmts")!.SetAttributeValue("count", 2); parts["xl/styles.xml"] = Encoding.UTF8.GetBytes(sourceStyles.ToString());
        var source = Write(parts); var preservedBook = OfficeInterchange.Import(source, ".xlsx"); var unchanged = Read(OfficeInterchange.Export(preservedBook));
        check(parts.All(p => unchanged.TryGetValue(p.Key, out var value) && value.SequenceEqual(p.Value)), "Unchanged XLSX export modified source parts");
        preservedBook.Sheet["cells"]!["B2"] = "99"; SheetDimensions.SetColumnWidth(preservedBook.Sheet, 0, 260); SheetDimensions.SetRowHeight(preservedBook.Sheet, 2, 72);
        preservedBook.Sheet["styles"]!["B2"]!["color"] = "#185abd";
        var preservedBytes = OfficeInterchange.Export(preservedBook); var patched = Read(preservedBytes); var patchedBook = OfficeInterchange.Import(preservedBytes, ".xlsx");
        check(patchedBook.Sheet["cells"]?["B2"]?.ToString() == "99" && Math.Abs(SheetDimensions.ColumnWidth(patchedBook.Sheet, 0) - 260) < 1 && SheetDimensions.RowHeight(patchedBook.Sheet, 2) == 72, "Source-preserving XLSX edit did not update values and dimensions");
        check(patchedBook.Sheet["styles"]?["B2"]?["color"]?.ToString() == "#185abd" && patchedBook.Sheet["styles"]?["B2"]?["wrap"]?.GetValue<bool>() == true, "Source-preserving XLSX edit did not merge styles");
        var patchedSheetXml = XDocument.Parse(Encoding.UTF8.GetString(patched["xl/worksheets/sheet1.xml"])); var patchedStyles = XDocument.Parse(Encoding.UTF8.GetString(patched["xl/styles.xml"])); var styledCell = patchedSheetXml.Descendants(s + "c").First(c => c.Attribute("r")?.Value == "B2"); var changedStyle = patchedStyles.Root!.Element(s + "cellXfs")!.Elements().ElementAt(int.Parse(styledCell.Attribute("s")!.Value)); var changedFont = patchedStyles.Root.Element(s + "fonts")!.Elements().ElementAt(int.Parse(changedStyle.Attribute("fontId")!.Value));
        check(changedFont.Element(s + "name")?.Attribute("val")?.Value == "Cambria" && changedFont.Element(s + "sz")?.Attribute("val")?.Value == "19" && changedStyle.Attribute("numFmtId")?.Value == "165" && changedStyle.Element(s + "alignment")?.Attribute("vertical")?.Value == "center", "Changing a supported cell property removed source font, custom format or vertical alignment");
        check(patched["customXml/retained.xml"].SequenceEqual(parts["customXml/retained.xml"]) && Encoding.UTF8.GetString(patched["xl/worksheets/sheet1.xml"]).Contains("E7:F7"), "Supported XLSX edits removed unsupported source content");
        ((JsonArray)preservedBook.Sheet["charts"]!).RemoveAt(0); var chartEdited = OfficeInterchange.Import(OfficeInterchange.Export(preservedBook), ".xlsx");
        check(chartEdited.Sheet["charts"]?.AsArray().Count == 8, "XLSX chart deletion duplicated or lost charts");
        var first = preservedBook.Sheet.DeepClone(); preservedBook.Data["worksheets"] = new JsonArray(first, new JsonObject { ["name"] = "Added", ["cells"] = new JsonObject { ["A1"] = "new sheet" }, ["rows"] = 100, ["columns"] = 26 });
        var added = OfficeInterchange.Import(OfficeInterchange.Export(preservedBook), ".xlsx"); check(added.Data["worksheets"]?.AsArray().Count == 2, "Source-preserving XLSX export lost an added worksheet");
        var invalid = (JsonObject)sheet.DeepClone(); invalid["cells"]!["A2"] = "Jan"; invalid["cells"]!["A3"] = "Feb"; invalid["cells"]!["A4"] = "Mar"; bool rejected = false; try { SheetChartModel.Validate(SheetChartModel.Read(invalid, "A1:C4"), "scatter"); } catch (FormatException) { rejected = true; }
        check(rejected, "Scatter chart silently accepted nonnumeric X values");
        if (Environment.GetEnvironmentVariable("GAIUS_WRITE_SHEET_SAMPLES") is string destination && Directory.Exists(destination)) { File.WriteAllBytes(Path.Combine(destination, "all-chart-types.xlsx"), exported); File.WriteAllBytes(Path.Combine(destination, "preserved-chart-types.xlsx"), preservedBytes); }
    }
    private static Dictionary<string, byte[]> Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes); using var archive = new ZipArchive(stream, ZipArchiveMode.Read); var parts = new Dictionary<string, byte[]>(); foreach (var entry in archive.Entries) { using var source = entry.Open(); using var output = new MemoryStream(); source.CopyTo(output); parts[entry.FullName] = output.ToArray(); } return parts;
    }
    private static byte[] Write(Dictionary<string, byte[]> parts)
    {
        using var output = new MemoryStream(); using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true)) foreach (var part in parts) { using var entry = archive.CreateEntry(part.Key).Open(); entry.Write(part.Value); } return output.ToArray();
    }
}
