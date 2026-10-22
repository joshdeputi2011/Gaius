using Gaius.Office;
using System.Text.Json.Nodes;

internal static class HomeTemplateTests
{
    public static void Run(Action<bool, string> check)
    {
        foreach (string kind in new[] { "document", "spreadsheet", "presentation" }) {
            var templates = OfficeTemplates.ForKind(kind);
            check(templates.Select(t => t.Id).Distinct().Count() == templates.Count && templates.Count >= 4, kind + " template gallery is incomplete or duplicated");
            foreach (var template in templates) {
                var file = OfficeTemplates.Create(kind, template.Id); var second = OfficeTemplates.Create(kind, template.Id);
                check(file.Id != second.Id && file.Kind == kind, template.Name + " reuses a document identity");
                var parsed = OfficeFile.Parse(file.Data.ToJsonString());
                var restored = OfficeInterchange.Import(OfficeInterchange.Export(parsed), OfficeInterchange.Extension(kind));
                check(restored.Kind == kind, template.Name + " cannot be exported and reopened");
                if (template.Id == "blank") continue;
                if (kind == "document") check(restored.Content["text"]!.ToString().Contains(template.Id == "letter" ? "Your name" : template.Name), template.Name + " loses its starter text");
                else if (kind == "spreadsheet") check(restored.Sheet["styles"]?["A1"]?["background"]?.ToString() == "#107c41", template.Name + " loses its header style");
                else check(restored.Slides.Count == 4 && restored.Slides[0]?["objects"] is JsonArray { Count: > 0 }, template.Name + " loses its slides or accent design");
            }
        }
        var budget = OfficeTemplates.Create("spreadsheet", "budget");
        check(budget.Sheet["cells"]?["B7"]?.ToString() == "=SUM(B3:B6)" && budget.Sheet["cells"]?["C8"]?.ToString() == "=C2-C7", "Budget template totals are missing");
        var independent = OfficeTemplates.Create("document", "meeting"); var other = OfficeTemplates.Create("document", "meeting"); independent.Content["text"] = "Changed";
        check(other.Content["text"]!.ToString().Contains("Meeting notes"), "Template instances share mutable content");
    }
}
