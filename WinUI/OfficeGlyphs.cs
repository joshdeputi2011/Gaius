namespace Gaius.Office;

internal static class OfficeGlyphs
{
    public static string ForLabel(string label) => label.ToLowerInvariant() switch {
        "table" => "\uE80A", "wordart" or "text" or "text box" or "body text" => "\uE8D2",
        "rectangle" => "\uE91B", "ellipse" => "\uEA3A", "triangle" => "\uE8F8",
        "rulers" or "margins" or "page width" => "\uE9A6",
        "thumbnails / navigation" => "\uE8F1", "reading view" or "column preview" => "\uE736",
        "gridlines" or "columns" => "\uE80A", "print layout" or "page border" => "\uE8A7",
        "web layout" => "\uE774", "format object" or "shape gallery" => "\uE771",
        "link" => "\uE71B", "date" or "today's date" => "\uE787", "heading" => "\uE8D2",
        "left" or "align left" => "\uE8E4", "center" => "\uE8E3", "right" or "align right" => "\uE8E2", "justify" => "\uE8E1",
        "chart" or "charts" => "\uE9D2", "add rows" or "add columns" => "\uE710",
        "sort ascending" => "\uE8CB", "sort descending" => "\uE8CC", "filter column" or "clear filter" => "\uE71C",
        "calculate" or "statistics" => "\uE8EF", "orientation" => "\uE8A7", "word count" => "\uE8EF",
        "pen" or "highlighter" or "replace word" or "add text" => "\uE70F", "clear" or "eraser" => "\uE75C",
        "timing" => "\uE823", "speaker notes" or "insert note" => "\uE70B",
        "rename sheet" => "\uE8AC", "target size" or "compact" => "\uE8AC",
        "crop" => "\uE7A8", "resize" => "\uE740", "flip horizontal" or "flip vertical" => "\uE8AB",
        "caption" or "watermark" or "page numbers" => "\uE8D2",
        "fade" or "push" or "zoom" or "morph" or "fly" => "\uE768",
        "none" or "reset adjustments" => "\uE777", "page to png" or "image to pdf" => "\uE8B9",
        "save png" or "save jpeg" or "extract text" => "\uE74E",
        "reverse pages" or "move slide left" or "move slide right" => "\uE8AB",
        _ => "\uE8A5"
    };
}
