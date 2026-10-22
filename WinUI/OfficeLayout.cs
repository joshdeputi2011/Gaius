using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Gaius.Office;

internal static partial class OfficeInterchange
{
    private static XElement ExportLayout(OfficeFile file)
    {
        var layout = file.Data["layout"]; bool landscape = layout?["orientation"]?.ToString() == "landscape";
        (int width, int height) = layout?["pageSize"]?.ToString() switch { "letter" => (12240, 15840), "legal" => (12240, 20160), _ => (11906, 16838) };
        double preset = layout?["margins"]?.ToString() switch { "narrow" => 720, "wide" => 2160, _ => 1440 };
        var size = E(W, "pgSz", new XAttribute(W + "w", landscape ? height : width), new XAttribute(W + "h", landscape ? width : height)); if (landscape) size.SetAttributeValue(W + "orient", "landscape");
        var margins = E(W, "pgMar"); foreach (string side in new[] { "top", "right", "bottom", "left" }) margins.SetAttributeValue(W + side, layout?["customMargins"]?[side] is JsonNode mm ? Math.Round(SlideRenderer.Number(mm) * 1440 / 25.4) : preset);
        var section = E(W, "sectPr", size, margins);
        if (SlideRenderer.Number(layout?["borderWidth"]) > 0) section.Add(E(W, "pgBorders", new XAttribute(W + "offsetFrom", "page"), new[] { "top", "right", "bottom", "left" }.Select(side => E(W, side, new XAttribute(W + "val", "single"), new XAttribute(W + "sz", Math.Round(SlideRenderer.Number(layout?["borderWidth"]) * 8)), new XAttribute(W + "color", (layout?["borderColor"]?.ToString() ?? "#64748b").TrimStart('#'))))));
        section.Add(E(W, "cols", new XAttribute(W + "num", Math.Clamp((int)SlideRenderer.Number(layout?["columns"], 1), 1, 4)), new XAttribute(W + "space", Math.Round(SlideRenderer.Number(layout?["columnGap"], 10) * 1440 / 25.4)))); return section;
    }
    private static void ImportLayout(XDocument document, OfficeFile file)
    {
        var size = document.Descendants(W + "pgSz").FirstOrDefault(); if (size is null) return;
        var margins = document.Descendants(W + "pgMar").FirstOrDefault(); var columns = document.Descendants(W + "cols").FirstOrDefault(); var custom = new JsonObject();
        foreach (string side in new[] { "top", "right", "bottom", "left" }) custom[side] = Number(margins?.Attribute(W + side)?.Value, 1440) * 25.4 / 1440;
        double width = Number(size.Attribute(W + "w")?.Value, 11906), height = Number(size.Attribute(W + "h")?.Value, 16838);
        file.Data["layout"] = new JsonObject { ["orientation"] = width > height ? "landscape" : "portrait", ["pageSize"] = width == 20160 || height == 20160 ? "legal" : width == 12240 || height == 12240 ? "letter" : "a4", ["customMargins"] = custom, ["columns"] = Math.Clamp((int)Number(columns?.Attribute(W + "num")?.Value, 1), 1, 4), ["columnGap"] = Number(columns?.Attribute(W + "space")?.Value, 567) * 25.4 / 1440 };
    }
}
