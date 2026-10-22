using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;
using System.Text;
namespace Gaius.Office;
public sealed class OfficeFile
{
    public JsonObject Data { get; }
    public string Id => Data["id"]!.GetValue<string>();
    public string Title { get => Data["title"]!.GetValue<string>(); set => Data["title"] = value; }
    public string Kind => Data["kind"]!.GetValue<string>();
    public string Revision => Data["updatedAt"]!.GetValue<string>();
    public JsonObject Content => (JsonObject)Data["content"]!;
    public JsonObject Sheet => Data["worksheets"] is JsonArray sheets && sheets.Count > 0 ? (JsonObject)sheets[Math.Clamp(Data["activeSheet"]?.GetValue<int>() ?? 0, 0, sheets.Count - 1)]! : (JsonObject)Data["sheet"]!;
    public JsonObject Image => (JsonObject)Data["image"]!;
    public JsonArray Slides => (JsonArray)Data["slides"]!;
    public OfficeFile(JsonObject data) { Data = data; }
    public static OfficeFile Create(string kind) => new(new JsonObject
    {
        ["schemaVersion"] = 1, ["id"] = Guid.NewGuid().ToString(), ["title"] = $"Untitled {kind}", ["kind"] = kind, ["updatedAt"] = DateTime.UtcNow.ToString("O"),
        ["content"] = new JsonObject { ["text"] = "", ["html"] = "<p><br></p>" },
        ["sheet"] = new JsonObject { ["cells"] = new JsonObject(), ["rows"] = 100, ["columns"] = 26 },
        ["slides"] = new JsonArray(new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["title"] = "Untitled presentation", ["body"] = "Add a subtitle", ["background"] = "#ffffff" }),
        ["image"] = new JsonObject { ["dataUrl"] = "", ["rotation"] = 0.0, ["brightness"] = 100.0, ["contrast"] = 100.0, ["grayscale"] = false },
        ["pdf"] = new JsonObject { ["dataUrl"] = "" }
    });
    public static OfficeFile Parse(string json)
    {
        var data = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("Invalid document.");
        if (data["schemaVersion"]?.GetValue<int>() != 1 || !UI.Kinds.Contains(data["kind"]?.GetValue<string>()) || string.IsNullOrEmpty(data["id"]?.GetValue<string>()) || data["title"] is null) throw new FormatException("Not a supported Gaius document.");
        var file = new OfficeFile(data);
        _ = file.Content["text"]!.GetValue<string>(); _ = file.Content["html"]!.GetValue<string>();
        if (file.Sheet["cells"] is not JsonObject || data["slides"] is not JsonArray || data["pdf"] is not JsonObject || data["image"] is not JsonObject) throw new FormatException("Invalid document content.");
        foreach (var pair in (JsonObject)file.Sheet["cells"]!) { if (!System.Text.RegularExpressions.Regex.IsMatch(pair.Key, "^[A-Z]+[1-9][0-9]*$") || pair.Value is null) throw new FormatException("Invalid spreadsheet cell."); _ = pair.Value.GetValue<string>(); }
        return file;
    }
    public OfficeFile Copy() { var copy = (JsonObject)Data.DeepClone(); copy["sheet"] = Sheet.DeepClone(); return new(copy); }
    public static string Column(int index) { var result = ""; for (int n = index + 1; n > 0; n = (n - 1) / 26) result = (char)('A' + (n - 1) % 26) + result; return result; }
}
public static class DocumentStore
{
    public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Gaius", "Documents");
    private static string FilePath(string id) { if (!Guid.TryParse(id, out _)) throw new FormatException("Invalid document id."); return Path.Combine(DirectoryPath, id + ".gaius"); }
    public static async Task<List<OfficeFile>> ListAsync()
    {
        Directory.CreateDirectory(DirectoryPath);
        var result = new List<OfficeFile>();
        foreach (var path in Directory.GetFiles(DirectoryPath, "*.gaius")) result.Add(OfficeFile.Parse(await File.ReadAllTextAsync(path)));
        return result.OrderByDescending(f => f.Revision).ToList();
    }
    public static async Task SaveAsync(OfficeFile file, string? expectedRevision)
    {
        Directory.CreateDirectory(DirectoryPath); var target = FilePath(file.Id);
        // Cross-process lock plus optimistic revision checking prevents silent overwrites.
        await using var fileLock = new FileStream(target + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(target) && expectedRevision is not null)
        {
            var existing = OfficeFile.Parse(await File.ReadAllTextAsync(target));
            if (existing.Revision != expectedRevision) throw new IOException("This document changed in another window. Export this copy before reopening the latest version.");
        }
        var temp = target + "." + Guid.NewGuid() + ".tmp";
        await File.WriteAllTextAsync(temp, file.Data.ToJsonString()); File.Move(temp, target, true);
    }
    public static void Delete(OfficeFile file) { File.Delete(FilePath(file.Id)); }
}
public static class NativeEngine
{
    [DllImport("gaius_engine", CallingConvention = CallingConvention.Cdecl)]
    private static extern nuint gaius_calculate([MarshalAs(UnmanagedType.LPUTF8Str)] string expression, [MarshalAs(UnmanagedType.LPUTF8Str)] string cells, byte[] output, nuint capacity);
    public static string Calculate(string address, JsonObject cells)
    {
        var output = new byte[4096];
        try { var size = gaius_calculate(address, cells.ToJsonString(), output, (nuint)output.Length); if (size > (nuint)output.Length) { output = new byte[checked((int)size)]; gaius_calculate(address, cells.ToJsonString(), output, size); } return Encoding.UTF8.GetString(output, 0, Array.IndexOf(output, (byte)0)); }
        catch (DllNotFoundException) { return "#ENGINE!"; } catch (BadImageFormatException) { return "#ARCH!"; }
    }
}
