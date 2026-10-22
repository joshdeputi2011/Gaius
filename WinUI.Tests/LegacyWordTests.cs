using System.Buffers.Binary;
using System.Text;
using Gaius.Office;

internal static class LegacyWordTests
{
    public static void Run(Action<bool, string> check)
    {
        string text = "Native Ω नमस्ते 😀\rSecond\aCell";
        var unicode = Fixture(Encoding.Unicode.GetBytes(text), text.Length, compressed: false, mini: false);
        var imported = LegacyWord.Import(unicode);
        check(imported.Content["text"]?.ToString() == text.Replace('\r', '\n').Replace('\a', '\t'), "Binary DOC lost Unicode, paragraphs or table cell text");
        check(imported.Data["importWarning"]?.ToString().Contains("formatting") == true, "Binary DOC import omitted the content compatibility warning");
        check(Convert.FromBase64String(imported.Data["originalFile"]!["dataBase64"]!.ToString()).SequenceEqual(unicode), "Legacy DOC import did not preserve exact source bytes");
        var compressed = Fixture([0x93, 65, 0x94, 0x20, 0x97, 0x20, 0x82], 7, compressed: true, mini: true);
        check(LegacyWord.Import(compressed).Content["text"]?.ToString() == "“A” — ‚", "DOC mini-stream/compressed text mapping failed");
        var rtf = LegacyWord.Import(Encoding.UTF8.GetBytes(@"{\rtf1\ansi Before \b bold\b0  after\par}"));
        check(rtf.Content["rtf"] is not null && RichTextInterop.HtmlToDelta(rtf.Content["html"]!.ToString()).Any(operation => operation?["insert"]?.ToString().Contains("bold") == true && operation?["attributes"]?["bold"]?.ToString() == "true"), "RTF disguised as DOC lost rich text");
        var html = LegacyWord.Import(Encoding.UTF8.GetBytes("<!doctype html><html><body><p><b>HTML Ω</b></p></body></html>"));
        check(html.Content["text"]!.ToString().Contains("HTML Ω"), "HTML disguised as DOC was not imported");
        var docx = OfficeInterchange.Export(html);
        check(LegacyWord.Import(docx).Content["text"]!.ToString().Contains("HTML Ω"), "DOCX disguised as DOC was not routed to OOXML import");
        bool Reject(byte[] input) { try { LegacyWord.Import(input); return false; } catch (FormatException) { return true; } }
        check(Reject(unicode[..^1]), "Truncated compound DOC was accepted");
        var cycle = unicode.ToArray(); U32(cycle, 512 + 4, 1);
        check(Reject(cycle), "Cyclic compound DOC directory chain was accepted");
        var encrypted = unicode.ToArray(); U16(encrypted, 3 * 512 + 10, 0x100);
        check(Reject(encrypted), "Encrypted DOC was treated as editable plaintext");
        var invalidOffset = unicode.ToArray(); U32(invalidOffset, 11 * 512 + 15, 0x3fffffff);
        check(Reject(invalidOffset), "Out-of-bounds DOC piece offset was accepted");
        check(Reject("arbitrary bytes are not a Word document"u8.ToArray()), "Unrecognized DOC content was silently misinterpreted");
    }

    // Small generated MS-CFB files exercise both FAT and mini-FAT without storing external documents.
    private static byte[] Fixture(byte[] text, int characters, bool compressed, bool mini)
    {
        const uint Free = 0xffffffff, End = 0xfffffffe;
        int sectors = mini ? 8 : 18;
        var result = new byte[(sectors + 1) * 512];
        new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 }.CopyTo(result, 0);
        U16(result, 24, 0x3e); U16(result, 26, 3); U16(result, 28, 0xfffe); U16(result, 30, 9); U16(result, 32, 6);
        U32(result, 44, 1); U32(result, 48, 1); U32(result, 56, 4096); U32(result, 60, mini ? 2u : End); U32(result, 64, mini ? 1u : 0u); U32(result, 68, End);
        for (int position = 76; position < 512; position += 4) U32(result, position, Free);
        U32(result, 76, 0);
        for (int index = 0; index < 128; index++) U32(result, 512 + index * 4, Free);
        U32(result, 512, 0xfffffffd); U32(result, 516, End);
        var word = new byte[mini ? 2048 : 4096];
        U16(word, 0, 0xa5ec); U16(word, 2, 0xc1); U32(word, 0x4c, (uint)characters); U32(word, 0x1a2, 0); U32(word, 0x1a6, 21);
        text.CopyTo(word, 1024);
        var table = new byte[mini ? 21 : 4096];
        table[0] = 2; U32(table, 1, 16); U32(table, 5, 0); U32(table, 9, (uint)characters); U32(table, 15, compressed ? 0x40000800 : 1024u);
        void Entry(int index, string name, byte type, uint start, uint size)
        {
            int offset = 1024 + index * 128; var encoded = Encoding.Unicode.GetBytes(name + '\0'); encoded.CopyTo(result, offset);
            U16(result, offset + 64, (ushort)encoded.Length); result[offset + 66] = type; result[offset + 67] = 1;
            U32(result, offset + 68, Free); U32(result, offset + 72, Free); U32(result, offset + 76, type == 5 ? 1u : Free); U32(result, offset + 116, start); U32(result, offset + 120, size);
        }
        if (mini) {
            Entry(0, "Root Entry", 5, 3, 2112); Entry(1, "WordDocument", 2, 0, 2048); Entry(2, "0Table", 2, 32, 21);
            U32(result, 512 + 2 * 4, End);
            for (int sector = 3; sector <= 7; sector++) U32(result, 512 + sector * 4, sector == 7 ? End : (uint)sector + 1);
            for (int index = 0; index < 128; index++) U32(result, 1536 + index * 4, Free);
            for (int index = 0; index < 32; index++) U32(result, 1536 + index * 4, index == 31 ? End : (uint)index + 1);
            U32(result, 1536 + 32 * 4, End); word.CopyTo(result, 2048); table.CopyTo(result, 2048 + 2048);
        } else {
            Entry(0, "Root Entry", 5, End, 0); Entry(1, "WordDocument", 2, 2, 4096); Entry(2, "0Table", 2, 10, 4096);
            for (int sector = 2; sector <= 9; sector++) U32(result, 512 + sector * 4, sector == 9 ? End : (uint)sector + 1);
            for (int sector = 10; sector <= 17; sector++) U32(result, 512 + sector * 4, sector == 17 ? End : (uint)sector + 1);
            word.CopyTo(result, 1536); table.CopyTo(result, 5632);
        }
        return result;
    }

    private static void U16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), value);
    private static void U32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
}
