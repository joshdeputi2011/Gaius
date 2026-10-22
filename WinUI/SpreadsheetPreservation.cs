using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Gaius.Office;

internal static partial class OfficeInterchange
{
    // Patch the edited subset in the source package. Unsupported Excel objects and
    // properties remain in their original parts instead of being silently removed.
    private static byte[] PreserveXlsx(OfficeFile file, Dictionary<string, byte[]> generated)
    {
        if (file.Data["xlsxSource"] is not JsonValue source || file.Data["xlsxBaseline"] is not JsonArray baseline) return Zip(generated);
        var sheets = file.Data["worksheets"] as JsonArray ?? new JsonArray(file.Sheet.DeepClone());
        var original = Unzip(Convert.FromBase64String(source.ToString()));
        if (JsonNode.DeepEquals(sheets, baseline)) return Zip(original);
        var workbook = Read(original, "xl/workbook.xml"); var workbookRels = Read(original, "xl/_rels/workbook.xml.rels"); var workbookTargets = Targets(original, "xl/workbook.xml");
        string styleRelationship = workbookRels.Root!.Elements().FirstOrDefault(e => e.Attribute("Type")?.Value == R.NamespaceName + "/styles")?.Attribute("Id")?.Value ?? "";
        string stylesPath = workbookTargets.GetValueOrDefault(styleRelationship) ?? "xl/styles.xml";
        int styleOffset = MergeSpreadsheetStyles(original, generated, stylesPath);
        var preservedStyles = Read(original, stylesPath); var overlayStyles = new Dictionary<string, int>();
        var contentTypes = Read(original, "[Content_Types].xml"); var freshTypes = Read(generated, "[Content_Types].xml");
        var freshWorkbook = Read(generated, "xl/workbook.xml"); var sourceEntries = workbook.Root!.Element(S + "sheets")!; var currentEntries = new List<XElement>();
        for (int i = 0; i < sheets.Count; i++) {
            if (sheets[i] is not JsonObject sheet) continue;
            string freshPath = $"xl/worksheets/sheet{i + 1}.xml"; var freshXml = Read(generated, freshPath);
            string? sourcePath = sheet["sourcePart"]?.ToString(); var before = baseline.OfType<JsonObject>().FirstOrDefault(b => b["sourcePart"]?.ToString() == sourcePath);
            if (sourcePath is null || before is null || !original.ContainsKey(sourcePath)) {
                string newPath = $"xl/worksheets/gaius-{Guid.NewGuid():N}.xml", newId = "gaiusSheet" + Guid.NewGuid().ToString("N");
                foreach (var cell in freshXml.Descendants(S + "c")) if (int.TryParse(cell.Attribute("s")?.Value, out int id)) cell.SetAttributeValue("s", id + styleOffset);
                original[newPath] = System.Text.Encoding.UTF8.GetBytes(freshXml.ToString(SaveOptions.DisableFormatting));
                string freshRels = RelationshipsPath(freshPath); if (generated.TryGetValue(freshRels, out var newRels)) original[RelationshipsPath(newPath)] = newRels;
                workbookRels.Root!.Add(E(Rel, "Relationship", new XAttribute("Id", newId), new XAttribute("Type", R.NamespaceName + "/worksheet"), new XAttribute("Target", "worksheets/" + Path.GetFileName(newPath))));
                currentEntries.Add(E(S, "sheet", new XAttribute("name", freshWorkbook.Root!.Element(S + "sheets")!.Elements().ElementAt(i).Attribute("name")!.Value), new XAttribute("sheetId", 1000 + i), new XAttribute(R + "id", newId)));
                contentTypes.Root!.Add(E(Ct, "Override", new XAttribute("PartName", "/" + newPath), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
                continue;
            }
            var entry = sourceEntries.Elements(S + "sheet").FirstOrDefault(e => workbookTargets.GetValueOrDefault(e.Attribute(R + "id")?.Value ?? "") == sourcePath);
            if (entry is null) throw new FormatException("The source worksheet relationship is missing. Save this document as a Gaius file to preserve its contents.");
            var renamed = new XElement(entry); renamed.SetAttributeValue("name", freshWorkbook.Root!.Element(S + "sheets")!.Elements().ElementAt(i).Attribute("name")!.Value); currentEntries.Add(renamed);
            var xml = Read(original, sourcePath); var data = xml.Root!.Element(S + "sheetData")!;
            var addresses = ((JsonObject)before["cells"]!).Select(p => p.Key).Concat(((JsonObject)sheet["cells"]!).Select(p => p.Key)).Concat((before["styles"] as JsonObject ?? new()).Select(p => p.Key)).Concat((sheet["styles"] as JsonObject ?? new()).Select(p => p.Key)).Distinct();
            var freshCells = freshXml.Descendants(S + "c").ToDictionary(c => c.Attribute("r")!.Value); var originalCells = xml.Descendants(S + "c").ToDictionary(c => c.Attribute("r")!.Value);
            foreach (string address in addresses) {
                bool valueChanged = !JsonNode.DeepEquals(before["cells"]?[address], sheet["cells"]?[address]), styleChanged = !JsonNode.DeepEquals(before["styles"]?[address], sheet["styles"]?[address]); if (!valueChanged && !styleChanged) continue;
                originalCells.TryGetValue(address, out var existing); freshCells.TryGetValue(address, out var replacement);
                if (replacement is null) { if (existing is not null) { existing.Elements().Where(e => e.Name == S + "v" || e.Name == S + "f" || e.Name == S + "is").Remove(); existing.Attribute("t")?.Remove(); if (styleChanged) existing.Attribute("s")?.Remove(); } continue; }
                var cell = existing is null ? new XElement(replacement) : new XElement(existing);
                if (valueChanged || existing is null) { cell.Elements().Where(e => e.Name == S + "v" || e.Name == S + "f" || e.Name == S + "is").Remove(); cell.SetAttributeValue("t", replacement.Attribute("t")?.Value); cell.Add(replacement.Elements().Select(e => new XElement(e))); }
                if (styleChanged || existing is null) {
                    int mappedStyle = int.TryParse(replacement.Attribute("s")?.Value, out int styleId) ? styleId + styleOffset : styleOffset;
                    if (existing is not null) mappedStyle = OverlaySpreadsheetStyle(preservedStyles, (int)Number(existing.Attribute("s")?.Value), before["styles"]?[address] as JsonObject ?? new(), sheet["styles"]?[address] as JsonObject ?? new(), mappedStyle, overlayStyles);
                    cell.SetAttributeValue("s", mappedStyle);
                }
                if (existing is not null) existing.ReplaceWith(cell); else { int rowIndex = SheetData.Position(address).Row; var row = data.Elements(S + "row").FirstOrDefault(r => r.Attribute("r")?.Value == rowIndex.ToString(CultureInfo.InvariantCulture)); if (row is null) { row = E(S, "row", new XAttribute("r", rowIndex)); data.Add(row); } row.Add(cell); }
            }
            if (!JsonNode.DeepEquals(before["columnWidths"], sheet["columnWidths"])) PatchColumnDimensions(xml, before, sheet);
            if (!JsonNode.DeepEquals(before["rowHeights"], sheet["rowHeights"])) PatchRowDimensions(xml, before, sheet);
            foreach (var row in data.Elements(S + "row")) { var ordered = row.Elements(S + "c").OrderBy(c => SheetData.Position(c.Attribute("r")!.Value).Column).ToArray(); ordered.Remove(); row.Add(ordered); }
            var orderedRows = data.Elements(S + "row").OrderBy(r => Number(r.Attribute("r")?.Value)).ToArray(); orderedRows.Remove(); data.Add(orderedRows);
            if (!JsonNode.DeepEquals(before["charts"], sheet["charts"])) MergeSpreadsheetDrawings(original, generated, sourcePath, freshPath, xml, freshXml, before);
            original[sourcePath] = System.Text.Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting));
        }
        sourceEntries.ReplaceNodes(currentEntries);
        original[stylesPath] = System.Text.Encoding.UTF8.GetBytes(preservedStyles.ToString(SaveOptions.DisableFormatting));
        var views = workbook.Root!.Element(S + "bookViews"); if (views is null) { views = E(S, "bookViews", E(S, "workbookView")); sourceEntries.AddBeforeSelf(views); } views.Element(S + "workbookView")?.SetAttributeValue("activeTab", Math.Clamp(file.Data["activeSheet"]?.GetValue<int>() ?? 0, 0, Math.Max(0, sheets.Count - 1)));
        var calculation = workbook.Root!.Element(S + "calcPr"); if (calculation is null) { calculation = E(S, "calcPr"); workbook.Root.Add(calculation); } calculation.SetAttributeValue("fullCalcOnLoad", 1); calculation.SetAttributeValue("forceFullCalc", 1);
        original["xl/workbook.xml"] = System.Text.Encoding.UTF8.GetBytes(workbook.ToString(SaveOptions.DisableFormatting)); original["xl/_rels/workbook.xml.rels"] = System.Text.Encoding.UTF8.GetBytes(workbookRels.ToString(SaveOptions.DisableFormatting));
        foreach (var pair in generated.Where(p => p.Key.StartsWith("xl/charts/gaius-", StringComparison.Ordinal) || p.Key.StartsWith("xl/drawings/", StringComparison.Ordinal))) original[pair.Key] = pair.Value;
        foreach (var type in freshTypes.Root!.Elements(Ct + "Override").Where(e => e.Attribute("PartName")?.Value.Contains("/gaius-", StringComparison.Ordinal) == true)) if (!contentTypes.Root!.Elements(Ct + "Override").Any(e => e.Attribute("PartName")?.Value == type.Attribute("PartName")?.Value)) contentTypes.Root.Add(new XElement(type));
        original["[Content_Types].xml"] = System.Text.Encoding.UTF8.GetBytes(contentTypes.ToString(SaveOptions.DisableFormatting)); return Zip(original);
    }
    private static string RelationshipsPath(string source) => source[..(source.LastIndexOf('/') + 1)] + "_rels/" + Path.GetFileName(source) + ".rels";
    private static int OverlaySpreadsheetStyle(XDocument styles, int originalId, JsonObject before, JsonObject after, int generatedId, Dictionary<string, int> cache)
    {
        string key = originalId + ":" + before.ToJsonString() + ":" + after.ToJsonString(); if (cache.TryGetValue(key, out int cached)) return cached;
        var formats = styles.Root!.Element(S + "cellXfs")!; var original = formats.Elements().ElementAtOrDefault(originalId); var generated = formats.Elements().ElementAtOrDefault(generatedId); if (original is null || generated is null) return generatedId;
        bool Changed(string property) => !JsonNode.DeepEquals(before[property], after[property]); var result = new XElement(original);
        if (new[] { "bold", "italic", "color" }.Any(Changed)) {
            var fonts = styles.Root.Element(S + "fonts")!; var font = new XElement(fonts.Elements().ElementAtOrDefault((int)Number(original.Attribute("fontId")?.Value)) ?? E(S, "font"));
            foreach (var (property, element) in new[] { ("bold", "b"), ("italic", "i"), ("color", "color") }) if (Changed(property)) { font.Elements(S + element).Remove(); if (property == "color" && after[property] is JsonNode color) font.Add(E(S, "color", new XAttribute("rgb", "FF" + color.ToString().TrimStart('#')))); else if (property != "color" && after[property]?.GetValue<bool>() == true) font.Add(E(S, element)); }
            result.SetAttributeValue("fontId", fonts.Elements().Count()); fonts.Add(font); fonts.SetAttributeValue("count", fonts.Elements().Count()); result.SetAttributeValue("applyFont", 1);
        }
        if (Changed("background")) { result.SetAttributeValue("fillId", generated.Attribute("fillId")?.Value ?? "0"); result.SetAttributeValue("applyFill", 1); }
        if (Changed("format")) { result.SetAttributeValue("numFmtId", generated.Attribute("numFmtId")?.Value ?? "0"); result.SetAttributeValue("applyNumberFormat", 1); }
        if (Changed("align") || Changed("wrap")) {
            var alignment = result.Element(S + "alignment"); if (alignment is null) { alignment = E(S, "alignment"); var next = result.Element(S + "protection") ?? result.Element(S + "extLst"); if (next is not null) next.AddBeforeSelf(alignment); else result.Add(alignment); }
            if (Changed("align")) alignment.SetAttributeValue("horizontal", after["align"]?.ToString() ?? "left"); if (Changed("wrap")) alignment.SetAttributeValue("wrapText", after["wrap"]?.GetValue<bool>() == true ? 1 : 0); result.SetAttributeValue("applyAlignment", 1);
        }
        int index = formats.Elements().Count(); formats.Add(result); formats.SetAttributeValue("count", formats.Elements().Count()); cache[key] = index; return index;
    }
    private static int MergeSpreadsheetStyles(Dictionary<string, byte[]> original, Dictionary<string, byte[]> generated, string path)
    {
        if (!original.ContainsKey(path)) { original[path] = generated["xl/styles.xml"]; return 0; }
        var xml = Read(original, path); var fresh = Read(generated, "xl/styles.xml");
        XElement Group(string name) { var group = xml.Root!.Element(S + name); if (group is null) { group = E(S, name); xml.Root.Add(group); } return group; }
        var fonts = Group("fonts"); var fills = Group("fills"); var formats = Group("cellXfs"); var nums = Group("numFmts"); int fontOffset = fonts.Elements().Count(), fillOffset = fills.Elements().Count(), styleOffset = formats.Elements().Count();
        int currency = nums.Elements().FirstOrDefault(n => n.Attribute("formatCode")?.Value == "$#,##0.00") is XElement found ? (int)Number(found.Attribute("numFmtId")?.Value) : Math.Max(164, nums.Elements().Select(n => (int)Number(n.Attribute("numFmtId")?.Value)).DefaultIfEmpty(163).Max() + 1);
        if (!nums.Elements().Any(n => n.Attribute("numFmtId")?.Value == currency.ToString(CultureInfo.InvariantCulture))) nums.Add(E(S, "numFmt", new XAttribute("numFmtId", currency), new XAttribute("formatCode", "$#,##0.00")));
        fonts.Add(fresh.Root!.Element(S + "fonts")!.Elements().Select(e => new XElement(e))); fills.Add(fresh.Root.Element(S + "fills")!.Elements().Select(e => new XElement(e)));
        foreach (var format in fresh.Root.Element(S + "cellXfs")!.Elements()) { var clone = new XElement(format); clone.SetAttributeValue("fontId", (int)Number(clone.Attribute("fontId")?.Value) + fontOffset); clone.SetAttributeValue("fillId", (int)Number(clone.Attribute("fillId")?.Value) + fillOffset); if (clone.Attribute("numFmtId")?.Value == "164") clone.SetAttributeValue("numFmtId", currency); formats.Add(clone); }
        foreach (var group in new[] { fonts, fills, formats, nums }) group.SetAttributeValue("count", group.Elements().Count());
        if (nums.PreviousNode is not null) { nums.Remove(); xml.Root!.AddFirst(nums); }
        original[path] = System.Text.Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting)); return styleOffset;
    }
    private static void PatchColumnDimensions(XDocument xml, JsonObject before, JsonObject sheet)
    {
        var columns = xml.Root!.Element(S + "cols"); if (columns is null) { columns = E(S, "cols"); xml.Root.Element(S + "sheetData")!.AddBeforeSelf(columns); }
        var keys = (before["columnWidths"] as JsonObject ?? new()).Select(p => p.Key).Concat((sheet["columnWidths"] as JsonObject ?? new()).Select(p => p.Key)).Distinct();
        foreach (string key in keys) {
            if (JsonNode.DeepEquals(before["columnWidths"]?[key], sheet["columnWidths"]?[key])) continue; int column = SheetData.Position(key + "1").Column + 1;
            var existing = columns.Elements(S + "col").FirstOrDefault(c => Number(c.Attribute("min")?.Value) <= column && Number(c.Attribute("max")?.Value) >= column);
            XElement target = existing is null ? E(S, "col") : new XElement(existing);
            if (existing is not null) { int min = (int)Number(existing.Attribute("min")?.Value), max = (int)Number(existing.Attribute("max")?.Value); if (min < column) { var left = new XElement(existing); left.SetAttributeValue("max", column - 1); existing.AddBeforeSelf(left); } if (max > column) { var right = new XElement(existing); right.SetAttributeValue("min", column + 1); existing.AddAfterSelf(right); } existing.Remove(); }
            target.SetAttributeValue("min", column); target.SetAttributeValue("max", column); target.SetAttributeValue("width", SheetDimensions.ToExcelWidth(SheetDimensions.ColumnWidth(sheet, column - 1))); target.SetAttributeValue("customWidth", 1); columns.Add(target);
        }
        var sorted = columns.Elements().OrderBy(c => Number(c.Attribute("min")?.Value)).ToArray(); columns.ReplaceNodes(sorted);
    }
    private static void PatchRowDimensions(XDocument xml, JsonObject before, JsonObject sheet)
    {
        var data = xml.Root!.Element(S + "sheetData")!; var keys = (before["rowHeights"] as JsonObject ?? new()).Select(p => p.Key).Concat((sheet["rowHeights"] as JsonObject ?? new()).Select(p => p.Key)).Distinct();
        foreach (string key in keys) { if (JsonNode.DeepEquals(before["rowHeights"]?[key], sheet["rowHeights"]?[key]) || !int.TryParse(key, out int index)) continue; var row = data.Elements(S + "row").FirstOrDefault(r => r.Attribute("r")?.Value == key); if (row is null) { row = E(S, "row", new XAttribute("r", index)); data.Add(row); } row.SetAttributeValue("ht", SheetDimensions.RowHeight(sheet, index) * .75); row.SetAttributeValue("customHeight", 1); }
    }
    private static void MergeSpreadsheetDrawings(Dictionary<string, byte[]> original, Dictionary<string, byte[]> generated, string originalPath, string freshPath, XDocument xml, XDocument freshXml, JsonObject before)
    {
        if (before["charts"] is JsonArray charts) foreach (var group in charts.OfType<JsonObject>().Where(c => c["sourceDrawing"] is not null).GroupBy(c => c["sourceDrawing"]!.ToString())) {
            var drawing = Read(original, group.Key); var ids = group.Select(c => c["sourceRelationship"]?.ToString()).ToHashSet(); foreach (var reference in drawing.Descendants(C + "chart").Where(c => ids.Contains(c.Attribute(R + "id")?.Value)).ToArray()) reference.Ancestors().FirstOrDefault(a => a.Parent == drawing.Root)?.Remove(); original[group.Key] = System.Text.Encoding.UTF8.GetBytes(drawing.ToString(SaveOptions.DisableFormatting));
        }
        var freshDrawing = freshXml.Root!.Element(S + "drawing"); if (freshDrawing is null) return;
        string freshTarget = Targets(generated, freshPath)[freshDrawing.Attribute(R + "id")!.Value]; var newDrawing = Read(generated, freshTarget); var newRels = Read(generated, RelationshipsPath(freshTarget));
        var existing = xml.Root!.Element(S + "drawing"); if (existing is null) {
            string id = "gaiusDrawing" + Guid.NewGuid().ToString("N"); var relPath = RelationshipsPath(originalPath); var rels = original.ContainsKey(relPath) ? Read(original, relPath) : new XDocument(Relationships([])); rels.Root!.Add(E(Rel, "Relationship", new XAttribute("Id", id), new XAttribute("Type", R.NamespaceName + "/drawing"), new XAttribute("Target", "../drawings/" + Path.GetFileName(freshTarget)))); original[relPath] = System.Text.Encoding.UTF8.GetBytes(rels.ToString(SaveOptions.DisableFormatting)); xml.Root.Add(E(S, "drawing", new XAttribute(R + "id", id))); return;
        }
        string existingTarget = Targets(original, originalPath)[existing.Attribute(R + "id")!.Value]; var existingDrawing = Read(original, existingTarget); string existingRelPath = RelationshipsPath(existingTarget); var existingRels = original.ContainsKey(existingRelPath) ? Read(original, existingRelPath) : new XDocument(Relationships([]));
        foreach (var relationship in newRels.Root!.Elements()) { string oldId = relationship.Attribute("Id")!.Value, newId = "gaius" + Guid.NewGuid().ToString("N"); relationship.SetAttributeValue("Id", newId); foreach (var reference in newDrawing.Descendants(C + "chart").Where(c => c.Attribute(R + "id")?.Value == oldId)) reference.SetAttributeValue(R + "id", newId); existingRels.Root!.Add(new XElement(relationship)); }
        uint nextId = (uint)existingDrawing.Descendants(Xdr + "cNvPr").Select(e => Number(e.Attribute("id")?.Value)).DefaultIfEmpty(1).Max() + 1; foreach (var property in newDrawing.Descendants(Xdr + "cNvPr")) property.SetAttributeValue("id", nextId++);
        existingDrawing.Root!.Add(newDrawing.Root!.Elements().Select(e => new XElement(e))); original[existingTarget] = System.Text.Encoding.UTF8.GetBytes(existingDrawing.ToString(SaveOptions.DisableFormatting)); original[existingRelPath] = System.Text.Encoding.UTF8.GetBytes(existingRels.ToString(SaveOptions.DisableFormatting));
    }
}
