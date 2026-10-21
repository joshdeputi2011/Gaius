import 'dart:convert';
import 'dart:typed_data';
import 'package:archive/archive.dart';
import 'package:gaius_document/gaius_document.dart';
import 'package:gaius_document/office_import.dart';
import 'package:gaius_document/office_export.dart';
import 'package:test/test.dart';

Uint8List zip(Map<String, String> parts) {
  final archive = Archive();
  for (final part in parts.entries) {
    final bytes = utf8.encode(part.value);
    archive.addFile(ArchiveFile(part.key, bytes.length, bytes));
  }
  return Uint8List.fromList(ZipEncoder().encode(archive));
}

const relns = 'http://schemas.openxmlformats.org/package/2006/relationships';
void main() {
  test('DOCX import preserves Unicode, runs, paragraphs and heading attributes',
      () {
    final bytes = zip({
      'word/document.xml':
          '<w:document xmlns:w="urn:word"><w:body><w:p><w:pPr><w:pStyle w:val="Heading2"/></w:pPr><w:r><w:rPr><w:b/></w:rPr><w:t>Plan &amp; café 世界</w:t></w:r><w:r><w:tab/><w:t>2026</w:t></w:r></w:p><w:p><w:r><w:t>Second paragraph</w:t></w:r></w:p></w:body></w:document>'
    });
    for (final ext in ['docx', 'docm', 'dotx', 'dotm']) {
      final file = importOffice('Plan.$ext', bytes);
      expect(file.kind, 'document');
      expect(file.content['text'], 'Plan & café 世界\t2026\nSecond paragraph\n');
      expect(file.content['delta'][0]['attributes']['bold'], true);
      expect(file.content['delta'][2]['attributes']['header'], 2);
    }
  });
  test(
      'XLSX import follows relationships and retains all worksheets and formulas',
      () {
    final bytes = zip({
      'xl/workbook.xml':
          '<workbook xmlns:r="urn:rels"><sheets><sheet name="Costs" sheetId="1" r:id="rId3"/><sheet name="Notes" sheetId="2" r:id="rId1"/></sheets></workbook>',
      'xl/_rels/workbook.xml.rels':
          '<Relationships xmlns="$relns"><Relationship Id="rId3" Target="worksheets/sheet2.xml"/><Relationship Id="rId1" Target="/xl/worksheets/sheet1.xml"/></Relationships>',
      'xl/sharedStrings.xml':
          '<sst><si><r><t>Office </t></r><r><t>budget</t></r></si></sst>',
      'xl/worksheets/sheet2.xml':
          '<worksheet><sheetData><row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1"><v>123.5</v></c><c r="C1"><f>SUM(B1:B2)</f><v>130</v></c></row></sheetData></worksheet>',
      'xl/worksheets/sheet1.xml':
          '<worksheet><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>Note</t></is></c></row></sheetData></worksheet>',
    });
    for (final ext in ['xlsx', 'xlsm', 'xltx', 'xltm']) {
      final file = importOffice('Budget.$ext', bytes);
      expect(file.cells,
          {'A1': 'Office budget', 'B1': '123.5', 'C1': '=SUM(B1:B2)'});
      expect(file.data['worksheets'].length, 2);
      expect(file.data['worksheets'][1]['cells']['A1'], 'Note');
    }
  });
  test('PPTX import uses r:id instead of numeric id and respects slide order',
      () {
    final bytes = zip({
      'ppt/presentation.xml':
          '<p:presentation xmlns:p="urn:p" xmlns:r="urn:r"><p:sldIdLst><p:sldId id="256" r:id="rId2"/><p:sldId id="257" r:id="rId1"/></p:sldIdLst></p:presentation>',
      'ppt/_rels/presentation.xml.rels':
          '<Relationships xmlns="$relns"><Relationship Id="rId1" Target="slides/slide1.xml"/><Relationship Id="rId2" Target="slides/slide2.xml"/></Relationships>',
      'ppt/slides/slide1.xml': '<sld><p><r><t>End</t></r></p></sld>',
      'ppt/slides/slide2.xml':
          '<sld><p><r><t>Start</t></r></p><p><r><t>Welcome</t></r></p></sld>',
    });
    for (final ext in ['pptx', 'pptm', 'ppsx', 'ppsm', 'potx', 'potm']) {
      final file = importOffice('Talk.$ext', bytes);
      expect(file.slides.map((s) => s['title']), ['Start', 'End']);
      expect(file.slides[0]['body'], 'Welcome');
    }
  });
  test('ODS repeated rows and cells are bounded and imported', () {
    final bytes = zip({
      'content.xml':
          '<document xmlns:table="urn:table" xmlns:office="urn:office"><table:table table:name="Data"><table:table-row table:number-rows-repeated="2"><table:table-cell table:number-columns-repeated="2" office:value="12"/></table:table-row></table:table></document>'
    });
    final file = importOffice('data.ods', bytes);
    expect(file.cells, {'A1': '12', 'B1': '12', 'A2': '12', 'B2': '12'});
  });
  test('ODT and ODP text import', () {
    final bytes = zip({
      'content.xml':
          '<document xmlns:draw="urn:draw"><draw:page draw:name="page1"><h>Title</h><p>Body</p></draw:page></document>'
    });
    expect(importOffice('note.odt', bytes).content['text'], 'Title\nBody');
    expect(importOffice('deck.odp', bytes).slides[0]['title'], 'Body');
  });
  test('RTF import handles Unicode fallback and ignores font and image data',
      () {
    final text = decodeRtf(
        r'{\rtf1\ansi{\fonttbl{\f0 Arial;}}Hello \b world\b0\par Unicode \uc1\u945? {\pict HIDDEN}done}');
    expect(text, 'Hello world\nUnicode α done');
  });
  test('CSV and TSV quoting and multiline cells', () {
    expect(parseDelimited('"a,b","line\n2"\r\n"x""y",5'), [
      ['a,b', 'line\n2'],
      ['x"y', '5']
    ]);
    expect(
        importOffice('book.tsv',
                Uint8List.fromList(utf8.encode('name\tvalue\nhello\t42')))
            .cells['B2'],
        '42');
    expect(() => parseDelimited('"broken'), throwsFormatException);
  });
  test(
      'unsupported formats, invalid ZIP, oversized sheets and external relationships fail explicitly',
      () {
    expect(
        () => importOffice('book.xlsb', Uint8List(3)), throwsFormatException);
    expect(() => importOffice('word.docx', Uint8List(100)), throwsA(anything));
    expect(() => importOffice('bad.pdf', Uint8List.fromList([1, 2, 3])),
        throwsFormatException);
    final bytes = zip({
      'content.xml':
          '<d xmlns:t="urn:t" xmlns:o="urn:o"><t:table><t:table-row t:number-rows-repeated="10001"><t:table-cell o:value="1"/></t:table-row></t:table></d>'
    });
    expect(() => importOffice('big.ods', bytes), throwsFormatException);
    expect(
        () => importOffice('bad.doc', Uint8List(512)), throwsFormatException);
  });
  test('legacy Word piece table imports main story without table metadata', () {
    final word = Uint8List(4096), table = Uint8List(4096);
    put16(word, 0, 0xa5ec);
    put16(word, 2, 0xc1);
    put32(word, 0x4c, 5);
    put32(word, 0x1a2, 0);
    put32(word, 0x1a6, 21);
    word.setRange(1024, 1029, latin1.encode('Hello'));
    table[0] = 2;
    put32(table, 1, 16);
    put32(table, 5, 0);
    put32(table, 9, 5);
    put32(table, 15, 0x40000000 | (1024 * 2));
    expect(
        importOffice(
                'old.doc', compound({'WordDocument': word, '0Table': table}))
            .content['text'],
        'Hello');
  });
  test('legacy Excel numeric, boolean and SST cells retain sheet names', () {
    final bof = Uint8List(16);
    put16(bof, 0, 0x600);
    final bound = Uint8List.fromList(
        [...List.filled(6, 0), 4, 0, ...ascii.encode('Data')]);
    final sst = Uint8List(16);
    put32(sst, 0, 1);
    put32(sst, 4, 1);
    put16(sst, 8, 5);
    sst.setRange(11, 16, ascii.encode('Hello'));
    final number = Uint8List(14);
    put16(number, 2, 1);
    ByteData.sublistView(number).setFloat64(6, 42.5, Endian.little);
    final label = Uint8List(10);
    final globals = [
      record(0x809, bof),
      record(0x85, bound),
      record(0xfc, sst),
      record(0x0a, Uint8List(0))
    ];
    put32(bound, 0, globals.fold<int>(0, (n, b) => n + b.length));
    globals[1] = record(0x85, bound);
    final workbook = Uint8List(4096);
    final stream = [
      ...globals.expand((b) => b),
      ...record(0x809, bof),
      ...record(0x203, number),
      ...record(0xfd, label),
      ...record(0x0a, Uint8List(0))
    ];
    workbook.setRange(0, stream.length, stream);
    final file = importOffice('old.xls', compound({'Workbook': workbook}));
    expect(file.sheet['name'], 'Data');
    expect(file.cells, {'A1': 'Hello', 'B1': '42.5'});
  });
  test(
      'legacy PPT basic text import and malformed compounds do not silently succeed',
      () {
    Uint8List pptRecord(int type, List<int> b, [int flags = 0]) {
      final out = Uint8List(b.length + 8);
      put16(out, 0, flags);
      put16(out, 2, type);
      put32(out, 4, b.length);
      out.setRange(8, out.length, b);
      return out;
    }

    final payload = [
      ...pptRecord(0x3f3, []),
      ...pptRecord(4008, ascii.encode('Title')),
      ...pptRecord(4008, ascii.encode('Body'))
    ];
    final stream = Uint8List(4096), list = pptRecord(0xff0, payload, 15);
    stream.setRange(0, list.length, list);
    final file =
        importOffice('old.ppt', compound({'PowerPoint Document': stream}));
    expect(file.slides[0]['title'], 'Title');
    expect(file.slides[0]['body'], 'Body');
    final broken = compound({'WordDocument': Uint8List(4096)});
    put32(broken, 512 + 4, 1);
    expect(() => importOffice('bad.doc', broken), throwsFormatException);
  });
  test('editable OOXML exports can be reopened for all three applications', () {
    final doc = OfficeFile.create('document');
    doc.content['text'] = 'A & B\nWorld';
    expect(importOffice('out.docx', exportOffice(doc)).content['text'],
        'A & B\nWorld\n');
    final sheet = OfficeFile.create('spreadsheet');
    sheet.sheet['cells'] = {'A1': 'Name', 'B1': '42', 'C1': '=SUM(B1:B2)'};
    expect(importOffice('out.xlsx', exportOffice(sheet)).cells, sheet.cells);
    final slides = OfficeFile.create('presentation');
    slides.slides[0]['title'] = 'Hello';
    expect(importOffice('out.pptx', exportOffice(slides)).slides[0]['title'],
        'Hello');
  });
}

void put16(Uint8List b, int p, int n) =>
    ByteData.sublistView(b).setUint16(p, n, Endian.little);
void put32(Uint8List b, int p, int n) =>
    ByteData.sublistView(b).setUint32(p, n, Endian.little);
Uint8List record(int type, Uint8List b) {
  final out = Uint8List(b.length + 4);
  put16(out, 0, type);
  put16(out, 2, b.length);
  out.setRange(4, out.length, b);
  return out;
}

/// Minimal independent CFB v3 fixture: FAT sector, directory, 4 KiB regular streams.
Uint8List compound(Map<String, Uint8List> streams) {
  final out = Uint8List(512 * (3 + streams.length * 8));
  out.setRange(0, 8, [208, 207, 17, 224, 161, 177, 26, 225]);
  put16(out, 24, 0x3e);
  put16(out, 26, 3);
  put16(out, 28, 0xfffe);
  put16(out, 30, 9);
  put16(out, 32, 6);
  put32(out, 44, 1);
  put32(out, 48, 1);
  put32(out, 56, 4096);
  put32(out, 60, 0xfffffffe);
  put32(out, 68, 0xfffffffe);
  for (var p = 76; p < 512; p += 4) {
    put32(out, p, 0xffffffff);
  }
  put32(out, 76, 0);
  for (var p = 512; p < 1024; p += 4) {
    put32(out, p, 0xffffffff);
  }
  put32(out, 512, 0xfffffffd);
  put32(out, 516, 0xfffffffe);
  void directory(int index, String name, int type, int start, int size) {
    final p = 1024 + index * 128;
    for (var i = 0; i < name.length; i++) {
      put16(out, p + i * 2, name.codeUnitAt(i));
    }
    put16(out, p + 64, (name.length + 1) * 2);
    out[p + 66] = type;
    put32(out, p + 116, start);
    put32(out, p + 120, size);
  }

  directory(0, 'Root Entry', 5, 0xfffffffe, 0);
  var index = 0;
  for (final entry in streams.entries) {
    final sector = 2 + index * 8;
    directory(index + 1, entry.key, 2, sector, 4096);
    for (var s = 0; s < 8; s++) {
      put32(out, 512 + (sector + s) * 4, s == 7 ? 0xfffffffe : sector + s + 1);
    }
    out.setRange((sector + 1) * 512, (sector + 1) * 512 + entry.value.length,
        entry.value);
    index++;
  }
  return out;
}
