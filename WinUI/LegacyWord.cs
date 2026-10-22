using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Gaius.Office;

/// <summary>Bounded, local Word 97–2003 content import. Binary DOC is never presented as a lossless editable format.</summary>
public static class LegacyWord
{
    private const int Limit = 128 * 1024 * 1024;
    private const uint SpecialSector = 0xfffffffa, EndOfChain = 0xfffffffe;
    private static readonly byte[] Signature = [0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1];

    public static OfficeFile Import(byte[] bytes)
    {
        if (bytes.Length > Limit || bytes.Length == 0) throw new FormatException("Choose a Word document smaller than 128 MB.");
        OfficeFile result;
        string warning;
        if (bytes.AsSpan().StartsWith(Signature)) {
            result = OfficeFile.Create("document");
            string text = ReadWord(new Compound(bytes));
            result.Content["text"] = text;
            result.Content["html"] = "<p>" + WebUtility.HtmlEncode(text).Replace("\n", "<br>") + "</p>";
            result.Content["delta"] = RichTextInterop.HtmlToDelta(result.Content["html"]!.ToString());
            warning = "Word 97–2003 content import: text is editable; original formatting, pictures, fields, headers and layout may be omitted. Save edits as DOCX or Gaius. The original DOC is retained for exporting an unchanged copy.";
        } else if (bytes.AsSpan().StartsWith("PK\x03\x04"u8)) {
            result = OfficeInterchange.Import(bytes, ".docx");
            warning = "This DOC file contains a DOCX package. Save edited content as DOCX or Gaius; the original file is retained unchanged.";
        } else {
            string source = DecodeText(bytes);
            string prefix = source.TrimStart('\ufeff', ' ', '\r', '\n', '\t');
            result = OfficeFile.Create("document");
            if (prefix.StartsWith(@"{\rtf", StringComparison.OrdinalIgnoreCase)) {
                result.Content["rtf"] = source;
                SetHtml(result, RtfPipe.Rtf.ToHtml(source));
                warning = "This DOC file contains RTF. Supported rich text is editable; save edits as DOCX or Gaius. The original file is retained unchanged.";
            } else if (Regex.IsMatch(prefix, @"^(?:<!doctype\s+html\b|<html\b|<head\b|<body\b|<\?xml[^>]*>\s*<html\b)", RegexOptions.IgnoreCase)) {
                SetHtml(result, source);
                warning = "This DOC file contains HTML. Supported document content is editable; save edits as DOCX or Gaius. The original file is retained unchanged.";
            } else throw new FormatException("This file is not a supported Word 97–2003, RTF, HTML or DOCX document. Open it in its original application and save a DOCX copy.");
        }
        result.Data["importWarning"] = warning;
        result.Data["originalFile"] = new JsonObject { ["extension"] = ".doc", ["dataBase64"] = Convert.ToBase64String(bytes), ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() };
        return result;
    }

    private static void SetHtml(OfficeFile file, string html)
    {
        var delta = RichTextInterop.HtmlToDelta(html);
        file.Content["html"] = html; file.Content["delta"] = delta;
        file.Content["text"] = string.Concat(delta.OfType<JsonObject>().Where(op => op["insert"] is JsonValue).Select(op => op["insert"]!.ToString()));
    }

    private static string DecodeText(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(1252).GetString(bytes); }
    }

    // MS-DOC 2.4.1: https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-doc/01d5d8c4-cf9c-4ef9-80fd-439e763cfe01
    private static string ReadWord(Compound file)
    {
        var word = file.Stream("WordDocument");
        if (word.Length < 0x1aa || U16(word, 0) != 0xa5ec || U16(word, 2) < 0xc1) throw new FormatException("Only Word 97–2003 binary documents are supported.");
        ushort flags = U16(word, 10);
        if ((flags & 0x8100) != 0) throw new FormatException("Password-protected Word documents must be decrypted in their original application before importing.");
        var table = file.Stream((flags & 0x200) != 0 ? "1Table" : "0Table");
        int offset = Length(U32(word, 0x1a2)), length = Length(U32(word, 0x1a6));
        Require(table, offset, length);
        if (length == 0) throw new FormatException("The Word document has no supported text piece table.");
        int position = offset, end = offset + length;
        while (position < end && table[position] == 1) { if (position + 3 > end) throw new FormatException("Truncated Word formatting record."); position = checked(position + 3 + U16(table, position + 1)); }
        if (position + 5 > end || table[position] != 2) throw new FormatException("Invalid Word piece table.");
        int pieceLength = Length(U32(table, position + 1)), start = position + 5;
        if (pieceLength < 4 || (pieceLength - 4) % 12 != 0 || pieceLength > end - start) throw new FormatException("Invalid Word text pieces.");
        int count = (pieceLength - 4) / 12, mainLength = Length(U32(word, 0x4c));
        if (U32(table, start) != 0 || count == 0 && mainLength > 0) throw new FormatException("Invalid Word character positions.");
        var text = new StringBuilder(Math.Min(mainLength, 1024 * 1024));
        for (int index = 0; index < count; index++) {
            int cp = Length(U32(table, start + index * 4)), next = Length(U32(table, start + (index + 1) * 4));
            if (next < cp) throw new FormatException("Word character positions are out of order.");
            if (cp >= mainLength) break;
            int characters = Math.Min(next, mainLength) - cp;
            uint raw = U32(table, start + (count + 1) * 4 + index * 8 + 2);
            bool compressed = (raw & 0x40000000) != 0;
            int address = Length((raw & 0x3fffffff) / (compressed ? 2u : 1u)), size = checked(characters * (compressed ? 1 : 2));
            Require(word, address, size);
            if (compressed) for (int character = 0; character < characters; character++) text.Append(CompressedCharacter(word[address + character]));
            else text.Append(Encoding.Unicode.GetString(word, address, size));
        }
        if (text.Length < mainLength) throw new FormatException("The Word document's main text is truncated.");
        return Regex.Replace(text.ToString().Replace('\r', '\n').Replace('\v', '\n').Replace('\f', '\n').Replace('\a', '\t'), "[\x00-\x06\x0e-\x1f]", "");
    }

    // FcCompressed's explicit mappings differ from Windows-1252 at 0x80/0x8e/0x9e.
    // https://learn.microsoft.com/en-us/openspecs/office_file_formats/ms-doc/aa2e55a2-f4f2-4795-bab5-6d9d7a0ed249
    private static char CompressedCharacter(byte value) => value switch {
        0x82 => '\u201a', 0x83 => '\u0192', 0x84 => '\u201e', 0x85 => '\u2026', 0x86 => '\u2020', 0x87 => '\u2021', 0x88 => '\u02c6', 0x89 => '\u2030',
        0x8a => '\u0160', 0x8b => '\u2039', 0x8c => '\u0152', 0x91 => '\u2018', 0x92 => '\u2019', 0x93 => '\u201c', 0x94 => '\u201d', 0x95 => '\u2022',
        0x96 => '\u2013', 0x97 => '\u2014', 0x98 => '\u02dc', 0x99 => '\u2122', 0x9a => '\u0161', 0x9b => '\u203a', 0x9c => '\u0153', 0x9f => '\u0178', _ => (char)value
    };

    private static int Length(uint value) => value <= Limit ? (int)value : throw new FormatException("The Word content exceeds the import limit.");
    private static void Require(byte[] bytes, int offset, int length) { if (offset < 0 || length < 0 || offset > bytes.Length - length) throw new FormatException("The Word document is truncated or contains an invalid offset."); }
    private static ushort U16(byte[] bytes, int offset) { Require(bytes, offset, 2); return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2)); }
    private static uint U32(byte[] bytes, int offset) { Require(bytes, offset, 4); return BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)); }

    // MS-CFB 2.2: https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-cfb/05060311-bfce-4b12-874d-71fd4ce63aea
    private sealed class Compound
    {
        private readonly byte[] bytes;
        private readonly int sectorSize;
        private readonly List<uint> fat = [], miniFat = [];
        private readonly Dictionary<string, (uint Start, int Size)> entries = new(StringComparer.Ordinal);
        private readonly byte[] miniStream;

        public Compound(byte[] source)
        {
            bytes = source;
            Require(bytes, 0, 512);
            int shift = U16(bytes, 30);
            if (shift is not (9 or 12) || U16(bytes, 32) != 6 || U16(bytes, 28) != 0xfffe || U32(bytes, 56) != 4096) throw new FormatException("Unsupported compound document sector layout.");
            sectorSize = 1 << shift;
            if (bytes.Length < sectorSize || bytes.Length % sectorSize != 0) throw new FormatException("Truncated compound document.");
            uint fatCount = U32(bytes, 44);
            if (fatCount > bytes.Length / sectorSize) throw new FormatException("Invalid compound allocation table size.");
            var sectors = new List<uint>();
            for (int position = 76; position < 512; position += 4) { uint sector = U32(bytes, position); if (sector < SpecialSector) sectors.Add(sector); }
            var seen = new HashSet<uint>(); uint next = U32(bytes, 68);
            while (next < SpecialSector) {
                if (!seen.Add(next) || seen.Count > bytes.Length / sectorSize) throw new FormatException("Cyclic compound allocation chain.");
                var data = Sector(next);
                for (int position = 0; position < sectorSize - 4; position += 4) { uint sector = U32(data, position); if (sector < SpecialSector) sectors.Add(sector); }
                next = U32(data, sectorSize - 4);
            }
            if (sectors.Count != fatCount || sectors.Distinct().Count() != sectors.Count || seen.Count != U32(bytes, 72)) throw new FormatException("Invalid compound allocation table.");
            foreach (uint sector in sectors) { var data = Sector(sector); for (int position = 0; position < data.Length; position += 4) fat.Add(U32(data, position)); }
            var directory = Chain(U32(bytes, 48), fat);
            uint rootStart = EndOfChain; int rootSize = 0;
            for (int position = 0; position + 128 <= directory.Length; position += 128) {
                int nameLength = U16(directory, position + 64); byte type = directory[position + 66];
                if (type is not (2 or 5)) continue;
                if (nameLength < 2 || nameLength > 64 || nameLength % 2 != 0) throw new FormatException("Invalid compound stream name.");
                string name = Encoding.Unicode.GetString(directory, position, nameLength - 2);
                if (U32(directory, position + 124) != 0) throw new FormatException("The compound stream exceeds the import limit.");
                uint start = U32(directory, position + 116); int size = Length(U32(directory, position + 120));
                if (type == 5) { rootStart = start; rootSize = size; }
                else if (!entries.TryAdd(name, (start, size))) throw new FormatException("Duplicate compound document stream.");
            }
            if (entries.ContainsKey("EncryptedPackage") || entries.ContainsKey("EncryptionInfo")) throw new FormatException("Password-protected documents must be decrypted before importing.");
            miniStream = Chain(rootStart, fat, rootSize);
            uint miniSectorCount = U32(bytes, 64);
            if (miniSectorCount > bytes.Length / sectorSize) throw new FormatException("Invalid mini allocation table size.");
            var miniTable = Chain(U32(bytes, 60), fat, checked((int)miniSectorCount * sectorSize));
            for (int position = 0; position + 4 <= miniTable.Length; position += 4) miniFat.Add(U32(miniTable, position));
        }

        private byte[] Sector(uint index)
        {
            long start = ((long)index + 1) * sectorSize;
            if (start < sectorSize || start + sectorSize > bytes.Length) throw new FormatException("Invalid compound document sector.");
            return bytes.AsSpan((int)start, sectorSize).ToArray();
        }

        private byte[] Chain(uint start, List<uint> table, int? size = null, bool mini = false)
        {
            if (size == 0) return [];
            using var result = new MemoryStream(); var seen = new HashSet<uint>(); uint next = start;
            while (next < SpecialSector) {
                if (next >= table.Count || !seen.Add(next)) throw new FormatException("Invalid or cyclic compound stream.");
                byte[] data;
                if (mini) { long offset = (long)next * 64; if (offset + 64 > miniStream.Length) throw new FormatException("Invalid compound mini stream."); data = miniStream.AsSpan((int)offset, 64).ToArray(); }
                else data = Sector(next);
                result.Write(data);
                if (result.Length > Limit) throw new FormatException("The compound stream exceeds the import limit.");
                if (size is int expected && result.Length >= expected) break;
                next = table[(int)next];
            }
            if (size is int length && result.Length < length) throw new FormatException("Truncated compound stream.");
            return size is int requested ? result.GetBuffer().AsSpan(0, requested).ToArray() : result.ToArray();
        }

        public byte[] Stream(string name)
        {
            if (!entries.TryGetValue(name, out var entry)) throw new FormatException("Missing Word document stream: " + name);
            return Chain(entry.Start, entry.Size < 4096 ? miniFat : fat, entry.Size, entry.Size < 4096);
        }
    }
}
