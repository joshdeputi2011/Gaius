using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Gaius.Office;

internal static partial class OfficeInterchange
{
    private static byte[] ExportPptx(OfficeFile file)
    {
        var parts = new Dictionary<string, byte[]>(); var types = new List<(string, string)> { ("ppt/presentation.xml", "presentationml.presentation.main") }; var slideIds = new List<XElement>(); var slideRels = new List<(string, string, string)>();
        const long slideWidth = 12192000, slideHeight = 6858000;
        XElement Transform(double x, double y, double width, double height) => E(A, "xfrm", E(A, "off", new XAttribute("x", (long)(x * slideWidth / 100)), new XAttribute("y", (long)(y * slideHeight / 100))), E(A, "ext", new XAttribute("cx", (long)(width * slideWidth / 100)), new XAttribute("cy", (long)(height * slideHeight / 100))));
        XElement TextShape(int id, string name, string text, string color, double size, bool bold, XElement transform) => E(P, "sp", E(P, "nvSpPr", E(P, "cNvPr", new XAttribute("id", id), new XAttribute("name", name)), E(P, "cNvSpPr", new XAttribute("txBox", 1)), E(P, "nvPr")), E(P, "spPr", transform, E(A, "prstGeom", new XAttribute("prst", "rect"), E(A, "avLst")), E(A, "noFill")), E(P, "txBody", E(A, "bodyPr", new XAttribute("wrap", "square")), E(A, "lstStyle"), text.Split('\n').Select(line => E(A, "p", E(A, "r", E(A, "rPr", new XAttribute("lang", "en-US"), new XAttribute("sz", (int)(size * 100)), new XAttribute("b", bold ? 1 : 0), E(A, "solidFill", E(A, "srgbClr", new XAttribute("val", color.TrimStart('#')))), E(A, "latin", new XAttribute("typeface", "Arial"))), E(A, "t", line))))));
        for (int i = 0; i < file.Slides.Count; i++) {
            var slide = file.Slides[i]!; var relationships = new List<(string, string, string)>();
            var tree = E(P, "spTree", E(P, "nvGrpSpPr", E(P, "cNvPr", new XAttribute("id", 1), new XAttribute("name", "")), E(P, "cNvGrpSpPr"), E(P, "nvPr")), E(P, "grpSpPr", E(A, "xfrm", E(A, "off", new XAttribute("x", 0), new XAttribute("y", 0)), E(A, "ext", new XAttribute("cx", 0), new XAttribute("cy", 0)), E(A, "chOff", new XAttribute("x", 0), new XAttribute("y", 0)), E(A, "chExt", new XAttribute("cx", 0), new XAttribute("cy", 0)))));
            tree.Add(TextShape(2, "Gaius-title", slide["title"]?.ToString() ?? "", "#24334a", 40, true, Transform(6, 8, 88, 17))); tree.Add(TextShape(3, "Gaius-body", slide["body"]?.ToString() ?? "", "#586b83", 24, false, Transform(6, 30, 88, 58)));
            int objectIndex = 3;
            foreach (var obj in slide["objects"] as JsonArray ?? []) {
                if (obj is null) continue; int id = ++objectIndex; string kind = obj["kind"]?.ToString() ?? "text", color = obj["color"]?.ToString() ?? "#185abd"; var transform = Transform(Number(obj["x"]?.ToString(), 10), Number(obj["y"]?.ToString(), 40), Number(obj["width"]?.ToString(), 30), Number(obj["height"]?.ToString(), 20));
                transform.SetAttributeValue("rot", Math.Round(Number(obj["rotation"]?.ToString()) * 60000));
                if (kind == "image" && ImageData(obj["dataUrl"]?.ToString() ?? "") is { } image) { string imageName = $"image-{i + 1}-{id}.{image.Extension}", relation = "image" + id; parts["ppt/media/" + imageName] = image.Bytes; relationships.Add((relation, "image", "../media/" + imageName)); tree.Add(E(P, "pic", E(P, "nvPicPr", E(P, "cNvPr", new XAttribute("id", id), new XAttribute("name", "Gaius-image-" + id)), E(P, "cNvPicPr"), E(P, "nvPr")), E(P, "blipFill", E(A, "blip", new XAttribute(R + "embed", relation)), E(A, "stretch", E(A, "fillRect"))), E(P, "spPr", transform, E(A, "prstGeom", new XAttribute("prst", "rect"), E(A, "avLst"))))); }
                else if (kind is "text" or "wordart") tree.Add(TextShape(id, "Gaius-" + kind + "-" + id, obj["text"]?.ToString() ?? "", color, Number(obj["fontSize"]?.ToString(), 28), kind == "wordart", transform));
                else if (OfficeShapes.Paths.ContainsKey(kind)) tree.Add(E(P, "sp", E(P, "nvSpPr", E(P, "cNvPr", new XAttribute("id", id), new XAttribute("name", "Gaius-" + kind + "-" + id)), E(P, "cNvSpPr"), E(P, "nvPr")), E(P, "spPr", transform, E(A, "prstGeom", new XAttribute("prst", OfficeShapes.Presets.GetValueOrDefault(kind, kind)), E(A, "avLst")), E(A, "solidFill", E(A, "srgbClr", new XAttribute("val", color.TrimStart('#')))))));
            }
            foreach (var shape in tree.Elements().Where(shape => shape.Name == P + "sp" || shape.Name == P + "pic")) {
                if (!int.TryParse(shape.Descendants(P + "cNvPr").FirstOrDefault()?.Attribute("id")?.Value, out int shapeId) || shapeId < 4) continue;
                var obj = (slide["objects"] as JsonArray)?[shapeId - 4]; if (obj is null) continue;
                var line = E(A, "ln", new XAttribute("w", Math.Round(Number(obj["strokeWidth"]?.ToString()) * 9525)), Number(obj["strokeWidth"]?.ToString()) > 0 ? E(A, "solidFill", E(A, "srgbClr", new XAttribute("val", (obj["stroke"]?.ToString() ?? "#185abd").TrimStart('#')))) : E(A, "noFill")); shape.Element(P + "spPr")?.Add(line);
            }
            var root = E(P, "sld", new XAttribute(XNamespace.Xmlns + "p", P), new XAttribute(XNamespace.Xmlns + "a", A), new XAttribute(XNamespace.Xmlns + "r", R), E(P, "cSld", E(P, "bg", E(P, "bgPr", E(A, "solidFill", E(A, "srgbClr", new XAttribute("val", (slide["background"]?.ToString() ?? "#ffffff").TrimStart('#')))), E(A, "effectLst"))), tree), E(P, "clrMapOvr", E(A, "masterClrMapping")));
            string? transition = slide["transition"]?.ToString(); if (transition is "fade" or "push" or "zoom") root.Add(E(P, "transition", transition == "push" ? E(P, "push", new XAttribute("dir", "l")) : transition == "zoom" ? E(P, "zoom", new XAttribute("dir", "in")) : E(P, "fade")));
            Put(parts, $"ppt/slides/slide{i + 1}.xml", root); types.Add(($"ppt/slides/slide{i + 1}.xml", "presentationml.slide")); slideIds.Add(E(P, "sldId", new XAttribute("id", 256 + i), new XAttribute(R + "id", "slide" + (i + 1)))); slideRels.Add(("slide" + (i + 1), "slide", $"slides/slide{i + 1}.xml"));
            if (!string.IsNullOrEmpty(slide["notes"]?.ToString())) { var notesTree = E(P, "spTree", E(P, "nvGrpSpPr", E(P, "cNvPr", new XAttribute("id", 1), new XAttribute("name", "")), E(P, "cNvGrpSpPr"), E(P, "nvPr")), E(P, "grpSpPr"), TextShape(2, "Notes", slide["notes"]!.ToString(), "#242424", 16, false, Transform(5, 40, 50, 60))); notesTree.Elements(P + "sp").First().Element(P + "nvSpPr")!.Element(P + "nvPr")!.Add(E(P, "ph", new XAttribute("type", "body"), new XAttribute("idx", 1))); Put(parts, $"ppt/notesSlides/notesSlide{i + 1}.xml", E(P, "notes", E(P, "cSld", notesTree))); Put(parts, $"ppt/notesSlides/_rels/notesSlide{i + 1}.xml.rels", Relationships(new[] { ("slide", "slide", $"../slides/slide{i + 1}.xml") })); relationships.Add(("notes", "notesSlide", $"../notesSlides/notesSlide{i + 1}.xml")); types.Add(($"ppt/notesSlides/notesSlide{i + 1}.xml", "presentationml.notesSlide")); }
            if (relationships.Count > 0) Put(parts, $"ppt/slides/_rels/slide{i + 1}.xml.rels", Relationships(relationships));
        }
        Put(parts, "ppt/presentation.xml", E(P, "presentation", new XAttribute(XNamespace.Xmlns + "r", R), E(P, "sldIdLst", slideIds), E(P, "sldSz", new XAttribute("cx", slideWidth), new XAttribute("cy", slideHeight)), E(P, "notesSz", new XAttribute("cx", slideHeight), new XAttribute("cy", slideWidth)))); Put(parts, "ppt/_rels/presentation.xml.rels", Relationships(slideRels)); Put(parts, "_rels/.rels", Relationships(new[] { ("presentation", "officeDocument", "ppt/presentation.xml") })); ContentTypes(parts, types); return Zip(parts);
    }
    private static OfficeFile ImportPptx(Dictionary<string, byte[]> parts)
    {
        var presentation = Read(parts, "ppt/presentation.xml"); var targets = Targets(parts, "ppt/presentation.xml"); var size = presentation.Root?.Element(P + "sldSz"); double width = Number(size?.Attribute("cx")?.Value, 12192000), height = Number(size?.Attribute("cy")?.Value, 6858000); var file = OfficeFile.Create("presentation"); file.Slides.Clear();
        foreach (var item in presentation.Descendants(P + "sldId")) {
            if (!targets.TryGetValue(item.Attribute(R + "id")?.Value ?? "", out var source)) continue; var document = Read(parts, source); var relationships = Targets(parts, source); var objects = new JsonArray(); string title = "", body = "";
            var background = document.Descendants(P + "bg").FirstOrDefault()?.Descendants(A + "srgbClr").FirstOrDefault()?.Attribute("val")?.Value ?? "ffffff";
            foreach (var shape in document.Descendants(P + "spTree").FirstOrDefault()?.Elements() ?? []) {
                if (shape.Name != P + "sp" && shape.Name != P + "pic") continue; var name = shape.Descendants(P + "cNvPr").FirstOrDefault()?.Attribute("name")?.Value ?? ""; var text = string.Join("\n", shape.Descendants(A + "p").Select(p => string.Concat(p.Descendants(A + "t").Select(t => t.Value))));
                if (name == "Gaius-title") { title = text; continue; } if (name == "Gaius-body") { body = text; continue; }
                var transform = shape.Element(P + "spPr")?.Element(A + "xfrm"); var offset = transform?.Element(A + "off"); var extent = transform?.Element(A + "ext"); var color = shape.Element(P + "spPr")?.Element(A + "solidFill")?.Element(A + "srgbClr")?.Attribute("val")?.Value ?? shape.Descendants(A + "rPr").FirstOrDefault()?.Descendants(A + "srgbClr").FirstOrDefault()?.Attribute("val")?.Value ?? "185abd";
                string geometry = shape.Descendants(A + "prstGeom").FirstOrDefault()?.Attribute("prst")?.Value ?? "rect", kind = shape.Name == P + "pic" ? "image" : text.Length > 0 ? name.Contains("wordart") ? "wordart" : "text" : OfficeShapes.Presets.FirstOrDefault(pair => pair.Value == geometry).Key ?? (OfficeShapes.Paths.ContainsKey(geometry) ? geometry : "rectangle");
                var obj = new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["kind"] = kind, ["text"] = text, ["color"] = "#" + color, ["x"] = Math.Clamp(Number(offset?.Attribute("x")?.Value) / Math.Max(1, width) * 100, 0, 100), ["y"] = Math.Clamp(Number(offset?.Attribute("y")?.Value) / Math.Max(1, height) * 100, 0, 100), ["width"] = Math.Clamp(Number(extent?.Attribute("cx")?.Value, width * .3) / Math.Max(1, width) * 100, 1, 100), ["height"] = Math.Clamp(Number(extent?.Attribute("cy")?.Value, height * .2) / Math.Max(1, height) * 100, 1, 100), ["fontSize"] = Number(shape.Descendants(A + "rPr").FirstOrDefault()?.Attribute("sz")?.Value, 2800) / 100 };
                obj["rotation"] = Number(transform?.Attribute("rot")?.Value) / 60000;
                var outline = shape.Element(P + "spPr")?.Element(A + "ln"); obj["strokeWidth"] = Number(outline?.Attribute("w")?.Value) / 9525; obj["stroke"] = "#" + (outline?.Descendants(A + "srgbClr").FirstOrDefault()?.Attribute("val")?.Value ?? "185abd");
                if (kind == "image") { var id = shape.Descendants(A + "blip").FirstOrDefault()?.Attribute(R + "embed")?.Value; if (id is null || !relationships.TryGetValue(id, out var path) || ImageUrl(parts, path) is not string url) continue; obj["dataUrl"] = url; }
                objects.Add(obj);
            }
            string transition = document.Root?.Element(P + "transition")?.Elements().FirstOrDefault()?.Name.LocalName ?? "none"; var slide = new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["title"] = title, ["body"] = body, ["background"] = "#" + background, ["objects"] = objects, ["transition"] = transition is "fade" or "push" or "zoom" ? transition : "none" };
            var notes = relationships.Values.FirstOrDefault(path => path.Contains("notesSlides/")); if (notes is not null && parts.ContainsKey(notes)) slide["notes"] = string.Join("\n", Read(parts, notes).Descendants(A + "p").Select(p => string.Concat(p.Descendants(A + "t").Select(t => t.Value)))); file.Slides.Add(slide);
        }
        if (file.Slides.Count == 0) throw new FormatException("This presentation has no slides."); return file;
    }
}
