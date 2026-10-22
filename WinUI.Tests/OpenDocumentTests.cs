using Gaius.Office;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

internal static class OpenDocumentTests
{
    public static void Run(Action<bool, string> check)
    {
        var file = OfficeFile.Create("document");
        file.Content["delta"] = new JsonArray(new JsonObject { ["insert"] = "ODT नमस्ते Ω", ["attributes"] = new JsonObject { ["bold"] = true, ["color"] = "#c43e1c", ["background"] = "#fff1ba" } }, new JsonObject { ["insert"] = "\n", ["attributes"] = new JsonObject { ["header"] = 1 } }, new JsonObject { ["insert"] = new JsonObject { ["gaius-table"] = "[[\"Item\",\"Value\"],[\"Budget\",\"120\"]]" } });
        var bytes = OfficeInterchange.ExportOdt(file);
        using (var archive = new ZipArchive(new MemoryStream(bytes))) {
            check(archive.Entries[0].FullName == "mimetype" && archive.Entries[0].CompressedLength == archive.Entries[0].Length, "ODT mimetype must be first and uncompressed");
            check(archive.GetEntry("META-INF/manifest.xml") is not null, "ODT package manifest missing");
        }
        var reopened = OfficeInterchange.Import(bytes, ".odt");
        check(reopened.Content["text"]!.ToString().Contains("ODT नमस्ते Ω"), "ODT Unicode text lost");
        check(reopened.Content["html"]!.ToString().Contains("<table") && reopened.Content["html"]!.ToString().Contains("Budget"), "ODT table lost");
        check(reopened.Content["delta"]![0]?["attributes"]?["bold"]?.GetValue<bool>() == true && reopened.Content["delta"]![0]?["attributes"]?["color"]?.ToString() == "#c43e1c", "ODT formatting lost");
        check(OfficeInterchange.ExportOdt(reopened).SequenceEqual(bytes), "Unmodified ODT original package was altered");
        reopened.Content["delta"]!.AsArray().Add(new JsonObject { ["insert"] = "Edited\n" });
        var edited = OfficeInterchange.Import(OfficeInterchange.ExportOdt(reopened), ".odt");
        check(edited.Content["text"]!.ToString().Contains("Edited"), "ODT edit failed export");
        bool rejected = false; try { OfficeInterchange.Import(Encoding.UTF8.GetBytes("not a zip"), ".odt"); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "Invalid ODT accepted");
    }
}
