using HtmlAgilityPack;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Gaius.Office;

internal sealed class DocumentPreview
{
    private readonly HtmlDocument html = new();
    private readonly Dictionary<string, BitmapImage> pictures = new();
    public DocumentPreview(string content) => html.LoadHtml(content);
    public async Task LoadImages()
    {
        foreach (var node in html.DocumentNode.Descendants("img")) {
            var data = node.GetAttributeValue("src", ""); if (!data.StartsWith("data:image/") || pictures.ContainsKey(data)) continue;
            try { using var stream = new MemoryStream(Convert.FromBase64String(data[(data.IndexOf(',') + 1)..])); var image = new BitmapImage(); await image.SetSourceAsync(stream.AsRandomAccessStream()); pictures[data] = image; } catch (FormatException) { }
        }
    }
    public RichTextBlock Create(double width)
    {
        var block = new RichTextBlock { FontFamily = new FontFamily("Calibri"), FontSize = 16, Foreground = UI.DocumentBrush("#242424"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = false };
        void AddNode(HtmlNode node)
        {
            if (node.Name == "table") {
                var tableRows = node.SelectNodes(".//tr"); if (tableRows is null) return;
                foreach (var row in tableRows) {
                    var cells = row.SelectNodes("./td|./th"); if (cells is null) continue;
                    var grid = new Grid { Width = width }; foreach (var _ in cells) grid.ColumnDefinitions.Add(new ColumnDefinition());
                    for (int c = 0; c < cells.Count; c++) { var text = new RichTextBlock { FontSize = 14, Foreground = UI.DocumentBrush("#242424"), TextWrapping = TextWrapping.Wrap }; var p = new Paragraph(); foreach (var child in cells[c].ChildNodes) Inline(child, p.Inlines, width / cells.Count - 16); text.Blocks.Add(p); var border = new Border { BorderBrush = UI.DocumentBrush("#aab7c9"), BorderThickness = new Thickness(1), Padding = new Thickness(8), Child = text }; Grid.SetColumn(border, c); grid.Children.Add(border); }
                    var paragraph = new Paragraph { Margin = new Thickness(0) }; paragraph.Inlines.Add(new InlineUIContainer { Child = grid }); block.Blocks.Add(paragraph);
                }
            } else if (node.Name is "div" or "body" or "html" && node.ChildNodes.Any(n => n.Name is "p" or "div" or "table" or "ul" or "ol")) { foreach (var child in node.ChildNodes) AddNode(child); }
            else if (node.Name is "ul" or "ol") { int i = 0; foreach (var item in node.ChildNodes.Where(n => n.Name == "li")) { var paragraph = new Paragraph(); paragraph.Inlines.Add(new Run { Text = node.Name == "ol" ? $"{++i}. " : "• " }); foreach (var child in item.ChildNodes) Inline(child, paragraph.Inlines, width - 20); block.Blocks.Add(paragraph); } }
            else {
                if (node.NodeType == HtmlNodeType.Text && string.IsNullOrWhiteSpace(node.InnerText)) return;
                var p = new Paragraph { Margin = new Thickness(0, 0, 0, 8) }; var style = node.GetAttributeValue("style", "");
                p.TextAlignment = Regex.Match(style, @"text-align\s*:\s*(\w+)").Groups[1].Value switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, "justify" => TextAlignment.Justify, _ => TextAlignment.Left };
                Inline(node, p.Inlines, width); block.Blocks.Add(p);
            }
        }
        foreach (var node in html.DocumentNode.ChildNodes) AddNode(node);
        if (block.Blocks.Count == 0) block.Blocks.Add(new Paragraph()); return block;
    }
    private void Inline(HtmlNode node, InlineCollection target, double width)
    {
        if (node.NodeType == HtmlNodeType.Text) { target.Add(new Run { Text = HtmlEntity.DeEntitize(node.InnerText) }); return; }
        if (node.Name == "br") { target.Add(new LineBreak()); return; }
        if (node.Name == "img") { if (pictures.TryGetValue(node.GetAttributeValue("src", ""), out var source)) target.Add(new InlineUIContainer { Child = new Microsoft.UI.Xaml.Controls.Image { Source = source, Width = Math.Min(width, source.PixelWidth), MaxHeight = 500, Stretch = Stretch.Uniform } }); return; }
        var span = new Span(); string style = node.GetAttributeValue("style", "");
        if (node.Name is "b" or "strong" || Regex.IsMatch(style, @"font-weight\s*:\s*(bold|[6-9]00)")) span.FontWeight = FontWeights.Bold;
        if (node.Name is "i" or "em" || style.Contains("font-style:italic") || style.Contains("font-style: italic")) span.FontStyle = Windows.UI.Text.FontStyle.Italic;
        if (node.Name == "u" || style.Contains("underline")) span.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
        var color = Regex.Match(style, @"(?:^|;)\s*color\s*:\s*(#[a-fA-F0-9]{6})"); if (color.Success) span.Foreground = UI.DocumentBrush(color.Groups[1].Value);
        var size = Regex.Match(style, @"font-size\s*:\s*([0-9.]+)(pt|px)"); if (size.Success && double.TryParse(size.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double points)) span.FontSize = Math.Clamp(points * (size.Groups[2].Value == "pt" ? 4.0 / 3 : 1), 6, 200);
        if (Regex.IsMatch(node.Name, "^h[1-6]$")) { span.FontSize = 36 - int.Parse(node.Name[1..]) * 3; span.FontWeight = FontWeights.Bold; }
        foreach (var child in node.ChildNodes) Inline(child, span.Inlines, width); target.Add(span);
    }
}
