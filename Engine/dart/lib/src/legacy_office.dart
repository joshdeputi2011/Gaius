import 'dart:convert';
import 'dart:typed_data';
import '../gaius_document.dart';

int u16(Uint8List b, int p) =>
    ByteData.sublistView(b).getUint16(p, Endian.little);
int u32(Uint8List b, int p) =>
    ByteData.sublistView(b).getUint32(p, Endian.little);
String utf16(Uint8List b) =>
    String.fromCharCodes([for (var i = 0; i + 1 < b.length; i += 2) u16(b, i)]);

/// Bounded MS-CFB stream reader, including mini streams and extended DIFAT.
class CompoundFile {
  final Uint8List bytes;
  late final int sectorSize, miniSize;
  final fat = <int>[], miniFat = <int>[];
  final directory = <String, ({int start, int size})>{};
  late Uint8List miniStream;
  CompoundFile(this.bytes) {
    if (bytes.length < 512 ||
        bytes.sublist(0, 8).join(',') != '208,207,17,224,161,177,26,225')
      throw const FormatException('Not an Office 97–2003 compound file.');
    sectorSize = 1 << u16(bytes, 30);
    miniSize = 1 << u16(bytes, 32);
    if (![512, 4096].contains(sectorSize) ||
        miniSize != 64 ||
        bytes.length < sectorSize)
      throw const FormatException('Unsupported compound-file sector size.');
    final fatSectors = <int>[
      for (var p = 76; p < 512; p += 4)
        if (u32(bytes, p) < 0xfffffffa) u32(bytes, p)
    ];
    var next = u32(bytes, 68);
    final visited = <int>{};
    while (next < 0xfffffffa) {
      if (!visited.add(next))
        throw const FormatException('Cyclic DIFAT chain.');
      final data = sector(next);
      for (var p = 0; p < sectorSize - 4; p += 4) {
        final n = u32(data, p);
        if (n < 0xfffffffa) fatSectors.add(n);
      }
      next = u32(data, sectorSize - 4);
    }
    if (fatSectors.length != u32(bytes, 44))
      throw const FormatException('Invalid FAT sector count.');
    for (final n in fatSectors) {
      final data = sector(n);
      for (var p = 0; p < data.length; p += 4) {
        fat.add(u32(data, p));
      }
    }
    final dir = chain(u32(bytes, 48), fat);
    var rootStart = 0xfffffffe, rootSize = 0;
    for (var p = 0; p + 128 <= dir.length; p += 128) {
      final nameLength = u16(dir, p + 64), type = dir[p + 66];
      if (nameLength < 2 || nameLength > 64 || ![2, 5].contains(type)) continue;
      final name = utf16(dir.sublist(p, p + nameLength - 2));
      final start = u32(dir, p + 116), size = u32(dir, p + 120);
      if (size > 128 * 1024 * 1024)
        throw const FormatException('Compound stream exceeds content limit.');
      if (type == 5) {
        rootStart = start;
        rootSize = size;
      } else {
        directory[name] = (start: start, size: size);
      }
    }
    miniStream = chain(rootStart, fat, size: rootSize);
    final mf = chain(u32(bytes, 60), fat);
    for (var p = 0; p + 4 <= mf.length; p += 4) {
      miniFat.add(u32(mf, p));
    }
    if (directory.containsKey('EncryptedPackage') ||
        directory.containsKey('EncryptionInfo'))
      throw const FormatException(
          'Password-protected Office files must be decrypted before importing.');
  }
  Uint8List sector(int n) {
    final start = (n + 1) * sectorSize;
    if (start < sectorSize || start + sectorSize > bytes.length)
      throw const FormatException('Invalid compound-file sector.');
    return Uint8List.sublistView(bytes, start, start + sectorSize);
  }

  Uint8List chain(int start, List<int> table, {int? size, bool mini = false}) {
    if (size == 0) return Uint8List(0);
    final result = BytesBuilder(copy: false), seen = <int>{};
    var n = start;
    while (n < 0xfffffffa) {
      if (n >= table.length || !seen.add(n))
        throw const FormatException('Invalid or cyclic compound stream.');
      if (mini) {
        final offset = n * miniSize;
        if (offset + miniSize > miniStream.length)
          throw const FormatException('Invalid mini stream.');
        result
            .add(Uint8List.sublistView(miniStream, offset, offset + miniSize));
      } else {
        result.add(sector(n));
      }
      if (result.length > 128 * 1024 * 1024)
        throw const FormatException('Stream exceeds content limit.');
      if (size != null && result.length >= size) break;
      n = table[n];
    }
    final data = result.takeBytes();
    if (size != null && data.length < size)
      throw const FormatException('Truncated compound stream.');
    return size == null ? data : Uint8List.sublistView(data, 0, size);
  }

  Uint8List stream(String name) {
    final entry = directory[name];
    if (entry == null) throw FormatException('Missing Office stream: $name');
    return chain(entry.start, entry.size < 4096 ? miniFat : fat,
        size: entry.size, mini: entry.size < 4096);
  }
}

void importLegacy(OfficeFile file, Uint8List bytes) {
  final cfb = CompoundFile(bytes);
  switch (file.kind) {
    case 'document':
      final word = cfb.stream('WordDocument');
      if (word.length < 0x1aa || u16(word, 0) != 0xa5ec || u16(word, 2) < 0xc1)
        throw const FormatException(
            'Only Word 97–2003 binary documents are supported.');
      if (u16(word, 10) & 0x8100 != 0)
        throw const FormatException('Encrypted Word files are not supported.');
      final table =
          cfb.stream(u16(word, 10) & 0x200 != 0 ? '1Table' : '0Table');
      final fc = u32(word, 0x1a2), len = u32(word, 0x1a6);
      if (fc + len > table.length || len == 0)
        throw const FormatException('Missing Word piece table.');
      var p = fc;
      while (p < fc + len && table[p] == 1) {
        p += 3 + u16(table, p + 1);
      }
      if (p + 5 > fc + len || table[p] != 2)
        throw const FormatException('Invalid Word piece table.');
      final length = u32(table, p + 1), base = p + 5;
      if ((length - 4) % 12 != 0 || base + length > table.length)
        throw const FormatException('Invalid Word text pieces.');
      final count = (length - 4) ~/ 12,
          mainLength = u32(word, 0x4c),
          text = StringBuffer();
      for (var i = 0; i < count; i++) {
        final cp = u32(table, base + i * 4),
            end = u32(table, base + (i + 1) * 4);
        if (cp >= mainLength) break;
        final chars = (end > mainLength ? mainLength : end) - cp;
        if (chars < 0)
          throw const FormatException('Invalid Word character range.');
        final raw = u32(table, base + (count + 1) * 4 + i * 8 + 2),
            compressed = raw & 0x40000000 != 0;
        final offset = (raw & 0x3fffffff) ~/ (compressed ? 2 : 1),
            size = chars * (compressed ? 1 : 2);
        if (offset + size > word.length)
          throw const FormatException('Truncated Word text.');
        final piece = Uint8List.sublistView(word, offset, offset + size);
        text.write(compressed ? latin1.decode(piece) : utf16(piece));
      }
      final value = text
          .toString()
          .replaceAll('\r', '\n')
          .replaceAll('\x07', '\t')
          .replaceAll(RegExp('[\x00-\x06\x0e-\x1f]'), '');
      file.data['content'] = {
        'text': value,
        'html': '<p>${escapeHtml(value).replaceAll('\n', '<br>')}</p>'
      };
    case 'spreadsheet':
      readBiff(
          file,
          cfb.stream(
              cfb.directory.containsKey('Workbook') ? 'Workbook' : 'Book'));
    case 'presentation':
      readPpt(file, cfb.stream('PowerPoint Document'));
  }
}

List<({int type, int offset, Uint8List data})> biffRecords(Uint8List bytes) {
  final out = <({int type, int offset, Uint8List data})>[];
  for (var p = 0; p + 4 <= bytes.length;) {
    final type = u16(bytes, p), size = u16(bytes, p + 2);
    if (p + 4 + size > bytes.length)
      throw const FormatException('Truncated Excel record.');
    out.add((
      type: type,
      offset: p,
      data: Uint8List.sublistView(bytes, p + 4, p + 4 + size)
    ));
    p += 4 + size;
  }
  return out;
}

class SstReader {
  final List<Uint8List> blocks;
  int block = 0, offset = 0;
  SstReader(this.blocks);
  int byte() {
    while (block < blocks.length && offset >= blocks[block].length) {
      block++;
      offset = 0;
    }
    if (block >= blocks.length)
      throw const FormatException('Truncated Excel shared strings.');
    return blocks[block][offset++];
  }

  int short() => byte() | byte() << 8;
  int integer() => short() | short() << 16;
  String string() {
    final count = short(), flags = byte();
    final runs = flags & 8 != 0 ? short() : 0,
        extended = flags & 4 != 0 ? integer() : 0;
    var wide = flags & 1 != 0;
    final chars = <int>[];
    for (var i = 0; i < count; i++) {
      if (offset == blocks[block].length) {
        block++;
        offset = 0;
        wide = byte() & 1 != 0;
      }
      chars.add(wide ? short() : byte());
    }
    for (var i = 0; i < runs * 4 + extended; i++) {
      byte();
    }
    return String.fromCharCodes(chars);
  }
}

void readBiff(OfficeFile file, Uint8List bytes) {
  final records = biffRecords(bytes), shared = <String>[];
  if (records.any((r) => r.type == 0x2f))
    throw const FormatException('Encrypted Excel files are not supported.');
  if (records.isEmpty ||
      records.first.type != 0x809 ||
      u16(records.first.data, 0) != 0x600)
    throw const FormatException(
        'Only Excel 97–2003 BIFF8 workbooks are supported.');
  for (var i = 0; i < records.length; i++) {
    if (records[i].type != 0xfc) continue;
    final blocks = [records[i].data];
    while (i + 1 < records.length && records[i + 1].type == 0x3c) {
      blocks.add(records[++i].data);
    }
    final reader = SstReader(blocks);
    reader.integer();
    final count = reader.integer();
    if (count > 250000) throw const FormatException('Too many shared strings.');
    for (var j = 0; j < count; j++) {
      shared.add(reader.string());
    }
  }
  final sheets = <Map<String, dynamic>>[];
  for (final bound in records.where((r) => r.type == 0x85)) {
    final data = bound.data;
    if (data.length < 8 || data[5] != 0) continue;
    final offset = u32(data, 0), len = data[6], wide = data[7] & 1 != 0;
    final nameBytes = data.sublist(8, 8 + len * (wide ? 2 : 1));
    final cells = <String, String>{};
    var rows = 100, cols = 26;
    for (final r in records.where((r) => r.offset >= offset)) {
      if (r.type == 0x0a) break;
      final b = r.data;
      void cell(int col, String value) {
        final row = u16(b, 0) + 1;
        if (row > 10000 || col >= 256 || cells.length >= 250000)
          throw const FormatException(
              'Legacy workbook exceeds content limits.');
        cells['${columnName(col)}$row'] = value;
        if (row > rows) rows = row;
        if (col + 1 > cols) cols = col + 1;
      }

      if (b.length < 6) continue;
      final col = u16(b, 2);
      switch (r.type) {
        case 0xfd:
          cell(col, shared[u32(b, 6)]);
        case 0x203:
          cell(col,
              ByteData.sublistView(b).getFloat64(6, Endian.little).toString());
        case 0x27e:
          cell(col, rkValue(u32(b, 6)).toString());
        case 0xbd:
          for (var p = 4, c = col; p + 6 <= b.length - 2; p += 6, c++) {
            cell(c, rkValue(u32(b, p + 2)).toString());
          }
        case 0x205:
          cell(col, b[7] == 0 ? (b[6] == 0 ? 'FALSE' : 'TRUE') : '#ERROR!');
        case 0x6:
          if (b.length < 14) break;
          if (u16(b, 12) != 0xffff) {
            cell(
                col,
                ByteData.sublistView(b)
                    .getFloat64(6, Endian.little)
                    .toString());
          } else if (b[6] == 1) {
            cell(col, b[8] == 0 ? 'FALSE' : 'TRUE');
          } else if (b[6] == 0) {
            throw const FormatException(
                'This legacy workbook contains string formulas. Save it as XLSX before importing to retain its data.');
          }
      }
    }
    sheets.add({
      'name': wide ? utf16(nameBytes) : latin1.decode(nameBytes),
      'cells': cells,
      'rows': rows,
      'columns': cols
    });
  }
  if (sheets.isEmpty) throw const FormatException('No readable worksheets.');
  file.data['worksheets'] = sheets;
  file.data['activeSheet'] = 0;
  file.data['sheet'] = sheets.first;
}

double rkValue(int value) {
  double result;
  if (value & 2 != 0) {
    result = (value.toSigned(32) >> 2).toDouble();
  } else {
    final bytes = ByteData(8)..setUint32(4, value & 0xfffffffc, Endian.little);
    result = bytes.getFloat64(0, Endian.little);
  }
  return value & 1 != 0 ? result / 100 : result;
}

/// Basic legacy slide text. Layout objects and revision-history fidelity need conversion.
void readPpt(OfficeFile file, Uint8List bytes) {
  List<List<String>> slides = [];
  void walk(int start, int end, int depth) {
    if (depth > 30)
      throw const FormatException('PowerPoint nesting limit exceeded.');
    for (var p = start; p + 8 <= end;) {
      final flags = u16(bytes, p),
          type = u16(bytes, p + 2),
          size = u32(bytes, p + 4),
          from = p + 8,
          to = p + 8 + size;
      if (to > end) throw const FormatException('Truncated PowerPoint record.');
      if (type == 0xff0 && flags >> 4 == 0) {
        final next = <List<String>>[];
        List<String>? current;
        for (var q = from; q + 8 <= to;) {
          final t = u16(bytes, q + 2),
              len = u32(bytes, q + 4),
              a = q + 8,
              b = q + 8 + len;
          if (b > to) throw const FormatException('Invalid slide text record.');
          if (t == 0x3f3) {
            current = [];
            next.add(current);
          }
          if (current != null && (t == 4000 || t == 4008)) {
            current.add((t == 4000
                    ? utf16(bytes.sublist(a, b))
                    : latin1.decode(bytes.sublist(a, b)))
                .replaceAll('\r', '\n'));
          }
          q = b;
        }
        if (next.isNotEmpty) slides = next;
      } else if (flags & 0xf == 0xf && type != 0x3f8 && type != 0x3f0) {
        walk(from, to, depth + 1);
      }
      p = to;
    }
  }

  walk(0, bytes.length, 0);
  if (slides.isEmpty || slides.every((s) => s.isEmpty))
    throw const FormatException(
        'This PPT stores text in drawing objects. Save it as PPTX before importing.');
  file.data['slides'] = [
    for (var i = 0; i < slides.length; i++)
      {
        'id': 'legacy-$i',
        'title': slides[i].firstOrNull ?? 'Slide ${i + 1}',
        'body': slides[i].skip(1).join('\n'),
        'background': '#ffffff'
      }
  ];
}
