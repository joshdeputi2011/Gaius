using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Gaius.Office;

internal static partial class OfficeInterchange
{
    private static readonly XNamespace O = "urn:oasis:names:tc:opendocument:xmlns:office:1.0", T = "urn:oasis:names:tc:opendocument:xmlns:text:1.0", St = "urn:oasis:names:tc:opendocument:xmlns:style:1.0", Fo = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0", Tb = "urn:oasis:names:tc:opendocument:xmlns:table:1.0", Dr = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0", Xl = "http://www.w3.org/1999/xlink", Svg = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0", Mf = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
    private const string OdtMime = "application/vnd.oasis.opendocument.text";

    internal static OfficeFile ImportOdt(byte[] bytes)
    {
        var parts = Unzip(bytes); var xml = Read(parts, "content.xml");
        var styleNodes = xml.Descendants(St + "style").ToList();
        if (parts.ContainsKey("styles.xml")) styleNodes.InsertRange(0, Read(parts, "styles.xml").Descendants(St + "style"));
        var styles = styleNodes.Where(e => e.Attribute(St + "name") is not null).GroupBy(e => e.Attribute(St + "name")!.Value).ToDictionary(g => g.Key, g => g.Last());
        var delta = new JsonArray();
        JsonObject Format(string? name, HashSet<string>? visited = null)
        {
            visited ??= []; if (name is null || !visited.Add(name) || !styles.TryGetValue(name, out var style)) return new();
            var result = Format(style.Attribute(St + "parent-style-name")?.Value, visited);
            if (style.Element(St + "text-properties") is { } props) {
                string? value = props.Attribute(Fo + "font-weight")?.Value; if (value is not null) result["bold"] = value == "bold" || Number(value) >= 600;
                if (props.Attribute(Fo + "font-style") is { } italic) result["italic"] = italic.Value == "italic";
                if (props.Attribute(St + "text-underline-style") is { } underline) result["underline"] = underline.Value != "none";
                foreach (var (source, key) in new[] { ("color", "color"), ("background-color", "background") }) if (props.Attribute(Fo + source)?.Value is { } color && System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$")) result[key] = color;
                if (props.Attribute(Fo + "font-size") is { } size) result["size"] = (Length(size.Value) / .75).ToString(CultureInfo.InvariantCulture);
                if (props.Attribute(Fo + "font-family")?.Value is { } family) result["font"] = family.Trim('\'', '"');
            }
            if (style.Element(St + "paragraph-properties")?.Attribute(Fo + "text-align")?.Value is { } align && align is "center" or "right" or "justify") result["align"] = align;
            return result;
        }
        void Text(string text, JsonObject attrs) { if (text.Length > 0) delta.Add(new JsonObject { ["insert"] = text, ["attributes"] = attrs.DeepClone() }); }
        void Inline(XNode node, JsonObject attrs)
        {
            if (node is XText text) { Text(text.Value, attrs); return; }
            if (node is not XElement element) return;
            if (element.Name == T + "s") { Text(new string(' ', Math.Clamp((int)Number(element.Attribute(T + "c")?.Value, 1), 1, 10000)), attrs); return; }
            if (element.Name == T + "tab") { Text("\t", attrs); return; }
            if (element.Name == T + "line-break") { Text("\n", attrs); return; }
            if (element.Name == Dr + "image") {
                string? path = element.Attribute(Xl + "href")?.Value?.TrimStart('.', '/');
                if (ImageUrl(parts, path) is { } image) delta.Add(new JsonObject { ["insert"] = new JsonObject { ["image"] = image } }); return;
            }
            var next = (JsonObject)attrs.DeepClone(); foreach (var pair in Format(element.Attribute(T + "style-name")?.Value)) next[pair.Key] = pair.Value?.DeepClone();
            if (element.Name == T + "a" && element.Attribute(Xl + "href")?.Value is { } href && Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto") next["link"] = href;
            foreach (var child in element.Nodes()) Inline(child, next);
        }
        void Block(XElement block)
        {
            if (block.Name == Tb + "table") {
                var rows = new JsonArray(); foreach (var row in block.Elements(Tb + "table-row").Take(1000)) { var cells = new JsonArray(); foreach (var cell in row.Elements().Where(e => e.Name == Tb + "table-cell" || e.Name == Tb + "covered-table-cell").Take(100)) { int repeat = Math.Clamp((int)Number(cell.Attribute(Tb + "number-columns-repeated")?.Value, 1), 1, 100 - cells.Count); for (int i = 0; i < repeat; i++) cells.Add(string.Join("\n", cell.Descendants(T + "p").Select(p => p.Value))); if (cells.Count >= 100) break; } rows.Add(cells); }
                delta.Add(new JsonObject { ["insert"] = new JsonObject { ["gaius-table"] = rows.ToJsonString() } }); Text("\n", new()); return;
            }
            if (block.Name == T + "p" || block.Name == T + "h") {
                var attrs = Format(block.Attribute(T + "style-name")?.Value); foreach (var node in block.Nodes()) Inline(node, attrs);
                if (block.Name == T + "h") attrs["header"] = Math.Clamp((int)Number(block.Attribute(T + "outline-level")?.Value, 1), 1, 6);
                Text("\n", attrs); return;
            }
            foreach (var child in block.Elements()) Block(child);
        }
        var body = xml.Root?.Element(O + "body")?.Element(O + "text") ?? throw new FormatException("This package is not an OpenDocument text file.");
        foreach (var block in body.Elements()) Block(block);
        var file = OfficeFile.Create("document"); file.Content["delta"] = delta; file.Content["html"] = RtfPipe.Rtf.ToHtml(RichTextInterop.ToRtf(delta)); file.Content["text"] = string.Concat(delta.Select(op => op?["insert"] is JsonValue value ? value.ToString() : ""));
        file.Data["importWarning"] = "ODT content import supports text, common formatting, tables and raster images. Complex layouts, tracked changes and embedded objects may differ. The source remains available unchanged.";
        file.Data["originalFile"] = new JsonObject { ["extension"] = ".odt", ["dataBase64"] = Convert.ToBase64String(bytes), ["sha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant() }; file.Data["odtBaseline"] = OdtBaseline(file); return file;
    }
    private static double Length(string value)
    {
        var match = System.Text.RegularExpressions.Regex.Match(value, "^([0-9.]+)(pt|in|cm|mm|px)?$"); double size = Number(match.Groups[1].Value, 12);
        return match.Groups[2].Value switch { "in" => size * 72, "cm" => size * 72 / 2.54, "mm" => size * 72 / 25.4, "px" => size * .75, _ => size };
    }
    private static string OdtBaseline(OfficeFile file) => (file.Content["delta"]?.ToJsonString() ?? "") + (file.Data["layout"]?.ToJsonString() ?? "");
    internal static byte[] ExportOdt(OfficeFile file)
    {
        string? original = file.Data["originalFile"]?["extension"]?.ToString() == ".odt" ? file.Data["originalFile"]?["dataBase64"]?.ToString() : file.Data["odtSource"]?.ToString();
        if (original is not null && file.Data["odtBaseline"]?.ToString() == OdtBaseline(file)) return Convert.FromBase64String(original);
        var parts = new Dictionary<string, byte[]>(); var styles = E(O, "automatic-styles"); var body = E(O, "text"); var paragraph = E(T, "p"); int styleId = 0, imageId = 0;
        var manifest = E(Mf, "manifest", new XAttribute(Mf + "version", "1.3"), E(Mf, "file-entry", new XAttribute(Mf + "full-path", "/"), new XAttribute(Mf + "media-type", OdtMime)));
        void Flush(JsonNode? attrs = null) { if (attrs?["header"] is not null) { paragraph.Name = T + "h"; paragraph.SetAttributeValue(T + "outline-level", attrs["header"]!.ToString()); } body.Add(paragraph); paragraph = E(T, "p"); }
        foreach (var op in RichTextInterop.ImageOperations(file.Content["delta"] as JsonArray ?? RichTextInterop.HtmlToDelta(file.Content["html"]?.ToString() ?? ""))) {
            if (op?["insert"] is JsonObject embed) {
                if (embed["gaius-table"] is JsonValue table) { if (paragraph.HasElements || paragraph.Value.Length > 0) Flush(); var rows = JsonNode.Parse(table.ToString())!.AsArray(); body.Add(E(Tb, "table", rows.Select(row => E(Tb, "table-row", row!.AsArray().Select(cell => E(Tb, "table-cell", new XAttribute(O + "value-type", "string"), E(T, "p", cell?.ToString() ?? ""))))))); }
                else if (embed["image"] is { } data && ImageData(data.ToString()) is { } image) { string path = $"Pictures/image{++imageId}.{image.Extension}"; parts[path] = image.Bytes; manifest.Add(E(Mf, "file-entry", new XAttribute(Mf + "full-path", path), new XAttribute(Mf + "media-type", "image/" + (image.Extension == "jpg" ? "jpeg" : image.Extension)))); paragraph.Add(E(Dr, "frame", new XAttribute(T + "anchor-type", "as-char"), new XAttribute(Svg + "width", "12cm"), new XAttribute(Svg + "height", (12.0 * RichTextInterop.ImageDimensions(image.Bytes).Height / Math.Max(1, RichTextInterop.ImageDimensions(image.Bytes).Width)).ToString(CultureInfo.InvariantCulture) + "cm"), E(Dr, "image", new XAttribute(Xl + "href", path), new XAttribute(Xl + "type", "simple")))); }
                continue;
            }
            var attributes = op?["attributes"]; string name = "T" + ++styleId; var properties = E(St, "text-properties");
            foreach (var (key, attr, value) in new[] { ("bold", "font-weight", "bold"), ("italic", "font-style", "italic") }) if (attributes?[key]?.GetValue<bool>() == true) properties.SetAttributeValue(Fo + attr, value);
            if (attributes?["underline"]?.GetValue<bool>() == true) properties.SetAttributeValue(St + "text-underline-style", "solid");
            foreach (var (key, attr) in new[] { ("color", "color"), ("background", "background-color"), ("font", "font-family") }) if (attributes?[key] is { } value) properties.SetAttributeValue(Fo + attr, value.ToString());
            if (attributes?["size"] is { } size) properties.SetAttributeValue(Fo + "font-size", (Number(size.ToString(), 16) * .75).ToString(CultureInfo.InvariantCulture) + "pt");
            styles.Add(E(St, "style", new XAttribute(St + "name", name), new XAttribute(St + "family", "text"), properties));
            var lines = (op?["insert"]?.ToString() ?? "").Split('\n'); for (int i = 0; i < lines.Length; i++) { if (lines[i].Length > 0) paragraph.Add(E(T, "span", new XAttribute(T + "style-name", name), lines[i])); if (i < lines.Length - 1) Flush(attributes); }
        }
        if (paragraph.HasElements || paragraph.Value.Length > 0 || !body.HasElements) Flush();
        Put(parts, "content.xml", E(O, "document-content", new XAttribute(O + "version", "1.3"), new XAttribute(XNamespace.Xmlns + "office", O), new XAttribute(XNamespace.Xmlns + "text", T), new XAttribute(XNamespace.Xmlns + "style", St), new XAttribute(XNamespace.Xmlns + "fo", Fo), new XAttribute(XNamespace.Xmlns + "table", Tb), new XAttribute(XNamespace.Xmlns + "draw", Dr), new XAttribute(XNamespace.Xmlns + "xlink", Xl), new XAttribute(XNamespace.Xmlns + "svg", Svg), styles, E(O, "body", body)));
        manifest.Add(E(Mf, "file-entry", new XAttribute(Mf + "full-path", "content.xml"), new XAttribute(Mf + "media-type", "text/xml"))); Put(parts, "META-INF/manifest.xml", manifest);
        using var buffer = new MemoryStream(); using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, true)) { using (var mime = archive.CreateEntry("mimetype", CompressionLevel.NoCompression).Open()) mime.Write(Encoding.ASCII.GetBytes(OdtMime)); foreach (var (path, data) in parts) { using var entry = archive.CreateEntry(path, CompressionLevel.Optimal).Open(); entry.Write(data); } } return buffer.ToArray();
    }
}
