using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Gaius.Office;
public static class Csv
{
    public static List<List<string>> Parse(string input)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var field = ""; bool quoted = false;
        for (int i = 0; i < input.Length; i++) { char c = input[i]; if (c == '"') { if (quoted && i + 1 < input.Length && input[i + 1] == '"') { field += '"'; i++; } else quoted = !quoted; } else if (c == ',' && !quoted) { row.Add(field); field = ""; } else if ((c == '\n' || c == '\r') && !quoted) { if (c == '\r' && i + 1 < input.Length && input[i + 1] == '\n') i++; row.Add(field); rows.Add(row); row = []; field = ""; } else field += c; }
        if (quoted) throw new FormatException("CSV has an unclosed quoted field."); if (field.Length > 0 || row.Count > 0) { row.Add(field); rows.Add(row); } return rows;
    }
    public static string Export(JsonObject cells)
    {
        int rows = 1, columns = 1; foreach (var cell in cells) { var match = Regex.Match(cell.Key, "^([A-Z]+)([0-9]+)$"); if (!match.Success) continue; int col = 0; foreach (char c in match.Groups[1].Value) col = col * 26 + c - 'A' + 1; rows = Math.Max(rows, int.Parse(match.Groups[2].Value)); columns = Math.Max(columns, col); }
        if ((long)rows * columns > 1000000) throw new InvalidOperationException("Export is too large.");
        return string.Join("\r\n", Enumerable.Range(0, rows).Select(r => string.Join(",", Enumerable.Range(0, columns).Select(c => { var address = $"{OfficeFile.Column(c)}{r + 1}"; var value = cells[address]?.GetValue<string>() ?? ""; if (value.StartsWith('=')) value = NativeEngine.Calculate(address, cells); if (Regex.IsMatch(value, "^[=+\\-@\\t\\r]") && !double.TryParse(value, out _)) value = "'" + value; return "\"" + value.Replace("\"", "\"\"") + "\""; }))));
    }
}
