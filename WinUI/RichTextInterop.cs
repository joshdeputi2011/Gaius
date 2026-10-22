using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;

namespace Gaius.Office;

public static class RichTextInterop
{
    static RichTextInterop() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    public static void Load(RichEditBox editor, OfficeFile file)
    {
        var rtf = file.Content["rtf"]?.GetValue<string>();
        if (rtf is not null) editor.Document.SetText(TextSetOptions.FormatRtf, rtf);
        else {
            var delta = (JsonArray)(file.Content["delta"] as JsonArray ?? HtmlToDelta(file.Content["html"]!.GetValue<string>())).DeepClone();
            file.Content["objects"] ??= new JsonArray(); var objects = (JsonArray)file.Content["objects"]!;
            foreach (var op in delta.ToArray()) if (op?["insert"] is JsonObject custom && custom["gaius-drawing"] is JsonValue encoded) {
                var drawing = JsonNode.Parse(encoded.ToString()) as JsonObject; if (drawing is null) continue;
                drawing["id"] = Guid.NewGuid().ToString(); drawing["x"] = 10; drawing["y"] = 10 + objects.Count * 5;
                drawing["width"] = Math.Clamp(SlideRenderer.Number(drawing["width"], 260) / 7.94, 2, 90); drawing["height"] = Math.Clamp(SlideRenderer.Number(drawing["height"], 150) / 10.5, 2, 90); drawing["wrap"] = "front";
                objects.Add(drawing); delta.Remove(op);
            }
            var pictures = (file.Content["objects"] as JsonArray ?? []).Select(o => o?["dataUrl"]?.ToString()).ToHashSet();
            foreach (var op in delta.ToArray()) if (op?["insert"] is JsonObject embed && pictures.Contains(embed["image"]?.ToString())) delta.Remove(op);
            editor.Document.SetText(TextSetOptions.FormatRtf, ToRtf(delta));
        }
    }
    public static void Save(RichEditBox editor, OfficeFile file)
    {
        editor.Document.GetText(TextGetOptions.FormatRtf, out var rtf);
        editor.Document.GetText(TextGetOptions.None, out var text);
        var html = RtfPipe.Rtf.ToHtml(rtf);
        foreach (var obj in file.Content["objects"] as JsonArray ?? []) {
            if (obj?["dataUrl"]?.ToString() is not string data) continue;
            string wrap = obj["wrap"]?.ToString() ?? "front", floating = wrap == "square-right" ? "right" : wrap == "square-left" ? "left" : "none";
            string metadata = obj["kind"]?.ToString() == "image" ? "" : $" data-shape=\"{WebUtility.HtmlEncode(obj["kind"]!.ToString())}\" data-fill=\"{WebUtility.HtmlEncode(obj["color"]?.ToString())}\" data-stroke=\"{WebUtility.HtmlEncode(obj["stroke"]?.ToString())}\" data-stroke-width=\"{SlideRenderer.Number(obj["strokeWidth"])}\"";
            html += $"<p><img src=\"{WebUtility.HtmlEncode(data)}\" alt=\"{WebUtility.HtmlEncode(obj["alt"]?.ToString() ?? "Drawing")}\"{metadata} data-wrap=\"{WebUtility.HtmlEncode(wrap)}\" style=\"display:block;float:{floating};object-fit:contain;max-width:100%;width:{SlideRenderer.Number(obj["width"], 35) * 7.94}px;height:{SlideRenderer.Number(obj["height"], 20) * 10.5}px;margin:12px\"></p>";
        }
        file.Content["rtf"] = rtf; file.Content["text"] = text.TrimEnd('\r'); file.Content["html"] = html; file.Content["delta"] = HtmlToDelta(html);
    }
    public static string Escape(string text)
    {
        var output = new StringBuilder();
        foreach (char c in text) output.Append(c switch { '\\' => "\\\\", '{' => "\\{", '}' => "\\}", '\n' => "\\par\n", '\r' => "", '\t' => "\\tab ", >= ' ' and <= '~' => c.ToString(), _ => "\\u" + unchecked((short)c) + "?" });
        return output.ToString();
    }
    public static string TableRtf(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (rows.Count == 0) return "";
        int columns = Math.Max(1, rows.Max(row => row.Count)); var rtf = new StringBuilder(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Calibri;}}\f0\fs24 ");
        foreach (var row in rows) {
            rtf.Append(@"\trowd\trgaph100\trleft0");
            for (int c = 0; c < columns; c++) rtf.Append(@"\clbrdrt\brdrs\brdrw10\clbrdrl\brdrs\brdrw10\clbrdrb\brdrs\brdrw10\clbrdrr\brdrs\brdrw10\cellx").Append((c + 1) * 9000 / columns);
            rtf.Append(' ');
            for (int c = 0; c < columns; c++) rtf.Append(@"\pard\intbl ").Append(Escape(c < row.Count ? row[c] : "")).Append(@"\cell ");
            rtf.Append(@"\row ");
        }
        return rtf.Append(@"\pard\par }").ToString();
    }
    public static string ToRtf(JsonArray delta)
    {
        delta = ImageOperations(delta);
        var colors = new List<string> { "#000000" };
        foreach (var op in delta) foreach (var key in new[] { "color", "background" }) { var color = op?["attributes"]?[key]?.ToString(); if (color is not null && Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$") && !colors.Contains(color)) colors.Add(color); }
        var fonts = new List<string> { "Calibri" };
        string Family(JsonNode? attributes) => (attributes?["font"]?.ToString() ?? "Calibri").Split(',')[0].Trim(' ', '\'', '"').Replace(";", "");
        foreach (var op in delta) { string family = Family(op?["attributes"]); if (family.Length > 0 && !fonts.Contains(family) && fonts.Count < 256) fonts.Add(family); }
        var result = new StringBuilder(@"{\rtf1\ansi\deff0\uc1{\fonttbl");
        for (int i = 0; i < fonts.Count; i++) result.Append("{\\f").Append(i).Append(' ').Append(Escape(fonts[i])).Append(";}");
        result.Append(@"}{\colortbl ;");
        foreach (var color in colors) result.Append("\\red").Append(Convert.ToInt32(color[1..3], 16)).Append("\\green").Append(Convert.ToInt32(color[3..5], 16)).Append("\\blue").Append(Convert.ToInt32(color[5..7], 16)).Append(';');
        result.Append(@"}\f0\fs24 ");
        foreach (var op in delta) {
            if (op is null) continue;
            if (op["insert"] is JsonObject embed) {
                if (embed["image"] is JsonValue image) {
                    var src = image.GetValue<string>(); if (!Regex.IsMatch(src, "^data:image/(png|jpe?g);base64,", RegexOptions.IgnoreCase)) continue;
                    var bytes = Convert.FromBase64String(src[(src.IndexOf(',') + 1)..]); bool png = src.StartsWith("data:image/png", StringComparison.OrdinalIgnoreCase);
                    var dimensions = ImageDimensions(bytes); int width = Math.Min(500, dimensions.Width), height = (int)(width * (double)dimensions.Height / Math.Max(1, dimensions.Width));
                    result.Append(png ? @"{\pict\pngblip" : @"{\pict\jpegblip").Append("\\picw").Append(dimensions.Width).Append("\\pich").Append(dimensions.Height).Append("\\picwgoal").Append(width * 15).Append("\\pichgoal").Append(height * 15).Append(' ').Append(Convert.ToHexString(bytes)).Append('}');
                } else if (embed["gaius-table"] is JsonValue table) {
                    var rows = JsonNode.Parse(table.GetValue<string>())!.AsArray().Select(row => (IReadOnlyList<string>)row!.AsArray().Select(cell => cell?.ToString() ?? "").ToArray()).ToArray(); var tableRtf = TableRtf(rows); result.Append(tableRtf[tableRtf.IndexOf(@"\trowd", StringComparison.Ordinal)..^1]);
                }
                continue;
            }
            string text = op["insert"]?.GetValue<string>() ?? ""; var attributes = op["attributes"] as JsonObject;
            result.Append('{');
            result.Append("\\f").Append(Math.Max(0, fonts.IndexOf(Family(attributes)))).Append(' ');
            foreach (var pair in new[] { ("bold", "\\b "), ("italic", "\\i "), ("underline", "\\ul "), ("strike", "\\strike ") }) if (attributes?[pair.Item1]?.ToString() == "true") result.Append(pair.Item2);
            foreach (var pair in new[] { ("color", "\\cf"), ("background", "\\highlight") }) { var index = colors.IndexOf(attributes?[pair.Item1]?.ToString() ?? ""); if (index >= 0) result.Append(pair.Item2).Append(index + 1).Append(' '); }
            if (double.TryParse(attributes?["size"]?.ToString(), out var size)) result.Append("\\fs").Append((int)Math.Clamp(size * 1.5, 12, 300)).Append(' ');
            if (attributes?["align"]?.ToString() is string align) result.Append(align switch { "center" => "\\qc ", "right" => "\\qr ", "justify" => "\\qj ", _ => "\\ql " });
            result.Append(Escape(text)).Append('}');
        }
        return result.Append('}').ToString();
    }
    public static JsonArray ImageOperations(JsonArray source)
    {
        var result = (JsonArray)source.DeepClone();
        foreach (var op in result) if (op?["insert"] is JsonObject embed && embed["gaius-drawing"] is JsonValue encoded) {
            var drawing = JsonNode.Parse(encoded.ToString());
            if (drawing?["dataUrl"] is JsonValue data) op!["insert"] = new JsonObject { ["image"] = data.ToString() };
        }
        return result;
    }
    public static (int Width, int Height) ImageDimensions(byte[] bytes)
    {
        if (bytes.Length >= 26 && bytes[0] == 'B' && bytes[1] == 'M') { int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18, 4)), height = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22, 4)); if (width > 0 && height != 0 && height != int.MinValue) return (width, Math.Abs(height)); }
        if (bytes.Length > 24 && bytes[0] == 137) return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
        for (int p = 2; p + 8 < bytes.Length;) { if (bytes[p] != 255) break; int marker = bytes[p + 1], length = bytes[p + 2] * 256 + bytes[p + 3]; if (marker is 0xc0 or 0xc1 or 0xc2) return (bytes[p + 7] * 256 + bytes[p + 8], bytes[p + 5] * 256 + bytes[p + 6]); if (length < 2) break; p += length + 2; }
        return (500, 300);
    }
    public static JsonArray HtmlToDelta(string html)
    {
        var doc = new HtmlDocument(); doc.LoadHtml(html); var delta = new JsonArray();
        void Add(string text, JsonObject attributes) { if (text.Length == 0) return; var op = new JsonObject { ["insert"] = text }; if (attributes.Count > 0) op["attributes"] = attributes.DeepClone(); delta.Add(op); }
        void Visit(HtmlNode node, JsonObject inherited)
        {
            if (node.NodeType == HtmlNodeType.Text) { Add(WebUtility.HtmlDecode(node.InnerText), inherited); return; }
            if (node.Name is "script" or "style") return;
            if (node.Name == "table") {
                var rows = node.SelectNodes(".//tr")?.Select(row => row.SelectNodes("./th|./td")?.Select(cell => WebUtility.HtmlDecode(cell.InnerText)).ToArray() ?? []).ToArray() ?? [];
                delta.Add(new JsonObject { ["insert"] = new JsonObject { ["gaius-table"] = System.Text.Json.JsonSerializer.Serialize(rows) } }); Add("\n", new()); return;
            }
            if (node.Name == "img") { var src = node.GetAttributeValue("src", ""); if (Regex.IsMatch(src, "^data:image/(png|jpe?g|webp);base64,", RegexOptions.IgnoreCase)) delta.Add(new JsonObject { ["insert"] = new JsonObject { ["image"] = src } }); return; }
            if (node.Name == "br") { Add("\n", new()); return; }
            var attributes = (JsonObject)inherited.DeepClone();
            if (Regex.IsMatch(node.Name, "^h[1-6]$")) { attributes["bold"] = true; attributes["size"] = new[] { 32, 26, 22, 20, 18, 16 }[int.Parse(node.Name[1..]) - 1].ToString(System.Globalization.CultureInfo.InvariantCulture); }
            if (node.Name is "strong" or "b") attributes["bold"] = true;
            if (node.Name is "em" or "i") attributes["italic"] = true;
            if (node.Name == "u") attributes["underline"] = true;
            if (node.Name is "s" or "strike") attributes["strike"] = true;
            if (node.Name == "a") { var url = node.GetAttributeValue("href", ""); if (Regex.IsMatch(url, "^(https?://|mailto:)", RegexOptions.IgnoreCase)) attributes["link"] = url; }
            foreach (var style in node.GetAttributeValue("style", "").Split(';')) {
                var pair = style.Split(':', 2); if (pair.Length != 2) continue; var value = pair[1].Trim();
                switch (pair[0].Trim()) {
                    case "font-weight": if (value is "bold" or "700" or "600") attributes["bold"] = true; break;
                    case "font-style": if (value == "italic") attributes["italic"] = true; break;
                    case "text-decoration": if (value.Contains("underline")) attributes["underline"] = true; if (value.Contains("line-through")) attributes["strike"] = true; break;
                    case "color": case "background-color": if (ColorHex(value) is string hex) attributes[pair[0].Trim() == "color" ? "color" : "background"] = hex; break;
                    case "font-size": if (double.TryParse(Regex.Match(value, @"[\d.]+").Value, out var size)) attributes["size"] = (size * (value.EndsWith("pt") ? 4.0 / 3 : 1)).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture); break;
                    case "font-family": attributes["font"] = value.Trim('"', '\''); break;
                }
            }
            foreach (var child in node.ChildNodes) Visit(child, attributes);
            if (node.Name is "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "li" || node.Name == "div" && !node.ChildNodes.Any(c => c.Name is "p" or "div" or "table")) {
                var block = new JsonObject(); if (Regex.IsMatch(node.Name, "^h[1-6]$")) block["header"] = int.Parse(node.Name[1..]);
                var align = Regex.Match(node.GetAttributeValue("style", ""), @"text-align:\s*(center|right|justify)"); if (align.Success) block["align"] = align.Groups[1].Value;
                if (node.Name == "li") block["list"] = node.ParentNode?.Name == "ol" ? "ordered" : "bullet"; Add("\n", block);
            }
        }
        Visit(doc.DocumentNode, new());
        if (delta.Count == 0 || delta.Last()?["insert"] is not JsonValue value || !value.TryGetValue<string>(out var last) || !last.EndsWith('\n')) Add("\n", new());
        return delta;
    }
    public static string? ColorHex(string color)
    {
        if (Regex.IsMatch(color, "^#[a-fA-F0-9]{6}$")) return color;
        if (Regex.IsMatch(color, "^#[a-fA-F0-9]{3}$")) return "#" + string.Concat(color[1..].Select(c => new string(c, 2)));
        var rgb = Regex.Match(color, @"rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)");
        if (rgb.Success) return "#" + string.Concat(Enumerable.Range(1, 3).Select(i => Math.Clamp(int.Parse(rgb.Groups[i].Value), 0, 255).ToString("x2")));
        return color switch { "black" => "#000000", "white" => "#ffffff", "red" => "#ff0000", "blue" => "#0000ff", "green" => "#008000", "yellow" => "#ffff00", _ => null };
    }
}
